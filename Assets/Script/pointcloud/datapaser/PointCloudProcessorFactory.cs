using UnityEngine;

public static class PointCloudProcessorFactory
{
    /// <summary>
    /// Creates the best available point cloud processor for the given device.
    /// Priority order: GPU Binary -> GPU/CPU Hybrid -> CPU Only
    /// </summary>
    /// <param name="deviceName">The name of the device to create the processor for</param>
    /// <returns>The best available processor implementation</returns>
    public static IPointCloudProcessor CreateBestProcessor(string deviceName)
    {
        // Try GPU Binary processor first (fastest)
        var gpuBinaryProcessor = new GPUBinaryPointCloudProcessor(deviceName);
        if (gpuBinaryProcessor.IsSupported())
        {
            Debug.Log($"{deviceName}: Using GPU Binary Point Cloud Processor (Ultra-fast)");
            return gpuBinaryProcessor;
        }
        else
        {
            gpuBinaryProcessor.Dispose(); // Clean up if not supported
        }

        // Fallback to CPU/GPU Hybrid processor
        var gpuPointCloudProcessor = new GPUPointCloudProcessor(deviceName);

        // Try to load GPU compute shader for hybrid processing
        ComputeShader computeShader = Resources.Load<ComputeShader>("DepthArrayToPointCloud");
        if (computeShader != null)
        {
            gpuPointCloudProcessor.depthPixelProcessor = computeShader;
            Debug.Log($"{deviceName}: Using GPU Point Cloud Processor with GPU acceleration");
            return gpuPointCloudProcessor;
        }
        else
        {
            gpuPointCloudProcessor.Dispose(); // Clean up if not supported
        }


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

        // Check GPU Binary support
        var gpuBinary = new GPUBinaryPointCloudProcessor("test");
        info += $"- GPU Binary: {(gpuBinary.IsSupported() ? "Available" : "Not Supported")}\n";
        gpuBinary.Dispose();

        // Check GPU Compute Shader support
        bool hasGPUSupport = Resources.Load<ComputeShader>("DepthArrayToPointCloud") != null && SystemInfo.supportsComputeShaders;
        info += $"- GPU Hybrid: {(hasGPUSupport ? "Available" : "Not Supported")}\n";

        // CPU is always available
        info += "- CPU: Always Available\n";

        return info;
    }
}