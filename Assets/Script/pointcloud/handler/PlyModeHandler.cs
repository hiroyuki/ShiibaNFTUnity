using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Handler for PLY mode - loads pre-exported PLY files
/// Provides simple playback without processing raw sensor data
/// Supports both regular PLY and PLY with motion vectors
/// </summary>
public class PlyModeHandler : BaseProcessingModeHandler
{
    private PlyFrameController plyFrameController;
    private ProcessingType processingType;

    public override ProcessingType ProcessingType => processingType;

    public PlyModeHandler(ProcessingType type = ProcessingType.PLY)
    {
        processingType = type;
    }

    protected override bool InitializeInternal()
    {
        SetupStatusUI.ShowStatus("Initializing PLY mode...");

        // Determine PLY folder based on processing type
        string plyFolderName = processingType == ProcessingType.PLY_WITH_MOTION ? "PLY_WithMotion" : "PLY";
        string plyFolder = System.IO.Path.Combine(rootDirectory, plyFolderName);

        if (System.IO.Directory.Exists(plyFolder))
        {
            // Use PLY folder as root for the frame controller
            plyFrameController = new PlyFrameController(plyFolder, displayName);
            Debug.Log($"Using PLY folder: {plyFolder}");
        }
        else
        {
            // Fall back to legacy Export folder
            plyFrameController = new PlyFrameController(rootDirectory, displayName);
            Debug.Log($"Using Export folder: {System.IO.Path.Combine(rootDirectory, "Export")}");
        }

        // Check if PLY files exist
        if (!plyFrameController.ShouldEnablePlyMode())
        {
            return false;
        }
        SetupStatusUI.ShowStatus($"PLY mode: {plyFrameController.GetTotalFrameCount()} files loaded");

        // Initialize viewer
        InitializeViewer();

        return true;
    }

    private void InitializeViewer()
    {
        SetupStatusUI.ShowStatus("Initializing PLY viewer...");

        GameObject multiViewObj = CreateMultiPointCloudViewObject("MultiPointCloudView_PLY");
        multiPointCloudView = multiViewObj.AddComponent<MultiPointCloudView>();

        // Setup unified viewer for PLY mode (creates the mesh)
        multiPointCloudView.SetupUnifiedViewer();

        SetupStatusUI.ShowStatus($"PLY viewer initialized: {plyFrameController.GetTotalFrameCount()} frames available");
    }

    public override void Update()
    {
        ProcessFirstFramesIfNeeded();
        HandleArrowKeyNavigation();
    }

    public override void ProcessFirstFramesIfNeeded()
    {
        if (!plyFrameController.IsFirstFrameProcessed)
        {
            // If timeline is available, sync it so BVH updates properly
            TimelineUtil.SeekToTime(0);
        }
    }

    /// <summary>
    /// Handle arrow key navigation for frame seeking
    /// Updates both PLY point cloud and BVH skeleton via timeline synchronization
    /// </summary>
    private void HandleArrowKeyNavigation()
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

    /// <summary>
    /// Seek to next frame via arrow key
    /// </summary>
    private void LoadNextPlyFrame()
    {
        int currentFrame = GetCurrentFrameIndex();
        int nextFrame = currentFrame + 1;

        if (nextFrame >= plyFrameController.GetTotalFrameCount())
        {
            return;
        }

        SeekToFrameWithTimelineSync(nextFrame);
    }

    /// <summary>
    /// Seek to previous frame via arrow key
    /// </summary>
    private void LoadPreviousPlyFrame()
    {
        int currentFrame = GetCurrentFrameIndex();
        int previousFrame = currentFrame - 1;

        if (previousFrame < 0)
        {
            return;
        }

        SeekToFrameWithTimelineSync(previousFrame);
    }

    /// <summary>
    /// Get current frame index from controller timestamp
    /// </summary>
    private int GetCurrentFrameIndex()
    {
        return (int)(plyFrameController.CurrentTimestamp / (1_000_000_000UL / (ulong)plyFrameController.GetFps()));
    }

    /// <summary>
    /// Seek to frame and synchronize timeline for BVH updates
    /// </summary>
    private void SeekToFrameWithTimelineSync(int frameIndex)
    {
        if (!plyFrameController.TryGetPlyFilePath(frameIndex, out string filePath))
        {
            Debug.LogWarning($"PLY file not found for frame {frameIndex}");
            return;
        }

        // Update state tracking
        plyFrameController.UpdateCurrentTimestamp(plyFrameController.GetTimestampForFrame(frameIndex));
        plyFrameController.NotifyFirstFrameProcessed();

        // Always load PLY file directly first (this ensures the point cloud is displayed)
        if (multiPointCloudView != null)
        {
            multiPointCloudView.LoadFromPLY(filePath);
        }

        // Try to find timeline (lazy lookup - only when needed)
        PlayableDirector timelinePlayableDirector = Object.FindFirstObjectByType<PlayableDirector>();

        // If timeline is available, sync it so BVH updates properly
        if (timelinePlayableDirector != null)
        {
            int fps = plyFrameController.GetFps();
            double timelineTimeInSeconds = (double)frameIndex / fps;
            timelinePlayableDirector.time = timelineTimeInSeconds;
            timelinePlayableDirector.Evaluate();
        }
    }

    /// <summary>
    /// Load PLY frame during initialization
    /// Delegates to SeekToFrameWithTimelineSync for actual frame loading
    /// </summary>
    private void LoadPlyFrame(int frameIndex)
    {
        SeekToFrameWithTimelineSync(frameIndex);
    }

    public override void SeekToFrame(int frameIndex)
    {
        LoadPlyFrame(frameIndex);
    }

    public override void ProcessFrame(int frameIndex, ulong targetTimestamp)
    {
        LoadPlyFrame(frameIndex);
    }

    public override int GetTotalFrameCount()
    {
        return plyFrameController?.GetTotalFrameCount() ?? -1;
    }

    public override int GetFps()
    {
        return plyFrameController?.GetFps() ?? -1;
    }

    public override ulong GetTargetTimestamp(int frameIndex)
    {
        if (plyFrameController != null)
        {
            return plyFrameController.GetTimestampForFrame(frameIndex);
        }

        // Fall back to base implementation
        return base.GetTargetTimestamp(frameIndex);
    }

    public override void Dispose()
    {
        base.Dispose(); // Disposes multiPointCloudView
        plyFrameController?.Dispose();
    }
}
