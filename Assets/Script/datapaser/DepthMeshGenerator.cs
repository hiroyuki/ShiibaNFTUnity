using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

public class DepthMeshGenerator
{
    int depthWidth, depthHeight;
    private float[] intrinsics; // fx, fy, cx, cy
    float depthScaleFactor;
    float depthBias;

    float[] colorIntrinsics; // fx, fy, cx, cy
    private float[] color_distortion; // k1～k6, p1, p2
    private float[] depth_distortion; // k1～k6, p1, p2
    private Vector2[,] depthUndistortLUT;
    int colorWidth, colorHeight;
    Color32[] latestColorPixels;

    Quaternion rotation = Quaternion.identity;
    Vector3 translation = Vector3.zero;

    public void setup(SensorHeader header, float depthScaleFactor, float depthBias = 0f)
    {
        this.depthWidth = header.custom.camera_sensor.width;
        this.depthHeight = header.custom.camera_sensor.height;
        // DEBUG: Show raw intrinsics string from YAML
        Debug.Log($"Raw intrinsics from YAML: '{header.custom.additional_info.orbbec_intrinsics_parameters}'");
        
        var allParams = ParseIntrinsics(header.custom.additional_info.orbbec_intrinsics_parameters);
        this.intrinsics = allParams.Take(4).ToArray(); // fx, fy, cx, cy
        
        // DEBUG: Show parsed values
        Debug.Log($"Parsed intrinsics: fx={allParams[0]:F2}, fy={allParams[1]:F2}, cx={allParams[2]:F2}, cy={allParams[3]:F2}");
        this.depth_distortion = allParams.Skip(4).ToArray(); // k1~k6, p1, p2
        this.depthScaleFactor = depthScaleFactor;
        this.depthBias = depthBias;
        
        // Build depth undistortion LUT using OpenCV (fallback to simple pinhole for now)
        this.depthUndistortLUT = OpenCVUndistortHelper.BuildUndistortLUTFromHeader(header);

        // Debug.Log($"DepthMeshGenerator setup: {width}x{height}, scale={depthScaleFactor}, rotation={rotation}, translation={translation}");
    }

    public void UpdateMeshFromDepthAndColor(Mesh mesh, ushort[] depthValues, Color32[] colorPixels)
    {
        if (depthValues.Length != depthWidth * depthHeight)
            throw new ArgumentException("Depth size does not match resolution");

        if (colorPixels == null || colorIntrinsics == null)
            throw new InvalidOperationException("Color intrinsics or pixels not set");

        latestColorPixels = colorPixels; // 保持して使う

        float fx_d = intrinsics[0], fy_d = intrinsics[1], cx_d = intrinsics[2], cy_d = intrinsics[3];
        float fx_c = colorIntrinsics[0], fy_c = colorIntrinsics[1], cx_c = colorIntrinsics[2], cy_c = colorIntrinsics[3];
        
        // DEBUG: Print camera intrinsics (only once)
        {
            Debug.Log($"Depth Camera: fx={fx_d:F2}, fy={fy_d:F2}, cx={cx_d:F2}, cy={cy_d:F2}");
            Debug.Log($"Image size: {depthWidth}x{depthHeight}");
            Debug.Log($"Depth scale factor: {depthScaleFactor}");
        }

        List<Vector3> validVertices = new List<Vector3>();
        List<Color32> validColors = new List<Color32>();
        List<int> validIndices = new List<int>();
        
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
                px = (x - cx_d) * z / fx_d;
                py = (y - cy_d) * z / fy_d;
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

            // Only add points with valid (non-black) colors
            if (hasValidColor)
            {
                validVertices.Add(cPoint);
                validColors.Add(color);
                validIndices.Add(validVertices.Count - 1);
            }
        }

