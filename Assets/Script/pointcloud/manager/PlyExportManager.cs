using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages PLY export operations including single frame and batch export
/// </summary>
public class PlyExportManager
{
    private PlyFrameController plyFrameController;
    private MultiPointCloudView multiPointCloudView;

    private bool isExportingAllFrames = false;
    private int exportFrameIndex = 0;
    private int exportTotalFrames = 0;

    public bool IsExportingAllFrames => isExportingAllFrames;

    public PlyExportManager(PlyFrameController plyFrameController)
    {
        this.plyFrameController = plyFrameController;
    }

    public void SetPlyFrameController(PlyFrameController plyController)
    {
        this.plyFrameController = plyController;
    }

    public void SetMultiPointCloudView(MultiPointCloudView view)
    {
        this.multiPointCloudView = view;
    }

    /// <summary>
    /// Handle keyboard input for PLY export (O key for current frame, Shift+E for all frames)
    /// </summary>
    public void HandlePlyExportInput(ProcessingType processingType, int currentFrameIndex)
    {
        if (Keyboard.current == null) return;

        // Press 'O' key to export current frame to PLY
        if (Keyboard.current.oKey.wasPressedThisFrame)
        {
            ExportCurrentFrameToPLY(processingType, currentFrameIndex);
        }

        // Press 'Shift+E' to export all frames
        if ((Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed) &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            StartExportAllFrames(processingType);
        }
    }

    /// <summary>
    /// Process batch export frame-by-frame (call this in Update loop)
    /// </summary>
    public void ProcessBatchExport(System.Action<int, ulong> processFrameCallback, System.Func<int, ulong> getTargetTimestamp)
    {
        if (!isExportingAllFrames) return;

        if (exportFrameIndex >= exportTotalFrames)
        {
            // Export complete
            isExportingAllFrames = false;
            Debug.Log($"Batch PLY export complete: {exportTotalFrames} frames exported");
            SetupStatusUI.ShowStatus($"Export complete: {exportTotalFrames} frames");
            return;
        }

        if (plyFrameController == null)
        {
            Debug.LogError("PLY frame controller not set");
            isExportingAllFrames = false;
            return;
        }

        string filepath = plyFrameController.GeneratePlyFilePath(exportFrameIndex);

        // Skip if file already exists
        if (File.Exists(filepath))
        {
            Debug.Log($"PLY file already exists, skipping frame {exportFrameIndex}: {filepath}");
            exportFrameIndex++;
            SetupStatusUI.ShowStatus($"Exporting frames (skipping existing)... {exportFrameIndex}/{exportTotalFrames}");
            return;
        }

        // Get target timestamp for this frame
        ulong targetTimestamp = getTargetTimestamp(exportFrameIndex);

        // Process frame
        processFrameCallback(exportFrameIndex, targetTimestamp);

        // Export after processing
        if (multiPointCloudView != null)
        {
            multiPointCloudView.ExportToPLY(filepath);
            exportFrameIndex++;
            SetupStatusUI.ShowStatus($"Exporting frames... {exportFrameIndex}/{exportTotalFrames}");
        }
    }

    private void ExportCurrentFrameToPLY(ProcessingType processingType, int currentFrameIndex)
    {
        if (processingType == ProcessingType.ONESHADER || processingType == ProcessingType.PLY)
        {
            if (multiPointCloudView != null && plyFrameController != null)
            {
                string filepath = plyFrameController.GeneratePlyFilePath(currentFrameIndex);

                // Skip if file already exists
                if (File.Exists(filepath))
                {
                    Debug.Log($"PLY file already exists, skipping: {filepath}");
                    return;
                }

                multiPointCloudView.ExportToPLY(filepath);
                Debug.Log($"PLY export saved to: {filepath}");
            }
        }
        else
        {
            Debug.LogWarning("PLY export is currently only supported in ONESHADER and PLY modes");
        }
    }

    private void StartExportAllFrames(ProcessingType processingType)
    {
        if (processingType != ProcessingType.ONESHADER && processingType != ProcessingType.PLY)
        {
            Debug.LogWarning("Batch PLY export is only supported in ONESHADER and PLY modes");
            return;
        }

        if (multiPointCloudView == null || plyFrameController == null)
        {
            Debug.LogError("MultiPointCloudView or PLY frame controller not available for batch export");
            return;
        }

        // Total frames will be set externally
        isExportingAllFrames = true;
        exportFrameIndex = 0;

        Debug.Log($"Starting batch PLY export to {plyFrameController.PlyExportDir}");
        SetupStatusUI.ShowStatus($"Exporting all frames to PLY... {exportFrameIndex}/{exportTotalFrames}");
    }

    public void SetTotalFramesForExport(int totalFrames, int startFrameIndex)
    {
        exportTotalFrames = totalFrames;
        exportFrameIndex = startFrameIndex;
    }
}
