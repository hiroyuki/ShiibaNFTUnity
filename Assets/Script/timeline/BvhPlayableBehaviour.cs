using UnityEngine;
using UnityEngine.Playables;
using Assets.Script.sceneflow;
using System.Collections.Generic;

/// <summary>
/// Playable behaviour for controlling BVH motion playback in Unity Timeline
/// </summary>
public class BvhPlayableBehaviour : PlayableBehaviour
{
    public BvhData bvhData;
    public Transform targetTransform;
    public float frameRate = 30f;

    // Transform adjustment settings
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;
    public Vector3 scale = Vector3.one;
    public bool applyRootMotion = true;
    public int frameOffset = 0;

    // BVH Drift Correction
    public BvhDriftCorrectionData driftCorrectionData;

    private int currentFrame = -1;
    private Transform[] jointTransforms;
    private BvhJoint[] joints;

    /// <summary>
    /// 現在のBVHフレーム番号を取得（キーフレーム記録用）
    /// </summary>
    public int GetCurrentFrame() => currentFrame;

    public override void OnGraphStart(Playable playable)
    {
        if (bvhData != null && targetTransform != null)
        {
            // Save the initial position of BVH_Character as baseline for drift correction
            // This allows us to restore or reference the original position during correction
            targetTransform.localPosition = positionOffset;
            Debug.Log($"[BvhPlayableBehaviour] OnGraphStart: Saved BVH_Character initial position as positionOffset: {positionOffset}");

            // Cache joint hierarchy
            var jointList = bvhData.GetAllJoints();
            joints = jointList.ToArray();
            jointTransforms = new Transform[joints.Length];

            // Create or find transforms for all joints
            CreateJointHierarchy();

            // Notify SceneFlowCalculator that BVH data is now available
            NotifySceneFlowCalculator();
        }
    }

    /// <summary>
    /// Notify SceneFlowCalculator that BVH data has been loaded and is ready
    /// </summary>
    private void NotifySceneFlowCalculator()
    {
        SceneFlowCalculator calculator = GameObject.FindFirstObjectByType<SceneFlowCalculator>();
        if (calculator != null && bvhData != null)
        {
            calculator.SetBvhData(bvhData, this);
            Debug.Log("[BvhPlayableBehaviour] Notified SceneFlowCalculator of BVH data availability");
        }
    }

    public override void OnGraphStop(Playable playable)
    {
        currentFrame = -1;
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        if (bvhData == null || targetTransform == null) return;

        double currentTime = playable.GetTime();
        int targetFrame = GetTargetFrameForTime((float)currentTime);

        // Only update if frame changed
        if (targetFrame != currentFrame)
        {
            ApplyFrame(targetFrame);
            currentFrame = targetFrame;
        }

        // Apply drift correction if enabled
        ApplyDriftCorrection((float)currentTime);
    }

