using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Calculates scene flow (3D velocity vectors) for point cloud points based on BVH bone motion.
///
/// Algorithm:
/// 1. Pre-calculate 100 uniformly distributed segment points on each bone
/// 2. For each frame, compute the movement vector of each segment point
/// 3. For each point cloud point, find the nearest segment point and assign its motion vector as scene flow
/// 4. Accumulate vectors over multiple frames for cumulative scene flow
/// </summary>
public class SceneFlowCalculator : MonoBehaviour
{
    /// <summary>
    /// Represents a point on a bone with its motion vector across frames.
    /// Uses linked-list structure to maintain history chain backward through frames.
    /// </summary>
    [System.Serializable]
    public class BoneSegmentPoint
    {
        /// <summary>Index of the bone this segment belongs to</summary>
        public int boneIndex;

        /// <summary>Index within the bone (0-99)</summary>
        public int segmentIndex;

        /// <summary>Frame number this segment point belongs to</summary>
        public int frameIndex;

        /// <summary>Current frame position in world space</summary>
        public Vector3 position;

        /// <summary>Reference to the previous frame's segment point (linked-list structure)</summary>
        public BoneSegmentPoint previousPoint;

        /// <summary>Motion vector (current position - previous position)</summary>
        public Vector3 motionVector;

        /// <summary>
        /// Constructor for BoneSegmentPoint
        /// </summary>
        public BoneSegmentPoint(int boneIdx, int segIdx)
        {
            boneIndex = boneIdx;
            segmentIndex = segIdx;
            frameIndex = 0;
            position = Vector3.zero;
            previousPoint = null;
            motionVector = Vector3.zero;
        }
    }

    /// <summary>
    /// Stores scene flow information for a single point cloud point
    /// </summary>
    [System.Serializable]
    public class PointSceneFlow
    {
        /// <summary>Point cloud position</summary>
        public Vector3 position;

        /// <summary>Index of nearest segment point</summary>
        public int nearestSegmentIndex;

        /// <summary>Distance to nearest segment</summary>
        public float distanceToSegment;

        /// <summary>Motion vector from nearest segment (this frame)</summary>
        public Vector3 currentMotionVector;

        /// <summary>Accumulated motion over period</summary>
        public Vector3 cumulativeMotionVector;

        /// <summary>
        /// Constructor for PointSceneFlow
        /// </summary>
        public PointSceneFlow()
        {
            position = Vector3.zero;
            nearestSegmentIndex = -1;
            distanceToSegment = float.MaxValue;
            currentMotionVector = Vector3.zero;
            cumulativeMotionVector = Vector3.zero;
        }
    }

    /// <summary>
    /// Represents a historical frame entry containing BVH frame number and segment points
    /// </summary>
    public struct FrameHistoryEntry
    {
        /// <summary>BVH frame number for this history entry</summary>
        public int bvhFrameNumber;

        /// <summary>Timeline time corresponding to this BVH frame</summary>
        public float timelineTime;

        /// <summary>Bone segment points for this frame (boneIndex -> array of 100 segments)</summary>
        public BoneSegmentPoint[][] segments;
    }


    [Header("Configuration")]

    /// <summary>Number of uniformly distributed points per bone (default: 100)</summary>
    [SerializeField]
    [Tooltip("Number of segment points to generate per bone")]
    private int segmentsPerBone = 100;

    /// <summary>Enable debug logging and visualization</summary>
    [SerializeField]
    [Tooltip("Enable debug mode for logging and visualization")]
    private bool debugMode = true;

    /// <summary>Maximum history depth for frame chain (default: 100 frames)</summary>
    [SerializeField]
    [Range(0, 1000)]
    [Tooltip("Number of historical frames to maintain in the linked-list chain")]
    private int historyFrameCount = 30;

    /// <summary>Cached BVH data obtained from BvhDataCache</summary>
    private BvhData bvhData;

    /// <summary>Frame mapper for calculating BVH frame from timeline time</summary>
    private readonly BvhPlaybackFrameMapper frameMapper = new();

    /// <summary>BVH joint hierarchy gathered from BvhData (not Scene GameObjects)</summary>
    private List<BvhJoint> bvhJoints = new();

