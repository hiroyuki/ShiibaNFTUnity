using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using YamlDotNet.Serialization;

/// <summary>
/// Handler for Binary mode - processes raw sensor data from binary files
/// Supports ONESHADER, GPU, and CPU processing types with optional PLY export
/// </summary>
public class BinaryModeHandler : BaseProcessingModeHandler
{
    private List<IFrameController> frameControllers = new();
    private List<CameraFrameController> cameraControllers = new();
    private ProcessingType processingType;

    private List<SinglePointCloudView> singlePointCloudViews = new();

    private FrameProcessingManager frameProcessingManager;
    private PlyExportManager plyExportManager;

    private bool enablePlyExport;
    private int leadingCameraIndex = 0;

    public override ProcessingType ProcessingType => processingType;

    public BinaryModeHandler(ProcessingType processingType = ProcessingType.ONESHADER, bool enablePlyExport = false)
    {
        this.processingType = processingType;
        this.enablePlyExport = enablePlyExport;
    }

    protected override bool InitializeInternal()
    {
        SetupStatusUI.ShowStatus("Initializing Binary mode...");

        if (!LoadCameraControllers())
            return false;

        InitializeBinaryManagers();
        InitializeViews();

        // Optionally setup PLY export
        if (enablePlyExport)
        {
            SetupPlyExport();
        }

        return true;
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
                frameControllers.Add(cameraController);
            }
            else
            {
                Debug.LogWarning($"Skipping device {deviceDirName}: missing depth or color file");
            }
        }

        SetupStatusUI.SetProgress(1f);
        SetupStatusUI.ShowStatus($"Loaded {cameraControllers.Count} camera controllers");
        return cameraControllers.Count > 0;
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
        if (processingType == ProcessingType.ONESHADER)
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

        GameObject multiViewObj = CreateMultiPointCloudViewObject("MultiPointCloudView");
        multiPointCloudView = multiViewObj.AddComponent<MultiPointCloudView>();
        multiPointCloudView.Initialize(cameraControllers);

        SetupStatusUI.ShowStatus($"Multi-camera view initialized for {cameraControllers.Count} cameras");
    }

    private void InitializeSingleCameraViews()
    {
        SetupStatusUI.ShowStatus($"Initializing {cameraControllers.Count} individual camera views for {processingType} processing...");

        foreach (var cameraController in cameraControllers)
        {
            var processor = CreateAndSetupProcessor(cameraController);

            GameObject viewObj = new GameObject($"SinglePointCloudView_{cameraController.Device.deviceName}");
            viewObj.transform.parent = parentTransform;
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

    public override void Update()
    {
        ProcessFirstFramesIfNeeded();
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

    public override void ProcessFirstFramesIfNeeded()
    {
        frameProcessingManager?.ProcessFirstFramesIfNeeded(processingType);
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

    public override void SeekToFrame(int frameIndex)
    {
        Debug.Log($"[Binary Mode] SeekToFrame: {frameIndex}");
        ulong targetTimestamp = GetTargetTimestamp(frameIndex);
        ProcessFrame(frameIndex, targetTimestamp);
    }

    public override void ProcessFrame(int frameIndex, ulong targetTimestamp)
    {
        frameProcessingManager.ProcessFrame(frameIndex, targetTimestamp, processingType);
        UpdateLeadingCameraIndex();
    }

    public override int GetTotalFrameCount()
    {
        if (frameControllers.Count == 0) return -1;
        return frameControllers[0].GetTotalFrameCount();
    }

    public override int GetFps()
    {
        if (frameControllers.Count == 0) return -1;
        return frameControllers[0].GetFps();
    }

    public override ulong GetTargetTimestamp(int frameIndex)
    {
        if (frameControllers.Count > 0)
        {
            return frameControllers[0].GetTimestampForFrame(frameIndex);
        }

        // Fall back to base implementation
        return base.GetTargetTimestamp(frameIndex);
    }

    public override void SetupTimelineDuration(TimelineController timelineController)
    {
        base.SetupTimelineDuration(timelineController);

        if (plyExportManager != null)
        {
            int totalFrameCount = GetTotalFrameCount();
            plyExportManager.SetTotalFramesForExport(totalFrameCount, 0);
        }
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

    public override void Dispose()
    {
        if (processingType == ProcessingType.ONESHADER)
        {
            base.Dispose(); // Disposes multiPointCloudView
        }
        else
        {
            foreach (var view in singlePointCloudViews)
            {
                if (view != null)
                {
                    Object.DestroyImmediate(view.gameObject);
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
