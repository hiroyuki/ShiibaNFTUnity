using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

public class GPUPointCloudProcessor : CPUPointCloudProcessor
{
    public override ProcessingType ProcessingType => ProcessingType.GPU;

    // CPU/GPU Hybrid specific resources
    public ComputeShader depthPixelProcessor;
    private ComputeBuffer depthBuffer;
    private ComputeBuffer colorBuffer;
    private ComputeBuffer lutBuffer;
    private ComputeBuffer outputBuffer;
    private ComputeBuffer validCountBuffer;

    // Cached arrays and buffer size tracking for performance
    private Vector4[] cachedColorData;
    private Vector2[] cachedLutData;
    private uint[] cachedDepthDataUint;
    private bool lutCacheInitialized = false;
    private int currentDepthBufferSize = -1;
    private int currentColorBufferSize = -1;

    private struct VertexData
    {
        public Vector3 vertex;
        public Vector4 color;  // Changed from Color32 to Vector4 to match compute shader
        public int isValid;
        // Size: 12 + 16 + 4 = 32 bytes (matches compute shader)
    }

    public GPUPointCloudProcessor(string deviceName) : base(deviceName)
    {
    }

    public override bool IsSupported()
    {
        // CPU processing is always supported
        return true;
    }

    public override void Setup(SensorDevice device, float depthBias)
    {
        // Call base implementation for common setup
        base.Setup(device, depthBias);
        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Loading, ProcessingType, "GPU processor setup complete"));
    }

    protected override void ProcessDepthPixels(ushort[] depthValues, Color32[] colorPixels, List<Vector3> validVertices, List<Color32> validColors, List<int> validIndices)
    {
        int totalPixels = depthValues.Length;
        // SetupStatusUI.UpdateDeviceStatus(deviceName, $"Total depth pixels: {totalPixels}");
        // Initialize compute buffers
        InitializeComputeBuffers(totalPixels);

        // Platform-specific depth data handling (always needed per frame)
        SetDepthBufferData(depthValues);

        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Settting depth buffer data done.");
        // Cache and reuse color data conversion (only if changed)
        if (cachedColorData == null || cachedColorData.Length != latestColorPixels.Length)
        {
            cachedColorData = new Vector4[latestColorPixels.Length];
        }

        // Convert color pixels to Vector4 array
        for (int i = 0; i < latestColorPixels.Length; i++)
        {
            Color32 c = latestColorPixels[i];
            cachedColorData[i] = new Vector4(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        }
        colorBuffer.SetData(cachedColorData);

        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Setting color buffer data done.");
        // Cache LUT data (only convert once as it never changes)
        if (!lutCacheInitialized)
        {
            if (cachedLutData == null || cachedLutData.Length != depthWidth * depthHeight)
            {
                cachedLutData = new Vector2[depthWidth * depthHeight];
            }

            for (int y = 0; y < depthHeight; y++)
            {
                for (int x = 0; x < depthWidth; x++)
                {
                    cachedLutData[y * depthWidth + x] = depthUndistortLUT[x, y];
                }
            }
            lutCacheInitialized = true;
            Debug.Log("LUT cache initialized");
        }
        lutBuffer.SetData(cachedLutData);

        // Reset valid count
        validCountBuffer.SetData(new int[] { 0 });
        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Setting LUT buffer data done.");
        // Set compute shader parameters
        SetComputeShaderParameters(cameraParams);


        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Setting compute shader buffers...");
        // Set buffers
        int kernelIndex = depthPixelProcessor.FindKernel("ProcessDepthPixels");
        depthPixelProcessor.SetBuffer(kernelIndex, "depthValues", depthBuffer);
        depthPixelProcessor.SetBuffer(kernelIndex, "colorPixels", colorBuffer);
        depthPixelProcessor.SetBuffer(kernelIndex, "depthUndistortLUT", lutBuffer);
        depthPixelProcessor.SetBuffer(kernelIndex, "outputVertices", outputBuffer);
        depthPixelProcessor.SetBuffer(kernelIndex, "validCount", validCountBuffer);

        // Dispatch compute shader
        int threadGroups = Mathf.CeilToInt(totalPixels / 64f);

        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Dispatching compute shader...");

        depthPixelProcessor.Dispatch(kernelIndex, threadGroups, 1, 1);

        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Reading back results...");
        // Read results
        VertexData[] results = new VertexData[totalPixels];
        outputBuffer.GetData(results);
        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Read back results done.");

        // Process results
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].isValid == 1)
            {
                validVertices.Add(results[i].vertex);

                // Convert Vector4 color back to Color32
                Vector4 colorVec = results[i].color;
                Color32 color32 = new Color32(
                    (byte)(colorVec.x * 255f),
                    (byte)(colorVec.y * 255f),
                    (byte)(colorVec.z * 255f),
                    (byte)(colorVec.w * 255f)
                );
                validColors.Add(color32);
                validIndices.Add(validVertices.Count - 1);
            }
        }

        // SetupStatusUI.UpdateDeviceStatus(deviceName, "Processing done. Valid points: " + validVertices.Count);
    }
    private void InitializeComputeBuffers(int totalPixels)
    {
        int colorPixelCount = colorWidth * colorHeight;
        
        // Only recreate buffers if size changed
        bool needsDepthBufferResize = currentDepthBufferSize != totalPixels;
        bool needsColorBufferResize = currentColorBufferSize != colorPixelCount;
        
        if (needsDepthBufferResize)
        {
            // Dispose and recreate depth-related buffers
            if (depthBuffer != null) { depthBuffer.Dispose(); depthBuffer = null; }
            if (lutBuffer != null) { lutBuffer.Dispose(); lutBuffer = null; }
            if (outputBuffer != null) { outputBuffer.Dispose(); outputBuffer = null; }
            
            int depthBufferElementSize = IsMetalPlatform() ? sizeof(uint) : sizeof(ushort);
            depthBuffer = new ComputeBuffer(totalPixels, depthBufferElementSize);
            lutBuffer = new ComputeBuffer(totalPixels, sizeof(float) * 2);
            outputBuffer = new ComputeBuffer(totalPixels, 32); // Vector3 + Vector4 + int
            
            currentDepthBufferSize = totalPixels;
            lutCacheInitialized = false; // Need to rebuild LUT cache
            
            // SetupStatusUI.UpdateDeviceStatus(deviceName, $"Depth Buffers recreated for {totalPixels} pixels");
        }
        
        if (needsColorBufferResize)
        {
            // Dispose and recreate color buffer
            if (colorBuffer != null) { colorBuffer.Dispose(); colorBuffer = null; }
            colorBuffer = new ComputeBuffer(colorPixelCount, sizeof(float) * 4);
            
            currentColorBufferSize = colorPixelCount;
            cachedColorData = null; // Need to rebuild color cache
            
            //  SetupStatusUI.UpdateDeviceStatus(deviceName, $"Recreated color buffer for {colorPixelCount} pixels");
        }
        
        // Create validCountBuffer only once (always size 1)
        if (validCountBuffer == null)
        {
            validCountBuffer = new ComputeBuffer(1, sizeof(int));
        }
    }

    private bool IsMetalPlatform()
    {
        #if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
            return SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal;
        #else
            return false;
        #endif
    }
    
    private void SetDepthBufferData(ushort[] depthValues)
    {
        if (IsMetalPlatform())
        {
            // Cache and reuse uint array for Metal compatibility
            if (cachedDepthDataUint == null || cachedDepthDataUint.Length != depthValues.Length)
            {
                cachedDepthDataUint = new uint[depthValues.Length];
            }

            // Convert ushort to uint
            for (int i = 0; i < depthValues.Length; i++)
            {
                cachedDepthDataUint[i] = depthValues[i];
            }
            depthBuffer.SetData(cachedDepthDataUint);
        }
        else
        {
            // Use ushort directly on Windows/DirectX for better memory efficiency
            depthBuffer.SetData(depthValues);
        }
    }
    
    private void SetComputeShaderParameters(CameraParameters cameraParams)
    {
        // Convert rotation quaternion to matrix
        Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotation);
        depthPixelProcessor.SetMatrix("rotationMatrix", rotMatrix);
        depthPixelProcessor.SetVector("translation", translation);
        depthPixelProcessor.SetFloat("depthScaleFactor", depthScaleFactor);
        depthPixelProcessor.SetFloat("depthBias", depthBias);
        
        // Camera parameters
        depthPixelProcessor.SetFloat("fx_d", cameraParams.fx_d);
        depthPixelProcessor.SetFloat("fy_d", cameraParams.fy_d);
        depthPixelProcessor.SetFloat("cx_d", cameraParams.cx_d);
        depthPixelProcessor.SetFloat("cy_d", cameraParams.cy_d);
        depthPixelProcessor.SetFloat("fx_c", cameraParams.fx_c);
        depthPixelProcessor.SetFloat("fy_c", cameraParams.fy_c);
        depthPixelProcessor.SetFloat("cx_c", cameraParams.cx_c);
        depthPixelProcessor.SetFloat("cy_c", cameraParams.cy_c);
        
        // Color distortion parameters
        if (colorDistortion != null && colorDistortion.Length >= 8)
        {
            depthPixelProcessor.SetVector("colorDistortion", new Vector4(colorDistortion[0], colorDistortion[1], colorDistortion[6], colorDistortion[7])); // k1, k2, p1, p2
            depthPixelProcessor.SetVector("colorDistortion2", new Vector4(colorDistortion[2], colorDistortion[3], colorDistortion[4], colorDistortion[5])); // k3, k4, k5, k6
        }
        
        // Image dimensions
        depthPixelProcessor.SetInt("depthWidth", depthWidth);
        depthPixelProcessor.SetInt("depthHeight", depthHeight);
        depthPixelProcessor.SetInt("colorWidth", colorWidth);
        depthPixelProcessor.SetInt("colorHeight", colorHeight);
        
        // Options
        depthPixelProcessor.SetBool("useOpenCVLUT", true);
        depthPixelProcessor.SetBool("showAllPoints", PointCloudSettings.showAllPoints);
        depthPixelProcessor.SetBool("hasBoundingVolume", boundingVolume != null);
        
        // Transforms for bounding volume check
        if (boundingVolume != null)
        {
            depthPixelProcessor.SetMatrix("boundingVolumeTransform", boundingVolume.localToWorldMatrix);
            depthPixelProcessor.SetMatrix("boundingVolumeInverseTransform", boundingVolume.worldToLocalMatrix);
        }
        
        if (depthViewerTransform != null)
        {
            depthPixelProcessor.SetMatrix("depthViewerTransform", depthViewerTransform.localToWorldMatrix);
        }
    }
    
    private void DisposeComputeBuffers()
    {
        if (depthBuffer != null) { depthBuffer.Dispose(); depthBuffer = null; }
        if (colorBuffer != null) { colorBuffer.Dispose(); colorBuffer = null; }
        if (lutBuffer != null) { lutBuffer.Dispose(); lutBuffer = null; }
        if (outputBuffer != null) { outputBuffer.Dispose(); outputBuffer = null; }
        if (validCountBuffer != null) { validCountBuffer.Dispose(); validCountBuffer = null; }
    }

    public override void Dispose()
    {
        DisposeComputeBuffers();
        base.Dispose(); // Call base class cleanup
    }
}