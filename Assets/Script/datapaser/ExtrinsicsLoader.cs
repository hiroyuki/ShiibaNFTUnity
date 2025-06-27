using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YamlDotNet.Serialization;
using UnityEngine.UIElements;

public class ExtrinsicsLoader
{
    private ExtrinsicsRoot extrinsics;
    private string extrinsicsYamlPath;
    
    public bool IsLoaded => extrinsics != null;

    public ExtrinsicsLoader(string extrinsicsYamlPath)
    {
        this.extrinsicsYamlPath = extrinsicsYamlPath;
        LoadExtrinsics();
    }

    public float? GetDepthScaleFactor(string targetSerialNumber)
    {
        var device = extrinsics.devices.FirstOrDefault(d => d.serialNumber == targetSerialNumber);
        if (device == null)
        {
            Debug.LogWarning("指定された serial number のデバイスが見つかりません: " + targetSerialNumber);
            return null;
        }

        return device.depthScaleFactor;
    }

    public bool TryGetGlobalTransform(string serial, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        var device = extrinsics.devices.FirstOrDefault(d => d.serialNumber == serial);
        if (device == null) return false;

        var t = device.global_t_colorCamera;
        var q = device.global_q_colorCamera;
        if (t == null || q == null || t.Count != 3 || q.Count != 4) return false;


        // ✅ Xのみ反転。Zは反転しない
        position = new Vector3(t[0], t[1], t[2]);
        rotation = new Quaternion(q[0], q[1], q[2], q[3]);

        return true;
    }

    public bool TryGetDepthToColorTransform(string serial, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        var device = extrinsics.devices.FirstOrDefault(d => d.serialNumber == serial);
        if (device == null) return false;

        var t = device.colorCamera_t_depthCamera;
        var q = device.colorCamera_q_depthCamera;
        if (t == null || q == null || t.Count != 3 || q.Count != 4) return false;

        position = new Vector3(t[0], t[1], t[2]);
        rotation = new Quaternion(q[0], q[1], q[2], q[3]);

        return true;
    }

    private void LoadExtrinsics()
    {
        if (!File.Exists(extrinsicsYamlPath))
        {
            Debug.LogError("extrinsics.yaml が見つかりません: " + extrinsicsYamlPath);
            return;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        using var reader = new StreamReader(extrinsicsYamlPath);
        extrinsics = deserializer.Deserialize<ExtrinsicsRoot>(reader);
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

    public List<float> colorCamera_t_depthCamera { get; set; }
    public List<float> colorCamera_q_depthCamera { get; set; } 
}