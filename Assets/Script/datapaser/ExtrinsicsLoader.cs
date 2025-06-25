using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;

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

    public static bool TryGetGlobalTransform(string path, string serial, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!File.Exists(path)) return false;

        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        using var reader = new StreamReader(path);
        var yaml = deserializer.Deserialize<ExtrinsicsRoot>(reader);

        var device = yaml.devices.FirstOrDefault(d => d.serialNumber == serial);
        if (device == null) return false;

        var t = device.global_t_colorCamera;
        var q = device.global_q_colorCamera;
        if (t == null || q == null || t.Count != 3 || q.Count != 4) return false;


        // ✅ Xのみ反転。Zは反転しない
        position = new Vector3(-t[0], t[1], t[2]);

        // ✅ X軸180度回転で上下補正。Z軸回転は不要
        var scannedQuat = new Quaternion(q[0], q[1], q[2], q[3]);
        var coordinateFix = Quaternion.Euler(180f, 0f, 0f);
        rotation = coordinateFix * scannedQuat;

        return true;
    }
}

public class ExtrinsicsRoot
{
    public List<ExtrinsicsDevice> devices { get; set; }
}

public class ExtrinsicsDevice
{
    public string serialNumber { get; set; }
    public float depthScaleFactor { get; set; }
    public List<float> global_t_colorCamera { get; set; }
    public List<float> global_q_colorCamera { get; set; }
}