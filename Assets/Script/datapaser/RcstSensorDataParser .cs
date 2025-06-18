using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;


public class RcstSensorDataParser : ISensorDataParser
{
  public uint HeaderSize { get; private set; }
  public string HeaderText { get; private set; }
  public string FormatIdentifier => "RCST";

  public void ParseHeader(BinaryReader reader)
  {
    HeaderSize = reader.ReadUInt32();
    byte[] yamlBytes = reader.ReadBytes((int)HeaderSize);
    HeaderText = Encoding.UTF8.GetString(yamlBytes);
    // Debug.Log($"Header Size: {HeaderSize}, Header Text: {HeaderText}");

    var deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();


    var result = deserializer.Deserialize<SensorHeader>(HeaderText);

    foreach (var field in result.RecordFormat)
    {
      Debug.Log($"Field: {field.Name}, Type: {field.Type}, Count: {field.Count}");
    }
  }

  public void ParseNextRecord(BinaryReader reader)
  {
    // 固定サイズレコードを読み込む処理（省略可能）
  }
}

public class SensorHeader
{
  [YamlMember(Alias = "record_format")]
  public List<RecordField> RecordFormat { get; set; }
    

    [YamlMember(Alias = "custom")]
    public CustomMetadata Custom { get; set; } 
}
public class RecordField
{
  [YamlMember(Alias = "name")]
  public string Name { get; set; }

  [YamlMember(Alias = "type")]
  public string Type { get; set; }

  [YamlMember(Alias = "count")]
  public int Count { get; set; }
}

public class CustomMetadata
{
    [YamlMember(Alias = "fps")]
    public int Fps { get; set; }

    [YamlMember(Alias = "format")]
    public string Format { get; set; }

    [YamlMember(Alias = "width")]
    public int Width { get; set; }

    [YamlMember(Alias = "height")]
    public int Height { get; set; }

    // その他必要なフィールド
}