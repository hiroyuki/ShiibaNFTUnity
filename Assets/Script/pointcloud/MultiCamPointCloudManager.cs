using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using YamlDotNet.Serialization;
using System.Threading.Tasks;

public class MultiCameraPointCloudManager : MonoBehaviour
{
    [SerializeField]
    private string rootDirectory; // datasetを含むディレクトリ

    private List<GameObject> pointCloudGameObjcts = new();
    private List<IPointCloudProcessor> pointCloudProcessors = new();
    private List<CameraFrameController> frameControllers = new(); // Data layer

    // Frame navigation tracking
    private int leadingCameraIndex = 0; // Index of camera that currently has the head timestamp

    private volatile bool isProcessing = false;

    // Multi-camera view
    private MultiPointCloudView multiPointCloudView;

    private ProcessingType processingType = ProcessingType.GPU;

    void Start()
    {
        Debug.Log("Start method called on thread: " + Thread.CurrentThread.ManagedThreadId);
        SetupStatusUI.ShowStatus("Starting Multi-Camera Point Cloud Manager...");
        
        // Disable timeline auto-play
        var playableDirector = FindFirstObjectByType<UnityEngine.Playables.PlayableDirector>();
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
                // if (deviceDirName != "FemtoBolt_CL8F253004Z")// && deviceDirName != "FemtoBolt_CL8F25300F0"
                // {
                //     Debug.Log($"スキップ: {deviceDirName}");
                // }
                // else
                // if (deviceDirName != "FemtoBolt_CL8F25300F0")// && deviceDirName != ""
                // if (deviceDirName != "FemtoBolt_CL8F253004Z"&& deviceDirName != "FemtoBolt_CL8F25300F0")
                // {
                //     Debug.Log($"スキップ: {deviceDirName}");
                // }
                // else
                {
                    // Create CameraFrameController (data layer)
                    var frameController = new CameraFrameController(
                        rootDirectory,
                        Path.GetFileName(hostDir),
                        deviceDirName
                    );
                    frameControllers.Add(frameController);

                    // Create SinglePointCloudView (view layer)
                    GameObject singlePointCloudContainer = new GameObject("SinglePointCloudView_" + deviceDirName);
                    var view = singlePointCloudContainer.AddComponent<SinglePointCloudView>();
                    // dataManagerObj.transform.parent = this.transform;

                    // Initialize view with frame controller reference
                    view.Initialize(frameController, processingType);
                    pointCloudGameObjcts.Add(singlePointCloudContainer);

                    // Setup processor (owned by view)
                    view.SetupProcessor();
                    view.SetupWithProcessor();

                    pointCloudProcessors.Add(view.GetProcessor());
                }
            }
            
