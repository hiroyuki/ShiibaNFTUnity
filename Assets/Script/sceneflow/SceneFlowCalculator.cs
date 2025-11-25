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
        private bool debugMode = true;

        /// <summary>Maximum history depth for frame chain (default: 100 frames)</summary>
        [SerializeField]
        [Range(1, 1000)]
        [Tooltip("Number of historical frames to maintain in the linked-list chain")]
        private int historyFrameCount = 30;

        [Header("Scene References")]

        /// <summary>Root transform of the BVH character hierarchy</summary>
        [SerializeField]
        [Tooltip("Root transform of the BVH character in the scene")]
        private Transform characterRoot;

        /// <summary>Cached BVH data obtained from BvhPlayableBehaviour on Timeline</summary>
        private BvhData bvhData;

        /// <summary>Cached reference to BvhPlayableBehaviour for Timeline synchronization</summary>
        private BvhPlayableBehaviour bvhPlayableBehaviour;

        /// <summary>All bone transforms gathered from BVH hierarchy in depth-first order</summary>
        private readonly List<Transform> boneTransforms = new();

        /// <summary>Historical frame entries built during CalculateSceneFlowForCurrentFrame()</summary>
        private readonly List<FrameHistoryEntry> frameHistory = new();

        /// <summary>Scene flow data for all points in current point cloud</summary>
        private readonly List<PointSceneFlow> pointFlows = new();

        // Frame tracking
        private int currentBvhFrameIndex = 0;
        private float currentFrameTime = 0f;

        /// <summary>
        /// Setup flag to track if bones have been gathered
        /// </summary>
        private bool isSetup = false;

        /// <summary>
        /// Setup bone transforms from the character hierarchy (one-time initialization)
        /// </summary>
        private void SetupBoneTransforms()
        {
            if (isSetup)
                return;

            if (characterRoot == null)
            {
                Debug.LogError("[SceneFlowCalculator] Character root not assigned in Inspector.");
                return;
            }

            // Set root transform in BvhData
            if (bvhData != null)
                bvhData.SetRootTransform(characterRoot);

            // Gather bone transforms from Scene hierarchy
            boneTransforms.Clear();
            GatherBoneTransforms(characterRoot);

            isSetup = true;

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Setup complete with {boneTransforms.Count} bones");
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
        /// Requires SetBvhData() to be called first to set up the BVH data source.
        /// </summary>
        public void OnShowSceneFlow()
        {
            // Validate prerequisites
            if (bvhData == null)
            {
                Debug.LogError("[SceneFlowCalculator] BvhData not set. Call SetBvhData() first, or ensure BvhPlayableBehaviour is on Timeline.");
                return;
            }

            if (characterRoot == null)
            {
                Debug.LogError("[SceneFlowCalculator] Character root not assigned in Inspector.");
                return;
            }

            // One-time setup of bone transforms
            SetupBoneTransforms();

            if (boneTransforms.Count == 0)
            {
                Debug.LogError("[SceneFlowCalculator] No bone transforms found in character hierarchy.");
                return;
            }

            // Apply current BVH frame to scene transforms
            bvhData.UpdateTransforms(currentBvhFrameIndex);

            // Calculate scene flow for current frame (includes frame history backtracking)
            CalculateSceneFlowForCurrentFrame();

            if (debugMode)
                Debug.Log($"[SceneFlowCalculator] Scene flow calculated for frame {currentBvhFrameIndex}");
        }

        /// <summary>
        /// Recursively gather all bone Transform components from the Scene hierarchy
        /// </summary>
        /// <param name="root">Root transform to start gathering from</param>
        private void GatherBoneTransforms(Transform root)
        {
            // Gather all children (root itself is not a bone, just the origin point)
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                boneTransforms.Add(child);
                GatherBoneTransforms(child);
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
        /// Displays segment points and motion vectors
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!debugMode || frameHistory.Count == 0)
                return;

            DrawDebugVisualization();
        }
    }
}
