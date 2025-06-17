using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
// using YamlDotNet.Serialization;
// using YamlDotNet.Serialization.NamingConventions;


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
        string yaml = @"
record_format:
  - name: image
    type: u8
    count: 737280
  - name: timestamp_ns
    type: u64
    count: 1";

        // var deserializer = new DeserializerBuilder()
        //     .WithNamingConvention(CamelCaseNamingConvention.Instance)
        //     .Build();


        // var result = deserializer.Deserialize<SensorHeader>(yaml);

        // foreach (var field in result.RecordFormat)
        // {
        //     Debug.Log($"Field: {field.name}, Type: {field.type}, Count: {field.count}");
        // }
    }

    public void ParseNextRecord(BinaryReader reader)
    {
        // 固定サイズレコードを読み込む処理（省略可能）
    }
}