using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using YamlDotNet.Serialization;

public class MultiCameraPointCloudManager : MonoBehaviour
{
    [SerializeField]
    private string rootDirectory; // datasetを含むディレクトリ

    private List<GameObject> parserObjects = new();

    void Start()
    {
        string datasetPath = Path.Combine(rootDirectory, "dataset");
        if (!Directory.Exists(datasetPath))
        {
            Debug.LogError($"dataset ディレクトリが見つかりません: {datasetPath}");
            return;
        }

        string hostDir = Directory.GetDirectories(datasetPath).FirstOrDefault();
        if (hostDir == null)
        {
            Debug.LogError("ホストディレクトリが dataset 配下に見つかりません");
            return;
        }

        string hostInfoPath = Path.Combine(hostDir, "hostinfo.yaml");
        if (!File.Exists(hostInfoPath))
        {
            Debug.LogError("hostinfo.yaml が見つかりません: " + hostInfoPath);
            return;
        }

        HostInfo hostInfo = YamlLoader.Load<HostInfo>(hostInfoPath);
        foreach (var device in hostInfo.devices)
        {
            // deviceType_serialNumber → 例: FemtoBolt_CL8F25300C6
            string deviceDirName = $"{device.deviceType}_{device.serialNumber}";
// 
             // (deviceDirName != "FemtoBolt_CL8F25300HJ" && deviceDirName != "FemtoBolt_CL8F25300EG")
            // (deviceDirName != "FemtoBolt_CL8F25300HJ" )
            // if (deviceDirName != "FemtoBolt_CL8F25300C6" )
            //      continue;//center , right

            string deviceDir = Path.Combine(hostDir, deviceDirName);
            string depthPath = Path.Combine(deviceDir, "camera_depth");
            string colorPath = Path.Combine(deviceDir, "camera_color");

            if (File.Exists(depthPath) && File.Exists(colorPath))
            {
                GameObject parserObj = new GameObject("BinaryDataParser_" + deviceDirName);
                var parser = parserObj.AddComponent<BinaryDataParser>();
                parserObj.transform.parent = this.transform;

                SetPrivateField(parser, "dir", rootDirectory);                    // 例: /Volumes/MyDisk/CaptureSession/
                SetPrivateField(parser, "hostname", Path.GetFileName(hostDir));  // 例: PAN-SHI
                SetPrivateField(parser, "deviceName", deviceDirName);            // 例: FemtoBolt_CL8F25300C6
                parserObjects.Add(parserObj);
            }
        }

        Debug.Log($"BinaryDataParser を {parserObjects.Count} 個作成しました");
    }

    private void SetPrivateField(BinaryDataParser instance, string fieldName, object value)
    {
        var field = typeof(BinaryDataParser).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
            field.SetValue(instance, value);
        else
            Debug.LogWarning($"フィールド {fieldName} が見つかりません");
    }
}
