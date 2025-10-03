using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the unified point cloud view from multiple cameras.
/// Displays a single merged mesh from all camera sources.
/// </summary>
public class MultiPointCloudView : MonoBehaviour
{
    private const float DEFAULT_POINT_SIZE = 3.0f;

    // References to individual camera views
    private List<SinglePointCloudView> cameraViews = new List<SinglePointCloudView>();

    // Unified mesh for all cameras
    private GameObject unifiedViewer;
    private MeshFilter unifiedMeshFilter;
    private Mesh unifiedMesh;

    // Multi-camera processor
    private MultiCameraGPUProcessor multiCameraProcessor;

    private bool isInitialized = false;

    void Start()
    {
        SetupUnifiedViewer();
    }

    private void SetupUnifiedViewer()
    {
        // Create unified point cloud viewer GameObject
        unifiedViewer = new GameObject("UnifiedPointCloudViewer");
        unifiedViewer.transform.parent = transform;
        unifiedViewer.transform.localPosition = Vector3.zero;

        // Add mesh components
        unifiedMeshFilter = unifiedViewer.AddComponent<MeshFilter>();
        var meshRenderer = unifiedViewer.AddComponent<MeshRenderer>();

        // Create mesh
        unifiedMesh = new Mesh();
        unifiedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Support large meshes
        unifiedMeshFilter.mesh = unifiedMesh;

        // Setup material for point cloud rendering
        Material pointCloudMaterial = new Material(Shader.Find("Custom/PointCloud"));
        pointCloudMaterial.SetFloat("_PointSize", DEFAULT_POINT_SIZE);
        meshRenderer.material = pointCloudMaterial;

        Debug.Log("Unified point cloud viewer created");
    }

    public void RegisterCameraView(SinglePointCloudView cameraView)
    {
        if (!cameraViews.Contains(cameraView))
        {
            cameraViews.Add(cameraView);
            Debug.Log($"Registered camera view: {cameraView.name}, Total: {cameraViews.Count}");
        }
    }

    public void InitializeMultiCameraProcessing()
    {
        if (cameraViews.Count == 0)
        {
            Debug.LogWarning("No camera views registered for multi-camera processing");
            return;
        }

        Debug.Log($"Initializing multi-camera processing for {cameraViews.Count} cameras");

        // Create multi-camera processor
        if (multiCameraProcessor == null)
        {
            GameObject processorObj = new GameObject("MultiCameraGPUProcessor");
            processorObj.transform.parent = transform;
            multiCameraProcessor = processorObj.AddComponent<MultiCameraGPUProcessor>();
        }

        // Register all camera views with the processor
        foreach (var cameraView in cameraViews)
        {
            multiCameraProcessor.RegisterCameraManager(cameraView);
        }

        // Set unified mesh callback
        multiCameraProcessor.SetUnifiedMeshCallback(UpdateUnifiedMesh);

        // Initialize the processor
        multiCameraProcessor.InitializeMultiCameraProcessing();

        isInitialized = true;
        Debug.Log("Multi-camera view initialized successfully");
    }

    public void UpdateUnifiedMesh(Mesh newMesh)
    {
        if (unifiedMesh != null && newMesh != null)
        {
            unifiedMesh.Clear();
            unifiedMesh.vertices = newMesh.vertices;
            unifiedMesh.colors32 = newMesh.colors32;
            unifiedMesh.SetIndices(newMesh.GetIndices(0), MeshTopology.Points, 0);
            unifiedMesh.RecalculateBounds();
        }
    }

    public void ProcessFrame(ulong timestamp)
    {
        if (!isInitialized || multiCameraProcessor == null)
        {
            Debug.LogWarning("Multi-camera view not initialized");
            return;
        }

        // Process frame using multi-camera processor
        multiCameraProcessor.ProcessAllCameras(timestamp);
    }

    public Mesh GetUnifiedMesh()
    {
        return unifiedMesh;
    }

    public int GetCameraCount()
    {
        return cameraViews.Count;
    }

    void OnDestroy()
    {
        if (unifiedMesh != null)
        {
            Destroy(unifiedMesh);
        }

        if (multiCameraProcessor != null)
        {
            Destroy(multiCameraProcessor.gameObject);
        }
    }

    // Future: PLY export functionality
    public void ExportToPLY(string filePath)
    {
        if (unifiedMesh == null)
        {
            Debug.LogWarning("No unified mesh to export");
            return;
        }

        // TODO: Implement PLY export
        Debug.Log($"Exporting unified point cloud to: {filePath}");
    }
}
