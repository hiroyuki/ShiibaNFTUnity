using System.IO;
using UnityEngine;

/// <summary>
/// Main manager for multi-camera point cloud visualization
/// Delegates processing to specialized mode handlers (PLY, Binary)
/// Can be configured via DatasetConfig ScriptableObject or legacy inspector settings
/// </summary>
public class MultiCameraPointCloudManager : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private DatasetConfig datasetConfig;
    [SerializeField] private string rootDirectory;
    [SerializeField] private bool usePly = true;
    [SerializeField] private bool enablePlyExport = false;
    [SerializeField] private ProcessingType binaryProcessingType = ProcessingType.ONESHADER;

    private IProcessingModeHandler currentHandler;
    private string displayName = "";
    private DatasetConfig currentDatasetConfig;

    /// <summary>
    /// Set and store DatasetConfig at runtime (called from PointCloudPlayableAsset)
    /// </summary>
    public void SetDatasetConfig(DatasetConfig config)
    {
        currentDatasetConfig = config;
        if (config != null)
        {
            Debug.Log($"DatasetConfig set to: {config.DatasetName}");
            // Update rootDirectory to use the point cloud directory from config
            rootDirectory = config.GetPointCloudRootDirectory();
            usePly = config.UsePly;
            enablePlyExport = config.EnablePlyExport;
            binaryProcessingType = config.BinaryProcessingType;
        }
    }

    /// <summary>
    /// Get the current DatasetConfig
    /// </summary>
    public DatasetConfig GetDatasetConfig()
    {
        return currentDatasetConfig;
    }

    void Start()
    {
        SetupStatusUI.ShowStatus("Starting Multi-Camera Point Cloud Manager...");

        DisableTimelineAutoPlay();
        LoadDatasetInfo();

        // Try to initialize PLY mode first if enabled
        if (usePly && TryInitializeHandler(new PlyModeHandler()))
        {
            Debug.Log("PLY mode initialized successfully");
            SetupTimelineDuration();
            return;
        }

        // Fall back to Binary mode
        if (TryInitializeHandler(new BinaryModeHandler(binaryProcessingType, enablePlyExport)))
        {
            Debug.Log($"Binary mode ({binaryProcessingType}) initialized successfully");
            SetupTimelineDuration();
            return;
        }

        Debug.LogError("Failed to initialize any processing mode!");
        SetupStatusUI.ShowStatus("ERROR: Failed to initialize");
    }

    private bool TryInitializeHandler(IProcessingModeHandler handler)
    {
        if (handler.Initialize(rootDirectory, displayName, transform))
        {
            currentHandler = handler;
            return true;
        }
        return false;
    }

    void Update()
    {
        currentHandler?.Update();
    }

    private void DisableTimelineAutoPlay()
    {
        var playableDirector = FindFirstObjectByType<UnityEngine.Playables.PlayableDirector>();
        if (playableDirector != null && playableDirector.playOnAwake)
        {
            playableDirector.Stop();
        }
        SetupStatusUI.ShowStatus("Timeline auto-play disabled");
    }

    private void LoadDatasetInfo()
    {
        string datasetYamlPath = Path.Combine(rootDirectory, "dataset.yaml");
        if (File.Exists(datasetYamlPath))
        {
            try
            {
                DatasetInfo datasetInfo = YamlLoader.Load<DatasetInfo>(datasetYamlPath);
                displayName = datasetInfo.displayName ?? "";
                Debug.Log($"Loaded dataset display name: {displayName}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to load dataset.yaml: {ex.Message}");
                displayName = Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
        else
        {
            Debug.LogWarning($"dataset.yaml not found");
            displayName = Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    private void SetupTimelineDuration()
    {
        var timelineController = FindFirstObjectByType<TimelineController>();
        if (timelineController != null && currentHandler != null)
        {
            currentHandler.SetupTimelineDuration(timelineController);
        }
    }

    #region Public API - Delegates to current handler

    public void SeekToFrame(int frameIndex)
    {
        currentHandler?.SeekToFrame(frameIndex);
    }

    public void ProcessFrame(int frameIndex, ulong targetTimestamp)
    {
        currentHandler?.ProcessFrame(frameIndex, targetTimestamp);
    }

    public void ResetToFirstFrame()
    {
        SeekToFrame(0);
    }

    public int GetTotalFrameCount()
    {
        return currentHandler?.GetTotalFrameCount() ?? -1;
    }

    public int GetFpsFromHeader()
    {
        return currentHandler?.GetFps() ?? -1;
    }

    public MultiPointCloudView GetMultiPointCloudView()
    {
        return currentHandler?.GetMultiPointCloudView();
    }

    #endregion

    void OnDestroy()
    {
        currentHandler?.Dispose();
        currentHandler = null;
    }
}
