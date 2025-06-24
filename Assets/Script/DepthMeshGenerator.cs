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

    public void UpdateMeshFromDepthAndColor(Mesh mesh, ushort[] depthValues, Color32[] colorPixels)
    {

        Debug.Log("ColorPixels.Length = " + colorPixels.Length);
        Debug.Log("Depth.Length = " + depthValues.Length);
        if (depthValues.Length != width * height)
            throw new ArgumentException("Depth size does not match header resolution");
        if (colorPixels.Length != depthValues.Length)
            throw new ArgumentException("Color size does not match resolution");

        float fx = intrinsics[0], fy = intrinsics[1], cx = intrinsics[2], cy = intrinsics[3];

        Vector3[] vertices = new Vector3[depthValues.Length];
        int[] indices = new int[depthValues.Length];

        for (int i = 0; i < depthValues.Length; i++)
        {
            int x = i % width;
            int y = i / width;
            float z = depthValues[i] * (depthScaleFactor / 1000f);
            if (z <= 0) z = 0.0001f;

            float px = (x - cx) * z / fx;
            float py = (y - cy) * z / fy;

            vertices[i] = new Vector3(px, -py, z);
            indices[i] = i;
        }

        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.colors32 = colorPixels;
        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();
    }


    private float[] ParseIntrinsics(string param)
    {
        return param.Trim('[', ']').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(float.Parse).ToArray();
    }
}
