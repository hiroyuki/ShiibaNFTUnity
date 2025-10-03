using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class MultiCameraGPUProcessor : MonoBehaviour
{
    private ComputeShader multiCamProcessor;
    private List<CameraFrameController> frameControllers = new List<CameraFrameController>();

    // Multi-camera buffers
    private ComputeBuffer cameraMetadataBuffer;
    private ComputeBuffer allDepthDataBuffer;
    private ComputeBuffer allLutDataBuffer;
    private ComputeBuffer allOutputBuffer;
    private ComputeBuffer validCountBuffer;

    // Texture array for all camera colors
    private Texture2DArray colorTextureArray;

    // Output vertex structure (matches compute shader)
    private struct VertexData
    {
        public Vector3 vertex;
        public Vector4 color;
        public int isValid;
        public uint cameraIndex;
    }

    private bool isInitialized = false;
    private int totalPixels = 0;

    // Callback for unified mesh update
    private System.Action<Mesh> onUnifiedMeshUpdated;

    void Start()
    {
        multiCamProcessor = Resources.Load<ComputeShader>("MultiCamDepthToPointCloud");
        if (multiCamProcessor == null)
        {
            Debug.LogError("MultiCamDepthToPointCloud compute shader not found!");
        }
    }

    public bool IsSupported()
    {
        return multiCamProcessor != null && SystemInfo.supportsComputeShaders;
    }


    public void Initialize(List<CameraFrameController> controllers)
    {
        this.frameControllers = controllers;

        if (frameControllers.Count == 0)
        {
            Debug.LogWarning("No frame controllers provided for multi-camera processing");
            return;
        }

        Debug.Log($"Initializing multi-camera GPU processing for {frameControllers.Count} cameras");

        // Calculate total buffer sizes
        CalculateBufferSizes();

        // Create GPU buffers
        CreateGPUBuffers();

        // Setup camera metadata
        SetupCameraMetadata();

        // Create color texture array
        CreateColorTextureArray();

        isInitialized = true;
        Debug.Log("Multi-camera GPU processing initialized successfully");
    }

    public void SetUnifiedMeshCallback(System.Action<Mesh> callback)
    {
        onUnifiedMeshUpdated = callback;
    }

    public void ProcessAllCameras(ulong timestamp)
    {
        ProcessAllCamerasFrame(timestamp);
    }

    private void CalculateBufferSizes()
    {
        totalPixels = 0;
        foreach (var controller in frameControllers)
        {
            // Get depth dimensions from the controller's device
            var device = controller.Device;
            if (device != null)
            {
                totalPixels += device.GetDepthWidth() * device.GetDepthHeight();
            }
        }
        Debug.Log($"Total pixels across all cameras: {totalPixels}");
    }

    private void CreateGPUBuffers()
    {
        // Camera metadata buffer
        cameraMetadataBuffer = new ComputeBuffer(frameControllers.Count, System.Runtime.InteropServices.Marshal.SizeOf<CameraMetadata>());

        // Combined depth data buffer
        allDepthDataBuffer = new ComputeBuffer(totalPixels, sizeof(uint));

        // Combined LUT data buffer
        allLutDataBuffer = new ComputeBuffer(totalPixels, sizeof(float) * 2);//buffrer for Vector2

        // Combined output buffer
        allOutputBuffer = new ComputeBuffer(totalPixels, System.Runtime.InteropServices.Marshal.SizeOf<VertexData>());

        // Valid count per camera
        validCountBuffer = new ComputeBuffer(frameControllers.Count, sizeof(int));

        Debug.Log("GPU buffers created successfully");
    }

    private void SetupCameraMetadata()
    {
        CameraMetadata[] metadataArray = new CameraMetadata[frameControllers.Count];
        uint currentDepthOffset = 0;
        uint currentLutOffset = 0;
        uint currentOutputOffset = 0;

        for (int i = 0; i < frameControllers.Count; i++)
        {
            var controller = frameControllers[i];
            var device = controller.Device;

            if (device == null) continue;

            CameraMetadata metadata = new CameraMetadata();

            // Get camera parameters directly from centralized SensorDevice
            // Image dimensions
            metadata.depthWidth = (uint)device.GetDepthWidth();
            metadata.depthHeight = (uint)device.GetDepthHeight();
            metadata.colorWidth = (uint)device.GetColorWidth();
            metadata.colorHeight = (uint)device.GetColorHeight();

            // depthj intrinsics
            metadata.fx_d = device.GetDepthIntrinsics()[0]; // fx
            metadata.fy_d = device.GetDepthIntrinsics()[1]; // fy
            metadata.cx_d = device.GetDepthIntrinsics()[2]; // cx
            metadata.cy_d = device.GetDepthIntrinsics()[3]; // cy

            // Color intrinsics
            metadata.fx_c = device.GetColorIntrinsics()[0]; // fx
            metadata.fy_c = device.GetColorIntrinsics()[1]; // fy
            metadata.cx_c = device.GetColorIntrinsics()[2]; // cx
            metadata.cy_c = device.GetColorIntrinsics()[3]; // cy

            // Depth distortion
            metadata.k1_d = device.GetDepthDistortion()[0]; // k1
            metadata.k2_d = device.GetDepthDistortion()[1]; // k2
            metadata.k3_d = device.GetDepthDistortion()[2]; // k3
            metadata.k4_d = device.GetDepthDistortion()[3]; // k4
            metadata.k5_d = device.GetDepthDistortion()[4]; // k5
            metadata.k6_d = device.GetDepthDistortion()[5]; // k6
            metadata.p1_d = device.GetDepthDistortion()[6]; // p1
            metadata.p2_d = device.GetDepthDistortion()[7]; // p2

            // Color distortion
            metadata.k1_c = device.GetColorDistortion()[0]; // k1
            metadata.k2_c = device.GetColorDistortion()[1]; // k2
            metadata.k3_c = device.GetColorDistortion()[2]; // k3
            metadata.k4_c = device.GetColorDistortion()[3]; // k4
            metadata.k5_c = device.GetColorDistortion()[4]; // k1
            metadata.k6_c = device.GetColorDistortion()[5]; // k1
            metadata.p1_c = device.GetColorDistortion()[6]; // k1
            metadata.p2_c = device.GetColorDistortion()[7]; // k1


            // Processing parameters
            metadata.depthScaleFactor = device.GetDepthScaleFactor();
            metadata.depthBias = device.GetDepthBias();
            metadata.useOpenCVLUT = 1; // Assuming OpenCV LUT is used

            // Transform matrices from centralized SensorDevice
            metadata.d2cRotation = Matrix4x4.Rotate(device.GetDepthToColorRotation());
            metadata.d2cTranslation = device.GetDepthToColorTranslation();

            // Get global transform from device
            if (device.TryGetGlobalTransform(out Vector3 pos, out Quaternion rot))
            {
                metadata.depthViewerTransform = Matrix4x4.TRS(pos, rot, Vector3.one);
            }
            else
            {
                metadata.depthViewerTransform = Matrix4x4.identity;
            }

            // Buffer offsets
            metadata.depthDataOffset = currentDepthOffset;
            metadata.lutDataOffset = currentLutOffset;
            metadata.outputOffset = currentOutputOffset;

            uint pixelCount = metadata.depthWidth * metadata.depthHeight;
            currentDepthOffset += pixelCount;
            currentLutOffset += pixelCount;
            currentOutputOffset += pixelCount;

            metadataArray[i] = metadata;
        }

        cameraMetadataBuffer.SetData(metadataArray);
        Debug.Log("Camera metadata setup complete");
    }

    private void CreateColorTextureArray()
    {
        if (frameControllers.Count == 0) return;

        // Get texture dimensions from first camera
        var firstDevice = frameControllers[0].Device;
        if (firstDevice == null) return;

        var colorHeader = firstDevice.GetColorParser()?.sensorHeader;
        if (colorHeader == null) return;

        int width = colorHeader.custom.camera_sensor.width;
        int height = colorHeader.custom.camera_sensor.height;

        // Create texture array
        colorTextureArray = new Texture2DArray(width, height, frameControllers.Count, TextureFormat.RGB24, false);
        colorTextureArray.wrapMode = TextureWrapMode.Clamp;
        colorTextureArray.filterMode = FilterMode.Bilinear;

        Debug.Log($"Created color texture array: {width}x{height}x{frameControllers.Count}");
    }

    public void ProcessAllCamerasFrame(ulong targetTimestamp)
    {
        if (!isInitialized)
        {
            Debug.LogWarning("Multi-camera processor not initialized");
            return;
        }

        // Collect data from all cameras
        List<uint[]> allDepthData = new List<uint[]>();
        List<Vector2[]> allLutData = new List<Vector2[]>();
        List<Texture2D> allColorTextures = new List<Texture2D>();

        foreach (var controller in frameControllers)
        {
            var device = controller.Device;
            if (device == null) continue;

            bool synchronized = controller.SeekToTimestamp(targetTimestamp, out ulong actualTimestamp);
            if (synchronized)
            {
                // Process the synchronized frame
                var frameOk = controller.ParseRecord(true); // optimizeForGPU = true
                if (frameOk)
                {
                    controller.UpdateTexture(true); // optimizeForGPU = true
                    controller.UpdateCurrentTimestamp(actualTimestamp);
                }
            }

            // Get the processed data
            var depthData = device.GetLatestDepthData();
            var colorTexture = device.GetLatestColorTexture();

            if (depthData != null && colorTexture != null)
            {
                allDepthData.Add(depthData);
                allColorTextures.Add(colorTexture);

                // Get LUT data from device
                Vector2[] lutData = GetLutDataFromDevice(device);
                allLutData.Add(lutData);
            }
        }

        // Upload data to GPU and process
        if (allDepthData.Count > 0)
        {
            UploadDataAndProcess(allDepthData, allLutData, allColorTextures);
        }

        foreach (var frameController in frameControllers)
        {
            frameController.NotifyFirstFrameProcessed();
        }
    }

    private Vector2[] GetLutDataFromDevice(SensorDevice device)
    {
        if (device == null) return new Vector2[0];

        // Get LUT directly from centralized SensorDevice
        Vector2[,] depthUndistortLUT = device.GetDepthUndistortLUT();
        if (depthUndistortLUT == null) return new Vector2[0];

        int width = device.GetDepthWidth();
        int height = device.GetDepthHeight();

        // Convert 2D array to 1D array for compute shader
        Vector2[] lutData = new Vector2[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                lutData[y * width + x] = depthUndistortLUT[x, y];
            }
        }

        return lutData;
    }

    private void UploadDataAndProcess(List<uint[]> allDepthData, List<Vector2[]> allLutData, List<Texture2D> allColorTextures)
    {
        // Concatenate all depth data
        uint[] combinedDepthData = allDepthData.SelectMany(x => x).ToArray();
        allDepthDataBuffer.SetData(combinedDepthData);

        // Concatenate all LUT data
        Vector2[] combinedLutData = allLutData.SelectMany(x => x).ToArray();
        allLutDataBuffer.SetData(combinedLutData);

        // Update color texture array
        for (int i = 0; i < allColorTextures.Count && i < frameControllers.Count; i++)
        {
            Graphics.CopyTexture(allColorTextures[i], 0, 0, colorTextureArray, i, 0);
        }

        // Reset valid counts
        validCountBuffer.SetData(new int[frameControllers.Count]);

        // Set compute shader parameters
        int kernelIndex = multiCamProcessor.FindKernel("ProcessMultiCamDepthData");
        multiCamProcessor.SetInt("cameraCount", frameControllers.Count);
        multiCamProcessor.SetBuffer(kernelIndex, "cameraMetadata", cameraMetadataBuffer);
        multiCamProcessor.SetBuffer(kernelIndex, "allDepthData", allDepthDataBuffer);
        multiCamProcessor.SetBuffer(kernelIndex, "allLutData", allLutDataBuffer);
        multiCamProcessor.SetTexture(kernelIndex, "colorTextureArray", colorTextureArray);
        multiCamProcessor.SetBuffer(kernelIndex, "allOutputVertices", allOutputBuffer);
        multiCamProcessor.SetBuffer(kernelIndex, "validCountPerCamera", validCountBuffer);

        // Dispatch compute shader
        int threadGroups = Mathf.CeilToInt(totalPixels / 32f);
        multiCamProcessor.Dispatch(kernelIndex, threadGroups, 1, 1);

        // Apply results to unified or individual camera meshes
        ApplyResultsToMeshes();
    }

    private void ApplyResultsToMeshes()
    {
        // Read back the results
        VertexData[] allResults = new VertexData[totalPixels];
        allOutputBuffer.GetData(allResults);

        int[] validCounts = new int[frameControllers.Count];
        validCountBuffer.GetData(validCounts);

        // If unified mesh callback is set, create unified mesh
        if (onUnifiedMeshUpdated != null)
        {
            CreateUnifiedMesh(allResults, validCounts);
        }
        else
        {
            // Otherwise, split results to individual cameras (legacy behavior)
            UpdateIndividualCameraMeshes(allResults, validCounts);
        }
    }

    private void CreateUnifiedMesh(VertexData[] allResults, int[] validCounts)
    {
        // Count total valid points across all cameras
        int totalValidPoints = 0;
        foreach (int count in validCounts)
        {
            totalValidPoints += count;
        }

        if (totalValidPoints == 0)
        {
            Debug.LogWarning("No valid points in unified mesh");
            return;
        }

        // Extract all valid vertices and colors
        Vector3[] vertices = new Vector3[totalValidPoints];
        Color[] colors = new Color[totalValidPoints];
        int[] indices = new int[totalValidPoints];

        int validIndex = 0;
        for (int i = 0; i < allResults.Length; i++)
        {
            if (allResults[i].isValid == 1)
            {
                vertices[validIndex] = allResults[i].vertex;
                colors[validIndex] = allResults[i].color;
                indices[validIndex] = validIndex;
                validIndex++;
            }
        }

        // Create unified mesh
        Mesh unifiedMesh = new Mesh();
        unifiedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        unifiedMesh.vertices = vertices;
        unifiedMesh.colors = colors;
        unifiedMesh.SetIndices(indices, MeshTopology.Points, 0);
        unifiedMesh.RecalculateBounds();

        // Invoke callback
        onUnifiedMeshUpdated?.Invoke(unifiedMesh);

        Debug.Log($"Unified mesh created: {totalValidPoints} points from {frameControllers.Count} cameras");
    }

    private void UpdateIndividualCameraMeshes(VertexData[] allResults, int[] validCounts)
    {
        // NOTE: This method is legacy code from before the architecture refactoring.
        // It is no longer used since MultiPointCloudView always sets the unified mesh callback.
        // Keeping for reference but should be removed in future cleanup.
        Debug.LogWarning("UpdateIndividualCameraMeshes called - this is legacy code and should not be used");
    }

    void OnDestroy()
    {
        // Dispose GPU resources
        cameraMetadataBuffer?.Dispose();
        allDepthDataBuffer?.Dispose();
        allLutDataBuffer?.Dispose();
        allOutputBuffer?.Dispose();
        validCountBuffer?.Dispose();

        if (colorTextureArray != null)
        {
            UnityEngine.Object.DestroyImmediate(colorTextureArray);
        }
    }
}