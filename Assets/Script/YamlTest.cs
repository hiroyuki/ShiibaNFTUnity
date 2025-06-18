using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class YamlTest : MonoBehaviour
{
    void Start()
    {
        string yaml = @"
record_format:
  - name: image
    type: u8
    count: 737280
  - name: timestamp_ns
    type: u64
    count: 1
";

        // var deserializer = new DeserializerBuilder()
        //     .WithNamingConvention(CamelCaseNamingConvention.Instance)
        //     .Build();

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        SensorHeader header = deserializer.Deserialize<SensorHeader>(yaml);

        foreach (var field in header.RecordFormat)
        {
            Debug.Log($"Field: {field.Name}, Type: {field.Type}, Count: {field.Count}");
        }
    }
}