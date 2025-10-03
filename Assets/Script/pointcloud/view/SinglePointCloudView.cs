using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Pure view layer for displaying point cloud mesh from a single camera.
/// All data operations and mesh generation are handled externally by MultiCamPointCloudManager.
/// This class only manages the visual representation (GameObject, Mesh, Material).
/// </summary>
public class SinglePointCloudView : MonoBehaviour
{
    // Configuration constants
    private const float DEFAULT_POINT_SIZE = 3.0f;
    private const float GIZMO_SIZE = 0.1f;

    // View components (owned by this view)
    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private string deviceName;

    /// <summary>
    /// Initialize the view with device information.
    /// </summary>
    public void Initialize(string deviceName, Vector3 position, Quaternion rotation)
    {
        this.deviceName = deviceName;
        SetupDepthViewer(position, rotation);
    }

    private void SetupDepthViewer(Vector3 position, Quaternion rotation)
    {
        // Create DepthViewer GameObject
        string viewerName = $"DepthViewer_{deviceName}";
        depthViewer = new GameObject(viewerName);
        depthViewer.transform.SetParent(this.transform);

        // Add visualization gizmo
        var gizmo = depthViewer.AddComponent<CameraPositionGizmo>();
        gizmo.gizmoColor = Color.red;
        gizmo.size = GIZMO_SIZE;

        // Apply global transform
        depthViewer.transform.SetLocalPositionAndRotation(position, rotation);

        // Setup mesh components
        depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.AddComponent<MeshRenderer>();
        Material material = new(Shader.Find("Unlit/VertexColor"));
        material.SetFloat("_PointSize", DEFAULT_POINT_SIZE);
        depthRenderer.material = material;

        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        Debug.Log($"SinglePointCloudView initialized for {deviceName}");
    }

    /// <summary>
    /// Update the mesh displayed by this view.
    /// Called by MultiCamPointCloudManager after mesh generation.
    /// </summary>
    public void UpdateMesh(Mesh newMesh)
    {
        if (depthMesh != null && newMesh != null)
        {
            depthMesh.Clear();
            depthMesh.vertices = newMesh.vertices;
            depthMesh.colors32 = newMesh.colors32;
            depthMesh.SetIndices(newMesh.GetIndices(0), MeshTopology.Points, 0);
            depthMesh.RecalculateBounds();
        }
    }

    public Mesh GetMesh()
    {
        return depthMesh;
    }

    public Transform GetDepthViewerTransform()
    {
        return depthViewer?.transform;
    }

    void OnDestroy()
    {
        if (depthMesh != null)
        {
            Destroy(depthMesh);
        }
    }
}
