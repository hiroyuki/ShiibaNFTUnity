using System.IO;
using UnityEngine;

/// <summary>
/// Main manager for multi-camera point cloud visualization
/// Delegates processing to specialized mode handlers (PLY, Binary)
/// Must be configured via DatasetConfig ScriptableObject set by PointCloudPlayableAsset
/// </summary>
public class MultiCameraPointCloudManager : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private DatasetConfig datasetConfig;

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

        // Ensure DatasetConfig is set
        // First try runtime config, then fall back to serialized field
        if (GetDatasetConfig() == null && datasetConfig != null)
        {
            SetDatasetConfig(datasetConfig);
            Debug.Log("DatasetConfig: Using serialized field from Inspector");
        }

        if (GetDatasetConfig() == null)
        {
            Debug.LogError("DatasetConfig not set! Cannot initialize.");
            SetupStatusUI.ShowStatus("ERROR: DatasetConfig not configured");
            return;
        }

        DisableTimelineAutoPlay();
        LoadDatasetInfo();

        // Initialize based on configured processing type
        ProcessingType processingType = GetDatasetConfig().ProcessingType;

        if (processingType == ProcessingType.PLY)
        {
            if (TryInitializeHandler(new PlyModeHandler()))
            {
                Debug.Log("PLY mode initialized successfully");
                SetupTimelineDuration();
                return;
            }
            else
            {
                Debug.LogWarning("PLY mode initialization failed. No fallback available for PLY mode.");
                SetupStatusUI.ShowStatus("ERROR: PLY mode initialization failed");
                return;
            }
        }
        else if (processingType == ProcessingType.CPU || processingType == ProcessingType.GPU || processingType == ProcessingType.ONESHADER)
        {
            if (TryInitializeHandler(new BinaryModeHandler(processingType, GetDatasetConfig().EnablePlyExport)))
            {
                Debug.Log($"Binary mode ({processingType}) initialized successfully");
                SetupTimelineDuration();
                return;
            }
        }

        Debug.LogError($"Failed to initialize processing mode: {processingType}");
        SetupStatusUI.ShowStatus("ERROR: Failed to initialize");
    }

    private bool TryInitializeHandler(IProcessingModeHandler handler)
    {
        string rootDirectory = GetDatasetConfig().GetPointCloudRootDirectory();
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
        string rootDirectory = GetDatasetConfig().GetPointCloudRootDirectory();
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
            Debug.LogWarning($"dataset.yaml not found, using folder name as display name");
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
