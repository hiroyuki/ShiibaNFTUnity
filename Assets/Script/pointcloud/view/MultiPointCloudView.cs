using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the unified point cloud view from multiple cameras.
/// Displays a single merged mesh from all camera sources.
/// </summary>
public class MultiPointCloudView : MonoBehaviour
{

    // Frame controllers (passed from MultiCamPointCloudManager)
    private List<CameraFrameController> frameControllers = new List<CameraFrameController>();

    // Unified mesh for all cameras
    private GameObject unifiedViewer;
    private MeshFilter unifiedMeshFilter;
    private Mesh unifiedMesh;

    // Multi-camera processor
    private MultiCameraGPUProcessor multiCameraProcessor;

    private bool isInitialized = false;

    public void SetupUnifiedViewer()
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

        // Setup material for point cloud rendering (use same shader as SinglePointCloudView)
        Material pointCloudMaterial = new Material(Shader.Find("Unlit/VertexColor"));
        pointCloudMaterial.SetFloat("_PointSize", PointCloudSettings.pointSize);
        pointCloudMaterial.SetFloat("_Opacity", PointCloudSettings.pointCloudOpacity);
        meshRenderer.material = pointCloudMaterial;

        Debug.Log("Unified point cloud viewer created");
    }

    public void Initialize(List<CameraFrameController> controllers)
    {
        this.frameControllers = controllers;

        if (frameControllers.Count == 0)
        {
            Debug.LogWarning("No frame controllers provided for multi-camera processing");
            return;
        }

        Debug.Log($"Initializing multi-camera view for {frameControllers.Count} cameras");

        // Setup unified viewer
        SetupUnifiedViewer();

        // Create multi-camera processor
        GameObject processorObj = new GameObject("MultiCameraGPUProcessor");
        processorObj.transform.parent = transform;
        multiCameraProcessor = processorObj.AddComponent<MultiCameraGPUProcessor>();

        // Initialize processor with frame controllers
        multiCameraProcessor.Initialize(frameControllers);

        // Set unified mesh callback
        multiCameraProcessor.SetUnifiedMeshCallback(UpdateUnifiedMesh);

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

    public void ProcessFirstFramesIfNeeded()
    {
        for (int i = 0; i < frameControllers.Count; i++)
        {
            var controller = frameControllers[i];
            if (controller.AutoLoadFirstFrame && !controller.IsFirstFrameProcessed)
            {
                ulong targetTimestamp = controller.GetTimestampForFrame(0);
                ProcessFrame(targetTimestamp);
                return; // Process one frame at a time for all cameras
            }
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
        return frameControllers.Count;
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

    /// <summary>
    /// Export unified point cloud to PLY format (binary little-endian)
    /// </summary>
    public void ExportToPLY(string filePath)
    {
        PlyExporter.ExportToPLY(unifiedMesh, filePath);
    }

    /// <summary>
    /// Load point cloud from PLY file and update the unified mesh
    /// </summary>
    public void LoadFromPLY(string filePath)
    {
        Mesh loadedMesh = PlyImporter.ImportFromPLY(filePath);
        if (loadedMesh != null)
        {
            UpdateUnifiedMesh(loadedMesh);
        }
        else
        {
            Debug.LogError($"Failed to load PLY file: {filePath}");
        }
    }
}
