using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Playable asset for BVH motion capture data in Unity Timeline
/// </summary>
[System.Serializable]
public class BvhPlayableAsset : PlayableAsset, ITimelineClipAsset
{
    [Header("Dataset Configuration")]
    [Tooltip("Reference to dataset configuration (contains BVH file path and settings)")]
    public DatasetConfig datasetConfig;

    [Header("Target")]
    [Tooltip("Name of the GameObject to find in scene (leave empty to search for 'BVH_Character')")]
    public string targetGameObjectName = "BVH_Character";

    [Header("Override Transform Adjustments")]
    [Tooltip("Leave empty to use settings from DatasetConfig. Only fill if you want to override.")]
    [SerializeField] private bool overrideTransformSettings = false;

    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;
    [SerializeField] private Vector3 scale = Vector3.one;
    [SerializeField] private bool applyRootMotion = true;
    [SerializeField] private float overrideFrameRate = 0f;
    [SerializeField] private int frameOffset = 0;

    // Cached BVH data
    private BvhData cachedBvhData;

    /// <summary>
    /// Get the actual BVH file path from DatasetConfig
    /// If not assigned, try to get it from MultiCameraPointCloudManager
    /// </summary>
    private string GetBvhFilePath()
    {
        if (datasetConfig != null)
        {
            return datasetConfig.GetBvhFilePath();
        }

        // Try to get DatasetConfig from MultiCameraPointCloudManager
        var pointCloudManager = GameObject.FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (pointCloudManager != null)
        {
            DatasetConfig config = pointCloudManager.GetDatasetConfig();
            if (config != null)
            {
                return config.GetBvhFilePath();
            }
        }

        Debug.LogWarning("BvhPlayableAsset: No DatasetConfig assigned and none found in scene!");
        return "";
    }

    /// <summary>
    /// Get transform settings, preferring DatasetConfig if available
    /// </summary>
    private void GetTransformSettings(out Vector3 position, out Vector3 rotation, out Vector3 scaleVal, out bool applyRoot, out float frameRate, out int frameOff)
    {
        if (datasetConfig != null && !overrideTransformSettings)
        {
            position = datasetConfig.BvhPositionOffset;
            rotation = datasetConfig.BvhRotationOffset;
            scaleVal = datasetConfig.BvhScale;
            applyRoot = datasetConfig.BvhApplyRootMotion;
            frameRate = datasetConfig.BvhOverrideFrameRate;
            frameOff = datasetConfig.BvhFrameOffset;
        }
        else
        {
            position = positionOffset;
            rotation = rotationOffset;
            scaleVal = scale;
            applyRoot = applyRootMotion;
            frameRate = overrideFrameRate;
            frameOff = frameOffset;
        }
    }

    public ClipCaps clipCaps => ClipCaps.Looping | ClipCaps.Extrapolation | ClipCaps.ClipIn;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
    {
        var playable = ScriptPlayable<BvhPlayableBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();

        // Auto-find target transform by name in scene
        string searchName = string.IsNullOrEmpty(targetGameObjectName) ? "BVH_Character" : targetGameObjectName;
        GameObject targetGO = GameObject.Find(searchName);

        if (targetGO != null)
        {
            behaviour.targetTransform = targetGO.transform;
            Debug.Log($"BvhPlayableAsset: Found target GameObject '{searchName}'");
        }
        else
        {
            Debug.LogWarning($"BvhPlayableAsset: Target GameObject '{searchName}' not found in scene!");
        }

        // Load BVH data
        string bvhFilePath = GetBvhFilePath();
        if (!string.IsNullOrEmpty(bvhFilePath))
        {
            behaviour.bvhData = LoadBvhData(bvhFilePath);
        }
        else
        {
            behaviour.bvhData = cachedBvhData;
        }

        // Get transform settings from DatasetConfig or overrides
        GetTransformSettings(out Vector3 position, out Vector3 rotation, out Vector3 scaleVal,
                           out bool applyRoot, out float frameRate, out int frameOff);

        // Set frame rate
        if (behaviour.bvhData != null)
        {
            behaviour.frameRate = frameRate > 0 ? frameRate : behaviour.bvhData.FrameRate;
        }
        else
        {
            behaviour.frameRate = frameRate > 0 ? frameRate : 30f;
        }

        // Apply transform adjustments
        behaviour.positionOffset = position;
        behaviour.rotationOffset = rotation;
        behaviour.scale = scaleVal;
        behaviour.applyRootMotion = applyRoot;
        behaviour.frameOffset = frameOff;

        if (behaviour.bvhData == null)
        {
            Debug.LogWarning($"BvhPlayableAsset: BVH data not loaded. Path: {bvhFilePath}");
        }

        return playable;
    }

    /// <summary>
    /// Load or get cached BVH data
    /// </summary>
    private BvhData LoadBvhData(string filePath)
    {
        if (cachedBvhData != null)
        {
            return cachedBvhData;
        }

        if (string.IsNullOrEmpty(filePath))
        {
            Debug.LogWarning("BvhPlayableAsset: No BVH file path specified");
            return null;
        }

        cachedBvhData = BvhImporter.ImportFromBVH(filePath);

        if (cachedBvhData != null)
        {
            Debug.Log($"BvhPlayableAsset: Loaded BVH file:\n{cachedBvhData.GetSummary()}");
        }

        return cachedBvhData;
    }

    /// <summary>
    /// Reload BVH data from file
    /// </summary>
    public void ReloadBvhData()
    {
        cachedBvhData = null;
        string filePath = GetBvhFilePath();
        if (!string.IsNullOrEmpty(filePath))
        {
            cachedBvhData = LoadBvhData(filePath);
        }
    }

    /// <summary>
    /// Get the cached BVH data
    /// </summary>
    public BvhData GetBvhData()
    {
        if (cachedBvhData == null)
        {
            string filePath = GetBvhFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                cachedBvhData = LoadBvhData(filePath);
            }
        }
        return cachedBvhData;
    }

    /// <summary>
    /// Get the expected duration based on BVH data
    /// </summary>
    public double GetBvhDuration()
    {
        var data = GetBvhData();
        return data != null ? data.Duration : 0;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only: Update clip duration to match BVH file duration
    /// </summary>
    [ContextMenu("Update Clip Duration from BVH")]
    public void UpdateClipDurationFromBvh()
    {
        var data = GetBvhData();
        if (data != null)
        {
            Debug.Log($"BVH Duration: {data.Duration}s ({data.FrameCount} frames at {data.FrameRate} fps)");
        }
        else
        {
            Debug.LogWarning("Failed to load BVH data");
        }
    }

    /// <summary>
    /// Validate settings in editor
    /// </summary>
    void OnValidate()
    {
        // Ensure scale is not zero
        if (scale.x == 0) scale.x = 1;
        if (scale.y == 0) scale.y = 1;
        if (scale.z == 0) scale.z = 1;
    }
#endif
}
