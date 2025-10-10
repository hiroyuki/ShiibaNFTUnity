using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using YamlDotNet.Serialization;

/// <summary>
/// Main manager for multi-camera point cloud visualization
/// Refactored to use specialized managers and IFrameController interface
/// Supports two modes: PLY mode (read-only) and Binary mode (with optional export)
/// </summary>
public class MultiCameraPointCloudManager : MonoBehaviour
{
    [SerializeField] private string rootDirectory;
    [SerializeField] private bool usePly = true;
    [SerializeField] private bool enablePlyExport = false; // Only used in Binary mode

    private List<IFrameController> frameControllers = new();
    private List<CameraFrameController> cameraControllers = new(); // Only for binary mode
    private PlyFrameController plyFrameController; // Only for PLY mode
    private ProcessingType processingType = ProcessingType.ONESHADER;
    private string displayName = "";

    // Views
    private MultiPointCloudView multiPointCloudView;
    private List<SinglePointCloudView> singlePointCloudViews = new();

    // Managers (only for binary mode)
    private PlyExportManager plyExportManager;
    private FrameProcessingManager frameProcessingManager;

    // Frame navigation
    private int leadingCameraIndex = 0;

    void Start()
    {
        SetupStatusUI.ShowStatus("Starting Multi-Camera Point Cloud Manager...");

        DisableTimelineAutoPlay();
        LoadDatasetInfo();

        // Check if we should use PLY mode (existing PLY files found)
        if (usePly && TryInitializePlyMode())
        {
            return; // PLY mode initialized successfully
        }

        // Fall back to Binary mode
        InitializeBinaryMode();
    }

    void Update()
    {
        if (processingType == ProcessingType.PLY)
        {
            // PLY mode - simple navigation only
            ProcessFirstPlyFrameIfNeeded();
            HandlePlyModeNavigation();
        }
        else
        {
            // Binary mode - full processing and optional export
            frameProcessingManager?.ProcessFirstFramesIfNeeded(processingType);
            HandleSynchronizedFrameNavigation();

            if (enablePlyExport && plyExportManager != null)
            {
                plyExportManager.HandlePlyExportInput(processingType, frameProcessingManager.CurrentFrameIndex);
                plyExportManager.ProcessBatchExport(
                    (frameIdx, timestamp) => ProcessFrame(frameIdx, timestamp),
                    (frameIdx) => GetTargetTimestamp(frameIdx)
                );
            }
        }
    }

    #region Initialization

    private bool TryInitializePlyMode()
    {
        // Try to create PLY frame controller
        plyFrameController = new PlyFrameController(rootDirectory, displayName);

        // Check if PLY files exist
        if (!plyFrameController.ShouldEnablePlyMode())
        {
            Debug.Log("No PLY files found. Switching to Binary mode.");
            return false;
        }

        // PLY files found - initialize PLY mode
        processingType = ProcessingType.PLY;
        frameControllers.Add(plyFrameController);

        Debug.Log($"PLY mode enabled: {plyFrameController.GetTotalFrameCount()} files found");
        SetupStatusUI.ShowStatus($"PLY mode: {plyFrameController.GetTotalFrameCount()} files loaded");

        // Initialize simple viewer for PLY mode
        InitializePlyModeView();
        SetupTimelineDurationForPly();

        return true;
    }

    private void InitializePlyModeView()
    {
        SetupStatusUI.ShowStatus("Initializing PLY viewer...");

        GameObject multiViewObj = new GameObject("MultiPointCloudView_PLY");
        multiViewObj.transform.parent = transform;
        multiPointCloudView = multiViewObj.AddComponent<MultiPointCloudView>();

        // Setup unified viewer for PLY mode (creates the mesh)
        multiPointCloudView.SetupUnifiedViewer();

        SetupStatusUI.ShowStatus($"PLY viewer initialized: {plyFrameController.GetTotalFrameCount()} frames available");
    }

