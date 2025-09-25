using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using YamlDotNet.Serialization;

public class RcsvSensorDataParser : AbstractSensorDataParser
{
    public override string FormatIdentifier => "RCSV";
    private ulong IndexOffset { get; set; }
    private ulong Unused { get; set; }
    private uint HeaderSize { get; set; }
    private string HeaderText { get; set; }
    private byte[] CurrentColorBytes { get; set; }
    private Color32[] CurrentColorPixels { get; set; }

    private Texture2D _decodedTexture;
    
    // Direct texture access for GPU processing (avoids Color32[] conversion)
    public Texture2D GetLatestColorTexture() => _decodedTexture;

    public Color32[] GetLatestColorPixels() => CurrentColorPixels;

    public RcsvSensorDataParser(BinaryReader reader, string deviceName = "Unknown Device") : base(reader, deviceName) { }
    ~RcsvSensorDataParser() => Dispose();
    public override void Dispose() => reader?.Dispose();

    public override void ParseHeader()
    {
        IndexOffset = reader.ReadUInt64();
        Unused = reader.ReadUInt64();
        HeaderSize = reader.ReadUInt32();
        byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
        HeaderText = Encoding.UTF8.GetString(yamlBytes);

        // Debug.Log("RCSV Header YAML:");
        // Debug.Log($"Header Size: {HeaderSize} bytes");
        // Debug.Log($"YAML Content:\n{HeaderText}");

        var deserializer = new DeserializerBuilder().Build();
        sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);
    }

    // GPU-optimized parsing with mode selection
    public override bool ParseNextRecord(bool optimizeForGPU)
    {
        var colorField = sensorHeader.record_format.FirstOrDefault(f => f.name == "image");
        if (colorField == null)
        {
            Debug.LogError("record_format に 'image' フィールドがありません");
            return false;
        }

        int sizeTypeBytes = colorField.type switch
        {
            "u16" => 2,
            "u32" => 4,
            _ => throw new InvalidDataException($"Unsupported size type: {colorField.type}")
        };

        int metadataSize = sensorHeader.MetadataSize;
        byte[] headerAndSize = reader.ReadBytes(metadataSize + sizeTypeBytes);
        if (headerAndSize.Length < metadataSize + sizeTypeBytes) return false;

        CurrentTimestamp = BitConverter.ToUInt64(headerAndSize, 0);

        int imageSize = sizeTypeBytes == 2
            ? BitConverter.ToUInt16(headerAndSize, metadataSize)
            : BitConverter.ToInt32(headerAndSize, metadataSize);
        CurrentColorBytes = reader.ReadBytes(imageSize);
        if (CurrentColorBytes.Length != imageSize) return false;

        if (_decodedTexture == null)
            _decodedTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        if (_decodedTexture.LoadImage(CurrentColorBytes))
        {
            if (optimizeForGPU)
            {
                // Binary GPU processor - texture only, skip CPU conversion
                CurrentColorPixels = null;
            }
            else
            {
                // Standard processor - convert to Color32 array
                CurrentColorPixels = _decodedTexture.GetPixels32();
            }
        }
        else
        {
            Debug.LogWarning("カラー画像の読み込みに失敗しました");
            return false;
        }

        return true;
    }

    // Legacy method for compatibility
    public override bool ParseNextRecord()
    {
        // Default to standard processing for backward compatibility
        return ParseNextRecord(optimizeForGPU: false);
    }

    public override bool PeekNextTimestamp(out ulong timestamp)
    {
        try
        {
            long originalPos = reader.BaseStream.Position;

            var colorField = sensorHeader.record_format.FirstOrDefault(f => f.name == "image");
            if (colorField == null)
            {
                timestamp = 0;
                return false;
            }

            int sizeTypeBytes = colorField.type switch
            {
                "u16" => 2,
                "u32" => 4,
                _ => throw new InvalidDataException($"Unsupported size type: {colorField.type}")
            };

            int metadataSize = sensorHeader.MetadataSize;
            byte[] header = reader.ReadBytes(metadataSize);
            if (header.Length != metadataSize)
            {
                timestamp = 0;
                reader.BaseStream.Position = originalPos;
                return false;
            }

            timestamp = BitConverter.ToUInt64(header, 0);
            reader.BaseStream.Position = originalPos;
            return true;
        }
        catch
        {
            timestamp = 0;
            return false;
        }
    }
    
    // Fast skip method for timeline seeking - only reads header, skips JPEG data
    public override bool SkipCurrentRecord()
    {
        try
        {
            var colorField = sensorHeader.record_format.FirstOrDefault(f => f.name == "image");
            if (colorField == null) return false;

            int sizeTypeBytes = colorField.type switch
            {
                "u16" => 2,
                "u32" => 4,
                _ => throw new InvalidDataException($"Unsupported size type: {colorField.type}")
            };

            int metadataSize = sensorHeader.MetadataSize;
            
            // Read metadata + size field, skip JPEG data
            byte[] headerAndSize = reader.ReadBytes(metadataSize + sizeTypeBytes);
            if (headerAndSize.Length < metadataSize + sizeTypeBytes) return false;

            // Update timestamp
            CurrentTimestamp = BitConverter.ToUInt64(headerAndSize, 0);

            // Get JPEG size and skip it
            int imageSize = sizeTypeBytes == 2
                ? BitConverter.ToUInt16(headerAndSize, metadataSize)
                : BitConverter.ToInt32(headerAndSize, metadataSize);

            // Skip the JPEG data without reading/decompressing it
            reader.BaseStream.Seek(imageSize, SeekOrigin.Current);
            
            return true;
        }
        catch
        {
            return false;
        }
    }
}
