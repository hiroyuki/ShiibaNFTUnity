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
  private byte[] _latestRecordBytes; // Store raw binary data for GPU processing
  public ushort[] GetLatestDepthValues() => _latestDepthValues;
  public byte[] GetLatestRecordBytes() => _latestRecordBytes;

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
      // Binary GPU processor - store raw bytes
      _latestRecordBytes = recordBytes;
      _latestDepthValues = null;
      return true;
    }
    else
    {
      // Standard processor - convert to ushort array
      if (imageSize % 2 == 0)
      {
        int pixelCount = imageSize / 2;
        _latestDepthValues = new ushort[pixelCount];
        Buffer.BlockCopy(recordBytes, metadataSize, _latestDepthValues, 0, imageSize);
        _latestRecordBytes = null;
        return true;
      }
      else
      {
        Debug.LogWarning("Image data is not aligned for 16-bit conversion.");
        _latestDepthValues = null;
        _latestRecordBytes = null;
        return false;
      }
    }
  }

  // Legacy method for compatibility - detects usage pattern
  public override bool ParseNextRecord()
  {
    // For now, default to the more compatible approach that does both
    // Can be optimized later based on usage detection
    int metadataSize = sensorHeader.MetadataSize;
    int imageSize = sensorHeader.ImageSize;
    int recordSize = metadataSize + imageSize;

    byte[] recordBytes = reader.ReadBytes(recordSize);
    if (recordBytes.Length != recordSize)
    {
      Debug.LogWarning("Record size does not match expected size");
      return false;
    }
    
    // Store raw record bytes for binary GPU processing
    _latestRecordBytes = recordBytes;
    
    CurrentTimestamp = BitConverter.ToUInt64(recordBytes, 0);

    // Convert to ushort array for standard processing
    if (imageSize % 2 == 0)
    {
      int pixelCount = imageSize / 2;
      _latestDepthValues = new ushort[pixelCount];
      Buffer.BlockCopy(recordBytes, metadataSize, _latestDepthValues, 0, imageSize);
    }
    else
    {
      Debug.LogWarning("Image data is not aligned for 16-bit conversion.");
      _latestDepthValues = null;
    }
    
    return _latestDepthValues != null;
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