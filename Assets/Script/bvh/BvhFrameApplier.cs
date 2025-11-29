using UnityEngine;

/// <summary>
/// Abstract base class for applying BVH frame data to transform hierarchies
/// Provides common frame application logic while allowing subclasses to customize position/rotation adjustments
/// This eliminates code duplication between BvhData and BvhPlayableBehaviour
/// </summary>
public abstract class BvhFrameApplier
{
    /// <summary>
    /// Apply BVH frame data to a transform hierarchy
    /// </summary>
    /// <param name="rootJoint">Root joint of the BVH skeleton</param>
    /// <param name="rootTransform">Root transform to apply motion to</param>
    /// <param name="frameData">Frame data array (channel values)</param>
    public void ApplyFrame(BvhJoint rootJoint, Transform rootTransform, float[] frameData)
    {
        if (rootJoint == null || rootTransform == null || frameData == null)
            return;

        int channelIndex = 0;
        ApplyJointRecursive(rootJoint, rootTransform, frameData, ref channelIndex, true);
    }

    /// <summary>
    /// Recursively apply motion data to joint hierarchy
    /// </summary>
    private void ApplyJointRecursive(BvhJoint joint, Transform targetTransform, float[] frameData, ref int channelIndex, bool isRoot)
    {
        if (joint.IsEndSite)
            return;

        Vector3 position = joint.Offset;
        Vector3 rotation = Vector3.zero;

        // Read channel data for this joint
        BvhDataReader.ReadChannelData(joint.Channels, frameData, ref channelIndex, ref position, ref rotation);

        // Allow subclasses to customize adjustments
        position = AdjustPosition(position, joint, isRoot);
        rotation = AdjustRotation(rotation, joint, isRoot);

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
                ApplyJointRecursive(childJoint, childTransform, frameData, ref channelIndex, false);
            }
            else
            {
                // Create child transform if it doesn't exist
                GameObject childObj = new GameObject(childJoint.Name);
                childObj.transform.SetParent(targetTransform);
                childObj.transform.localPosition = childJoint.Offset;
                childObj.transform.localRotation = Quaternion.identity;

                ApplyJointRecursive(childJoint, childObj.transform, frameData, ref channelIndex, false);
            }
        }
    }

    /// <summary>
    /// Adjust position value (called for each joint before applying)
    /// Override in subclasses to apply custom adjustments like scale or root motion
    /// </summary>
    /// <param name="basePosition">Position from BVH data or joint offset</param>
    /// <param name="joint">The BVH joint being processed</param>
    /// <param name="isRoot">True if this is the root joint</param>
    /// <returns>Adjusted position to apply</returns>
    protected virtual Vector3 AdjustPosition(Vector3 basePosition, BvhJoint joint, bool isRoot)
    {
        return basePosition;
    }

    /// <summary>
    /// Adjust rotation value (called for each joint before applying)
    /// Override in subclasses to apply custom adjustments like offset rotations
    /// </summary>
    /// <param name="baseRotation">Rotation from BVH data (euler angles)</param>
    /// <param name="joint">The BVH joint being processed</param>
    /// <param name="isRoot">True if this is the root joint</param>
    /// <returns>Adjusted rotation to apply</returns>
    protected virtual Vector3 AdjustRotation(Vector3 baseRotation, BvhJoint joint, bool isRoot)
    {
        return baseRotation;
    }
}
