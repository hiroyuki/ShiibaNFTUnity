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

    public RcsvSensorDataParser(BinaryReader reader) : base(reader) {}

    ~RcsvSensorDataParser() => Dispose();
    public override void Dispose() => reader?.Dispose();

    public override void ParseHeader()
    {
        IndexOffset = reader.ReadUInt64();
        Unused = reader.ReadUInt64();
        HeaderSize = reader.ReadUInt32();
        byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
        HeaderText = Encoding.UTF8.GetString(yamlBytes);

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

        return true;
    }
}
