using UnityEngine;
using System;
using System.Linq;

public static class DepthMeshGenerator
{
    public static Mesh CreateMeshFromDepth(ushort[] depthValues, SensorHeader header, float depthScaleFactor)
    {
        int width = header.custom.camera_sensor.width;
        int height = header.custom.camera_sensor.height;
        if (depthValues.Length != width * height)
            throw new ArgumentException("Depth size does not match header resolution");

        // Intrinsics 抽出
        float[] intrinsics = ParseIntrinsics(header.custom.additional_info.orbbec_intrinsics_parameters);
        float fx = intrinsics[0], fy = intrinsics[1], cx = intrinsics[2], cy = intrinsics[3];

        // メッシュ頂点生成
        Vector3[] vertices = new Vector3[depthValues.Length];
        int idx = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, idx++)
            {
                float z = depthValues[idx] * (depthScaleFactor / 1000f);
                if (z <= 0) z = 0.0001f;

                float px = (x - cx) * z / fx;
                float py = (y - cy) * z / fy;

                // Unity座標系に調整（+y上）
                vertices[idx] = new Vector3(px, -py, z);
            }
        }

        // Indexは点群として扱う
        int[] indices = Enumerable.Range(0, vertices.Length).ToArray();

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    private static float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }
}
