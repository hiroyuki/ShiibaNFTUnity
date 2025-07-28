using UnityEngine;
using System.Linq;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.Calib3dModule;

public static class OpenCVUndistortHelper
{
    /// <summary>
    /// Build undistortion map using OpenCV for better reliability
    /// </summary>
    public static Vector2[,] BuildUndistortLUT(int width, int height, 
        float fx, float fy, float cx, float cy,
        float k1, float k2, float k3, float k4, float k5, float k6, 
        float p1, float p2)
    {
        // Camera matrix
        Mat cameraMatrix = new Mat(3, 3, CvType.CV_64FC1);
        cameraMatrix.put(0, 0, new double[] {
            fx, 0, cx,
            0, fy, cy,
            0, 0, 1
        });

        // Distortion coefficients (k1, k2, p1, p2, k3, k4, k5, k6)
        Mat distCoeffs = new Mat(1, 8, CvType.CV_64FC1);
        distCoeffs.put(0, 0, new double[] { k1, k2, p1, p2, k3, k4, k5, k6 });

        // Generate undistortion maps
        Mat map1 = new Mat();
        Mat map2 = new Mat();
        
        Calib3d.initUndistortRectifyMap(
            cameraMatrix, distCoeffs, 
            new Mat(), cameraMatrix, // No rectification, use same camera matrix
            new Size(width, height),
            CvType.CV_32FC1, map1, map2
        );

        // Convert OpenCV maps to Unity LUT format
        Vector2[,] undistortLUT = new Vector2[width, height];
        
        float[] map1Data = new float[width * height];
        float[] map2Data = new float[width * height];
        map1.get(0, 0, map1Data);
        map2.get(0, 0, map2Data);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                
                // Get undistorted pixel coordinates
                float undistX = map1Data[idx];
                float undistY = map2Data[idx];
                
                // Convert to normalized ray coordinates
                float rayX = (undistX - cx) / fx;
                float rayY = (undistY - cy) / fy;
                
                undistortLUT[x, y] = new Vector2(rayX, rayY);
            }
        }

        // Cleanup OpenCV objects
        cameraMatrix.Dispose();
        distCoeffs.Dispose();
        map1.Dispose();
        map2.Dispose();

        Debug.Log($"Built OpenCV undistortion LUT for {width}x{height} with fx={fx:F2}, fy={fy:F2}");
        return undistortLUT;
    }

    /// <summary>
    /// Build undistortion map from SensorHeader using OpenCV
    /// </summary>
    public static Vector2[,] BuildUndistortLUTFromHeader(SensorHeader header)
    {
        var allParams = ParseIntrinsics(header.custom.additional_info.orbbec_intrinsics_parameters);
        
        return BuildUndistortLUT(
            header.custom.camera_sensor.width,
            header.custom.camera_sensor.height,
            allParams[0], allParams[1], allParams[2], allParams[3], // fx, fy, cx, cy
            allParams[4], allParams[5], allParams[6], // k1, k2, k3
            allParams[7], allParams[8], allParams[9], // k4, k5, k6
            allParams[10], allParams[11] // p1, p2
        );
    }

    private static float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }
}