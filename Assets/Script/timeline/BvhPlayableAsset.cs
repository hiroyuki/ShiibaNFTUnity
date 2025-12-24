using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Playable asset for BVH motion capture data in Unity Timeline
/// </summary>
[System.Serializable]
public class BvhPlayableAsset : PlayableAsset, ITimelineClipAsset
{

    [Header("Target")]
    [Tooltip("Name of the GameObject to find in scene (leave empty to search for 'BVH_Character')")]
    public string targetGameObjectName = "BVH_Character";

    [Header("Override Transform Adjustments")]
    [Tooltip("Leave empty to use settings from DatasetConfig. Only fill if you want to override.")]
    [SerializeField] private bool overrideTransformSettings = false;


    //dataset config からの値をオーバーライドする
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;
    [SerializeField] private Vector3 scale = Vector3.one;

    // Cached BVH data
    private BvhData cachedBvhData;

    // キャッシュ：最後に作成した Playable の Behaviour（キーフレーム記録用）
    private BvhPlayableBehaviour cachedBehaviour;

    /// <summary>
    /// Get the actual BVH file path from DatasetConfig
    /// </summary>
    private string GetBvhFilePath()
    {
        DatasetConfig config = DatasetConfig.GetInstance();
        if (config != null)
        {
            return config.GetBvhFilePath();
        }

        Debug.LogWarning("BvhPlayableAsset: No DatasetConfig found in scene!");
        return "";
    }

    /// <summary>
    /// Get transform settings from DatasetConfig or override values
    /// </summary>
    private void GetTransformSettings(out Vector3 position, out Vector3 rotation, out Vector3 scaleVal)
    {
        DatasetConfig config = DatasetConfig.GetInstance();

        if (config != null && !overrideTransformSettings)
        {
            position = config.BvhPositionOffset;
            rotation = config.BvhRotationOffset;
            scaleVal = config.BvhScale;
        }
        else
        {
            position = positionOffset;
            rotation = rotationOffset;
            scaleVal = scale;
        }
    }

    public ClipCaps clipCaps => ClipCaps.Looping | ClipCaps.Extrapolation | ClipCaps.ClipIn;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
    {
        var playable = ScriptPlayable<BvhPlayableBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();

        // キャッシュに保存（TimelineController からアクセス用）
        cachedBehaviour = behaviour;

        // Auto-find target transform by name in scene
        string BVH_Character = string.IsNullOrEmpty(targetGameObjectName) ? "BVH_Character" : targetGameObjectName;
        GameObject targetGO = GameObject.Find(BVH_Character);

        if (targetGO != null)
        {
            behaviour.BvhCharacterTransform = targetGO.transform;
            Debug.Log($"BvhPlayableAsset: Found target GameObject '{BVH_Character}'");
        }
        else
        {
            Debug.LogWarning($"BvhPlayableAsset: Target GameObject '{BVH_Character}' not found in scene!");
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

        // Check if BVH data is available
        if (behaviour.bvhData == null)
        {
            Debug.LogWarning($"BvhPlayableAsset: BVH data not loaded. Path: {bvhFilePath}. Returning null playable.");
            return Playable.Null;
        }

        // Get transform settings from DatasetConfig or overrides
        GetTransformSettings(out Vector3 position, out Vector3 rotation, out Vector3 scaleVal);

        // Set frame rate
        behaviour.frameRate = behaviour.bvhData.FrameRate;


        // Apply transform adjustments
        behaviour.PositionOffset = position;
        behaviour.RotationOffset = rotation;
        behaviour.scale = scaleVal;

        // Set drift correction data from DatasetConfig
        var config = DatasetConfig.GetInstance();
        if (config != null && config.BvhPlaybackCorrectionKeyframes != null)
        {
            behaviour.driftCorrectionData = config.BvhPlaybackCorrectionKeyframes;
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

    /// <summary>
    /// ドリフト補正データを取得（TimelineController用）
    /// </summary>
    public BvhPlaybackCorrectionKeyframes GetDriftCorrectionData()
    {
        if (cachedBehaviour != null)
        {
            return cachedBehaviour.driftCorrectionData;
        }

        // キャッシュがない場合、DatasetConfig から取得
        var config = DatasetConfig.GetInstance();
        if (config != null)
        {
            return config.BvhPlaybackCorrectionKeyframes;
        }

        return null;
    }

    /// <summary>
    /// BvhPlayableBehaviour を取得（TimelineController用）
    /// </summary>
    public BvhPlayableBehaviour GetBvhPlayableBehaviour()
    {
        return cachedBehaviour;
    }

    /// <summary>
    /// BVH_Character の現在の localPosition を取得（キーフレーム記録用）
    /// </summary>
    public Vector3 GetBvhCharacterPosition()
    {
        if (cachedBehaviour != null && cachedBehaviour.BvhCharacterTransform != null)
        {
            return cachedBehaviour.BvhCharacterTransform.localPosition;
        }

        // キャッシュがない場合、シーンから探す
        string searchName = string.IsNullOrEmpty(targetGameObjectName) ? "BVH_Character" : targetGameObjectName;
        GameObject targetGO = GameObject.Find(searchName);
        if (targetGO != null)
        {
            return targetGO.transform.localPosition;
        }

        return Vector3.zero;
    }

    /// <summary>
    /// BVH_Character の現在の localEulerAngles を取得（キーフレーム記録用）
    /// </summary>
    public Vector3 GetBvhCharacterRotation()
    {
        if (cachedBehaviour != null && cachedBehaviour.BvhCharacterTransform != null)
        {
            return cachedBehaviour.BvhCharacterTransform.localEulerAngles;
        }

        // キャッシュがない場合、シーンから探す
        string searchName = string.IsNullOrEmpty(targetGameObjectName) ? "BVH_Character" : targetGameObjectName;
        GameObject targetGO = GameObject.Find(searchName);
        if (targetGO != null)
        {
            return targetGO.transform.localEulerAngles;
        }

        return Vector3.zero;
    }

#if UNITY_EDITOR
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
