using UnityEngine;
using System;
using System.Linq;

public class DepthMeshGenerator
{
    int width, height;
    float[] intrinsics;
    float depthScaleFactor;

    public void setup(SensorHeader header, float depthScaleFactor)
    {
        this.width = header.custom.camera_sensor.width;
        this.height = header.custom.camera_sensor.height;
        this.intrinsics = ParseIntrinsics(header.custom.additional_info.orbbec_intrinsics_parameters);
        this.depthScaleFactor = depthScaleFactor;
    }

    public void UpdateMeshFromDepth(Mesh mesh, ushort[] depthValues)
    {
        
        if (depthValues.Length != width * height)
            throw new ArgumentException("Depth size does not match header resolution");

        float fx = intrinsics[0], fy = intrinsics[1], cx = intrinsics[2], cy = intrinsics[3];

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

                vertices[idx] = new Vector3(px, -py, z);
            }
        }

        int[] indices = Enumerable.Range(0, vertices.Length).ToArray();

        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();
    }

    private float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }
}
