using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Utility class for exporting debug images (depth and color) from sensor data.
/// Useful for debugging sensor data issues and verifying data integrity.
/// </summary>
public static class DebugImageExporter
{
    /// <summary>
    /// Export depth data as a grayscale PNG image.
    /// </summary>
    /// <param name="depthValues">Depth values (ushort array)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="outputPath">Output file path (PNG)</param>
    /// <param name="maxDepth">Maximum depth value for normalization (default: 5000mm)</param>
    public static void ExportDepthImage(ushort[] depthValues, int width, int height, string outputPath, ushort maxDepth = 5000)
    {
        if (depthValues == null || depthValues.Length != width * height)
        {
            Debug.LogError($"Invalid depth data: expected {width * height} values, got {depthValues?.Length ?? 0}");
            return;
        }

        Texture2D depthTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * height];

        for (int i = 0; i < depthValues.Length; i++)
        {
            // Normalize depth to 0-1 range (closer = brighter)
            float normalized = 1.0f - Mathf.Clamp01((float)depthValues[i] / maxDepth);
            pixels[i] = new Color(normalized, normalized, normalized);
        }

        depthTexture.SetPixels(pixels);
        depthTexture.Apply();

        byte[] bytes = depthTexture.EncodeToPNG();
        File.WriteAllBytes(outputPath, bytes);

        UnityEngine.Object.Destroy(depthTexture);
        Debug.Log($"Depth image exported to: {outputPath}");
    }

    /// <summary>
    /// Export depth data from uint array (GPU format) as a grayscale PNG image.
    /// </summary>
    public static void ExportDepthImage(uint[] depthUints, int width, int height, string outputPath, uint maxDepth = 5000)
    {
        if (depthUints == null || depthUints.Length != width * height)
        {
            Debug.LogError($"Invalid depth data: expected {width * height} values, got {depthUints?.Length ?? 0}");
            return;
        }

        Texture2D depthTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * height];

        for (int i = 0; i < depthUints.Length; i++)
        {
            // Normalize depth to 0-1 range (closer = brighter)
            float normalized = 1.0f - Mathf.Clamp01((float)depthUints[i] / maxDepth);
            pixels[i] = new Color(normalized, normalized, normalized);
        }

        depthTexture.SetPixels(pixels);
        depthTexture.Apply();

        byte[] bytes = depthTexture.EncodeToPNG();
        File.WriteAllBytes(outputPath, bytes);

        UnityEngine.Object.Destroy(depthTexture);
        Debug.Log($"Depth image exported to: {outputPath}");
    }

    /// <summary>
    /// Export color data as PNG image.
    /// </summary>
    /// <param name="colorPixels">Color pixel data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="outputPath">Output file path (PNG)</param>
    public static void ExportColorImage(Color32[] colorPixels, int width, int height, string outputPath)
    {
        if (colorPixels == null || colorPixels.Length != width * height)
        {
            Debug.LogError($"Invalid color data: expected {width * height} values, got {colorPixels?.Length ?? 0}");
            return;
        }

        Texture2D colorTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        colorTexture.SetPixels32(colorPixels);
        colorTexture.Apply();

        byte[] bytes = colorTexture.EncodeToPNG();
        File.WriteAllBytes(outputPath, bytes);

        UnityEngine.Object.Destroy(colorTexture);
        Debug.Log($"Color image exported to: {outputPath}");
    }

    /// <summary>
    /// Export color texture directly as PNG image.
    /// </summary>
    public static void ExportColorTexture(Texture2D colorTexture, string outputPath)
    {
        if (colorTexture == null)
        {
            Debug.LogError("Color texture is null");
            return;
        }

        byte[] bytes = colorTexture.EncodeToPNG();
        File.WriteAllBytes(outputPath, bytes);

        Debug.Log($"Color texture exported to: {outputPath}");
    }

    /// <summary>
    /// Export both depth and color images from a sensor device.
    /// </summary>
    /// <param name="device">Sensor device</param>
    /// <param name="outputDir">Output directory path</param>
    /// <param name="frameIndex">Frame index (for filename)</param>
    public static void ExportSensorImages(SensorDevice device, string outputDir, int frameIndex = 0)
    {
        if (device == null)
        {
            Debug.LogError("Sensor device is null");
            return;
        }

        // Create output directory if it doesn't exist
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string deviceName = device.GetDeviceName();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // Export depth image
        ushort[] depthValues = device.GetLatestDepthValues();
        uint[] depthUints = device.GetLatestDepthData();

        if (depthValues != null)
        {
            string depthPath = Path.Combine(outputDir, $"{deviceName}_depth_frame{frameIndex}_{timestamp}.png");
            ExportDepthImage(depthValues, device.GetDepthWidth(), device.GetDepthHeight(), depthPath);
        }
        else if (depthUints != null)
        {
            string depthPath = Path.Combine(outputDir, $"{deviceName}_depth_frame{frameIndex}_{timestamp}.png");
            ExportDepthImage(depthUints, device.GetDepthWidth(), device.GetDepthHeight(), depthPath);
        }
        else
        {
            Debug.LogWarning($"No depth data available for {deviceName}");
        }

        // Export color image
        Texture2D colorTexture = device.GetLatestColorTexture();
        Color32[] colorPixels = device.GetLatestColorData();

        if (colorTexture != null)
        {
            string colorPath = Path.Combine(outputDir, $"{deviceName}_color_frame{frameIndex}_{timestamp}.png");
            ExportColorTexture(colorTexture, colorPath);
        }
        else if (colorPixels != null)
        {
            string colorPath = Path.Combine(outputDir, $"{deviceName}_color_frame{frameIndex}_{timestamp}.png");
            ExportColorImage(colorPixels, device.GetColorWidth(), device.GetColorHeight(), colorPath);
        }
        else
        {
            Debug.LogWarning($"No color data available for {deviceName}");
        }

        Debug.Log($"Sensor images exported for {deviceName} to {outputDir}");
    }
}
