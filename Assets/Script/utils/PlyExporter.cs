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

    /// <summary>
    /// Export mesh to PLY format with motion vectors (ASCII format for debugging)
    /// </summary>
    /// <param name="mesh">The mesh to export</param>
    /// <param name="motionVectors">Motion vectors for each vertex (vx, vy, vz)</param>
    /// <param name="filePath">Output file path</param>
    public static void ExportToPLY_ASCII(Mesh mesh, Vector3[] motionVectors, string filePath)
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

            // Validate motion vectors
            bool hasMotionVectors = motionVectors != null && motionVectors.Length == vertices.Length;
            if (motionVectors != null && motionVectors.Length != vertices.Length)
            {
                Debug.LogWarning($"Motion vector count ({motionVectors.Length}) does not match vertex count ({vertices.Length}). Motion vectors will not be exported.");
                hasMotionVectors = false;
            }

            using (StreamWriter writer = new StreamWriter(filePath))
            {
                // Write PLY header
                writer.WriteLine("ply");
                writer.WriteLine("format ascii 1.0");
                writer.WriteLine($"element vertex {vertices.Length}");
                writer.WriteLine("property float x");
                writer.WriteLine("property float y");
                writer.WriteLine("property float z");
                writer.WriteLine("property uchar red");
                writer.WriteLine("property uchar green");
                writer.WriteLine("property uchar blue");

                // Add motion vector properties if provided
                if (hasMotionVectors)
                {
                    writer.WriteLine("property float vx");
                    writer.WriteLine("property float vy");
                    writer.WriteLine("property float vz");
                }

                writer.WriteLine("end_header");

                // Write vertex data as text
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    Color32 c = colors != null && i < colors.Length ? colors[i] : new Color32(255, 255, 255, 255);

                    // Write position and color
                    writer.Write($"{v.x} {v.y} {v.z} {c.r} {c.g} {c.b}");

                    // Write motion vector if available
                    if (hasMotionVectors)
                    {
                        Vector3 motion = motionVectors[i];
                        writer.Write($" {motion.x} {motion.y} {motion.z}");
                    }

                    writer.WriteLine();
                }
            }

            long fileSize = new FileInfo(filePath).Length;
            Debug.Log($"Successfully exported {vertices.Length} points {(hasMotionVectors ? "with motion vectors " : "")}to ASCII PLY: {filePath} ({fileSize / 1024.0:F2} KB)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to export ASCII PLY: {ex.Message}");
        }
    }

    /// <summary>
    /// Export mesh to PLY format with motion vectors (binary little-endian)
    /// </summary>
    /// <param name="mesh">The mesh to export</param>
    /// <param name="motionVectors">Motion vectors for each vertex (vx, vy, vz)</param>
    /// <param name="filePath">Output file path</param>
    public static void ExportToPLY(Mesh mesh, Vector3[] motionVectors, string filePath)
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

            // Validate motion vectors
            bool hasMotionVectors = motionVectors != null && motionVectors.Length == vertices.Length;
            if (motionVectors != null && motionVectors.Length != vertices.Length)
            {
                Debug.LogWarning($"Motion vector count ({motionVectors.Length}) does not match vertex count ({vertices.Length}). Motion vectors will not be exported.");
                hasMotionVectors = false;
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
                    "property uchar blue\n";

                // Add motion vector properties if provided
                if (hasMotionVectors)
                {
                    header += "property float vx\n" +
                              "property float vy\n" +
                              "property float vz\n";
                }

                header += "end_header\n";

                byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
                bw.Write(headerBytes);

                // Write vertex data in binary
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    Color32 c = colors != null && i < colors.Length ? colors[i] : new Color32(255, 255, 255, 255);

                    // Write position
                    bw.Write(v.x);  // float (4 bytes)
                    bw.Write(v.y);  // float (4 bytes)
                    bw.Write(v.z);  // float (4 bytes)

                    // Write color
                    bw.Write(c.r);  // uchar (1 byte)
                    bw.Write(c.g);  // uchar (1 byte)
                    bw.Write(c.b);  // uchar (1 byte)

                    // Write motion vector if available
                    if (hasMotionVectors)
                    {
                        Vector3 motion = motionVectors[i];
                        bw.Write(motion.x);  // float (4 bytes)
                        bw.Write(motion.y);  // float (4 bytes)
                        bw.Write(motion.z);  // float (4 bytes)
                    }
                }
            }

            long fileSize = new FileInfo(filePath).Length;
            int bytesPerVertex = hasMotionVectors ? 27 : 15;
            Debug.Log($"Successfully exported {vertices.Length} points {(hasMotionVectors ? "with motion vectors " : "")}to: {filePath} ({fileSize / 1024.0:F2} KB, {bytesPerVertex} bytes/vertex)");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to export PLY with motion vectors: {ex.Message}");
        }
    }
}
