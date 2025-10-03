using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class SinglePointCloudView : MonoBehaviour
{
    // Configuration constants
    private const float DEFAULT_POINT_SIZE = 3.0f;
    private const float GIZMO_SIZE = 0.1f;

    // View components
    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    // Data controller (managed by MultiCamPointCloudManager, not owned)
    private CameraFrameController frameController;

    // Processor (owned by this view)
    private IPointCloudProcessor pointCloudProcessor;

    public ProcessingType processingType { get; private set; }

    /// <summary>
    /// Initialize with a CameraFrameController reference.
    /// The controller is managed by MultiCamPointCloudManager.
    /// </summary>
    public void Initialize(CameraFrameController controller, ProcessingType processingType)
    {
        this.frameController = controller;
        this.processingType = processingType;
        SetupDepthViewer();
    }

    public void SetupProcessor()
    {
        SensorDevice device = frameController.Device;

        // Load depth bias from configuration.yaml
        float depthBias = device.GetDepthBias();

        // Create the best available processor using factory pattern
        pointCloudProcessor = PointCloudProcessorFactory.CreateBestProcessor(device.deviceName);

        device.UpdateDeviceStatus(DeviceStatusType.Loading, processingType,
                          $"{processingType} processing enabled");

        // Setup the processor with all required parameters
        pointCloudProcessor.Setup(device, depthBias);
        pointCloudProcessor.SetDepthViewerTransform(depthViewer.transform);
    }

    public IPointCloudProcessor GetProcessor()
    {
        return pointCloudProcessor;
    }

    public void SetupWithProcessor()
    {
        SensorDevice device = frameController.Device;

        ConfigureBoundingVolume(pointCloudProcessor);
        ConfigureTransforms(pointCloudProcessor);
        pointCloudProcessor.SetupCameraMetadata(device);
        FinalizeSetup();
    }


    public void ConfigureBoundingVolume(IPointCloudProcessor pointCloudProcessor)
    {
        // Find and set bounding volume
        Transform boundingVolume = GameObject.Find("BoundingVolume")?.transform;
        if (boundingVolume != null)
        {
            pointCloudProcessor.SetBoundingVolume(boundingVolume);
        }
        else
        {
            Debug.LogWarning("BoundingVolume GameObject not found in hierarchy");
        }
    }

    public void ConfigureTransforms(IPointCloudProcessor pointCloudProcessor)
    {
        SensorDevice device = frameController.Device;

        // Transforms are already loaded in SensorDevice via LoadExtrinsics()
        // Just apply them to the processor
        pointCloudProcessor.ApplyDepthToColorExtrinsics(
            device.GetDepthToColorTranslation(),
            device.GetDepthToColorRotation()
        );

        device.UpdateDeviceStatus(DeviceStatusType.Loading, ProcessingType.None, "Finalizing setup...");
        pointCloudProcessor.SetupColorIntrinsics(device.GetColorParser().sensorHeader);
    }

    private void FinalizeSetup()
    {
        SensorDevice device = frameController.Device;

        device.UpdateDeviceStatus(DeviceStatusType.Ready, ProcessingType.None, "Waiting for first frame");
        SetupStatusUI.ShowStatus($"Setup complete for {device.deviceName}");
    }

    void Start()
    {
        if (frameController != null)
        {
            SetupStatusUI.UpdateDeviceStatus(frameController.Device);
        }
    }

    private void SetupDepthViewer()
    {
        SensorDevice device = frameController.Device;

        // Create DepthViewer GameObject
        string viewerName = $"DepthViewer_{device.deviceName}";
        depthViewer = new GameObject(viewerName);
        depthViewer.transform.SetParent(this.transform);

        // Add visualization gizmo
        var gizmo = depthViewer.AddComponent<CameraPositionGizmo>();
        gizmo.gizmoColor = Color.red;
        gizmo.size = GIZMO_SIZE;

        // Apply global transform if available
        if (device.TryGetGlobalTransform(out Vector3 pos, out Quaternion rot))
        {
            depthViewer.transform.SetLocalPositionAndRotation(pos, rot);
        }

        // Setup mesh components
        depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.AddComponent<MeshRenderer>();
        Material material = new(Shader.Find("Unlit/VertexColor"));
        material.SetFloat("_PointSize", DEFAULT_POINT_SIZE);
        depthRenderer.material = material;

        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;
    }


    public void ProcessFirstFrameIfNeeded()
    {
        // Auto-load first frame on startup (disabled - MultiCamPointCloudManager handles this)
        if (frameController.AutoLoadFirstFrame && !frameController.IsFirstFrameProcessed())
        {
            ulong targetTimestamp = frameController.GetTimestampForFrame(0);
            bool success = ProcessFrame(targetTimestamp);
            if (success)
            {
                frameController.NotifyFirstFrameProcessed();
            }
        }
    }

    public void ResetToFirstFrame()
    {
        Debug.Log("Reset To First Frame");

        ulong targetTimestamp = frameController.GetTimestampForFrame(0);
        bool success = ProcessFrame(targetTimestamp);
    }
    
    // Delegate to frameController
    public bool SeekToTimestamp(ulong targetTimestamp, out ulong depthTs)
    {
        return frameController.SeekToTimestamp(targetTimestamp, out depthTs);
    }

    public void UpdateTexture()
    {
        frameController.UpdateTexture(processingType != ProcessingType.CPU);
    }

    public void UpdateCurrentTimestamp(ulong timestamp)
    {
        frameController.UpdateCurrentTimestamp(timestamp);
    }


    public bool ParseRecord()
    {
        return frameController.ParseRecord(processingType != ProcessingType.CPU);
    }


    public bool ProcessFrame(ulong targetTimestamp)
    {
        SensorDevice device = frameController.Device;
        ulong actualTimestamp = 0;

        device.UpdateDeviceStatus(DeviceStatusType.Ready, processingType, "Starting frame processing...");
        bool synchronized = SeekToTimestamp(targetTimestamp, out actualTimestamp);
        device.UpdateDeviceStatus(DeviceStatusType.Ready, processingType, "Frame seek complete");

        if (synchronized)
        {
            // Process the synchronized frame
            var frameOk = ParseRecord();
            device.UpdateDeviceStatus(DeviceStatusType.Processing, processingType, "Frame data parsed");

            if (frameOk)
            {
                UpdateTexture();
                if (processingType != ProcessingType.ONESHADER)
                {
                    // Use the unified interface - no more branching logic!
                    pointCloudProcessor.UpdateMesh(depthMesh, device);
                }
                UpdateCurrentTimestamp(actualTimestamp);
                device.UpdateDeviceStatus(DeviceStatusType.Complete, processingType, "Mesh updated");
            }
            return frameOk;
        }
        else
        {
            Debug.LogWarning($"No synchronized frame found for timestamp {targetTimestamp}");
            device.UpdateDeviceStatus(DeviceStatusType.Error, processingType, "No synchronized frame");
            return false;
        }
    }


    public int GetTotalFrameCount()
    {
        return frameController.GetTotalFrameCount();
    }

    private void ResetParsers()
    {
        frameController.ResetParsers();
    }

    public ulong GetTimestampForFrame(int frameIndex)
    {
        return frameController.GetTimestampForFrame(frameIndex);
    }

    // Public methods for synchronized frame navigation
    public string GetDeviceName() => frameController.DeviceName;

    public ulong GetCurrentTimestamp() => frameController.CurrentTimestamp;

    public int GetFps()
    {
        return frameController.GetFps();
    }

    public bool PeekNextTimestamp(out ulong timestamp)
    {
        return frameController.PeekNextTimestamp(out timestamp);
    }
    
    // public bool NavigateToTimestamp(ulong targetTimestamp)
    // {
    //     try
    //     {
    //         // Use the ParseRecord method to seek to the target timestamp
    //         bool depthSuccess = depthParser?.ParseRecord(targetTimestamp, optimizeForGPU: false) ?? false;
    //         bool colorSuccess = colorParser?.ParseRecord(targetTimestamp, optimizeForGPU: false) ?? false;
            
    //         if (depthSuccess && colorSuccess)
    //         {
    //             // Process the synchronized frame
    //             return ProcessFrameWithParsers(depthParser, colorParser, targetTimestamp, showStatus: false);
    //         }
            
    //         return false;
    //     }
    //     catch (System.Exception ex)
    //     {
    //         Debug.LogError($"Error navigating to timestamp {targetTimestamp}: {ex.Message}");
    //         return false;
    //     }
    // }


    private ProcessingType GetCurrentProcessingType()
    {
        return processingType;
    }

    // Public getters for multi-camera processing
    public SensorDevice GetDevice() => frameController?.Device;
    public CameraFrameController GetFrameController() => frameController;

    void OnDestroy()
    {
        // Don't dispose frameController - it's managed by MultiCamPointCloudManager
        // Only dispose the processor we own
        if (pointCloudProcessor != null)
        {
            pointCloudProcessor.Dispose();
        }
    }
}
