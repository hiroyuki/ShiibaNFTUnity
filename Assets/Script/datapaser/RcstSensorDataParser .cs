using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;
using System;

public class RcstSensorDataParser : AbstractSensorDataParser
{
  public override string FormatIdentifier => "RCST";
  private ushort[] _latestDepthValues;
  public ushort[] GetLatestDepthValues() => _latestDepthValues;

  public RcstSensorDataParser(BinaryReader reader) : base(reader) {}

  ~RcstSensorDataParser() => Dispose();

  public override void Dispose() => reader?.Dispose();

  public override void ParseHeader()
  {
    uint HeaderSize = reader.ReadUInt32();
    byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
    string HeaderText = Encoding.UTF8.GetString(yamlBytes);
    var deserializer = new DeserializerBuilder().Build();
    sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);
  }

  public override bool ParseNextRecord()
  {
    int metadataSize = sensorHeader.MetadataSize;
    int imageSize = sensorHeader.ImageSize;
    int recordSize = metadataSize + imageSize;

    byte[] recordBytes = reader.ReadBytes(recordSize);
    if (recordBytes.Length != recordSize)
    {
      Debug.LogWarning("Record size does not match expected size");
      return false;
    }

    CurrentTimestamp = BitConverter.ToUInt64(recordBytes, 0);
    byte[] imageBytes = new byte[imageSize];
    Array.Copy(recordBytes, metadataSize, imageBytes, 0, imageSize);

    if (imageBytes.Length % 2 == 0)
    {
      int pixelCount = imageBytes.Length / 2;
      _latestDepthValues = new ushort[pixelCount];
      for (int i = 0; i < pixelCount; i++)
      {
        _latestDepthValues[i] = BitConverter.ToUInt16(imageBytes, i * 2);
      }
    }
    else
    {
      Debug.Log("Image data is not aligned for 16-bit conversion.");
    }
    return true;
  }
}