    private void SetupTimelineDurationForPly()
    {
        int fps = plyFrameController.GetFps();
        int totalFrameCount = plyFrameController.GetTotalFrameCount();

        if (fps <= 0 || totalFrameCount <= 0)
        {
            Debug.LogWarning($"Invalid PLY metadata: FPS={fps}, frames={totalFrameCount}");
            return;
        }

        var timelineController = FindObjectOfType<TimelineController>();
        timelineController?.SetDuration(totalFrameCount, fps);
    }

    private void InitializeBinaryMode()
    {
        SetupStatusUI.ShowStatus("Initializing Binary mode...");

        if (!LoadCameraControllers())
            return;

        InitializeBinaryManagers();
        InitializeViews();
        SetupTimelineDuration();

        // Optionally setup PLY export
        if (enablePlyExport)
        {
            SetupPlyExport();
        }
    }

    private void InitializeBinaryManagers()
    {
        frameProcessingManager = new FrameProcessingManager(frameControllers);
        frameProcessingManager.SetDisplayName(displayName);
    }

    private void SetupPlyExport()
    {
        if (cameraControllers.Count == 0)
            return;

        // Create PLY frame controller for export
        var plyController = CreatePlyFrameController();
        if (plyController == null)
            return;

        // Initialize export manager
        plyExportManager = new PlyExportManager(plyController);
        plyExportManager.SetMultiPointCloudView(multiPointCloudView);

        Debug.Log("PLY export enabled");
        SetupStatusUI.ShowStatus("PLY export enabled");
    }

    private void DisableTimelineAutoPlay()
    {
        var playableDirector = FindFirstObjectByType<UnityEngine.Playables.PlayableDirector>();
        if (playableDirector != null && playableDirector.playOnAwake)
        {
            playableDirector.Stop();
        }
        SetupStatusUI.ShowStatus("Timeline auto-play disabled");
    }

    private bool LoadCameraControllers()
    {
        SetupStatusUI.ShowStatus("Looking for dataset directory...");
        string datasetPath = Path.Combine(rootDirectory, "dataset");

        if (!Directory.Exists(datasetPath))
        {
            Debug.LogError($"dataset directory not found: {datasetPath}");
            SetupStatusUI.ShowStatus($"ERROR: dataset directory not found");
            return false;
        }

        string hostDir = Directory.GetDirectories(datasetPath).FirstOrDefault();
        if (hostDir == null)
        {
            Debug.LogError("No host directory found in dataset");
            return false;
        }

        string hostInfoPath = Path.Combine(hostDir, "hostinfo.yaml");
        if (!File.Exists(hostInfoPath))
        {
            Debug.LogError($"hostinfo.yaml not found: {hostInfoPath}");
            return false;
        }

        SetupStatusUI.ShowStatus("Loading host configuration...");
        HostInfo hostInfo = YamlLoader.Load<HostInfo>(hostInfoPath);

        SetupStatusUI.ShowStatus($"Found {hostInfo.devices.Count} devices to initialize");
        SetupStatusUI.SetProgress(0f);

        for (int i = 0; i < hostInfo.devices.Count; i++)
        {
            var deviceNode = hostInfo.devices[i];
            SetupStatusUI.SetProgress((float)i / hostInfo.devices.Count);

            string deviceDirName = $"{deviceNode.deviceType}_{deviceNode.serialNumber}";
            string deviceDir = Path.Combine(hostDir, deviceDirName);
            string depthPath = Path.Combine(deviceDir, "camera_depth");
            string colorPath = Path.Combine(deviceDir, "camera_color");

            if (File.Exists(depthPath) && File.Exists(colorPath))
            {
                var cameraController = new CameraFrameController(
                    rootDirectory,
                    Path.GetFileName(hostDir),
                    deviceDirName
                );
                cameraControllers.Add(cameraController);
                frameControllers.Add(cameraController); // Add to unified list
            }
            else
            {
                Debug.LogWarning($"Skipping device {deviceDirName}: missing depth or color file");
            }
        }

        SetupStatusUI.SetProgress(1f);
        SetupStatusUI.ShowStatus($"Loaded {cameraControllers.Count} camera controllers");
        return true;
    }

