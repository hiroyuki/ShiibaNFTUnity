using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

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
    private bool autoLoadFirstFrame = true;
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
            Debug.Log($"Applying global transform for {deviceName}: position = {pos:F6}, rotation = {rot.eulerAngles:F6}");

            // Unity用に座標系変換（右手系→左手系、Y軸下→Y軸上）
            Vector3 unityPosition = new Vector3(pos.x, pos.y, pos.z);

            // 回転はX軸180°回転を前掛けして上下反転と利き手系の変換を行う
            Quaternion unityRotation = rot;

            // 結果を確認
            Vector3 unityEuler = unityRotation.eulerAngles;
            Debug.Log($"Unity Position: {unityPosition:F6}  Rotation (Euler): {unityEuler:F6}");

            depthViewer.transform.localRotation = unityRotation;
            depthViewer.transform.localPosition = unityPosition;

            // Debug.Log($"Applied inverse transform for {deviceName} → position = {-(rot * pos)}, rotation = {Quaternion.Inverse(rot).eulerAngles}");
        }

        depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.AddComponent<MeshRenderer>();
        var material = new Material(Shader.Find("Unlit/VertexColor"));
        material.SetFloat("_PointSize", 3.0f); // Set point size for macOS compatibility
        depthRenderer.material = material;

        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        // Load depth bias from configuration.yaml
        float depthBias = LoadDepthBias();
        
        depthMeshGenerator = new DepthMeshGenerator();
        depthMeshGenerator.setup(depthParser.sensorHeader, depthScaleFactor, depthBias);
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

        colorUndistortLUT = OpenCVUndistortHelper.BuildUndistortLUTFromHeader(colorParser.sensorHeader);
    }

    void Update()
    {
        // Auto-load first frame on startup
        if (autoLoadFirstFrame && !firstFrameProcessed)
        {
            ProcessNextFrame();
            autoLoadFirstFrame = false; // Prevent auto-loading again
        }
        
        // Check for right arrow key press to advance to next frame
        if (Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            ProcessNextFrame();
        }
    }

    private void ProcessNextFrame()
    {
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
                        
                        Debug.Log($"Processed frame for {deviceName}");
                        firstFrameProcessed = true; // Mark first frame as processed
                        if (savedFrameCount < maxSavedFrames)
                        {
                            // Export debug images using utility class
                            DebugImageExporter.ExportAllDebugImages(
                                _depth, _color,
                                depthParser.sensorHeader.custom.camera_sensor.width,
                                depthParser.sensorHeader.custom.camera_sensor.height,
                                colorParser.sensorHeader.custom.camera_sensor.width,
                                colorParser.sensorHeader.custom.camera_sensor.height,
                                depthScaleFactor, depthMeshGenerator, deviceName, savedFrameCount
                            );

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


    private float LoadDepthBias()
    {
        string configPath = Path.Combine(dir, "configuration.yaml");
        
        if (!File.Exists(configPath))
        {
            Debug.LogWarning($"configuration.yaml not found at {configPath}, using depthBias = 0");
            return 0f;
        }

        try
        {
            string yamlText = File.ReadAllText(configPath);
            
            // Simple parsing to find depthBias value
            string[] lines = yamlText.Split('\n');
            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("depthBias:"))
                {
                    string[] parts = line.Split(':');
                    if (parts.Length > 1 && float.TryParse(parts[1].Trim(), out float bias))
                    {
                        Debug.Log($"Loaded depthBias: {bias} from {configPath}");
                        return bias;
                    }
                }
            }
            
            Debug.LogWarning("depthBias not found in configuration.yaml, using 0");
            return 0f;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load configuration.yaml: {ex.Message}");
            return 0f;
        }
    }
}
