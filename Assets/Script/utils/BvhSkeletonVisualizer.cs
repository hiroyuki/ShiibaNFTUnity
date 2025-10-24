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
    [Range(0.001f, 10.0f)]
    public float jointRadius = 0.05f;

    [Tooltip("Width of bone lines")]
    [Range(0.001f, 0.5f)]
    public float boneWidth = 0.01f;

    [Header("Rendering")]
    [Tooltip("Material for rendering (leave null for default)")]
    public Material renderMaterial;

    private GameObject renderRoot;
    private readonly List<LineRenderer> boneRenderers = new();
    private readonly List<GameObject> jointSpheres = new();

    void Start()
    {
        // Wait a bit for skeleton to be created
        Invoke(nameof(CreateVisuals), 0.2f);
    }

    void CreateVisuals()
    {
        Debug.Log($"BvhSkeletonVisualizer.CreateVisuals() called on '{gameObject.name}'");
        Debug.Log($"  Transform has {transform.childCount} children");

        // List all children
        foreach (Transform child in transform)
        {
            Debug.Log($"  Child: '{child.name}' at position {child.position}");
        }

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

        Debug.Log($"Creating BVH visuals for skeleton joints...");

        // Create visuals for all skeleton joints (skip render root itself)
        int rootJointCount = 0;
        foreach (Transform child in transform)
        {
            if (child.name != "BVH_Visuals")
            {
                rootJointCount++;
                Debug.Log($"  Processing root joint: '{child.name}'");
                CreateVisualsRecursive(child);
            }
        }

        Debug.Log($"Created {jointSpheres.Count} joint spheres and {boneRenderers.Count} bone lines from {rootJointCount} root joints");

        if (jointSpheres.Count == 0)
        {
            Debug.LogWarning("No joints created! The skeleton may not have been initialized yet.");
        }
    }

    void CreateVisualsRecursive(Transform joint)
    {
        Debug.Log($"    Creating sphere for joint '{joint.name}' at {joint.position}");

        // Create sphere for this joint
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = $"Joint_{joint.name}";
        sphere.transform.SetParent(renderRoot.transform);
        sphere.transform.position = joint.position;
        sphere.transform.localScale = Vector3.one * jointRadius * 2f;

        Debug.Log($"      Sphere created at {sphere.transform.position} with scale {sphere.transform.localScale}");

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
            renderer.material = new Material(Shader.Find("Standard"))
            {
                color = skeletonColor
            };
        }

        jointSpheres.Add(sphere);

        // Create lines to all children
        foreach (Transform child in joint)
        {
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
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = skeletonColor;
                lr.endColor = skeletonColor;
            }

            boneRenderers.Add(lr);

            // Recurse to children
            CreateVisualsRecursive(child);
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
        foreach (Transform child in joint)
        {
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
    }

    void OnDestroy()
    {
        if (renderRoot != null)
        {
            Destroy(renderRoot);
        }
    }
}
