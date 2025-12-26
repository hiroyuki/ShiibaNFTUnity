using UnityEngine;

/// <summary>
/// Utility class for BVH joint operations
/// Provides shared functionality for finding and extracting joint information
/// </summary>
public static class BvhJointUtility
{
    /// <summary>
    /// Get Joint_torso_7 position as header comments for a specific BVH frame
    /// </summary>
    /// <param name="frameIndex">BVH frame index (used to get skeleton pose - for backward compatibility)</param>
    /// <returns>Array of comment strings for PLY header, or null if joint not found</returns>
    public static string[] GetJointPositionComments(int frameIndex)
    {
        // For backward compatibility, assume frameIndex is both pointcloud frame and BVH frame
        return GetJointPositionComments(frameIndex, frameIndex);
    }

    /// <summary>
    /// Get Joint_torso_7 position as header comments with separate point cloud and BVH frame numbers
    /// </summary>
    /// <param name="pointCloudFrame">Point cloud frame number (used in comment)</param>
    /// <param name="bvhFrame">BVH frame number (skeleton is already at this pose in the scene)</param>
    /// <returns>Array of comment strings for PLY header, or null if joint not found</returns>
    public static string[] GetJointPositionComments(int pointCloudFrame, int bvhFrame)
    {
        BvhData bvhData = BvhDataCache.GetBvhData();
        if (bvhData == null)
        {
            return null;
        }

        // Find the BVH_Character GameObject in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            Debug.LogWarning("[BvhJointUtility] BVH_Character GameObject not found in scene");
            return null;
        }

        // Find the root joint transform
        Transform rootJoint = bvhCharacter.transform.Find(bvhData.RootJoint.Name);
        if (rootJoint == null)
        {
            Debug.LogWarning($"[BvhJointUtility] Root joint '{bvhData.RootJoint.Name}' not found under BVH_Character");
            return null;
        }

        // Find torso_7 by recursively searching the hierarchy
        Transform torso7Joint = FindTransformRecursive(rootJoint, "torso_7");
        if (torso7Joint == null)
        {
            Debug.LogWarning("[BvhJointUtility] torso_7 not found in BVH hierarchy");
            return null;
        }

        // Get global/world position (this is what you need - not affected by parent transforms)
        Vector3 globalPosition = torso7Joint.position;

        // Log for verification
        Debug.Log($"[BvhJointUtility] PLY Frame {pointCloudFrame} (BVH Frame {bvhFrame}) - torso_7 global position: ({globalPosition.x:F6}, {globalPosition.y:F6}, {globalPosition.z:F6})");

        // Create comment lines with point cloud frame number and global position
        string[] comments = new string[]
        {
            $"PointCloudFrame: {pointCloudFrame}",
            $"BvhFrame: {bvhFrame}",
            $"torso_7_global_position: {globalPosition.x:F6} {globalPosition.y:F6} {globalPosition.z:F6}"
        };

        return comments;
    }

    /// <summary>
    /// Get position of a specific joint by name for a given frame
    /// </summary>
    /// <param name="jointName">Name of the joint to find</param>
    /// <param name="frameIndex">BVH frame index (for comment metadata)</param>
    /// <returns>Array of comment strings for PLY header, or null if joint not found</returns>
    public static string[] GetJointPositionComments(string jointName, int frameIndex)
    {
        BvhData bvhData = BvhDataCache.GetBvhData();
        if (bvhData == null)
        {
            return null;
        }

        // Find the BVH_Character GameObject in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            Debug.LogWarning($"[BvhJointUtility] BVH_Character GameObject not found in scene");
            return null;
        }

        // Find the root joint transform
        Transform rootJoint = bvhCharacter.transform.Find(bvhData.RootJoint.Name);
        if (rootJoint == null)
        {
            Debug.LogWarning($"[BvhJointUtility] Root joint '{bvhData.RootJoint.Name}' not found under BVH_Character");
            return null;
        }

        // Find the specified joint by name
        Transform joint = FindTransformRecursive(rootJoint, jointName);
        if (joint == null)
        {
            Debug.LogWarning($"[BvhJointUtility] Joint '{jointName}' not found in BVH hierarchy");
            return null;
        }

        // Get world position of the joint
        Vector3 worldPosition = joint.position;

        // Create comment lines
        string[] comments = new string[]
        {
            $"Frame: {frameIndex}",
            $"{jointName}_position: {worldPosition.x:F6} {worldPosition.y:F6} {worldPosition.z:F6}"
        };

        return comments;
    }

    /// <summary>
    /// Find a transform by name in the hierarchy (recursive search)
    /// </summary>
    /// <param name="parent">Parent transform to search from</param>
    /// <param name="name">Name of the transform to find</param>
    /// <returns>Found transform or null</returns>
    public static Transform FindTransformRecursive(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent;
        }

        foreach (Transform child in parent)
        {
            Transform found = FindTransformRecursive(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Get world position of a specific joint by name
    /// </summary>
    /// <param name="jointName">Name of the joint to find</param>
    /// <returns>World position of the joint, or Vector3.zero if not found</returns>
    public static Vector3 GetJointWorldPosition(string jointName)
    {
        BvhData bvhData = BvhDataCache.GetBvhData();
        if (bvhData == null)
        {
            return Vector3.zero;
        }

        // Find the BVH_Character GameObject in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            return Vector3.zero;
        }

        // Find the root joint transform
        Transform rootJoint = bvhCharacter.transform.Find(bvhData.RootJoint.Name);
        if (rootJoint == null)
        {
            return Vector3.zero;
        }

        // Find the specified joint
        Transform joint = FindTransformRecursive(rootJoint, jointName);
        if (joint == null)
        {
            return Vector3.zero;
        }

        return joint.position;
    }
}
