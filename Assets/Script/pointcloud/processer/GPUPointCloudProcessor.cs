using UnityEngine;
using System;
using System.Linq;

public class GPUPointCloudProcessor : BasePointCloudProcessor
{
    public override ProcessingType ProcessingType => ProcessingType.GPU;

    // GPU-specific resources
    private ComputeShader depthProcessor;
    private ComputeBuffer depthDataBuffer;
    private ComputeBuffer lutBuffer;
    private ComputeBuffer outputBuffer;
    private ComputeBuffer validCountBuffer;

    // Cached GPU resources
    private Texture2D colorTexture;
    private Vector2[] cachedLutData;
    private int currentDepthDataSize = -1;

    private struct VertexData
    {
        public Vector3 vertex;
        public Vector4 color;
        public int isValid;
    }

    public GPUPointCloudProcessor(string deviceName) : base(deviceName)
    {
        depthProcessor = Resources.Load<ComputeShader>("DepthToPointCloud");
    }

    public override bool IsSupported()
    {
        return depthProcessor != null && SystemInfo.supportsComputeShaders;
    }

    public override void Setup(SensorDevice device, float depthBias)
    {
        if (!IsSupported())
        {
            throw new System.NotSupportedException("GPU Binary processing is not supported on this system");
        }

        // Call base implementation for common setup
        base.Setup(device, depthBias);

        CacheLUTData();
        // Setup constant compute shader parameters once
        SetupComputeShaderConstantParameters();
        // SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Loading, ProcessingType, "GPU processor setup complete"));
    }


    public override void UpdateMesh(Mesh mesh, SensorDevice device)
    {
        // SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Processing depth data..."));

        var depthDataUints = device.GetLatestDepthData();
        var colorTexture = device.GetLatestColorTexture();

        if (depthDataUints != null && colorTexture != null)
        {
            UpdateMeshFromDepthData(mesh, depthDataUints, colorTexture, device);
        }

        // SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Complete, ProcessingType, "GPU processing complete"));
    }

