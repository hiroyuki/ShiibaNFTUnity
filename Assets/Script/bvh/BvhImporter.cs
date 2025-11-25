using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Utility class for importing BVH (BioVision Hierarchical) motion capture files
/// </summary>
public static class BvhImporter
{
    /// <summary>
    /// Import BVH file and parse hierarchy and motion data
    /// </summary>
    /// <param name="filePath">Path to BVH file</param>
    /// <returns>Parsed BVH data or null if failed</returns>
    public static BvhData ImportFromBVH(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"BVH file not found: {filePath}");
            return null;
        }

        try
        {
            string[] lines = File.ReadAllLines(filePath);
            int currentLine = 0;

            BvhData bvhData = new BvhData();

            // Parse hierarchy section
            if (!FindSection(lines, ref currentLine, "HIERARCHY"))
            {
                Debug.LogError("HIERARCHY section not found in BVH file");
                return null;
            }

            bvhData.RootJoint = ParseHierarchy(lines, ref currentLine);
            if (bvhData.RootJoint == null)
            {
                Debug.LogError("Failed to parse BVH hierarchy");
                return null;
            }

            // Parse motion section
            if (!FindSection(lines, ref currentLine, "MOTION"))
            {
                Debug.LogError("MOTION section not found in BVH file");
                return null;
            }

            if (!ParseMotion(lines, ref currentLine, bvhData))
            {
                Debug.LogError("Failed to parse BVH motion data");
                return null;
            }

            Debug.Log($"Successfully imported BVH file: {filePath}\n{bvhData.GetSummary()}");
            return bvhData;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to import BVH: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Find a section marker in the file
    /// </summary>
    private static bool FindSection(string[] lines, ref int currentLine, string sectionName)
    {
        while (currentLine < lines.Length)
        {
            string line = lines[currentLine].Trim();
            if (line.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
            {
                currentLine++;
                return true;
            }
            currentLine++;
        }
        return false;
    }

    /// <summary>
    /// Parse the HIERARCHY section
    /// </summary>
    private static BvhJoint ParseHierarchy(string[] lines, ref int currentLine)
    {
        BvhJoint rootJoint = null;

        while (currentLine < lines.Length)
        {
            string line = lines[currentLine].Trim();

            if (line.StartsWith("ROOT", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    Debug.LogError($"Invalid ROOT declaration at line {currentLine}");
                    return null;
                }

                rootJoint = new BvhJoint { Name = parts[1] };
                currentLine++;
                ParseJoint(lines, ref currentLine, rootJoint);
                break;
            }
            currentLine++;
        }

        return rootJoint;
    }

    /// <summary>
    /// Parse a joint and its children recursively
    /// </summary>
    private static void ParseJoint(string[] lines, ref int currentLine, BvhJoint joint)
    {
        bool inBraces = false;

        while (currentLine < lines.Length)
        {
            string line = lines[currentLine].Trim();
            currentLine++;

            if (line == "{")
            {
                inBraces = true;
                continue;
            }
            else if (line == "}")
            {
                return; // End of this joint
            }

            if (!inBraces) continue;

            if (line.StartsWith("OFFSET", StringComparison.OrdinalIgnoreCase))
            {
                joint.Offset = ParseVector3(line);
            }
            else if (line.StartsWith("CHANNELS", StringComparison.OrdinalIgnoreCase))
            {
                ParseChannels(line, joint);
            }
            else if (line.StartsWith("JOINT", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    BvhJoint childJoint = new BvhJoint
                    {
                        Name = parts[1],
                        Parent = joint
                    };
                    joint.Children.Add(childJoint);
                    ParseJoint(lines, ref currentLine, childJoint);
                }
            }
            else if (line.StartsWith("End Site", StringComparison.OrdinalIgnoreCase))
            {
                BvhJoint endSite = new BvhJoint
                {
                    Name = joint.Name + "_End",
                    Parent = joint,
                    IsEndSite = true
                };
                joint.Children.Add(endSite);
                ParseJoint(lines, ref currentLine, endSite);
            }
        }
    }

    /// <summary>
    /// Parse OFFSET line to Vector3
    /// </summary>
    private static Vector3 ParseVector3(string line)
    {
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4)
        {
            float x = ParseFloat(parts[1]);
            float y = ParseFloat(parts[2]);
            float z = ParseFloat(parts[3]);
            return new Vector3(x, y, z);
        }
        return Vector3.zero;
    }

    /// <summary>
    /// Parse CHANNELS line
    /// </summary>
    private static void ParseChannels(string line, BvhJoint joint)
    {
        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        int channelCount = int.Parse(parts[1]);
        joint.Channels.Clear();

        for (int i = 0; i < channelCount && i + 2 < parts.Length; i++)
        {
            joint.Channels.Add(parts[i + 2]);
        }
    }

    /// <summary>
    /// Parse the MOTION section
    /// </summary>
    private static bool ParseMotion(string[] lines, ref int currentLine, BvhData bvhData)
    {
        // Parse frame count
        while (currentLine < lines.Length)
        {
            string line = lines[currentLine].Trim();
            currentLine++;

            if (line.StartsWith("Frames:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(':');
                if (parts.Length >= 2)
                {
                    bvhData.FrameCount = int.Parse(parts[1].Trim());
                }
            }
            else if (line.StartsWith("Frame Time:", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(':');
                if (parts.Length >= 2)
                {
                    bvhData.FrameTime = ParseFloat(parts[1].Trim());
                }
                break;
            }
        }

        if (bvhData.FrameCount <= 0)
        {
            Debug.LogError("Invalid frame count in BVH file");
            return false;
        }

        // Calculate expected channel count
        int expectedChannelCount = bvhData.RootJoint.GetTotalChannelCount();

        // Parse frame data
        bvhData.Frames = new float[bvhData.FrameCount][];
        int frameIndex = 0;

        while (currentLine < lines.Length && frameIndex < bvhData.FrameCount)
        {
            string line = lines[currentLine].Trim();
            currentLine++;

            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (values.Length != expectedChannelCount)
            {
                Debug.LogWarning($"Frame {frameIndex}: Expected {expectedChannelCount} channels, got {values.Length}");
            }

            float[] frameData = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                frameData[i] = ParseFloat(values[i]);
            }

            bvhData.Frames[frameIndex] = frameData;
            frameIndex++;
        }

        if (frameIndex != bvhData.FrameCount)
        {
            Debug.LogWarning($"Expected {bvhData.FrameCount} frames, parsed {frameIndex} frames");
            bvhData.FrameCount = frameIndex;
        }

        return true;
    }

    /// <summary>
    /// Parse float with culture-invariant format
    /// </summary>
    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Apply BVH motion data to a Unity Transform hierarchy
    /// Delegates to BvhData.ApplyFrameToTransforms() for consistent behavior
    /// </summary>
    /// <param name="bvhData">BVH data</param>
    /// <param name="rootTransform">Root transform of the character</param>
    /// <param name="frameIndex">Frame index to apply</param>
    public static void ApplyFrameToTransform(BvhData bvhData, Transform rootTransform, int frameIndex)
    {
        if (bvhData == null || rootTransform == null) return;
        if (frameIndex < 0 || frameIndex >= bvhData.FrameCount) return;

        float[] frameData = bvhData.GetFrame(frameIndex);
        if (frameData == null) return;

        // Delegate to BvhData's unified implementation
        BvhData.ApplyFrameToTransforms(bvhData.RootJoint, rootTransform, frameData);
    }
}
