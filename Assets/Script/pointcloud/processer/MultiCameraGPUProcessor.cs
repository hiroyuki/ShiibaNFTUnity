using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class MultiCameraGPUProcessor : MonoBehaviour
{
    private ComputeShader multiCamProcessor;
    private List<SingleCameraDataManager> cameraManagers = new List<SingleCameraDataManager>();

    // Multi-camera buffers
    private ComputeBuffer cameraMetadataBuffer;
    private ComputeBuffer allDepthDataBuffer;
    private ComputeBuffer allLutDataBuffer;
    private ComputeBuffer allOutputBuffer;
    private ComputeBuffer validCountBuffer;

    // Texture array for all camera colors
    private Texture2DArray colorTextureArray;

    // Camera metadata structure (matches BasePointCloudProcessor structure)
    private struct CameraMetadata
    {
        // Transform matrices
        public Matrix4x4 rotationMatrix;
        public Vector3 translation;
        public Matrix4x4 depthViewerTransform;

        // Camera intrinsics arrays (following BasePointCloudProcessor pattern)
        public float fx_d, fy_d, cx_d, cy_d; // Depth camera: fx, fy, cx, cy
        
        public float fx_c, fy_c, cx_c, cy_c; // Color camera: fx, fy, cx, cy
        public float k1_d, k2_d, k3_d, k4_d, k5_d, k6_d, p1_d, p2_d; // k1～k6, p1, p2
        public float k1_c, k2_c, k3_c, k4_c, k5_c, k6_c, p1_c, p2_c; // k1～k6, p1, p2

        // Image dimensions
        public uint depthWidth;
        public uint depthHeight;
        public uint colorWidth;
        public uint colorHeight;

        // Processing parameters
        public float depthScaleFactor;
        public float depthBias;
        public int useOpenCVLUT;

        // Bounding volume parameters
        public int hasBoundingVolume;
        public int showAllPoints;
        public Matrix4x4 boundingVolumeInverseTransform;

        // Buffer offsets
        public uint depthDataOffset;
        public uint lutDataOffset;
        public uint outputOffset;
    }

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


    public void RegisterCameraManager(SingleCameraDataManager cameraManager)
    {
        if (!cameraManagers.Contains(cameraManager))
        {
            cameraManagers.Add(cameraManager);
            Debug.Log($"Registered camera: {cameraManager.name}, Total cameras: {cameraManagers.Count}");
        }
    }

    public void InitializeMultiCameraProcessing()
    {
        if (cameraManagers.Count == 0)
        {
            Debug.LogWarning("No camera managers registered for multi-camera processing");
            return;
        }

        Debug.Log($"Initializing multi-camera processing for {cameraManagers.Count} cameras");

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

    private void CalculateBufferSizes()
    {
        totalPixels = 0;
        foreach (var manager in cameraManagers)
        {
            // Get depth dimensions from the manager's device
            var device = manager.GetComponent<SingleCameraDataManager>().GetDevice();
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
        cameraMetadataBuffer = new ComputeBuffer(cameraManagers.Count, System.Runtime.InteropServices.Marshal.SizeOf<CameraMetadata>());

        // Combined depth data buffer
        allDepthDataBuffer = new ComputeBuffer(totalPixels, sizeof(uint));

        // Combined LUT data buffer
        allLutDataBuffer = new ComputeBuffer(totalPixels, sizeof(float) * 2);//buffrer for Vector2

        // Combined output buffer
        allOutputBuffer = new ComputeBuffer(totalPixels, System.Runtime.InteropServices.Marshal.SizeOf<VertexData>());

        // Valid count per camera
        validCountBuffer = new ComputeBuffer(cameraManagers.Count, sizeof(int));

        Debug.Log("GPU buffers created successfully");
    }

    private void SetupCameraMetadata()
    {
        CameraMetadata[] metadataArray = new CameraMetadata[cameraManagers.Count];
        uint currentDepthOffset = 0;
        uint currentLutOffset = 0;
        uint currentOutputOffset = 0;

        for (int i = 0; i < cameraManagers.Count; i++)
        {
            var manager = cameraManagers[i];
            var device = manager.GetDevice();

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
            metadata.rotationMatrix = Matrix4x4.Rotate(device.GetDepthToColorRotation());
            metadata.translation = device.GetDepthToColorTranslation();
            metadata.depthViewerTransform = manager.transform.localToWorldMatrix;

            // Bounding volume (placeholder - needs implementation)
            metadata.hasBoundingVolume = 0;
            metadata.showAllPoints = 1;
            metadata.boundingVolumeInverseTransform = Matrix4x4.identity;

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
        if (cameraManagers.Count == 0) return;

        // Get texture dimensions from first camera
        var firstDevice = cameraManagers[0].GetDevice();
        if (firstDevice == null) return;

        var colorHeader = firstDevice.GetColorParser()?.sensorHeader;
        if (colorHeader == null) return;

        int width = colorHeader.custom.camera_sensor.width;
        int height = colorHeader.custom.camera_sensor.height;

        // Create texture array
        colorTextureArray = new Texture2DArray(width, height, cameraManagers.Count, TextureFormat.RGB24, false);
        colorTextureArray.wrapMode = TextureWrapMode.Clamp;
        colorTextureArray.filterMode = FilterMode.Bilinear;

        Debug.Log($"Created color texture array: {width}x{height}x{cameraManagers.Count}");
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

        foreach (var manager in cameraManagers)
        {
            var device = manager.GetDevice();
            if (device == null) continue;

            bool synchronized = manager.SeekToTimestamp(targetTimestamp, out ulong actualTimestamp);
            if (synchronized)
            {
                // Process the synchronized frame

                var frameOk = manager.ParseRecord();
                if (frameOk)
                {
                    manager.UpdateTexture();
                    manager.UpdateCurrentTimestamp(actualTimestamp);
                }
            }


            // Get the processed data
            var depthData = device.GetLatestDepthData();
            var colorTexture = device.GetLatestColorTexture();

            if (depthData != null && colorTexture != null)
            {
                allDepthData.Add(depthData);
                allColorTextures.Add(colorTexture);

                // Get LUT data (this needs to be exposed from the processor)
                Vector2[] lutData = GetLutDataFromManager(manager);
                allLutData.Add(lutData);
            }
        }

        // Upload data to GPU and process
        if (allDepthData.Count > 0)
        {
            UploadDataAndProcess(allDepthData, allLutData, allColorTextures);
        }
        
        foreach (var manager in cameraManagers)
        {
            
            manager.NotifyFirstFrameProcessed();
        }
    }

    private Vector2[] GetLutDataFromManager(SingleCameraDataManager manager)
    {
        var device = manager.GetDevice();
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
        for (int i = 0; i < allColorTextures.Count && i < cameraManagers.Count; i++)
        {
            Graphics.CopyTexture(allColorTextures[i], 0, 0, colorTextureArray, i, 0);
        }

        // Reset valid counts
        validCountBuffer.SetData(new int[cameraManagers.Count]);

        // Set compute shader parameters
        int kernelIndex = multiCamProcessor.FindKernel("ProcessMultiCamDepthData");
        multiCamProcessor.SetInt("cameraCount", cameraManagers.Count);
        multiCamProcessor.SetBuffer(kernelIndex, "cameraMetadata", cameraMetadataBuffer);
        multiCamProcessor.SetBuffer(kernelIndex, "allDepthData", allDepthDataBuffer);
        multiCamProcessor.SetBuffer(kernelIndex, "allLutData", allLutDataBuffer);
        multiCamProcessor.SetTexture(kernelIndex, "colorTextureArray", colorTextureArray);
        multiCamProcessor.SetBuffer(kernelIndex, "allOutputVertices", allOutputBuffer);
        multiCamProcessor.SetBuffer(kernelIndex, "validCountPerCamera", validCountBuffer);

        // Dispatch compute shader
        int threadGroups = Mathf.CeilToInt(totalPixels / 32f);
        multiCamProcessor.Dispatch(kernelIndex, threadGroups, 1, 1);

        // Apply results to individual camera meshes
        ApplyResultsToMeshes();
    }

    private void ApplyResultsToMeshes()
    {
        // Read back the results
        VertexData[] allResults = new VertexData[totalPixels];
        allOutputBuffer.GetData(allResults);

        int[] validCounts = new int[cameraManagers.Count];
        validCountBuffer.GetData(validCounts);

        // Split results back to individual cameras and update their meshes
        uint currentOffset = 0;
        for (int camIndex = 0; camIndex < cameraManagers.Count; camIndex++)
        {
            var manager = cameraManagers[camIndex];
            var device = manager.GetDevice();
            if (device == null) continue;

            var depthHeader = device.GetDepthParser()?.sensorHeader;
            if (depthHeader == null) continue;

            uint pixelCount = (uint)(depthHeader.custom.camera_sensor.width * depthHeader.custom.camera_sensor.height);

            // Extract this camera's results
            VertexData[] cameraResults = new VertexData[pixelCount];
            System.Array.Copy(allResults, (int)currentOffset, cameraResults, 0, (int)pixelCount);

            // Update the camera's mesh
            UpdateCameraMesh(manager, cameraResults, validCounts[camIndex]);

            currentOffset += pixelCount;
        }
    }

    private void UpdateCameraMesh(SingleCameraDataManager manager, VertexData[] results, int validCount)
    {
        if (validCount == 0) return;

        // Extract valid vertices and colors
        Vector3[] vertices = new Vector3[validCount];
        Color[] colors = new Color[validCount];
        int[] indices = new int[validCount];

        int validIndex = 0;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].isValid == 1)
            {
                vertices[validIndex] = results[i].vertex;
                colors[validIndex] = results[i].color;
                indices[validIndex] = validIndex;
                validIndex++;
            }
        }

        // Update the manager's mesh
        var mesh = manager.GetComponent<MeshFilter>()?.mesh;
        if (mesh != null)
        {
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.Points, 0);
            mesh.RecalculateBounds();
        }
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