    private void UpdateMeshFromDepthData(Mesh mesh, uint[] depthDataUints, Texture2D colorTexture, SensorDevice device)
    {
        if (!IsSupported())
        {
            // SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Error, ProcessingType, "Fallback: No compute shader"));
            return;
        }

        try
        {
            // SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Dispatching compute shader..."));
            ProcessDepthData(mesh, depthDataUints, colorTexture);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"GPU processing failed: {ex.Message}");
            SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Error, ProcessingType, "GPU processing failed"));
        }
    }

    private void ProcessDepthData(Mesh mesh, uint[] depthDataUints, Texture2D colorTexture)
    {
        if (depthProcessor == null)
        {
            return;
        }

        // Initialize GPU resources
        InitializeGPUResources(depthDataUints.Length);

        // Upload depth data directly (no conversion needed!)
        depthDataBuffer.SetData(depthDataUints);

        // Cache color texture (upload once per frame)
        this.colorTexture = colorTexture;

        lutBuffer.SetData(cachedLutData);

        // Reset valid count
        validCountBuffer.SetData(new int[] { 0 });

        // Set dynamic compute shader parameters
        UpdateComputeShaderDynamicParameters();

        // Set buffers and textures
        int kernelIndex = depthProcessor.FindKernel("ProcessRawDepthData");
        depthProcessor.SetBuffer(kernelIndex, "rawDepthData", depthDataBuffer);
        depthProcessor.SetTexture(kernelIndex, "colorTexture", colorTexture);
        depthProcessor.SetBuffer(kernelIndex, "depthUndistortLUT", lutBuffer);
        depthProcessor.SetBuffer(kernelIndex, "outputVertices", outputBuffer);
        depthProcessor.SetBuffer(kernelIndex, "validCount", validCountBuffer);

        // Dispatch compute shader
        int totalPixels = depthWidth * depthHeight;
        int threadGroups = Mathf.CeilToInt(totalPixels / 64f);
        depthProcessor.Dispatch(kernelIndex, threadGroups, 1, 1);

        // Read results and apply to mesh
        ApplyResultsToMesh(mesh, totalPixels);
    }


    private void InitializeGPUResources(int depthPixelCount)
    {
        bool needsResize = currentDepthDataSize != depthPixelCount;
        
        if (needsResize)
        {
            // Dispose old buffers
            if (depthDataBuffer != null) { depthDataBuffer.Dispose(); depthDataBuffer = null; }
            if (outputBuffer != null) { outputBuffer.Dispose(); outputBuffer = null; }
            
            // Create new buffers for structured depth data
            depthDataBuffer = new ComputeBuffer(depthPixelCount, sizeof(uint)); // 4 bytes per uint
            
            int totalPixels = depthWidth * depthHeight;
            outputBuffer = new ComputeBuffer(totalPixels, 32); // VertexData struct size
            
            currentDepthDataSize = depthPixelCount;
            // SetupStatusUI.UpdateDeviceStatus(deviceName, $"[GPU] Buffers created for {depthPixelCount} pixels");
        }
        
        // Create LUT buffer (only once)
        if (lutBuffer == null)
        {
            int totalPixels = depthWidth * depthHeight;
            lutBuffer = new ComputeBuffer(totalPixels, sizeof(float) * 2);
        }
        
        // Create validCountBuffer (only once)
        if (validCountBuffer == null)
        {
            validCountBuffer = new ComputeBuffer(1, sizeof(int));
        }
    }

    private void CacheLUTData()
    {
        int totalPixels = depthWidth * depthHeight;
        cachedLutData = new Vector2[totalPixels];
        
        for (int y = 0; y < depthHeight; y++)
        {
            for (int x = 0; x < depthWidth; x++)
            {
                cachedLutData[y * depthWidth + x] = depthUndistortLUT[x, y];
            }
        }
        // SetupStatusUI.UpdateDeviceStatus(deviceName, "[GPU] LUT cache initialized");
    }
    // Setup constant parameters once during initialization
    private void SetupComputeShaderConstantParameters()
    {
        if (depthProcessor == null) return;


        depthProcessor.SetFloat("depthScaleFactor", depthScaleFactor);
        depthProcessor.SetFloat("depthBias", depthBias);

        // Camera intrinsics (constant)
        depthProcessor.SetFloat("fx_d", depthIntrinsics[0]);
        depthProcessor.SetFloat("fy_d", depthIntrinsics[1]);
        depthProcessor.SetFloat("cx_d", depthIntrinsics[2]);
        depthProcessor.SetFloat("cy_d", depthIntrinsics[3]);
        depthProcessor.SetFloat("fx_c", colorIntrinsics[0]);
        depthProcessor.SetFloat("fy_c", colorIntrinsics[1]);
        depthProcessor.SetFloat("cx_c", colorIntrinsics[2]);
        depthProcessor.SetFloat("cy_c", colorIntrinsics[3]);

        // Color distortion parameters (constant)
        if (colorDistortion != null && colorDistortion.Length >= 8)
        {
            depthProcessor.SetVector("colorDistortion",
                new Vector4(colorDistortion[0], colorDistortion[1], colorDistortion[6], colorDistortion[7])); // k1, k2, p1, p2
            depthProcessor.SetVector("colorDistortion2",
                new Vector4(colorDistortion[2], colorDistortion[3], colorDistortion[4], colorDistortion[5])); // k3, k4, k5, k6
        }

        // Image dimensions (constant)
        depthProcessor.SetInt("depthWidth", depthWidth);
        depthProcessor.SetInt("depthHeight", depthHeight);
        depthProcessor.SetInt("colorWidth", colorWidth);
        depthProcessor.SetInt("colorHeight", colorHeight);

        // Constant processing options
        depthProcessor.SetBool("useOpenCVLUT", true);

    }

    // Update only dynamic parameters per frame (if they actually change)
    private void UpdateComputeShaderDynamicParameters()
    {
        // Transform parameters (constant for this camera)
        Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotation);
        depthProcessor.SetMatrix("rotationMatrix", rotMatrix);
        depthProcessor.SetVector("translation", translation);

        // Static transform matrices (set once if they don't move)
        if (depthViewerTransform != null)
        {
            depthProcessor.SetMatrix("depthViewerTransform", depthViewerTransform.localToWorldMatrix);
        }

        // Runtime settings that can change
        depthProcessor.SetBool("showAllPoints", PointCloudSettings.showAllPoints);
        depthProcessor.SetBool("hasBoundingVolume", boundingVolume != null);

        // Transform matrices that might move at runtime
        if (boundingVolume != null)
        {
            depthProcessor.SetMatrix("boundingVolumeInverseTransform", boundingVolume.worldToLocalMatrix);
        }
    }

        private void ApplyResultsToMesh(Mesh mesh, int totalPixels)
    {
        VertexData[] results = new VertexData[totalPixels];
        outputBuffer.GetData(results);
        
        var validVertices = new System.Collections.Generic.List<Vector3>();
        var validColors = new System.Collections.Generic.List<Color32>();
        var validIndices = new System.Collections.Generic.List<int>();
        
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
        
        // Apply to mesh
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = validVertices.ToArray();
        mesh.colors32 = validColors.ToArray();
        mesh.SetIndices(validIndices.ToArray(), MeshTopology.Points, 0);
        mesh.RecalculateBounds();
        
        // SetupStatusUI.UpdateDeviceStatus(deviceName, $"[GPU] Valid points: {validVertices.Count}");
    }

    public override void Dispose()
    {
        depthDataBuffer?.Dispose();
        lutBuffer?.Dispose();
        outputBuffer?.Dispose();
        validCountBuffer?.Dispose();

        if (colorTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(colorTexture);
        }

        base.Dispose(); // Call base class cleanup
    }
}