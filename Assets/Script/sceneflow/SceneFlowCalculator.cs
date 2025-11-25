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
        private bool debugMode = false;

        /// <summary>Maximum history depth for frame chain (default: 100 frames)</summary>
        [SerializeField]
        [Range(1, 1000)]
        [Tooltip("Number of historical frames to maintain in the linked-list chain")]
        private int historyFrameCount = 100;

        // Internal state - BVH data
        private BvhData bvhData;

        /// <summary>Reference to BvhPlayableBehaviour for frame-time mapping (set via Initialize)</summary>
        private BvhPlayableBehaviour bvhPlayableBehaviour;

        /// <summary>All bone transforms gathered from BVH hierarchy in depth-first order</summary>
        private List<Transform> boneTransforms = new List<Transform>();

        /// <summary>Historical frame entries built during CalculateSceneFlowForCurrentFrame()</summary>
        private List<FrameHistoryEntry> frameHistory = new List<FrameHistoryEntry>();

        /// <summary>Scene flow data for all points in current point cloud</summary>
        private List<PointSceneFlow> pointFlows = new List<PointSceneFlow>();

        // Frame tracking
        private int currentBvhFrameIndex = 0;
        private float currentFrameTime = 0f;

        /// <summary>
        /// Initialize the calculator with BVH data and character root transform
        /// </summary>
        /// <param name="bvhData">BVH animation data containing skeleton and motion frames</param>
        /// <param name="characterRoot">Root transform of the BVH character hierarchy</param>
        /// <param name="bvhPlayableBehaviour">Reference to BvhPlayableBehaviour for frame-time mapping</param>
        public void Initialize(BvhData bvhData, Transform characterRoot, BvhPlayableBehaviour bvhPlayableBehaviour = null)
        {
            this.bvhData = bvhData;
            this.bvhPlayableBehaviour = bvhPlayableBehaviour;

            // Gather all bone transforms from hierarchy
            boneTransforms.Clear();
            GatherBoneTransforms(characterRoot);

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Initialized with {boneTransforms.Count} bones");

            // Initialize frame history
            frameHistory.Clear();

            // Initialize point flows list
            pointFlows.Clear();

            if (debugMode && bvhPlayableBehaviour == null)
                Debug.LogWarning("[SceneFlowCalculator] BvhPlayableBehaviour not provided - frame-time mapping will not work");
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
        /// Get the current frame time
        /// </summary>
        public float GetCurrentFrameTime()
        {
            return currentFrameTime;
        }

        /// <summary>
        /// Manual trigger to calculate scene flow for the current frame.
        /// This backtracks through BVH history (historyFrameCount depth) and builds linked-list structure.
        /// Call this from a button or manually when you want to compute flow.
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

            if (bvhPlayableBehaviour == null)
            {
                Debug.LogError("[SceneFlowCalculator] BvhPlayableBehaviour not set. Cannot map timeline to BVH frames.");
                return;
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
        /// Delegates to BvhData.ApplyFrameToTransforms() for proper BVH handling.
        /// </summary>
        /// <param name="bvhFrame">BVH frame number to apply</param>
        private void ApplyBvhFrame(int bvhFrame)
        {
            if (bvhData == null || bvhFrame < 0 || bvhFrame >= bvhData.FrameCount)
                return;

            float[] frameData = bvhData.GetFrame(bvhFrame);
            if (frameData == null)
                return;

            // Delegate BVH frame application to BvhData (responsible for BVH-specific logic)
            Transform rootTransform = boneTransforms[0].parent != null ? boneTransforms[0].parent : boneTransforms[0];
            BvhData.ApplyFrameToTransforms(bvhData.RootJoint, rootTransform, frameData);
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
    }
}
