using System.IO;
using UnityEngine;

/// <summary>
/// Utility class for importing PLY format files to Unity meshes
/// </summary>
public static class PlyImporter
{
    /// <summary>
    /// Import mesh from PLY format (binary little-endian)
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <returns>Imported mesh or null if failed</returns>
    public static Mesh ImportFromPLY(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"PLY file not found: {filePath}");
            return null;
        }

        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                // Parse header
                string line;
                int vertexCount = 0;
                bool isBinaryFormat = false;
                bool hasMotionVectors = false;
                bool headerComplete = false;

                // Read header line by line
                while (!headerComplete)
                {
                    line = ReadAsciiLine(br);
                    if (line == null) break;

                    if (line.StartsWith("format"))
                    {
                        if (line.Contains("binary_little_endian"))
                        {
                            isBinaryFormat = true;
                        }
                        else
                        {
                            Debug.LogError($"Unsupported PLY format: {line}. Only binary_little_endian is supported.");
                            return null;
                        }
                    }
                    else if (line.StartsWith("element vertex"))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length >= 3)
                        {
                            vertexCount = int.Parse(parts[2]);
                        }
                    }
                    else if (line.StartsWith("property"))
                    {
                        // Detect motion vector properties
                        if (line.Contains("float vx") || line.Contains("float vy") || line.Contains("float vz"))
                        {
                            hasMotionVectors = true;
                        }
                    }
                    else if (line.StartsWith("end_header"))
                    {
                        headerComplete = true;
                    }
                }

                if (!isBinaryFormat || vertexCount == 0)
                {
                    Debug.LogError($"Invalid PLY header: vertexCount={vertexCount}, isBinary={isBinaryFormat}");
                    return null;
                }

                // Read binary vertex data
                Vector3[] vertices = new Vector3[vertexCount];
                Color32[] colors = new Color32[vertexCount];
                Vector3[] motionVectors = hasMotionVectors ? new Vector3[vertexCount] : null;

                for (int i = 0; i < vertexCount; i++)
                {
                    // Read position (3 floats)
                    float x = br.ReadSingle();
                    float y = br.ReadSingle();
                    float z = br.ReadSingle();
                    vertices[i] = new Vector3(x, y, z);

                    // Read color (3 bytes)
                    byte r = br.ReadByte();
                    byte g = br.ReadByte();
                    byte b = br.ReadByte();
                    colors[i] = new Color32(r, g, b, 255);

                    // Read motion vector if present (3 floats)
                    if (hasMotionVectors)
                    {
                        float vx = br.ReadSingle();
                        float vy = br.ReadSingle();
                        float vz = br.ReadSingle();
                        motionVectors[i] = new Vector3(vx, vy, vz);
                    }
                }

                // Create mesh
                Mesh mesh = new Mesh();
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Support large meshes
                mesh.vertices = vertices;
                mesh.colors32 = colors;

                // Store motion vectors in UV1 channel if present
                if (hasMotionVectors && motionVectors != null)
                {
                    mesh.SetUVs(1, motionVectors);
                }

                // Create indices for point topology
                int[] indices = new int[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    indices[i] = i;
                }
                mesh.SetIndices(indices, MeshTopology.Points, 0);
                mesh.RecalculateBounds();

                Debug.Log($"Successfully imported {vertexCount} points {(hasMotionVectors ? "with motion vectors " : "")}from: {filePath}");
                return mesh;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to import PLY: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Read a single ASCII line from binary reader (until \n)
    /// </summary>
    private static string ReadAsciiLine(BinaryReader br)
    {
        string line = "";
        while (true)
        {
            if (br.BaseStream.Position >= br.BaseStream.Length)
                return null;

            char c = (char)br.ReadByte();
            if (c == '\n')
                break;
            if (c != '\r')
                line += c;
        }
        return line;
    }
}
