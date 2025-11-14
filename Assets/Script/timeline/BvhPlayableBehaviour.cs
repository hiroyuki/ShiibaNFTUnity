using UnityEngine;
using UnityEngine.Playables;

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

    public override void OnGraphStart(Playable playable)
    {
        if (bvhData != null && targetTransform != null)
        {
            // Save the initial position of BVH_Character as baseline for drift correction
            // This allows us to restore or reference the original position during correction
            positionOffset = targetTransform.localPosition;
            Debug.Log($"[BvhPlayableBehaviour] OnGraphStart: Saved BVH_Character initial position as positionOffset: {positionOffset}");

            // Cache joint hierarchy
            var jointList = bvhData.GetAllJoints();
            joints = jointList.ToArray();
            jointTransforms = new Transform[joints.Length];

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
        if (bvhData == null || targetTransform == null) return;

        // Get current time from timeline
        double currentTime = playable.GetTime();

        // Calculate frame based on BVH's frame rate
        float bvhFrameRate = bvhData.FrameRate;
        int targetFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate));

        // Apply frame offset for synchronization with point cloud
        targetFrame += frameOffset;

        // Clamp to valid range
        targetFrame = Mathf.Clamp(targetFrame, 0, bvhData.FrameCount - 1);

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
    /// Apply BVH drift correction based on keyframe interpolation
    /// Uses positionOffset (saved initial BVH_Character position) as the baseline reference
    /// </summary>
    private void ApplyDriftCorrection(float timelineTime)
    {
        if (driftCorrectionData == null || !driftCorrectionData.IsEnabled)
            return;

        if (bvhData == null || targetTransform == null)
            return;

        // Get target anchor position from keyframe interpolation
        Vector3 targetAnchorPositionRelative = driftCorrectionData.GetAnchorPositionAtTime(timelineTime);

        // Find root joint transform
        Transform rootJointTransform = targetTransform.Find(bvhData.RootJoint.Name);
        if (rootJointTransform == null)
            return;

        // Calculate the correction delta (difference between target and current relative position)
        Vector3 currentRelativePosition = rootJointTransform.localPosition;

        // DEBUG LOG
        Debug.Log($"[DriftCorrection] Time: {timelineTime:F2}s, " +
                  $"Target: {targetAnchorPositionRelative}, " +
                  $"Current: {currentRelativePosition}, " +
                  $"BaselinePos (positionOffset): {positionOffset}, " +
                  $"ParentPos Before: {targetTransform.localPosition}");

        // Apply correction: baseline position (positionOffset) + anchor correction
        // This preserves the initial BVH_Character position while applying drift correction
        targetTransform.localPosition = positionOffset + targetAnchorPositionRelative;

        Debug.Log($"[DriftCorrection] ParentPos After: {targetTransform.localPosition}");
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

        int channelIndex = 0;
        ApplyJointTransform(bvhData.RootJoint, rootJointTransform, frameData, ref channelIndex, true);
    }

    /// <summary>
    /// Recursively apply joint transforms with adjustments
    /// </summary>
    private void ApplyJointTransform(BvhJoint joint, Transform transform, float[] frameData, ref int channelIndex, bool isRoot)
    {
        if (joint.IsEndSite) return;
        if (transform == null) return;

        Vector3 position = joint.Offset;
        float rotX = 0, rotY = 0, rotZ = 0;
        bool hasPosition = false;

        // Read channel data
        foreach (string channel in joint.Channels)
        {
            if (channelIndex >= frameData.Length) break;

            float value = frameData[channelIndex];
            channelIndex++;

            switch (channel.ToUpper())
            {
                case "XPOSITION":
                    position.x = value;
                    hasPosition = true;
                    break;
                case "YPOSITION":
                    position.y = value;
                    hasPosition = true;
                    break;
                case "ZPOSITION":
                    position.z = value;
                    hasPosition = true;
                    break;
                case "XROTATION":
                    rotX = value;
                    break;
                case "YROTATION":
                    rotY = value;
                    break;
                case "ZROTATION":
                    rotZ = value;
                    break;
            }
        }

        // Apply adjustments for root joint
        if (isRoot)
        {
            if (applyRootMotion && hasPosition)
            {
                // Apply position with scale
                position = Vector3.Scale(position, scale);
            }
            else if (!hasPosition)
            {
                // Use offset position only
                position = joint.Offset;
            }

            // Apply rotation offset
            rotX += rotationOffset.x;
            rotY += rotationOffset.y;
            rotZ += rotationOffset.z;
        }
        else
        {
            // Non-root joints: scale position
            if (hasPosition)
            {
                position = Vector3.Scale(position, scale);
            }
        }

        // Apply position
        transform.localPosition = position;

        // Apply rotation in ZXY order (mocopi uses Zrotation Xrotation Yrotation)
        // Unity uses left-handed coordinates, so we need to adjust
        Quaternion qZ = Quaternion.AngleAxis(rotZ, Vector3.forward);
        Quaternion qX = Quaternion.AngleAxis(rotX, Vector3.right);
        Quaternion qY = Quaternion.AngleAxis(rotY, Vector3.up);

        transform.localRotation = qZ * qX * qY;

        // Apply to children
        foreach (var childJoint in joint.Children)
        {
            if (childJoint.IsEndSite) continue;

            Transform childTransform = transform.Find(childJoint.Name);
            if (childTransform != null)
            {
                ApplyJointTransform(childJoint, childTransform, frameData, ref channelIndex, false);
            }
            else
            {
                // Skip channels if transform not found
                int skipChannels = childJoint.GetTotalChannelCount();
                channelIndex += skipChannels;
            }
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
