using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;
using YamlDotNet.Serialization;
using PointCloud;

public class MultiCameraPointCloudManager : MonoBehaviour
{
    [SerializeField]
    private string rootDirectory; // datasetを含むディレクトリ

    private List<GameObject> parserObjects = new();
    
    // Async processing infrastructure
    private readonly List<CameraProcessor> cameraProcessors = new();
    private readonly ConcurrentDictionary<string, FrameResult> completedFrames = new();
    private CancellationTokenSource cancellationTokenSource;
    private volatile bool isProcessing = false;

    void Start()
    {
        SetupStatusUI.ShowStatus("Starting Multi-Camera Point Cloud Manager...");
        
        // Disable timeline auto-play
        var playableDirector = FindObjectOfType<UnityEngine.Playables.PlayableDirector>();
        if (playableDirector != null && playableDirector.playOnAwake)
        {
            playableDirector.Stop();
            Debug.Log("Timeline auto-play disabled");
        }
        
        SetupStatusUI.ShowStatus("Timeline auto-play disabled");
        
        SetupStatusUI.ShowStatus("Looking for dataset directory...");
        string datasetPath = Path.Combine(rootDirectory, "dataset");
        if (!Directory.Exists(datasetPath))
        {
            string errorMsg = $"dataset ディレクトリが見つかりません: {datasetPath}";
            Debug.LogError(errorMsg);
            SetupStatusUI.ShowStatus($"ERROR: {errorMsg}");
            return;
        }

        string hostDir = Directory.GetDirectories(datasetPath).FirstOrDefault();
        if (hostDir == null)
        {
            Debug.LogError("ホストディレクトリが dataset 配下に見つかりません");
            return;
        }

        string hostInfoPath = Path.Combine(hostDir, "hostinfo.yaml");
        if (!File.Exists(hostInfoPath))
        {
            Debug.LogError("hostinfo.yaml が見つかりません: " + hostInfoPath);
            return;
        }

        SetupStatusUI.ShowStatus("Loading host configuration...");
        HostInfo hostInfo = YamlLoader.Load<HostInfo>(hostInfoPath);
        
        SetupStatusUI.ShowStatus($"Found {hostInfo.devices.Count} devices to initialize");
        SetupStatusUI.SetProgress(0f);
        
        int deviceIndex = 0;
        foreach (var device in hostInfo.devices)
        {
            float progress = (float)deviceIndex / hostInfo.devices.Count;
            SetupStatusUI.SetProgress(progress);
            // deviceType_serialNumber → 例: FemtoBolt_CL8F25300C6
            string deviceDirName = $"{device.deviceType}_{device.serialNumber}";
            
            // ALL CAMERAS ENABLED: Process all available cameras
            Debug.Log($"Processing device: {deviceDirName}");

            string deviceDir = Path.Combine(hostDir, deviceDirName);
            string depthPath = Path.Combine(deviceDir, "camera_depth");
            string colorPath = Path.Combine(deviceDir, "camera_color");

            if (File.Exists(depthPath) && File.Exists(colorPath))
            {
                // if (deviceDirName != "FemtoBolt_CL8F25300F0")
                // {
                //     Debug.Log($"スキップ: {deviceDirName}");
                // }
                // else
                {
                    
                    GameObject parserObj = new GameObject("CameraDataManager_" + deviceDirName);
                    var parser = parserObj.AddComponent<CameraDataManager>();
                    parserObj.transform.parent = this.transform;
                    SetPrivateField(parser, "dir", rootDirectory);                    // 例: /Volumes/MyDisk/CaptureSession/
                    SetPrivateField(parser, "hostname", Path.GetFileName(hostDir));  // 例: PAN-SHI
                    SetPrivateField(parser, "deviceName", deviceDirName);            // 例: FemtoBolt_CL8F25300C6
                    parserObjects.Add(parserObj);
                    
                    // Create async processor for this camera
                    var cameraProcessor = new CameraProcessor(deviceDirName, parser);
                    cameraProcessors.Add(cameraProcessor);
                }
            }
            
            deviceIndex++;
        }
        
        SetupStatusUI.SetProgress(1f);
        SetupStatusUI.ShowStatus($"Created {parserObjects.Count} CameraDataManager instances");
        Debug.Log($"CameraDataManager を {parserObjects.Count} 個作成しました");
        
        // Initialize async processing
        cancellationTokenSource = new CancellationTokenSource();
        SetupStatusUI.ShowStatus("Async multi-camera processing ready");
        
        // Load first frame asynchronously after setup
        _ = LoadFirstFrameAsync();
    }
    
