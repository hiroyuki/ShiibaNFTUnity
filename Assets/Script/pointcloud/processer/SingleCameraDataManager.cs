using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class SingleCameraDataManager : MonoBehaviour
{
    public ProcessingType processingType { get; private set; }
    // Configuration constants
    private const float DEFAULT_POINT_SIZE = 3.0f;
    private const float GIZMO_SIZE = 0.1f;

    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private SensorDevice device;

    private bool firstFrameProcessed = false;
    private bool autoLoadFirstFrame = true; // Enabled - each manager loads its own first frame
    
    // Store current timestamp for efficient leading camera detection
    private ulong currentTimestamp = 0;

    // Timeline scrubbing support
    private int totalFrameCount = -1;

    public void Initialize(string dir, string hostname, string deviceName, ProcessingType processingType)
    {
        this.device = new SensorDevice();
        this.processingType = processingType;
        this.device.setup(dir, hostname, deviceName);
        SetupDepthViewer();
    }

    public IPointCloudProcessor SetupProcessors()
    {
        // Load depth bias from configuration.yaml
        float depthBias = device.GetDepthBias();

        // Create the best available processor using factory pattern
        IPointCloudProcessor pointCloudProcessor = PointCloudProcessorFactory.CreateBestProcessor(device.deviceName);

        UpdateDeviceStatus(DeviceStatusType.Loading, processingType,
                          $"{processingType} processing enabled");
        
        // Setup the processor with all required parameters
        pointCloudProcessor.Setup(device, depthBias);

        pointCloudProcessor.SetDepthViewerTransform(depthViewer.transform);
        
        return pointCloudProcessor;
    }

    public void SetupWithProcessor(IPointCloudProcessor pointCloudProcessor)
    {
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
        // Transforms are already loaded in SensorDevice via LoadExtrinsics()
        // Just apply them to the processor
        pointCloudProcessor.ApplyDepthToColorExtrinsics(
            device.GetDepthToColorTranslation(),
            device.GetDepthToColorRotation()
        );

        UpdateDeviceStatus(DeviceStatusType.Loading, ProcessingType.None, "Finalizing setup...");
        pointCloudProcessor.SetupColorIntrinsics(device.GetColorParser().sensorHeader);
    }

    private void FinalizeSetup()
    {
        // Set reasonable defaults for timeline support
        totalFrameCount = -1; // Unknown, will be estimated

        UpdateDeviceStatus(DeviceStatusType.Ready, ProcessingType.None, "Waiting for first frame");
        SetupStatusUI.ShowStatus($"Setup complete for {device.deviceName}");
    }

    void Start()
    {
        SetupStatusUI.UpdateDeviceStatus(device);

    }

    private void SetupDepthViewer()
    {
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

    private void UpdateDeviceStatus(DeviceStatusType loading, ProcessingType GPU_Binary, string v)
    {
        device.statusType = loading;
        device.processingType = GPU_Binary;
        device.statusMessage = v;
        device.lastUpdated = DateTime.Now;
        SetupStatusUI.UpdateDeviceStatus(device);
    }


    public void ProcessFirstFrameIfNeeded(IPointCloudProcessor pointCloudProcessor)
    {
        // Auto-load first frame on startup (disabled - MultiCamPointCloudManager handles this)
        if (autoLoadFirstFrame && !firstFrameProcessed)
        {
            ulong targetTimestamp = device.GetTimestampForFrame(0);
            bool success = ProcessFrame(targetTimestamp, pointCloudProcessor);
            autoLoadFirstFrame = false; // Prevent auto-loading again
        }
    }
   
    public void ResetToFirstFrame(IPointCloudProcessor pointCloudProcessor)
    {
        Debug.Log("Reset To First Frame");
        
        ulong targetTimestamp = device.GetTimestampForFrame(0);
        bool success = ProcessFrame(targetTimestamp, pointCloudProcessor);
    }
    
    // Simplified timestamp-only seeking logic
    public bool SeekToTimestamp(ulong targetTimestamp, out ulong depthTs)
    {
        // Reset parsers to beginning
        if(currentTimestamp > targetTimestamp) device.ResetParsers();
        bool synchronized = false;
        depthTs = 0;
        while (!synchronized)
        {
            // Check synchronization using unified method
            synchronized = device.CheckSynchronization(out depthTs, out ulong colorTs, out long delta);
            if (!synchronized)
            {
                if (depthTs == 0 && colorTs == 0) break; // No more data
                else
                {
                    // Skip the earlier timestamp to catch up
                    if (delta < 0)
                    {
                        // Debug.LogWarning($"{device.deviceName}: Depth is behind color by {-delta} ticks, skipping depth frame depthTs={depthTs}, colorTs={colorTs}");
                        device.SkipDepthRecord();
                    }
                    else
                    {
                        // Debug.LogWarning($"{device.deviceName}: Color is behind depth by {-delta} ticks, skipping color frame depthTs={depthTs}, colorTs={colorTs}");
                        device.SkipColorRecord();
                    }
                }
            }
            if (depthTs < targetTimestamp)
            {
                synchronized = false;
                device.SkipCurrentRecord();
            }
        }
        return synchronized;
    }


    public void UpdateTexture()
    {
        device.UpdateTexture(processingType != ProcessingType.CPU);
    }

    public void UpdateCurrentTimestamp(ulong timestamp)
    {
        currentTimestamp = timestamp;
    }

    // // Unified frame processing logic using the interface
    // private void UpdateMesh(ulong frameTimestamp, bool showStatus = false)
    // {
    //     if (processingType != ProcessingType.MultiGPU)
    //     {
    //         // Use the unified interface - no more branching logic!
    //         pointCloudProcessor.UpdateMesh(depthMesh, device);
    //     }
    // }
    
    public void NotifyFirstFrameProcessed()
    {
        if (!firstFrameProcessed)
        {
            SetupStatusUI.OnFirstFrameProcessed();
            firstFrameProcessed = true; // Mark first frame as processed
        }
    }

    public bool ParseRecord()
    {
        return device.ParseRecord(processingType != ProcessingType.CPU);
    }


    public bool ProcessFrame(ulong targetTimestamp, IPointCloudProcessor pointCloudProcessor)
    {
        ulong actualTimestamp = 0;

        UpdateDeviceStatus(DeviceStatusType.Ready, processingType, "Starting frame processing...");
        bool synchronized = SeekToTimestamp(targetTimestamp, out actualTimestamp);
        UpdateDeviceStatus(DeviceStatusType.Ready, processingType, "Frame seek complete");
        if (synchronized)
        {
            // Process the synchronized frame

            var frameOk = ParseRecord();
            UpdateDeviceStatus(DeviceStatusType.Processing, processingType, "Frame data parsed");
            if (frameOk)
            {
                UpdateTexture();
                if (processingType != ProcessingType.SINGLEGPU)
                {
                    // Use the unified interface - no more branching logic!
                    pointCloudProcessor.UpdateMesh(depthMesh, device);
                }
                UpdateCurrentTimestamp(actualTimestamp);
                NotifyFirstFrameProcessed();
                UpdateDeviceStatus(DeviceStatusType.Complete, processingType, "Mesh updated");
            }
            return frameOk;
        }
        else
        {
            Debug.LogWarning($"No synchronized frame found for timestamp {targetTimestamp}");
            UpdateDeviceStatus(DeviceStatusType.Error, processingType, "No synchronized frame");
            return false;
        }
    }


    public int GetTotalFrameCount()
    {
        return totalFrameCount;
    }
    
    private void ResetParsers()
    {
        device.ResetParsers();
    }
    
    public ulong GetTimestampForFrame(int frameIndex)
    {
        return device.GetTimestampForFrame(frameIndex);
    }
    
    // Public methods for synchronized frame navigation
    public string GetDeviceName() => device.deviceName;
    
    public ulong GetCurrentTimestamp() => currentTimestamp;

    public int GetFps()
    {
        return device.GetFpsFromHeader();
    }
    
    public bool PeekNextTimestamp(out ulong timestamp)
    {
        return device.PeekNextTimestamp(out timestamp);
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
    public SensorDevice GetDevice() => device;

    void OnDestroy()
    {
        device?.Dispose();
    }
}
