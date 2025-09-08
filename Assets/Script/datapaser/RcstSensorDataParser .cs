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

  public RcstSensorDataParser(BinaryReader reader) : base(reader) { }

  ~RcstSensorDataParser() => Dispose();

  public override void Dispose() => reader?.Dispose();

  public override void ParseHeader()
  {
    uint HeaderSize = reader.ReadUInt32();
    byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
    string HeaderText = Encoding.UTF8.GetString(yamlBytes);
    var deserializer = new DeserializerBuilder().Build();
    sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);

    Debug.Log("RCST Header YAML:");
    Debug.Log($"Header Size: {HeaderSize} bytes");
    Debug.Log($"YAML Content:\n{HeaderText}");

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
      Buffer.BlockCopy(imageBytes, 0, _latestDepthValues, 0, imageBytes.Length);

    }
    else
    {
      Debug.Log("Image data is not aligned for 16-bit conversion.");
    }
    return true;
  }
  
  public override bool PeekNextTimestamp(out ulong timestamp)
  {
      try
      {
          long originalPos = reader.BaseStream.Position;

          int metadataSize = sensorHeader.MetadataSize;
          byte[] metadataBytes = reader.ReadBytes(metadataSize);
          if (metadataBytes.Length < 8)
          {
              timestamp = 0;
              reader.BaseStream.Position = originalPos;
              return false;
          }

          timestamp = BitConverter.ToUInt64(metadataBytes, 0);

          reader.BaseStream.Position = originalPos;
          return true;
      }
      catch
      {
          timestamp = 0;
          return false;
      }
  }
  
  // Fast skip method for timeline seeking - only reads header, skips image data
  public bool SkipCurrentRecord()
  {
      try
      {
          int metadataSize = sensorHeader.MetadataSize;
          int imageSize = sensorHeader.ImageSize;
          int recordSize = metadataSize + imageSize;
          
          // Read only metadata for timestamp, skip image data
          byte[] metadataBytes = reader.ReadBytes(metadataSize);
          if (metadataBytes.Length != metadataSize)
          {
              return false;
          }
          
          // Update timestamp but skip image processing
          CurrentTimestamp = BitConverter.ToUInt64(metadataBytes, 0);
          
          // Skip the image data portion
          reader.BaseStream.Seek(imageSize, SeekOrigin.Current);
          
          return true;
      }
      catch
      {
          return false;
      }
  }
}