    // Async first frame loading
    private async Task LoadFirstFrameAsync()
    {
        await Task.Delay(100); // Small delay to ensure all setup is complete
        
        SetupStatusUI.ShowStatus("Loading first frame across all cameras...");
        
        try
        {
            // Load frame 0 (first frame) across all cameras
            await ProcessFrameAsync(GetTargetTimestamp(0));
            SetupStatusUI.ShowStatus($"First frame loaded successfully across {cameraProcessors.Count} cameras");
            Debug.Log("Multi-camera first frame loading completed");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load first frame: {ex.Message}");
            SetupStatusUI.ShowStatus("ERROR: First frame loading failed");
        }
    }

    // Timeline control methods - now async for parallel processing
    public async void SeekToFrame(int frameIndex)
    {
        Debug.Log($"MultiCameraPointCloudManager.SeekToFrame: {frameIndex} (ASYNC)");
        
        // Convert frame index to target timestamp for synchronized seeking
        ulong targetTimestamp = GetTargetTimestamp(frameIndex);
        
        await ProcessFrameAsync(targetTimestamp);
    }
    
    // True parallel frame processing with main thread coordination
    public async Task ProcessFrameAsync(ulong targetTimestamp)
    {
        if (isProcessing)
        {
            Debug.LogWarning("Frame processing already in progress, skipping...");
            return;
        }
        
        isProcessing = true;
        
        try
        {
            SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} across {parserObjects.Count} cameras in parallel...");
            
            // Create tasks for all cameras to run in parallel
            var processingTasks = parserObjects.Select(async (parserObj, index) =>
            {
                var parser = parserObj.GetComponent<CameraDataManager>();
                if (parser != null)
                {
                    try
                    {
                        // Run on main thread using ConfigureAwait to maintain Unity context
                        await Task.Run(() => { }); // Small async delay
                        await Task.Yield(); // Ensure we're back on main thread
                        
                        // Call the existing SeekToTimestamp method on main thread
                        parser.SeekToTimestamp(targetTimestamp);
                        return true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Failed to process camera {parserObj.name}: {ex.Message}");
                        return false;
                    }
                }
                return false;
            }).ToArray();
            
            Debug.Log($"Started {processingTasks.Length} camera processing tasks in parallel");
            
            // Wait for all cameras to complete
            var results = await Task.WhenAll(processingTasks);
            int successCount = results.Count(success => success);
            
            SetupStatusUI.ShowStatus($"Parallel processing complete: {successCount}/{parserObjects.Count} cameras succeeded");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error in parallel frame processing: {ex.Message}");
            SetupStatusUI.ShowStatus($"ERROR: Parallel frame processing failed");
        }
        finally
        {
            isProcessing = false;
        }
    }
    
    
    private ulong GetTargetTimestamp(int frameIndex)
    {
        // Use first parser as reference for timestamp calculation
        if (parserObjects.Count > 0)
        {
            var parser = parserObjects[0].GetComponent<CameraDataManager>();
            if (parser != null)
            {
                return parser.GetTimestampForFrame(frameIndex);
            }
        }
        
        // Fallback: estimate based on 30 FPS (33.33ms per frame)
        return (ulong)(frameIndex * 33333333); // nanoseconds
    }
    
    public void ResetToFirstFrame()
    {
        foreach (var parserObj in parserObjects)
        {
            var parser = parserObj.GetComponent<CameraDataManager>();
            if (parser != null)
            {
                parser.ResetToFirstFrame();
            }
        }
    }
    
    public int GetTotalFrameCount()
    {
        // Return frame count from first parser (assuming all have same length)
        if (parserObjects.Count > 0)
        {
            var parser = parserObjects[0].GetComponent<CameraDataManager>();
            return parser?.GetTotalFrameCount() ?? -1;
        }
        return -1;
    }
    
    public int GetFpsFromHeader()
    {
        // Return FPS from first parser
        if (parserObjects.Count > 0)
        {
            var parser = parserObjects[0].GetComponent<CameraDataManager>();
            return parser?.GetFpsFromHeader() ?? 30;
        }
        return 30;
    }

    private void SetPrivateField(CameraDataManager instance, string fieldName, object value)
    {
        var field = typeof(CameraDataManager).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(instance, value);
        else
            Debug.LogWarning($"フィールド {fieldName} が見つかりません");
    }
    
    void OnDestroy()
    {
        // Clean up async resources
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        
        foreach (var processor in cameraProcessors)
        {
            processor.Dispose();
        }
    }
}
