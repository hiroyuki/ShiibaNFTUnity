using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System;


public class RcstSensorDataParser : ISensorDataParser, IDisposable
{
  private readonly BinaryReader _reader;
  public SensorHeader _sensorHeader { get; private set; }
  public string FormatIdentifier => "RCST";


  public RcstSensorDataParser(BinaryReader reader)
  {
    _reader = reader;
  }

  ~RcstSensorDataParser()
  {
    Dispose();
  }

  public void Dispose()
  {
    _reader?.Dispose();
  }
  
  public void ParseHeader()
  {
    uint HeaderSize = _reader.ReadUInt32();
    byte[] yamlBytes = _reader.ReadBytes((int)HeaderSize);
    string HeaderText = Encoding.UTF8.GetString(yamlBytes);


    var deserializer = new DeserializerBuilder()
        .Build();


    _sensorHeader = deserializer.Deserialize<SensorHeader>(HeaderText);

    foreach (var field in _sensorHeader.record_format)
    {
      Debug.Log($"Field: {field.name}, Type: {field.type}, Count: {field.count}");
    }
  }

  public void ParseNextRecord()
  {
    // 推定: 各レコードの合計バイト数（固定）
    int metadataSize =  _sensorHeader.MetadataSize; //48 u64 * 4 + f32 * 4
    int imageSize = _sensorHeader.ImageSize; //737280 ヘッダー記載の count（u8想定）
    int recordSize = metadataSize + imageSize;

    byte[] recordBytes = _reader.ReadBytes(recordSize);
    if (recordBytes.Length != recordSize)
    {
      Debug.LogWarning("Record size does not match expected size");
      return;
    }

    // image データの切り出し
    byte[] imageBytes = new byte[imageSize];
    Array.Copy(recordBytes, metadataSize, imageBytes, 0, imageSize);

    // u16 として解釈（画像が 2バイト / pixel の場合）
    if (imageBytes.Length % 2 == 0)
    {
      int pixelCount = imageBytes.Length / 2;
      ushort[] depthValues = new ushort[pixelCount];
      for (int i = 0; i < pixelCount; i++)
      {
        depthValues[i] = BitConverter.ToUInt16(imageBytes, i * 2);
      }

      Debug.Log($"First depth values: {depthValues[0]}, {depthValues[1]}, {depthValues[2]}");
    }
    else
    {
      Debug.Log("Image data is not aligned for 16-bit conversion.");
    }
  }
}

