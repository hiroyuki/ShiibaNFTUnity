using System.IO;
using System.Linq;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using UnityEngine;

public static class ExtrinsicsLoader
{
    public static float? GetDepthScaleFactor(string extrinsicsYamlPath, string targetSerialNumber)
    {
        if (!File.Exists(extrinsicsYamlPath))
        {
            Debug.LogError("extrinsics.yaml が見つかりません: " + extrinsicsYamlPath);
            return null;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        using var reader = new StreamReader(extrinsicsYamlPath);
        var yaml = deserializer.Deserialize<ExtrinsicsRoot>(reader);

        var device = yaml.devices.FirstOrDefault(d => d.serialNumber == targetSerialNumber);
        if (device == null)
        {
            Debug.LogWarning("指定された serial number のデバイスが見つかりません: " + targetSerialNumber);
            return null;
        }

        return device.depthScaleFactor;
    }
}

// PascalCase をそのまま扱う（YAMLと一致）
public class ExtrinsicsRoot
{
    public List<ExtrinsicsDevice> devices { get; set; }
}

public class ExtrinsicsDevice
{
    public string serialNumber { get; set; }
    public float depthScaleFactor { get; set; }
}
