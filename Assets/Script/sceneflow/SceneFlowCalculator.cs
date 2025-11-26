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

    /// <summary>
    /// Represents BVH bone data with calculated positions (global position calculation)
    /// </summary>
    private class BoneData
    {
        /// <summary>Bone name from BVH hierarchy</summary>
        public string Name;

        /// <summary>Depth in hierarchy (0 = root)</summary>
        public int Depth;

        /// <summary>Reference to the BvhJoint</summary>
        public BvhJoint Joint;

        /// <summary>Offset from parent joint (from BVH file)</summary>
        public Vector3 LocalOffset;

        /// <summary>Local position with animation applied (frame data)</summary>
        public Vector3 LocalPosition;

        /// <summary>Global position in world space (Method A - mathematical calculation)</summary>
        public Vector3 GlobalPositionMath;

        /// <summary>Global position in world space (Method B - GameObject Transform)</summary>
        public Vector3 GlobalPositionGameObject;

        /// <summary>Parent bone name</summary>
        public string ParentName;

        /// <summary>Number of child bones</summary>
        public int ChildCount;

        /// <summary>Difference between two methods</summary>
        public float PositionDifference;

        public BoneData()
        {
            Name = "";
            Depth = 0;
            Joint = null;
            LocalOffset = Vector3.zero;
            LocalPosition = Vector3.zero;
            GlobalPositionMath = Vector3.zero;
            GlobalPositionGameObject = Vector3.zero;
            ParentName = "";
            ChildCount = 0;
            PositionDifference = 0f;
        }
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

    /// <summary>Cached BVH data obtained from BvhPlayableBehaviour on Timeline</summary>
    private BvhData bvhData;

    /// <summary>Cached reference to BvhPlayableBehaviour for Timeline synchronization</summary>
    private BvhPlayableBehaviour bvhPlayableBehaviour;

    /// <summary>Frame mapper for calculating BVH frame from timeline time</summary>
    private BvhFrameMapper frameMapper = new();

    /// <summary>BVH joint hierarchy gathered from BvhData (not Scene GameObjects)</summary>
    private List<BvhJoint> bvhJoints = new();

    /// <summary>Cached bone positions for visualization via Gizmos</summary>
    private Vector3[] visualizationBonePositions;

    /// <summary>Flag to enable bone visualization in OnDrawGizmos</summary>
    private bool shouldVisualizeBones = false;

    /// <summary>All bone transforms gathered from BVH hierarchy in depth-first order</summary>
    private readonly List<Transform> boneTransforms = new();

    /// <summary>Historical frame entries built during CalculateSceneFlowForCurrentFrame()</summary>
    private readonly List<FrameHistoryEntry> frameHistory = new();

    /// <summary>Scene flow data for all points in current point cloud</summary>
    private readonly List<PointSceneFlow> pointFlows = new();

    // Frame tracking
    private int currentBvhFrameIndex = 0;
    private float currentFrameTime = 0f;

    /// <summary>Flag to track if base position offset has been initialized</summary>
    private bool isBasePositionInitialized = false;


    /// <summary>
    /// Initialize base position offset when Play mode starts or component becomes enabled
    /// </summary>
    void Start()
    {
        InitializeBasePositionOffset();
        Debug.Log($"SceneFlowCalculator [After Initialize] transform.position = {transform.position}");
    }

    void Update()
    {
        Debug.Log($"SceneFlowCalculator [Update] transform.position = {transform.position}");
    }

    /// <summary>
    /// Initialize SceneFlow GameObject position with base positionOffset
    /// Called once when Play mode starts
    /// </summary>
    private void InitializeBasePositionOffset()
    {
        if (isBasePositionInitialized)
            return;

        // Try to get position offset from config
        Vector3 positionOffset = GetBvhPositionOffset();

        // Log what we got
        Debug.Log($"[SceneFlowCalculator] OnEnable: GetBvhPositionOffset returned: {positionOffset}");

        // Debug: Check MultiCameraPointCloudManager and DatasetConfig
        var pointCloudManager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (pointCloudManager != null)
        {
            Debug.Log("[SceneFlowCalculator] OnEnable: MultiCameraPointCloudManager found");
            DatasetConfig config = pointCloudManager.GetDatasetConfig();
            if (config != null)
            {
                Debug.Log($"[SceneFlowCalculator] OnEnable: DatasetConfig.BvhPositionOffset = {config.BvhPositionOffset}");
            }
            else
            {
                Debug.LogWarning("[SceneFlowCalculator] OnEnable: DatasetConfig is null in MultiCameraPointCloudManager");
            }
        }
        else
        {
            Debug.LogWarning("[SceneFlowCalculator] OnEnable: MultiCameraPointCloudManager not found in scene");
        }

        // Set position (even if zero, OnShowSceneFlow will update it later)
        transform.localPosition = positionOffset;
        Debug.Log($"[SceneFlowCalculator] OnEnable: Set transform.localPosition to {positionOffset} and {transform.localPosition}");

        isBasePositionInitialized = true;
    }

    /// <summary>
    /// Try to auto-initialize BVH data from BvhDataManager (centralized data source)
    /// This is clean and independent from Timeline
    /// </summary>
    private void TryAutoInitializeBvhData()
    {
        if (bvhData != null)
            return;  // Already initialized

        // Get BVH data from BvhDataManager cache (initialized by MultiCameraPointCloudManager)
        bvhData = BvhDataManager.GetBvhData();

        if (bvhData != null)
        {
            if (debugMode)
                Debug.Log("[SceneFlowCalculator] BvhData loaded from BvhDataManager cache");
            return;
        }

        // If not available, clear error message
        Debug.LogError(
            "[SceneFlowCalculator] BvhData not initialized.\n" +
            "MultiCameraPointCloudManager must be in scene to initialize BvhDataManager.\n" +
            "Or manually call: sceneFlowCalculator.SetBvhData(bvhData);");
    }

    /// <summary>
    /// Set the BVH data source for this calculator.
    /// Called by BvhPlayableBehaviour or other components that have access to BvhData.
    /// </summary>
    public void SetBvhData(BvhData data, BvhPlayableBehaviour behaviour = null)
    {
        bvhData = data;
        bvhPlayableBehaviour = behaviour;

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

        // Get BVH frame index from Timeline time using BvhFrameMapper
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
        Vector3 bvhScale = GetBvhScale();
        Debug.Log($"[SceneFlowCalculator] Base position offset: {positionOffset}");
        Debug.Log($"[SceneFlowCalculator] BVH scale: {bvhScale}");

        // Step 5: Get drift correction for current frame time
        Vector3 driftCorrectedPosition = GetDriftCorrectedPositionForCurrentFrame(currentFrameTime, positionOffset);
        Debug.Log($"[SceneFlowCalculator] Drift-corrected position for frame time {currentFrameTime}: {driftCorrectedPosition}");

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

        // Step 9: Calculate final position = driftCorrectedPosition (offset + correction) + frame data
        // driftCorrectedPosition already includes positionOffset and drift correction
        // rootPosMath is frame data with scale applied
        Vector3 finalPosition = driftCorrectedPosition + rootPosMath;

        Debug.Log($"[SceneFlowCalculator] Final position calculation:");
        Debug.Log($"  driftCorrectedPosition (offset + correction): {driftCorrectedPosition}");
        Debug.Log($"  rootPosMath (frame data with scale): {rootPosMath}");
        Debug.Log($"  finalPosition: {finalPosition}");

        // Step 10: Apply SceneFlow GameObject position
        transform.localPosition = finalPosition;
        Debug.Log($"[SceneFlowCalculator] Applied SceneFlow GameObject localPosition: {transform.localPosition}");

        // Step 11: Prepare visualization data (only root node at local origin, since we moved the parent)
        visualizationBonePositions = new Vector3[1];
        visualizationBonePositions[0] = Vector3.zero;  // Root is at origin since we moved the container

        // Step 12: Enable visualization in OnDrawGizmos
        shouldVisualizeBones = true;

        Debug.Log("[SceneFlowCalculator] Visualization enabled. Root node should appear in Scene view at same position as BVH_Visuals.");
    }

    /// <summary>
    /// Get drift-corrected position for current frame time
    /// Uses BvhDriftCorrectionData directly based on currentFrameTime
    /// </summary>
    private Vector3 GetDriftCorrectedPositionForCurrentFrame(float frameTime, Vector3 basePositionOffset)
    {
        // Get drift correction data
        BvhDriftCorrectionData driftCorrectionData = GetDriftCorrectionData();
        if (driftCorrectionData == null)
        {
            Debug.Log("[SceneFlowCalculator] No drift correction data, using base offset only");
            return basePositionOffset;
        }

        // Apply drift correction using BvhDriftCorrectionController
        var driftController = new BvhDriftCorrectionController();
        Vector3 correctedPos = driftController.GetCorrectedRootPosition(frameTime, driftCorrectionData, basePositionOffset);

        Debug.Log($"[SceneFlowCalculator] Drift correction: {basePositionOffset} -> {correctedPos}");

        return correctedPos;
    }

    /// <summary>
    /// Get current BVH_Character position from the scene
    /// </summary>
    private Vector3 GetCurrentBvhCharacterPosition()
    {
        // Find BVH_Character GameObject in scene
        var bvhCharacter = FindObjectsByType<GameObject>(FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == "BVH_Character");

        if (bvhCharacter != null)
        {
            return bvhCharacter.transform.position;
        }

        // Fallback: return base position offset
        Debug.LogWarning("[SceneFlowCalculator] Could not find BVH_Character, using base offset");
        return GetBvhPositionOffset();
    }

    /// <summary>
    /// Check if Timeline is currently playing
    /// </summary>
    private bool IsTimelinePlayerPlaying()
    {
        // Try to find TimelineController or DirectorComponent
        var timelineController = FindFirstObjectByType<TimelineController>();
        if (timelineController != null)
        {
            // Check if TimelineController is playing
            return timelineController.IsPlaying;
        }

        return false;
    }

    /// <summary>
    /// Get BVH position offset from DatasetConfig
    /// </summary>
    private Vector3 GetBvhPositionOffset()
    {
        var pointCloudManager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (pointCloudManager != null)
        {
            DatasetConfig config = pointCloudManager.GetDatasetConfig();
            if (config != null)
            {
                return config.BvhPositionOffset;
            }
        }

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhPositionOffset from config, using zero");
        return Vector3.zero;
    }

    /// <summary>
    /// Get BVH scale from DatasetConfig
    /// </summary>
    private Vector3 GetBvhScale()
    {
        var pointCloudManager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (pointCloudManager != null)
        {
            DatasetConfig config = pointCloudManager.GetDatasetConfig();
            if (config != null)
            {
                return config.BvhScale;
            }
        }

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhScale from config, using identity");
        return Vector3.one;
    }

    /// <summary>
    /// Get drift-corrected position (same as BvhPlayableBehaviour applies)
    /// Uses BvhDriftCorrectionController with current frame time
    /// </summary>
    private Vector3 GetDriftCorrectedPosition(float timelineTime, Vector3 basePositionOffset)
    {
        if (bvhData == null)
            return basePositionOffset;

        // Get drift correction data from BvhPlayableBehaviour or BvhDataManager
        BvhDriftCorrectionData driftCorrectionData = GetDriftCorrectionData();
        if (driftCorrectionData == null)
        {
            Debug.Log("[SceneFlowCalculator] No drift correction data available, using base offset only");
            return basePositionOffset;
        }

        // Apply drift correction using the same controller as BvhPlayableBehaviour
        var driftController = new BvhDriftCorrectionController();
        Vector3 correctedPos = driftController.GetCorrectedRootPosition(timelineTime, driftCorrectionData, basePositionOffset);

        Debug.Log($"[SceneFlowCalculator] Drift correction applied: {basePositionOffset} -> {correctedPos}");

        return correctedPos;
    }

    /// <summary>
    /// Get BvhDriftCorrectionData from Timeline asset
    /// </summary>
    private BvhDriftCorrectionData GetDriftCorrectionData()
    {
        // Find TimelineController and access its playable director
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

        Debug.LogWarning("[SceneFlowCalculator] Could not find BvhDriftCorrectionData from Timeline");
        return null;
    }

    /// <summary>
    /// Get BVH frame index from current Timeline time using BvhFrameMapper.
    /// If Timeline is not available or stopped, falls back to frame 0.
    /// </summary>
    /// <returns>BVH frame index calculated from Timeline time</returns>
    private int GetBvhFrameIndexFromTimelineTime()
    {
        // Try to get TimelineController and current time
        var timelineController = FindFirstObjectByType<TimelineController>();
        if (timelineController != null)
        {
            var director = timelineController.GetComponent<PlayableDirector>();
            if (director != null)
            {
                float timelineTime = (float)director.time;

                // Get drift correction data
                BvhDriftCorrectionData driftCorrectionData = GetDriftCorrectionData();

                // Get frame offset from config if available
                int frameOffset = 0;
                var pointCloudManager = FindFirstObjectByType<MultiCameraPointCloudManager>();
                if (pointCloudManager != null)
                {
                    var config = pointCloudManager.GetDatasetConfig();
                    if (config != null)
                    {
                        frameOffset = config.BvhFrameOffset;
                    }
                }

                // Use BvhFrameMapper to calculate frame index from timeline time
                int frameIndex = frameMapper.GetTargetFrameForTime(timelineTime, bvhData, driftCorrectionData, frameOffset);
                Debug.Log($"[OnShowSceneFlow] Calculated frame index from Timeline time {timelineTime}s: {frameIndex}");
                return frameIndex;
            }
        }

        // Fallback: use frame 0 if Timeline not available
        Debug.LogWarning("[OnShowSceneFlow] Could not determine Timeline time, using frame 0");
        return 0;
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
        BvhChannelReader.ReadChannelData(rootJoint.Channels, frameData, ref channelIndex, ref localPos, ref localRot);

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
    /// Applies BVH scale via PlayableFrameApplier
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
    /// Calculate global bone positions using mathematical method (Method A)
    /// Recursively traverses BVH hierarchy and accumulates positions
    /// No GameObjects created - pure mathematical calculation
    /// </summary>
    private void CalculateGlobalPositionsMath(BvhJoint joint, int frameIndex, Vector3 parentWorldPos, List<BoneData> result, int depth)
    {
        if (joint == null || joint.IsEndSite)
            return;

        // Get frame data
        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData == null)
            return;

        // Read channel data for this joint
        Vector3 localPos = Vector3.zero;
        Vector3 localRot = Vector3.zero;
        int channelIndex = GetChannelIndexForJoint(joint);

        BvhChannelReader.ReadChannelData(joint.Channels, frameData, ref channelIndex, ref localPos, ref localRot);

        // Calculate local position (offset + animation)
        Vector3 currentLocalPos = joint.Offset + localPos;

        // Calculate world position (accumulate from parent)
        Vector3 worldPos = parentWorldPos + currentLocalPos;

        // Create BoneData entry
        var boneData = new BoneData
        {
            Name = joint.Name,
            Depth = depth,
            Joint = joint,
            LocalOffset = joint.Offset,
            LocalPosition = currentLocalPos,
            GlobalPositionMath = worldPos,
            GlobalPositionGameObject = Vector3.zero,  // Will be filled by Method B
            ParentName = joint.Parent?.Name ?? "null",
            ChildCount = joint.Children.Count,
            PositionDifference = 0f
        };

        result.Add(boneData);

        // Recursively process children
        foreach (var child in joint.Children)
        {
            CalculateGlobalPositionsMath(child, frameIndex, worldPos, result, depth + 1);
        }
    }

    /// <summary>
    /// Get the channel index for a specific joint in the BVH frame data
    /// Traverses from root to target joint, accumulating channel counts
    /// </summary>
    private int GetChannelIndexForJoint(BvhJoint targetJoint)
    {
        int index = 0;
        TraverseForChannelIndex(bvhData.RootJoint, targetJoint, ref index);
        return index;
    }

    /// <summary>
    /// Recursively traverse BVH hierarchy to find channel index for a joint
    /// </summary>
    private bool TraverseForChannelIndex(BvhJoint current, BvhJoint target, ref int channelIndex)
    {
        if (current == target)
            return true;

        int startIndex = channelIndex;
        channelIndex += current.Channels.Count;

        foreach (var child in current.Children)
        {
            if (TraverseForChannelIndex(child, target, ref channelIndex))
                return true;
        }

        // Target not found in this branch, reset index
        channelIndex = startIndex;
        return false;
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
    /// Calculate global bone positions using GameObject method (Method B)
    /// Creates temporary skeleton, applies frame data, reads Transform.position
    /// </summary>
    private void CalculateGlobalPositionsGameObject(int frameIndex, List<BoneData> result)
    {
        // Create temporary skeleton
        Transform tempSkeleton = CreateTemporaryBvhSkeleton(frameIndex);
        if (tempSkeleton == null)
        {
            Debug.LogError("[SceneFlowCalculator] Failed to create temporary skeleton for Method B");
            return;
        }

        // Apply frame data to skeleton
        ApplyFrameDataToSkeletonTransforms(tempSkeleton, frameIndex);

        // Traverse skeleton and read positions
        TraverseSkeletonForPositions(tempSkeleton, bvhData.RootJoint, result, 0);

        // Clean up temporary skeleton
        GameObject.Destroy(tempSkeleton.gameObject);
    }

    /// <summary>
    /// Recursively traverse the temporary skeleton and extract bone positions
    /// </summary>
    private void TraverseSkeletonForPositions(Transform boneTransform, BvhJoint joint, List<BoneData> result, int depth)
    {
        if (joint == null || joint.IsEndSite)
            return;

        // Get world position from Transform
        Vector3 worldPos = boneTransform.position;

        // Find matching BoneData from result (if already created by Method A)
        var matchingBone = result.Find(b => b.Name == joint.Name);
        if (matchingBone != null)
        {
            // Update GameObjectPosition in existing entry
            matchingBone.GlobalPositionGameObject = worldPos;
        }
        else
        {
            // Create new BoneData if not found (shouldn't happen normally)
            var boneData = new BoneData
            {
                Name = joint.Name,
                Depth = depth,
                Joint = joint,
                LocalOffset = joint.Offset,
                LocalPosition = Vector3.zero,
                GlobalPositionMath = Vector3.zero,
                GlobalPositionGameObject = worldPos,
                ParentName = joint.Parent?.Name ?? "null",
                ChildCount = joint.Children.Count,
                PositionDifference = 0f
            };
            result.Add(boneData);
        }

        // Recursively process children
        int childIndex = 0;
        foreach (Transform child in boneTransform)
        {
            if (childIndex < joint.Children.Count)
            {
                TraverseSkeletonForPositions(child, joint.Children[childIndex], result, depth + 1);
                childIndex++;
            }
        }
    }

    /// <summary>
    /// Compare results from Method A and Method B
    /// Calculates differences and logs results
    /// </summary>
    private void ComparePositionResults(List<BoneData> mathResults, List<BoneData> gameObjectResults)
    {
        Debug.Log("[SceneFlowCalculator] === Method Comparison ===");

        if (mathResults.Count != gameObjectResults.Count)
        {
            Debug.LogWarning($"[SceneFlowCalculator] Bone count mismatch: Math={mathResults.Count}, GameObject={gameObjectResults.Count}");
        }

        float totalDifference = 0f;
        int differenceCount = 0;

        for (int i = 0; i < mathResults.Count; i++)
        {
            var mathBone = mathResults[i];

            // Find corresponding GameObject result
            var gameBone = gameObjectResults.Find(b => b.Name == mathBone.Name);
            if (gameBone != null)
            {
                float diff = Vector3.Distance(mathBone.GlobalPositionMath, gameBone.GlobalPositionGameObject);
                mathBone.GlobalPositionGameObject = gameBone.GlobalPositionGameObject;
                mathBone.PositionDifference = diff;

                totalDifference += diff;
                differenceCount++;

                if (debugMode && diff > 0.001f)
                {
                    Debug.Log($"[SceneFlowCalculator] {mathBone.Name}: diff={diff:F4} (Math={mathBone.GlobalPositionMath}, GO={gameBone.GlobalPositionGameObject})");
                }
            }
        }

        float avgDifference = differenceCount > 0 ? totalDifference / differenceCount : 0f;
        Debug.Log($"[SceneFlowCalculator] Average position difference: {avgDifference:F6}");

        if (avgDifference < 0.001f)
        {
            Debug.Log("[SceneFlowCalculator] ✓ Both methods produce nearly identical results (Math method is reliable)");
        }
        else
        {
            Debug.LogWarning($"[SceneFlowCalculator] ⚠ Methods differ by {avgDifference:F6}m on average");
        }
    }

    /// <summary>
    /// Log bone hierarchy in tree format with indentation
    /// </summary>
    private void LogBoneHierarchy(List<BoneData> bones, int indent)
    {
        if (bones == null || bones.Count == 0)
            return;

        // Log root bone first
        var rootBone = bones[0];
        LogBoneEntry(rootBone, 0);

        // Find and log children recursively
        LogBoneChildren(bones, rootBone, 1);
    }

    /// <summary>
    /// Recursively log child bones with indentation
    /// </summary>
    private void LogBoneChildren(List<BoneData> bones, BoneData parentBone, int indent)
    {
        var children = bones.FindAll(b => b.ParentName == parentBone.Name);

        foreach (var child in children)
        {
            LogBoneEntry(child, indent);
            LogBoneChildren(bones, child, indent + 1);
        }
    }

    /// <summary>
    /// Log a single bone entry with indentation
    /// </summary>
    private void LogBoneEntry(BoneData bone, int depth)
    {
        string indent = new string(' ', depth * 2);
        string message = $"{indent}{bone.Name}";
        message += $" [depth={bone.Depth}, children={bone.ChildCount}]";
        message += $" local_offset={bone.LocalOffset}";
        message += $" global_pos={bone.GlobalPositionMath:F3}";

        if (bone.PositionDifference > 0.001f)
        {
            message += $" (diff={bone.PositionDifference:F4})";
        }

        Debug.Log(message);
    }

    /// <summary>
    /// Create a temporary BVH skeleton GameObject hierarchy for visualization
    /// This is completely independent from Timeline's BVH_Character
    /// </summary>
    private Transform CreateTemporaryBvhSkeleton(int frameIndex)
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

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Created temporary skeleton with {bvhJoints.Count} bones");

        return tempSkeletonRoot;
    }

    /// <summary>
    /// Create temporary BVH skeleton with frame data applied and scale transform
    /// Uses PlayableFrameApplier to apply scale same as BvhPlayableBehaviour
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

        // Apply frame data with scale using PlayableFrameApplier
        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData != null)
        {
            // Find the root joint transform
            Transform rootJointTransform = tempSkeletonRoot.Find(bvhData.RootJoint.Name);
            if (rootJointTransform != null)
            {
                // Create a custom applier with scale
                var applier = new SceneFlowFrameApplier(bvhScale);
                applier.ApplyFrame(bvhData.RootJoint, rootJointTransform, frameData);
            }
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Created temporary skeleton with scale {bvhScale}");

        return tempSkeletonRoot;
    }

    /// <summary>
    /// Custom frame applier for SceneFlowCalculator with scale applied (same as BvhPlayableBehaviour)
    /// </summary>
    private class SceneFlowFrameApplier : BvhFrameApplier
    {
        private Vector3 scale;

        public SceneFlowFrameApplier(Vector3 scale)
        {
            this.scale = scale;
        }

        protected override Vector3 AdjustPosition(Vector3 basePosition, BvhJoint joint, bool isRoot)
        {
            // Apply scale to all joints (including root)
            return Vector3.Scale(basePosition, scale);
        }
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
    /// Apply specific BVH frame data to temporary skeleton transforms
    /// Uses same logic as BvhFrameApplier (proven working in Timeline)
    /// </summary>
    private void ApplyFrameDataToSkeletonTransforms(Transform skeletonRoot, int frameIndex)
    {
        if (bvhData == null || skeletonRoot == null)
            return;

        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData == null)
        {
            Debug.LogError($"[SceneFlowCalculator] Failed to get frame data for frame {frameIndex}");
            return;
        }

        int channelIndex = 0;
        TraverseAndApplyFrame(skeletonRoot, frameData, ref channelIndex, bvhData.RootJoint);

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Applied frame {frameIndex} to temporary skeleton");
    }

    /// <summary>
    /// Recursively traverse skeleton and apply frame data to each bone
    /// </summary>
    private void TraverseAndApplyFrame(Transform transform, float[] frameData, ref int channelIndex, BvhJoint joint)
    {
        if (joint == null)
            return;

        Vector3 localPos = Vector3.zero;
        Vector3 localRot = Vector3.zero;

        // Read frame data for this joint
        BvhChannelReader.ReadChannelData(joint.Channels, frameData, ref channelIndex, ref localPos, ref localRot);

        // Apply local position and rotation
        transform.localPosition = joint.Offset + localPos;
        transform.localRotation = BvhChannelReader.GetRotationQuaternion(localRot);

        if (debugMode)
        {
            Debug.Log($"[TraverseAndApplyFrame] {joint.Name}: offset={joint.Offset}, framePos={localPos}, frameRot={localRot.ToString("F2")}, localRot_quat={transform.localRotation}");
        }

        // Traverse children
        int childIndex = 0;
        foreach (Transform child in transform)
        {
            if (childIndex < joint.Children.Count)
            {
                TraverseAndApplyFrame(child, frameData, ref channelIndex, joint.Children[childIndex]);
                childIndex++;
            }
        }
    }

    /// <summary>
    /// Gather all Transform references from temporary skeleton in depth-first order
    /// Returns transforms in same order as bvhJoints
    /// </summary>
    private List<Transform> GatherBoneTransformsFromSkeleton(Transform skeletonRoot)
    {
        var boneTransforms = new List<Transform>();

        // Create a dictionary for quick lookup by bone name
        var transformsByName = new System.Collections.Generic.Dictionary<string, Transform>();

        void BuildNameMap(Transform bone)
        {
            transformsByName[bone.name] = bone;
            foreach (Transform child in bone)
            {
                BuildNameMap(child);
            }
        }

        BuildNameMap(skeletonRoot);

        // Gather transforms in the same order as bvhJoints
        foreach (BvhJoint joint in bvhJoints)
        {
            if (transformsByName.TryGetValue(joint.Name, out Transform boneTransform))
            {
                boneTransforms.Add(boneTransform);
            }
            else
            {
                Debug.LogWarning($"[SceneFlowCalculator] Could not find transform for bone '{joint.Name}'");
            }
        }

        return boneTransforms;
    }

    /// <summary>
    /// Get the number of bones in the skeleton
    /// </summary>
    public int GetBoneCount()
    {
        return boneTransforms.Count;
    }

    /// <summary>
    /// Get the segments per bone count
    /// </summary>
    public int GetSegmentsPerBone()
    {
        return segmentsPerBone;
    }

    /// <summary>
    /// Get the current frame time
    /// </summary>
    public float GetCurrentFrameTime()
    {
        return currentFrameTime;
    }

    /// <summary>
    /// Calculate scene flow for the current frame.
    /// This backtracks through BVH history (historyFrameCount depth) and builds linked-list structure.
    /// Call this from a button or manually when you want to compute flow.
    /// </summary>
    public void CalculateSceneFlowForCurrentFrame()
    {
        if (bvhData == null)
        {
            // Try to auto-initialize from BvhPlayableBehaviour in scene
            TryAutoInitializeBvhData();

            if (bvhData == null)
            {
                Debug.LogError("[SceneFlowCalculator] BvhData is not initialized. Call SetBvhData() first or ensure BvhPlayableBehaviour is on Timeline.");
                return;
            }
        }

        if (boneTransforms.Count == 0)
        {
            Debug.LogError("[SceneFlowCalculator] No bone transforms available. Call Initialize() first.");
            return;
        }

        if (bvhPlayableBehaviour == null)
        {
            Debug.LogWarning("[SceneFlowCalculator] BvhPlayableBehaviour not set. Timeline sync will not be available, but history building will proceed.");
            // Allow proceeding without BvhPlayableBehaviour for testing/debug scenarios
        }

        // Build on-demand history by backtracking from current BVH frame
        BuildFrameHistoryBacktrack(currentBvhFrameIndex);

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Built history with {frameHistory.Count} frames, depth = {frameHistory.Count}");
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
    /// Set the current BVH frame index and time (should be called by BvhPlayableBehaviour or TimelineController)
    /// </summary>
    public void SetFrameInfo(int bvhFrameIndex, float frameTime)
    {
        currentBvhFrameIndex = bvhFrameIndex;
        currentFrameTime = frameTime;
    }

    /// <summary>
    /// Get frame history entries (for debugging or export)
    /// </summary>
    public List<FrameHistoryEntry> GetFrameHistory()
    {
        return frameHistory;
    }

    /// <summary>
    /// Get the current BVH frame index
    /// </summary>
    public int GetCurrentBvhFrameIndex()
    {
        return currentBvhFrameIndex;
    }

    /// <summary>
    /// Calculate scene flow for a point cloud by finding nearest segment points and accumulating motion
    /// </summary>
    /// <param name="pointCloudPositions">Array of point cloud positions in world space</param>
    /// <returns>List of PointSceneFlow data for each point</returns>
    public List<PointSceneFlow> CalculatePointFlows(Vector3[] pointCloudPositions)
    {
        if (frameHistory.Count == 0)
        {
            Debug.LogError("[SceneFlowCalculator] Frame history is empty. Call CalculateSceneFlowForCurrentFrame() first.");
            return new();
        }

        if (pointCloudPositions == null || pointCloudPositions.Length == 0)
        {
            Debug.LogWarning("[SceneFlowCalculator] Point cloud positions array is empty.");
            return new();
        }

        pointFlows.Clear();

        // For each point in the point cloud
        for (int pointIdx = 0; pointIdx < pointCloudPositions.Length; pointIdx++)
        {
            var flow = new PointSceneFlow { position = pointCloudPositions[pointIdx] };

            // Find nearest segment point in the current frame (latest frame)
            var currentFrameEntry = frameHistory[frameHistory.Count - 1];
            FindNearestSegmentInFrame(flow, currentFrameEntry);

            // Accumulate motion over all historical frames
            AccumulateMotion(flow);

            pointFlows.Add(flow);
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Calculated scene flow for {pointFlows.Count} points");

        return pointFlows;
    }

    /// <summary>
    /// Find the nearest segment point in a given frame for a point cloud point
    /// Updates nearestSegmentIndex, distanceToSegment, and currentMotionVector
    /// </summary>
    /// <param name="pointFlow">Point flow data to update</param>
    /// <param name="frameEntry">Frame to search in</param>
    private void FindNearestSegmentInFrame(PointSceneFlow pointFlow, FrameHistoryEntry frameEntry)
    {
        float minDistance = float.MaxValue;
        int nearestSegmentIdx = -1;
        Vector3 nearestMotionVector = Vector3.zero;

        // Search all bones and all segments
        for (int boneIdx = 0; boneIdx < frameEntry.segments.Length; boneIdx++)
        {
            var boneSegments = frameEntry.segments[boneIdx];

            for (int segIdx = 0; segIdx < boneSegments.Length; segIdx++)
            {
                var segment = boneSegments[segIdx];
                float distance = Vector3.Distance(pointFlow.position, segment.position);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestSegmentIdx = boneIdx * segmentsPerBone + segIdx;
                    nearestMotionVector = segment.motionVector;
                }
            }
        }

        pointFlow.nearestSegmentIndex = nearestSegmentIdx;
        pointFlow.distanceToSegment = minDistance;
        pointFlow.currentMotionVector = nearestMotionVector;
    }

    /// <summary>
    /// Accumulate motion vectors over historical frames for a point
    /// Traces back through the linked-list chain to sum all motion vectors
    /// </summary>
    /// <param name="pointFlow">Point flow data to accumulate into</param>
    private void AccumulateMotion(PointSceneFlow pointFlow)
    {
        if (pointFlow.nearestSegmentIndex < 0 || frameHistory.Count == 0)
        {
            pointFlow.cumulativeMotionVector = Vector3.zero;
            return;
        }

        Vector3 cumulativeMotion = Vector3.zero;

        // Start from the current frame and trace backward
        var currentFrameEntry = frameHistory[frameHistory.Count - 1];
        int boneIdx = pointFlow.nearestSegmentIndex / segmentsPerBone;
        int segIdx = pointFlow.nearestSegmentIndex % segmentsPerBone;

        // Traverse the linked-list chain backward through frames
        BoneSegmentPoint currentSegment = currentFrameEntry.segments[boneIdx][segIdx];

        while (currentSegment != null)
        {
            cumulativeMotion += currentSegment.motionVector;
            currentSegment = currentSegment.previousPoint;
        }

        pointFlow.cumulativeMotionVector = cumulativeMotion;
    }

    /// <summary>
    /// Get all calculated point flows (returns reference to internal list)
    /// </summary>
    /// <returns>List of PointSceneFlow data</returns>
    public List<PointSceneFlow> GetPointFlows()
    {
        return pointFlows;
    }

    /// <summary>
    /// Export cumulative motion vectors for all points
    /// </summary>
    /// <returns>Array of cumulative motion vectors matching point cloud order</returns>
    public Vector3[] GetCumulativeMotionVectors()
    {
        var result = new Vector3[pointFlows.Count];
        for (int i = 0; i < pointFlows.Count; i++)
        {
            result[i] = pointFlows[i].cumulativeMotionVector;
        }
        return result;
    }

    /// <summary>
    /// Export current motion vectors for all points
    /// </summary>
    /// <returns>Array of current motion vectors matching point cloud order</returns>
    public Vector3[] GetCurrentMotionVectors()
    {
        var result = new Vector3[pointFlows.Count];
        for (int i = 0; i < pointFlows.Count; i++)
        {
            result[i] = pointFlows[i].currentMotionVector;
        }
        return result;
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
    /// Draw Gizmos for scene flow visualization in the Scene view
    /// Displays bone positions when visualization is enabled, or segment points and motion vectors
    /// </summary>
    private void OnDrawGizmos()
    {
        // Draw bone visualization if enabled by OnShowSceneFlow()
        if (shouldVisualizeBones && visualizationBonePositions != null && bvhJoints.Count > 0)
        {
            DrawBoneVisualization();
            return;
        }

        // Draw scene flow debug visualization
        if (!debugMode || frameHistory.Count == 0)
            return;

        DrawDebugVisualization();
    }

    /// <summary>
    /// Draw bone structure based on cached BVH positions
    /// Shows joints as spheres and parent-child connections as lines
    /// </summary>
    private void DrawBoneVisualization()
    {
        for (int i = 0; i < bvhJoints.Count; i++)
        {
            BvhJoint joint = bvhJoints[i];
            Vector3 bonePos = visualizationBonePositions[i];

            // Draw joint sphere
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(bonePos, 0.02f);

            // Draw lines to children
            foreach (var child in joint.Children)
            {
                int childIndex = bvhJoints.IndexOf(child);
                if (childIndex >= 0)
                {
                    Vector3 childPos = visualizationBonePositions[childIndex];
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(bonePos, childPos);
                }
            }

            // Highlight root bone with larger red sphere
            if (joint.Parent == null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(bonePos, 0.03f);
            }
        }
    }
}
