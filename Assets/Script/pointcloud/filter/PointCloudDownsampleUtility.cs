using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static utility class for uniform point cloud downsampling operations.
/// Provides methods for downsampling point clouds based on target vertex count.
/// </summary>
public static class PointCloudDownsampleUtility
{
    /// <summary>
    /// Performs uniform downsampling on a point cloud mesh to reach a target vertex count.
    /// Uses uniform sampling to keep every Nth vertex evenly across the point cloud.
    /// </summary>
    /// <param name="originalVertices">Source vertex positions</param>
    /// <param name="originalColors">Source vertex colors</param>
    /// <param name="originalMotionVectors">Optional motion vectors from UV1 channel</param>
    /// <param name="targetVertexCount">Desired number of vertices in output</param>
    /// <returns>DownsampledMeshData containing the downsampled point cloud</returns>
    public static DownsampledMeshData DownsampleUniform(
        Vector3[] originalVertices,
        Color32[] originalColors,
        Vector3[] originalMotionVectors,
        int targetVertexCount)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Edge case 1: Empty input
        if (originalVertices == null || originalVertices.Length == 0)
        {
            Debug.LogWarning("[PointCloudDownsampleUtility] Input mesh is empty");
            var emptyResult = CreateEmptyResult();
            emptyResult.processingTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
            return emptyResult;
        }

        int originalCount = originalVertices.Length;

        // Edge case 2: Target >= original (no downsampling needed)
        if (targetVertexCount >= originalCount)
        {
            Debug.Log($"[PointCloudDownsampleUtility] Target count ({targetVertexCount}) >= original ({originalCount}), performing passthrough");
            var passthroughResult = CreatePassthroughResult(originalVertices, originalColors, originalMotionVectors);
            passthroughResult.processingTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
            return passthroughResult;
        }

        // Edge case 3: Invalid target
        if (targetVertexCount <= 0)
        {
            Debug.LogError($"[PointCloudDownsampleUtility] Invalid target vertex count: {targetVertexCount}");
            var emptyResult = CreateEmptyResult();
            emptyResult.processingTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
            return emptyResult;
        }

        // Calculate step size for uniform sampling
        float step = (float)originalCount / targetVertexCount;

        // Validate colors array
        bool hasColors = originalColors != null && originalColors.Length == originalCount;
        if (originalColors != null && originalColors.Length != originalCount)
        {
            Debug.LogWarning($"[PointCloudDownsampleUtility] Color count mismatch ({originalColors.Length} vs {originalCount}), using white default");
            hasColors = false;
        }

        // Validate motion vectors array
        bool hasMotionVectors = originalMotionVectors != null && originalMotionVectors.Length == originalCount;
        if (originalMotionVectors != null && originalMotionVectors.Length != originalCount)
        {
            Debug.LogWarning($"[PointCloudDownsampleUtility] Motion vector count mismatch ({originalMotionVectors.Length} vs {originalCount}), skipping UV1");
            hasMotionVectors = false;
        }

        // Pre-allocate output arrays
        Vector3[] downsampledVertices = new Vector3[targetVertexCount];
        Color32[] downsampledColors = new Color32[targetVertexCount];
        Vector3[] downsampledMotionVectors = hasMotionVectors ? new Vector3[targetVertexCount] : null;

        // Default color if colors are missing
        Color32 defaultColor = new Color32(255, 255, 255, 255);

        // Perform uniform sampling
        for (int i = 0; i < targetVertexCount; i++)
        {
            // Calculate source index using uniform sampling
            int sourceIndex = Mathf.FloorToInt(i * step);

            // Clamp to valid range (safety check)
            sourceIndex = Mathf.Clamp(sourceIndex, 0, originalCount - 1);

            // Copy vertex data
            downsampledVertices[i] = originalVertices[sourceIndex];

            // Copy color data or use default
            downsampledColors[i] = hasColors ? originalColors[sourceIndex] : defaultColor;

            // Copy motion vector data if present
            if (hasMotionVectors)
            {
                downsampledMotionVectors[i] = originalMotionVectors[sourceIndex];
            }
        }

