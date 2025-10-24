using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Playable asset for BVH motion capture data in Unity Timeline
/// </summary>
[System.Serializable]
public class BvhPlayableAsset : PlayableAsset, ITimelineClipAsset
{
    [Header("BVH File")]
    [Tooltip("Path to the BVH file (relative to project or absolute)")]
    public string bvhFilePath;

    [Tooltip("Load BVH file automatically when timeline starts")]
    public bool autoLoadBvh = true;

    [Header("Target")]
    [Tooltip("Name of the GameObject to find in scene (leave empty to search for 'BVH_Character')")]
    public string targetGameObjectName = "BVH_Character";

    [Header("Transform Adjustments")]
    [Tooltip("Position offset applied to root motion")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("Rotation offset applied to root (in degrees)")]
    public Vector3 rotationOffset = Vector3.zero;

    [Tooltip("Scale multiplier for all positions")]
    public Vector3 scale = Vector3.one;

    [Tooltip("Apply BVH root position changes (if false, only rotation is applied)")]
    public bool applyRootMotion = true;

    [Header("Playback")]
    [Tooltip("Frame rate for BVH playback (0 = use BVH file's frame rate)")]
    public float overrideFrameRate = 0f;

    [Tooltip("Frame offset to sync with point cloud (positive = delay BVH, negative = advance BVH)")]
    public int frameOffset = 0;

    // Cached BVH data
    private BvhData cachedBvhData;

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
        if (autoLoadBvh && !string.IsNullOrEmpty(bvhFilePath))
        {
            behaviour.bvhData = LoadBvhData();
        }
        else
        {
            behaviour.bvhData = cachedBvhData;
        }

        // Set frame rate
        if (behaviour.bvhData != null)
        {
            behaviour.frameRate = overrideFrameRate > 0 ? overrideFrameRate : behaviour.bvhData.FrameRate;
        }
        else
        {
            behaviour.frameRate = overrideFrameRate > 0 ? overrideFrameRate : 30f;
        }

        // Apply transform adjustments
        behaviour.positionOffset = positionOffset;
        behaviour.rotationOffset = rotationOffset;
        behaviour.scale = scale;
        behaviour.applyRootMotion = applyRootMotion;
        behaviour.frameOffset = frameOffset;

        if (behaviour.bvhData == null)
        {
            Debug.LogWarning($"BvhPlayableAsset: BVH data not loaded. Path: {bvhFilePath}");
        }

        return playable;
    }

    /// <summary>
    /// Load or get cached BVH data
    /// </summary>
    private BvhData LoadBvhData()
    {
        if (cachedBvhData != null)
        {
            return cachedBvhData;
        }

        if (string.IsNullOrEmpty(bvhFilePath))
        {
            Debug.LogWarning("BvhPlayableAsset: No BVH file path specified");
            return null;
        }

        cachedBvhData = BvhImporter.ImportFromBVH(bvhFilePath);

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
        cachedBvhData = LoadBvhData();
    }

    /// <summary>
    /// Get the cached BVH data
    /// </summary>
    public BvhData GetBvhData()
    {
        if (cachedBvhData == null && !string.IsNullOrEmpty(bvhFilePath))
        {
            cachedBvhData = LoadBvhData();
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