    /// <summary>Cached bone positions for visualization via Gizmos</summary>
    private Vector3[] visualizationBonePositions;


    /// <summary>All bone transforms gathered from BVH hierarchy in depth-first order</summary>
    private readonly List<Transform> boneTransforms = new();

    /// <summary>Historical frame entries built during CalculateSceneFlowForCurrentFrame()</summary>
    private readonly List<FrameHistoryEntry> frameHistory = new();

    /// <summary>Scene flow data for all points in current point cloud</summary>
    private readonly List<PointSceneFlow> pointFlows = new();

    // Frame tracking
    private int currentBvhFrameIndex = 0;
    private double currentFrameTime = 0f;

    /// <summary>
    /// Try to auto-initialize BVH data from BvhDataCache (centralized data source)
    /// This is clean and independent from Timeline
    /// </summary>
    private void TryAutoInitializeBvhData()
    {
        if (bvhData != null)
            return;  // Already initialized

        // Get BVH data from BvhDataCache cache (initialized by MultiCameraPointCloudManager)
        bvhData = BvhDataCache.GetBvhData();

        if (bvhData != null)
        {
            if (debugMode)
                Debug.Log("[SceneFlowCalculator] BvhData loaded from BvhDataCache cache");
            return;
        }

        // If not available, clear error message
        Debug.LogError(
            "[SceneFlowCalculator] BvhData not initialized.\n" +
            "MultiCameraPointCloudManager must be in scene to initialize BvhDataCache.\n" +
            "Or manually call: sceneFlowCalculator.SetBvhData(bvhData);");
    }

