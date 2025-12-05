using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a BVH joint/bone node in the skeleton hierarchy
/// </summary>
public class BvhJoint
{
    public string Name { get; set; }
    public Vector3 Offset { get; set; }
    public List<string> Channels { get; set; }
    public List<BvhJoint> Children { get; set; }
    public BvhJoint Parent { get; set; }
    public bool IsEndSite { get; set; }

    public BvhJoint()
    {
        Channels = new List<string>();
        Children = new List<BvhJoint>();
        IsEndSite = false;
    }

    /// <summary>
    /// Get the total number of channels for this joint and all its children
    /// </summary>
    public int GetTotalChannelCount()
    {
        int count = Channels.Count;
        foreach (var child in Children)
        {
            count += child.GetTotalChannelCount();
        }
        return count;
    }

    /// <summary>
    /// Get all joints in depth-first order
    /// </summary>
    public List<BvhJoint> GetAllJoints()
    {
        List<BvhJoint> joints = new List<BvhJoint>();
        if (!IsEndSite)
        {
            joints.Add(this);
        }
        foreach (var child in Children)
        {
            joints.AddRange(child.GetAllJoints());
        }
        return joints;
    }
}

/// <summary>
/// Represents the complete BVH data including hierarchy and motion data
/// </summary>
public class BvhData
{
    public BvhJoint RootJoint { get; set; }
    public int FrameCount { get; set; }
    public float FrameTime { get; set; }
    public float[][] Frames { get; set; }

    /// <summary>Root transform in the Scene hierarchy (optional, set via SetRootTransform)</summary>
    private Transform rootTransform;

    /// <summary>
    /// Get frames per second
    /// </summary>
    public float FrameRate => FrameTime > 0 ? 1f / FrameTime : 30f;

    /// <summary>
    /// Get total duration in seconds
    /// </summary>
    public float Duration => FrameCount * FrameTime;

    /// <summary>
    /// Set the root transform in the Scene hierarchy
    /// Required for UpdateTransforms() to work
    /// </summary>
    /// <param name="root">Root transform of the character in the scene</param>
    public void SetRootTransform(Transform root)
    {
        this.rootTransform = root;
    }

    /// <summary>
    /// Get the root transform (if set)
    /// </summary>
    public Transform GetRootTransform()
    {
        return rootTransform;
    }

    /// <summary>
    /// Get all joints in the hierarchy (excluding end sites)
    /// </summary>
    public List<BvhJoint> GetAllJoints()
    {
        if (RootJoint == null) return new List<BvhJoint>();
        return RootJoint.GetAllJoints();
    }


