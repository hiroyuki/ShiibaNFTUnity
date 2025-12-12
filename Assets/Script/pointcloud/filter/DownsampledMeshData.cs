using UnityEngine;

/// <summary>
/// Data container for downsampled point cloud mesh results.
/// Stores vertices, colors, optional motion vectors, and processing statistics.
/// </summary>
public class DownsampledMeshData
{
    /// <summary>
    /// Downsampled vertex positions
    /// </summary>
    public Vector3[] vertices;

    /// <summary>
    /// Downsampled vertex colors
    /// </summary>
    public Color32[] colors;

    /// <summary>
    /// Downsampled motion vectors (UV1 channel data), null if not present
    /// </summary>
    public Vector3[] motionVectors;

    /// <summary>
    /// Number of vertices kept after downsampling
    /// </summary>
    public int keptCount;

    /// <summary>
    /// Number of vertices discarded during downsampling
    /// </summary>
    public int discardedCount;

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public float processingTimeMs;

    /// <summary>
    /// Total original vertex count
    /// </summary>
    public int TotalCount => keptCount + discardedCount;

    /// <summary>
    /// Reduction ratio (0.0 to 1.0, where 0.8 means 80% reduction)
    /// </summary>
    public float ReductionRatio
    {
        get
        {
            int total = TotalCount;
            if (total == 0) return 0f;
            return (float)discardedCount / total;
        }
    }

    /// <summary>
    /// Percentage of vertices kept (0.0 to 1.0)
    /// </summary>
    public float KeptPercentage
    {
        get
        {
            int total = TotalCount;
            if (total == 0) return 0f;
            return (float)keptCount / total;
        }
    }

    /// <summary>
    /// Whether motion vectors are present in this data
    /// </summary>
    public bool HasMotionVectors => motionVectors != null && motionVectors.Length > 0;

    /// <summary>
    /// Whether this data contains any vertices
    /// </summary>
    public bool IsEmpty => vertices == null || vertices.Length == 0;

    /// <summary>
    /// Creates an empty DownsampledMeshData instance
    /// </summary>
    public DownsampledMeshData()
    {
        vertices = new Vector3[0];
        colors = new Color32[0];
        motionVectors = null;
        keptCount = 0;
        discardedCount = 0;
        processingTimeMs = 0f;
    }

    /// <summary>
    /// Creates a DownsampledMeshData instance with specified capacity
    /// </summary>
    public DownsampledMeshData(int capacity)
    {
        vertices = new Vector3[capacity];
        colors = new Color32[capacity];
        motionVectors = null; // Allocated later if needed
        keptCount = 0;
        discardedCount = 0;
        processingTimeMs = 0f;
    }
}