    /// <summary>
    /// Set the BVH data source for this calculator.
    /// Can be called by components that have access to BvhData.
    /// </summary>
    public void SetBvhData(BvhData data)
    {
        bvhData = data;

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] BvhData set: {(data != null ? "Valid" : "Null")}");
    }

    /// <summary>
    /// Show scene flow for the current BVH frame.
    /// Called when "Show Scene Flow" button is pressed in the Inspector.
    /// Tests root node position calculation using two methods.
    /// Applies same position offset and drift correction as BVH_Visuals for alignment.
    /// </summary>
    public void OnShowSceneFlow()
    {
        // Step 1: Validate BVH data
        if (bvhData == null)
        {
            TryAutoInitializeBvhData();
            if (bvhData == null)
            {
                Debug.LogError("[SceneFlowCalculator] BvhData not available. Ensure MultiCameraPointCloudManager is in scene.");
                return;
            }
        }

        // Step 2: Get BVH hierarchy
        GatherBoneHierarchyFromBvhData();
        if (bvhJoints.Count == 0)
        {
            Debug.LogError("[SceneFlowCalculator] No bones loaded from BVH data");
            return;
        }

        Debug.Log("[OnShowSceneFlow] === Root Node Info ===");

        // Step 3: Get root joint
        BvhJoint rootJoint = bvhData.RootJoint;
        if (rootJoint == null)
        {
            Debug.LogError("[OnShowSceneFlow] Root joint is null");
            return;
        }

        // Log root joint information
        Debug.Log($"[OnShowSceneFlow] Root joint name: {rootJoint.Name}");
        Debug.Log($"[OnShowSceneFlow] Root joint offset: {rootJoint.Offset}");
        Debug.Log($"[OnShowSceneFlow] Root joint channels: {string.Join(", ", rootJoint.Channels)}");

        // Get BVH frame index from Timeline time using BvhPlaybackFrameMapper
        int frameIndex = GetBvhFrameIndexFromTimelineTime();
        Debug.Log($"[OnShowSceneFlow] Calculated BVH frame index from Timeline time: {frameIndex}");

        // Log frame data
        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData != null)
        {
            Debug.Log($"[OnShowSceneFlow] Frame {frameIndex} data length: {frameData.Length}");
            Debug.Log($"[OnShowSceneFlow] Frame {frameIndex} values: {string.Join(", ", frameData.Take(10).Select(f => f.ToString("F2")))}");
        }

        Debug.Log("[OnShowSceneFlow] === Testing frame " + frameIndex + " ===");

        // Step 4: Get base position offset and BVH scale
        Vector3 positionOffset = GetBvhPositionOffset();
        Vector3 rotationOffset = GetBvhRotationOffset();
        Vector3 bvhScale = GetBvhScale();
        Debug.Log($"[SceneFlowCalculator] Base position offset: {positionOffset}");
        Debug.Log($"[SceneFlowCalculator] BVH scale: {bvhScale}");

        // Step 5: Get drift correction for current frame time

        BvhPlaybackCorrectionKeyframes driftCorrectionData = GetDriftCorrectionData();
        currentFrameTime = TimelineUtil.GetCurrentTimelineTime();
        Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(currentFrameTime, driftCorrectionData, positionOffset);
        Quaternion correctedRot = BvhPlaybackTransformCorrector.GetCorrectedRootRotation(currentFrameTime, driftCorrectionData, rotationOffset);
        transform.SetLocalPositionAndRotation(correctedPos, correctedRot);
    
        Debug.Log($"[SceneFlowCalculator] Drift-corrected position for frame time {currentFrameTime}: {correctedPos}");

        // Step 6: Calculate root position using Method A (mathematical calculation with scale)
        Vector3 rootPosMath = CalculateRootPositionMath(rootJoint, frameIndex, bvhScale);
        Debug.Log($"[SceneFlowCalculator] Method A (Math - with scale): {rootPosMath}");

        // Step 7: Calculate root position using Method B (GameObject Transform with scale)
        Vector3 rootPosGameObject = CalculateRootPositionGameObject(frameIndex, bvhScale);
        Debug.Log($"[SceneFlowCalculator] Method B (GameObject - with scale): {rootPosGameObject}");

        // Step 8: Compare results (BVH frame data only, before adding offset)
        float difference = Vector3.Distance(rootPosMath, rootPosGameObject);
        Debug.Log($"[SceneFlowCalculator] Position difference (BVH frame data): {difference:F6}");

        if (difference < 0.001f)
        {
            Debug.Log("[SceneFlowCalculator] ✓ PASS: Both methods produce identical results!");
        }
        else
        {
            Debug.LogWarning($"[SceneFlowCalculator] ⚠ FAIL: Methods differ by {difference:F6}m");
        }


        // Step 11: Prepare visualization data (only root node at local origin, since we moved the parent)
        visualizationBonePositions = new Vector3[1];
        visualizationBonePositions[0] = Vector3.zero;  // Root is at origin since we moved the container


        Debug.Log("[SceneFlowCalculator] Visualization enabled. Root node should appear in Scene view at same position as BVH_Visuals.");
    }

    // /// <summary>
    // /// Get drift-corrected position for current frame time
    // /// Uses BvhPlaybackCorrectionKeyframes directly based on currentFrameTime
    // /// </summary>
    // private Vector3 GetDriftCorrectedPositionForCurrentFrame(float frameTime, Vector3 basePositionOffset)
    // {
    //     // Get drift correction data
    //     BvhPlaybackCorrectionKeyframes driftCorrectionData = GetDriftCorrectionData();
    //             // Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(timelineTime, driftCorrectionData, positionOffset);
    //     // Quaternion correctedRot = BvhPlaybackTransformCorrector.GetCorrectedRootRotation(timelineTime, driftCorrectionData, rotationOffset);
    //     // Step 5: Get drift correction for current frame time
    //     if (driftCorrectionData == null)
    //     {
    //         Debug.Log("[SceneFlowCalculator] No drift correction data, using base offset only");
    //         return basePositionOffset;
    //     }

    //     // Apply drift correction using BvhPlaybackTransformCorrector
    //     Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(frameTime, driftCorrectionData, basePositionOffset);

    //     Debug.Log($"[SceneFlowCalculator] Drift correction: {basePositionOffset} -> {correctedPos}");

    //     return correctedPos;
    // }


    /// <summary>
    /// Get BVH position offset from DatasetConfig
    /// </summary>
    private Vector3 GetBvhPositionOffset()
    {
        DatasetConfig config = DatasetConfig.GetInstance();
        if (config != null)
        {
            return config.BvhPositionOffset;
        }

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhPositionOffset from config, using zero");
        return Vector3.zero;
    }


    /// <summary>
    /// Get BVH rotation offset from DatasetConfig
    /// </summary>
    private Vector3 GetBvhRotationOffset()
    {
        DatasetConfig config = DatasetConfig.GetInstance();
        if (config != null)
        {
            return config.BvhRotationOffset;
        }

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhRotationOffset from config, using zero");
        return Vector3.zero;
    }


    /// <summary>
    /// Get BVH scale from DatasetConfig
    /// </summary>
    private Vector3 GetBvhScale()
    {
        DatasetConfig config = DatasetConfig.GetInstance();
        if (config != null)
        {
            return config.BvhScale;
        }

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhScale from config, using identity");
        return Vector3.one;
    }


    /// <summary>
    /// Get BvhPlaybackCorrectionKeyframes from Timeline asset
    /// </summary>
    private BvhPlaybackCorrectionKeyframes GetDriftCorrectionData()
    {
        // Try to find from Timeline first (for active playback)
        var timelineController = FindFirstObjectByType<TimelineController>();
        if (timelineController != null)
        {
            var director = timelineController.GetComponent<PlayableDirector>();
            if (director != null && director.playableAsset is TimelineAsset timelineAsset)
            {
                foreach (var track in timelineAsset.GetOutputTracks())
                {
                    foreach (var clip in track.GetClips())
                    {
                        if (clip.asset is BvhPlayableAsset bvhAsset)
                        {
                            var driftData = bvhAsset.GetDriftCorrectionData();
                            if (driftData != null)
                            {
                                return driftData;
                            }
                        }
                    }
                }
            }
        }

        // Fallback: Get from DatasetConfig directly
        var config = DatasetConfig.GetInstance();
        if (config != null)
        {
            return config.BvhPlaybackCorrectionKeyframes;
        }

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhPlaybackCorrectionKeyframes from Timeline or DatasetConfig");
        return null;
    }

    /// <summary>
    /// Get BVH frame index from current Timeline time using BvhPlaybackFrameMapper.
    /// If Timeline is not available or stopped, falls back to frame 0.
    /// </summary>
    /// <returns>BVH frame index calculated from Timeline time</returns>
    private int GetBvhFrameIndexFromTimelineTime()
    {
        float timelineTime = (float)TimelineUtil.GetCurrentTimelineTime();

        // Get drift correction data
        BvhPlaybackCorrectionKeyframes driftCorrectionData = GetDriftCorrectionData();

        // Get frame offset from config if available
        int frameOffset = 0;
        var config = DatasetConfig.GetInstance();
        if (config != null)
        {
            frameOffset = config.BvhFrameOffset;
        }

        // Use BvhPlaybackFrameMapper to calculate frame index from timeline time
        int frameIndex = frameMapper.GetTargetFrameForTime(timelineTime, bvhData, driftCorrectionData, frameOffset);
        Debug.Log($"[OnShowSceneFlow] Calculated frame index from Timeline time {timelineTime}s: {frameIndex}");
        // SetFrameInfo(frameIndex, timelineTime);
        return frameIndex;
    }

    /// <summary>
    /// Calculate root node position using mathematical method (Method A)
    /// Applies BVH scale to frame data positions
    /// </summary>
    private Vector3 CalculateRootPositionMath(BvhJoint rootJoint, int frameIndex, Vector3 bvhScale)
    {
        if (rootJoint == null)
            return Vector3.zero;

        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData == null)
            return Vector3.zero;

        // Debug: Log root joint info
        Debug.Log("[CalculateRootPositionMath] Root Joint Info:");
        Debug.Log($"  Name: {rootJoint.Name}");
        Debug.Log($"  Offset: {rootJoint.Offset}");
        Debug.Log($"  Channels: {string.Join(", ", rootJoint.Channels)}");
        Debug.Log($"  Channel count: {rootJoint.Channels.Count}");

        // Debug: Log frame data
        Debug.Log($"[CalculateRootPositionMath] Frame {frameIndex} data (first 20 values):");
        for (int i = 0; i < Mathf.Min(20, frameData.Length); i++)
        {
            Debug.Log($"  frameData[{i}] = {frameData[i]}");
        }

        Vector3 localPos = Vector3.zero;
        Vector3 localRot = Vector3.zero;

        // Read channel data for root joint (frame data only, no offset)
        int channelIndex = 0;
        BvhDataReader.ReadChannelData(rootJoint.Channels, frameData, ref channelIndex, ref localPos, ref localRot);

        Debug.Log($"[CalculateRootPositionMath] After reading channels (before scale):");
        Debug.Log($"  localPos (from frame data): {localPos}");
        Debug.Log($"  localRot: {localRot}");

        // Apply BVH scale to position (same as BvhPlayableBehaviour does)
        Vector3 scaledPos = Vector3.Scale(localPos, bvhScale);

        Debug.Log($"[CalculateRootPositionMath] Final calculation:");
        Debug.Log($"  bvhScale: {bvhScale}");
        Debug.Log($"  scaledPos: {scaledPos}");

        return scaledPos;
    }

    /// <summary>
    /// Calculate root node position using GameObject method (Method B)
    /// Applies BVH scale via PlayableMotionApplier
    /// </summary>
    private Vector3 CalculateRootPositionGameObject(int frameIndex, Vector3 bvhScale)
    {
        // Create temporary skeleton with scale applied
        Transform tempSkeleton = CreateTemporaryBvhSkeletonWithScale(frameIndex, bvhScale);
        if (tempSkeleton == null)
        {
            Debug.LogError("[SceneFlowCalculator] Failed to create temporary skeleton");
            return Vector3.zero;
        }

        Debug.Log($"[CalculateRootPositionGameObject] Created temp skeleton: {tempSkeleton.name}");
        Debug.Log($"[CalculateRootPositionGameObject] After applying frame data with scale - position: {tempSkeleton.position}");

        // Get root position from temporary skeleton
        Vector3 rootPos = tempSkeleton.position;

        // Clean up
        GameObject.Destroy(tempSkeleton.gameObject);

        return rootPos;
    }


    /// <summary>
    /// Gather BVH joint hierarchy directly from BvhData (not from Scene GameObjects)
    /// Gets all joints in depth-first order from the BVH data structure
    /// </summary>
    private void GatherBoneHierarchyFromBvhData()
    {
        if (bvhData == null)
            return;

        // Get all joints from BVH data in depth-first order
        bvhJoints = bvhData.GetAllJoints();

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Loaded {bvhJoints.Count} bones from BVH data");
    }



    /// <summary>
    /// Create temporary BVH skeleton with frame data applied and scale transform
    /// Uses PlayableMotionApplier to apply scale same as BvhPlayableBehaviour
    /// </summary>
    private Transform CreateTemporaryBvhSkeletonWithScale(int frameIndex, Vector3 bvhScale)
    {
        // Create root GameObject
        GameObject tempSkeletonGO = new GameObject($"TempBvhSkeleton_{frameIndex}");
        Transform tempSkeletonRoot = tempSkeletonGO.transform;

        if (bvhData == null || bvhData.RootJoint == null)
        {
            Debug.LogError("[SceneFlowCalculator] Cannot create temp skeleton - BvhData is null");
            GameObject.Destroy(tempSkeletonGO);
            return null;
        }

        // Recursively create bone hierarchy
        CreateBoneHierarchy(bvhData.RootJoint, tempSkeletonRoot);

        // Apply frame data with scale using BvhData
        bvhData.SetRootTransform(tempSkeletonRoot.Find(bvhData.RootJoint.Name));
        bvhData.UpdateTransforms(frameIndex, bvhScale, DatasetConfig.GetInstance().BvhRotationOffset, DatasetConfig.GetInstance().BvhPositionOffset);

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Created temporary skeleton with scale {bvhScale}");

        return tempSkeletonRoot;
    }

    /// <summary>
    /// Recursively create GameObject hierarchy for BVH bones
    /// </summary>
    private void CreateBoneHierarchy(BvhJoint joint, Transform parentTransform)
    {
        if (joint == null)
            return;

        // Create GameObject for this joint
        GameObject jointGO = new GameObject(joint.Name);
        Transform jointTransform = jointGO.transform;
        jointTransform.SetParent(parentTransform, false);
        jointTransform.localPosition = joint.Offset;
        jointTransform.localRotation = Quaternion.identity;

        // Recursively create children
        foreach (BvhJoint child in joint.Children)
        {
            CreateBoneHierarchy(child, jointTransform);
        }
    }


    /// <summary>
    /// Build frame history by backtracking from current BVH frame through historyFrameCount frames.
    /// For each frame, calculates segment points and links them via previousPoint references.
    /// </summary>
    /// <param name="currentBvhFrame">Current BVH frame number to start backtracking from</param>
    private void BuildFrameHistoryBacktrack(int currentBvhFrame)
    {
        frameHistory.Clear();

        // Calculate the oldest frame to include in history
        int oldestBvhFrame = Mathf.Max(0, currentBvhFrame - historyFrameCount + 1);

        // Build segment points for each frame in history range
        for (int bvhFrame = oldestBvhFrame; bvhFrame <= currentBvhFrame; bvhFrame++)
        {
            // Create bone segment array for this frame
            var segmentsForFrame = new BoneSegmentPoint[boneTransforms.Count][];

            // Generate 100 segment points for each bone
            for (int boneIdx = 0; boneIdx < boneTransforms.Count; boneIdx++)
            {
                var segments = new BoneSegmentPoint[segmentsPerBone];
                for (int segIdx = 0; segIdx < segmentsPerBone; segIdx++)
                {
                    segments[segIdx] = new BoneSegmentPoint(boneIdx, segIdx);
                    segments[segIdx].frameIndex = bvhFrame;
                }
                segmentsForFrame[boneIdx] = segments;
            }

            // Calculate segment positions for this BVH frame
            // Apply BVH frame data to bone transforms
            ApplyBvhFrame(bvhFrame);

            // Update segment positions from current bone transform positions
            for (int boneIdx = 0; boneIdx < boneTransforms.Count; boneIdx++)
            {
                UpdateSegmentPositions(boneIdx, segmentsForFrame[boneIdx]);
            }

            // Create history entry
            var entry = new FrameHistoryEntry
            {
                bvhFrameNumber = bvhFrame,
                timelineTime = 0f,  // Will be calculated if needed
                segments = segmentsForFrame
            };
            frameHistory.Add(entry);
        }

        // Link segments across frames via previousPoint references
        LinkSegmentHistory();

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Frame history built: {frameHistory.Count} frames ({oldestBvhFrame} to {currentBvhFrame})");
    }

    /// <summary>
    /// Apply BVH frame data to bone transforms to prepare for segment point calculation.
    /// Delegates to BvhData.UpdateTransforms() for proper BVH handling.
    /// </summary>
    /// <param name="bvhFrame">BVH frame number to apply</param>
    private void ApplyBvhFrame(int bvhFrame)
    {
        if (bvhData == null)
            return;

        // Delegate BVH frame application to BvhData
        bvhData.UpdateTransforms(bvhFrame);
    }

    /// <summary>
    /// Update segment positions for a given bone based on current transform positions
    /// </summary>
    /// <param name="boneIndex">Index of the bone</param>
    /// <param name="segments">Segment array to update</param>
    private void UpdateSegmentPositions(int boneIndex, BoneSegmentPoint[] segments)
    {
        Transform boneTransform = boneTransforms[boneIndex];

        // Get bone endpoints in world space
        Vector3 boneStart = boneTransform.parent != null ? boneTransform.parent.position : boneTransform.position;
        Vector3 boneEnd = boneTransform.position;

        // Generate 100 uniformly distributed points along the bone
        for (int segIdx = 0; segIdx < segments.Length; segIdx++)
        {
            float t = segments.Length > 1 ? (float)segIdx / (segments.Length - 1) : 0f;
            segments[segIdx].position = Vector3.Lerp(boneStart, boneEnd, t);
        }
    }

    /// <summary>
    /// Link segment points across frames via previousPoint references
    /// Calculates motion vectors from position differences
    /// </summary>
    private void LinkSegmentHistory()
    {
        for (int frameIdx = 1; frameIdx < frameHistory.Count; frameIdx++)
        {
            var currentEntry = frameHistory[frameIdx];
            var previousEntry = frameHistory[frameIdx - 1];

            // Link segments from each bone
            for (int boneIdx = 0; boneIdx < boneTransforms.Count; boneIdx++)
            {
                var currentSegments = currentEntry.segments[boneIdx];
                var previousSegments = previousEntry.segments[boneIdx];

                for (int segIdx = 0; segIdx < currentSegments.Length; segIdx++)
                {
                    // Link to previous frame's segment
                    currentSegments[segIdx].previousPoint = previousSegments[segIdx];

                    // Calculate motion vector
                    currentSegments[segIdx].motionVector = currentSegments[segIdx].position - previousSegments[segIdx].position;
                }
            }
        }
    }

    /// <summary>
    /// Draw debug visualization of segment points and motion vectors
    /// Call from OnDrawGizmos() or similar
    /// </summary>
    public void DrawDebugVisualization()
    {
        if (frameHistory.Count == 0)
            return;

        var currentFrame = frameHistory[frameHistory.Count - 1];

        // Draw bone structure (joints and connections)
        DrawBoneStructure();

        // Draw segment points for each bone
        for (int boneIdx = 0; boneIdx < currentFrame.segments.Length; boneIdx++)
        {
            var segments = currentFrame.segments[boneIdx];
            var boneColor = GetBoneColor(boneIdx);

            for (int segIdx = 0; segIdx < segments.Length; segIdx++)
            {
                var segment = segments[segIdx];

                // Draw segment point
                Gizmos.color = boneColor;
                Gizmos.DrawSphere(segment.position, 0.01f);

                // Draw motion vector
                if (segment.motionVector.magnitude > 0.001f)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(segment.position, segment.position + segment.motionVector);
                }
            }
        }

        // Draw point flows if calculated
        if (pointFlows.Count > 0)
        {
            for (int pointIdx = 0; pointIdx < pointFlows.Count; pointIdx++)
            {
                var flow = pointFlows[pointIdx];

                // Draw point
                Gizmos.color = Color.white;
                Gizmos.DrawSphere(flow.position, 0.015f);

                // Draw motion vector
                if (flow.currentMotionVector.magnitude > 0.001f)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(flow.position, flow.position + flow.currentMotionVector);
                }

                // Draw cumulative motion vector (if significant)
                if (flow.cumulativeMotionVector.magnitude > 0.001f)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(flow.position, flow.position + flow.cumulativeMotionVector * 0.1f);
                }
            }
        }
    }

    /// <summary>
    /// Draw bone structure: joints as spheres and bones as lines
    /// Helps verify that bone positions are correctly loaded
    /// </summary>
    private void DrawBoneStructure()
    {
        if (boneTransforms.Count == 0)
            return;

        // Draw each bone's endpoints (joint positions)
        for (int boneIdx = 0; boneIdx < boneTransforms.Count; boneIdx++)
        {
            Transform boneTransform = boneTransforms[boneIdx];

            // Draw bone start (parent position)
            Vector3 boneStart = boneTransform.parent != null ? boneTransform.parent.position : boneTransform.position;
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(boneStart, 0.02f);

            // Draw bone end (bone position)
            Vector3 boneEnd = boneTransform.position;
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(boneEnd, 0.02f);

            // Draw line connecting bone start to end
            Gizmos.color = new Color(1.0f, 1.0f, 1.0f, 0.5f);  // Semi-transparent white
            Gizmos.DrawLine(boneStart, boneEnd);
        }
    }

    /// <summary>
    /// Get a distinct color for a bone based on its index
    /// </summary>
    /// <param name="boneIndex">Index of the bone</param>
    /// <returns>Color for visualization</returns>
    private Color GetBoneColor(int boneIndex)
    {
        // Cycle through distinct colors based on bone index
        float hue = (boneIndex * 0.33f) % 1.0f;
        return Color.HSVToRGB(hue, 0.8f, 0.9f);
    }
}
