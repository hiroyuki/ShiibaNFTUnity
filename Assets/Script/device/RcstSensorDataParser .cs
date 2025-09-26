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
  private uint[] _latestDepthUints; // Store depth data as uint[] for GPU processing
  public ushort[] GetLatestDepthValues() => _latestDepthValues;
  public uint[] GetLatestDepthUints() => _latestDepthUints;

  public RcstSensorDataParser(BinaryReader reader, string deviceName = "Unknown Device") : base(reader, deviceName) { }

  ~RcstSensorDataParser() => Dispose();

  public override void Dispose() => reader?.Dispose();

  public override void ParseHeader()
  {
    uint HeaderSize = reader.ReadUInt32();
    byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
    string HeaderText = Encoding.UTF8.GetString(yamlBytes);
    var deserializer = new DeserializerBuilder().Build();
    sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);

    // Debug.Log("RCST Header YAML:");
    // Debug.Log($"Header Size: {HeaderSize} bytes");
    // Debug.Log($"YAML Content:\n{HeaderText}");

  }

  // Helper method to convert depth bytes to uint array
  private uint[] ConvertDepthBytesToUints(byte[] recordBytes, int metadataSize, int imageSize)
  {
    int depthPixelCount = imageSize / 2; // 2 bytes per ushort
    uint[] depthUints = new uint[depthPixelCount];

    for (int i = 0; i < depthPixelCount; i++)
    {
      int byteIndex = metadataSize + i * 2;
      if (byteIndex + 1 < recordBytes.Length)
      {
        // Read ushort as little-endian and store as uint
        ushort depthValue = (ushort)(recordBytes[byteIndex] | (recordBytes[byteIndex + 1] << 8));
        depthUints[i] = depthValue;
      }
    }

    return depthUints;
  }

  // GPU-optimized parsing with mode selection
  public override bool ParseNextRecord(bool optimizeForGPU)
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

    if (optimizeForGPU)
    {
      // GPU processor - convert depth data to uint array directly
      if (imageSize % 2 == 0)
      {
        _latestDepthUints = ConvertDepthBytesToUints(recordBytes, metadataSize, imageSize);
        _latestDepthValues = null;
        return true;
      }
      else
      {
        Debug.LogWarning("Image data is not aligned for 16-bit conversion.");
        _latestDepthUints = null;
        _latestDepthValues = null;
        return false;
      }
    }
    else
    {
      // Standard processor - convert to ushort array
      if (imageSize % 2 == 0)
      {
        int pixelCount = imageSize / 2;
        _latestDepthValues = new ushort[pixelCount];
        Buffer.BlockCopy(recordBytes, metadataSize, _latestDepthValues, 0, imageSize);
        _latestDepthUints = null;
        return true;
      }
      else
      {
        Debug.LogWarning("Image data is not aligned for 16-bit conversion.");
        _latestDepthValues = null;
        _latestDepthUints = null;
        return false;
      }
    }
  }
  
  public override bool PeekNextTimestamp(out ulong timestamp)
  {
      try
      {
          long originalPos = reader.BaseStream.Position;
          Debug.Log("Peeking next timestamp at position: " + originalPos + " for device: " + deviceName);
          int metadataSize = sensorHeader.MetadataSize;
          byte[] metadataBytes = reader.ReadBytes(metadataSize);
          if (metadataBytes.Length != metadataSize)
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
  public override bool SkipCurrentRecord()
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