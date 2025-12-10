using UnityEngine;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Configuration for a dataset including BVH file, Point Cloud folder, and Binary data paths
/// Can be referenced from Timeline Assets to centralize dataset configuration
/// </summary>
[CreateAssetMenu(fileName = "DatasetConfig", menuName = "Shiiba/DatasetConfig")]
public class DatasetConfig : ScriptableObject
{
    [Header("Dataset Folder")]
    [SerializeField] private DefaultAsset datasetFolder;
    [Tooltip("Drag the dataset folder here (Assets/Data/Datasets/{DatasetName}/)")]

    [Header("BVH Configuration")]
    [SerializeField] private Vector3 bvhPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 bvhRotationOffset = Vector3.zero;
    [SerializeField] private Vector3 bvhScale = Vector3.one;
    [SerializeField] private float bvhOverrideFrameRate = 0f;
    [SerializeField] private int bvhFrameOffset = 0;

    [Header("Processing Mode")]
    [SerializeField] private ProcessingType processingType = ProcessingType.PLY;
    [Tooltip("PLY: Use PLY files from PLY/ folder, PLY_WITH_MOTION: Use PLY files with motion vectors from PLY_WithMotion/ folder, CPU/GPU/ONESHADER: Use raw sensor data")]
    [SerializeField] private bool enablePlyExport = false;
    [Tooltip("When using binary mode, export frames as PLY files")]

    [Header("Binary Data Configuration")]
    [SerializeField] private string binaryDataRootPath = "";
    [Tooltip("Path to binary dataset root (can be external path, not in Assets)")]

    [Header("BVH Drift Correction")]
    [SerializeField] private BvhPlaybackCorrectionKeyframes bvhDriftCorrectionData;
    [Tooltip("Reference to BVH drift correction data (contains keyframes for manual drift correction)")]

    // Properties
    /// <summary>
    /// Get dataset name from folder name
    /// </summary>
    public string DatasetName
    {
        get
        {
            if (datasetFolder == null) return "Unknown";
            string folderPath = AssetDatabase.GetAssetPath(datasetFolder);
            return new DirectoryInfo(folderPath).Name;
        }
    }

    /// <summary>
    /// Get the full folder path from Assets
    /// </summary>
    private string GetDatasetFolderPath()
    {
        if (datasetFolder == null) return "";
        return AssetDatabase.GetAssetPath(datasetFolder);
    }
    public Vector3 BvhPositionOffset => bvhPositionOffset;
    public Vector3 BvhRotationOffset => bvhRotationOffset;
    public Vector3 BvhScale => bvhScale;
    public float BvhOverrideFrameRate => bvhOverrideFrameRate;
    public int BvhFrameOffset => bvhFrameOffset;
    public ProcessingType ProcessingType => processingType;
    public bool EnablePlyExport => enablePlyExport;
    public string BinaryDataRootPath => binaryDataRootPath;
    public BvhPlaybackCorrectionKeyframes BvhPlaybackCorrectionKeyframes => bvhDriftCorrectionData;

    /// <summary>
    /// Get the BVH folder path (relative to project)
    /// </summary>
    public string GetBvhFolderPath()
    {
        string folderPath = GetDatasetFolderPath();
        return Path.Combine(folderPath, "BVH");
    }

    /// <summary>
    /// Get the first BVH file found in the BVH folder
    /// </summary>
    public string GetBvhFilePath()
    {
        string bvhFolder = GetBvhFolderPath();
        if (!Directory.Exists(bvhFolder))
        {
            Debug.LogWarning($"BVH folder not found: {bvhFolder}");
            return "";
        }

        string[] bvhFiles = Directory.GetFiles(bvhFolder, "*.bvh", SearchOption.TopDirectoryOnly);
        if (bvhFiles.Length > 0)
        {
            return bvhFiles[0];
        }
        Debug.LogWarning($"No BVH file found in {bvhFolder}");
        return "";
    }

    /// <summary>
    /// Get the PLY directory (full path)
    /// </summary>
    public string GetPlyDirectory()
    {
        string folderPath = GetDatasetFolderPath();
        return Path.Combine(folderPath, "PLY");
    }

    /// <summary>
    /// Get the point cloud root directory for MultiCameraPointCloudManager
    /// This is the dataset folder itself (PlyModeHandler will look for PLY subfolder)
    /// </summary>
    public string GetPointCloudRootDirectory()
    {
        return GetDatasetFolderPath();
    }

    /// <summary>
    /// Get the binary data root directory (external path, not in Assets)
    /// </summary>
    public string GetBinaryDataRootDirectory()
    {
        return binaryDataRootPath;
    }

    /// <summary>
    /// Validate the configuration paths exist
    /// </summary>
    public bool ValidatePaths()
    {
        // Check BVH file
        if (!System.IO.File.Exists(GetBvhFilePath()))
        {
            Debug.LogWarning($"BVH file not found: {GetBvhFilePath()}");
            return false;
        }

        // Check PointCloud directory
        string pcRoot = GetPointCloudRootDirectory();
        if (!System.IO.Directory.Exists(pcRoot))
        {
            Debug.LogWarning($"PointCloud directory not found: {pcRoot}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get a summary of the configuration
    /// </summary>
    public string GetSummary()
    {
        return $"Dataset: {DatasetName}\n" +
               $"BVH: {GetBvhFilePath()}\n" +
               $"PointCloud Root: {GetPointCloudRootDirectory()}\n" +
               $"Binary Data Root: {GetBinaryDataRootDirectory()}";
    }

    /// <summary>
    /// Get the active DatasetConfig instance from ConfigManager
    /// Allows independent discovery without going through MultiCameraPointCloudManager
    /// </summary>
    public static DatasetConfig GetInstance()
    {
        return ConfigManager.GetDatasetConfig();
    }
}
