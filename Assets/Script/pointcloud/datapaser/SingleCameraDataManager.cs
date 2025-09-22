using System;
using System.IO;
using UnityEngine;

public class SingleCameraDataManager : MonoBehaviour
{
    [SerializeField] private string dir;
    [SerializeField] private string hostname;
    [SerializeField] private string deviceName;

    RcstSensorDataParser depthParser;
    RcsvSensorDataParser colorParser;
    [SerializeField] private float depthScaleFactor = 1000f;

    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private DepthMeshGenerator depthMeshGenerator;
    private DepthToPointCloudGPU depthToPointCloudGPU; // New efficient processor


    private bool firstFrameProcessed = false;
    private bool autoLoadFirstFrame = false; // Disabled - MultiCamPointCloudManager handles first frame
    
    // Store current timestamp for efficient leading camera detection
    private ulong currentTimestamp = 0;
    private ExtrinsicsLoader extrisics;
    
    // Timeline scrubbing support
    private int currentFrameIndex = 0;
    private int totalFrameCount = -1;
    


    void Start()
    {
        SetupStatusUI.ShowStatus($"Initializing {deviceName}...");
        
        string devicePath = Path.Combine(dir, "dataset", hostname, deviceName);
        string depthFilePath = Path.Combine(devicePath, "camera_depth");
        string colorFilePath = Path.Combine(devicePath, "camera_color");

        if (!File.Exists(depthFilePath) || !File.Exists(colorFilePath))
        {
            string errorMsg = "指定されたファイルが存在しません: " + devicePath;
            Debug.LogError(errorMsg);
            SetupStatusUI.UpdateDeviceStatus(deviceName, "ERROR: Files not found");
            SetupStatusUI.ShowStatus($"Failed to initialize {deviceName}");
            return;
        }

        SetupStatusUI.UpdateDeviceStatus(deviceName, "Loading sensor data...");
        depthParser = (RcstSensorDataParser)SensorDataParserFactory.Create(depthFilePath, deviceName);
        colorParser = (RcsvSensorDataParser)SensorDataParserFactory.Create(colorFilePath, deviceName);

        SetupStatusUI.UpdateDeviceStatus(deviceName, "Loading extrinsics...");
        string extrinsicsPath = Path.Combine(dir, "calibration", "extrinsics.yaml");
        string serial = deviceName.Split('_')[^1];

        extrisics = new ExtrinsicsLoader(extrinsicsPath);
        if (!extrisics.IsLoaded)
        {
            string errorMsg = "Extrinsics data could not be loaded from: " + extrinsicsPath;
            Debug.LogError(errorMsg);
            SetupStatusUI.UpdateDeviceStatus(deviceName, "ERROR: Extrinsics failed");
            return;
        }

        float? loadedScale = extrisics.GetDepthScaleFactor(serial);
        if (loadedScale.HasValue)
        {
            depthScaleFactor = loadedScale.Value;
        }

        // --- DepthViewer 自動生成 ---
        string viewerName = $"DepthViewer_{deviceName}";
        depthViewer = new GameObject(viewerName);
        depthViewer.transform.SetParent(this.transform);

        // 視覚化 Gizmo を追加
        var gizmo = depthViewer.AddComponent<CameraPositionGizmo>();
        gizmo.gizmoColor = Color.red;
        gizmo.size = 0.1f;

        if (extrisics.TryGetGlobalTransform(serial, out Vector3 pos, out Quaternion rot))
        {
            // Debug.Log($"Applying global transform for {deviceName}: position = {pos:F6}, rotation = {rot.eulerAngles:F6}");

            // Unity用に座標系変換（右手系→左手系、Y軸下→Y軸上）
            Vector3 unityPosition = new Vector3(pos.x, pos.y, pos.z);

            // 回転はX軸180°回転を前掛けして上下反転と利き手系の変換を行う
            Quaternion unityRotation = rot;

            // 結果を確認
            Vector3 unityEuler = unityRotation.eulerAngles;
            // Debug.Log($"Unity Position: {unityPosition:F6}  Rotation (Euler): {unityEuler:F6}");

            depthViewer.transform.SetLocalPositionAndRotation(unityPosition, unityRotation);

            // Debug.Log($"Applied inverse transform for {deviceName} → position = {-(rot * pos)}, rotation = {Quaternion.Inverse(rot).eulerAngles}");
        }

        depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.AddComponent<MeshRenderer>();
        Material material = new(Shader.Find("Unlit/VertexColor"));
        material.SetFloat("_PointSize", 3.0f); // Set point size for macOS compatibility
        depthRenderer.material = material;

        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        // Load depth bias from configuration.yaml
        float depthBias = LoadDepthBias();
        
        depthMeshGenerator = new DepthMeshGenerator(deviceName);
        
        
        // Check for new binary processor compute shader first (most efficient)
        ComputeShader binaryComputeShader = Resources.Load<ComputeShader>("RawDepthToPointCloud");
        if (binaryComputeShader != null)
        {
            // Use new efficient binary processor
            depthToPointCloudGPU = new DepthToPointCloudGPU(deviceName);
            depthToPointCloudGPU.binaryDepthProcessor = binaryComputeShader;
            SetupStatusUI.UpdateDeviceStatus(deviceName, "[GPU-BINARY] Ultra-fast processing enabled");
        }
        else
        {
            // Fallback to original GPU processing
            ComputeShader computeShader = Resources.Load<ComputeShader>("DepthArrayToPointCloud");
            if (computeShader != null)
            {
                depthMeshGenerator.depthPixelProcessor = computeShader;
                SetupStatusUI.UpdateDeviceStatus(deviceName, "[GPU] Processing enabled");
            }
            else
            {
                Debug.LogWarning($"No compute shaders found, using CPU processing: {deviceName}");
                SetupStatusUI.UpdateDeviceStatus(deviceName, "[CPU] Processing (fallback)");
            }
        }
        
        // Setup processors
        depthMeshGenerator.setup(depthParser.sensorHeader, depthScaleFactor, depthBias);
        depthMeshGenerator.SetDepthViewerTransform(depthViewer.transform);
        
        if (depthToPointCloudGPU != null)
        {
            // Setup binary processor with both depth and color headers
            depthToPointCloudGPU.Setup(depthParser.sensorHeader, colorParser.sensorHeader, depthScaleFactor, depthBias);
            depthToPointCloudGPU.SetDepthViewerTransform(depthViewer.transform);
        }
        
        // Find and set bounding volume
        Transform boundingVolume = GameObject.Find("BoundingVolume")?.transform;
        if (boundingVolume != null)
        {
            depthMeshGenerator.SetBoundingVolume(boundingVolume);
            if (depthToPointCloudGPU != null)
            {
                depthToPointCloudGPU.SetBoundingVolume(boundingVolume);
            }
            // Debug.Log($"BoundingVolume found and set for {deviceName}");
        }   
        else
        {
            Debug.LogWarning("BoundingVolume GameObject not found in hierarchy");
        }
        if (extrisics.TryGetDepthToColorTransform(serial, out Vector3 d2cTranslation, out Quaternion d2cRotation))
        {
            // Debug.Log($"Depth to Color transform for {serial}: translation = {d2cTranslation}, rotation = {d2cRotation.eulerAngles}");
            depthMeshGenerator.ApplyDepthToColorExtrinsics(d2cTranslation, d2cRotation);
            
            // Apply same transform to binary processor
            if (depthToPointCloudGPU != null)
            {
                depthToPointCloudGPU.ApplyDepthToColorExtrinsics(d2cTranslation, d2cRotation);
            }
        }
        else
        {
            Debug.LogError($"Failed to get depth to color transform for {serial}");
            SetupStatusUI.UpdateDeviceStatus(deviceName, "ERROR: Transform failed");
            return;
        }
        
        SetupStatusUI.UpdateDeviceStatus(deviceName, "Finalizing setup...");
        depthMeshGenerator.SetupColorIntrinsics(colorParser.sensorHeader);

        
        // Set reasonable defaults for timeline support without expensive counting
        totalFrameCount = -1; // Unknown, will be estimated
        
        
        SetupStatusUI.UpdateDeviceStatus(deviceName, "Ready - waiting for first frame");
        SetupStatusUI.ShowStatus($"Setup complete for {deviceName}");
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



    private float LoadDepthBias()
    {
        string configPath = Path.Combine(dir, "configuration.yaml");
        
        if (!File.Exists(configPath))
        {
            Debug.LogWarning($"configuration.yaml not found at {configPath}, using depthBias = 0");
            return 0f;
        }

        try
        {
            string yamlText = File.ReadAllText(configPath);
            
            // Simple parsing to find depthBias value
            string[] lines = yamlText.Split('\n');
            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("depthBias:"))
                {
                    string[] parts = line.Split(':');
                    if (parts.Length > 1 && float.TryParse(parts[1].Trim(), out float bias))
                    {
                        // Debug.Log($"Loaded depthBias: {bias} from {configPath}");
                        return bias;
                    }
                }
            }
            
            Debug.LogWarning("depthBias not found in configuration.yaml, using 0");
            return 0f;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load configuration.yaml: {ex.Message}");
            return 0f;
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
            ulong targetTimestamp = GetTimestampForFrame(frameIndex);
            bool success = SeekToTimestampInternal(targetTimestamp);
            
            if (success)
            {
                currentFrameIndex = frameIndex;
            }
            else
            {
                Debug.LogWarning($"{deviceName}: Failed to seek to frame {frameIndex}");
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
            Debug.Log($"{deviceName}: Seek to timestampp{targetTimestamp}");
            bool success = SeekToTimestampInternal(targetTimestamp);
            
            if (!success)
            {
                Debug.LogWarning($"{deviceName}: No suitable frame found for timestamp {targetTimestamp}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error in SeekToTimestamp: {ex.Message}");
        }
    }
    
    public ulong GetTimestampForFrame(int frameIndex)
    {
        if (frameIndex < 0) return 0;
        
        try
        {
            if (depthParser == null || colorParser == null)
            {
                Debug.LogError("Parsers not initialized");
                return 0;
            }
            
            // Reset parsers to beginning
            ResetParsers();
            
            int currentFrame = 0;
            // Synchronization tolerance is now handled by SensorSynchronizer
            
            while (currentFrame <= frameIndex)
            {
                // Check synchronization using unified method
                bool synchronized = CheckSynchronization(depthParser, colorParser, out ulong depthTs, out ulong colorTs, out long delta);
                if (!synchronized && depthTs == 0 && colorTs == 0) break; // No more data
                
                if (synchronized)
                {
                    if (currentFrame == frameIndex)
                    {
                        return depthTs; // Return the timestamp for this frame
                    }
                    
                    // Skip to next frame
                    depthParser.SkipCurrentRecord();
                    colorParser.SkipCurrentRecord();
                    currentFrame++;
                }
                else
                {
                    // Skip the earlier timestamp to catch up
                    if (delta < 0)
                    {
                        depthParser.SkipCurrentRecord();
                    }
                    else
                    {
                        colorParser.SkipCurrentRecord();
                    }
                }
            }
            
            // Fallback: estimate based on FPS from header
            int fps = GetFpsFromHeader();
            if (fps > 0)
            {
                // Calculate nanoseconds per frame: 1,000,000,000 / fps
                ulong nanosecondsPerFrame = (ulong)(1_000_000_000L / fps);
                return (ulong)frameIndex * nanosecondsPerFrame;
            }
            else
            {
                Debug.LogError($"Cannot estimate timestamp for frame {frameIndex}: FPS not available from header");
                return 0; // Error case - cannot estimate without FPS
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error in GetTimestampForFrame: {ex.Message}");
            return (ulong)(frameIndex * 33333333); // nanoseconds
        }
    }
    
    public int GetTotalFrameCount()
    {
        return totalFrameCount;
    }
    
    public int GetFpsFromHeader()
    {
        int? fps = colorParser?.sensorHeader?.custom?.camera_sensor?.fps;
        if (fps.HasValue)
        {
            return fps.Value;
        }
        
        string errorMsg = $"FPS not available in sensor header for device {deviceName}. " +
                         "This may cause timeline synchronization issues.";
        Debug.LogError(errorMsg);
        SetupStatusUI.ShowStatus($"ERROR: {errorMsg}");
        
        // Return -1 to indicate error instead of defaulting to magic number
        return -1;
    }
    
    public int GetCurrentFrameIndex()
    {
        return currentFrameIndex;
    }
    
    private void ResetParsers()
    {
        // Reset parsers to beginning by recreating them (since we don't have a Reset method)
        if (depthParser != null && colorParser != null)
        {
            depthParser.Dispose();
            colorParser.Dispose();

            string devicePath = Path.Combine(dir, "dataset", hostname, deviceName);
            string depthFilePath = Path.Combine(devicePath, "camera_depth");
            string colorFilePath = Path.Combine(devicePath, "camera_color");

            depthParser = (RcstSensorDataParser)SensorDataParserFactory.Create(depthFilePath, deviceName);
            colorParser = (RcsvSensorDataParser)SensorDataParserFactory.Create(colorFilePath, deviceName);
        }
    }
    
    
    // Simplified timestamp-only seeking logic
    private bool SeekToTimestampInternal(ulong targetTimestamp)
    {
        if (depthParser == null || colorParser == null)
        {
            Debug.LogError("Parsers not initialized");
            return false;
        }

        // Reset parsers to beginning
        ResetParsers();
        
        while (true)
        {
            // Check synchronization using unified method
            bool synchronized = CheckSynchronization(depthParser, colorParser, out ulong depthTs, out ulong colorTs, out long delta);
            if (!synchronized && depthTs == 0 && colorTs == 0) break; // No more data
            
            if (synchronized)
            {   
                
                // Check if we've reached or passed the target timestamp
                if (depthTs >= targetTimestamp)
                {
                    SetupStatusUI.ShowStatus($"Seeking {deviceName}: depthTs={depthTs}, colorTs={colorTs}, delta={delta}");
                    // Process the current frame
                    bool success = ProcessFrameWithParsers(depthParser, colorParser, depthTs, showStatus: false);
                    if (success)
                    {
                        Debug.Log($"{deviceName}: Seeked to timestamp {targetTimestamp} (actual: {depthTs})");
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"{deviceName}: Failed to process frame at timestamp {targetTimestamp} (actual: {depthTs})");
                        return false;
                    }
                }

                // Skip to next synchronized frame pair
                // SetupStatusUI.ShowStatus($"Skipping {deviceName}: synchronized frame at {depthTs} to target {targetTimestamp}");
                depthParser.SkipCurrentRecord();
                colorParser.SkipCurrentRecord();
            }
            else
            {
                // Skip the earlier timestamp to catch up
                if (delta < 0)
                {
                    depthParser.SkipCurrentRecord();
                }
                else
                {
                    colorParser.SkipCurrentRecord();
                }
            }
        }
        
        return false;
    }

    
    // Unified frame processing logic to eliminate code duplication  
    private bool ProcessFrameWithParsers(RcstSensorDataParser depthParser, RcsvSensorDataParser colorParser, ulong frameTimestamp, bool showStatus = false)
    {
        // Synchronization tolerance is now handled by SensorSynchronizer
        // Debug.Log($"Processing frame at timestamp {frameTimestamp} for {deviceName}");
        if (showStatus) SetupStatusUI.ShowStatus($"Processing frame for {deviceName}...");
        
        // Synchronization is already checked in SeekToTarget before calling this method
        bool useGPUOptimization = depthToPointCloudGPU != null;
        
        if (showStatus) SetupStatusUI.UpdateDeviceStatus(deviceName, "Parsing synchronized frame...");
        
        // Parse both frames with GPU optimization
        bool depthOk = depthParser.ParseNextRecord(optimizeForGPU: useGPUOptimization);
        bool colorOk = colorParser.ParseNextRecord(optimizeForGPU: useGPUOptimization);
        bool frameOk = depthOk && colorOk;
        
        if (showStatus) SetupStatusUI.UpdateDeviceStatus(deviceName, "frame data parsed");

        if (frameOk)
        {
            if (showStatus) SetupStatusUI.UpdateDeviceStatus(deviceName, "Start Update Mesh");
            
            // Use binary processor if available (most efficient)
            if (depthToPointCloudGPU != null)
            {
                // Get raw binary data and color texture directly (no CPU conversion needed)
                var depthRecordBytes = depthParser.GetLatestRecordBytes();
                var colorTexture = colorParser.GetLatestColorTexture();
                int metadataSize = depthParser.sensorHeader.MetadataSize;
                
                depthToPointCloudGPU.UpdateMeshFromRawBinary(depthMesh, depthRecordBytes, colorTexture, metadataSize);
            }
            else
            {
                // Fallback to standard processing (get color pixels only when needed)
                var _color = colorParser.CurrentColorPixels;
                if (_color != null)
                {
                    var _depth = depthParser.GetLatestDepthValues();
                    if (_depth != null && _depth.Length > 0)
                    {
                        depthMeshGenerator.UpdateMeshFromDepthAndColor(depthMesh, _depth, _color);
                    }
                }
            }

            // Store the current timestamp for efficient leading camera detection
            currentTimestamp = frameTimestamp;
            
            if (showStatus)
            {
                SetupStatusUI.ShowStatus($"Processed frame for {deviceName}");

                if (!firstFrameProcessed)
                {
                    SetupStatusUI.UpdateDeviceStatus(deviceName, "[OK] Active - processing frames");
                    SetupStatusUI.ShowStatus($"{deviceName} is now rendering point clouds");
                    SetupStatusUI.OnFirstFrameProcessed();
                    firstFrameProcessed = true; // Mark first frame as processed
                }
            }
            
            return true;
        }
        
        return false;
    }
    
    // Unified synchronization logic
    private bool CheckSynchronization(RcstSensorDataParser depthParser, RcsvSensorDataParser colorParser, out ulong depthTs, out ulong colorTs, out long delta)
    {
        bool hasDepthTs = depthParser.PeekNextTimestamp(out depthTs);
        bool hasColorTs = colorParser.PeekNextTimestamp(out colorTs);

        if (!hasDepthTs || !hasColorTs)
        {
            depthTs = 0;
            colorTs = 0;
            delta = 0;
            return false;
        }

        delta = (long)depthTs - (long)colorTs;

        // Calculate FPS-based synchronization threshold (use 25% of frame duration)
        int fps = GetFpsFromHeader();
        long syncThreshold;
        if (fps > 0)
        {
            // 25% of frame duration in nanoseconds: (1,000,000,000 / fps) * 0.25
            syncThreshold = (long)(250_000_000L / fps);
        }
        else
        {
            // Fallback to a reasonable default if FPS unavailable (assume 30 FPS)
            syncThreshold = 8_333_333; // 25% of 33.33ms frame at 30 FPS
        }

        return Math.Abs(delta) <= syncThreshold;
    }
    
    // Public methods for synchronized frame navigation
    public string GetDeviceName() => deviceName;
    
    public ulong GetCurrentTimestamp() => currentTimestamp;
    
    public bool PeekNextTimestamp(out ulong timestamp)
    {
        timestamp = 0;
        if (depthParser == null) return false;
        return depthParser.PeekNextTimestamp(out timestamp);
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
    
    void OnDestroy()
    {
        // Clean up resources
        depthParser?.Dispose();
        colorParser?.Dispose();
        depthToPointCloudGPU?.Dispose();
    }
}
