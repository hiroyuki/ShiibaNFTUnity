using UnityEngine;
using UnityEngine.Playables;
using System.Collections.Generic;
using ShiibaNFT.BVH;

/// <summary>
/// Playable behaviour for controlling BVH motion playback in Unity Timeline
/// Thin adapter that uses BvhPlaybackFrameMapper and BvhPlaybackTransformCorrector for data transformation logic
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

    // BVH Drift Correction
    public BvhPlaybackCorrectionKeyframes driftCorrectionData;

    private int currentFrame = -1;
    private BvhJoint[] joints;

    // Timeline-independent utilities
    private BvhPlaybackFrameMapper frameMapper = new BvhPlaybackFrameMapper();

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
            // Update transform settings from DatasetConfig first
            UpdateTransformSettingsFromConfig();

            // Save the initial position of BVH_Character as baseline for drift correction
            // This allows us to restore or reference the original position during correction
            BvhCharacterTransform.localPosition = positionOffset;
            Debug.Log($"[BvhPlayableBehaviour] OnGraphStart: Saved BVH_Character initial position as positionOffset: {positionOffset}");

            // Cache joint hierarchy
            var jointList = bvhData.GetAllJoints();
            joints = jointList.ToArray();

            // Create or find transforms for all joints using the utility builder
            BvhJointHierarchyBuilder.CreateOrGetJointHierarchy(bvhData, BvhCharacterTransform);
        }
    }

    public override void OnGraphStop(Playable playable)
    {
        currentFrame = -1;
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        if (bvhData == null || BvhCharacterTransform == null) return;

        // Update transform settings from DatasetConfig in real-time
        UpdateTransformSettingsFromConfig();

        float timelineTime = (float)playable.GetTime();

        // Use BvhPlaybackFrameMapper to calculate target frame (handles keyframe interpolation)
        int targetFrame = frameMapper.GetTargetFrameForTime(timelineTime, bvhData, driftCorrectionData);

        // Only update if frame changed
        if (targetFrame != currentFrame)
        {
            ApplyFrame(targetFrame);
            currentFrame = targetFrame;
        }

        // Apply drift correction using BvhPlaybackTransformCorrector
        Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(timelineTime, driftCorrectionData, positionOffset);
        Quaternion correctedRot = BvhPlaybackTransformCorrector.GetCorrectedRootRotation(timelineTime, driftCorrectionData, rotationOffset);
        BvhCharacterTransform.SetLocalPositionAndRotation(correctedPos, correctedRot);
        BvhCharacterTransform.localScale = scale;
    }

    /// <summary>
    /// Update transform settings from DatasetConfig in real-time
    /// </summary>
    private void UpdateTransformSettingsFromConfig()
    {
        var config = DatasetConfig.GetInstance();
        if (config != null)
        {
            positionOffset = config.BvhPositionOffset;
            rotationOffset = config.BvhRotationOffset;
            scale = config.BvhScale;

            Debug.Log($"[BvhPlayableBehaviour] UpdateTransformSettings: pos={positionOffset}, rot={rotationOffset}, scale={scale}");
        }
    }

    /// <summary>
    /// Apply BVH frame data to the transform hierarchy
    /// </summary>
    private void ApplyFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= bvhData.FrameCount) return;

        // Set root transform on BvhData and apply frame (scale/offset applied in PrepareFrame)
        Transform rootTransform = BvhCharacterTransform.Find(bvhData.RootJoint.Name);
        bvhData.ApplyFrameToTransforms(frameIndex, rootTransform);
    }
}
