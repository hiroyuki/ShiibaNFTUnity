using UnityEngine;
using System;
using System.Linq;

public class GPUBinaryPointCloudProcessor : BasePointCloudProcessor
{
    public override ProcessingType ProcessingType => ProcessingType.GPU_Binary;

    // GPU-specific resources
    private ComputeShader binaryDepthProcessor;
    private ComputeBuffer depthDataBuffer;
    private ComputeBuffer lutBuffer;
    private ComputeBuffer outputBuffer;
    private ComputeBuffer validCountBuffer;

    // Cached GPU resources
    private Texture2D colorTexture;
    private Vector2[] cachedLutData;
    private bool lutCacheInitialized = false;
    private int currentDepthDataSize = -1;

    private struct VertexData
    {
        public Vector3 vertex;
        public Vector4 color;
        public int isValid;
    }

    public GPUBinaryPointCloudProcessor(string deviceName) : base(deviceName)
    {
        binaryDepthProcessor = Resources.Load<ComputeShader>("RawDepthToPointCloud");
    }

    public override bool IsSupported()
    {
        return binaryDepthProcessor != null && SystemInfo.supportsComputeShaders;
    }

    public override void Setup(SensorDevice device, float depthBias)
    {
        if (!IsSupported())
        {
            throw new System.NotSupportedException("GPU Binary processing is not supported on this system");
        }

        // Call base implementation for common setup
        base.Setup(device, depthBias);
        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Loading, ProcessingType, "Binary processor setup complete"));
    }


    public override void UpdateMesh(Mesh mesh, SensorDevice device)
    {
        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Processing raw binary data..."));

        var depthRecordBytes = device.GetLatestDepthData();
        var colorTexture = device.GetLatestColorTexture();
        int metadataSize = device.GetMetaDataSize();

        if (depthRecordBytes != null && colorTexture != null)
        {
            UpdateMeshFromRawBinary(mesh, depthRecordBytes, colorTexture, metadataSize, device);
        }

        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Complete, ProcessingType, "Binary processing complete"));
    }

    private void UpdateMeshFromRawBinary(Mesh mesh, byte[] depthRecordBytes, Texture2D colorTexture, int metadataSize, SensorDevice device)
    {
        if (!IsSupported())
        {
            SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Error, ProcessingType, "Fallback: No binary compute shader"));
            return;
        }

        try
        {
            SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Dispatching compute shader..."));
            ProcessRawBinaryData(mesh, depthRecordBytes, colorTexture, metadataSize);
            SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Complete, ProcessingType, "GPU processing complete"));
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"GPU Binary processing failed: {ex.Message}");
            SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Error, ProcessingType, "GPU processing failed"));
        }
    }

    private void ProcessRawBinaryData(Mesh mesh, byte[] depthRecordBytes, Texture2D colorTexture, int metadataSize)
    {
        if (binaryDepthProcessor == null)
        {
            // SetupStatusUI.UpdateDeviceStatus(deviceName, "[CPU] Fallback: No binary compute shader");
            // Could fallback to original CPU processing here
            return;
        }

        // SetupStatusUI.UpdateDeviceStatus(deviceName, "[GPU] Processing raw binary data...");

        // Convert raw bytes to structured data for Metal compatibility
        uint[] depthAsUints = ConvertRawBytesToUints(depthRecordBytes, metadataSize);

        // Initialize GPU resources
        InitializeGPUResources(depthAsUints.Length);

        // Upload structured depth data (Metal-friendly)
        depthDataBuffer.SetData(depthAsUints);

        // Cache color texture (upload once per frame)
        this.colorTexture = colorTexture;

        // Cache LUT data (only convert once as it never changes)
        if (!lutCacheInitialized)
        {
            CacheLUTData();
        }
        lutBuffer.SetData(cachedLutData);

        // Reset valid count
        validCountBuffer.SetData(new int[] { 0 });

        // Set compute shader parameters
        SetComputeShaderParameters(metadataSize);

        // Set buffers and textures
        int kernelIndex = binaryDepthProcessor.FindKernel("ProcessRawDepthData");
        binaryDepthProcessor.SetBuffer(kernelIndex, "rawDepthData", depthDataBuffer);
        binaryDepthProcessor.SetTexture(kernelIndex, "colorTexture", colorTexture);
        binaryDepthProcessor.SetBuffer(kernelIndex, "depthUndistortLUT", lutBuffer);
        binaryDepthProcessor.SetBuffer(kernelIndex, "outputVertices", outputBuffer);
        binaryDepthProcessor.SetBuffer(kernelIndex, "validCount", validCountBuffer);

        // Dispatch compute shader
        int totalPixels = depthWidth * depthHeight;
        int threadGroups = Mathf.CeilToInt(totalPixels / 64f);
        binaryDepthProcessor.Dispatch(kernelIndex, threadGroups, 1, 1);

        // Read results and apply to mesh
        ApplyResultsToMesh(mesh, totalPixels);
    }

    private uint[] ConvertRawBytesToUints(byte[] rawData, int metadataSize)
    {
        // Skip metadata and convert depth bytes to uint array for structured buffer
        int depthDataStart = metadataSize;
        int depthByteCount = rawData.Length - metadataSize;
        int depthPixelCount = depthByteCount / 2; // 2 bytes per ushort
        
        uint[] depthAsUints = new uint[depthPixelCount];
        
        for (int i = 0; i < depthPixelCount; i++)
        {
            int byteIndex = depthDataStart + i * 2;
            if (byteIndex + 1 < rawData.Length)
            {
                // Read ushort as little-endian and store as uint
                ushort depthValue = (ushort)(rawData[byteIndex] | (rawData[byteIndex + 1] << 8));
                depthAsUints[i] = depthValue;
            }
        }
        
        return depthAsUints;
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
        lutCacheInitialized = true;
        // SetupStatusUI.UpdateDeviceStatus(deviceName, "[GPU] LUT cache initialized");
    }
    private void SetComputeShaderParameters(int metadataSize)
    {
        // Transform parameters
        Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotation);
        binaryDepthProcessor.SetMatrix("rotationMatrix", rotMatrix);
        binaryDepthProcessor.SetVector("translation", translation);
        binaryDepthProcessor.SetFloat("depthScaleFactor", depthScaleFactor);
        binaryDepthProcessor.SetFloat("depthBias", depthBias);
        
        // Binary format parameters
        binaryDepthProcessor.SetInt("metadataSize", metadataSize);
        binaryDepthProcessor.SetInt("depthDataOffset", metadataSize); // Depth data starts after metadata
        
        // Camera intrinsics
        binaryDepthProcessor.SetFloat("fx_d", depthIntrinsics[0]);
        binaryDepthProcessor.SetFloat("fy_d", depthIntrinsics[1]);
        binaryDepthProcessor.SetFloat("cx_d", depthIntrinsics[2]);
        binaryDepthProcessor.SetFloat("cy_d", depthIntrinsics[3]);
        binaryDepthProcessor.SetFloat("fx_c", colorIntrinsics[0]);
        binaryDepthProcessor.SetFloat("fy_c", colorIntrinsics[1]);
        binaryDepthProcessor.SetFloat("cx_c", colorIntrinsics[2]);
        binaryDepthProcessor.SetFloat("cy_c", colorIntrinsics[3]);
        
        // Color distortion parameters
        if (colorDistortion != null && colorDistortion.Length >= 8)
        {
            binaryDepthProcessor.SetVector("colorDistortion", 
                new Vector4(colorDistortion[0], colorDistortion[1], colorDistortion[6], colorDistortion[7])); // k1, k2, p1, p2
            binaryDepthProcessor.SetVector("colorDistortion2", 
                new Vector4(colorDistortion[2], colorDistortion[3], colorDistortion[4], colorDistortion[5])); // k3, k4, k5, k6
        }
        
        // Image dimensions
        binaryDepthProcessor.SetInt("depthWidth", depthWidth);
        binaryDepthProcessor.SetInt("depthHeight", depthHeight);
        binaryDepthProcessor.SetInt("colorWidth", colorWidth);
        binaryDepthProcessor.SetInt("colorHeight", colorHeight);
        
        // Processing options
        binaryDepthProcessor.SetBool("useOpenCVLUT", true);
        binaryDepthProcessor.SetBool("showAllPoints", DepthMeshGenerator.showAllPoints);
        binaryDepthProcessor.SetBool("hasBoundingVolume", boundingVolume != null);
        
        // Transform matrices for bounding volume
        if (boundingVolume != null)
        {
            binaryDepthProcessor.SetMatrix("boundingVolumeInverseTransform", boundingVolume.worldToLocalMatrix);
        }
        
        if (depthViewerTransform != null)
        {
            binaryDepthProcessor.SetMatrix("depthViewerTransform", depthViewerTransform.localToWorldMatrix);
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