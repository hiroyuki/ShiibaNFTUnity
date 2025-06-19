using System.IO;
using UnityEngine;

public class BinaryDataParser : MonoBehaviour
{
    [SerializeField]
    private string dir;

    ISensorDataParser depthParser;
    ISensorDataParser colorParser;

    [SerializeField]
    private float depthScaleFactor = 1000f; // extrinsics.yaml 由来（仮固定）

    private GameObject depthViewer;
    private MeshFilter depthViewerMeshFilter;

    private GameObject colorViewer;

    void Start()
    {
        string depthFilePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_depth");
        string colorFilePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_color");

        if (!File.Exists(depthFilePath))
        {
            Debug.LogError("指定されたファイルが存在しません: " + depthFilePath);
            return;
        }

        if (!File.Exists(colorFilePath))
        {
            Debug.LogError("指定されたファイルが存在しません: " + colorFilePath);
            return;
        }

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

        depthParser = SensorDataParserFactory.Create(depthFilePath);
        colorParser = SensorDataParserFactory.Create(colorFilePath);

        Debug.Log("Depth header loaded from: " + depthFilePath + " " + depthParser.FormatIdentifier);
        Debug.Log("Color header loaded from: " + colorFilePath + " " + colorParser.FormatIdentifier);

        depthViewer = GameObject.Find("DepthViewer");
        colorViewer = GameObject.Find("ColorViewer");

        if (depthViewer == null || colorViewer == null)
        {
            Debug.LogError("DepthViewer または ColorViewer GameObject が見つかりません");
            return;
        }

        depthViewerMeshFilter = depthViewer.GetComponent<MeshFilter>();
        if (depthViewerMeshFilter == null)
            depthViewerMeshFilter = depthViewer.AddComponent<MeshFilter>();

        var depthRenderer = depthViewer.GetComponent<MeshRenderer>();
        if (depthRenderer == null)
        {
            depthRenderer = depthViewer.AddComponent<MeshRenderer>();
            depthRenderer.material = new Material(Shader.Find("Unlit/Color"));
        }
    }

    void Update()
    {
        if (depthParser == null || colorParser == null) return;
        if (!depthParser.ParseNextRecord()) return;
        if (!colorParser.ParseNextRecord()) return;

        if (depthParser is RcstSensorDataParser rcst)
        {
            ushort[] depth = rcst.GetLatestDepthValues();
            if (depth == null || depth.Length == 0) return;

            Mesh mesh = DepthMeshGenerator.CreateMeshFromDepth(depth, rcst.sensorHeader, depthScaleFactor);
            depthViewerMeshFilter.mesh = mesh;
        }

        if (colorParser is RcsvSensorDataParser rgb && rgb.CurrentColorBytes != null)
        {
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(rgb.CurrentColorBytes))
            {
                var renderer = colorViewer.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    renderer = colorViewer.AddComponent<MeshRenderer>();
                }

                // Plane に貼り付けるためのマテリアルに設定
                if (renderer.material == null || renderer.material.shader.name != "Unlit/Texture")
                {
                    renderer.material = new Material(Shader.Find("Unlit/Texture"));
                }
                renderer.material.mainTexture = texture;
            }
        }
    }
}
