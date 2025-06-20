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
        depthMeshGenerator = new DepthMeshGenerator();
        depthMeshGenerator.setup(depthParser.sensorHeader, depthScaleFactor);
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
        Debug.Log("Frame: " + Time.frameCount + ", Time: " + Time.time + ", FPS: " + Time.frameCount / Time.time);
        if (depthParser?.ParseNextRecord() == true)
        {
            var depth = depthParser.GetLatestDepthValues();
            if (depth != null && depth.Length > 0)
            {
                depthMeshGenerator.UpdateMeshFromDepth(depthMesh, depth);
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