        stopwatch.Stop();

        // Create result
        DownsampledMeshData result = new DownsampledMeshData
        {
            vertices = downsampledVertices,
            colors = downsampledColors,
            motionVectors = downsampledMotionVectors,
            keptCount = targetVertexCount,
            discardedCount = originalCount - targetVertexCount,
            processingTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds
        };

        Debug.Log($"[PointCloudDownsampleUtility] Downsampled {originalCount} → {targetVertexCount} vertices " +
                  $"(reduction: {result.ReductionRatio * 100:F1}%) in {result.processingTimeMs:F2}ms");

        return result;
    }

    /// <summary>
    /// Creates an empty DownsampledMeshData result
    /// </summary>
    public static DownsampledMeshData CreateEmptyResult()
    {
        return new DownsampledMeshData
        {
            vertices = new Vector3[0],
            colors = new Color32[0],
            motionVectors = null,
            keptCount = 0,
            discardedCount = 0,
            processingTimeMs = 0f
        };
    }

    /// <summary>
    /// Creates a passthrough result (no downsampling) by copying input arrays
    /// </summary>
    public static DownsampledMeshData CreatePassthroughResult(
        Vector3[] vertices,
        Color32[] colors,
        Vector3[] motionVectors)
    {
        // Default color if colors are missing
        Color32 defaultColor = new Color32(255, 255, 255, 255);
        Color32[] resultColors;

        if (colors != null && colors.Length == vertices.Length)
        {
            resultColors = (Color32[])colors.Clone();
        }
        else
        {
            resultColors = new Color32[vertices.Length];
            for (int i = 0; i < resultColors.Length; i++)
            {
                resultColors[i] = defaultColor;
            }
        }

        Vector3[] resultMotionVectors = null;
        if (motionVectors != null && motionVectors.Length == vertices.Length)
        {
            resultMotionVectors = (Vector3[])motionVectors.Clone();
        }

        return new DownsampledMeshData
        {
            vertices = (Vector3[])vertices.Clone(),
            colors = resultColors,
            motionVectors = resultMotionVectors,
            keptCount = vertices.Length,
            discardedCount = 0,
            processingTimeMs = 0f
        };
    }

    /// <summary>
    /// Calculates the indices of vertices that would be kept with the given target count
    /// </summary>
    public static int[] GetKeptIndices(int originalCount, int targetCount)
    {
        if (originalCount <= 0 || targetCount <= 0)
            return new int[0];

        if (targetCount >= originalCount)
        {
            int[] allIndices = new int[originalCount];
            for (int i = 0; i < originalCount; i++)
                allIndices[i] = i;
            return allIndices;
        }

        float step = (float)originalCount / targetCount;
        int[] keptIndices = new int[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            int sourceIndex = Mathf.FloorToInt(i * step);
            keptIndices[i] = Mathf.Clamp(sourceIndex, 0, originalCount - 1);
        }

        return keptIndices;
    }

    /// <summary>
    /// Calculates the indices of vertices that would be discarded with the given target count
    /// </summary>
    public static int[] GetDiscardedIndices(int originalCount, int targetCount)
    {
        if (originalCount <= 0 || targetCount >= originalCount)
            return new int[0];

        if (targetCount <= 0)
        {
            int[] allIndices = new int[originalCount];
            for (int i = 0; i < originalCount; i++)
                allIndices[i] = i;
            return allIndices;
        }

        int[] keptIndices = GetKeptIndices(originalCount, targetCount);
        HashSet<int> keptSet = new HashSet<int>(keptIndices);

        List<int> discardedList = new List<int>(originalCount - targetCount);
        for (int i = 0; i < originalCount; i++)
        {
            if (!keptSet.Contains(i))
            {
                discardedList.Add(i);
            }
        }

        return discardedList.ToArray();
    }
}
