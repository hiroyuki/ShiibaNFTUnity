using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using System;
using System.Collections.Generic;

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
    /// Tracks nearest bone segments for K-NN interpolation.
    /// Stores segment index, squared distance, and motion vector for weighted averaging.
    /// </summary>
    private struct NearestSegment
    {
        public int segmentIndex;
        public float distanceSq;
        public Vector3 motionVector;
    }

    /// <summary>
    /// Stores K-nearest segments info for a single point for visualization.
    /// Includes segment positions and weights for debugging.
    /// </summary>
    private struct KNearestInfo
    {
        public Vector3[] segmentPositions;  // K segment positions
        public float[] weights;              // K weights (normalized to sum to 1.0)
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

    [Header("Point Cloud Motion Vector Visualization")]
    [SerializeField]
    [Tooltip("Show motion vectors for point cloud points")]
    private bool showPointCloudMotionVectors = true;

    [SerializeField]
    [Tooltip("Scale factor for motion vector arrows")]
    private float pointCloudArrowScale = 0.01f;

    [SerializeField]
    [Tooltip("Maximum number of points to visualize (subsampling for performance)")]
    private int maxPointsToVisualize = 10000;

    [SerializeField]
    [Tooltip("Only draw motion vectors for points visible in Scene view viewport")]
    private bool useFrustumCulling = true;

    [SerializeField]
    [Tooltip("Draw lines from points to their matched bone segments")]
    private bool showMatchingLines = false;

    [Header("Motion Vector Calculation")]
    [SerializeField]
    [Tooltip("Number of nearest bone segments to interpolate (3-10 recommended)")]
    [Range(1, 20)]
    private int kNearestNeighbors = 5;

    [SerializeField]
    [Tooltip("Normalize motion vector magnitudes to [0,1] range for export")]
    private bool normalizeMotionMagnitudes = true;

    [SerializeField]
    [Tooltip("Use weighted K-NN interpolation (false = original nearest-neighbor)")]
    private bool useWeightedInterpolation = true;

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

    /// <summary>Reference to CurrentFrameBVH container for Gizmo visualization (blue)</summary>
    private Transform currentFrameContainer;

    /// <summary>Reference to PreviousFrameBVH container for Gizmo visualization (yellow)</summary>
    private Transform previousFrameContainer;

    /// <summary>Joint motion data for all joints in current frame for visualization</summary>
    private readonly List<SegmentedBoneMotionData> jointMotionDataList = new();

    /// <summary>Cached segment positions for current frame (per-bone arrays)</summary>
    private SegmentedBoneMotionData[][] cachedCurrentFrameSegments;

    /// <summary>Cached segment positions for previous frame (per-bone arrays)</summary>
    private SegmentedBoneMotionData[][] cachedPreviousFrameSegments;

    /// <summary>Cached motion vector data for bone segments</summary>
    private List<SegmentedBoneMotionData> cachedSegmentMotionVectors;

    /// <summary>Motion vectors for point cloud visualization</summary>
    private Vector3[] pointCloudMotionVectors = null;

    /// <summary>Point cloud positions for visualization</summary>
    private Vector3[] pointCloudPositions = null;

    /// <summary>Matched bone segment positions for each point</summary>
    private Vector3[] matchedSegmentPositions = null;

    /// <summary>K-nearest segments info for each point (for visualization)</summary>
    private KNearestInfo[] kNearestInfoPerPoint = null;

    // Frame tracking
    private double currentFrameTime = 0f;

    /// <summary>
    /// Clear all cached frame data
    /// </summary>
    private void ClearCachedData()
    {
        currentFrameContainer = null;
        previousFrameContainer = null;
        currentFrameBones.Clear();
        previousFrameBones.Clear();
        cachedCurrentFrameSegments = null;
        cachedPreviousFrameSegments = null;
        cachedSegmentMotionVectors = null;
    }

    /// <summary>
    /// Try to auto-initialize BVH data from BvhDataCache (centralized data source)
    /// </summary>
    private void TryAutoInitializeBvhData()
    {
        if (bvhData != null)
            return;  // Already initialized

        // Get BVH data from BvhDataCache cache (initialized by MultiCameraPointCloudManager)
        bvhData = BvhDataCache.GetBvhData();

        if (bvhData != null && debugMode)
            Debug.Log("[SceneFlowCalculator] BvhData loaded from BvhDataCache");

        if (bvhData == null)
            Debug.LogError("[SceneFlowCalculator] BvhData not initialized. MultiCameraPointCloudManager must be in scene to initialize BvhDataCache.");
    }

    /// <summary>
    /// Show scene flow for the current BVH frame.
    /// Called when "Show Scene Flow" button is pressed in the Inspector.
    /// Applies same position offset and drift correction as BVH_Visuals for alignment.
    /// </summary>
    public void OnShowSceneFlow()
    {
        ClearCachedData();

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
                       positionOffset, rotationOffset, bvhScale, driftCorrectionData);
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
                           positionOffset, rotationOffset, bvhScale, driftCorrectionData);
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

        // Calculate and cache bone segment data for visualization
        CacheSegmentDataForVisualization();

        // Calculate point cloud motion vectors
        CalculatePointCloudMotionVectors();
    }

    /// <summary>
    /// Get bone segment data in GPU-compatible format
    /// Converts cached SegmentedBoneMotionData to BoneSegmentGPUData array
    /// </summary>
    public BoneSegmentGPUData[] GetBoneSegmentGPUData()
    {
        if (cachedSegmentMotionVectors == null || cachedSegmentMotionVectors.Count == 0)
        {
            Debug.LogWarning("[SceneFlowCalculator] No cached segment motion vectors available");
            return new BoneSegmentGPUData[0];
        }

        BoneSegmentGPUData[] gpuData = new BoneSegmentGPUData[cachedSegmentMotionVectors.Count];
        for (int i = 0; i < cachedSegmentMotionVectors.Count; i++)
        {
            var seg = cachedSegmentMotionVectors[i];
            gpuData[i] = new BoneSegmentGPUData
            {
                currentPosition = seg.position,
                previousPosition = seg.previousPosition,
                motionVector = seg.motionVector,
                boneIndex = seg.boneIndex,
                segmentIndex = seg.segmentIndex,
                interpolationT = seg.interpolationT,
                motionMagnitude = seg.motionMagnitude
            };
        }

        return gpuData;
    }

    /// <summary>
    /// Calculate motion vectors for point cloud points by finding nearest bone segment
    /// Called after bone segment data is cached in OnShowSceneFlow()
    /// </summary>
    private void CalculatePointCloudMotionVectors()
    {
        // Get current frame point cloud mesh
        MultiPointCloudView view = FindFirstObjectByType<MultiPointCloudView>();
        if (view == null)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] No MultiPointCloudView found in scene");
            return;
        }

        // Get unified mesh from child GameObject "UnifiedPointCloudViewer"
        Transform unifiedViewerTransform = view.transform.Find("UnifiedPointCloudViewer");
        if (unifiedViewerTransform == null)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] UnifiedPointCloudViewer child not found");
            return;
        }

        MeshFilter meshFilter = unifiedViewerTransform.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] No point cloud mesh found on UnifiedPointCloudViewer");
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;

        if (vertices.Length == 0)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] Point cloud mesh has no vertices");
            return;
        }

        // Get bone segment data
        BoneSegmentGPUData[] segments = GetBoneSegmentGPUData();
        if (segments.Length == 0)
        {
            if (debugMode)
                Debug.LogWarning("[SceneFlowCalculator] No bone segment data available");
            return;
        }

        // Calculate motion vectors using K-NN or nearest-neighbor algorithm
        pointCloudPositions = vertices;

        if (useWeightedInterpolation)
        {
            // K-NN interpolation with visualization data
            pointCloudMotionVectors = new Vector3[vertices.Length];
            kNearestInfoPerPoint = new KNearestInfo[vertices.Length];
            matchedSegmentPositions = new Vector3[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                pointCloudMotionVectors[i] = CalculateKNNMotionVectorForPoint(
                    vertices[i],
                    segments,
                    kNearestNeighbors,
                    out kNearestInfoPerPoint[i]
                );

                // Also track nearest for legacy visualization
                int nearestIdx = FindNearestSegmentIndex(vertices[i], segments);
                if (nearestIdx >= 0)
                {
                    matchedSegmentPositions[i] = segments[nearestIdx].currentPosition;
                }
            }

            // Apply normalization if enabled
            if (normalizeMotionMagnitudes)
            {
                NormalizeMotionVectorMagnitudes(pointCloudMotionVectors);
            }
        }
        else
        {
            // Original nearest-neighbor (no K-nearest info needed)
            pointCloudMotionVectors = CalculateWeightedKNNMotionVectors(
                vertices,
                segments,
                kNearestNeighbors,
                normalizeMotionMagnitudes
            );

            // Update matched segment positions for visualization
            matchedSegmentPositions = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int nearestIdx = FindNearestSegmentIndex(vertices[i], segments);
                if (nearestIdx >= 0)
                {
                    matchedSegmentPositions[i] = segments[nearestIdx].currentPosition;
                }
            }

            // Clear K-nearest info when not using K-NN
            kNearestInfoPerPoint = null;
        }

        if (debugMode)
        {
            string algorithmName = useWeightedInterpolation ? $"K-NN (K={kNearestNeighbors})" : "nearest-neighbor";
            string normalizationStatus = normalizeMotionMagnitudes ? " with normalization" : "";
            Debug.Log($"[SceneFlowCalculator] Calculated motion vectors for {vertices.Length} points using {segments.Length} bone segments ({algorithmName}{normalizationStatus})");
        }
    }

    /// <summary>
    /// Get BVH position offset from DatasetConfig
    /// </summary>
    private Vector3 GetBvhPositionOffset()
    {
        DatasetConfig config = DatasetConfig.GetInstance();
        if (config != null)
            return config.BvhPositionOffset;

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
            return config.BvhRotationOffset;

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
            return config.BvhScale;

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
    /// Display a BVH frame with specified container name and time
    /// Handles creation/destruction of container and attachment of BVH skeleton
    /// </summary>
    private void DisplayBvhFrame(string containerName, int frameIndex, float frameTime,
                                 Vector3 positionOffset, Vector3 rotationOffset, Vector3 bvhScale,
                                 BvhPlaybackCorrectionKeyframes driftCorrectionData)
    {
        // Remove existing container if present
        Transform existingContainer = transform.Find(containerName);
        if (existingContainer != null)
        {
            DestroyImmediate(existingContainer.gameObject);
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

        int frameIndex = frameMapper.GetTargetFrameForTime(timelineTime, bvhData, driftCorrectionData);
        Debug.Log($"[SceneFlowCalculator] Calculated frame index from Timeline time {timelineTime}s: {frameIndex}");
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

        foreach (BvhJoint joint in bvhJoints)
        {
            foreach (BvhJoint childJoint in joint.Children)
            {
                BoneDefinition boneDef = new BoneDefinition
                {
                    index = boneIndex,
                    parentJoint = joint,
                    childJoint = childJoint,
                    isEndSiteChild = childJoint.IsEndSite
                };

                templateBones.Add(boneDef);
                boneIndex++;
            }
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Created {templateBones.Count} template bone definitions from BVH hierarchy");
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

        Vector3 parentPos = boneDef.parentTransform.position;
        Vector3 childPos;

        if (boneDef.isEndSiteChild && boneDef.childTransform == null)
        {
            childPos = boneDef.parentTransform.position + boneDef.parentTransform.TransformDirection(boneDef.childJoint.Offset);
        }
        else if (boneDef.childTransform != null)
        {
            childPos = boneDef.childTransform.position;
        }
        else
        {
            Debug.LogWarning($"[SceneFlowCalculator] Child transform not available for bone {boneIndex}");
            return new SegmentedBoneMotionData[0];
        }

        SegmentedBoneMotionData[] segments = new SegmentedBoneMotionData[segmentsPerBone];

        for (int i = 0; i < segmentsPerBone; i++)
        {
            float t = segmentsPerBone > 1 ? (float)i / (segmentsPerBone - 1) : 0f;
            Vector3 segmentPos = Vector3.Lerp(parentPos, childPos, t);

            segments[i] = new SegmentedBoneMotionData(
                boneIndex,
                i,
                segmentPos,
                t,
                $"{boneDef.parentJoint.Name}->{boneDef.childJoint.Name}"
            );
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
    /// Draw skeleton visualization in Scene view using Gizmos
    /// </summary>
    private void OnDrawGizmos()
    {
        if (currentFrameContainer != null && currentFrameContainer.gameObject != null)
            DrawBvhContainerStructure(currentFrameContainer, Color.blue, 0.02f);

        if (previousFrameContainer != null && previousFrameContainer.gameObject != null)
            DrawBvhContainerStructure(previousFrameContainer, Color.yellow, 0.015f);

        DrawBoneSegmentPositions();
        DrawBoneSegmentMotionVectors();

        if (showPointCloudMotionVectors && pointCloudMotionVectors != null)
            DrawPointCloudMotionVectors();
    }

    /// <summary>
    /// Draw bone segment positions as small white spheres in Scene view
    /// Uses pre-cached segment data for performance
    /// </summary>
    private void DrawBoneSegmentPositions()
    {
        const float segmentSphereRadius = 0.003f;

        // Draw segments for current frame (white)
        if (cachedCurrentFrameSegments != null)
        {
            foreach (var boneSegments in cachedCurrentFrameSegments)
            {
                foreach (var segment in boneSegments)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawSphere(segment.position, segmentSphereRadius);
                }
            }
        }

        // Draw segments for previous frame (light gray)
        if (cachedPreviousFrameSegments != null)
        {
            foreach (var boneSegments in cachedPreviousFrameSegments)
            {
                foreach (var segment in boneSegments)
                {
                    Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                    Gizmos.DrawSphere(segment.position, segmentSphereRadius);
                }
            }
        }
    }

    /// <summary>
    /// Link bone definitions to a specific frame skeleton transforms
    /// Creates a new list of BoneDefinition objects with transforms linked to the given frame
    /// </summary>
    private List<BoneDefinition> LinkBoneDefinitionsToFrame(Transform frameContainer)
    {
        var linkedBones = new List<BoneDefinition>();

        if (frameContainer == null || templateBones.Count == 0)
            return linkedBones;

        if (frameContainer.childCount == 0)
            return linkedBones;

        Transform tempSkeleton = frameContainer.GetChild(0);
        if (tempSkeleton.childCount == 0)
            return linkedBones;

        Transform bvhRoot = tempSkeleton.GetChild(0);
        int linkedCount = 0;

        foreach (var templateBone in templateBones)
        {
            var boneDef = new BoneDefinition
            {
                index = templateBone.index,
                parentJoint = templateBone.parentJoint,
                childJoint = templateBone.childJoint,
                isEndSiteChild = templateBone.isEndSiteChild
            };

            Transform parentTransform = FindJointTransformByName(bvhRoot, boneDef.parentJoint.Name);
            if (parentTransform != null)
            {
                boneDef.parentTransform = parentTransform;
                linkedCount++;
            }

            if (!boneDef.isEndSiteChild)
            {
                Transform childTransform = FindJointTransformByName(bvhRoot, boneDef.childJoint.Name);
                if (childTransform != null)
                    boneDef.childTransform = childTransform;
            }

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
    /// Pre-calculate and cache all segment data for efficient rendering in OnDrawGizmos
    /// Called once per frame update from OnShowSceneFlow
    /// </summary>
    private void CacheSegmentDataForVisualization()
    {
        // Calculate current frame segments
        if (currentFrameBones.Count > 0)
        {
            cachedCurrentFrameSegments = new SegmentedBoneMotionData[currentFrameBones.Count][];
            for (int boneIdx = 0; boneIdx < currentFrameBones.Count; boneIdx++)
            {
                cachedCurrentFrameSegments[boneIdx] = CalculateSegmentPositionsForBone(currentFrameBones, boneIdx);
            }
        }
        else
        {
            cachedCurrentFrameSegments = null;
        }

        // Calculate previous frame segments
        if (previousFrameBones.Count > 0)
        {
            cachedPreviousFrameSegments = new SegmentedBoneMotionData[previousFrameBones.Count][];
            for (int boneIdx = 0; boneIdx < previousFrameBones.Count; boneIdx++)
            {
                cachedPreviousFrameSegments[boneIdx] = CalculateSegmentPositionsForBone(previousFrameBones, boneIdx);
            }
        }
        else
        {
            cachedPreviousFrameSegments = null;
        }

        // Calculate motion vectors if both frames available
        if (cachedCurrentFrameSegments != null && cachedPreviousFrameSegments != null)
        {
            cachedSegmentMotionVectors = new List<SegmentedBoneMotionData>();

            for (int boneIdx = 0; boneIdx < cachedCurrentFrameSegments.Length && boneIdx < cachedPreviousFrameSegments.Length; boneIdx++)
            {
                var currentSegments = cachedCurrentFrameSegments[boneIdx];
                var previousSegments = cachedPreviousFrameSegments[boneIdx];

                for (int segIdx = 0; segIdx < currentSegments.Length && segIdx < previousSegments.Length; segIdx++)
                {
                    var currentSeg = currentSegments[segIdx];
                    var previousSeg = previousSegments[segIdx];

                    var segmentWithMotion = new SegmentedBoneMotionData(
                        currentSeg.boneIndex,
                        currentSeg.segmentIndex,
                        currentSeg.position,
                        previousSeg.position,
                        currentSeg.interpolationT,
                        currentSeg.boneName
                    );
                    cachedSegmentMotionVectors.Add(segmentWithMotion);
                }
            }
        }
        else
        {
            cachedSegmentMotionVectors = null;
        }

        if (debugMode)
        {
            int totalSegments = 0;
            if (cachedCurrentFrameSegments != null)
            {
                foreach (var arr in cachedCurrentFrameSegments)
                    totalSegments += arr.Length;
            }
            Debug.Log($"[SceneFlowCalculator] Cached {totalSegments} segments for visualization");
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
    /// Uses pre-cached motion vector data for performance
    /// </summary>
    private void DrawBoneSegmentMotionVectors()
    {
        if (cachedSegmentMotionVectors == null || cachedSegmentMotionVectors.Count == 0)
            return;

        const float motionThreshold = 0.001f;
        const float arrowHeadSize = 0.002f;

        // Find max motion magnitude for color gradient
        float maxMagnitude = 0f;
        foreach (var segment in cachedSegmentMotionVectors)
        {
            if (segment.motionMagnitude > maxMagnitude)
                maxMagnitude = segment.motionMagnitude;
        }

        if (maxMagnitude < motionThreshold)
            return;

        // Draw arrows for each segment
        foreach (var segment in cachedSegmentMotionVectors)
        {
            if (segment.motionMagnitude > motionThreshold)
            {
                float colorFactor = segment.motionMagnitude / maxMagnitude;
                Color arrowColor = Color.Lerp(Color.blue, Color.red, colorFactor);

                Gizmos.color = arrowColor;
                Vector3 startPoint = segment.previousPosition;
                Vector3 endPoint = segment.position;
                Gizmos.DrawLine(startPoint, endPoint);
                Gizmos.DrawSphere(endPoint, arrowHeadSize);
            }
        }
    }

    /// <summary>
    /// Draw motion vectors for point cloud points with frustum culling
    /// Only draws vectors for points visible in Scene view viewport
    /// </summary>
    private void DrawPointCloudMotionVectors()
    {
        if (pointCloudPositions == null || pointCloudMotionVectors == null)
            return;

        // Get Scene view camera for frustum culling
        Camera sceneCamera = null;
#if UNITY_EDITOR
        if (UnityEditor.SceneView.lastActiveSceneView != null)
        {
            sceneCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
        }
#endif

        // Build frustum planes for culling (only show points in viewport)
        Plane[] frustumPlanes = null;
        if (useFrustumCulling && sceneCamera != null)
        {
            frustumPlanes = GeometryUtility.CalculateFrustumPlanes(sceneCamera);
        }

        // Find max magnitude for color gradient
        float maxMagnitude = 0f;
        foreach (var mv in pointCloudMotionVectors)
        {
            if (mv.magnitude > maxMagnitude)
                maxMagnitude = mv.magnitude;
        }

        // Subsample for performance (draw every Nth point)
        int step = Mathf.Max(1, pointCloudPositions.Length / maxPointsToVisualize);
        int visibleCount = 0;

        for (int i = 0; i < pointCloudPositions.Length; i += step)
        {
            Vector3 motion = pointCloudMotionVectors[i];
            if (motion.magnitude < 0.001f)
                continue; // Skip near-zero motion

            Vector3 start = pointCloudPositions[i];

            // Frustum culling: only draw if point is in Scene view viewport
            if (frustumPlanes != null)
            {
                Bounds pointBounds = new Bounds(start, Vector3.one * 0.01f);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, pointBounds))
                {
                    continue; // Skip points outside viewport
                }
            }

            Vector3 end = start + motion * pointCloudArrowScale;

            // Also check if endpoint is visible (where sphere will be drawn)
            if (frustumPlanes != null)
            {
                Bounds endBounds = new Bounds(end, Vector3.one * 0.01f);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, endBounds))
                {
                    continue; // Skip if endpoint is outside viewport
                }
            }

            // Color gradient: blue (slow) → red (fast)
            float t = maxMagnitude > 0 ? motion.magnitude / maxMagnitude : 0;
            Gizmos.color = Color.Lerp(Color.blue, Color.red, t);

            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.002f);

            // Draw lines to K-nearest bone segments (with color intensity based on weight)
            if (showMatchingLines && useWeightedInterpolation && kNearestInfoPerPoint != null && i < kNearestInfoPerPoint.Length)
            {
                KNearestInfo knnInfo = kNearestInfoPerPoint[i];
                if (knnInfo.segmentPositions != null && knnInfo.weights != null)
                {
                    // Debug: Log weights for first visualized point
                    if (visibleCount == 0 && debugMode)
                    {
                        string weightsDebug = string.Join(", ", System.Array.ConvertAll(knnInfo.weights, w => w.ToString("F3")));
                        Debug.Log($"[Gizmo Debug] First point K-nearest weights: [{weightsDebug}], K={knnInfo.weights.Length}");

                        // Also log relative weights and colors for debugging
                        float avgWeight = 1f / knnInfo.weights.Length;
                        for (int debugK = 0; debugK < knnInfo.weights.Length; debugK++)
                        {
                            float relWeight = knnInfo.weights[debugK] / avgWeight;
                            Debug.Log($"  Segment {debugK}: weight={knnInfo.weights[debugK]:F3}, relativeWeight={relWeight:F3}");
                        }
                    }

                    for (int k = 0; k < knnInfo.segmentPositions.Length; k++)
                    {
                        Vector3 segmentPos = knnInfo.segmentPositions[k];
                        float weight = knnInfo.weights[k];

                        // Use rank-based coloring for better visual differentiation
                        // K nearest segments are sorted by distance (index 0 = nearest)
                        // Color scheme: Red (nearest) → Orange → Yellow → Green → Cyan (farthest)
                        float rankNormalized = k / (float)Mathf.Max(1, knnInfo.weights.Length - 1);  // 0.0 = nearest, 1.0 = farthest

                        Color lineColor;
                        float colorT;
                        if (rankNormalized < 0.25f)
                        {
                            // Nearest 25%: Red → Orange
                            colorT = rankNormalized / 0.25f;
                            lineColor = Color.Lerp(Color.red, new Color(1f, 0.5f, 0f), colorT);  // red to orange
                        }
                        else if (rankNormalized < 0.5f)
                        {
                            // 25-50%: Orange → Yellow
                            colorT = (rankNormalized - 0.25f) / 0.25f;
                            lineColor = Color.Lerp(new Color(1f, 0.5f, 0f), Color.yellow, colorT);  // orange to yellow
                        }
                        else if (rankNormalized < 0.75f)
                        {
                            // 50-75%: Yellow → Green
                            colorT = (rankNormalized - 0.5f) / 0.25f;
                            lineColor = Color.Lerp(Color.yellow, Color.green, colorT);  // yellow to green
                        }
                        else
                        {
                            // Farthest 25%: Green → Cyan
                            colorT = (rankNormalized - 0.75f) / 0.25f;
                            lineColor = Color.Lerp(Color.green, Color.cyan, colorT);  // green to cyan
                        }

                        // Alpha varies by weight magnitude (stronger influence = more opaque)
                        lineColor.a = Mathf.Lerp(0.3f, 1.0f, weight * knnInfo.weights.Length);  // weight * K to amplify differences
                        Gizmos.color = lineColor;
                        Gizmos.DrawLine(start, segmentPos);

                        // Draw small sphere at segment position with size proportional to weight
                        float sphereSize = Mathf.Lerp(0.003f, 0.008f, weight);
                        Gizmos.DrawSphere(segmentPos, sphereSize);
                    }
                }
            }
            else if (showMatchingLines && matchedSegmentPositions != null && i < matchedSegmentPositions.Length)
            {
                // Fallback: draw single line to nearest segment (original behavior)
                Vector3 segmentPos = matchedSegmentPositions[i];
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(start, segmentPos);
            }

            visibleCount++;
        }

        // Debug info (only in editor)
#if UNITY_EDITOR
        if (visibleCount > 0 && sceneCamera != null)
        {
            UnityEditor.Handles.Label(
                sceneCamera.transform.position + sceneCamera.transform.forward * 2f,
                $"Visible motion vectors: {visibleCount}");
        }
#endif
    }

    /// <summary>
    /// Calculate bone segments for a specific frame pair
    /// Public method for batch processing - automatically applies drift correction from DatasetConfig
    /// </summary>
    public void CalculateBoneSegmentsForFramePair(int currentFrame, int previousFrame)
    {
        if (bvhData == null)
        {
            TryAutoInitializeBvhData();
            if (bvhData == null)
            {
                Debug.LogError("[SceneFlowCalculator] Cannot calculate bone segments - BvhData is null. Ensure BvhDataCache is initialized.");
                return;
            }
        }

        Vector3 positionOffset = GetBvhPositionOffset();
        Vector3 rotationOffset = GetBvhRotationOffset();
        Vector3 bvhScale = GetBvhScale();
        BvhPlaybackCorrectionKeyframes driftData = GetDriftCorrectionData();

        float currentFrameTime = currentFrame * bvhData.FrameTime;
        float previousFrameTime = previousFrame * bvhData.FrameTime;

        Debug.Log($"[SceneFlowCalculator] Calculating bone segments for frame pair ({previousFrame}, {currentFrame}) at times ({previousFrameTime}s, {currentFrameTime}s)");

        if (templateBones.Count == 0)
        {
            GatherBoneHierarchyFromBvhData();
            GatherBoneDefinitionsFromBvhData();
        }

        DisplayBvhFrame("CurrentFrameBVH", currentFrame, currentFrameTime,
                        positionOffset, rotationOffset, bvhScale, driftData);
        DisplayBvhFrame("PreviousFrameBVH", previousFrame, previousFrameTime,
                        positionOffset, rotationOffset, bvhScale, driftData);

        currentFrameContainer = transform.Find("CurrentFrameBVH");
        previousFrameContainer = transform.Find("PreviousFrameBVH");

        currentFrameBones = LinkBoneDefinitionsToFrame(currentFrameContainer);
        previousFrameBones = LinkBoneDefinitionsToFrame(previousFrameContainer);

        CacheSegmentDataForVisualization();

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Calculated bone segments for frame pair ({previousFrame}, {currentFrame}) - {currentFrameBones.Count} bones");
    }

    /// <summary>
    /// Calculate motion vectors for a given mesh using current bone segment data
    /// Public method for batch processing - must call CalculateBoneSegmentsForFramePair() first
    /// </summary>
    public Vector3[] CalculateMotionVectorsForMesh(Mesh mesh)
    {
        if (mesh == null || mesh.vertices.Length == 0)
        {
            Debug.LogWarning("[SceneFlowCalculator] Invalid mesh provided");
            return new Vector3[0];
        }

        Vector3[] vertices = mesh.vertices;
        BoneSegmentGPUData[] segments = GetBoneSegmentGPUData();

        if (segments.Length == 0)
        {
            Debug.LogWarning("[SceneFlowCalculator] No bone segment data available. Call CalculateBoneSegmentsForFramePair() first.");
            return new Vector3[vertices.Length]; // Return zero vectors
        }

        // Use unified K-NN or nearest-neighbor calculation
        Vector3[] motionVectors = CalculateWeightedKNNMotionVectors(
            vertices,
            segments,
            kNearestNeighbors,
            normalizeMotionMagnitudes
        );

        if (debugMode)
        {
            string algorithmName = useWeightedInterpolation ? $"K-NN (K={kNearestNeighbors})" : "nearest-neighbor";
            string normalizationStatus = normalizeMotionMagnitudes ? " with normalization" : "";
            Debug.Log($"[SceneFlowCalculator] Calculated motion vectors for {vertices.Length} points using {segments.Length} bone segments ({algorithmName}{normalizationStatus})");
        }

        return motionVectors;
    }

    #region K-NN Motion Vector Interpolation

    /// <summary>
    /// Calculate K-nearest neighbor weighted motion vector for a single point.
    /// Uses distance-ratio weighting: closer segments have higher influence.
    /// </summary>
    /// <param name="point">Point cloud vertex position</param>
    /// <param name="segments">Array of bone segment data with motion vectors</param>
    /// <param name="K">Number of nearest neighbors to consider</param>
    /// <returns>Weighted interpolated motion vector</returns>
    private Vector3 CalculateKNNMotionVectorForPoint(Vector3 point, BoneSegmentGPUData[] segments, int K)
    {
        if (segments.Length == 0)
            return Vector3.zero;

        // Handle case where K is larger than available segments
        int actualK = Mathf.Min(K, segments.Length);

        // Initialize K-nearest tracking array
        NearestSegment[] nearest = new NearestSegment[actualK];
        for (int k = 0; k < actualK; k++)
        {
            nearest[k] = new NearestSegment { segmentIndex = -1, distanceSq = float.MaxValue };
        }

        // K-NN search with insertion sort
        for (int s = 0; s < segments.Length; s++)
        {
            float distSq = (point - segments[s].currentPosition).sqrMagnitude;

            // Early exit for zero distance - point exactly on segment
            if (distSq < 0.0001f)
            {
                return segments[s].motionVector;
            }

            // Check if this segment is closer than the farthest in top-K
            if (distSq < nearest[actualK - 1].distanceSq)
            {
                // Find insertion position (keep array sorted by distance)
                int insertPos = actualK - 1;
                for (int k = 0; k < actualK - 1; k++)
                {
                    if (distSq < nearest[k].distanceSq)
                    {
                        insertPos = k;
                        break;
                    }
                }

                // Shift elements right to make space
                for (int k = actualK - 1; k > insertPos; k--)
                {
                    nearest[k] = nearest[k - 1];
                }

                // Insert new segment
                nearest[insertPos] = new NearestSegment
                {
                    segmentIndex = s,
                    distanceSq = distSq,
                    motionVector = segments[s].motionVector
                };
            }
        }

        // Count valid segments found
        int validCount = 0;
        for (int k = 0; k < actualK; k++)
        {
            if (nearest[k].segmentIndex >= 0)
                validCount++;
        }

        if (validCount == 0)
            return Vector3.zero;

        // Calculate distance sum (convert from squared to actual distance)
        float sumDistances = 0f;
        for (int k = 0; k < validCount; k++)
        {
            sumDistances += Mathf.Sqrt(nearest[k].distanceSq);
        }

        // Calculate weights using user's formula: weight[i] = (sumDistances - dist[i]) / sumDistances
        float[] weights = new float[validCount];

        if (sumDistances < 0.0001f)
        {
            // All segments at identical distance - use uniform weights
            for (int k = 0; k < validCount; k++)
            {
                weights[k] = 1f / validCount;
            }
        }
        else
        {
            // Apply distance-ratio formula
            float normFactor = 0f;
            for (int k = 0; k < validCount; k++)
            {
                float dist = Mathf.Sqrt(nearest[k].distanceSq);
                weights[k] = (sumDistances - dist) / sumDistances;
                normFactor += weights[k];
            }

            // Normalize weights to sum to 1.0
            if (normFactor > 0.0001f)
            {
                for (int k = 0; k < validCount; k++)
                {
                    weights[k] /= normFactor;
                }
            }
            else
            {
                // Fallback to uniform weights
                for (int k = 0; k < validCount; k++)
                {
                    weights[k] = 1f / validCount;
                }
            }
        }

        // Weighted interpolation
        Vector3 result = Vector3.zero;
        for (int k = 0; k < validCount; k++)
        {
            result += weights[k] * nearest[k].motionVector;
        }

        return result;
    }

    /// <summary>
    /// Calculate K-nearest neighbor weighted motion vector for a single point.
    /// Overloaded version that also returns K-nearest segment info for visualization.
    /// </summary>
    /// <param name="point">Point cloud vertex position</param>
    /// <param name="segments">Array of bone segment data with motion vectors</param>
    /// <param name="K">Number of nearest neighbors to consider</param>
    /// <param name="kNearestInfo">Output: K-nearest segments positions and weights</param>
    /// <returns>Weighted interpolated motion vector</returns>
    private Vector3 CalculateKNNMotionVectorForPoint(Vector3 point, BoneSegmentGPUData[] segments, int K, out KNearestInfo kNearestInfo)
    {
        // Initialize output
        kNearestInfo = new KNearestInfo
        {
            segmentPositions = new Vector3[0],
            weights = new float[0]
        };

        if (segments.Length == 0)
            return Vector3.zero;

        // Handle case where K is larger than available segments
        int actualK = Mathf.Min(K, segments.Length);

        // Initialize K-nearest tracking array
        NearestSegment[] nearest = new NearestSegment[actualK];
        for (int k = 0; k < actualK; k++)
        {
            nearest[k] = new NearestSegment { segmentIndex = -1, distanceSq = float.MaxValue };
        }

        // K-NN search with insertion sort
        for (int s = 0; s < segments.Length; s++)
        {
            float distSq = (point - segments[s].currentPosition).sqrMagnitude;

            // Early exit for zero distance - point exactly on segment
            if (distSq < 0.0001f)
            {
                // Return single segment info
                kNearestInfo = new KNearestInfo
                {
                    segmentPositions = new Vector3[] { segments[s].currentPosition },
                    weights = new float[] { 1.0f }
                };
                return segments[s].motionVector;
            }

            // Check if this segment is closer than the farthest in top-K
            if (distSq < nearest[actualK - 1].distanceSq)
            {
                // Find insertion position (keep array sorted by distance)
                int insertPos = actualK - 1;
                for (int k = 0; k < actualK - 1; k++)
                {
                    if (distSq < nearest[k].distanceSq)
                    {
                        insertPos = k;
                        break;
                    }
                }

                // Shift elements right to make space
                for (int k = actualK - 1; k > insertPos; k--)
                {
                    nearest[k] = nearest[k - 1];
                }

                // Insert new segment
                nearest[insertPos] = new NearestSegment
                {
                    segmentIndex = s,
                    distanceSq = distSq,
                    motionVector = segments[s].motionVector
                };
            }
        }

        // Count valid segments found
        int validCount = 0;
        for (int k = 0; k < actualK; k++)
        {
            if (nearest[k].segmentIndex >= 0)
                validCount++;
        }

        if (validCount == 0)
            return Vector3.zero;

        // Calculate distance sum (convert from squared to actual distance)
        float sumDistances = 0f;
        for (int k = 0; k < validCount; k++)
        {
            sumDistances += Mathf.Sqrt(nearest[k].distanceSq);
        }

        // Calculate weights using user's formula: weight[i] = (sumDistances - dist[i]) / sumDistances
        float[] weights = new float[validCount];
        Vector3[] segmentPositions = new Vector3[validCount];

        if (sumDistances < 0.0001f)
        {
            // All segments at identical distance - use uniform weights
            for (int k = 0; k < validCount; k++)
            {
                weights[k] = 1f / validCount;
                segmentPositions[k] = segments[nearest[k].segmentIndex].currentPosition;
            }
        }
        else
        {
            // Apply distance-ratio formula
            float normFactor = 0f;
            for (int k = 0; k < validCount; k++)
            {
                float dist = Mathf.Sqrt(nearest[k].distanceSq);
                weights[k] = (sumDistances - dist) / sumDistances;
                normFactor += weights[k];
                segmentPositions[k] = segments[nearest[k].segmentIndex].currentPosition;
            }

            // Normalize weights to sum to 1.0
            if (normFactor > 0.0001f)
            {
                for (int k = 0; k < validCount; k++)
                {
                    weights[k] /= normFactor;
                }
            }
            else
            {
                // Fallback to uniform weights
                for (int k = 0; k < validCount; k++)
                {
                    weights[k] = 1f / validCount;
                }
            }
        }

        // Store K-nearest info for visualization
        kNearestInfo = new KNearestInfo
        {
            segmentPositions = segmentPositions,
            weights = weights
        };

        // Debug: Log weights for first few points
        if (debugMode && UnityEngine.Random.value < 0.001f) // Log 0.1% of points
        {
            string weightsStr = string.Join(", ", System.Array.ConvertAll(weights, w => w.ToString("F3")));
            Debug.Log($"[K-NN Debug] Point weights: [{weightsStr}], sum={System.Linq.Enumerable.Sum(weights):F3}");
        }

        // Weighted interpolation
        Vector3 result = Vector3.zero;
        for (int k = 0; k < validCount; k++)
        {
            result += weights[k] * nearest[k].motionVector;
        }

        return result;
    }

    /// <summary>
    /// Normalize motion vector magnitudes to [0, 1] range.
    /// Largest magnitude becomes 1.0, smallest becomes 0.0.
    /// Preserves direction of each vector while scaling magnitudes.
    /// </summary>
    /// <param name="motionVectors">Motion vector array to normalize in-place</param>
    private void NormalizeMotionVectorMagnitudes(Vector3[] motionVectors)
    {
        if (motionVectors == null || motionVectors.Length == 0)
            return;

        // Find min and max magnitudes
        float minMagnitude = float.MaxValue;
        float maxMagnitude = 0f;

        for (int i = 0; i < motionVectors.Length; i++)
        {
            float mag = motionVectors[i].magnitude;
            if (mag < minMagnitude)
                minMagnitude = mag;
            if (mag > maxMagnitude)
                maxMagnitude = mag;
        }

        // Calculate range
        float range = maxMagnitude - minMagnitude;

        // Skip normalization if all vectors have same magnitude
        if (range < 0.0001f)
        {
            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Skipping magnitude normalization - all vectors have similar magnitude ({maxMagnitude:F6})");
            return;
        }

        // Normalize each vector
        for (int i = 0; i < motionVectors.Length; i++)
        {
            float originalMag = motionVectors[i].magnitude;

            // Handle zero vectors
            if (originalMag < 0.0001f)
            {
                motionVectors[i] = Vector3.zero;
                continue;
            }

            // Calculate normalized magnitude [0, 1]
            float normalizedMag = (originalMag - minMagnitude) / range;

            // Preserve direction, apply normalized magnitude
            Vector3 direction = motionVectors[i] / originalMag;  // Normalize direction
            motionVectors[i] = direction * normalizedMag;
        }

        if (debugMode)
            Debug.Log($"[SceneFlowCalculator] Normalized motion vector magnitudes: [{minMagnitude:F6}, {maxMagnitude:F6}] → [0.0, 1.0]");
    }

    /// <summary>
    /// Original nearest-neighbor algorithm (preserved for comparison and fallback).
    /// Each point is assigned the motion vector of its single nearest bone segment.
    /// </summary>
    /// <param name="vertices">Point cloud vertex positions</param>
    /// <param name="segments">Bone segment data with motion vectors</param>
    /// <returns>Motion vector array (one per vertex)</returns>
    private Vector3[] CalculateNearestNeighborMotionVectors(Vector3[] vertices, BoneSegmentGPUData[] segments)
    {
        Vector3[] motionVectors = new Vector3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 point = vertices[i];
            float minDistSq = float.MaxValue;
            int nearestIdx = -1;

            // Brute-force nearest neighbor search
            for (int s = 0; s < segments.Length; s++)
            {
                float distSq = (point - segments[s].currentPosition).sqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    nearestIdx = s;
                }
            }

            // Assign motion vector from nearest segment
            if (nearestIdx >= 0)
                motionVectors[i] = segments[nearestIdx].motionVector;
        }

        return motionVectors;
    }

    /// <summary>
    /// Unified method for calculating motion vectors with configurable algorithm.
    /// Supports both K-NN weighted interpolation and original nearest-neighbor.
    /// </summary>
    /// <param name="vertices">Point cloud vertex positions</param>
    /// <param name="segments">Bone segment data with motion vectors</param>
    /// <param name="kNearest">Number of nearest neighbors for K-NN (ignored if using nearest-neighbor)</param>
    /// <param name="applyGlobalNormalization">Normalize magnitudes to [0,1] range</param>
    /// <returns>Motion vector array (one per vertex)</returns>
    private Vector3[] CalculateWeightedKNNMotionVectors(
        Vector3[] vertices,
        BoneSegmentGPUData[] segments,
        int kNearest,
        bool applyGlobalNormalization)
    {
        Vector3[] motionVectors;

        // Choose algorithm based on configuration
        if (!useWeightedInterpolation)
        {
            // Use original nearest-neighbor algorithm
            motionVectors = CalculateNearestNeighborMotionVectors(vertices, segments);
        }
        else
        {
            // Use K-NN weighted interpolation
            motionVectors = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                motionVectors[i] = CalculateKNNMotionVectorForPoint(vertices[i], segments, kNearest);
            }
        }

        // Apply global magnitude normalization if enabled
        if (applyGlobalNormalization)
        {
            NormalizeMotionVectorMagnitudes(motionVectors);
        }

        return motionVectors;
    }

    /// <summary>
    /// Find the nearest segment index for a single point (used for visualization).
    /// </summary>
    /// <param name="point">Point cloud vertex position</param>
    /// <param name="segments">Bone segment data</param>
    /// <returns>Index of nearest segment, or -1 if none found</returns>
    private int FindNearestSegmentIndex(Vector3 point, BoneSegmentGPUData[] segments)
    {
        if (segments.Length == 0)
            return -1;

        float minDistSq = float.MaxValue;
        int nearestIdx = -1;

        for (int s = 0; s < segments.Length; s++)
        {
            float distSq = (point - segments[s].currentPosition).sqrMagnitude;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearestIdx = s;
            }
        }

        return nearestIdx;
    }

    #endregion

}
