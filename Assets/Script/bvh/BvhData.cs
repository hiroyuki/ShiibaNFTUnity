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

    /// <summary>
    /// Get frames per second
    /// </summary>
    public float FrameRate => FrameTime > 0 ? 1f / FrameTime : 30f;

    /// <summary>
    /// Get total duration in seconds
    /// </summary>
    public float Duration => FrameCount * FrameTime;

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
    /// Apply BVH frame data to a Transform hierarchy
    /// Static utility method for applying motion capture data to bones
    /// </summary>
    /// <param name="rootJoint">Root joint of the BVH skeleton</param>
    /// <param name="rootTransform">Root transform to apply motion to</param>
    /// <param name="frameData">Frame data array (channel values)</param>
    public static void ApplyFrameToTransforms(BvhJoint rootJoint, Transform rootTransform, float[] frameData)
    {
        if (rootJoint == null || rootTransform == null || frameData == null)
            return;

        int channelIndex = 0;
        ApplyJointMotionRecursive(rootJoint, rootTransform, frameData, ref channelIndex);
    }

    /// <summary>
    /// Recursively apply motion data to joint hierarchy
    /// </summary>
    private static void ApplyJointMotionRecursive(BvhJoint joint, Transform targetTransform, float[] frameData, ref int channelIndex)
    {
        if (joint.IsEndSite)
            return;

        Vector3 position = joint.Offset;
        Vector3 rotation = Vector3.zero;

        // Read channel data for this joint
        foreach (string channel in joint.Channels)
        {
            if (channelIndex >= frameData.Length)
                break;

            float value = frameData[channelIndex];
            channelIndex++;

            switch (channel.ToUpper())
            {
                case "XPOSITION":
                    position.x = value;
                    break;
                case "YPOSITION":
                    position.y = value;
                    break;
                case "ZPOSITION":
                    position.z = value;
                    break;
                case "XROTATION":
                    rotation.x = value;
                    break;
                case "YROTATION":
                    rotation.y = value;
                    break;
                case "ZROTATION":
                    rotation.z = value;
                    break;
            }
        }

        // Apply position and rotation to this transform
        targetTransform.localPosition = position;
        targetTransform.localRotation = Quaternion.Euler(rotation);

        // Recursively apply to children
        foreach (var childJoint in joint.Children)
        {
            if (childJoint.IsEndSite)
                continue;

            Transform childTransform = targetTransform.Find(childJoint.Name);
            if (childTransform != null)
            {
                ApplyJointMotionRecursive(childJoint, childTransform, frameData, ref channelIndex);
            }
            else
            {
                // Create child transform if it doesn't exist
                GameObject childObj = new GameObject(childJoint.Name);
                childObj.transform.SetParent(targetTransform);
                childObj.transform.localPosition = childJoint.Offset;
                childObj.transform.localRotation = Quaternion.identity;

                ApplyJointMotionRecursive(childJoint, childObj.transform, frameData, ref channelIndex);
            }
        }
    }
}
