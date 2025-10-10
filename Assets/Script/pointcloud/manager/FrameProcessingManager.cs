using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages frame processing for different processing types (ONESHADER, PLY, GPU, CPU)
/// Works with IFrameController for unified frame management
/// </summary>
public class FrameProcessingManager
{
    private readonly List<IFrameController> frameControllers;
    private PlyFrameController plyFrameController;

    private MultiPointCloudView multiPointCloudView;
    private List<SinglePointCloudView> singlePointCloudViews;

    private volatile bool isProcessing = false;
    private int currentFrameIndex = 0;
    private string displayName = "";

    public int CurrentFrameIndex => currentFrameIndex;
    public bool IsProcessing => isProcessing;

    public FrameProcessingManager(List<IFrameController> frameControllers)
    {
        this.frameControllers = frameControllers;
    }

    public void SetPlyFrameController(PlyFrameController plyController)
    {
        this.plyFrameController = plyController;
    }

    public void SetDisplayName(string displayName)
    {
        this.displayName = displayName;
    }

    public void SetViews(MultiPointCloudView multiView, List<SinglePointCloudView> singleViews)
    {
        this.multiPointCloudView = multiView;
        this.singlePointCloudViews = singleViews;
    }

    /// <summary>
    /// Process a frame based on the current processing type
    /// </summary>
    public void ProcessFrame(int frameIndex, ulong targetTimestamp, ProcessingType processingType)
    {
        if (isProcessing)
        {
            Debug.LogWarning("Frame processing already in progress, skipping...");
            return;
        }

        isProcessing = true;

        try
        {
            if (processingType == ProcessingType.PLY)
            {
                ProcessPlyFrame(frameIndex, targetTimestamp);
            }
            else if (processingType == ProcessingType.ONESHADER)
            {
                ProcessOneshaderFrame(targetTimestamp);
            }
            else
            {
                ProcessIndividualCameraFrames(targetTimestamp, processingType);
            }

            currentFrameIndex = frameIndex;
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

    private void ProcessPlyFrame(int frameIndex, ulong targetTimestamp)
    {
        if (plyFrameController == null)
        {
            Debug.LogError("PLY frame controller not set");
            return;
        }

        // Load from PLY file
        if (plyFrameController.TryGetPlyFilePath(frameIndex, out string plyFilePath))
        {
            SetupStatusUI.ShowStatus($"Loading frame {frameIndex} from PLY...");
            multiPointCloudView?.LoadFromPLY(plyFilePath);
            SetupStatusUI.ShowStatus($"PLY frame {frameIndex} loaded");
        }
        else
        {
            // PLY file doesn't exist, generate and export it
            Debug.Log($"PLY file for frame {frameIndex} not found. Generating from binary data...");
            SetupStatusUI.ShowStatus($"Generating PLY for frame {frameIndex}...");

            // Process using ONESHADER
            multiPointCloudView?.ProcessFrame(targetTimestamp);

            // Export to PLY
            string filepath = plyFrameController.GeneratePlyFilePath(frameIndex);
            multiPointCloudView?.ExportToPLY(filepath);

            // Add to cache
            plyFrameController.CachePlyFile(frameIndex, filepath);
            Debug.Log($"PLY file generated and cached: {filepath}");
            SetupStatusUI.ShowStatus($"PLY frame {frameIndex} generated and loaded");
        }
    }

    private void ProcessOneshaderFrame(ulong targetTimestamp)
    {
        // Use multi-camera GPU processing
        SetupStatusUI.ShowStatus($"Processing frame at timestamp {targetTimestamp} using ONESHADER ({frameControllers.Count} cameras)...");
        multiPointCloudView?.ProcessFrame(targetTimestamp);
        SetupStatusUI.ShowStatus($"ONESHADER processing complete for {frameControllers.Count} cameras");
    }

    private void ProcessIndividualCameraFrames(ulong targetTimestamp, ProcessingType processingType)
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

    /// <summary>
    /// Process first frames if needed
    /// </summary>
    public void ProcessFirstFramesIfNeeded(ProcessingType processingType)
    {
        if (processingType == ProcessingType.PLY)
        {
            // No auto-processing needed for PLY mode
            return;
        }
        else if (processingType == ProcessingType.ONESHADER)
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
    }
}
