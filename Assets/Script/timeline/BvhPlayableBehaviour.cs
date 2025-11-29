using UnityEngine;
using UnityEngine.Playables;
using System.Collections.Generic;

/// <summary>
/// Playable behaviour for controlling BVH motion playback in Unity Timeline
/// Thin adapter that uses BvhFrameMapper and BvhDriftCorrectionController for data transformation logic
/// Focus: Timeline lifecycle and scene transform updates only
/// </summary>
public class BvhPlayableBehaviour : PlayableBehaviour
{
    public BvhData bvhData;
    public Transform BvhCharacterTransform;
    public float frameRate = 30f;

    // Transform adjustment settings
    private Vector3 positionOffset = Vector3.zero;
    private Vector3 rotationOffset = Vector3.zero;
    public Vector3 scale = Vector3.one;
    public bool applyRootMotion = true;
    public int frameOffset = 0;

    // BVH Drift Correction
    public BvhDriftCorrectionData driftCorrectionData;

    private int currentFrame = -1;
    private BvhJoint[] joints;

    // Timeline-independent utilities
    private BvhFrameMapper frameMapper = new BvhFrameMapper();
    private BvhDriftCorrectionController driftController = new BvhDriftCorrectionController();

    public Vector3 RotationOffset { set => rotationOffset = value; }
    public Vector3 PositionOffset { set => positionOffset = value; }

    /// <summary>
    /// 現在のBVHフレーム番号を取得（キーフレーム記録用）
    /// </summary>
    public int GetCurrentFrame() => currentFrame;

    public override void OnGraphStart(Playable playable)
    {
        if (bvhData != null && BvhCharacterTransform != null)
        {
            // Save the initial position of BVH_Character as baseline for drift correction
            // This allows us to restore or reference the original position during correction
            BvhCharacterTransform.localPosition = positionOffset;
            Debug.Log($"[BvhPlayableBehaviour] OnGraphStart: Saved BVH_Character initial position as positionOffset: {positionOffset}");

            // Cache joint hierarchy
            var jointList = bvhData.GetAllJoints();
            joints = jointList.ToArray();

            // Create or find transforms for all joints
            CreateJointHierarchy();
        }
    }

    public override void OnGraphStop(Playable playable)
    {
        currentFrame = -1;
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        if (bvhData == null || BvhCharacterTransform == null) return;

        float timelineTime = (float)playable.GetTime();

        // Use BvhFrameMapper to calculate target frame (handles keyframe interpolation)
        int targetFrame = frameMapper.GetTargetFrameForTime(timelineTime, bvhData, driftCorrectionData, frameOffset);

        // Only update if frame changed
        if (targetFrame != currentFrame)
        {
            ApplyFrame(targetFrame);
            currentFrame = targetFrame;
        }

        // Apply drift correction using BvhDriftCorrectionController
        Vector3 correctedPos = driftController.GetCorrectedRootPosition(timelineTime, driftCorrectionData, positionOffset);
        Quaternion correctedRot = driftController.GetCorrectedRootRotation(timelineTime, driftCorrectionData, rotationOffset);
        BvhCharacterTransform.SetLocalPositionAndRotation(correctedPos, correctedRot);
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
        Transform rootJointTransform = BvhCharacterTransform.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
        {
            Debug.LogWarning($"Root joint '{bvhData.RootJoint.Name}' not found under '{BvhCharacterTransform.name}'");
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
            hasPositionChannels = BvhDataReader.HasPositionChannels(joint.Channels);

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
        Transform rootJointTransform = BvhCharacterTransform.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
        {
            GameObject rootJointObj = new GameObject(bvhData.RootJoint.Name);
            rootJointTransform = rootJointObj.transform;
            rootJointTransform.SetParent(BvhCharacterTransform);
            rootJointTransform.localPosition = bvhData.RootJoint.Offset;
            rootJointTransform.localRotation = Quaternion.identity;
            rootJointTransform.localScale = Vector3.one;

            Debug.Log($"Created root joint '{bvhData.RootJoint.Name}' as child of '{BvhCharacterTransform.name}'");
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