    private PlyFrameController CreatePlyFrameController()
    {
        // Get FPS and total frames from first camera
        int fps = cameraControllers[0].GetFps();
        int totalFrames = CalculateTotalFrameCount(cameraControllers[0].Device);

        // Create PLY frame controller
        var plyController = new PlyFrameController(rootDirectory, displayName);

        // Set metadata from camera controller if available
        if (fps > 0)
        {
            plyController.SetFps(fps);
        }

        if (totalFrames > 0)
        {
            plyController.SetTotalFrameCount(totalFrames);
        }

        return plyController;
    }

    private void InitializeViews()
    {
        if (processingType == ProcessingType.ONESHADER || processingType == ProcessingType.PLY)
        {
            InitializeMultiCameraView();
        }
        else
        {
            InitializeSingleCameraViews();
        }

        frameProcessingManager.SetViews(multiPointCloudView, singlePointCloudViews);
        plyExportManager?.SetMultiPointCloudView(multiPointCloudView);
    }

    private void InitializeMultiCameraView()
    {
        SetupStatusUI.ShowStatus($"Initializing multi-camera {processingType} processing...");

        GameObject multiViewObj = new GameObject("MultiPointCloudView");
        multiViewObj.transform.parent = transform;
        multiPointCloudView = multiViewObj.AddComponent<MultiPointCloudView>();
        multiPointCloudView.Initialize(cameraControllers); // Still needs CameraFrameController for device access

        SetupStatusUI.ShowStatus($"Multi-camera view initialized for {cameraControllers.Count} cameras");
    }

    private void InitializeSingleCameraViews()
    {
        SetupStatusUI.ShowStatus($"Initializing {cameraControllers.Count} individual camera views for {processingType} processing...");

        foreach (var cameraController in cameraControllers)
        {
            var processor = CreateAndSetupProcessor(cameraController);

            GameObject viewObj = new GameObject($"SinglePointCloudView_{cameraController.Device.deviceName}");
            viewObj.transform.parent = transform;
            var view = viewObj.AddComponent<SinglePointCloudView>();
            view.Initialize(cameraController, processor, processingType);
            singlePointCloudViews.Add(view);

            cameraController.Device.UpdateDeviceStatus(DeviceStatusType.Ready, ProcessingType.None, "Ready for first frame");
        }

        SetupStatusUI.ShowStatus($"Initialized {singlePointCloudViews.Count} camera views");
    }

    private IPointCloudProcessor CreateAndSetupProcessor(CameraFrameController cameraController)
    {
        var device = cameraController.Device;
        float depthBias = device.GetDepthBias();

        IPointCloudProcessor processor = PointCloudProcessorFactory.CreateBestProcessor(device.deviceName);
        device.UpdateDeviceStatus(DeviceStatusType.Loading, processingType, $"{processingType} processing enabled");

        processor.Setup(device, depthBias);

        var boundingVolumeObj = GameObject.Find("BoundingVolume");
        if (boundingVolumeObj != null)
        {
            processor.SetBoundingVolume(boundingVolumeObj.transform);
        }

        processor.ApplyDepthToColorExtrinsics(
            device.GetDepthToColorTranslation(),
            device.GetDepthToColorRotation()
        );
        processor.SetupColorIntrinsics(device.GetColorParser().sensorHeader);
        processor.SetupCameraMetadata(device);

        SetupStatusUI.ShowStatus($"Processor setup complete for {device.deviceName}");
        return processor;
    }

