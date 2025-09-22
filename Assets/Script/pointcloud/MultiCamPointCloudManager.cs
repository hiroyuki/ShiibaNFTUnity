using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.InputSystem;
using YamlDotNet.Serialization;
using PointCloud;

public class MultiCameraPointCloudManager : MonoBehaviour
{
    [SerializeField]
    private string rootDirectory; // datasetを含むディレクトリ

    private List<GameObject> dataManagerObjects = new();
    
    // Frame navigation tracking
    private int leadingCameraIndex = 0; // Index of camera that currently has the head timestamp
    
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
                    
                    GameObject dataManagerObj = new GameObject("SingleCameraDataManager_" + deviceDirName);
                    var dataManager = dataManagerObj.AddComponent<SingleCameraDataManager>();
                    dataManagerObj.transform.parent = this.transform;
                    SetPrivateField(dataManager, "dir", rootDirectory);                    // 例: /Volumes/MyDisk/CaptureSession/
                    SetPrivateField(dataManager, "hostname", Path.GetFileName(hostDir));  // 例: PAN-SHI
                    SetPrivateField(dataManager, "deviceName", deviceDirName);            // 例: FemtoBolt_CL8F25300C6
                    dataManagerObjects.Add(dataManagerObj);
                    
                    // Create async processor for this camera
                    var cameraProcessor = new CameraProcessor(deviceDirName, dataManager);
                    cameraProcessors.Add(cameraProcessor);
                }
            }
            
            deviceIndex++;
        }
        
        // Initialize async processing
        cancellationTokenSource = new CancellationTokenSource();

        SetupStatusUI.SetProgress(1f);
        SetupStatusUI.ShowStatus($"SingleCameraDataManager を {dataManagerObjects.Count} 個作成しました");
        
        // Load first frame asynchronously after setup
        _ = LoadFirstFrameAsync();
    }
    
    void Update()
    {
        HandleSynchronizedFrameNavigation();
    }
    
    private void HandleSynchronizedFrameNavigation()
    {
        if (Keyboard.current == null) return;
        
        // Right arrow: Navigate to next synchronized frame across all cameras
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            NavigateToNextSynchronizedFrame();
        }
        
        // Left arrow: Navigate to previous synchronized frame across all cameras
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            NavigateToPreviousSynchronizedFrame();
        }
    }
    
    private void NavigateToNextSynchronizedFrame()
    {
        if (dataManagerObjects.Count == 0) return;
        
        // Find next synchronized timestamp from the leading camera
        ulong nextTimestamp = FindNextSynchronizedTimestamp();
        Debug.Log("Next synchronized timestamp: " + nextTimestamp + "   leadingCameraIndex=" + leadingCameraIndex);
        if (nextTimestamp == 0)
        {
            Debug.Log("No more synchronized frames available");
            return;
        }
        
        // Navigate all cameras to this synchronized timestamp using unified method
        _ = ProcessFrameAsync(nextTimestamp);
    }
    
    private void NavigateToPreviousSynchronizedFrame()
    {
        // Note: This would require backward seeking which our parsers don't support
        // For now, we'll show a warning and suggest using timeline control instead
        Debug.LogWarning("Backward navigation not supported with current forward-only parsers. Use timeline controls for seeking backward.");
    }
    
    private void UpdateLeadingCameraIndex()
    {
        int newLeadingIndex = 0;
        ulong foremostTimestamp = ulong.MinValue;
        
        for (int i = 0; i < dataManagerObjects.Count; i++)
        {
            var dataManager = dataManagerObjects[i].GetComponent<SingleCameraDataManager>();
            if (dataManager == null) continue;
            
            // Use stored current timestamp instead of peeking metadata
            ulong timestamp = dataManager.GetCurrentTimestamp();
            Debug.Log($"Checking camera index {i}: {dataManager.GetDeviceName()} with timestamp {timestamp}");
            if (timestamp > foremostTimestamp)
            {
                foremostTimestamp = timestamp;
                newLeadingIndex = i;
            }
        }
        
        if (newLeadingIndex != leadingCameraIndex)
        {
            leadingCameraIndex = newLeadingIndex;
            var leadingCamera = dataManagerObjects[leadingCameraIndex].GetComponent<SingleCameraDataManager>();
            Debug.Log($"Leading camera updated to index {leadingCameraIndex}: {leadingCamera?.GetDeviceName()} (head timestamp: {foremostTimestamp})");
        }
    }
    
    private ulong FindNextSynchronizedTimestamp()
    {
        try
        {
            // Get the next timestamp from the current leading camera
            if (leadingCameraIndex >= dataManagerObjects.Count)
            {
                Debug.LogError($"Leading camera index {leadingCameraIndex} is out of range");
                return 0;
            }
            
            var leadingCamera = dataManagerObjects[leadingCameraIndex].GetComponent<SingleCameraDataManager>();
            if (leadingCamera == null)
            {
                Debug.LogError($"Leading camera at index {leadingCameraIndex} is null");
                return 0;
            }
            
            Debug.Log($"Finding next synchronized timestamp from leading camera: {leadingCamera.GetDeviceName()}, current timestamp: {leadingCamera.GetCurrentTimestamp()}");
            // Try to get the next frame timestamp from the leading camera
            if (leadingCamera.PeekNextTimestamp(out ulong timestamp))
            {
                return timestamp;
            }
            
            return 0; // No more data
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error finding next synchronized timestamp: {ex.Message}");
            return 0;
        }
    }
    
    
    // Async first frame loading
    private async Task LoadFirstFrameAsync()
    {
        await Task.Delay(100); // Small delay to ensure all setup is complete
        
        SetupStatusUI.ShowStatus("LOADING FIRST FRAME ACROSS ALL CAMERAS...");
        
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
            SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} across {dataManagerObjects.Count} cameras in parallel...");
            
            // Create tasks for all cameras to run in parallel
            var processingTasks = dataManagerObjects.Select(async (dataManagerObj, index) =>
            {
                var dataManager = dataManagerObj.GetComponent<SingleCameraDataManager>();
                if (dataManager != null)
                {
                    try
                    {
                        // Run on main thread using ConfigureAwait to maintain Unity context
                        await Task.Run(() => { }); // Small async delay
                        await Task.Yield(); // Ensure we're back on main thread
                        
                        // Call the existing SeekToTimestamp method on main thread
                        dataManager.SeekToTimestamp(targetTimestamp);
                        return true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Failed to process camera {dataManagerObj.name}: {ex.Message}");
                        return false;
                    }
                }
                return false;
            }).ToArray();
            
            // Wait for all cameras to complete
            var results = await Task.WhenAll(processingTasks);
            int successCount = results.Count(success => success);
            
            SetupStatusUI.ShowStatus($"Parallel processing complete: {successCount}/{dataManagerObjects.Count} cameras succeeded");
            
            // Update leading camera index after processing for next navigation
            UpdateLeadingCameraIndex();
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
        // Use first data manager as reference for timestamp calculation
        if (dataManagerObjects.Count > 0)
        {
            var dataManager = dataManagerObjects[0].GetComponent<SingleCameraDataManager>();
            if (dataManager != null)
            {
                return dataManager.GetTimestampForFrame(frameIndex);
            }
        }
        
        // Fallback: estimate based on FPS from header
        int fps = GetFpsFromHeader();
        if (fps > 0)
        {
            ulong nanosecondsPerFrame = (ulong)(1_000_000_000L / fps);
            return (ulong)frameIndex * nanosecondsPerFrame;
        }
        else
        {
            Debug.LogError($"Cannot estimate timestamp for frame {frameIndex}: FPS not available from any camera headers");
            return 0; // Error case - cannot estimate without FPS
        }
    }
    
    public void ResetToFirstFrame()
    {
        foreach (var dataManagerObj in dataManagerObjects)
        {
            var dataManager = dataManagerObj.GetComponent<SingleCameraDataManager>();
            if (dataManager != null)
            {
                dataManager.ResetToFirstFrame();
            }
        }
    }
    
    public int GetTotalFrameCount()
    {
        // Return frame count from first data manager (assuming all have same length)
        if (dataManagerObjects.Count > 0)
        {
            var dataManager = dataManagerObjects[0].GetComponent<SingleCameraDataManager>();
            return dataManager?.GetTotalFrameCount() ?? -1;
        }
        return -1;
    }
    
    public int GetFpsFromHeader()
    {
        // Return FPS from first data manager
        if (dataManagerObjects.Count > 0)
        {
            var dataManager = dataManagerObjects[0].GetComponent<SingleCameraDataManager>();
            if (dataManager != null)
            {
                int fps = dataManager.GetFpsFromHeader();
                if (fps > 0)
                {
                    return fps;
                }
                
                // Error case: FPS not available in header
                string errorMsg = "FPS not available from any camera headers. Cannot determine timeline framerate.";
                Debug.LogError(errorMsg);
                SetupStatusUI.ShowStatus($"CRITICAL ERROR: {errorMsg}");
                return -1;
            }
        }
        
        Debug.LogError("No data managers available to get FPS from header");
        return -1;
    }

    private void SetPrivateField(SingleCameraDataManager instance, string fieldName, object value)
    {
        var field = typeof(SingleCameraDataManager).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
