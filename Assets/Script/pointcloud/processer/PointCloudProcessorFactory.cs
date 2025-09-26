using UnityEngine;

public static class PointCloudProcessorFactory
{
    /// <summary>
    /// Creates the best available point cloud processor for the given device.
    /// Priority order: GPU Binary -> CPU Only
    /// </summary>
    /// <param name="deviceName">The name of the device to create the processor for</param>
    /// <returns>The best available processor implementation</returns>
    public static IPointCloudProcessor CreateBestProcessor(string deviceName)
    {
        // Try GPU processor first (fastest)
        var gpuProcessor = new GPUPointCloudProcessor(deviceName);
        if (gpuProcessor.IsSupported())
        {
            Debug.Log($"{deviceName}: Using GPU Point Cloud Processor");
            return gpuProcessor;
        }
        else
        {
            gpuProcessor.Dispose(); // Clean up if not supported
        }

        // Fallback to CPU processor
        var cpuPointCloudProcessor = new CPUPointCloudProcessor(deviceName);
        Debug.Log($"{deviceName}: Using CPU Point Cloud Processor (fallback)");
        return cpuPointCloudProcessor;
    }

    /// <summary>
    /// Gets information about the available processors on this system
    /// </summary>
    /// <returns>String describing available processors</returns>
    public static string GetAvailableProcessorsInfo()
    {
        var info = "Available Point Cloud Processors:\n";

        // Check GPU support
        var gpu = new GPUPointCloudProcessor("test");
        info += $"- GPU: {(gpu.IsSupported() ? "Available" : "Not Supported")}\n";
        gpu.Dispose();

        // CPU is always available
        info += "- CPU: Always Available\n";

        return info;
    }
}