using UnityEngine;

/// <summary>
/// Abstract base class for processing mode handlers
/// Provides common functionality shared between PLY and Binary modes
/// </summary>
public abstract class BaseProcessingModeHandler : IProcessingModeHandler
{
    // Common fields
    protected string rootDirectory;
    protected string displayName;
    protected Transform parentTransform;
    protected MultiPointCloudView multiPointCloudView;

    // Interface implementation
    public abstract ProcessingType ProcessingType { get; }

    /// <summary>
    /// Initialize the handler - calls template method for mode-specific initialization
    /// </summary>
    public bool Initialize(string rootDirectory, string displayName, Transform parentTransform)
    {
        this.rootDirectory = rootDirectory;
        this.displayName = displayName;
        this.parentTransform = parentTransform;

        return InitializeInternal();
    }

    /// <summary>
    /// Template method for mode-specific initialization
    /// </summary>
    protected abstract bool InitializeInternal();

    /// <summary>
    /// Update called every frame
    /// </summary>
    public abstract void Update();

    /// <summary>
    /// Process first frames if needed
    /// </summary>
    public abstract void ProcessFirstFramesIfNeeded();

    /// <summary>
    /// Seek to a specific frame index
    /// </summary>
    public abstract void SeekToFrame(int frameIndex);

    /// <summary>
    /// Process a specific frame with timestamp
    /// </summary>
    public abstract void ProcessFrame(int frameIndex, ulong targetTimestamp);

    /// <summary>
    /// Get total frame count
    /// </summary>
    public abstract int GetTotalFrameCount();

    /// <summary>
    /// Get FPS from header/metadata
    /// </summary>
    public abstract int GetFps();

    /// <summary>
    /// Get timestamp for a specific frame index with fallback calculation
    /// </summary>
    public virtual ulong GetTargetTimestamp(int frameIndex)
    {
        int fps = GetFps();
        if (fps > 0)
        {
            ulong nanosecondsPerFrame = (ulong)(1_000_000_000L / fps);
            return (ulong)frameIndex * nanosecondsPerFrame;
        }

        Debug.LogError($"Cannot estimate timestamp for frame {frameIndex}");
        return 0;
    }

    /// <summary>
    /// Setup timeline duration - common implementation
    /// </summary>
    public virtual void SetupTimelineDuration(TimelineController timelineController)
    {
        int fps = GetFps();
        int totalFrameCount = GetTotalFrameCount();

        if (fps <= 0 || totalFrameCount <= 0)
        {
            Debug.LogWarning($"Invalid metadata: FPS={fps}, frames={totalFrameCount}");
            return;
        }

        if (timelineController != null)
        {
            timelineController.SetDuration(totalFrameCount, fps);
        }
    }

    /// <summary>
    /// Get the multi-view component
    /// </summary>
    public virtual MultiPointCloudView GetMultiPointCloudView()
    {
        return multiPointCloudView;
    }

    /// <summary>
    /// Step forward one frame (arrow key navigation)
    /// Default implementation - can be overridden
    /// </summary>
    public abstract void StepFrameForward();

    /// <summary>
    /// Step backward one frame (arrow key navigation)
    /// Default implementation - can be overridden
    /// </summary>
    public abstract void StepFrameBackward();

    /// <summary>
    /// Cleanup resources - can be overridden for mode-specific cleanup
    /// </summary>
    public virtual void Dispose()
    {
        if (multiPointCloudView != null)
        {
            Object.DestroyImmediate(multiPointCloudView.gameObject);
            multiPointCloudView = null;
        }
    }

    /// <summary>
    /// Helper method to create a GameObject with MultiPointCloudView component
    /// </summary>
    protected GameObject CreateMultiPointCloudViewObject(string name)
    {
        GameObject viewObj = new GameObject(name);
        viewObj.transform.SetParent(parentTransform, worldPositionStays: false);
        viewObj.transform.localScale = Vector3.one; // Explicitly set to (1,1,1)
        return viewObj;
    }
}
