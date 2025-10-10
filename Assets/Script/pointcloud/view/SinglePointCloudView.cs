using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Manages point cloud display and processing for a single camera.
/// Used in GPU/CPU processing modes where each camera is displayed individually.
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

    // Processing components (owned by this view)
    private CameraFrameController frameController;
    private IPointCloudProcessor processor;
    private ProcessingType processingType;

    /// <summary>
    /// Initialize the view with frame controller and processor.
    /// </summary>
    public void Initialize(CameraFrameController controller, IPointCloudProcessor processor, ProcessingType processingType)
    {
        this.frameController = controller;
        this.processor = processor;
        this.processingType = processingType;
        this.deviceName = controller.DeviceName;

        // Get global transform from device
        var device = controller.Device;
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;
        device.TryGetGlobalTransform(out position, out rotation);

        SetupDepthViewer(position, rotation);

        // Set depth viewer transform in processor
        processor.SetDepthViewerTransform(depthViewer.transform);
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

    public void ProcessFirstFrameIfNeeded()
    {
        if (frameController.AutoLoadFirstFrame && !frameController.IsFirstFrameProcessed)
        {
            ulong targetTimestamp = frameController.GetTimestampForFrame(0);
            ProcessFrame(targetTimestamp);
        }
    }

    public bool ProcessFrame(ulong targetTimestamp)
    {
        var device = frameController.Device;

        // Seek to timestamp
        device.UpdateDeviceStatus(DeviceStatusType.Ready, processingType, "Starting frame processing...");
        bool synchronized = frameController.SeekToTimestamp(targetTimestamp, out ulong actualTimestamp);
        device.UpdateDeviceStatus(DeviceStatusType.Ready, processingType, "Frame seek complete");

        if (!synchronized)
        {
            Debug.LogWarning($"No synchronized frame found for timestamp {targetTimestamp}");
            device.UpdateDeviceStatus(DeviceStatusType.Error, processingType, "No synchronized frame");
            return false;
        }

        // Parse record
        bool frameOk = frameController.ParseRecord(processingType != ProcessingType.CPU);
        device.UpdateDeviceStatus(DeviceStatusType.Processing, processingType, "Frame data parsed");

        if (!frameOk)
        {
            return false;
        }

        // Update texture
        frameController.UpdateTexture(processingType != ProcessingType.CPU);

        // Generate mesh using processor
        processor.UpdateMesh(depthMesh, device);

        // Update timestamp
        frameController.UpdateCurrentTimestamp(actualTimestamp);
        frameController.NotifyFirstFrameProcessed();
        device.UpdateDeviceStatus(DeviceStatusType.Complete, processingType, "Mesh updated");

        return true;
    }

    public Mesh GetMesh()
    {
        return depthMesh;
    }

    public Transform GetDepthViewerTransform()
    {
        return depthViewer?.transform;
    }

    public CameraFrameController GetFrameController()
    {
        return frameController;
    }

    public ulong GetCurrentTimestamp()
    {
        return frameController?.CurrentTimestamp ?? 0;
    }

    public bool PeekNextTimestamp(out ulong timestamp)
    {
        return frameController.PeekNextTimestamp(out timestamp);
    }

    public ulong GetTimestampForFrame(int frameIndex)
    {
        return frameController.GetTimestampForFrame(frameIndex);
    }

    public int GetFps()
    {
        return frameController.GetFps();
    }

    public int GetTotalFrameCount()
    {
        return frameController.GetTotalFrameCount();
    }

    void OnDestroy()
    {
        if (depthMesh != null)
        {
            Destroy(depthMesh);
        }

        if (processor != null)
        {
            processor.Dispose();
        }
    }
}
