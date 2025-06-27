using UnityEngine;
using System;
using System.Linq;

public class DepthMeshGenerator
{
    int width, height;
    private float[] intrinsics; // fx, fy, cx, cy
    float depthScaleFactor;

    float[] colorIntrinsics; // fx, fy, cx, cy
    private float[] color_distortion; // k1～k6, p1, p2
    int colorWidth, colorHeight;
    Color32[] latestColorPixels;

    Quaternion rotation = Quaternion.identity;
    Vector3 translation = Vector3.zero;

    public void setup(SensorHeader header, float depthScaleFactor)
    {
        this.width = header.custom.camera_sensor.width;
        this.height = header.custom.camera_sensor.height;
        this.intrinsics = ParseIntrinsics(header.custom.additional_info.orbbec_intrinsics_parameters);
        this.depthScaleFactor = depthScaleFactor;

        // Debug.Log($"DepthMeshGenerator setup: {width}x{height}, scale={depthScaleFactor}, rotation={rotation}, translation={translation}");
    }

    public void UpdateMeshFromDepthAndColor(Mesh mesh, ushort[] depthValues, Color32[] colorPixels)
    {
        if (depthValues.Length != width * height)
            throw new ArgumentException("Depth size does not match resolution");

        if (colorPixels == null || colorIntrinsics == null)
            throw new InvalidOperationException("Color intrinsics or pixels not set");

        latestColorPixels = colorPixels; // 保持して使う

        float fx_d = intrinsics[0], fy_d = intrinsics[1], cx_d = intrinsics[2], cy_d = intrinsics[3];
        float fx_c = colorIntrinsics[0], fy_c = colorIntrinsics[1], cx_c = colorIntrinsics[2], cy_c = colorIntrinsics[3];

        Vector3[] vertices = new Vector3[depthValues.Length];
        Color32[] vertexColors = new Color32[depthValues.Length];
        int[] indices = new int[depthValues.Length];

        for (int i = 0; i < depthValues.Length; i++)
        {
            int x = i % width;
            int y = i / width;
            float z = depthValues[i] * (depthScaleFactor / 1000f);
            if (z <= 0) z = 0.0001f;

            float px = (x - cx_d) * z / fx_d;
            float py = (y - cy_d) * z / fy_d;

            Vector3 dPoint = new Vector3(-px, py, z);
            Vector3 cPoint = rotation * dPoint + translation;

            // color画像上に再投影
            float u = fx_c * cPoint.x / cPoint.z + cx_c;
            float v = fy_c * cPoint.y / cPoint.z + cy_c;

            int ui = Mathf.RoundToInt(u);
            int vi = Mathf.RoundToInt(v);

            Color32 color = new Color32(0, 0, 0, 255); // デフォルト:黒

            if (ui >= 0 && ui < colorWidth && vi >= 0 && vi < colorHeight)
            {
                int colorIdx = vi * colorWidth + ui;
                if (colorIdx >= 0 && colorIdx < latestColorPixels.Length)
                    color = latestColorPixels[colorIdx];
            }

            vertices[i] = cPoint;
            vertexColors[i] = color;
            indices[i] = i;
        }

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
            int x = i % width;
            int y = i / width;
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
    
    public void ApplyDepthToColorExtrinsics(Vector3 translation, Quaternion rotation)
    {
        this.translation = translation;
        this.rotation = rotation;
    }

}
