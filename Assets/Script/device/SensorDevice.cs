using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

public enum DeviceStatusType
{
    Error,
    Loading,
    Ready,
    Active,
    Processing,
    Complete
}

public enum ProcessingType
{
    None,
    CPU,
    GPU,
    ONESHADER,
    PLY,
    PLY_WITH_MOTION
}

[Serializable]
public class SensorDevice
{
    public string deviceName;
    public DeviceStatusType statusType;
    public ProcessingType processingType;
    public string statusMessage;
    public DateTime lastUpdated;

    string dir;
    string hostname;
    string devicePath;
    string depthFilePath;
    string colorFilePath;
    private const float DEFAULT_DEPTH_SCALE = 1000f;

    private float depthScaleFactor = DEFAULT_DEPTH_SCALE;

    private float depthBias = 0f;

    // Camera parameters (centralized from BasePointCloudProcessor)
    private int depthWidth, depthHeight;
    private int colorWidth, colorHeight;
    private float[] depthIntrinsics; // fx, fy, cx, cy
    private float[] colorIntrinsics; // fx, fy, cx, cy
    private float[] depthDistortion; // k1~k6, p1, p2
    private float[] colorDistortion; // k1~k6, p1, p2
    private Vector2[,] depthUndistortLUT;
    private Quaternion depthToColorRotation = Quaternion.identity;
    private Vector3 depthToColorTranslation = Vector3.zero;

    RcstSensorDataParser depthParser;
    RcsvSensorDataParser colorParser;

    public void setup(string dir, string hostname, string deviceName)
    {
        this.dir = dir;
        this.hostname = hostname;
        this.deviceName = deviceName;

        devicePath = Path.Combine(dir, "dataset", hostname, deviceName);
        depthFilePath = Path.Combine(devicePath, "camera_depth");
        colorFilePath = Path.Combine(devicePath, "camera_color");

        depthParser = (RcstSensorDataParser)SensorDataParserFactory.Create(depthFilePath, deviceName);
        colorParser = (RcsvSensorDataParser)SensorDataParserFactory.Create(colorFilePath, deviceName);

        statusType = DeviceStatusType.Loading;
        processingType = ProcessingType.None;
        statusMessage = "Initialized";
        lastUpdated = DateTime.Now;

        LoadDepthBias(); // Load depth bias from configuration.yaml (if exists

        // Initialize camera parameters
        InitializeCameraParameters();

        // Load extrinsics
        LoadExtrinsics();
    }