    /// <summary>
    /// Get the target BVH frame for the given timeline time, including offset and clamping
    /// </summary>
    private int GetTargetFrameForTime(float timelineTime)
    {
        int targetFrame = CalculateTargetFrame(timelineTime, bvhData.FrameRate);
        targetFrame += frameOffset; // Apply frame offset for synchronization with point cloud
        return Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1); // Clamp to valid range
    }

    /// <summary>
    /// Apply BVH drift correction based on keyframe interpolation
    /// Uses positionOffset (saved initial BVH_Character position) as the baseline reference
    /// Applies both position and rotation corrections from keyframe data
    /// </summary>
    private void ApplyDriftCorrection(float timelineTime)
    {
        if (driftCorrectionData == null || !driftCorrectionData.IsEnabled)
            return;

        if (bvhData == null || targetTransform == null)
            return;

        // Get target anchor position and rotation from keyframe interpolation
        Vector3 targetAnchorPositionRelative = driftCorrectionData.GetAnchorPositionAtTime(timelineTime);
        Vector3 targetAnchorRotationRelative = driftCorrectionData.GetAnchorRotationAtTime(timelineTime);

        // Find root joint transform
        Transform rootJointTransform = targetTransform.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
            return;

        // Calculate the correction delta (difference between target and current relative position)
        Vector3 currentRelativePosition = rootJointTransform.localPosition;

        // Apply position correction: baseline position (positionOffset) + anchor correction
        // This preserves the initial BVH_Character position while applying drift correction
        targetTransform.localPosition = positionOffset + targetAnchorPositionRelative;

        // Apply rotation correction: baseline rotation + anchor rotation correction
        // Convert both euler angles to quaternions and combine them
        Quaternion baseRotation = Quaternion.Euler(rotationOffset);
        Quaternion rotationCorrection = Quaternion.Euler(targetAnchorRotationRelative);
        targetTransform.localRotation = baseRotation * rotationCorrection;
    }

    /// <summary>
    /// Calculate target frame using keyframe-based mapping if available, otherwise use linear mapping
    ///
    /// When drift correction keyframes are available, uses them to define Timeline-to-BVH-frame mapping:
    /// - Finds keyframes before and after current Timeline time
    /// - Interpolates bvhFrameNumber between keyframes
    /// - This allows speed adjustment by modifying bvhFrameNumber values in keyframes
    ///
    /// Falls back to linear mapping if no keyframes are available.
    /// </summary>
    private int CalculateTargetFrame(float currentTime, float bvhFrameRate)
    {
        if (driftCorrectionData == null || driftCorrectionData.GetKeyframeCount() == 0)
        {
            // Fall back to linear mapping when no keyframes available
            return Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
        }

        FindSurroundingKeyframes(currentTime, out BvhKeyframe prevKeyframe, out BvhKeyframe nextKeyframe);
        return InterpolateFrameNumber(currentTime, prevKeyframe, nextKeyframe, bvhFrameRate);
    }

    /// <summary>
    /// Find keyframes that surround the given time
    /// </summary>
    private void FindSurroundingKeyframes(float currentTime, out BvhKeyframe prevKeyframe, out BvhKeyframe nextKeyframe)
    {
        prevKeyframe = null;
        nextKeyframe = null;

        var keyframes = driftCorrectionData.GetAllKeyframes();
        foreach (var kf in keyframes)
        {
            if (kf.timelineTime <= currentTime)
                prevKeyframe = kf;
            else if (nextKeyframe == null)
                nextKeyframe = kf;
        }
    }

    /// <summary>
    /// Interpolate BVH frame number based on surrounding keyframes
    /// </summary>
    private int InterpolateFrameNumber(float currentTime, BvhKeyframe prevKeyframe, BvhKeyframe nextKeyframe, float bvhFrameRate)
    {
        // Case 1: Between two keyframes - interpolate frame number
        if (prevKeyframe != null && nextKeyframe != null)
        {
            float timeDelta = nextKeyframe.timelineTime - prevKeyframe.timelineTime;
            if (timeDelta > 0)
            {
                float frameDelta = nextKeyframe.bvhFrameNumber - prevKeyframe.bvhFrameNumber;
                float t = (currentTime - prevKeyframe.timelineTime) / timeDelta;
                t = Mathf.Clamp01(t);
                return Mathf.FloorToInt(prevKeyframe.bvhFrameNumber + (frameDelta * t));
            }
            return prevKeyframe.bvhFrameNumber;
        }

        // Case 2: Before first keyframe - interpolate from (0s, frame 0) to first keyframe
        if (prevKeyframe == null && nextKeyframe != null)
        {
            float timeDelta = nextKeyframe.timelineTime;
            if (timeDelta > 0)
            {
                float frameDelta = nextKeyframe.bvhFrameNumber;
                float t = currentTime / timeDelta;
                t = Mathf.Clamp01(t);
                return Mathf.FloorToInt(frameDelta * t);
            }
            return 0;
        }

        // Case 3: After last keyframe - extrapolate using BVH file's native frame rate
        if (prevKeyframe != null && nextKeyframe == null)
        {
            float timeSincePrevKeyframe = currentTime - prevKeyframe.timelineTime;
            float additionalFrames = timeSincePrevKeyframe * bvhFrameRate;
            return prevKeyframe.bvhFrameNumber + Mathf.FloorToInt(additionalFrames);
        }

        // Case 4: No surrounding keyframes (shouldn't happen if keyframes exist)
        return Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
    }

    /// <summary>
    /// Apply BVH frame data to the transform hierarchy
    /// </summary>
    private void ApplyFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= bvhData.FrameCount) return;

        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData == null) return;

        // Find the root joint transform (child of targetTransform)
        Transform rootJointTransform = targetTransform.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
        {
            Debug.LogWarning($"Root joint '{bvhData.RootJoint.Name}' not found under '{targetTransform.name}'");
            return;
        }

        var applier = new PlayableFrameApplier(scale, rotationOffset, applyRootMotion);
        applier.ApplyFrame(bvhData.RootJoint, rootJointTransform, frameData);
    }

    /// <summary>
    /// Custom frame applier for BvhPlayableBehaviour with position/rotation adjustments
    /// </summary>
    private class PlayableFrameApplier : BvhFrameApplier
    {
        private Vector3 scale;
        private Vector3 rotationOffset;
        private bool applyRootMotion;
        private bool hasPositionChannels;

        public PlayableFrameApplier(Vector3 scale, Vector3 rotationOffset, bool applyRootMotion)
        {
            this.scale = scale;
            this.rotationOffset = rotationOffset;
            this.applyRootMotion = applyRootMotion;
        }

        protected override Vector3 AdjustPosition(Vector3 basePosition, BvhJoint joint, bool isRoot)
        {
            if (!isRoot)
            {
                // Non-root joints: scale position
                return Vector3.Scale(basePosition, scale);
            }

            // Root joint adjustments
            hasPositionChannels = BvhChannelReader.HasPositionChannels(joint.Channels);

            if (applyRootMotion && hasPositionChannels)
            {
                // Apply position with scale
                return Vector3.Scale(basePosition, scale);
            }
            else if (!hasPositionChannels)
            {
                // Use offset position only
                return joint.Offset;
            }

            return basePosition;
        }

        protected override Vector3 AdjustRotation(Vector3 baseRotation, BvhJoint joint, bool isRoot)
        {
            // Apply rotation offset for root joint only
            if (isRoot)
            {
                return baseRotation + rotationOffset;
            }
            return baseRotation;
        }
    }

    /// <summary>
    /// Create joint hierarchy GameObjects if they don't exist
    /// </summary>
    private void CreateJointHierarchy()
    {
        if (bvhData == null || bvhData.RootJoint == null) return;

        // Create the root joint as a child of targetTransform
        Transform rootJointTransform = targetTransform.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
        {
            GameObject rootJointObj = new GameObject(bvhData.RootJoint.Name);
            rootJointTransform = rootJointObj.transform;
            rootJointTransform.SetParent(targetTransform);
            rootJointTransform.localPosition = bvhData.RootJoint.Offset;
            rootJointTransform.localRotation = Quaternion.identity;
            rootJointTransform.localScale = Vector3.one;

            Debug.Log($"Created root joint '{bvhData.RootJoint.Name}' as child of '{targetTransform.name}'");
        }

        // Create children of root joint
        foreach (var childJoint in bvhData.RootJoint.Children)
        {
            CreateJointRecursive(childJoint, rootJointTransform);
        }
    }

    /// <summary>
    /// Recursively create joint GameObjects
    /// </summary>
    private void CreateJointRecursive(BvhJoint joint, Transform parent)
    {
        if (joint.IsEndSite) return;

        Transform jointTransform = parent.Find(joint.Name);
        if (jointTransform == null)
        {
            GameObject jointObj = new GameObject(joint.Name);
            jointTransform = jointObj.transform;
            jointTransform.SetParent(parent);
            jointTransform.localPosition = joint.Offset;
            jointTransform.localRotation = Quaternion.identity;
            jointTransform.localScale = Vector3.one;
        }

        // Create children
        foreach (var childJoint in joint.Children)
        {
            CreateJointRecursive(childJoint, jointTransform);
        }
    }
}
