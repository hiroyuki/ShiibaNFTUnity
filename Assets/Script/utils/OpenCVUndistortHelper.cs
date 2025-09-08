using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.Calib3dModule;

public static class OpenCVUndistortHelper
{
    /// <summary>
    /// Build undistortion map using OpenCV for better reliability
    /// </summary>
    public static Vector2[,] BuildUndistortLUT(int width, int height, 
        double fx, double fy, double cx, double cy,
        double k1, double k2, double k3, double k4, double k5, double k6, 
        double p1, double p2)
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

        // Generate undistortion LUT using undistortPoints (more direct approach)
        Vector2[,] undistortLUT = new Vector2[width, height];
        
        // Create input points as Point2f array
        List<Point> inputPointsList = new List<Point>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                inputPointsList.Add(new Point(x, y));
            }
        }
        
        // Convert to MatOfPoint2f
        MatOfPoint2f inputPoints = new MatOfPoint2f();
        inputPoints.fromList(inputPointsList);
        
        // Undistort the points
        MatOfPoint2f outputPoints = new MatOfPoint2f();
        Calib3d.undistortPoints(inputPoints, outputPoints, cameraMatrix, distCoeffs, new Mat(), cameraMatrix);
        
        // Extract undistorted points and convert to normalized coordinates
        List<Point> outputPointsList = outputPoints.toList();
        
        int idx = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Point undistPoint = outputPointsList[idx];
                float undistX = (float)undistPoint.x;
                float undistY = (float)undistPoint.y;
                
                // Convert to normalized ray coordinates
                float rayX = (undistX - (float)cx) / (float)fx;
                float rayY = (undistY - (float)cy) / (float)fy;
                
                undistortLUT[x, y] = new Vector2(rayX, rayY);
                idx++;
            }
        }

        // Cleanup OpenCV objects
        cameraMatrix.Dispose();
        distCoeffs.Dispose();
        inputPoints.Dispose();
        outputPoints.Dispose();

        Debug.Log($"Built OpenCV undistortion LUT for {width}x{height} with fx={fx:F2}, fy={fy:F2}");
        return undistortLUT;
    }

    /// <summary>
    /// Build undistortion map from SensorHeader using OpenCV
    /// </summary>
    public static Vector2[,] BuildUndistortLUTFromHeader(SensorHeader header)
    {
        Debug.Log($"Raw orbbec_intrinsics_parameters: '{header.custom.additional_info.orbbec_intrinsics_parameters}'");
        var allParams = ParseIntrinsics(header.custom.additional_info.orbbec_intrinsics_parameters);
        Debug.Log($"Parsed parameters ({allParams.Length}): {string.Join(", ", allParams.Select(x => x.ToString("F4")))}");
        if (allParams.Length >= 12)
        {
            Debug.Log($"fx={allParams[0]:F4}, fy={allParams[1]:F4}, cx={allParams[2]:F4}, cy={allParams[3]:F4}");
            Debug.Log($"k1={allParams[4]:F4}, k2={allParams[5]:F4}, k3={allParams[6]:F4}, k4={allParams[7]:F4}, k5={allParams[8]:F4}, k6={allParams[9]:F4}");
            Debug.Log($"p1={allParams[10]:F4}, p2={allParams[11]:F4}");
        }
        
        return BuildUndistortLUT(
            header.custom.camera_sensor.width,
            header.custom.camera_sensor.height,
            allParams[0], allParams[1], allParams[2], allParams[3], // fx, fy, cx, cy
            allParams[4], allParams[5], allParams[6], // k1, k2, k3
            allParams[7], allParams[8], allParams[9], // k4, k5, k6
            allParams[10], allParams[11] // p1, p2
        );
    }

    private static double[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(double.Parse).ToArray();
    }
}