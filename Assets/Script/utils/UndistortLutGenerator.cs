using UnityEngine;
using System;

public static class UndistortHelper
{
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
                float x_n = (x - cx) / fx;
                float y_n = (y - cy) / fy;

                float r2 = x_n * x_n + y_n * y_n;
                float r4 = r2 * r2;
                float r6 = r4 * r2;

                float radial = 1 + k1 * r2 + k2 * r4 + k3 * r6;
                float x_d = x_n * radial + 2 * p1 * x_n * y_n + p2 * (r2 + 2 * x_n * x_n);
                float y_d = y_n * radial + 2 * p2 * x_n * y_n + p1 * (r2 + 2 * y_n * y_n);

                float u_d = fx * x_d + cx;
                float v_d = fy * y_d + cy;

                lut[x, y] = new Vector2(u_d, v_d);
            }
        }

        return lut;
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
