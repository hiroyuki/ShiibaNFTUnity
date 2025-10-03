using UnityEngine;

public class GPUPointCloudProcessor : BasePointCloudProcessor
{
    public override ProcessingType ProcessingType => ProcessingType.GPU;

    // GPU-specific resources
    private ComputeShader depthProcessor;
    private ComputeBuffer depthDataBuffer;
    private ComputeBuffer lutBuffer;
    private ComputeBuffer outputBuffer;
    private ComputeBuffer validCountBuffer;
    private ComputeBuffer cameraMetadataBuffer;

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
        // Instantiate a copy so each camera has its own compute shader instance
        ComputeShader original = Resources.Load<ComputeShader>("DepthToPointCloud");
        if (original != null)
        {
            depthProcessor = UnityEngine.Object.Instantiate(original);
        }
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
        // SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Loading, ProcessingType, "GPU processor setup complete"));
    }

    public override CameraMetadata SetupCameraMetadata(SensorDevice device)
    {
        // Get metadata from device
        CameraMetadata metadata = device.CreateCameraMetadata(depthViewerTransform);

        if (depthProcessor == null) return metadata;

        // Create metadata buffer
        if (cameraMetadataBuffer == null)
        {
            cameraMetadataBuffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<CameraMetadata>());
        }

        // Upload metadata to GPU
        cameraMetadataBuffer.SetData(new CameraMetadata[] { metadata });

        // Set buffer to compute shader
        int kernelIndex = depthProcessor.FindKernel("ProcessRawDepthData");
        depthProcessor.SetBuffer(kernelIndex, "cameraMetadata", cameraMetadataBuffer);

        // Initialize global bounding volume parameters
        depthProcessor.SetInt("hasBoundingVolume", boundingVolume != null ? 1 : 0);
        depthProcessor.SetInt("showAllPoints", PointCloudSettings.showAllPoints ? 1 : 0);
        if (boundingVolume != null)
        {
            depthProcessor.SetMatrix("boundingVolumeInverseTransform", boundingVolume.worldToLocalMatrix);
        }

        return metadata;
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
            ProcessDepthData(mesh, depthDataUints, colorTexture, device);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"GPU processing failed: {ex.Message}");
            device.UpdateDeviceStatus(DeviceStatusType.Error, ProcessingType, "GPU processing failed");
        }
    }

    private void ProcessDepthData(Mesh mesh, uint[] depthDataUints, Texture2D colorTexture, SensorDevice device)
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

        // Update dynamic metadata (bounding volume might have moved)
        UpdateCameraMetadata(device);

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
        Debug.Log($"device {DeviceName} valid points: {mesh.vertexCount}");
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

    // Update camera metadata buffer and global bounding volume parameters
    private void UpdateCameraMetadata(SensorDevice device)
    {
        // Get updated metadata from device
        CameraMetadata metadata = device.CreateCameraMetadata(depthViewerTransform);

        // Upload updated metadata to GPU
        cameraMetadataBuffer.SetData(new CameraMetadata[] { metadata });

        // Set global bounding volume parameters (shared across all cameras)
        int kernelIndex = depthProcessor.FindKernel("ProcessRawDepthData");
        depthProcessor.SetInt("hasBoundingVolume", boundingVolume != null ? 1 : 0);
        depthProcessor.SetInt("showAllPoints", PointCloudSettings.showAllPoints ? 1 : 0);
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
        cameraMetadataBuffer?.Dispose();

        if (colorTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(colorTexture);
        }

        // Destroy the instantiated compute shader copy
        if (depthProcessor != null)
        {
            UnityEngine.Object.DestroyImmediate(depthProcessor);
        }

        base.Dispose(); // Call base class cleanup
    }
}