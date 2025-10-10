using System;

/// <summary>
/// Common interface for frame controllers (camera binary data or PLY files)
/// Provides unified API for frame management and navigation
/// </summary>
public interface IFrameController
{
    /// <summary>
    /// Display name for this frame source
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Current timestamp (may be simulated for PLY files)
    /// </summary>
    ulong CurrentTimestamp { get; }

    /// <summary>
    /// Whether the first frame has been processed
    /// </summary>
    bool IsFirstFrameProcessed { get; }

    /// <summary>
    /// Whether to automatically load the first frame
    /// </summary>
    bool AutoLoadFirstFrame { get; }

    /// <summary>
    /// Get timestamp for a specific frame index
    /// </summary>
    ulong GetTimestampForFrame(int frameIndex);

    /// <summary>
    /// Peek at the next timestamp without consuming it
    /// </summary>
    bool PeekNextTimestamp(out ulong timestamp);

    /// <summary>
    /// Get FPS for this frame source
    /// </summary>
    int GetFps();

    /// <summary>
    /// Get total frame count
    /// </summary>
    int GetTotalFrameCount();

    /// <summary>
    /// Mark first frame as processed
    /// </summary>
    void NotifyFirstFrameProcessed();

    /// <summary>
    /// Update current timestamp
    /// </summary>
    void UpdateCurrentTimestamp(ulong timestamp);

    /// <summary>
    /// Dispose of resources
    /// </summary>
    void Dispose();
}
