using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Visualizes BVH skeleton by drawing lines between joints and spheres at joint positions
/// Simple version - renders in Game view only
/// </summary>
public class BvhSkeletonVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [Tooltip("Color for the skeleton")]
    public Color skeletonColor = Color.green;

    [Tooltip("Radius of joint spheres")]
    [Range(0.001f, 0.03f)]
    public float jointRadius = 0.05f;

    [Tooltip("Width of bone lines")]
    [Range(0.001f, 0.02f)]
    public float boneWidth = 0.01f;

    [Header("Rendering")]
    [Tooltip("Render skeleton on top of point clouds")]
    public bool renderOnTop = true;

    [Tooltip("Render queue offset from Transparent (3000). Default: +100")]
    [Range(0, 1000)]
    public int queueOffset = 100;

    [Tooltip("Skeleton opacity for alignment checking")]
    [Range(0f, 1f)]
    public float skeletonOpacity = 1f;

    [Tooltip("Material for rendering (leave null for default)")]
    public Material renderMaterial;

    private GameObject renderRoot;
    private readonly List<LineRenderer> boneRenderers = new();
    private readonly List<GameObject> jointSpheres = new();
    private Material skeletonMaterial;

    void Start()
    {
        // Wait for skeleton to be fully created
        // Timeline playback creates joints during OnGraphStart
        Invoke(nameof(CreateVisuals), 1.0f);
    }

    void CreateVisuals()
    {
        // Clean up any existing visuals first
        if (renderRoot != null)
        {
            Destroy(renderRoot);
            boneRenderers.Clear();
            jointSpheres.Clear();
        }

        // Create root container
        renderRoot = new GameObject("BVH_Visuals");
        renderRoot.transform.SetParent(transform);
        renderRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Create visuals for all skeleton joints (skip render root itself)
        foreach (Transform child in transform)
        {
            if (child.name != "BVH_Visuals")
            {
                CreateVisualsRecursive(child);
            }
        }
    }

    void CreateVisualsRecursive(Transform joint)
    {
        // Create sphere for this joint
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = $"Joint_{joint.name}";
        sphere.transform.SetParent(renderRoot.transform);
        sphere.transform.position = joint.position;
        sphere.transform.localScale = Vector3.one * jointRadius * 2f;

        // Remove collider
        if (sphere.TryGetComponent<Collider>(out var collider))
        {
            Destroy(collider);
        }

        // Set material
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderMaterial != null)
        {
            renderer.material = renderMaterial;
        }
        else
        {
            if (skeletonMaterial == null)
            {
                CreateSkeletonMaterial();
            }
            renderer.material = skeletonMaterial;
        }

        jointSpheres.Add(sphere);

        // Create lines to all children
        bool hasChildren = false;
        foreach (Transform child in joint)
        {
            hasChildren = true;

            GameObject lineObj = new($"Bone_{joint.name}_to_{child.name}");
            lineObj.transform.SetParent(renderRoot.transform);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.startWidth = boneWidth;
            lr.endWidth = boneWidth;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.SetPosition(0, joint.position);
            lr.SetPosition(1, child.position);

            if (renderMaterial != null)
            {
                lr.material = renderMaterial;
            }
            else
            {
                if (skeletonMaterial == null)
                {
                    CreateSkeletonMaterial();
                }
                lr.material = skeletonMaterial;
            }

            boneRenderers.Add(lr);

            // Recurse to children
            CreateVisualsRecursive(child);
        }

        // If this joint has no children, check for End Site children
        if (!hasChildren)
        {
            // Try to get BVH joint data to find End Site
            BvhData bvhData = BvhDataCache.GetBvhData();
            if (bvhData != null)
            {
                BvhJoint bvhJoint = FindBvhJointByName(bvhData.RootJoint, joint.name);
                if (bvhJoint != null)
                {
                    // Check if this joint has EndSite children
                    foreach (var child in bvhJoint.Children)
                    {
                        if (child.IsEndSite)
                        {
                            // Calculate endpoint in world space using EndSite offset
                            Vector3 endpointPosition = joint.position + joint.TransformDirection(child.Offset);

                            // Create sphere at endpoint
                            GameObject endSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            endSphere.name = $"EndPoint_{joint.name}";
                            endSphere.transform.SetParent(renderRoot.transform);
                            endSphere.transform.position = endpointPosition;
                            endSphere.transform.localScale = Vector3.one * jointRadius * 1.5f;

                            // Remove collider
                            if (endSphere.TryGetComponent<Collider>(out var endCollider))
                            {
                                Destroy(endCollider);
                            }

                            // Set material
                            Renderer endRenderer = endSphere.GetComponent<Renderer>();
                            if (renderMaterial != null)
                            {
                                endRenderer.material = renderMaterial;
                            }
                            else
                            {
                                if (skeletonMaterial == null)
                                {
                                    CreateSkeletonMaterial();
                                }
                                endRenderer.material = skeletonMaterial;
                            }

                            jointSpheres.Add(endSphere);

                            // Create line to endpoint
                            GameObject endLineObj = new($"Bone_{joint.name}_to_End");
                            endLineObj.transform.SetParent(renderRoot.transform);

                            LineRenderer endLr = endLineObj.AddComponent<LineRenderer>();
                            endLr.startWidth = boneWidth;
                            endLr.endWidth = boneWidth;
                            endLr.positionCount = 2;
                            endLr.useWorldSpace = true;
                            endLr.SetPosition(0, joint.position);
                            endLr.SetPosition(1, endpointPosition);

                            if (renderMaterial != null)
                            {
                                endLr.material = renderMaterial;
                            }
                            else
                            {
                                if (skeletonMaterial == null)
                                {
                                    CreateSkeletonMaterial();
                                }
                                endLr.material = skeletonMaterial;
                            }

                            boneRenderers.Add(endLr);
                            break;  // Each joint has at most one EndSite
                        }
                    }
                }
            }
        }
    }

    void LateUpdate()
    {
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (jointSpheres.Count == 0 || boneRenderers.Count == 0)
            return;

        int sphereIndex = 0;
        int boneIndex = 0;

        foreach (Transform child in transform)
        {
            if (child.name != "BVH_Visuals")
            {
                UpdateVisualsRecursive(child, ref sphereIndex, ref boneIndex);
            }
        }
    }

    void UpdateVisualsRecursive(Transform joint, ref int sphereIndex, ref int boneIndex)
    {
        // Update sphere position
        if (sphereIndex < jointSpheres.Count)
        {
            jointSpheres[sphereIndex].transform.position = joint.position;
            jointSpheres[sphereIndex].transform.localScale = Vector3.one * jointRadius * 2f;
            sphereIndex++;
        }

        // Update bone lines to children
        bool hasChildren = false;
        foreach (Transform child in joint)
        {
            hasChildren = true;

            if (boneIndex < boneRenderers.Count)
            {
                LineRenderer lr = boneRenderers[boneIndex];
                lr.SetPosition(0, joint.position);
                lr.SetPosition(1, child.position);
                lr.startWidth = boneWidth;
                lr.endWidth = boneWidth;
                boneIndex++;
            }

            UpdateVisualsRecursive(child, ref sphereIndex, ref boneIndex);
        }

        // Update EndSite visualizations if applicable
        if (!hasChildren)
        {
            BvhData bvhData = BvhDataCache.GetBvhData();
            if (bvhData != null)
            {
                BvhJoint bvhJoint = FindBvhJointByName(bvhData.RootJoint, joint.name);
                if (bvhJoint != null)
                {
                    foreach (var child in bvhJoint.Children)
                    {
                        if (child.IsEndSite)
                        {
                            // Update EndSite sphere and line
                            Vector3 endpointPosition = joint.position + joint.TransformDirection(child.Offset);

                            // Update endpoint sphere
                            if (sphereIndex < jointSpheres.Count)
                            {
                                jointSpheres[sphereIndex].transform.position = endpointPosition;
                                jointSpheres[sphereIndex].transform.localScale = Vector3.one * jointRadius * 1.5f;
                                sphereIndex++;
                            }

                            // Update endpoint line
                            if (boneIndex < boneRenderers.Count)
                            {
                                LineRenderer lr = boneRenderers[boneIndex];
                                lr.SetPosition(0, joint.position);
                                lr.SetPosition(1, endpointPosition);
                                lr.startWidth = boneWidth;
                                lr.endWidth = boneWidth;
                                boneIndex++;
                            }

                            break;  // Each joint has at most one EndSite
                        }
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        if (renderRoot != null)
        {
            Destroy(renderRoot);
        }
    }

    /// <summary>
    /// Find a BVH joint by name in the hierarchy
    /// </summary>
    private BvhJoint FindBvhJointByName(BvhJoint joint, string name)
    {
        if (joint == null)
            return null;

        if (joint.Name == name)
            return joint;

        foreach (var child in joint.Children)
        {
            BvhJoint found = FindBvhJointByName(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Creates or updates the shared skeleton material
    /// </summary>
    private void CreateSkeletonMaterial()
    {
        if (renderOnTop)
        {
            // Try to load custom foreground shader
            Shader foregroundShader = Shader.Find("Custom/SkeletonForeground");
            if (foregroundShader != null)
            {
                skeletonMaterial = new Material(foregroundShader);
                skeletonMaterial.SetColor("_Color", skeletonColor);
                skeletonMaterial.SetFloat("_Opacity", skeletonOpacity);
                // Runtime queue adjustment
                skeletonMaterial.renderQueue = 3000 + queueOffset;
            }
            else
            {
                Debug.LogWarning("SkeletonForeground shader not found, falling back to Standard");
                skeletonMaterial = new Material(Shader.Find("Standard"))
                {
                    color = skeletonColor
                };
                skeletonMaterial.renderQueue = 3000 + queueOffset;
            }
        }
        else
        {
            // Default rendering (existing behavior)
            skeletonMaterial = new Material(Shader.Find("Standard"))
            {
                color = skeletonColor
            };
        }
    }

    /// <summary>
    /// Update material when inspector values change
    /// </summary>
    void OnValidate()
    {
        // Update material when inspector values change
        if (skeletonMaterial != null && renderOnTop)
        {
            skeletonMaterial.SetColor("_Color", skeletonColor);
            skeletonMaterial.SetFloat("_Opacity", skeletonOpacity);
            skeletonMaterial.renderQueue = 3000 + queueOffset;
        }
    }
}