    private void LoadDepthBias()
    {
        string configPath = Path.Combine(dir, "configuration.yaml");

        if (!File.Exists(configPath))
        {
            Debug.LogWarning($"configuration.yaml not found at {configPath}, using depthBias = 0");
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
                        depthBias = bias;
                    }
                }
            }

        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load configuration.yaml: {ex.Message}");
        }
    }

    public void UpdateDeviceStatus(DeviceStatusType newStatusType, ProcessingType newProcessingType = ProcessingType.None, string newStatusMessage = "")
    {
        this.statusType = newStatusType;
        this.processingType = newProcessingType;
        this.statusMessage = newStatusMessage;
        this.lastUpdated = DateTime.Now;
        SetupStatusUI.UpdateDeviceStatus(this);
    }


    public SensorDevice UpdateStatus(string statusString)
    {
        this.statusMessage = statusString;
        this.lastUpdated = DateTime.Now;
        return this;
    }

    public bool PeekNextTimestamp(out ulong timestamp)
    {
        timestamp = 0;
        if (depthParser == null) return false;
        return depthParser.PeekNextTimestamp(out timestamp);
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
                bool synchronized = CheckSynchronization(out ulong depthTs, out ulong colorTs, out long delta);
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

    public bool ParseRecord(bool useGPUOptimization)
    {
        // Parse both frames with GPU optimization
        bool depthOk = depthParser.ParseNextRecord(optimizeForGPU: useGPUOptimization);
        bool colorOk = colorParser.ParseNextRecord(optimizeForGPU: useGPUOptimization);

        // bool depthOk = await Task.Run(() => depthParser.ParseNextRecord(optimizeForGPU: useGPUOptimization));
        // bool colorOk = await Task.Run(() => colorParser.ParseNextRecord(optimizeForGPU: useGPUOptimization));
        return depthOk && colorOk;
    }


    /// <summary>
    /// Async version - runs parsing in background thread, returns to main thread for result
    /// </summary>
    // public bool ParseRecord(bool useGPUOptimization)
    // {
    //     // Parse both frames with GPU optimization on background thread
    //     bool depthOk = depthParser.ParseNextRecord(optimizeForGPU: useGPUOptimization);
    //     bool colorOk = colorParser.ParseNextRecord(optimizeForGPU: useGPUOptimization);
    //     return depthOk && colorOk;
    // }

    public bool UpdateTexture(bool useGPUOptimization)
    {
        return colorParser.DecodeTexture(optimizeForGPU: useGPUOptimization);
    }

    public void ResetParsers()
    {
        // Reset parsers to beginning by recreating them (since we don't have a Reset method)
        if (depthParser != null && colorParser != null)
        {
            depthParser.Dispose();
            colorParser.Dispose();

            depthParser = (RcstSensorDataParser)SensorDataParserFactory.Create(depthFilePath, deviceName);
            colorParser = (RcsvSensorDataParser)SensorDataParserFactory.Create(colorFilePath, deviceName);
        }
    }

    // Unified synchronization logic
    public bool CheckSynchronization(out ulong depthTs, out ulong colorTs, out long delta)
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
            // 25% of frame duration in nanoseconds: (1,000,000 / fps) * 0.25
            syncThreshold = (long)(250_000L / fps);
        }
        else
        {
            // Fallback to a reasonable default if FPS unavailable (assume 30 FPS)
            syncThreshold = 8_333; // 25% of 33.33ms frame at 30 FPS
        }

        return Math.Abs(delta) <= syncThreshold;
    }

    public void SkipCurrentRecord()
    {
        depthParser.SkipCurrentRecord();
        colorParser.SkipCurrentRecord();
    }

    public void SkipColorRecord()
    {
        colorParser.SkipCurrentRecord();
    }

    public void SkipDepthRecord()
    {
        depthParser.SkipCurrentRecord();
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

    public string GetDisplayString()
    {
        string processingPrefix = processingType switch
        {
            ProcessingType.CPU => "[CPU] ",
            ProcessingType.GPU => "[GPU] ",
            _ => ""
        };

        string statusPrefix = statusType switch
        {
            DeviceStatusType.Active => "[OK] ",
            DeviceStatusType.Error => "ERROR: ",
            _ => ""
        };

        return $"{processingPrefix}{statusPrefix}{statusMessage}";
    }

    public void Dispose()
    {
        depthParser?.Dispose();
        colorParser?.Dispose();
    }

    public string GetDir() => dir;
    public string GetHostname() => hostname;
    public string GetDevicePath() => devicePath;
    public string GetDepthFilePath() => depthFilePath;
    public string GetColorFilePath() => colorFilePath;
    public string GetDeviceName() => deviceName;
    public float GetDepthBias() => depthBias;
    public uint[] GetLatestDepthData() => depthParser?.GetLatestDepthUints();
    public Texture2D GetLatestColorTexture() => colorParser?.GetLatestColorTexture();

    public Color32[] GetLatestColorData() => colorParser?.GetLatestColorPixels();
    public ushort[] GetLatestDepthValues() => depthParser?.GetLatestDepthValues();
    public int GetMetaDataSize() => depthParser == null ? 0 : depthParser.sensorHeader.MetadataSize;
    public RcstSensorDataParser GetDepthParser() => depthParser;
    public RcsvSensorDataParser GetColorParser() => colorParser;
    public float GetDepthScaleFactor() => depthScaleFactor;
    public void SetDepthScaleFactor(float scale) => depthScaleFactor = scale;

    // Camera parameter getters
    public int GetDepthWidth() => depthWidth;
    public int GetDepthHeight() => depthHeight;
    public int GetColorWidth() => colorWidth;
    public int GetColorHeight() => colorHeight;
    public float[] GetDepthIntrinsics() => depthIntrinsics;
    public float[] GetColorIntrinsics() => colorIntrinsics;
    public float[] GetDepthDistortion() => depthDistortion;
    public float[] GetColorDistortion() => colorDistortion;
    public Vector2[,] GetDepthUndistortLUT() => depthUndistortLUT;
    public Quaternion GetDepthToColorRotation() => depthToColorRotation;
    public Vector3 GetDepthToColorTranslation() => depthToColorTranslation;


    private void InitializeCameraParameters()
    {
        if (depthParser?.sensorHeader == null || colorParser?.sensorHeader == null)
        {
            Debug.LogError($"Cannot initialize camera parameters for {deviceName}: sensor headers not available");
            return;
        }

        var depthHeader = depthParser.sensorHeader;
        var colorHeader = colorParser.sensorHeader;

        // Image dimensions
        depthWidth = depthHeader.custom.camera_sensor.width;
        depthHeight = depthHeader.custom.camera_sensor.height;
        colorWidth = colorHeader.custom.camera_sensor.width;
        colorHeight = colorHeader.custom.camera_sensor.height;

        // Parse camera intrinsics using YamlLoader
        var depthParams = YamlLoader.ParseIntrinsics(depthHeader.custom.additional_info.orbbec_intrinsics_parameters);
        var colorParams = YamlLoader.ParseIntrinsics(colorHeader.custom.additional_info.orbbec_intrinsics_parameters);

        // Split intrinsics and distortion parameters
        depthIntrinsics = depthParams.Take(4).ToArray(); // fx, fy, cx, cy
        depthDistortion = depthParams.Skip(4).ToArray(); // k1~k6, p1, p2
        colorIntrinsics = colorParams.Take(4).ToArray(); // fx, fy, cx, cy
        colorDistortion = colorParams.Skip(4).ToArray(); // k1~k6, p1, p2

        // Build undistortion LUT
        depthUndistortLUT = OpenCVUndistortHelper.BuildUndistortLUTFromHeader(depthHeader);

        Debug.Log($"Camera parameters initialized for {deviceName}: Depth({depthWidth}x{depthHeight}), Color({colorWidth}x{colorHeight})");
    }

    private void LoadExtrinsics()
    {
        string extrinsicsPath = Path.Combine(dir, "calibration", "extrinsics.yaml");
        string serial = deviceName.Split('_')[^1];

        ExtrinsicsLoader extrinsics = new ExtrinsicsLoader(extrinsicsPath);
        if (!extrinsics.IsLoaded)
        {
            Debug.LogError($"Extrinsics data could not be loaded from: {extrinsicsPath}");
            return;
        }

        if (extrinsics.TryGetDepthToColorTransform(serial, out Vector3 d2cTranslation, out Quaternion d2cRotation))
        {
            depthToColorTranslation = d2cTranslation;
            depthToColorRotation = d2cRotation;
        }

        float? loadedScale = extrinsics.GetDepthScaleFactor(serial);
        if (loadedScale.HasValue)
        {
            depthScaleFactor = loadedScale.Value;
        }
    }

    public bool TryGetGlobalTransform(out Vector3 position, out Quaternion rotation)
    {
        string extrinsicsPath = Path.Combine(dir, "calibration", "extrinsics.yaml");
        string serial = deviceName.Split('_')[^1];

        ExtrinsicsLoader extrinsics = new ExtrinsicsLoader(extrinsicsPath);
        if (!extrinsics.IsLoaded)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        return extrinsics.TryGetGlobalTransform(serial, out position, out rotation);
    }

    public CameraMetadata CreateCameraMetadata(Transform depthViewerTransform = null)
    {
        CameraMetadata metadata = new CameraMetadata();

        // Transform matrices
        metadata.d2cRotation = Matrix4x4.Rotate(depthToColorRotation);
        metadata.d2cTranslation = depthToColorTranslation;
        if (depthViewerTransform != null)
        {
            metadata.depthViewerTransform = depthViewerTransform.localToWorldMatrix;
        }

        // Camera intrinsics
        metadata.fx_d = depthIntrinsics[0];
        metadata.fy_d = depthIntrinsics[1];
        metadata.cx_d = depthIntrinsics[2];
        metadata.cy_d = depthIntrinsics[3];
        metadata.fx_c = colorIntrinsics[0];
        metadata.fy_c = colorIntrinsics[1];
        metadata.cx_c = colorIntrinsics[2];
        metadata.cy_c = colorIntrinsics[3];

        // Color distortion parameters (matches ComputeShader layout: k1, k2, p1, p2, k3, k4, k5, k6)
        // Note: Depth distortion is pre-computed in LUT, not sent to GPU
        if (colorDistortion != null && colorDistortion.Length >= 8)
        {
            metadata.k1_c = colorDistortion[0]; // k1
            metadata.k2_c = colorDistortion[1]; // k2
            metadata.p1_c = colorDistortion[6]; // p1
            metadata.p2_c = colorDistortion[7]; // p2
            metadata.k3_c = colorDistortion[2]; // k3
            metadata.k4_c = colorDistortion[3]; // k4
            metadata.k5_c = colorDistortion[4]; // k5
            metadata.k6_c = colorDistortion[5]; // k6
        }

        // Image dimensions
        metadata.depthWidth = (uint)depthWidth;
        metadata.depthHeight = (uint)depthHeight;
        metadata.colorWidth = (uint)colorWidth;
        metadata.colorHeight = (uint)colorHeight;

        // Processing parameters
        metadata.depthScaleFactor = depthScaleFactor;
        metadata.depthBias = depthBias;
        metadata.useOpenCVLUT = 1;

        // Bounding volume parameters
        // Note: Bounding volume will be set by processor if available
        metadata.hasBoundingVolume = 0; // Will be updated by processor
        metadata.showAllPoints = PointCloudSettings.showAllPoints ? 1 : 0;
        metadata.boundingVolumeInverseTransform = Matrix4x4.identity; // Will be updated by processor

        return metadata;
    }

}