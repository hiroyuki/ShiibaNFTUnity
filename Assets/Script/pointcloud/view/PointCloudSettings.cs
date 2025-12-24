using UnityEngine;

/// <summary>
/// Global settings for all point cloud processors
/// This replaces the static properties that were previously in DepthMeshGenerator
/// </summary>
public static class PointCloudSettings
{
    /// <summary>
    /// When true, shows all points regardless of bounding volume constraints
    /// When false, only shows points within the bounding volume
    /// This is an important app function for visualization control
    /// </summary>
    public static bool showAllPoints = false;

    /// <summary>
    /// Point size for rendering (affects all processors)
    /// </summary>
    public static float pointSize = 2.0f;

    /// <summary>
    /// Opacity/transparency of point cloud (0.0 = fully transparent, 1.0 = fully opaque)
    /// </summary>
    public static float pointCloudOpacity = 1.0f;

    /// <summary>
    /// Enable/disable bounding volume culling across all processors
    /// </summary>
    public static bool enableBoundingVolumeCulling = true;

    /// <summary>
    /// Global depth scale multiplier that can be applied to all devices
    /// </summary>
    public static float globalDepthScaleMultiplier = 1.0f;

    /// <summary>
    /// Maximum processing distance (points beyond this distance are culled)
    /// 0 = no limit
    /// </summary>
    public static float maxProcessingDistance = 0f;

    /// <summary>
    /// Apply settings to all active point cloud processors
    /// This would be called when settings change at runtime
    /// </summary>
    public static void ApplyToAllProcessors()
    {
        // This could notify all processors of setting changes
        // Implementation would depend on how processors are managed
        Debug.Log($"Applied PointCloud settings: showAllPoints={showAllPoints}, pointSize={pointSize}");
    }

    /// <summary>
    /// Reset all settings to default values
    /// </summary>
    public static void ResetToDefaults()
    {
        showAllPoints = false;
        pointSize = 3.0f;
        pointCloudOpacity = 0.5f;
        enableBoundingVolumeCulling = true;
        globalDepthScaleMultiplier = 1.0f;
        maxProcessingDistance = 0f;
    }
}