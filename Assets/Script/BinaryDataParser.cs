using System;
using System.IO;
using UnityEngine;

public class BinaryDataParser : MonoBehaviour
{
    [SerializeField]
    private string dir;

    RcstSensorDataParser depthParser;
    RcsvSensorDataParser colorParser;

    [SerializeField]
    private float depthScaleFactor = 1000f;

    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private DepthMeshGenerator depthMeshGenerator;

    private GameObject colorViewer;
    private MeshRenderer colorRenderer;
    private Texture2D currentTexture;

    private int savedFrameCount = 0;
    private const int maxSavedFrames = 10;

    void Start()
    {

        string depthFilePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_depth");
        string colorFilePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_color");

        if (!File.Exists(depthFilePath) || !File.Exists(colorFilePath))
        {
            Debug.LogError("指定されたファイルが存在しません");
            return;
        }

        depthParser = (RcstSensorDataParser)SensorDataParserFactory.Create(depthFilePath);
        Debug.Log("Loaded depth header: " + depthParser.FormatIdentifier);

        colorParser = (RcsvSensorDataParser)SensorDataParserFactory.Create(colorFilePath);
        Debug.Log("Loaded color header: " + colorParser.FormatIdentifier);

        string extrinsicsPath = Path.Combine(dir, "calibration", "extrinsics.yaml");
        string serial = "CL8F25300C6"; // SensorHeader.custom.serial_number から動的取得もOK

        float? loadedScale = ExtrinsicsLoader.GetDepthScaleFactor(extrinsicsPath, serial);
        if (loadedScale.HasValue)
        {
            depthScaleFactor = loadedScale.Value;
            Debug.Log("Loaded depthScaleFactor: " + depthScaleFactor);
        }
        else
        {
            Debug.LogWarning("depthScaleFactor が読み込めなかったため、既定値を使用します");
        }

        depthViewer = GameObject.Find("DepthViewer");
        if (depthViewer == null) Debug.LogError("DepthViewer GameObject が見つかりません");
        depthMeshFilter = depthViewer.GetComponent<MeshFilter>();
        if (depthMeshFilter == null) depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.GetComponent<MeshRenderer>();
        if (depthRenderer == null)
        {
            depthRenderer = depthViewer.AddComponent<MeshRenderer>();
            depthRenderer.material = new Material(Shader.Find("Unlit/VertexColor"));
        }
        depthMesh = new Mesh();
        depthMeshGenerator = new DepthMeshGenerator();
        depthMeshGenerator.setup(depthParser.sensorHeader, depthScaleFactor);
        depthMeshGenerator.SetupColorIntrinsics(colorParser.sensorHeader);

        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        colorViewer = GameObject.Find("ColorViewer");
        // if (colorViewer == null) Debug.LogError("ColorViewer GameObject が見つかりません");
        // colorRenderer = colorViewer.GetComponent<MeshRenderer>();
        // if (colorRenderer == null) colorRenderer = colorViewer.AddComponent<MeshRenderer>();
        // if (colorRenderer.material == null || colorRenderer.material.shader.name != "Unlit/Texture")
        // {
        //     colorRenderer.material = new Material(Shader.Find("Unlit/Texture"));
        // }
        // currentTexture = new Texture2D(2, 2);
        // colorRenderer.material.mainTexture = currentTexture;
    }
    void Update()
    {
        // if (Time.frameCount > 2) return;
        const long maxAllowableDeltaNs = 2_000; // 2ms（必要に応じて調整）

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
                    var depth = depthParser.GetLatestDepthValues();
                    var color = colorParser.CurrentColorPixels;

                    if (depth != null && color != null && depth.Length > 0)
                    {
                        depthMeshGenerator.UpdateMeshFromDepthAndColor(depthMesh, depth, color);
                        // if (savedFrameCount < maxSavedFrames)
                        // {
                        //     SaveDepthAndColorImages(depth, color, savedFrameCount,
                        //         depthParser.sensorHeader.custom.camera_sensor.width,
                        //         depthParser.sensorHeader.custom.camera_sensor.height);

                        //     var projTex = depthMeshGenerator.ProjectDepthToColorImage(depth);
                        //     if (projTex != null)
                        //     {
                        //         string exportDir = Path.Combine(Application.persistentDataPath, "ExportedFrames");
                        //         byte[] bytes = projTex.EncodeToPNG();
                        //         File.WriteAllBytes(Path.Combine(exportDir, $"frame_{savedFrameCount:D2}_projection.png"), bytes);
                        //         Destroy(projTex);
                        //     }

                        //     savedFrameCount++;
                        // }
                    }
                }
                break;
            }

            if (delta < 0) depthParser.ParseNextRecord();  // depth is behind
            else colorParser.ParseNextRecord();            // color is behind
        }


    }

    // void Update()
    // {
    //     Debug.Log("Frame: " + Time.frameCount + ", Time: " + Time.time + ", FPS: " + Time.frameCount / Time.time);

    //     bool depthOk = depthParser?.ParseNextRecord() == true;
    //     bool colorOk = colorParser?.ParseNextRecord() == true;

    //     if (depthOk && colorOk)
    //     {
    //         var depth = depthParser.GetLatestDepthValues();
    //         var color = colorParser.CurrentColorPixels;

    //         if (depth != null && color != null && depth.Length > 0 && color.Length == depth.Length)
    //         {
    //             depthMeshGenerator.UpdateMeshFromDepthAndColor(depthMesh, depth, color);
    //         }
    //     }
    // }
    
    private void SaveDepthAndColorImages(ushort[] depth, Color32[] color, int frameIndex, int width, int height)
    {
        string exportDir = Path.Combine(Application.persistentDataPath, "ExportedFrames");
        if (!Directory.Exists(exportDir))
            Directory.CreateDirectory(exportDir);

        // --- Save Depth as Grayscale PNG ---
        Texture2D depthTex = new Texture2D(width, height, TextureFormat.R8, false);
        float maxDepthMeters = 4.0f; // 調整可能
        float scale = 255f / maxDepthMeters;

        for (int i = 0; i < depth.Length; i++)
        {
            float meters = depth[i] * (depthScaleFactor / 1000f);
            byte intensity = (byte)Mathf.Clamp(meters * scale, 0, 255);
            depthTex.SetPixel(i % width, height - 1 - (i / width), new Color32(intensity, intensity, intensity, 255));
        }
        depthTex.Apply();

        byte[] depthBytes = depthTex.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(exportDir, $"frame_{frameIndex:D2}_depth.png"), depthBytes);
        Destroy(depthTex);

        // --- Save Color as PNG ---
        int cW = colorParser.sensorHeader.custom.camera_sensor.width;
        int cH = colorParser.sensorHeader.custom.camera_sensor.height;

        Texture2D colorTex = new Texture2D(cW, cH, TextureFormat.RGB24, false);
        colorTex.SetPixels32(color);
        colorTex.Apply();

        byte[] colorBytes = colorTex.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(exportDir, $"frame_{frameIndex:D2}_color.png"), colorBytes);
        Destroy(colorTex);

        Debug.Log($"Saved frame {frameIndex} to {exportDir}");
    }
}

