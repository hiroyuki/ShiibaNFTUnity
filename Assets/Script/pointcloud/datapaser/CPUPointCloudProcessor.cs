using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

public class CPUPointCloudProcessor : BasePointCloudProcessor
{
    public override ProcessingType ProcessingType => ProcessingType.CPU;


    protected Color32[] latestColorPixels;

    public CPUPointCloudProcessor(string deviceName) : base(deviceName)
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
        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Loading, ProcessingType, "CPU processor setup complete"));
    }

    public override void UpdateMesh(Mesh mesh, SensorDevice device)
    {
        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Processing depth pixels..."));

        var depthValues = device.GetLatestDepthValues();
        var colorPixels = device.GetLatestColorData();

        if (depthValues != null && colorPixels != null)
        {
            UpdateMeshFromDepthAndColor(mesh, depthValues, colorPixels, device);
        }
    }

    protected void UpdateMeshFromDepthAndColor(Mesh mesh, ushort[] depthValues, Color32[] colorPixels, SensorDevice device)
    {
        latestColorPixels = colorPixels;

        List<Vector3> validVertices = new List<Vector3>();
        List<Color32> validColors = new List<Color32>();
        List<int> validIndices = new List<int>();

        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Processing depth pixels by CPU..."));
        ProcessDepthPixels(depthValues, colorPixels, validVertices, validColors, validIndices);

        SetupStatusUI.UpdateDeviceStatus(device.UpdateStatus(DeviceStatusType.Processing, ProcessingType, "Applying data to mesh..."));
        ApplyDataToMesh(mesh, validVertices, validColors, validIndices);
    }

    protected virtual void ProcessDepthPixels(ushort[] depthValues, Color32[] colorPixels, List<Vector3> validVertices, List<Color32> validColors, List<int> validIndices)
    {
        for (int i = 0; i < depthValues.Length; i++)
        {
            int x = i % depthWidth;
            int y = i / depthWidth;
            // Apply depth bias correction and scale factor  
            float correctedDepth = depthValues[i] + depthBias;
            float z = correctedDepth * (depthScaleFactor / 1000f);
            if (z <= 0) continue; // Skip invalid depth

            // Choose between LUT (OpenCV undistortion) or simple pinhole model
            float px, py;
            bool useOpenCVLUT = true; // Toggle this to test different methods
            
            if (useOpenCVLUT)
            {
                // Method 1: OpenCV-generated undistortion LUT
                Vector2 rayCoords = depthUndistortLUT[x, y];
                px = rayCoords.x * z;
                py = rayCoords.y * z;
            }
            else
            {
                // Method 2: Simple pinhole camera model (no distortion correction)
                px = (x - cameraParams.cx_d) * z / cameraParams.fx_d;
                py = (y - cameraParams.cy_d) * z / cameraParams.fy_d;
            }

            Vector3 dPoint = new Vector3(px, py, z);
            Vector3 cPoint = rotation * dPoint + translation;

            // Step 2: Project to color camera with distortion
            if (cPoint.z <= 0) continue; // Skip points behind camera

            float x_norm = cPoint.x / cPoint.z;
            float y_norm = cPoint.y / cPoint.z;
            Vector2 colorPixel = DistortColorProjection(x_norm, y_norm);

            int ui = Mathf.RoundToInt(colorPixel.x);
            int vi = colorHeight - 1 - Mathf.RoundToInt(colorPixel.y);

            Color32 color = new Color32(0, 0, 0, 255); // Default: black
            bool hasValidColor = false;

            if (ui >= 0 && ui < colorWidth && vi >= 0 && vi < colorHeight)
            {
                int colorIdx = vi * colorWidth + ui;
                if (colorIdx >= 0 && colorIdx < latestColorPixels.Length)
                {
                    color = latestColorPixels[colorIdx];
                    // Check if color is not completely black (allowing for slight variations)
                    hasValidColor = color.r > 0 || color.g > 0 || color.b > 0;
                }
            }

            // Convert cPoint (camera local) to world coordinates for bounding volume check
            Vector3 worldPoint = depthViewerTransform != null ? 
                depthViewerTransform.TransformPoint(cPoint) : cPoint;
                
            // Only add points with valid (non-black) colors and within bounding volume (unless debug mode)
            bool withinBounds = PointCloudSettings.showAllPoints || IsPointInBoundingVolume(worldPoint);
            if (hasValidColor && withinBounds)
            {
                validVertices.Add(cPoint);
                validColors.Add(color);
                validIndices.Add(validVertices.Count - 1);
            }
        }
    }

    protected void ApplyDataToMesh(Mesh mesh, List<Vector3> validVertices, List<Color32> validColors, List<int> validIndices)
    {
        mesh.Clear();
        mesh.vertices = validVertices.ToArray();
        mesh.colors32 = validColors.ToArray();
        mesh.SetIndices(validIndices.ToArray(), MeshTopology.Points, 0);
    }

    private Vector2 DistortColorProjection(float x_norm, float y_norm)
    {
        float fx = colorIntrinsics[0], fy = colorIntrinsics[1], cx = colorIntrinsics[2], cy = colorIntrinsics[3];
        float k1 = colorDistortion[0], k2 = colorDistortion[1], k3 = colorDistortion[2];
        float k4 = colorDistortion[3], k5 = colorDistortion[4], k6 = colorDistortion[5];
        float p1 = colorDistortion[6], p2 = colorDistortion[7];

        float r2 = x_norm * x_norm + y_norm * y_norm;
        float r4 = r2 * r2;
        float r6 = r4 * r2;

        float radial = 1 + k1 * r2 + k2 * r4 + k3 * r6;
        float x_d = x_norm * radial + 2 * p1 * x_norm * y_norm + p2 * (r2 + 2 * x_norm * x_norm);
        float y_d = y_norm * radial + 2 * p2 * x_norm * y_norm + p1 * (r2 + 2 * y_norm * y_norm);

        float u = fx * x_d + cx;
        float v = fy * y_d + cy;

        return new Vector2(u, v);
    }

    private bool IsPointInBoundingVolume(Vector3 worldPoint)
    {
        if (boundingVolume == null) return true; // No culling if no bounding volume

        // Convert world point to bounding volume's local space
        Vector3 localPoint = boundingVolume.InverseTransformPoint(worldPoint);

        // Unity Cube vertices are at [-0.5, 0.5], so check against 0.5
        // This makes the culling range match the visual Cube exactly
        return Mathf.Abs(localPoint.x) <= 0.5f &&
               Mathf.Abs(localPoint.y) <= 0.5f &&
               Mathf.Abs(localPoint.z) <= 0.5f;
    }

    public override void Dispose()
    {
        base.Dispose(); // Call base class cleanup
    }
}