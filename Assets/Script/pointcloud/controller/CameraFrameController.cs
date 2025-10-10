using System;
using UnityEngine;

/// <summary>
/// Controls frame-by-frame data management for a single camera sensor.
/// Handles SensorDevice operations, frame seeking, parsing, and synchronization.
/// This is a pure data management class without Unity MonoBehaviour dependencies.
/// Managed by MultiCamPointCloudManager and used by both SinglePointCloudView and MultiPointCloudView.
/// </summary>
public class CameraFrameController : IFrameController
{
    private SensorDevice device;
    private ulong currentTimestamp = 0;
    private bool firstFrameProcessed = false;
    private int totalFrameCount = -1;
    private bool autoLoadFirstFrame = true;

    // IFrameController interface properties
    public string Name => device?.deviceName ?? "Unknown";
    public ulong CurrentTimestamp => currentTimestamp;
    public bool IsFirstFrameProcessed => firstFrameProcessed;
    public bool AutoLoadFirstFrame => autoLoadFirstFrame;

    // Camera-specific properties
    public string DeviceName => device?.deviceName ?? "Unknown";
    public SensorDevice Device => device;

    public CameraFrameController(string rootDir, string hostname, string deviceName)
    {
        device = new SensorDevice();
        device.setup(rootDir, hostname, deviceName);
        totalFrameCount = -1; // Unknown, will be estimated
    }

    /// <summary>
    /// Seek to a specific timestamp, handling synchronization between depth and color streams.
    /// </summary>
    public bool SeekToTimestamp(ulong targetTimestamp, out ulong actualTimestamp)
    {
        // Reset parsers if seeking backwards
        if (currentTimestamp > targetTimestamp)
        {
            device.ResetParsers();
        }

        bool synchronized = false;
        actualTimestamp = 0;
        ulong depthTs = 0;

        while (!synchronized)
        {
            // Check synchronization using unified method
            synchronized = device.CheckSynchronization(out depthTs, out ulong colorTs, out long delta);

            if (!synchronized)
            {
                if (depthTs == 0 && colorTs == 0)
                {
                    // No more data
                    break;
                }
                else
                {
                    // Skip the earlier timestamp to catch up
                    if (delta < 0)
                    {
                        // Depth is behind color, skip depth frame
                        device.SkipDepthRecord();
                    }
                    else
                    {
                        // Color is behind depth, skip color frame
                        device.SkipColorRecord();
                    }
                }
            }

            // If synchronized but timestamp is before target, keep seeking
            if (synchronized && depthTs < targetTimestamp)
            {
                synchronized = false;
                device.SkipCurrentRecord();
            }
        }

        actualTimestamp = depthTs;
        return synchronized;
    }

    /// <summary>
    /// Parse the current record (depth and color data).
    /// </summary>
    public bool ParseRecord(bool optimizeForGPU)
    {
        return device.ParseRecord(optimizeForGPU);
    }

    /// <summary>
    /// Update texture after parsing (converts JPEG to texture if needed).
    /// </summary>
    public void UpdateTexture(bool optimizeForGPU)
    {
        device.UpdateTexture(optimizeForGPU);
    }

    /// <summary>
    /// Update the current timestamp after successful frame processing.
    /// </summary>
    public void UpdateCurrentTimestamp(ulong timestamp)
    {
        currentTimestamp = timestamp;
    }

    /// <summary>
    /// Mark first frame as processed.
    /// </summary>
    public void NotifyFirstFrameProcessed()
    {
        if (!firstFrameProcessed)
        {
            SetupStatusUI.OnFirstFrameProcessed();
            firstFrameProcessed = true;
        }
    }

    /// <summary>
    /// Get timestamp for a specific frame index.
    /// </summary>
    public ulong GetTimestampForFrame(int frameIndex)
    {
        return device.GetTimestampForFrame(frameIndex);
    }

    /// <summary>
    /// Peek at the next timestamp without consuming it.
    /// </summary>
    public bool PeekNextTimestamp(out ulong timestamp)
    {
        return device.PeekNextTimestamp(out timestamp);
    }

    /// <summary>
    /// Reset parsers to the beginning of the stream.
    /// </summary>
    public void ResetParsers()
    {
        device.ResetParsers();
    }

    /// <summary>
    /// Get FPS from sensor header.
    /// </summary>
    public int GetFps()
    {
        return device.GetFpsFromHeader();
    }

    /// <summary>
    /// Get total frame count (may be estimated).
    /// </summary>
    public int GetTotalFrameCount()
    {
        return totalFrameCount;
    }

    /// <summary>
    /// Dispose of resources.
    /// </summary>
    public void Dispose()
    {
        device?.Dispose();
    }
}
