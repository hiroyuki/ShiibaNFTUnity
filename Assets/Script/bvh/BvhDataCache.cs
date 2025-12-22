using UnityEngine;

/// <summary>
/// Centralized manager for loading and caching BVH data.
/// Ensures a single BvhData instance is shared across all components (Timeline, SceneFlow, etc.)
///
/// This solves the problem of having duplicate BvhData in memory and keeps data loading independent from Timeline.
/// </summary>
public static class BvhDataCache
{
    private static BvhData cachedBvhData;
    private static string cachedBvhPath = "";

    /// <summary>
    /// Initialize BvhDataCache with configuration.
    /// Called by MultiCameraPointCloudManager during startup.
    /// </summary>
    public static void InitializeWithConfig(DatasetConfig config)
    {
        if (config == null)
        {
            Debug.LogError("[BvhDataCache] DatasetConfig is null. Cannot initialize.");
            return;
        }

        // Check if BVH is enabled in the config
        if (!config.EnableBvh)
        {
            Debug.Log("[BvhDataCache] BVH is disabled in DatasetConfig. Skipping BVH initialization.");
            return;
        }

        string bvhFilePath = config.GetBvhFilePath();
        if (string.IsNullOrEmpty(bvhFilePath))
        {
            Debug.LogError("[BvhDataCache] BVH file path is empty in DatasetConfig.");
            return;
        }

        // Load BVH data from file
        cachedBvhData = BvhImporter.ImportFromBVH(bvhFilePath);
        cachedBvhPath = bvhFilePath;

        if (cachedBvhData != null)
        {
            Debug.Log($"[BvhDataCache] BVH data initialized from: {bvhFilePath}");
        }
        else
        {
            Debug.LogError($"[BvhDataCache] Failed to load BVH data from: {bvhFilePath}");
        }
    }

    /// <summary>
    /// Get the shared BvhData instance.
    /// Returns cached data that was initialized via InitializeWithConfig().
    /// Safe to call from any component.
    /// </summary>
    public static BvhData GetBvhData()
    {
        return cachedBvhData;
    }

    /// <summary>
    /// Check if BVH data is currently cached
    /// </summary>
    public static bool IsBvhDataCached()
    {
        return cachedBvhData != null;
    }

    /// <summary>
    /// Get the file path of the currently cached BVH data
    /// </summary>
    public static string GetCachedBvhPath()
    {
        return cachedBvhPath;
    }

    /// <summary>
    /// Clear the cached BVH data.
    /// Useful when switching datasets or during scene transitions.
    /// </summary>
    public static void ClearCache()
    {
        cachedBvhData = null;
        cachedBvhPath = "";
        Debug.Log("[BvhDataCache] BVH data cache cleared.");
    }

    /// <summary>
    /// Force reload BVH data from file, clearing the cache first
    /// </summary>
    public static BvhData ReloadBvhData()
    {
        ClearCache();
        return GetBvhData();
    }

    /// <summary>
    /// Get cache status for debugging
    /// </summary>
    public static string GetCacheStatus()
    {
        return $"BvhData cached: {IsBvhDataCached()}\nPath: {GetCachedBvhPath()}";
    }
}
