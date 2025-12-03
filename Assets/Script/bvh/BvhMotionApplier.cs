using UnityEngine;

/// <summary>
/// Applies BVH motion data to transform hierarchies with extensible position/rotation adjustment hooks.
///
/// This class provides the core logic for converting BVH motion capture data into transform hierarchies.
/// It can be instantiated directly for basic motion application, or subclassed to customize behavior.
///
/// Core Responsibilities:
/// - Read channel data from BVH frame arrays using BvhDataReader
/// - Recursively traverse joint hierarchy and apply motion transforms
/// - Provide extension points via virtual methods for custom adjustments (position/rotation)
///
/// Usage Examples:
///
/// 1. Direct usage (no adjustments):
///    var applier = new BvhMotionApplier();
///    applier.ApplyFrameToJointHierarchy(rootJoint, rootTransform, frameData);
///
/// 2. Custom adjustments via subclassing:
///    private class ScaledMotionApplier : BvhMotionApplier
///    {
///        protected override Vector3 AdjustPosition(Vector3 basePos, BvhJoint joint, bool isRoot)
///        {
///            return Vector3.Scale(basePos, scale);
///        }
///    }
///
/// Extension Points:
/// - AdjustPosition(): Override to customize position values (e.g., apply scale, root motion handling)
/// - AdjustRotation(): Override to customize rotation values (e.g., apply offset rotations)
/// </summary>
public class BvhMotionApplier
{
    /// <summary>
    /// Apply BVH frame data to a joint hierarchy by recursively updating transforms.
    ///
    /// This method:
    /// 1. Reads channel data using BvhDataReader
    /// 2. Calls AdjustPosition/AdjustRotation hooks for customization
    /// 3. Applies transforms to the joint hierarchy
    /// 4. Creates missing child transforms as needed (idempotent)
    /// </summary>
    /// <param name="rootJoint">Root joint of the BVH skeleton structure</param>
    /// <param name="rootTransform">Root transform in the scene to apply motion to</param>
    /// <param name="frameData">Frame data array containing channel values [pos_x, pos_y, pos_z, rot_x, rot_y, rot_z, ...]</param>
    public void ApplyFrameToJointHierarchy(BvhJoint rootJoint, Transform rootTransform, float[] frameData)
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
