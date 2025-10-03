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

    private List<CameraFrameController> frameControllers = new(); // Data layer

    // Frame navigation tracking
    private int leadingCameraIndex = 0; // Index of camera that currently has the head timestamp

    private volatile bool isProcessing = false;

    // Processing mode determines view architecture
    private ProcessingType processingType = ProcessingType.ONESHADER;

    // For ONESHADER mode: unified view
    private MultiPointCloudView multiPointCloudView;

    // For GPU/CPU mode: individual views
    private List<SinglePointCloudView> singlePointCloudViews = new();

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
        foreach (var deviceNode in hostInfo.devices)
        {
            float progress = (float)deviceIndex / hostInfo.devices.Count;
            SetupStatusUI.SetProgress(progress);
            // deviceType_serialNumber → 例: FemtoBolt_CL8F25300C6
            string deviceDirName = $"{deviceNode.deviceType}_{deviceNode.serialNumber}";
            string deviceDir = Path.Combine(hostDir, deviceDirName);
            string depthPath = Path.Combine(deviceDir, "camera_depth");
            string colorPath = Path.Combine(deviceDir, "camera_color");

            if (File.Exists(depthPath) && File.Exists(colorPath))
            {
                // Create CameraFrameController (data layer)
                var frameController = new CameraFrameController(
                    rootDirectory,
                    Path.GetFileName(hostDir),
                    deviceDirName
                );
                frameControllers.Add(frameController);
            }

            deviceIndex++;
        }

        SetupStatusUI.SetProgress(1f);
        SetupStatusUI.ShowStatus($"Loaded {frameControllers.Count} camera controllers");

        // Initialize views based on processing type
        if (processingType == ProcessingType.ONESHADER)
        {
            InitializeMultiCameraView();
        }
        else // GPU or CPU
        {
            InitializeSingleCameraViews();
        }

        // Set timeline duration based on camera 0 data
        SetupTimelineDuration();
    }

    private void SetupTimelineDuration()
    {
        if (frameControllers.Count == 0)
        {
            Debug.LogWarning("No frame controllers available to get duration from");
            return;
        }

        // Get FPS from camera 0
        int fps = GetFpsFromHeader();
        if (fps <= 0)
        {
            Debug.LogError($"Invalid FPS ({fps}). Cannot set timeline duration.");
            return;
        }

        // Calculate total frame count from depth binary file
        var device = frameControllers[0].Device;
        int totalFrameCount = CalculateTotalFrameCount(device);

        if (totalFrameCount <= 0)
        {
            Debug.LogError($"Invalid frame count ({totalFrameCount}). Cannot set timeline duration.");
            return;
        }

        // Find TimelineController and set duration
        var timelineController = FindObjectOfType<TimelineController>();
        if (timelineController != null)
        {
            timelineController.SetDuration(totalFrameCount, fps);
        }
    }

    private int CalculateTotalFrameCount(SensorDevice device)
    {
        try
        {
            var depthParser = device.GetDepthParser();
            if (depthParser == null) return -1;

            // Get file size and header information
            long fileSize = new System.IO.FileInfo(device.GetDepthFilePath()).Length;
            int headerMetadataSize = depthParser.sensorHeader.MetadataSize;
            int headerImageSize = depthParser.sensorHeader.ImageSize;
            int recordSize = headerMetadataSize + headerImageSize;

            // Calculate: (fileSize - headerMetadataSize) / recordSize
            long dataSize = fileSize - headerMetadataSize;
            int frameCount = (int)(dataSize / recordSize);
            return frameCount;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to calculate frame count: {ex.Message}");
            return -1;
        }
    }

    private void InitializeMultiCameraView()
    {
        SetupStatusUI.ShowStatus("Initializing multi-camera ONESHADER processing...");

        // Create MultiPointCloudView GameObject
        GameObject multiViewObj = new GameObject("MultiPointCloudView");
        multiViewObj.transform.parent = this.transform;
        multiPointCloudView = multiViewObj.AddComponent<MultiPointCloudView>();

        // Initialize with frame controllers
        multiPointCloudView.Initialize(frameControllers);

        SetupStatusUI.ShowStatus($"Multi-camera view initialized for {frameControllers.Count} cameras");
    }

    private void InitializeSingleCameraViews()
    {
        SetupStatusUI.ShowStatus($"Initializing {frameControllers.Count} individual camera views for {processingType} processing...");

        for (int i = 0; i < frameControllers.Count; i++)
        {
            var frameController = frameControllers[i];
            var device = frameController.Device;

            // Create processor
            IPointCloudProcessor processor = CreateAndSetupProcessor(frameController);

            // Create view
            GameObject viewObj = new GameObject($"SinglePointCloudView_{device.deviceName}");
            viewObj.transform.parent = this.transform;
            var view = viewObj.AddComponent<SinglePointCloudView>();

            // Initialize view with controller and processor
            view.Initialize(frameController, processor, processingType);
            singlePointCloudViews.Add(view);

            device.UpdateDeviceStatus(DeviceStatusType.Ready, ProcessingType.None, "Ready for first frame");
        }

        SetupStatusUI.ShowStatus($"Initialized {singlePointCloudViews.Count} camera views");
    }

    private IPointCloudProcessor CreateAndSetupProcessor(CameraFrameController frameController)
    {
        var device = frameController.Device;

        // Load depth bias from configuration
        float depthBias = device.GetDepthBias();

        // Create processor using factory pattern
        IPointCloudProcessor processor = PointCloudProcessorFactory.CreateBestProcessor(device.deviceName);

        device.UpdateDeviceStatus(DeviceStatusType.Loading, processingType, $"{processingType} processing enabled");

        // Setup processor with device parameters
        processor.Setup(device, depthBias);

        // Note: SetDepthViewerTransform will be called by SinglePointCloudView after initialization

        // Configure bounding volume
        Transform boundingVolume = GameObject.Find("BoundingVolume")?.transform;
        if (boundingVolume != null)
        {
            processor.SetBoundingVolume(boundingVolume);
        }

        // Configure transforms
        processor.ApplyDepthToColorExtrinsics(
            device.GetDepthToColorTranslation(),
            device.GetDepthToColorRotation()
        );
        processor.SetupColorIntrinsics(device.GetColorParser().sensorHeader);
        processor.SetupCameraMetadata(device);

        SetupStatusUI.ShowStatus($"Processor setup complete for {device.deviceName}");

        return processor;
    }

    void Update()
    {
        // Process first frames if needed
        if (processingType == ProcessingType.ONESHADER)
        {
            multiPointCloudView?.ProcessFirstFramesIfNeeded();
        }
        else
        {
            foreach (var view in singlePointCloudViews)
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
        if (processingType == ProcessingType.GPU && singlePointCloudViews.Count == 0) return;

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

        if (processingType == ProcessingType.ONESHADER)
        {
            // Use frame controllers directly
            for (int i = 0; i < frameControllers.Count; i++)
            {
                var controller = frameControllers[i];
                if (controller == null) continue;

                ulong timestamp = controller.CurrentTimestamp;
                if (timestamp > foremostTimestamp)
                {
                    foremostTimestamp = timestamp;
                    newLeadingIndex = i;
                }
            }
        }
        else
        {
            // Use single point cloud views
            for (int i = 0; i < singlePointCloudViews.Count; i++)
            {
                var view = singlePointCloudViews[i];
                if (view == null) continue;

                ulong timestamp = view.GetCurrentTimestamp();
                if (timestamp > foremostTimestamp)
                {
                    foremostTimestamp = timestamp;
                    newLeadingIndex = i;
                }
            }
        }

        if (newLeadingIndex != leadingCameraIndex)
        {
            leadingCameraIndex = newLeadingIndex;
        }
    }
    
    private ulong FindNextSynchronizedTimestamp()
    {
        try
        {
            if (processingType == ProcessingType.ONESHADER)
            {
                // Get the next timestamp from the current leading camera's controller
                if (leadingCameraIndex >= frameControllers.Count)
                {
                    Debug.LogError($"Leading camera index {leadingCameraIndex} is out of range");
                    return 0;
                }

                var leadingController = frameControllers[leadingCameraIndex];
                if (leadingController == null)
                {
                    Debug.LogError($"Leading controller at index {leadingCameraIndex} is null");
                    return 0;
                }

                if (leadingController.PeekNextTimestamp(out ulong timestamp))
                {
                    return timestamp;
                }
            }
            else
            {
                // Get the next timestamp from the current leading camera's view
                if (leadingCameraIndex >= singlePointCloudViews.Count)
                {
                    Debug.LogError($"Leading camera index {leadingCameraIndex} is out of range");
                    return 0;
                }

                var leadingView = singlePointCloudViews[leadingCameraIndex];
                if (leadingView == null)
                {
                    Debug.LogError($"Leading view at index {leadingCameraIndex} is null");
                    return 0;
                }

                if (leadingView.PeekNextTimestamp(out ulong timestamp))
                {
                    return timestamp;
                }
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
            if (processingType == ProcessingType.ONESHADER)
            {
                // Use multi-camera GPU processing
                SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} using ONESHADER ({frameControllers.Count} cameras)...");
                multiPointCloudView?.ProcessFrame(targetTimestamp);
                SetupStatusUI.ShowStatus($"ONESHADER processing complete for {frameControllers.Count} cameras");
            }
            else
            {
                // Individual camera processing (GPU/CPU)
                SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} across {singlePointCloudViews.Count} cameras ({processingType})...");

                int successCount = 0;
                foreach (var view in singlePointCloudViews)
                {
                    try
                    {
                        bool success = view.ProcessFrame(targetTimestamp);
                        if (success) successCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"Failed to process camera: {ex.Message}");
                    }
                }

                SetupStatusUI.ShowStatus($"Frame processing complete: {successCount}/{singlePointCloudViews.Count} cameras processed successfully");
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
        // Use first controller as reference for timestamp calculation
        if (frameControllers.Count > 0)
        {
            return frameControllers[0].GetTimestampForFrame(frameIndex);
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
        if (processingType == ProcessingType.ONESHADER)
        {
            if (frameControllers.Count > 0)
            {
                ulong targetTimestamp = frameControllers[0].GetTimestampForFrame(0);
                multiPointCloudView?.ProcessFrame(targetTimestamp);
            }
        }
        else
        {
            foreach (var view in singlePointCloudViews)
            {
                ulong targetTimestamp = view.GetTimestampForFrame(0);
                view.ProcessFrame(targetTimestamp);
            }
        }
    }

    public int GetTotalFrameCount()
    {
        if (processingType == ProcessingType.ONESHADER)
        {
            // Return frame count from first controller (assuming all have same length)
            if (frameControllers.Count > 0)
            {
                return frameControllers[0].GetTotalFrameCount();
            }
        }
        else
        {
            // Return frame count from first view
            if (singlePointCloudViews.Count > 0)
            {
                return singlePointCloudViews[0].GetTotalFrameCount();
            }
        }
        return -1;
    }

    public int GetFpsFromHeader()
    {
        if (processingType == ProcessingType.ONESHADER)
        {
            // Return FPS from first controller
            if (frameControllers.Count > 0)
            {
                int fps = frameControllers[0].GetFps();
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
        else
        {
            // Return FPS from first view
            if (singlePointCloudViews.Count > 0)
            {
                int fps = singlePointCloudViews[0].GetFps();
                if (fps > 0)
                {
                    return fps;
                }

                string errorMsg = "FPS not available from any camera headers. Cannot determine timeline framerate.";
                Debug.LogError(errorMsg);
                SetupStatusUI.ShowStatus($"CRITICAL ERROR: {errorMsg}");
                return -1;
            }
        }

        Debug.LogError("No cameras available to get FPS from header");
        return -1;
    }
    
    void OnDestroy()
    {
        if (processingType == ProcessingType.ONESHADER)
        {
            // Cleanup multi-camera view
            if (multiPointCloudView != null)
            {
                DestroyImmediate(multiPointCloudView.gameObject);
            }
        }
        else
        {
            // Cleanup single camera views (processors are owned by views)
            foreach (var view in singlePointCloudViews)
            {
                if (view != null)
                {
                    DestroyImmediate(view.gameObject);
                }
            }
            singlePointCloudViews.Clear();
        }

        // Cleanup frame controllers (data layer)
        foreach (var controller in frameControllers)
        {
            controller?.Dispose();
        }
        frameControllers.Clear();
    }
}
