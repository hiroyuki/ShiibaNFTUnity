using UnityEngine;
using System;

public static class UndistortHelper
{
    // Create XY table like K4A's create_xy_table - stores normalized ray coordinates
    public static Vector2[,] BuildUndistortLUT(
        int width, int height,
        float fx, float fy, float cx, float cy,
        float k1, float k2, float k3, float k4, float k5, float k6,
        float p1, float p2)
    {
        Vector2[,] lut = new Vector2[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Equivalent to k4a_calibration_2d_to_3d() at depth=1
                Vector2 normalizedRay = PixelToNormalizedRay(x, y, fx, fy, cx, cy, k1, k2, k3, k4, k5, k6, p1, p2);
                lut[x, y] = normalizedRay;
            }
        }

        return lut;
    }

    // Convert 2D pixel to normalized 3D ray coordinates (equivalent to k4a_calibration_2d_to_3d)
    private static Vector2 PixelToNormalizedRay(float u, float v, 
        float fx, float fy, float cx, float cy,
        float k1, float k2, float k3, float k4, float k5, float k6, float p1, float p2)
    {
        // Step 1: Convert pixel to normalized coordinates (this is the distorted position)
        float x_d = (u - cx) / fx;
        float y_d = (v - cy) / fy;

        // Step 2: Undistort using iterative method (Newton-Raphson)
        float x_u = x_d;  // Initial guess
        float y_u = y_d;

        for (int iter = 0; iter < 10; iter++)
        {
            float r2 = x_u * x_u + y_u * y_u;
            float r4 = r2 * r2;
            float r6 = r4 * r2;

            // Apply distortion model
            float radial = 1 + k1 * r2 + k2 * r4 + k3 * r6;
            float x_distorted = x_u * radial + 2 * p1 * x_u * y_u + p2 * (r2 + 2 * x_u * x_u);
            float y_distorted = y_u * radial + 2 * p2 * x_u * y_u + p1 * (r2 + 2 * y_u * y_u);

            // Calculate error
            float ex = x_distorted - x_d;
            float ey = y_distorted - y_d;

            // Check convergence
            if (ex * ex + ey * ey < 1e-8f) break;

            // Update estimate
            x_u -= ex * 0.9f;
            y_u -= ey * 0.9f;

            // Safety bounds
            if (Mathf.Abs(x_u) > 10.0f || Mathf.Abs(y_u) > 10.0f)
            {
                x_u = x_d; // Fallback to distorted coordinates
                y_u = y_d;
                break;
            }
        }

        // Return normalized ray coordinates (not pixel coordinates!)
        return new Vector2(x_u, y_u);
    }

    public static Vector2[,] BuildUndistortLUTFromHeader(SensorHeader header)
    {
        int width = header.custom.camera_sensor.width;
        int height = header.custom.camera_sensor.height;

        string[] tokens = header.custom.additional_info.orbbec_intrinsics_parameters
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 12)
            throw new ArgumentException("Invalid intrinsics parameters");

        float[] values = Array.ConvertAll(tokens, float.Parse);

        float fx = values[0], fy = values[1], cx = values[2], cy = values[3];
        float k1 = values[4], k2 = values[5], k3 = values[6];
        float k4 = values[7], k5 = values[8], k6 = values[9];
        float p1 = values[10], p2 = values[11];
        Debug.Log($"Undistort LUT parameters: fx={fx}, fy={fy}, cx={cx}, cy={cy}, k1={k1}, k2={k2}, k3={k3}, k4={k4}, k5={k5}, k6={k6}, p1={p1}, p2={p2}");
        return BuildUndistortLUT(width, height, fx, fy, cx, cy, k1, k2, k3, k4, k5, k6, p1, p2);
    }

    public static Color32 BilinearSample(Color32[] pixels, float u, float v, int width, int height)
    {
        int x = Mathf.FloorToInt(u);
        int y = Mathf.FloorToInt(v);

        if (x < 0 || x >= width - 1 || y < 0 || y >= height - 1)
            return new Color32(0, 0, 0, 255);

        float dx = u - x;
        float dy = v - y;

        Color32 c00 = pixels[y * width + x];
        Color32 c10 = pixels[y * width + (x + 1)];
        Color32 c01 = pixels[(y + 1) * width + x];
        Color32 c11 = pixels[(y + 1) * width + (x + 1)];

        Color32 c = Color32.Lerp(
            Color32.Lerp(c00, c10, dx),
            Color32.Lerp(c01, c11, dx),
            dy);

        return c;
    }

    public static Color32[] UndistortImage(Color32[] source, Vector2[,] lut, int width, int height)
    {
        Color32[] result = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 uv_d = lut[x, y];
                result[y * width + x] = BilinearSample(source, uv_d.x, uv_d.y, width, height);
            }
        }

        return result;
    }

    public static ushort[] UndistortImageUShort(ushort[] source, Vector2[,] lut, int width, int height)
    {
        ushort[] result = new ushort[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 uv = lut[x, y];
                int x0 = Mathf.FloorToInt(uv.x);
                int y0 = Mathf.FloorToInt(uv.y);

                if (x0 < 0 || x0 >= width - 1 || y0 < 0 || y0 >= height - 1)
                {
                    result[y * width + x] = 0;
                    continue;
                }

                float dx = uv.x - x0;
                float dy = uv.y - y0;

                float d00 = source[y0 * width + x0];
                float d10 = source[y0 * width + (x0 + 1)];
                float d01 = source[(y0 + 1) * width + x0];
                float d11 = source[(y0 + 1) * width + (x0 + 1)];

                float d0 = Mathf.Lerp(d00, d10, dx);
                float d1 = Mathf.Lerp(d01, d11, dx);
                float d = Mathf.Lerp(d0, d1, dy);

                result[y * width + x] = (ushort)Mathf.Clamp(d, 0, ushort.MaxValue);
            }
        }

        return result;
    }
}
