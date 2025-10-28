using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Frame controller for PLY files - provides uniform interface with CameraFrameController
/// Manages PLY file discovery, caching, and frame navigation
/// </summary>
public class PlyFrameController : IFrameController
{
    private readonly string rootDirectory;
    private readonly string displayName;
    private readonly string plyExportDir;

    private Dictionary<int, string> plyFileCache = new();
    private int totalFrames = 0;
    private int currentFrameIndex = 0;
    private ulong currentTimestamp = 0;
    private bool firstFrameProcessed = false;
    private int fps = 30; // Default, will be updated from camera controller if available

    public string Name => $"PLY_{displayName}";
    public ulong CurrentTimestamp => currentTimestamp;
    public bool IsFirstFrameProcessed => firstFrameProcessed;
    public bool AutoLoadFirstFrame => true;
    public string PlyExportDir => plyExportDir;

    public PlyFrameController(string rootDirectory, string displayName)
    {
        this.rootDirectory = rootDirectory;
        this.displayName = string.IsNullOrEmpty(displayName) ? "pointcloud" : displayName;

        // Try to find PLY files in this directory first (for Assets-based datasets where PLY is the root)
        // If not found, look in an "Export" subdirectory (for legacy datasets)
        if (Directory.Exists(rootDirectory))
        {
            string[] plyFiles = Directory.GetFiles(rootDirectory, "*.ply");
            if (plyFiles.Length > 0)
            {
                // PLY files are directly in this directory
                this.plyExportDir = rootDirectory;
            }
            else
            {
                // Fall back to looking in Export subdirectory
                this.plyExportDir = Path.Combine(rootDirectory, "Export");
            }
        }
        else
        {
            this.plyExportDir = Path.Combine(rootDirectory, "Export");
        }

        DiscoverPlyFiles();
    }

    /// <summary>
    /// Discover and cache all PLY files in the export directory
    /// </summary>
    private void DiscoverPlyFiles()
    {
        if (!Directory.Exists(plyExportDir))
        {
            Debug.Log($"PLY directory not found: {plyExportDir}");
            return;
        }

        string fileBaseName = displayName.Replace(" ", "_");
        string searchPattern = $"{fileBaseName}*.ply";
        string[] plyFiles = Directory.GetFiles(plyExportDir, searchPattern);

        foreach (string filePath in plyFiles)
        {
            string filename = Path.GetFileNameWithoutExtension(filePath);
            // Extract frame number from filename (last 6 digits)
            if (filename.Length >= 6)
            {
                string frameNumStr = filename.Substring(filename.Length - 6);
                if (int.TryParse(frameNumStr, out int frameNum))
                {
                    plyFileCache[frameNum] = filePath;
                }
            }
        }

        totalFrames = plyFileCache.Count;
        Debug.Log($"PlyFrameController discovered {totalFrames} PLY files");
    }

    /// <summary>
    /// Set FPS from external source (e.g., camera controller)
    /// </summary>
    public void SetFps(int fps)
    {
        this.fps = fps;
    }

    /// <summary>
    /// Set total frame count from external source
    /// </summary>
    public void SetTotalFrameCount(int totalFrames)
    {
        this.totalFrames = totalFrames;
    }

    /// <summary>
    /// Try to get PLY file path for a given frame index
    /// </summary>
    public bool TryGetPlyFilePath(int frameIndex, out string filePath)
    {
        return plyFileCache.TryGetValue(frameIndex, out filePath);
    }

    /// <summary>
    /// Generate PLY filepath for a given frame index
    /// </summary>
    public string GeneratePlyFilePath(int frameIndex)
    {
        Directory.CreateDirectory(plyExportDir);
        string fileBaseName = displayName.Replace(" ", "_");
        string filename = $"{fileBaseName}{frameIndex:D6}.ply";
        return Path.Combine(plyExportDir, filename);
    }

    /// <summary>
    /// Add a PLY file to the cache
    /// </summary>
    public void CachePlyFile(int frameIndex, string filePath)
    {
        if (!plyFileCache.ContainsKey(frameIndex))
        {
            plyFileCache[frameIndex] = filePath;
            totalFrames = plyFileCache.Count;
        }
    }

    /// <summary>
    /// Check if PLY file exists for a frame
    /// </summary>
    public bool HasPlyFile(int frameIndex)
    {
        return plyFileCache.ContainsKey(frameIndex);
    }

    /// <summary>
    /// Check if PLY mode should be enabled (has existing PLY files discovered)
    /// </summary>
    public bool ShouldEnablePlyMode()
    {
        return totalFrames > 0 && plyFileCache.Count > 0;
    }

    #region IFrameController Implementation

    public ulong GetTimestampForFrame(int frameIndex)
    {
        // Calculate synthetic timestamp based on FPS
        if (fps > 0)
        {
            ulong nanosecondsPerFrame = (ulong)(1_000_000_000L / fps);
            return (ulong)frameIndex * nanosecondsPerFrame;
        }
        return (ulong)frameIndex;
    }

    public bool PeekNextTimestamp(out ulong timestamp)
    {
        int nextFrameIndex = currentFrameIndex + 1;
        if (nextFrameIndex < totalFrames)
        {
            timestamp = GetTimestampForFrame(nextFrameIndex);
            return true;
        }

        timestamp = 0;
        return false;
    }

    public int GetFps()
    {
        return fps;
    }

    public int GetTotalFrameCount()
    {
        return totalFrames;
    }

    public void NotifyFirstFrameProcessed()
    {
        if (!firstFrameProcessed)
        {
            SetupStatusUI.OnFirstFrameProcessed();
            firstFrameProcessed = true;
        }
    }

    public void UpdateCurrentTimestamp(ulong timestamp)
    {
        currentTimestamp = timestamp;

        // Update frame index based on timestamp
        if (fps > 0)
        {
            ulong nanosecondsPerFrame = (ulong)(1_000_000_000L / fps);
            currentFrameIndex = (int)(timestamp / nanosecondsPerFrame);
        }
    }

    public void Dispose()
    {
        plyFileCache.Clear();
    }

    #endregion
}
