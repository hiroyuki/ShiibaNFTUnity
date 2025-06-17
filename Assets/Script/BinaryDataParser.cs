using System.IO;
using UnityEngine;

public class BinaryDataParser : MonoBehaviour
{


    [SerializeField]
    private string dir;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        string filePath = dir + "\\dataset\\PAN-SHI\\FemtoBolt_CL8F25300C6\\camera_depth";
        if (!File.Exists(filePath))
        {
            Debug.LogError("指定されたファイルが存在しません: " + filePath);
            return;
        }
        ISensorDataParser parser = SensorDataParserFactory.Create(filePath);
        Debug.Log("Header loaded from: " + filePath + " " + parser.FormatIdentifier);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