            deviceIndex++;
        }

        SetupStatusUI.SetProgress(1f);
        SetupStatusUI.ShowStatus($"SinglePointCloudView を {pointCloudGameObjcts.Count} 個作成しました - first frames will load individually");

        // Initialize multi-camera processing if enabled
        if (processingType == ProcessingType.ONESHADER && pointCloudGameObjcts.Count > 0)
        {
            InitializeMultiCameraProcessing();
        }
    }

    private void InitializeMultiCameraProcessing()
    {
        SetupStatusUI.ShowStatus("Initializing multi-camera ONESHADER processing...");

        // Create MultiPointCloudView GameObject
        GameObject multiViewObj = new GameObject("MultiPointCloudView");
        multiViewObj.transform.parent = this.transform;
        multiPointCloudView = multiViewObj.AddComponent<MultiPointCloudView>();

        // Register all camera views
        foreach (var dataManagerObj in pointCloudGameObjcts)
        {
            var cameraView = dataManagerObj.GetComponent<SinglePointCloudView>();
            if (cameraView != null)
            {
                multiPointCloudView.RegisterCameraView(cameraView);
            }
        }

        // Initialize multi-camera processing
        multiPointCloudView.InitializeMultiCameraProcessing();
        SetupStatusUI.ShowStatus($"Multi-camera view initialized for {pointCloudGameObjcts.Count} cameras");
    }

    void Update()
    {
        foreach (var pointCloudObj in pointCloudGameObjcts)
        {
            var view = pointCloudObj.GetComponent<SinglePointCloudView>();
            if (view != null)
            {
                view.ProcessFirstFrameIfNeeded();
            }
        }
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
        if (pointCloudGameObjcts.Count == 0) return;

        // Find next synchronized timestamp from the leading camera
        ulong nextTimestamp = FindNextSynchronizedTimestamp();
        Debug.Log("Next synchronized timestamp: " + nextTimestamp + "   leadingCameraIndex=" + leadingCameraIndex);
        if (nextTimestamp == 0)
        {
            Debug.Log("No more synchronized frames available");
            return;
        }

        // Navigate all cameras to this synchronized timestamp using unified method
        ProcessFrame(nextTimestamp);
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
        
        for (int i = 0; i < pointCloudGameObjcts.Count; i++)
        {
            var dataManager = pointCloudGameObjcts[i].GetComponent<SinglePointCloudView>();
            if (dataManager == null) continue;
            
            // Use stored current timestamp instead of peeking metadata
            ulong timestamp = dataManager.GetCurrentTimestamp();
            if (timestamp > foremostTimestamp)
            {
                foremostTimestamp = timestamp;
                newLeadingIndex = i;
            }
        }
        
        if (newLeadingIndex != leadingCameraIndex)
        {
            leadingCameraIndex = newLeadingIndex;
            var leadingCamera = pointCloudGameObjcts[leadingCameraIndex].GetComponent<SinglePointCloudView>();
        }
    }
    
    private ulong FindNextSynchronizedTimestamp()
    {
        try
        {
            // Get the next timestamp from the current leading camera
            if (leadingCameraIndex >= pointCloudGameObjcts.Count)
            {
                Debug.LogError($"Leading camera index {leadingCameraIndex} is out of range");
                return 0;
            }
            
            var leadingCamera = pointCloudGameObjcts[leadingCameraIndex].GetComponent<SinglePointCloudView>();
            if (leadingCamera == null)
            {
                Debug.LogError($"Leading camera at index {leadingCameraIndex} is null");
                return 0;
            }
            
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

    // Timeline control methods - simple synchronous processing
    public void SeekToFrame(int frameIndex)
    {
        Debug.Log($"MultiCameraPointCloudManager.SeekToFrame: {frameIndex}");

        // Convert frame index to target timestamp for synchronized seeking
        ulong targetTimestamp = GetTargetTimestamp(frameIndex);

        ProcessFrame(targetTimestamp);
    }
    
    // Simple synchronous frame processing for all cameras
    public void ProcessFrame(ulong targetTimestamp)
    {
        if (isProcessing)
        {
            Debug.LogWarning("Frame processing already in progress, skipping...");
            return;
        }

        isProcessing = true;

        try
        {
            if (processingType == ProcessingType.ONESHADER && multiPointCloudView != null)
            {
                // Use multi-camera GPU processing for all scenarios (single or multiple cameras)
                SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} using multi-camera GPU processing ({pointCloudGameObjcts.Count} camera(s))...");
                multiPointCloudView.ProcessFrame(targetTimestamp);
                SetupStatusUI.ShowStatus($"Multi-camera GPU processing complete for {pointCloudGameObjcts.Count} camera(s)");
            }
            else
            {
                // Fall back to individual camera processing only if multi-camera processor is not available
                SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} across {pointCloudGameObjcts.Count} cameras (fallback mode)...");

                int successCount = 0;
                foreach (var pointCloudObj in pointCloudGameObjcts)
                {
                    var view = pointCloudObj.GetComponent<SinglePointCloudView>();
                    if (view != null)
                    {
                        try
                        {
                            int index = pointCloudGameObjcts.IndexOf(pointCloudObj);
                            view.ProcessFrame(targetTimestamp);
                            successCount++;
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"Failed to process camera {pointCloudObj.name}: {ex.Message}");
                        }
                    }
                }

                SetupStatusUI.ShowStatus($"Frame processing complete: {successCount}/{pointCloudGameObjcts.Count} cameras processed successfully");
            }

            // Update leading camera index after processing for next navigation
            UpdateLeadingCameraIndex();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error in ProcessFrame: {ex.Message}");
            SetupStatusUI.ShowStatus("ERROR: Frame processing failed");
        }
        finally
        {
            isProcessing = false;
        }
    }
    
    
    private ulong GetTargetTimestamp(int frameIndex)
    {
        // Use first data manager as reference for timestamp calculation
        if (pointCloudGameObjcts.Count > 0)
        {
            var dataManager = pointCloudGameObjcts[0].GetComponent<SinglePointCloudView>();
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
        foreach (var dataManagerObj in pointCloudGameObjcts)
        {
            var pointCloudView = dataManagerObj.GetComponent<SinglePointCloudView>();
            if (pointCloudView != null)
            {
                pointCloudView.ResetToFirstFrame();
            }
        }
    }
    
    public int GetTotalFrameCount()
    {
        // Return frame count from first data manager (assuming all have same length)
        if (pointCloudGameObjcts.Count > 0)
        {
            var dataManager = pointCloudGameObjcts[0].GetComponent<SinglePointCloudView>();
            return dataManager?.GetTotalFrameCount() ?? -1;
        }
        return -1;
    }
    
    public int GetFpsFromHeader()
    {
        // Return FPS from first data manager
        if (pointCloudGameObjcts.Count > 0)
        {
            var dataManager = pointCloudGameObjcts[0].GetComponent<SinglePointCloudView>();
            if (dataManager != null)
            {
                int fps = dataManager.GetFps();
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
    
    void OnDestroy()
    {
        // Cleanup multi-camera view
        if (multiPointCloudView != null)
        {
            DestroyImmediate(multiPointCloudView.gameObject);
        }

        // Cleanup frame controllers (data layer)
        foreach (var controller in frameControllers)
        {
            controller?.Dispose();
        }
        frameControllers.Clear();
    }
}
