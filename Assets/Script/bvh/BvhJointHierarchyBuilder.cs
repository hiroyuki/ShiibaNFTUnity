using UnityEngine;

namespace ShiibaNFT.BVH
{
    /// <summary>
    /// Utility class for creating and managing BVH joint hierarchies.
    /// 
    /// Provides static methods to create GameObject hierarchies from BvhData structures.
    /// The hierarchy creation is frame-agnostic - the same hierarchy can serve all animation frames,
    /// with frame-specific data applied separately via BvhMotionApplier.
    /// 
    /// Key Features:
    /// - Idempotent: Safe to call multiple times without duplicating GameObjects
    /// - Frame-agnostic: Creates structure only, doesn't depend on animation frame data
    /// - Stateless: No instance state needed, all methods are static
    /// 
    /// Usage Examples:
    /// 
    /// // Create a complete skeleton hierarchy
    /// BvhData bvhData = GetBvhData();
    /// Transform skeletonRoot = CreateOrGetJointHierarchy(bvhData, parentTransform);
    /// 
    /// // Create individual joints (for advanced use cases)
    /// Transform jointTransform = CreateOrGetJoint(bvhData.RootJoint, parentTransform);
    /// </summary>
    public static class BvhJointHierarchyBuilder
    {
        /// <summary>
        /// Creates or retrieves the complete joint hierarchy from BVH data.
        /// 
        /// This method creates a GameObject hierarchy matching the BVH skeleton structure.
        /// If any joints already exist (by name), they are reused rather than recreated.
        /// This ensures idempotency - the method is safe to call multiple times.
        /// </summary>
        /// <param name="bvhData">BVH data containing the skeleton structure</param>
        /// <param name="parent">Parent transform under which to create the hierarchy</param>
        /// <returns>Transform of the root joint, or null if bvhData is invalid</returns>
        public static Transform CreateOrGetJointHierarchy(BvhData bvhData, Transform parent)
        {
            if (bvhData == null || bvhData.RootJoint == null || parent == null)
            {
                Debug.LogWarning("[BvhJointHierarchyBuilder] Invalid input: bvhData, RootJoint, or parent is null");
                return null;
            }

            // Create or get the root joint transform
            Transform rootJointTransform = CreateOrGetJoint(bvhData.RootJoint, parent);
            
            if (rootJointTransform == null)
            {
                Debug.LogError("[BvhJointHierarchyBuilder] Failed to create root joint");
                return null;
            }

            // Recursively create all children
            foreach (var childJoint in bvhData.RootJoint.Children)
            {
                CreateJointRecursive(childJoint, rootJointTransform);
            }

            return rootJointTransform;
        }

        /// <summary>
        /// Creates or retrieves a single joint transform.
        /// 
        /// If a child of the parent with the specified joint name already exists,
        /// that transform is returned. Otherwise, a new GameObject is created.
        /// End sites are skipped (they represent leaf nodes with no animation).
        /// </summary>
        /// <param name="joint">BVH joint definition</param>
        /// <param name="parent">Parent transform</param>
        /// <returns>Transform of the created or retrieved joint</returns>
        public static Transform CreateOrGetJoint(BvhJoint joint, Transform parent)
        {
            if (joint == null || parent == null)
            {
                return null;
            }

            // Skip end sites - they have no animation channels
            if (joint.IsEndSite)
            {
                return null;
            }

            // Check if the joint transform already exists
            Transform jointTransform = parent.Find(joint.Name);
            
            if (jointTransform != null)
            {
                return jointTransform;
            }

            // Create new joint GameObject
            GameObject jointObj = new GameObject(joint.Name);
            jointTransform = jointObj.transform;
            jointTransform.SetParent(parent, worldPositionStays: false);
            
            // Initialize transform properties from BVH joint data
            jointTransform.localPosition = joint.Offset;
            jointTransform.localRotation = Quaternion.identity;
            jointTransform.localScale = Vector3.one;

            // Debug.Log($"[BvhJointHierarchyBuilder] Created joint '{joint.Name}' under '{parent.name}'");

            return jointTransform;
        }

        /// <summary>
        /// Internal recursive helper method to create joint hierarchies.
        /// 
        /// Recursively processes all children of a joint, creating GameObjects
        /// and maintaining the parent-child transform relationships.
        /// </summary>
        /// <param name="joint">Current joint to process</param>
        /// <param name="parentTransform">Parent transform under which to create this joint</param>
        private static void CreateJointRecursive(BvhJoint joint, Transform parentTransform)
        {
            if (joint == null || parentTransform == null)
            {
                return;
            }

            // Skip end sites
            if (joint.IsEndSite)
            {
                return;
            }

            // Create or get this joint's transform
            Transform jointTransform = CreateOrGetJoint(joint, parentTransform);
            
            if (jointTransform == null)
            {
                return;
            }

            // Recursively create all children
            foreach (var childJoint in joint.Children)
            {
                CreateJointRecursive(childJoint, jointTransform);
            }
        }
    }
}
