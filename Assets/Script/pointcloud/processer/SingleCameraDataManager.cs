using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class SingleCameraDataManager : MonoBehaviour
{
    // Configuration constants
    private const float DEFAULT_POINT_SIZE = 3.0f;
    private const float GIZMO_SIZE = 0.1f;

    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private IPointCloudProcessor pointCloudProcessor;
    private SensorDevice device;

    private bool firstFrameProcessed = false;
    private bool autoLoadFirstFrame = true; // Enabled - each manager loads its own first frame
    
    // Store current timestamp for efficient leading camera detection
    private ulong currentTimestamp = 0;
    private ExtrinsicsLoader extrisics;
    
    // Timeline scrubbing support
    private int currentFrameIndex = -1;
    private int totalFrameCount = -1;

    public void Initialize(string dir, string hostname, string deviceName)
    {
        this.device = new SensorDevice();
        this.device.setup(dir, hostname, deviceName);
    }

    void Start()
    {
        SetupStatusUI.UpdateDeviceStatus(device);
        if (!LoadExtrinsicsAndScale()) return;
        SetupDepthViewer();
        SetupProcessors();
        ConfigureBoundingVolume();
        ConfigureTransforms();
        FinalizeSetup();
    
    }

    private bool LoadExtrinsicsAndScale()
    {
        UpdateDeviceStatus(DeviceStatusType.Loading, ProcessingType.None, "Loading extrinsics...");
        string extrinsicsPath = Path.Combine(device.GetDir(), "calibration", "extrinsics.yaml");
        string serial = device.GetDeviceName().Split('_')[^1];

        extrisics = new ExtrinsicsLoader(extrinsicsPath);
        if (!extrisics.IsLoaded)
        {
            string errorMsg = "Extrinsics data could not be loaded from: " + extrinsicsPath;
            Debug.LogError(errorMsg);
            UpdateDeviceStatus(DeviceStatusType.Error, ProcessingType.None, "Extrinsics failed");
            return false;
        }

        float? loadedScale = extrisics.GetDepthScaleFactor(serial);
        if (loadedScale.HasValue)
        {
            device.SetDepthScaleFactor(loadedScale.Value);
        }
        return true;
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
        string serial = device.deviceName.Split('_')[^1];
        if (extrisics.TryGetGlobalTransform(serial, out Vector3 pos, out Quaternion rot))
        {
            Vector3 unityPosition = new Vector3(pos.x, pos.y, pos.z);
            Quaternion unityRotation = rot;
            depthViewer.transform.SetLocalPositionAndRotation(unityPosition, unityRotation);
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

    private void SetupProcessors()
    {
        // Load depth bias from configuration.yaml
        float depthBias = device.GetDepthBias();

        // Create the best available processor using factory pattern
        pointCloudProcessor = PointCloudProcessorFactory.CreateBestProcessor(device.deviceName);

        UpdateDeviceStatus(DeviceStatusType.Loading, pointCloudProcessor.ProcessingType,
                          $"{pointCloudProcessor.ProcessingType} processing enabled");

        // Setup the processor with all required parameters
        pointCloudProcessor.Setup(device, depthBias);

        pointCloudProcessor.SetDepthViewerTransform(depthViewer.transform);
    }

    private void UpdateDeviceStatus(DeviceStatusType loading, ProcessingType gPU_Binary, string v)
    {
        device.statusType = loading;
        device.processingType = gPU_Binary;
        device.statusMessage = v;
        device.lastUpdated = DateTime.Now;
        SetupStatusUI.UpdateDeviceStatus(device);
    }

    private void ConfigureBoundingVolume()
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

    private void ConfigureTransforms()
    {
        string serial = device.deviceName.Split('_')[^1];
        if (extrisics.TryGetDepthToColorTransform(serial, out Vector3 d2cTranslation, out Quaternion d2cRotation))
        {
            pointCloudProcessor.ApplyDepthToColorExtrinsics(d2cTranslation, d2cRotation);
        }
        else
        {
            Debug.LogError($"Failed to get depth to color transform for {serial}");
            UpdateDeviceStatus(DeviceStatusType.Error, ProcessingType.None, "Transform failed");
            return;
        }

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

    void Update()
    {
        // Auto-load first frame on startup (disabled - MultiCamPointCloudManager handles this)
        if (autoLoadFirstFrame && !firstFrameProcessed)
        {
            SeekToFrame(0);
            autoLoadFirstFrame = false; // Prevent auto-loading again
        }
    }
    
    
    public void SeekToFrame(int frameIndex)
    {
        if (frameIndex < 0) frameIndex = 0;
        if (totalFrameCount > 0 && frameIndex >= totalFrameCount) 
            frameIndex = totalFrameCount - 1;
            
        if (frameIndex == currentFrameIndex) return;
        
        try
        {
            // Convert frame index to timestamp for unified seeking
            ulong targetTimestamp = device.GetTimestampForFrame(frameIndex);
            bool success = SeekToTimestampInternal(targetTimestamp);
            
            if (success)
            {
                currentFrameIndex = frameIndex;
            }
            else
            {
                Debug.LogWarning($"{device.deviceName}: Failed to seek to frame {frameIndex}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error in SeekToFrame: {ex.Message}");
        }
    }
    
    public void ResetToFirstFrame()
    {
        Debug.Log("Reset To First Frame");
        SeekToFrame(0);
    }
    
    public void SeekToTimestamp(ulong targetTimestamp)
    {
        try
        {
            Debug.Log($"{device.deviceName}: Seek to timestamp {targetTimestamp}");
            bool success = SeekToTimestampInternal(targetTimestamp);
            
            if (!success)
            {
                Debug.LogWarning($"{device.deviceName}: No suitable frame found for timestamp {targetTimestamp}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error in SeekToTimestamp: {ex.Message}");
        }
    }
    
           // Simplified timestamp-only seeking logic
    public bool SeekToTimestampInternal(ulong targetTimestamp)
    {
        // Reset parsers to beginning
        device.ResetParsers();
        
        while (true)
        {
            // Check synchronization using unified method
            bool synchronized = device.CheckSynchronization(out ulong depthTs, out ulong colorTs, out long delta);
            if (!synchronized && depthTs == 0 && colorTs == 0) break; // No more data
            
            if (synchronized)
            {   
                
                // Check if we've reached or passed the target timestamp
                if (depthTs >= targetTimestamp)
                {
                    // Process the current frame
                    bool success = ProcessFrameWithParsers(depthTs, showStatus: true);
                    if (success)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                device.SkipCurrentRecord();
            }
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
        
        return false;
    }

    // Unified frame processing logic using the interface
    private bool ProcessFrameWithParsers(ulong frameTimestamp, bool showStatus = false)
    {
        if (showStatus) SetupStatusUI.ShowStatus($"Processing frame for {device.deviceName}...");

        // Determine optimization based on processor type
        bool useGPUOptimization = pointCloudProcessor.ProcessingType == ProcessingType.GPU;

        if (showStatus) UpdateDeviceStatus(DeviceStatusType.Processing, pointCloudProcessor.ProcessingType, "Parsing synchronized frame...");

        bool frameOk = device.ParseRecord(useGPUOptimization);

        if (showStatus) UpdateDeviceStatus(DeviceStatusType.Processing, pointCloudProcessor.ProcessingType, "Frame data parsed");

        if (frameOk)
        {
            if (showStatus) UpdateDeviceStatus(DeviceStatusType.Processing, pointCloudProcessor.ProcessingType, "Updating mesh...");

            // Use the unified interface - no more branching logic!
            pointCloudProcessor.UpdateMesh(depthMesh, device);

            // Store the current timestamp for efficient leading camera detection
            currentTimestamp = frameTimestamp;

            if (showStatus)
            {
                UpdateDeviceStatus(DeviceStatusType.Complete, pointCloudProcessor.ProcessingType, "Frame processed");
                if (!firstFrameProcessed)
                {
                    SetupStatusUI.OnFirstFrameProcessed();
                    firstFrameProcessed = true; // Mark first frame as processed
                }
            }

            return true;
        }

        return false;
    }


    public int GetTotalFrameCount()
    {
        return totalFrameCount;
    }
    
    public int GetCurrentFrameIndex()
    {
        return currentFrameIndex;
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
        return pointCloudProcessor?.ProcessingType ?? ProcessingType.CPU;
    }

    void OnDestroy()
    {
        device?.Dispose();
        pointCloudProcessor?.Dispose();
    }
}