    /// <summary>
    /// Get motion data for a specific frame
    /// </summary>
    /// <param name="frameIndex">Frame index (0-based)</param>
    /// <returns>Array of channel values for this frame</returns>
    public float[] GetFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
        {
            Debug.LogWarning($"Frame index {frameIndex} out of range [0, {FrameCount - 1}]");
            return null;
        }
        return Frames[frameIndex];
    }

    /// <summary>
    /// Get interpolated motion data at a specific time
    /// </summary>
    /// <param name="time">Time in seconds</param>
    /// <returns>Interpolated channel values</returns>
    public float[] GetFrameAtTime(float time)
    {
        if (FrameCount == 0) return null;

        float frameFloat = time / FrameTime;
        int frame1 = Mathf.FloorToInt(frameFloat);
        int frame2 = Mathf.CeilToInt(frameFloat);

        // Clamp to valid range
        frame1 = Mathf.Clamp(frame1, 0, FrameCount - 1);
        frame2 = Mathf.Clamp(frame2, 0, FrameCount - 1);

        if (frame1 == frame2)
        {
            return Frames[frame1];
        }

        // Linear interpolation between frames
        float t = frameFloat - frame1;
        float[] result = new float[Frames[frame1].Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Mathf.Lerp(Frames[frame1][i], Frames[frame2][i], t);
        }
        return result;
    }

    /// <summary>
    /// Get summary information about the BVH data
    /// </summary>
    public string GetSummary()
    {
        var joints = GetAllJoints();
        int totalChannels = RootJoint?.GetTotalChannelCount() ?? 0;

        return $"BVH Data Summary:\n" +
               $"  Root: {RootJoint?.Name ?? "None"}\n" +
               $"  Joints: {joints.Count}\n" +
               $"  Channels: {totalChannels}\n" +
               $"  Frames: {FrameCount}\n" +
               $"  Frame Time: {FrameTime:F6}s ({FrameRate:F2} fps)\n" +
               $"  Duration: {Duration:F2}s";
    }

    /// <summary>
    /// Update the Scene transforms for a specific frame with optional adjustments
    /// Applies BVH frame data to the stored rootTransform
    /// </summary>
    /// <param name="frameNumber">Frame index to apply (0-based)</param>
    /// <param name="scale">Scale to apply to all joint positions (default: Vector3.one)</param>
    /// <param name="rotationOffset">Rotation offset to apply to root joint (default: Vector3.zero)</param>
    /// <param name="positionOffset">Position offset to apply to root joint (default: Vector3.zero)</param>
    public void UpdateTransforms(int frameNumber,
                                 Vector3 scale = default,
                                 Vector3 rotationOffset = default,
                                 Vector3 positionOffset = default)
    {
        if (rootTransform == null)
        {
            Debug.LogError("[BvhData] rootTransform not set. Call SetRootTransform() first.");
            return;
        }

        if (frameNumber < 0 || frameNumber >= FrameCount)
        {
            Debug.LogWarning($"[BvhData] Frame index {frameNumber} out of range [0, {FrameCount - 1}]");
            return;
        }

        float[] frameData = GetFrame(frameNumber);
        if (frameData == null)
            return;

        // Apply frame with adjustments
        Vector3 actualScale = scale == default ? Vector3.one : scale;
        ApplyFrameToTransforms(RootJoint, rootTransform, frameData, actualScale, rotationOffset, positionOffset);
    }

    /// <summary>
    /// Apply BVH frame data to a Transform hierarchy (no adjustments)
    /// </summary>
    /// <param name="rootJoint">Root joint of the BVH skeleton</param>
    /// <param name="rootTransform">Root transform to apply motion to</param>
    /// <param name="frameData">Frame data array (channel values)</param>
    public static void ApplyFrameToTransforms(BvhJoint rootJoint, Transform rootTransform, float[] frameData)
    {
        ApplyFrameToTransforms(rootJoint, rootTransform, frameData, Vector3.one, Vector3.zero, Vector3.zero);
    }

    /// <summary>
    /// Apply BVH frame data to a Transform hierarchy with optional adjustments
    /// </summary>
    /// <param name="rootJoint">Root joint of the BVH skeleton</param>
    /// <param name="rootTransform">Root transform to apply motion to</param>
    /// <param name="frameData">Frame data array (channel values)</param>
    /// <param name="scale">Scale to apply to joint positions</param>
    /// <param name="rotationOffset">Rotation offset for root joint</param>
    /// <param name="positionOffset">Position offset for root joint</param>
    public static void ApplyFrameToTransforms(BvhJoint rootJoint, Transform rootTransform, float[] frameData,
                                               Vector3 scale, Vector3 rotationOffset, Vector3 positionOffset)
    {
        int channelIndex = 0;
        ApplyJointRecursive(rootJoint, rootTransform, frameData, ref channelIndex, scale, rotationOffset, positionOffset, true);
    }

    /// <summary>
    /// Recursively apply motion data to joint hierarchy
    /// </summary>
    private static void ApplyJointRecursive(BvhJoint joint, Transform targetTransform, float[] frameData,
                                           ref int channelIndex, Vector3 scale, Vector3 rotationOffset, Vector3 positionOffset,
                                           bool isRoot)
    {
        if (joint.IsEndSite)
            return;

        Vector3 position = joint.Offset;
        Vector3 rotation = Vector3.zero;

        // Read channel data for this joint
        BvhDataReader.ReadChannelData(joint.Channels, frameData, ref channelIndex, ref position, ref rotation);

        // Apply adjustments
        position = AdjustPosition(position, joint, scale, isRoot);
        rotation = AdjustRotation(rotation, joint, rotationOffset, isRoot);

        // Apply position and rotation to this transform
        targetTransform.localPosition = position;
        targetTransform.localRotation = BvhDataReader.GetRotationQuaternion(rotation);

        // Recursively apply to children
        foreach (var childJoint in joint.Children)
        {
            if (childJoint.IsEndSite)
                continue;

            Transform childTransform = targetTransform.Find(childJoint.Name);
            if (childTransform != null)
            {
                ApplyJointRecursive(childJoint, childTransform, frameData, ref channelIndex, scale, rotationOffset, positionOffset, false);
            }
            else
            {
                // Create child transform if it doesn't exist
                GameObject childObj = new GameObject(childJoint.Name);
                childObj.transform.SetParent(targetTransform);
                childObj.transform.localPosition = childJoint.Offset;
                childObj.transform.localRotation = Quaternion.identity;

                ApplyJointRecursive(childJoint, childObj.transform, frameData, ref channelIndex, scale, rotationOffset, positionOffset, false);
            }
        }
    }

    /// <summary>
    /// Adjust position value before applying to transform
    /// </summary>
    private static Vector3 AdjustPosition(Vector3 basePosition, BvhJoint joint, Vector3 scale, bool isRoot)
    {
        // Apply scale to all joints
        return Vector3.Scale(basePosition, scale);
    }

    /// <summary>
    /// Adjust rotation value before applying to transform
    /// </summary>
    private static Vector3 AdjustRotation(Vector3 baseRotation, BvhJoint joint, Vector3 rotationOffset, bool isRoot)
    {
        // Apply rotation offset for root joint only
        if (isRoot)
        {
            return baseRotation + rotationOffset;
        }
        return baseRotation;
    }
}
