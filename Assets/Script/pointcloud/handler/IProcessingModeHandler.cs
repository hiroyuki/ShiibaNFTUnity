using UnityEngine;

/// <summary>
/// Interface for different processing mode handlers (PLY, Binary, etc.)
/// Each mode handles initialization, frame processing, and navigation differently
/// </summary>
public interface IProcessingModeHandler
{
    /// <summary>
    /// The processing type this handler manages
    /// </summary>
    ProcessingType ProcessingType { get; }

    /// <summary>
    /// Initialize the handler with necessary components
    /// </summary>
    bool Initialize(string rootDirectory, string displayName, Transform parentTransform);

    /// <summary>
    /// Update called every frame
    /// </summary>
    void Update();

    /// <summary>
    /// Process first frames if needed
    /// </summary>
    void ProcessFirstFramesIfNeeded();

    /// <summary>
    /// Seek to a specific frame index
    /// </summary>
    void SeekToFrame(int frameIndex);

    /// <summary>
    /// Process a specific frame with timestamp
    /// </summary>
    void ProcessFrame(int frameIndex, ulong targetTimestamp);

    /// <summary>
    /// Get total frame count
    /// </summary>
    int GetTotalFrameCount();

    /// <summary>
    /// Get FPS from header/metadata
    /// </summary>
    int GetFps();

    /// <summary>
    /// Get timestamp for a specific frame index
    /// </summary>
    ulong GetTargetTimestamp(int frameIndex);

    /// <summary>
    /// Setup timeline duration
    /// </summary>
    void SetupTimelineDuration(TimelineController timelineController);

    /// <summary>
    /// Get the multi-view component (if applicable)
    /// </summary>
    MultiPointCloudView GetMultiPointCloudView();

    /// <summary>
    /// Step forward one frame (arrow key navigation)
    /// </summary>
    void StepFrameForward();

    /// <summary>
    /// Step backward one frame (arrow key navigation)
    /// </summary>
    void StepFrameBackward();

    /// <summary>
    /// Cleanup resources
    /// </summary>
    void Dispose();
}
