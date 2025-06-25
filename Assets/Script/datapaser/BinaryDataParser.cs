using System;
using System.IO;
using UnityEngine;

public class BinaryDataParser : MonoBehaviour
{
    [SerializeField] private string dir;
    [SerializeField] private string hostname;
    [SerializeField] private string deviceName;

    RcstSensorDataParser depthParser;
    RcsvSensorDataParser colorParser;

    [SerializeField] private float depthScaleFactor = 1000f;

    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private DepthMeshGenerator depthMeshGenerator;

    private int savedFrameCount = 0;
    private const int maxSavedFrames = 1;

    private Vector2[,] colorUndistortLUT;
    private Vector2[,] depthUndistortLUT;

    private bool firstFrameProcessed = false;

    void Start()
    {
        string devicePath = Path.Combine(dir, "dataset", hostname, deviceName);
        string depthFilePath = Path.Combine(devicePath, "camera_depth");
        string colorFilePath = Path.Combine(devicePath, "camera_color");

        if (!File.Exists(depthFilePath) || !File.Exists(colorFilePath))
        {
            Debug.LogError("指定されたファイルが存在しません: " + devicePath);
            return;
        }

        depthParser = (RcstSensorDataParser)SensorDataParserFactory.Create(depthFilePath);
        colorParser = (RcsvSensorDataParser)SensorDataParserFactory.Create(colorFilePath);

        string extrinsicsPath = Path.Combine(dir, "calibration", "extrinsics.yaml");
        string serial = deviceName.Split('_')[^1];

        float? loadedScale = ExtrinsicsLoader.GetDepthScaleFactor(extrinsicsPath, serial);
        if (loadedScale.HasValue)
        {
            depthScaleFactor = loadedScale.Value;
        }

        // --- DepthViewer 自動生成 ---
        string viewerName = $"DepthViewer_{deviceName}";
        depthViewer = new GameObject(viewerName);
        depthViewer.transform.SetParent(this.transform);

        if (ExtrinsicsLoader.TryGetGlobalTransform(extrinsicsPath, serial, out Vector3 pos, out Quaternion rot))
        {
            Debug.Log($"Applying global transform for {deviceName}: position = {pos}, rotation = {rot.eulerAngles}");
            depthViewer.transform.localPosition = -pos;
            depthViewer.transform.localRotation = Quaternion.Inverse(rot);

            Debug.Log($"Applied inverse transform for {deviceName} → position = {-(rot * pos)}, rotation = {Quaternion.Inverse(rot).eulerAngles}");
        }

        depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.AddComponent<MeshRenderer>();
        depthRenderer.material = new Material(Shader.Find("Unlit/VertexColor"));

        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        depthMeshGenerator = new DepthMeshGenerator();
        depthMeshGenerator.setup(depthParser.sensorHeader, depthScaleFactor);
        depthMeshGenerator.SetupColorIntrinsics(colorParser.sensorHeader);

        // colorUndistortLUT = UndistortHelper.BuildUndistortLUTFromHeader(colorParser.sensorHeader);
        // depthUndistortLUT = UndistortHelper.BuildUndistortLUTFromHeader(depthParser.sensorHeader);
    }

    void Update()
    {
        if (firstFrameProcessed) return;
        const long maxAllowableDeltaNs = 2_000;

        while (true)
        {
            bool hasDepthTs = depthParser.PeekNextTimestamp(out ulong depthTs);
            bool hasColorTs = colorParser.PeekNextTimestamp(out ulong colorTs);
            if (!hasDepthTs || !hasColorTs) break;

            long delta = (long)depthTs - (long)colorTs;

            if (Math.Abs(delta) <= maxAllowableDeltaNs)
            {
                bool depthOk = depthParser.ParseNextRecord();
                bool colorOk = colorParser.ParseNextRecord();

                if (depthOk && colorOk)
                {
                    var _depth = depthParser.GetLatestDepthValues();
                    var _color = colorParser.CurrentColorPixels;

                    if (_depth != null && _color != null && _depth.Length > 0)
                    {
                        ushort[] correctedDepth = _depth;
                        // UndistortHelper.UndistortImageUShort(
                        //     _depth,
                        //     depthUndistortLUT,
                        //     depthParser.sensorHeader.custom.camera_sensor.width,
                        //     depthParser.sensorHeader.custom.camera_sensor.height);

                        Color32[] correctedColor = _color;
                        // UndistortHelper.UndistortImage(
                        //     _color,
                        //     colorUndistortLUT,
                        //     colorParser.sensorHeader.custom.camera_sensor.width,
                        //     colorParser.sensorHeader.custom.camera_sensor.height);

                        depthMeshGenerator.UpdateMeshFromDepthAndColor(depthMesh, correctedDepth, correctedColor);
                        firstFrameProcessed = true;
                        if (savedFrameCount < maxSavedFrames)
                        {
                            SaveDepthAndColorImages(correctedDepth, correctedColor, savedFrameCount,
                                depthParser.sensorHeader.custom.camera_sensor.width,
                                depthParser.sensorHeader.custom.camera_sensor.height);

                            savedFrameCount++;
                        }
                    }
                }
                break;
            }

            if (delta < 0) depthParser.ParseNextRecord();
            else colorParser.ParseNextRecord();
        }
    }

    private void SaveDepthAndColorImages(ushort[] depth, Color32[] color, int frameIndex, int width, int height)
    {
        string exportDir = Path.Combine(Application.persistentDataPath, "ExportedFrames");
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        Texture2D depthTex = new Texture2D(width, height, TextureFormat.R8, false);
        float maxDepthMeters = 4.0f;
        float scale = 255f / maxDepthMeters;

        for (int i = 0; i < depth.Length; i++)
        {
            float meters = depth[i] * (depthScaleFactor / 1000f);
            byte intensity = (byte)Mathf.Clamp(meters * scale, 0, 255);
            depthTex.SetPixel(i % width, height - 1 - (i / width), new Color32(intensity, intensity, intensity, 255));
        }
        depthTex.Apply();

        File.WriteAllBytes(Path.Combine(exportDir, $"frame_{frameIndex:D2}_depth.png"), depthTex.EncodeToPNG());
        Destroy(depthTex);

        Texture2D colorTex = new Texture2D(colorParser.sensorHeader.custom.camera_sensor.width,
                                            colorParser.sensorHeader.custom.camera_sensor.height,
                                            TextureFormat.RGB24, false);
        colorTex.SetPixels32(color);
        colorTex.Apply();

        File.WriteAllBytes(Path.Combine(exportDir, $"frame_{frameIndex:D2}_color.png"), colorTex.EncodeToPNG());
        Destroy(colorTex);

        Debug.Log($"Saved frame {frameIndex} to {exportDir}");
    }
}
