using UnityEngine;
using UnityEngine.Playables;
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
    [SerializeField] private bool enableBvh = true;
    [Tooltip("Enable/disable BVH skeleton loading and playback")]
    [SerializeField] private DefaultAsset bvhFile;
    [Tooltip("Drag and drop the BVH file here (or leave empty to auto-detect from BVH/ folder)")]
    [SerializeField] private Vector3 bvhPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 bvhRotationOffset = Vector3.zero;
    [SerializeField] private Vector3 bvhScale = Vector3.one;

    [Header("Processing Mode")]
    [SerializeField] private ProcessingType processingType = ProcessingType.PLY;
    [Tooltip("PLY: Use PLY files from PLY/ folder, PLY_WITH_MOTION: Use PLY files with motion vectors from PLY_WithMotion/ folder, CPU/GPU/ONESHADER: Use raw sensor data")]

    [Header("Binary Data Configuration")]
    [SerializeField] private string binaryDataRootPath = "";
    [Tooltip("Path to binary dataset root (can be external path, not in Assets)")]

    [Header("BVH Drift Correction")]
    [SerializeField] private BvhPlaybackCorrectionKeyframes bvhDriftCorrectionData;
    [Tooltip("Reference to BVH drift correction data (contains keyframes for manual drift correction)")]

    [Header("Point Cloud Downsampling")]
    [SerializeField] private bool showDownsampledPointCloud = false;
    [Tooltip("Show downsampled point cloud visualization (requires PointCloudDownsampler component in scene)")]

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
    public bool EnableBvh => enableBvh;
    public Vector3 BvhPositionOffset => bvhPositionOffset;
    public Vector3 BvhRotationOffset => bvhRotationOffset;
    public Vector3 BvhScale => bvhScale;
    public ProcessingType ProcessingType => processingType;
    public bool EnablePlyExport => true; // Always enabled
    public string BinaryDataRootPath => binaryDataRootPath;
    public BvhPlaybackCorrectionKeyframes BvhPlaybackCorrectionKeyframes => bvhDriftCorrectionData;
    public bool ShowDownsampledPointCloud => showDownsampledPointCloud;

    /// <summary>
    /// Get the BVH folder path (relative to project)
    /// </summary>
    public string GetBvhFolderPath()
    {
        string folderPath = GetDatasetFolderPath();
        return Path.Combine(folderPath, "BVH");
    }

    /// <summary>
    /// Get the BVH file path. If bvhFile is set, use that. Otherwise, auto-detect from BVH folder
    /// </summary>
    public string GetBvhFilePath()
    {
        if (!enableBvh)
        {
            return "";
        }

        // Priority 1: Use manually assigned BVH file
        if (bvhFile != null)
        {
#if UNITY_EDITOR
            string bvhFilePath = AssetDatabase.GetAssetPath(bvhFile);
            if (!string.IsNullOrEmpty(bvhFilePath) && File.Exists(bvhFilePath))
            {
                return bvhFilePath;
            }
#endif
        }

        // Priority 2: Auto-detect from BVH folder
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
        // Check BVH file only if BVH is enabled
        if (enableBvh)
        {
            string bvhPath = GetBvhFilePath();
            if (string.IsNullOrEmpty(bvhPath) || !System.IO.File.Exists(bvhPath))
            {
                Debug.LogWarning($"BVH file not found: {bvhPath}");
                return false;
            }
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

    /// <summary>
    /// Event triggered when BVH transform values change in Inspector
    /// </summary>
    public static event System.Action OnBvhTransformChanged;

    /// <summary>
    /// Event triggered when downsampled point cloud visibility toggle changes
    /// </summary>
    public static event System.Action<bool> OnShowDownsampledPointCloudChanged;

#if UNITY_EDITOR
    private bool previousShowDownsampledPointCloud = false;

    private void OnValidate()
    {
        OnBvhTransformChanged?.Invoke();

        if (showDownsampledPointCloud != previousShowDownsampledPointCloud)
        {
            OnShowDownsampledPointCloudChanged?.Invoke(showDownsampledPointCloud);
            previousShowDownsampledPointCloud = showDownsampledPointCloud;
            UpdateDownsampledPointCloudVisibility();
        }

        UpdateBvhCharacterTransform();
    }

    private void UpdateDownsampledPointCloudVisibility()
    {
        PointCloudDownsampler downsampler = FindFirstObjectByType<PointCloudDownsampler>();
        if (downsampler == null)
        {
            if (showDownsampledPointCloud)
            {
                Debug.LogWarning("[DatasetConfig] PointCloudDownsampler not found in scene.");
            }
            return;
        }

        if (!showDownsampledPointCloud)
        {
            downsampler.ResetToOriginal();
        }
    }

    /// <summary>
    /// Directly update BVH_Character transform when config changes in Edit mode
    /// </summary>
    private void UpdateBvhCharacterTransform()
    {
        if (Application.isPlaying) return;

        GameObject bvhCharacterGO = GameObject.Find("BVH_Character");
        if (bvhCharacterGO == null)
            return;

        var timeline = FindFirstObjectByType<PlayableDirector>();
        double timelineTime = timeline != null ? timeline.time : 0.0;

        Vector3 correctedPos = BvhPlaybackTransformCorrector.GetCorrectedRootPosition(
            timelineTime,
            BvhPlaybackCorrectionKeyframes,
            bvhPositionOffset
        );

        Quaternion correctedRot = BvhPlaybackTransformCorrector.GetCorrectedRootRotation(
            timelineTime,
            BvhPlaybackCorrectionKeyframes,
            bvhRotationOffset
        );

        bvhCharacterGO.transform.SetLocalPositionAndRotation(correctedPos, correctedRot);
        bvhCharacterGO.transform.localScale = bvhScale;

        EditorUtility.SetDirty(bvhCharacterGO);
    }
#endif
}
