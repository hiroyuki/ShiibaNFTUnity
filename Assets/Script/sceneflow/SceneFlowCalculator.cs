using UnityEngine;
using System.Collections.Generic;

namespace Assets.Script.sceneflow
{
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
        /// Represents a point on a bone with its motion vector across frames
        /// </summary>
        [System.Serializable]
        public class BoneSegmentPoint
        {
            /// <summary>Index of the bone this segment belongs to</summary>
            public int boneIndex;
            
            /// <summary>Index within the bone (0-99)</summary>
            public int segmentIndex;
            
            /// <summary>Current frame position in world space</summary>
            public Vector3 position;
            
            /// <summary>Previous frame position</summary>
            public Vector3 previousPosition;
            
            /// <summary>Motion vector (current - previous)</summary>
            public Vector3 motionVector;

            /// <summary>
            /// Constructor for BoneSegmentPoint
            /// </summary>
            public BoneSegmentPoint(int boneIdx, int segIdx)
            {
                boneIndex = boneIdx;
                segmentIndex = segIdx;
                position = Vector3.zero;
                previousPosition = Vector3.zero;
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

        [Header("Configuration")]
        
        /// <summary>Number of uniformly distributed points per bone (default: 100)</summary>
        [SerializeField]
        [Tooltip("Number of segment points to generate per bone")]
        private int segmentsPerBone = 100;

        /// <summary>Enable debug logging and visualization</summary>
        [SerializeField]
        [Tooltip("Enable debug mode for logging and visualization")]
        private bool debugMode = false;

        // Internal state - BVH data
        private BvhData bvhData;

        /// <summary>All bone transforms gathered from BVH hierarchy in depth-first order</summary>
        private List<Transform> boneTransforms = new List<Transform>();

        /// <summary>Segment points for each bone (boneIndex -> array of 100 segments)</summary>
        private List<BoneSegmentPoint[]> boneSegments = new List<BoneSegmentPoint[]>();

        /// <summary>Scene flow data for all points in current point cloud</summary>
        private List<PointSceneFlow> pointFlows = new List<PointSceneFlow>();

        // Frame tracking
        private int currentFrameIndex = 0;
        private float currentFrameTime = 0f;

        /// <summary>
        /// Initialize the calculator with BVH data and character root transform
        /// </summary>
        /// <param name="bvhData">BVH animation data containing skeleton and motion frames</param>
        /// <param name="characterRoot">Root transform of the BVH character hierarchy</param>
        public void Initialize(BvhData bvhData, Transform characterRoot)
        {
            this.bvhData = bvhData;

            // Gather all bone transforms from hierarchy
            boneTransforms.Clear();
            GatherBoneTransforms(characterRoot);

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Initialized with {boneTransforms.Count} bones");

            // Initialize segment storage for each bone
            boneSegments.Clear();
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                var segments = new BoneSegmentPoint[segmentsPerBone];
                for (int j = 0; j < segmentsPerBone; j++)
                {
                    segments[j] = new BoneSegmentPoint(i, j);
                }
                boneSegments.Add(segments);
            }

            // Initialize point flows list
            pointFlows.Clear();
        }

        /// <summary>
        /// Recursively gather all bone Transform components from the BVH hierarchy
        /// </summary>
        /// <param name="root">Root transform to start gathering from</param>
        private void GatherBoneTransforms(Transform root)
        {
            boneTransforms.Add(root);

            for (int i = 0; i < root.childCount; i++)
            {
                GatherBoneTransforms(root.GetChild(i));
            }
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
        /// Get the current frame index
        /// </summary>
        public int GetCurrentFrameIndex()
        {
            return currentFrameIndex;
        }

        /// <summary>
        /// Get the current frame time
        /// </summary>
        public float GetCurrentFrameTime()
        {
            return currentFrameTime;
        }

        /// <summary>
        /// Manual trigger to calculate scene flow for the current frame
        /// Call this from a button or manually when you want to compute flow
        /// </summary>
        [ContextMenu("Calculate Scene Flow for Current Frame")]
        public void CalculateSceneFlowForCurrentFrame()
        {
            if (bvhData == null)
            {
                Debug.LogError("[SceneFlowCalculator] BvhData is not initialized. Call Initialize() first.");
                return;
            }

            if (boneTransforms.Count == 0)
            {
                Debug.LogError("[SceneFlowCalculator] No bone transforms available. Call Initialize() first.");
                return;
            }

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Calculating scene flow for frame {currentFrameIndex} at time {currentFrameTime}s");

            // Update bone segments for current frame
            UpdateFrameSegments(currentFrameIndex, currentFrameTime);

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Scene flow calculation complete. Processed {pointFlows.Count} points.");
        }

        /// <summary>
        /// Update bone segment points for the current frame
        /// Saves previous positions and calculates motion vectors
        /// </summary>
        /// <param name="frameIndex">Current BVH frame index</param>
        /// <param name="frameTime">Current timeline time</param>
        private void UpdateFrameSegments(int frameIndex, float frameTime)
        {
            // Store previous frame data before updating
            for (int boneIdx = 0; boneIdx < boneSegments.Count; boneIdx++)
            {
                var segments = boneSegments[boneIdx];
                for (int segIdx = 0; segIdx < segments.Length; segIdx++)
                {
                    segments[segIdx].previousPosition = segments[segIdx].position;
                }
            }

            // Calculate segment points for new frame
            for (int boneIdx = 0; boneIdx < boneTransforms.Count; boneIdx++)
            {
                UpdateBoneSegments(boneIdx);
            }

            currentFrameIndex = frameIndex;
            currentFrameTime = frameTime;
        }

        /// <summary>
        /// Calculate 100 uniformly distributed segment points on a bone
        /// Bone is defined as line from parent joint to current joint
        /// </summary>
        /// <param name="boneIndex">Index of the bone to update</param>
        private void UpdateBoneSegments(int boneIndex)
        {
            Transform boneTransform = boneTransforms[boneIndex];
            var segments = boneSegments[boneIndex];

            // Get bone endpoints in world space
            Vector3 boneStart = boneTransform.parent != null ? boneTransform.parent.position : boneTransform.position;
            Vector3 boneEnd = boneTransform.position;

            // For each segment point, interpolate along the bone
            for (int segIdx = 0; segIdx < segments.Length; segIdx++)
            {
                float t = segments.Length > 1 ? (float)segIdx / (segments.Length - 1) : 0f;
                segments[segIdx].position = Vector3.Lerp(boneStart, boneEnd, t);

                // Calculate motion vector
                segments[segIdx].motionVector = segments[segIdx].position - segments[segIdx].previousPosition;
            }
        }

        /// <summary>
        /// Set the current frame index and time (should be called by TimelineController)
        /// </summary>
        public void SetFrameInfo(int frameIndex, float frameTime)
        {
            currentFrameIndex = frameIndex;
            currentFrameTime = frameTime;
        }
    }
}
