using System;
using System.IO;
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
    GPU_Binary
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

            Debug.LogWarning("depthBias not found in configuration.yaml, using 0");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load configuration.yaml: {ex.Message}");
        }
    }

    public SensorDevice UpdateStatus(DeviceStatusType newStatusType, ProcessingType newProcessingType = ProcessingType.None, string newStatusMessage = "")
    {
        this.statusType = newStatusType;
        this.processingType = newProcessingType;
        this.statusMessage = newStatusMessage;
        this.lastUpdated = DateTime.Now;
        return this;
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
        return depthOk && colorOk;
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
            ProcessingType.GPU => "[GPU] ",
            ProcessingType.CPU => "[CPU] ",
            ProcessingType.GPU_Binary => "[GPU-BINARY] ",
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
    public byte[] GetLatestDepthData() => depthParser?.GetLatestRecordBytes();
    public Texture2D GetLatestColorTexture() => colorParser?.GetLatestColorTexture();

    public Color32[] GetLatestColorData() => colorParser?.GetLatestColorPixels();
    public ushort[] GetLatestDepthValues() => depthParser?.GetLatestDepthValues();
    public int GetMetaDataSize() => depthParser == null ? 0 : depthParser.sensorHeader.MetadataSize;
    public RcstSensorDataParser GetDepthParser() => depthParser;
    public RcsvSensorDataParser GetColorParser() => colorParser;
    public float GetDepthScaleFactor() => depthScaleFactor;
    public void SetDepthScaleFactor(float scale) => depthScaleFactor = scale;


}