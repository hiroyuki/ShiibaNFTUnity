using System.IO;
using UnityEngine;

public static class DebugImageExporter
{
    private static string exportBaseDir = Path.Combine(Application.persistentDataPath, "DebugImages");

    /// <summary>
    /// Export depth image as grayscale PNG
    /// </summary>
    public static void ExportDepthImage(ushort[] depthValues, int width, int height, float depthScaleFactor, string deviceName, int frameIndex)
    {
        string exportDir = Path.Combine(exportBaseDir, deviceName);
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        Texture2D depthTex = new Texture2D(width, height, TextureFormat.R8, false);
        float maxDepthMeters = 4.0f;
        float scale = 255f / maxDepthMeters;

        for (int i = 0; i < depthValues.Length; i++)
        {
            float meters = depthValues[i] * (depthScaleFactor / 1000f);
            byte intensity = (byte)Mathf.Clamp(meters * scale, 0, 255);
            depthTex.SetPixel(i % width, height - 1 - (i / width), new Color32(intensity, intensity, intensity, 255));
        }
        depthTex.Apply();

        string filename = $"frame_{frameIndex:D3}_depth.png";
        string filepath = Path.Combine(exportDir, filename);
        File.WriteAllBytes(filepath, depthTex.EncodeToPNG());
        Object.DestroyImmediate(depthTex);

        Debug.Log($"Exported depth image: {filepath}");
    }

    /// <summary>
    /// Export color image as RGB PNG
    /// </summary>
    public static void ExportColorImage(Color32[] colorPixels, int width, int height, string deviceName, int frameIndex)
    {
        string exportDir = Path.Combine(exportBaseDir, deviceName);
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        Texture2D colorTex = new Texture2D(width, height, TextureFormat.RGB24, false);
        colorTex.SetPixels32(colorPixels);
        colorTex.Apply();

        string filename = $"frame_{frameIndex:D3}_color.png";
        string filepath = Path.Combine(exportDir, filename);
        File.WriteAllBytes(filepath, colorTex.EncodeToPNG());
        Object.DestroyImmediate(colorTex);

        Debug.Log($"Exported color image: {filepath}");
    }

    /// <summary>
    /// Export point cloud projection image showing how depth projects onto color camera
    /// </summary>
    public static void ExportPointCloudProjection(DepthMeshGenerator generator, ushort[] depthValues, string deviceName, int frameIndex)
    {
        string exportDir = Path.Combine(exportBaseDir, deviceName);
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        Texture2D projectionTex = generator.ProjectDepthToColorImage(depthValues);
        if (projectionTex == null)
        {
            Debug.LogWarning("Failed to create point cloud projection image");
            return;
        }

        string filename = $"frame_{frameIndex:D3}_projection.png";
        string filepath = Path.Combine(exportDir, filename);
        File.WriteAllBytes(filepath, projectionTex.EncodeToPNG());
        Object.DestroyImmediate(projectionTex);

        Debug.Log($"Exported point cloud projection: {filepath}");
    }

    /// <summary>
    /// Export point cloud projection with actual colors from the filtering algorithm
    /// </summary>
    public static void ExportFilteredPointCloudProjection(DepthMeshGenerator generator, ushort[] depthValues, Color32[] colorPixels, string deviceName, int frameIndex)
    {
        string exportDir = Path.Combine(exportBaseDir, deviceName);
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        Texture2D projectionTex = generator.CreateFilteredPointCloudProjection(depthValues, colorPixels);
        if (projectionTex == null)
        {
            Debug.LogWarning("Failed to create filtered point cloud projection image");
            return;
        }

        string filename = $"frame_{frameIndex:D3}_filtered_projection.png";
        string filepath = Path.Combine(exportDir, filename);
        File.WriteAllBytes(filepath, projectionTex.EncodeToPNG());
        Object.DestroyImmediate(projectionTex);

        Debug.Log($"Exported filtered point cloud projection: {filepath}");
    }

    /// <summary>
    /// Export all debug images for a frame
    /// </summary>
    public static void ExportAllDebugImages(ushort[] depthValues, Color32[] colorPixels,
        int depthWidth, int depthHeight, int colorWidth, int colorHeight,
        float depthScaleFactor, DepthMeshGenerator generator, string deviceName, int frameIndex)
    {
        ExportDepthImage(depthValues, depthWidth, depthHeight, depthScaleFactor, deviceName, frameIndex);
        ExportColorImage(colorPixels, colorWidth, colorHeight, deviceName, frameIndex);
        ExportPointCloudProjection(generator, depthValues, deviceName, frameIndex);
        ExportFilteredPointCloudProjection(generator, depthValues, colorPixels, deviceName, frameIndex);
        Debug.Log($"Exported all debug images for frame {frameIndex} in device {deviceName}");
    }

    /// <summary>
    /// Get the export directory path for a specific device
    /// </summary>
    public static string GetExportDirectory(string deviceName = null)
    {
        if (string.IsNullOrEmpty(deviceName))
            return exportBaseDir;
        return Path.Combine(exportBaseDir, deviceName);
    }

    /// <summary>
    /// Clear all exported debug images
    /// </summary>
    public static void ClearDebugImages()
    {
        if (Directory.Exists(exportBaseDir))
        {
            Directory.Delete(exportBaseDir, true);
            Debug.Log("Cleared all debug images");
        }
    }
}