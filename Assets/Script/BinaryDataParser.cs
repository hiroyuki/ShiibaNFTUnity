using System.IO;
using UnityEngine;
public class BinaryDataParser : MonoBehaviour
{
    [SerializeField]
    private string dir;

    ISensorDataParser depthParser;

    [SerializeField]
    private float depthScaleFactor = 1000f; // extrinsics.yaml 由来（仮固定）

    private GameObject viewer;
    private MeshFilter viewerMeshFilter;

    void Start()
    {
        string filePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_depth");

        if (!File.Exists(filePath))
        {
            Debug.LogError("指定されたファイルが存在しません: " + filePath);
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

        depthParser = SensorDataParserFactory.Create(filePath);
        Debug.Log("Header loaded from: " + filePath + " " + depthParser.FormatIdentifier);

        // DepthViewer を取得
        viewer = GameObject.Find("DepthViewer");
        if (viewer == null)
        {
            Debug.LogError("DepthViewer GameObject が見つかりません");
            return;
        }

        // MeshFilter + Renderer を準備
        viewerMeshFilter = viewer.GetComponent<MeshFilter>();
        if (viewerMeshFilter == null)
            viewerMeshFilter = viewer.AddComponent<MeshFilter>();

        var renderer = viewer.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = viewer.AddComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Unlit/Color"));
        }
    }

    void Update()
    {
        if (depthParser == null) return;
        if (!depthParser.ParseNextRecord()) return;

        // RcstSensorDataParser としてキャスト
        if (depthParser is RcstSensorDataParser rcst)
        {
            ushort[] depth = rcst.GetLatestDepthValues();
            if (depth == null || depth.Length == 0) return;

            Mesh mesh = DepthMeshGenerator.CreateMeshFromDepth(depth, rcst.sensorHeader, depthScaleFactor);
            viewerMeshFilter.mesh = mesh;
        }
    }
}