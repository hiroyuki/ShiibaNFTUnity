using System;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using YamlDotNet.Serialization;

public class RcsvSensorDataParser : AbstractSensorDataParser
{
    public override string FormatIdentifier => "RCSV";
    public ulong IndexOffset { get; private set; }
    public ulong Unused { get; private set; }
    public uint HeaderSize { get; private set; }
    public string HeaderText { get; private set; }
    public byte[] CurrentColorBytes { get; private set; }
    public Color32[] CurrentColorPixels { get; private set; }

    private Texture2D _decodedTexture;

    public RcsvSensorDataParser(BinaryReader reader) : base(reader) { }
    ~RcsvSensorDataParser() => Dispose();
    public override void Dispose() => reader?.Dispose();

    public override void ParseHeader()
    {
        IndexOffset = reader.ReadUInt64();
        Unused = reader.ReadUInt64();
        HeaderSize = reader.ReadUInt32();
        byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
        HeaderText = Encoding.UTF8.GetString(yamlBytes);

        Debug.Log("RCSV Header YAML:");
        Debug.Log($"Header Size: {HeaderSize} bytes");
        Debug.Log($"YAML Content:\n{HeaderText}");

        var deserializer = new DeserializerBuilder().Build();
        sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);
    }

    public override bool ParseNextRecord()
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
            CurrentColorPixels = _decodedTexture.GetPixels32();
        }
        else
        {
            Debug.LogWarning("カラー画像の読み込みに失敗しました");
            return false;
        }

        return true;
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
            if (header.Length < 8)
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
    public bool SkipCurrentRecord()
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
