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
    /// Represents a bone in the BVH skeleton as a parent-child joint pair.
    /// Used for segment position calculation and visualization.
    /// Implemented as a class (reference type) to ensure transform references persist
    /// when updated in LinkBoneDefinitionsToFrame().
    /// </summary>
    private class BoneDefinition
    {
        public int index;
        public BvhJoint parentJoint;
        public BvhJoint childJoint;
        public Transform parentTransform;
        public Transform childTransform;
        public bool isEndSiteChild;  // True if childJoint is an EndSite
    }

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

    /// <summary>Bone definitions for current frame (parent-child joint pairs linked to current BVH transforms)</summary>
    private List<BoneDefinition> currentFrameBones = new();

    /// <summary>Bone definitions for previous frame (parent-child joint pairs linked to previous BVH transforms)</summary>
    private List<BoneDefinition> previousFrameBones = new();

    /// <summary>Template bone definitions from BVH hierarchy (BvhJoint pairs, not Transform references)</summary>
    private List<BoneDefinition> templateBones = new();

    /// <summary>Cached bone positions for visualization via Gizmos</summary>
    private Vector3[] visualizationBonePositions;

    /// <summary>Reference to CurrentFrameBVH container for Gizmo visualization (blue)</summary>
    private Transform currentFrameContainer;

    /// <summary>Reference to PreviousFrameBVH container for Gizmo visualization (yellow)</summary>
    private Transform previousFrameContainer;

    /// <summary>Joint motion data for all joints in current frame for visualization</summary>
    private readonly List<SegmentedBoneMotionData> jointMotionDataList = new();

    /// <summary>All bone transforms gathered from BVH hierarchy in depth-first order</summary>
    private readonly List<Transform> boneTransforms = new();

    /// <summary>Historical frame entries built during CalculateSceneFlowForCurrentFrame()</summary>
    private readonly List<FrameHistoryEntry> frameHistory = new();

    /// <summary>Scene flow data for all points in current point cloud</summary>
    private readonly List<PointSceneFlow> pointFlows = new();

    // Frame tracking
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
    /// Show scene flow for the current BVH frame.
    /// Called when "Show Scene Flow" button is pressed in the Inspector.
    /// Tests root node position calculation using two methods.
    /// Applies same position offset and drift correction as BVH_Visuals for alignment.
    /// </summary>
    public void OnShowSceneFlow()
    {
        // 前回の実行時のコンテナをクリア
        currentFrameContainer = null;
        previousFrameContainer = null;
        currentFrameBones.Clear();
        previousFrameBones.Clear();

        // Validate BVH data
        if (bvhData == null)
        {
            TryAutoInitializeBvhData();
            if (bvhData == null)
            {
                Debug.LogError("[SceneFlowCalculator] BvhData not available. Ensure MultiCameraPointCloudManager is in scene.");
                return;
            }
        }

        // Get BVH hierarchy and validate
        GatherBoneHierarchyFromBvhData();
        if (bvhJoints.Count == 0)
        {
            Debug.LogError("[SceneFlowCalculator] No bones loaded from BVH data");
            return;
        }

        // Get configuration data
        Vector3 positionOffset = GetBvhPositionOffset();
        Vector3 rotationOffset = GetBvhRotationOffset();
        Vector3 bvhScale = GetBvhScale();
        BvhPlaybackCorrectionKeyframes driftCorrectionData = GetDriftCorrectionData();

        // Get current frame index and time
        int currentFrameIndex = GetBvhFrameIndexMapped();
        currentFrameTime = TimelineUtil.GetCurrentTimelineTime();

        // Display current frame (blue)
        DisplayBvhFrame("CurrentFrameBVH", currentFrameIndex, (float)currentFrameTime,
                       positionOffset, rotationOffset, bvhScale, driftCorrectionData, Color.blue);
        currentFrameContainer = transform.Find("CurrentFrameBVH");

        // Gather bone definitions from BVH data (template only)
        GatherBoneDefinitionsFromBvhData();

        // Link bone definitions to current frame transforms
        currentFrameBones = LinkBoneDefinitionsToFrame(currentFrameContainer);

        // Display previous frame (yellow) if available
        if (currentFrameIndex > 0)
        {
            int previousFrameIndex = currentFrameIndex - 1;
            float previousFrameTime = Mathf.Max(0f, (float)currentFrameTime - (1f / bvhData.FrameRate));

            DisplayBvhFrame("PreviousFrameBVH", previousFrameIndex, previousFrameTime,
                           positionOffset, rotationOffset, bvhScale, driftCorrectionData, Color.yellow);
            previousFrameContainer = transform.Find("PreviousFrameBVH");

            // Link bone definitions to previous frame transforms (for segmentation)
            previousFrameBones = LinkBoneDefinitionsToFrame(previousFrameContainer);

            // Calculate motion vectors for visualization
            CalculateJointMotionVectors();
        }
        else
        {
            Debug.Log("[SceneFlowCalculator] No previous frame available (already at frame 0)");
            previousFrameBones.Clear();
        }
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
    /// Display a BVH frame with specified container name, time, and visual color
    /// Handles creation/destruction of container and attachment of BVH skeleton
    /// </summary>
    private void DisplayBvhFrame(string containerName, int frameIndex, float frameTime,
                                 Vector3 positionOffset, Vector3 rotationOffset, Vector3 bvhScale,
                                 BvhPlaybackCorrectionKeyframes driftCorrectionData, Color gizmoColor)
    {
        // Remove existing container if present
        Transform existingContainer = transform.Find(containerName);
        if (existingContainer != null)
        {
            Destroy(existingContainer.gameObject);
            // 参照をnullにして、OnDrawGizmosで破棄前のGameObjectを使わないようにする
            if (containerName == "CurrentFrameBVH")
                currentFrameContainer = null;
            else if (containerName == "PreviousFrameBVH")
                previousFrameContainer = null;
        }

        // Create new container
        GameObject containerGO = new GameObject(containerName);
        Transform container = containerGO.transform;
        container.SetParent(transform, false);

        // Apply drift correction
        Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(frameTime, driftCorrectionData, positionOffset);
        Quaternion correctedRot = BvhPlaybackTransformCorrector.GetCorrectedRootRotation(frameTime, driftCorrectionData, rotationOffset);
        container.localPosition = correctedPos;
        container.localRotation = correctedRot;
        container.localScale = bvhScale;

        // Create BVH skeleton
        Transform skeleton = CreateTemporaryBvhSkeletonWithScale(frameIndex);
        if (skeleton != null)
        {
            skeleton.SetParent(container, false);
            Debug.Log($"[SceneFlowCalculator] {containerName} (frame {frameIndex}) created");
        }
        else
        {
            Debug.LogError($"[SceneFlowCalculator] Failed to create skeleton for {containerName}");
            Destroy(containerGO);
        }
    }

    /// <summary>
    /// Get BVH frame index from current Timeline time using BvhPlaybackFrameMapper.
    /// If Timeline is not available or stopped, falls back to frame 0.
    /// </summary>
    /// <returns>BVH frame index calculated from Timeline time</returns>
    private int GetBvhFrameIndexMapped()
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
    /// Gather bone definitions from BVH data (parent-child joint pairs)
    /// Creates a list of template BoneDefinition objects with BvhJoint references (no Transform refs)
    /// This template is used to create frame-specific bone lists
    /// Includes EndSite children to support complete bone segmentation
    /// </summary>
    private void GatherBoneDefinitionsFromBvhData()
    {
        if (bvhData == null || bvhJoints.Count == 0)
        {
            Debug.LogError("[SceneFlowCalculator] Cannot gather bone definitions - BvhData or bvhJoints is empty");
            templateBones.Clear();
            return;
        }

        templateBones.Clear();
        int boneIndex = 0;

        // Iterate through all joints and create bone definitions for parent-child pairs
        foreach (BvhJoint joint in bvhJoints)
        {
            // Process ALL children including EndSites (removed skip condition)
            // This ensures leaf bones with only EndSite children are included
            foreach (BvhJoint childJoint in joint.Children)
            {
                // Create bone definition for all children, including EndSite nodes
                BoneDefinition boneDef = new BoneDefinition
                {
                    index = boneIndex,
                    parentJoint = joint,
                    childJoint = childJoint,
                    isEndSiteChild = childJoint.IsEndSite  // Mark if this is an EndSite child
                    // parentTransform and childTransform will be set per-frame
                };

                templateBones.Add(boneDef);
                boneIndex++;
            }
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Created {templateBones.Count} template bone definitions from BVH hierarchy (including EndSite children)");
    }

    /// <summary>
    /// Find a joint transform by name in the skeleton hierarchy
    /// </summary>
    private Transform FindJointTransformByName(Transform root, string jointName)
    {
        if (root == null)
            return null;

        if (root.name == jointName)
            return root;

        foreach (Transform child in root)
        {
            Transform found = FindJointTransformByName(child, jointName);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Calculate segment positions for a single bone using linear interpolation
    /// </summary>
    /// <param name="bones">Bone definitions list for the frame</param>
    /// <param name="boneIndex">Index of the bone to calculate segments for</param>
    /// <returns>Array of SegmentedBoneMotionData representing positions along the bone</returns>
    private SegmentedBoneMotionData[] CalculateSegmentPositionsForBone(List<BoneDefinition> bones, int boneIndex)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
        {
            Debug.LogError($"[SceneFlowCalculator] Invalid bone index: {boneIndex}");
            return new SegmentedBoneMotionData[0];
        }

        BoneDefinition boneDef = bones[boneIndex];

        if (boneDef.parentTransform == null)
        {
            Debug.LogWarning($"[SceneFlowCalculator] CalculateSegmentPositionsForBone: Parent transform is null for bone {boneIndex}");
            return new SegmentedBoneMotionData[0];
        }

        // Get parent position in world space
        Vector3 parentPos = boneDef.parentTransform.position;

        // Calculate child position
        // For EndSite children, childTransform is null, so we calculate the virtual position
        Vector3 childPos;
        if (boneDef.isEndSiteChild && boneDef.childTransform == null)
        {
            // EndSite: calculate position from parent + offset
            // Use TransformDirection to apply parent's rotation to the offset
            childPos = boneDef.parentTransform.position + boneDef.parentTransform.TransformDirection(boneDef.childJoint.Offset);

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] EndSite child: {boneDef.childJoint.Name}, calculated pos: {childPos}");
        }
        else if (boneDef.childTransform != null)
        {
            // Regular bone: use actual transform position
            childPos = boneDef.childTransform.position;
        }
        else
        {
            Debug.LogWarning($"[SceneFlowCalculator] CalculateSegmentPositionsForBone: Child transform not available for bone {boneIndex} (not EndSite)");
            return new SegmentedBoneMotionData[0];
        }

        // Create array for segment positions
        SegmentedBoneMotionData[] segments = new SegmentedBoneMotionData[segmentsPerBone];

        // Calculate segment positions using linear interpolation
        for (int i = 0; i < segmentsPerBone; i++)
        {
            // Calculate interpolation parameter (0.0 at parent, 1.0 at child)
            float t = segmentsPerBone > 1 ? (float)i / (segmentsPerBone - 1) : 0f;

            // Interpolate position
            Vector3 segmentPos = Vector3.Lerp(parentPos, childPos, t);

            // Create bone segment data
            SegmentedBoneMotionData segmentData = new SegmentedBoneMotionData(
                boneIndex,
                i,
                segmentPos,
                t,
                $"{boneDef.parentJoint.Name}->{boneDef.childJoint.Name}"
            );

            segments[i] = segmentData;
        }

        return segments;
    }

    /// <summary>
    /// Create temporary BVH skeleton with frame data applied and scale transform
    /// Uses PlayableMotionApplier to apply scale same as BvhPlayableBehaviour
    /// </summary>
    private Transform CreateTemporaryBvhSkeletonWithScale(int frameIndex)
    {
        if (bvhData == null || bvhData.RootJoint == null)
        {
            Debug.LogError("[SceneFlowCalculator] Cannot create temp skeleton - BvhData is null");
            return null;
        }

        // Create root GameObject as container (this will hold the BVH root joint)
        GameObject tempSkeletonGO = new GameObject($"TempBvhSkeleton_{frameIndex}");
        Transform tempSkeletonRoot = tempSkeletonGO.transform;

        // Recursively create bone hierarchy (RootJoint becomes a child of tempSkeletonRoot)
        CreateBoneHierarchy(bvhData.RootJoint, tempSkeletonRoot);

        // Get the actual RootJoint transform that was just created
        Transform rootJointTransform = tempSkeletonRoot.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
        {
            Debug.LogError("[SceneFlowCalculator] Failed to find root joint transform after creating hierarchy");
            GameObject.Destroy(tempSkeletonGO);
            return null;
        }

        // Apply frame data directly without modifying bvhData state
        bvhData.ApplyFrameToTransforms(frameIndex, rootJointTransform);

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

    /// <summary>
    /// Draw skeleton visualization in Scene view using Gizmos
    /// - Current frame (blue)
    /// - Previous frame (yellow)
    /// - Motion vectors (red arrows)
    /// </summary>
    private void OnDrawGizmos()
    {
        // Draw current frame BVH (blue)
        // 参照がnullまたは破棄されていないかチェック
        if (currentFrameContainer != null && currentFrameContainer.gameObject != null)
        {
            DrawBvhContainerStructure(currentFrameContainer, Color.blue, 0.02f);
        }

        // Draw previous frame BVH (yellow)
        // 参照がnullまたは破棄されていないかチェック
        if (previousFrameContainer != null && previousFrameContainer.gameObject != null)
        {
            DrawBvhContainerStructure(previousFrameContainer, Color.yellow, 0.015f);
        }

        // Draw bone segment positions (white/gray spheres)
        DrawBoneSegmentPositions();

        // Draw bone segment motion vectors (blue→red arrows)
        DrawBoneSegmentMotionVectors();

        // Draw joint motion vectors (red arrows)
        DrawMotionVectors();
    }

    /// <summary>
    /// Draw bone segment positions as small white spheres in Scene view
    /// Draws segments for both current and previous frames
    /// </summary>
    private void DrawBoneSegmentPositions()
    {
        if (currentFrameBones.Count == 0 && previousFrameBones.Count == 0)
            return;

        const float segmentSphereRadius = 0.003f;

        // Draw segments for current frame (white)
        if (currentFrameBones.Count > 0)
        {
            for (int boneIdx = 0; boneIdx < currentFrameBones.Count; boneIdx++)
            {
                SegmentedBoneMotionData[] segments = CalculateSegmentPositionsForBone(currentFrameBones, boneIdx);

                foreach (SegmentedBoneMotionData segment in segments)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawSphere(segment.position, segmentSphereRadius);
                }
            }

            if (debugMode && currentFrameBones.Count > 0)
            {
                BoneDefinition firstBone = currentFrameBones[0];
                if (firstBone.parentTransform == null || firstBone.childTransform == null)
                {
                    Debug.LogWarning("[SceneFlowCalculator] DrawBoneSegmentPositions: Current frame bone transforms not set.");
                }
            }
        }

        // Draw segments for previous frame (light gray)
        if (previousFrameBones.Count > 0)
        {
            for (int boneIdx = 0; boneIdx < previousFrameBones.Count; boneIdx++)
            {
                SegmentedBoneMotionData[] segments = CalculateSegmentPositionsForBone(previousFrameBones, boneIdx);

                foreach (SegmentedBoneMotionData segment in segments)
                {
                    Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 1f);  // Light gray for previous frame
                    Gizmos.DrawSphere(segment.position, segmentSphereRadius);
                }
            }
        }
    }

    /// <summary>
    /// Calculate motion vectors for all bone segments by comparing current and previous frame positions
    /// </summary>
    /// <returns>List of SegmentedBoneMotionData with motion information</returns>
    private List<SegmentedBoneMotionData> CalculateBoneSegmentMotionVectors()
    {
        var segmentMotionDataList = new List<SegmentedBoneMotionData>();

        if (currentFrameBones.Count == 0 || previousFrameBones.Count == 0)
            return segmentMotionDataList;

        // For each bone, calculate segment positions in both frames and compute motion vectors
        for (int boneIdx = 0; boneIdx < currentFrameBones.Count && boneIdx < previousFrameBones.Count; boneIdx++)
        {
            // Get segments from current frame
            SegmentedBoneMotionData[] currentSegments = CalculateSegmentPositionsForBone(currentFrameBones, boneIdx);

            // Get segments from previous frame
            SegmentedBoneMotionData[] previousSegments = CalculateSegmentPositionsForBone(previousFrameBones, boneIdx);

            // Match segments and compute motion vectors
            for (int segIdx = 0; segIdx < currentSegments.Length && segIdx < previousSegments.Length; segIdx++)
            {
                SegmentedBoneMotionData currentSeg = currentSegments[segIdx];
                SegmentedBoneMotionData previousSeg = previousSegments[segIdx];

                // Create new segment data with motion information
                var segmentWithMotion = new SegmentedBoneMotionData(
                    currentSeg.boneIndex,
                    currentSeg.segmentIndex,
                    currentSeg.position,
                    previousSeg.position,
                    currentSeg.interpolationT,
                    currentSeg.boneName
                );

                segmentMotionDataList.Add(segmentWithMotion);
            }
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Calculated motion vectors for {segmentMotionDataList.Count} bone segments");

        return segmentMotionDataList;
    }

    /// <summary>
    /// Link bone definitions to a specific frame skeleton transforms
    /// Creates a new list of BoneDefinition objects with transforms linked to the given frame
    /// </summary>
    /// <param name="frameContainer">The frame container (CurrentFrameBVH or PreviousFrameBVH)</param>
    /// <returns>New list of BoneDefinition objects with transforms linked to the frame</returns>
    private List<BoneDefinition> LinkBoneDefinitionsToFrame(Transform frameContainer)
    {
        var linkedBones = new List<BoneDefinition>();

        if (frameContainer == null || templateBones.Count == 0)
        {
            if (debugMode)
                Debug.LogWarning($"[SceneFlowCalculator] LinkBoneDefinitionsToFrame: frameContainer={frameContainer}, templateBones.Count={templateBones.Count}");
            return linkedBones;
        }

        // Get the actual BVH root (skip TempBvhSkeleton container)
        if (frameContainer.childCount == 0)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] LinkBoneDefinitionsToFrame: frameContainer has no children");
            return linkedBones;
        }

        Transform tempSkeleton = frameContainer.GetChild(0);
        if (tempSkeleton.childCount == 0)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] LinkBoneDefinitionsToFrame: TempBvhSkeleton has no children");
            return linkedBones;
        }

        Transform bvhRoot = tempSkeleton.GetChild(0);

        // Create new bone definitions with transforms from this frame
        int linkedCount = 0;
        foreach (var templateBone in templateBones)
        {
            // Create new BoneDefinition object for this frame
            var boneDef = new BoneDefinition
            {
                //初期化子でコピー
                index = templateBone.index,
                parentJoint = templateBone.parentJoint,
                childJoint = templateBone.childJoint,
                isEndSiteChild = templateBone.isEndSiteChild
            };

            // Find parent transform
            Transform parentTransform = FindJointTransformByName(bvhRoot, boneDef.parentJoint.Name);
            if (parentTransform != null)
            {
                boneDef.parentTransform = parentTransform;
                linkedCount++;
            }

            // Find child transform
            // For EndSite children, childTransform will remain null (handled in CalculateSegmentPositionsForBone)
            if (!boneDef.isEndSiteChild)
            {
                Transform childTransform = FindJointTransformByName(bvhRoot, boneDef.childJoint.Name);
                if (childTransform != null)
                    boneDef.childTransform = childTransform;
            }
            // else: isEndSiteChild = true, leave childTransform = null

            linkedBones.Add(boneDef);
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Linked {linkedCount}/{templateBones.Count} bone transforms to {frameContainer.name}");

        return linkedBones;
    }

    /// <summary>
    /// Draw bone structure for a BVH container in Scene view
    /// </summary>
    /// <param name="containerTransform">Root transform of the BVH container</param>
    /// <param name="boneColor">Color for drawing</param>
    /// <param name="sphereSize">Radius of joint spheres</param>
    private void DrawBvhContainerStructure(Transform containerTransform, Color boneColor, float sphereSize)
    {
        if (containerTransform == null)
            return;

        // Find the actual BVH root (first child of container, which is the TempBvhSkeleton)
        if (containerTransform.childCount == 0)
            return;

        Transform tempSkeletonRoot = containerTransform.GetChild(0);  // TempBvhSkeleton_XXX

        // Skip the empty TempBvhSkeleton and draw from its children (the actual BVH joints)
        if (tempSkeletonRoot.childCount == 0)
            return;

        // Draw all joint hierarchies under TempBvhSkeleton
        foreach (Transform joint in tempSkeletonRoot)
        {
            DrawBvhJointRecursive(joint, boneColor, sphereSize);
        }
    }

    /// <summary>
    /// Recursively draw joints and bones in the BVH hierarchy
    /// </summary>
    private void DrawBvhJointRecursive(Transform jointTransform, Color boneColor, float sphereSize)
    {
        if (jointTransform == null)
            return;

        // Draw joint as sphere
        Gizmos.color = boneColor;
        Gizmos.DrawSphere(jointTransform.position, sphereSize);

        // Draw bones to children
        foreach (Transform child in jointTransform)
        {
            // Draw line from this joint to child
            Gizmos.color = boneColor;
            Gizmos.DrawLine(jointTransform.position, child.position);

            // Recursively draw child
            DrawBvhJointRecursive(child, boneColor, sphereSize);
        }
    }

    /// <summary>
    /// Calculate motion vectors for all joints by comparing current and previous frame positions
    /// </summary>
    private void CalculateJointMotionVectors()
    {
        jointMotionDataList.Clear();

        if (currentFrameContainer == null || previousFrameContainer == null)
            return;

        // Get root transforms of actual BVH skeletons (skip TempBvhSkeleton containers)
        Transform currentRoot = GetBvhRootTransform(currentFrameContainer);
        Transform previousRoot = GetBvhRootTransform(previousFrameContainer);

        if (currentRoot == null || previousRoot == null)
            return;

        // Recursively calculate motion vectors for all joints
        CalculateJointMotionVectorsRecursive(currentRoot, previousRoot);
    }

    /// <summary>
    /// Recursively calculate motion vectors for joint hierarchy
    /// </summary>
    private void CalculateJointMotionVectorsRecursive(Transform currentJoint, Transform previousJoint)
    {
        if (currentJoint == null || previousJoint == null)
            return;

        // Get world positions
        Vector3 currentPos = currentJoint.position;
        Vector3 previousPos = previousJoint.position;

        // Create motion data (bone index = -1 for joints, segment index as hash of joint name)
        var motionData = new SegmentedBoneMotionData(
            -1,  // boneIndex: -1 indicates this is a joint, not a segment
            currentJoint.name.GetHashCode(),  // segmentIndex: unique ID for joint
            currentPos,  // current position
            previousPos,  // previous position
            0f,  // interpolationT: not applicable for joints
            currentJoint.name
        );
        jointMotionDataList.Add(motionData);

        // Recurse to children
        int childCount = currentJoint.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform currentChild = currentJoint.GetChild(i);
            Transform previousChild = previousJoint.Find(currentChild.name);

            if (previousChild != null)
            {
                CalculateJointMotionVectorsRecursive(currentChild, previousChild);
            }
        }
    }

    /// <summary>
    /// Get the actual BVH root transform from a container (skips TempBvhSkeleton)
    /// </summary>
    private Transform GetBvhRootTransform(Transform container)
    {
        if (container == null || container.childCount == 0)
            return null;

        Transform tempSkeleton = container.GetChild(0);  // TempBvhSkeleton_XXX
        if (tempSkeleton.childCount == 0)
            return null;

        return tempSkeleton.GetChild(0);  // Actual root joint (e.g., Hips)
    }

    /// <summary>
    /// Draw bone segment motion vectors as colored arrows
    /// Color gradient: blue (no motion) → red (high motion)
    /// </summary>
    private void DrawBoneSegmentMotionVectors()
    {
        const float motionThreshold = 0.001f;
        const float arrowHeadSize = 0.002f;

        var segmentMotions = CalculateBoneSegmentMotionVectors();

        if (segmentMotions.Count == 0)
            return;

        // Find max motion magnitude for color gradient
        float maxMagnitude = 0f;
        foreach (var segment in segmentMotions)
        {
            if (segment.motionMagnitude > maxMagnitude)
                maxMagnitude = segment.motionMagnitude;
        }

        if (maxMagnitude < motionThreshold)
            return;  // No significant motion

        // Draw arrows for each segment
        foreach (var segment in segmentMotions)
        {
            if (segment.motionMagnitude > motionThreshold)
            {
                // Calculate color based on motion magnitude (blue → red gradient)
                float colorFactor = segment.motionMagnitude / maxMagnitude;
                Color arrowColor = Color.Lerp(Color.blue, Color.red, colorFactor);

                // Draw line from previous position to current position
                // motionVector = current - previous, so: previous + motionVector = current
                Gizmos.color = arrowColor;
                Vector3 startPoint = segment.previousPosition;  // Start from previous frame position
                Vector3 endPoint = segment.position;  // End at current frame position
                Gizmos.DrawLine(startPoint, endPoint);

                // Draw arrowhead at current position
                Gizmos.DrawSphere(endPoint, arrowHeadSize);
            }
        }
    }

    /// <summary>
    /// Draw motion vectors as red arrows from current frame (blue) pointing toward previous frame (yellow)
    /// Data stored as (current - previous), but visualized in opposite direction for clarity
    /// </summary>
    private void DrawMotionVectors()
    {
        const float motionThreshold = 0.001f;
        const float arrowHeadSize = 0.01f;  // Smaller than yellow skeleton joints (0.015f)

        foreach (var motionData in jointMotionDataList)
        {
            if (motionData.motionMagnitude > motionThreshold)
            {
                // Draw line from current position in opposite direction (visualize: blue → yellow)
                // Data is stored as (current - previous), so negate it for visualization
                Gizmos.color = Color.red;
                Vector3 startPoint = motionData.position;
                Vector3 endPoint = motionData.position - motionData.motionVector;  // Negate for visualization
                Gizmos.DrawLine(startPoint, endPoint);

                // Draw sphere at end point (toward previous position)
                Gizmos.DrawSphere(endPoint, arrowHeadSize);
            }
        }
    }
}
