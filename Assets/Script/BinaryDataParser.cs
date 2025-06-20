using System.IO;
using UnityEngine;

public class BinaryDataParser : MonoBehaviour
{
    [SerializeField]
    private string dir;

    ISensorDataParser depthParser;
    ISensorDataParser colorParser;

    [SerializeField]
    private float depthScaleFactor = 1000f;

    private GameObject depthViewer;
    private MeshFilter depthMeshFilter;
    private Mesh depthMesh;

    private GameObject colorViewer;
    private MeshRenderer colorRenderer;
    private Texture2D currentTexture;

    void Start()
    {
        string depthFilePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_depth");
        string colorFilePath = Path.Combine(dir, "dataset", "PAN-SHI", "FemtoBolt_CL8F25300C6", "camera_color");

        if (!File.Exists(depthFilePath) || !File.Exists(colorFilePath))
        {
            Debug.LogError("指定されたファイルが存在しません");
            return;
        }

        depthParser = SensorDataParserFactory.Create(depthFilePath);
        Debug.Log("Loaded depth header: " + depthParser.FormatIdentifier);

        colorParser = SensorDataParserFactory.Create(colorFilePath);
        Debug.Log("Loaded color header: " + colorParser.FormatIdentifier);

        depthViewer = GameObject.Find("DepthViewer");
        if (depthViewer == null) Debug.LogError("DepthViewer GameObject が見つかりません");
        depthMeshFilter = depthViewer.GetComponent<MeshFilter>();
        if (depthMeshFilter == null) depthMeshFilter = depthViewer.AddComponent<MeshFilter>();
        var depthRenderer = depthViewer.GetComponent<MeshRenderer>();
        if (depthRenderer == null)
        {
            depthRenderer = depthViewer.AddComponent<MeshRenderer>();
            depthRenderer.material = new Material(Shader.Find("Unlit/Color"));
        }
        depthMesh = new Mesh();
        depthMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        depthMeshFilter.mesh = depthMesh;

        colorViewer = GameObject.Find("ColorViewer");
        if (colorViewer == null) Debug.LogError("ColorViewer GameObject が見つかりません");
        colorRenderer = colorViewer.GetComponent<MeshRenderer>();
        if (colorRenderer == null) colorRenderer = colorViewer.AddComponent<MeshRenderer>();
        if (colorRenderer.material == null || colorRenderer.material.shader.name != "Unlit/Texture")
        {
            colorRenderer.material = new Material(Shader.Find("Unlit/Texture"));
        }
        currentTexture = new Texture2D(2, 2);
        colorRenderer.material.mainTexture = currentTexture;
    }

    void Update()
    {
        if (depthParser?.ParseNextRecord() == true && depthParser is RcstSensorDataParser rcst)
        {
            var depth = rcst.GetLatestDepthValues();
            if (depth != null && depth.Length > 0)
            {
                DepthMeshGenerator.UpdateMeshFromDepth(depthMesh, depth, rcst.sensorHeader, depthScaleFactor);
            }
        }

        if (colorParser?.ParseNextRecord() == true && colorParser is RcsvSensorDataParser rgb)
        {
            if (rgb.CurrentColorBytes != null)
            {
                currentTexture.LoadImage(rgb.CurrentColorBytes);
            }
        }
    }
}