        // Convert lists to arrays
        Vector3[] vertices = validVertices.ToArray();
        Color32[] vertexColors = validColors.ToArray();
        int[] indices = validIndices.ToArray();

        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.colors32 = vertexColors;
        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();
    }



    private float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }


    private Quaternion ParseRotationMatrix(string param)
    {
        // Debug.Log("Parsing rotation matrix: " + param);

        var parts = param.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => float.Parse(p.Trim())).ToArray();

        if (parts.Length != 9)
        {
            Debug.LogWarning("Invalid rotation matrix, defaulting to identity.");
            return Quaternion.identity;
        }

        Matrix4x4 mat = new Matrix4x4();

        // ✅ 左右と上下を反転（XとY軸）
        mat.SetRow(0, new Vector4(-parts[0], -parts[1], -parts[2], 0)); // X反転
        mat.SetRow(1, new Vector4(-parts[3], -parts[4], -parts[5], 0)); // Y反転
        mat.SetRow(2, new Vector4(parts[6], parts[7], parts[8], 0)); // Zそのまま
        mat.SetRow(3, new Vector4(0, 0, 0, 1));

        return mat.rotation;
    }
    private Vector3 ParseVector3(string param)
    {
        // Debug.Log("Parsing vector3: " + param);
        var parts = param.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => float.Parse(p.Trim())).ToArray();
        if (parts.Length == 3)
            return new Vector3(parts[0], parts[1], parts[2]);

        Debug.LogWarning("Invalid Vector3 input, defaulting to Vector3.zero");
        return Vector3.zero;
    }

    public void SetupColorIntrinsics(SensorHeader colorHeader)
    {
        colorIntrinsics = ParseIntrinsics(colorHeader.custom.additional_info.orbbec_intrinsics_parameters);
        colorWidth = colorHeader.custom.camera_sensor.width;
        colorHeight = colorHeader.custom.camera_sensor.height;

        // fx, fy, cx, cy: 最初の4要素
        // distortion: 残り8要素（k1~k6, p1, p2）
        this.color_distortion = colorIntrinsics.Skip(4).ToArray(); // 8要素
        this.colorIntrinsics = colorIntrinsics.Take(4).ToArray(); // 4要素
        // Debug.Log($"Color intrinsics set: {string.Join(", ", this.colorIntrinsics)}, distortion={string.Join(", ", this.color_distortion)}");  
    }

    public Texture2D ProjectDepthToColorImage(ushort[] depthValues)
    {
        if (colorIntrinsics == null || latestColorPixels == null)
        {
            Debug.LogWarning("Color intrinsics or color pixels not set.");
            return null;
        }

        Texture2D output = new Texture2D(colorWidth, colorHeight, TextureFormat.RGB24, false);
        Color32[] black = new Color32[colorWidth * colorHeight];
        for (int i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
        output.SetPixels32(black); // 黒背景

        float fx_d = intrinsics[0], fy_d = intrinsics[1], cx_d = intrinsics[2], cy_d = intrinsics[3];
        float fx_c = colorIntrinsics[0], fy_c = colorIntrinsics[1], cx_c = colorIntrinsics[2], cy_c = colorIntrinsics[3];

        for (int i = 0; i < depthValues.Length; i++)
        {
            int x = i % depthWidth;
            int y = i / depthWidth;
            float z = depthValues[i] * (depthScaleFactor / 1000f);
            if (z <= 0) continue;

            float px = (x - cx_d) * z / fx_d;
            float py = (y - cy_d) * z / fy_d;
            Vector3 dPoint = new Vector3(-px, py, z);
            Vector3 cPoint = rotation * dPoint + translation;

            if (cPoint.z <= 0) continue;

            float u = fx_c * cPoint.x / cPoint.z + cx_c;
            float v = fy_c * cPoint.y / cPoint.z + cy_c;

            int ui = Mathf.RoundToInt(u);
            int vi = Mathf.RoundToInt(v);

            if (ui >= 0 && ui < colorWidth && vi >= 0 && vi < colorHeight)
            {
                output.SetPixel(ui, vi, Color.white);
            }
        }

        output.Apply();
        return output;
    }

    public Texture2D CreateFilteredPointCloudProjection(ushort[] depthValues, Color32[] colorPixels)
    {
        if (colorIntrinsics == null || colorPixels == null)
        {
            Debug.LogWarning("Color intrinsics or color pixels not set.");
            return null;
        }

        latestColorPixels = colorPixels; // Update latest color pixels

        Texture2D output = new Texture2D(colorWidth, colorHeight, TextureFormat.RGB24, false);
        Color32[] outputPixels = new Color32[colorWidth * colorHeight];
        
        // Initialize with black background
        for (int i = 0; i < outputPixels.Length; i++) 
            outputPixels[i] = new Color32(0, 0, 0, 255);

        // float fx_d = intrinsics[0], fy_d = intrinsics[1], cx_d = intrinsics[2], cy_d = intrinsics[3];
        // float fx_c = colorIntrinsics[0], fy_c = colorIntrinsics[1], cx_c = colorIntrinsics[2], cy_c = colorIntrinsics[3];

        for (int i = 0; i < depthValues.Length; i++)
        {
            int x = i % depthWidth;
            int y = i / depthWidth;
            
            // Apply depth bias correction and scale factor
            float correctedDepth = depthValues[i] + depthBias;
            float z = correctedDepth * (depthScaleFactor / 1000f);
            if (z <= 0) continue; // Skip invalid depth

            // Use OpenCV-generated undistortion LUT for accurate results
            Vector2 rayCoords = depthUndistortLUT[x, y];
            float px = rayCoords.x * z;
            float py = rayCoords.y * z;

            Vector3 dPoint = new Vector3(px, py, z);
            Vector3 cPoint = rotation * dPoint + translation;

            // Skip points behind camera
            if (cPoint.z <= 0) continue;

            // Project to color camera with distortion (same as point cloud generation)
            float x_norm = cPoint.x / cPoint.z;
            float y_norm = cPoint.y / cPoint.z;
            Vector2 colorPixel = DistortColorProjection(x_norm, y_norm);

            int ui = Mathf.RoundToInt(colorPixel.x);
            int vi = colorHeight - 1 - Mathf.RoundToInt(colorPixel.y);

            // Get the color for this point (same filtering as point cloud)
            Color32 pointColor = new Color32(0, 0, 0, 255);
            bool hasValidColor = false;

            if (ui >= 0 && ui < colorWidth && vi >= 0 && vi < colorHeight)
            {
                int colorIdx = vi * colorWidth + ui;
                if (colorIdx >= 0 && colorIdx < latestColorPixels.Length)
                {
                    pointColor = latestColorPixels[colorIdx];
                    // Same black point filtering as point cloud: RGB > 5
                    hasValidColor = pointColor.r > 5 || pointColor.g > 5 || pointColor.b > 5;
                }
            }

            // Only draw points with valid colors (same filtering as point cloud)
            if (hasValidColor && ui >= 0 && ui < colorWidth && vi >= 0 && vi < colorHeight)
            {
                int outputIdx = vi * colorWidth + ui;
                outputPixels[outputIdx] = pointColor;
            }
        }

        output.SetPixels32(outputPixels);
        output.Apply();
        return output;
    }
    
    public void ApplyDepthToColorExtrinsics(Vector3 translation, Quaternion rotation)
    {
        this.translation = translation;
        this.rotation = rotation;
    }

    // Undistort depth pixel coordinates to normalized coordinates
    private Vector2 UndistortDepthPixel(float u, float v)
    {
        float fx = intrinsics[0], fy = intrinsics[1], cx = intrinsics[2], cy = intrinsics[3];
        float k1 = depth_distortion[0], k2 = depth_distortion[1], k3 = depth_distortion[2];
        float k4 = depth_distortion[3], k5 = depth_distortion[4], k6 = depth_distortion[5];
        float p1 = depth_distortion[6], p2 = depth_distortion[7];

        // Convert to normalized coordinates (distorted)
        float x_d = (u - cx) / fx;
        float y_d = (v - cy) / fy;

        // Iterative undistortion using Newton-Raphson
        float x_u = x_d, y_u = y_d;
        for (int i = 0; i < 5; i++)
        {
            float r2 = x_u * x_u + y_u * y_u;
            float r4 = r2 * r2;
            float r6 = r4 * r2;

            float radial = 1 + k1 * r2 + k2 * r4 + k3 * r6;
            float x_distorted = x_u * radial + 2 * p1 * x_u * y_u + p2 * (r2 + 2 * x_u * x_u);
            float y_distorted = y_u * radial + 2 * p2 * x_u * y_u + p1 * (r2 + 2 * y_u * y_u);

            x_u -= (x_distorted - x_d) * 0.9f;
            y_u -= (y_distorted - y_d) * 0.9f;
        }

        return new Vector2(x_u, y_u);
    }

    // Apply distortion to normalized coordinates for color projection
    private Vector2 DistortColorProjection(float x_norm, float y_norm)
    {
        float fx = colorIntrinsics[0], fy = colorIntrinsics[1], cx = colorIntrinsics[2], cy = colorIntrinsics[3];
        float k1 = color_distortion[0], k2 = color_distortion[1], k3 = color_distortion[2];
        float k4 = color_distortion[3], k5 = color_distortion[4], k6 = color_distortion[5];
        float p1 = color_distortion[6], p2 = color_distortion[7];

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

}
