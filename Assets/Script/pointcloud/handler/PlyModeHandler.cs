using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handler for PLY mode - loads pre-exported PLY files
/// Provides simple playback without processing raw sensor data
/// </summary>
public class PlyModeHandler : BaseProcessingModeHandler
{
    private PlyFrameController plyFrameController;

    public override ProcessingType ProcessingType => ProcessingType.PLY;

    protected override bool InitializeInternal()
    {
        SetupStatusUI.ShowStatus("Initializing PLY mode...");

        // Create PLY frame controller
        plyFrameController = new PlyFrameController(rootDirectory, displayName);

        // Check if PLY files exist
        if (!plyFrameController.ShouldEnablePlyMode())
        {
            Debug.Log("No PLY files found. PLY mode unavailable.");
            return false;
        }

        Debug.Log($"PLY mode enabled: {plyFrameController.GetTotalFrameCount()} files found");
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
        HandlePlyModeNavigation();
    }

    public override void ProcessFirstFramesIfNeeded()
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

    public override void SeekToFrame(int frameIndex)
    {
        Debug.Log($"[PLY Mode] SeekToFrame: {frameIndex}");
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
