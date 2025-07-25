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

    private bool firstFrameProcessed = false;
    private ExtrinsicsLoader extrisics;

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

        extrisics = new ExtrinsicsLoader(extrinsicsPath);
        if (!extrisics.IsLoaded)
        {
            Debug.LogError("Extrinsics data could not be loaded from: " + extrinsicsPath);
            return;
        }

        float? loadedScale = extrisics.GetDepthScaleFactor(serial);
        if (loadedScale.HasValue)
        {
            depthScaleFactor = loadedScale.Value;
        }

        // --- DepthViewer 自動生成 ---
        string viewerName = $"DepthViewer_{deviceName}";
        depthViewer = new GameObject(viewerName);
        depthViewer.transform.SetParent(this.transform);

        // 視覚化 Gizmo を追加
        var gizmo = depthViewer.AddComponent<CameraPositionGizmo>();
        gizmo.gizmoColor = Color.red;
        gizmo.size = 0.1f;

        if (extrisics.TryGetGlobalTransform(serial, out Vector3 pos, out Quaternion rot))
        {
            Debug.Log($"Applying global transform for {deviceName}: position = {pos}, rotation = {rot.eulerAngles}");

            // Unity用に座標系変換（右手系→左手系、Y軸下→Y軸上）
            Vector3 unityPosition = new Vector3(pos.x, pos.y, pos.z);

            // 回転はX軸180°回転を前掛けして上下反転と利き手系の変換を行う
            Quaternion unityRotation = rot;

            // 結果を確認
            Vector3 unityEuler = unityRotation.eulerAngles;
            Debug.Log($"Unity Position: {unityPosition}  Rotation (Euler): {unityEuler}");

            depthViewer.transform.localRotation = unityRotation;
            depthViewer.transform.localPosition = unityPosition;

            // Debug.Log($"Applied inverse transform for {deviceName} → position = {-(rot * pos)}, rotation = {Quaternion.Inverse(rot).eulerAngles}");
        }

        depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.AddComponent<MeshRenderer>();
        depthRenderer.material = new Material(Shader.Find("Unlit/VertexColor"));

        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        depthMeshGenerator = new DepthMeshGenerator();
        depthMeshGenerator.setup(depthParser.sensorHeader, depthScaleFactor);
        var d2cTranslation = Vector3.zero;
        var d2cRotation = Quaternion.identity;
        if (extrisics.TryGetDepthToColorTransform(serial, out d2cTranslation, out d2cRotation))
        {
            Debug.Log($"Depth to Color transform for {serial}: translation = {d2cTranslation}, rotation = {d2cRotation.eulerAngles}");
            depthMeshGenerator.ApplyDepthToColorExtrinsics(d2cTranslation, d2cRotation);
        }
        else
        {
            Debug.LogError($"Failed to get depth to color transform for {serial}");
            return;
        }
        depthMeshGenerator.SetupColorIntrinsics(colorParser.sensorHeader);

        colorUndistortLUT = UndistortHelper.BuildUndistortLUTFromHeader(colorParser.sensorHeader);
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
                        // Temporarily use original color without undistortion
                        // TODO: Fix color undistortion LUT bounds checking
                        depthMeshGenerator.UpdateMeshFromDepthAndColor(depthMesh, _depth, _color);
                        firstFrameProcessed = true;
                        if (savedFrameCount < maxSavedFrames)
                        {
                            SaveDepthAndColorImages(_depth, _color, savedFrameCount,
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

    private void SaveDepthAndColorImages(ushort[] originalDepth, Color32[] color, int frameIndex, int width, int height)
    {
        string exportDir = Path.Combine(Application.persistentDataPath, "ExportedFrames");
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        // Save original depth
        Texture2D originalDepthTex = new Texture2D(width, height, TextureFormat.R8, false);
        float maxDepthMeters = 4.0f;
        float scale = 255f / maxDepthMeters;

        for (int i = 0; i < originalDepth.Length; i++)
        {
            float meters = originalDepth[i] * (depthScaleFactor / 1000f);
            byte intensity = (byte)Mathf.Clamp(meters * scale, 0, 255);
            originalDepthTex.SetPixel(i % width, height - 1 - (i / width), new Color32(intensity, intensity, intensity, 255));
        }
        originalDepthTex.Apply();

        File.WriteAllBytes(Path.Combine(exportDir, $"frame_{frameIndex:D2}_depth_original.png"), originalDepthTex.EncodeToPNG());
        Destroy(originalDepthTex);

        Texture2D colorTex = new Texture2D(colorParser.sensorHeader.custom.camera_sensor.width,
                                            colorParser.sensorHeader.custom.camera_sensor.height,
                                            TextureFormat.RGB24, false);
        colorTex.SetPixels32(color);
        colorTex.Apply();

        File.WriteAllBytes(Path.Combine(exportDir, $"frame_{frameIndex:D2}_color.png"), colorTex.EncodeToPNG());
        Destroy(colorTex);

        Debug.Log($"Saved frame {frameIndex} (original depth + color) to {exportDir}");
    }
}
