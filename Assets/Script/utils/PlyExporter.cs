using System.IO;
using UnityEngine;

/// <summary>
/// Utility class for exporting Unity meshes to PLY format
/// </summary>
public static class PlyExporter
{
    /// <summary>
    /// Export mesh to PLY format (binary little-endian)
    /// </summary>
    /// <param name="mesh">The mesh to export</param>
    /// <param name="filePath">Output file path</param>
    public static void ExportToPLY(Mesh mesh, string filePath)
    {
        if (mesh == null)
        {
            Debug.LogWarning("No mesh to export");
            return;
        }

        try
        {
            var vertices = mesh.vertices;
            var colors = mesh.colors32;

            if (vertices.Length == 0)
            {
                Debug.LogWarning("Mesh has no vertices to export");
                return;
            }

            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // Write PLY header as ASCII
                string header =
                    "ply\n" +
                    "format binary_little_endian 1.0\n" +
                    $"element vertex {vertices.Length}\n" +
                    "property float x\n" +
                    "property float y\n" +
                    "property float z\n" +
                    "property uchar red\n" +
                    "property uchar green\n" +
                    "property uchar blue\n" +
                    "end_header\n";

                byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
                bw.Write(headerBytes);

                // Write vertex data in binary
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    Color32 c = colors != null && i < colors.Length ? colors[i] : new Color32(255, 255, 255, 255);

                    bw.Write(v.x);  // float (4 bytes)
                    bw.Write(v.y);  // float (4 bytes)
                    bw.Write(v.z);  // float (4 bytes)
                    bw.Write(c.r);  // uchar (1 byte)
                    bw.Write(c.g);  // uchar (1 byte)
                    bw.Write(c.b);  // uchar (1 byte)
                }
            }

            long fileSize = new FileInfo(filePath).Length;
            Debug.Log($"Successfully exported {vertices.Length} points to: {filePath} ({fileSize / 1024.0:F2} KB)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to export PLY: {ex.Message}");
        }
    }
}