    private void SetupTimelineDuration()
    {
        if (frameControllers.Count == 0)
        {
            Debug.LogWarning("No frame controllers available");
            return;
        }

        int fps = GetFpsFromHeader();
        if (fps <= 0)
        {
            Debug.LogError($"Invalid FPS ({fps})");
            return;
        }

        int totalFrameCount = GetTotalFrameCount();
        if (totalFrameCount <= 0)
        {
            Debug.LogError($"Invalid frame count ({totalFrameCount})");
            return;
        }

        var timelineController = FindObjectOfType<TimelineController>();
        if (timelineController != null)
        {
            timelineController.SetDuration(totalFrameCount, fps);
        }

        if (plyExportManager != null)
        {
            plyExportManager.SetTotalFramesForExport(totalFrameCount, 0);
        }
    }

    #endregion

    #region Frame Navigation

    private void ProcessFirstPlyFrameIfNeeded()
    {
        if (!plyFrameController.IsFirstFrameProcessed)
        {
            LoadPlyFrame(0);
        }
    }

    private void HandlePlyModeNavigation()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            LoadNextPlyFrame();
        }

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            LoadPreviousPlyFrame();
        }
    }

    private void LoadNextPlyFrame()
    {
        int currentFrame = (int)(plyFrameController.CurrentTimestamp / (1_000_000_000UL / (ulong)plyFrameController.GetFps()));
        int nextFrame = currentFrame + 1;

        if (nextFrame >= plyFrameController.GetTotalFrameCount())
        {
            Debug.Log("Already at last PLY frame");
            return;
        }

        LoadPlyFrame(nextFrame);
    }

    private void LoadPreviousPlyFrame()
    {
        int currentFrame = (int)(plyFrameController.CurrentTimestamp / (1_000_000_000UL / (ulong)plyFrameController.GetFps()));
        int previousFrame = currentFrame - 1;

        if (previousFrame < 0)
        {
            Debug.Log("Already at first PLY frame");
            return;
        }

        LoadPlyFrame(previousFrame);
    }

    private void LoadPlyFrame(int frameIndex)
    {
        if (plyFrameController.TryGetPlyFilePath(frameIndex, out string filePath))
        {
            if (multiPointCloudView != null)
            {
                multiPointCloudView.LoadFromPLY(filePath);
            }
            plyFrameController.UpdateCurrentTimestamp(plyFrameController.GetTimestampForFrame(frameIndex));
            plyFrameController.NotifyFirstFrameProcessed();
            Debug.Log($"Loaded PLY frame {frameIndex}: {filePath}");
        }
        else
        {
            Debug.LogWarning($"PLY file not found for frame {frameIndex}");
        }
    }

    private void HandleSynchronizedFrameNavigation()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            NavigateToNextSynchronizedFrame();
        }

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            Debug.LogWarning("Backward navigation not supported. Use timeline controls.");
        }
    }

    private void NavigateToNextSynchronizedFrame()
    {
        if (processingType == ProcessingType.GPU && singlePointCloudViews.Count == 0) return;

        ulong nextTimestamp = FindNextSynchronizedTimestamp();
        if (nextTimestamp == 0)
        {
            Debug.Log("No more synchronized frames available");
            return;
        }

        ProcessFrame(frameProcessingManager.CurrentFrameIndex + 1, nextTimestamp);
    }

    private ulong FindNextSynchronizedTimestamp()
    {
        if (processingType == ProcessingType.PLY)
        {
            return GetTargetTimestamp(frameProcessingManager.CurrentFrameIndex + 1);
        }

        try
        {
            if (processingType == ProcessingType.ONESHADER)
            {
                if (leadingCameraIndex >= frameControllers.Count) return 0;
                var leadingController = frameControllers[leadingCameraIndex];
                if (leadingController != null && leadingController.PeekNextTimestamp(out ulong timestamp))
                {
                    return timestamp;
                }
                return 0;
            }
            else
            {
                if (leadingCameraIndex >= singlePointCloudViews.Count) return 0;
                var leadingView = singlePointCloudViews[leadingCameraIndex];
                if (leadingView != null && leadingView.PeekNextTimestamp(out ulong timestamp))
                {
                    return timestamp;
                }
                return 0;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error finding next timestamp: {ex.Message}");
            return 0;
        }
    }

    private void UpdateLeadingCameraIndex()
    {
        if (processingType == ProcessingType.PLY) return;

        int newLeadingIndex = 0;
        ulong foremostTimestamp = ulong.MinValue;

        if (processingType == ProcessingType.ONESHADER)
        {
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

    #endregion

    #region Public API

    public void SeekToFrame(int frameIndex)
    {
        Debug.Log($"SeekToFrame: {frameIndex}");
        ulong targetTimestamp = GetTargetTimestamp(frameIndex);
        ProcessFrame(frameIndex, targetTimestamp);
    }

    public void ProcessFrame(int frameIndex, ulong targetTimestamp)
    {
        if (processingType == ProcessingType.PLY)
        {
            LoadPlyFrame(frameIndex);
        }
        else
        {
            frameProcessingManager.ProcessFrame(frameIndex, targetTimestamp, processingType);
            UpdateLeadingCameraIndex();
        }
    }

    public void ResetToFirstFrame()
    {
        SeekToFrame(0);
    }

    public int GetTotalFrameCount()
    {
        if (frameControllers.Count == 0) return -1;
        return frameControllers[0].GetTotalFrameCount();
    }

    public int GetFpsFromHeader()
    {
        if (frameControllers.Count == 0) return -1;
        return frameControllers[0].GetFps();
    }

    #endregion

    #region Helper Methods

    private ulong GetTargetTimestamp(int frameIndex)
    {
        if (frameControllers.Count > 0)
        {
            return frameControllers[0].GetTimestampForFrame(frameIndex);
        }

        int fps = GetFpsFromHeader();
        if (fps > 0)
        {
            ulong nanosecondsPerFrame = (ulong)(1_000_000_000L / fps);
            return (ulong)frameIndex * nanosecondsPerFrame;
        }

        Debug.LogError($"Cannot estimate timestamp for frame {frameIndex}");
        return 0;
    }

    private int CalculateTotalFrameCount(SensorDevice device)
    {
        try
        {
            var depthParser = device.GetDepthParser();
            if (depthParser == null) return -1;

            long fileSize = new FileInfo(device.GetDepthFilePath()).Length;
            int headerMetadataSize = depthParser.sensorHeader.MetadataSize;
            int headerImageSize = depthParser.sensorHeader.ImageSize;
            int recordSize = headerMetadataSize + headerImageSize;

            long dataSize = fileSize - headerMetadataSize;
            return (int)(dataSize / recordSize);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to calculate frame count: {ex.Message}");
            return -1;
        }
    }

    private void LoadDatasetInfo()
    {
        string datasetYamlPath = Path.Combine(rootDirectory, "dataset.yaml");
        if (File.Exists(datasetYamlPath))
        {
            try
            {
                DatasetInfo datasetInfo = YamlLoader.Load<DatasetInfo>(datasetYamlPath);
                displayName = datasetInfo.displayName ?? "";
                Debug.Log($"Loaded dataset display name: {displayName}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to load dataset.yaml: {ex.Message}");
                displayName = Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
        else
        {
            Debug.LogWarning($"dataset.yaml not found");
            displayName = Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    #endregion

    void OnDestroy()
    {
        if (processingType == ProcessingType.ONESHADER || processingType == ProcessingType.PLY)
        {
            if (multiPointCloudView != null)
            {
                DestroyImmediate(multiPointCloudView.gameObject);
            }
        }
        else
        {
            foreach (var view in singlePointCloudViews)
            {
                if (view != null)
                {
                    DestroyImmediate(view.gameObject);
                }
            }
            singlePointCloudViews.Clear();
        }

        foreach (var controller in frameControllers)
        {
            controller?.Dispose();
        }
        frameControllers.Clear();
    }
}
