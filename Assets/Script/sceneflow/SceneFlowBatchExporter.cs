using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Batch exporter for PLY files with scene flow (motion) vectors
/// Similar pattern to PointCloudDownsampler - attach to GameObject and use context menu
/// </summary>
public class SceneFlowBatchExporter : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField]
    [Tooltip("Dataset configuration containing BVH and PLY paths")]
    private DatasetConfig datasetConfig;

    [Header("References")]
    [SerializeField]
    [Tooltip("SceneFlowCalculator component for motion vector calculation (auto-found if not set)")]
    private SceneFlowCalculator sceneFlowCalculator;

    [SerializeField]
    [Tooltip("MultiCameraPointCloudManager component (auto-found if not set)")]
    private MultiCameraPointCloudManager multiCameraManager;

    // MultiPointCloudView is accessed through MultiCameraPointCloudManager
    private MultiPointCloudView multiPointCloudView;

    [Header("Export Settings")]
    [SerializeField]
    [Tooltip("Output directory for exported PLY files")]
    private string outputDirectory = "ExportedPLY_SceneFlow";

    [SerializeField]
    [Tooltip("Start frame index (inclusive)")]
    private int startFrame = 1;

    [SerializeField]
    [Tooltip("End frame index (inclusive, 0 = use total frame count)")]
    private int endFrame = 0;

    [SerializeField]
    [Tooltip("Use ASCII format instead of binary")]
    private bool exportAsAscii = false;

    [SerializeField]
    [Tooltip("Skip existing files during export")]
    private bool skipExistingFiles = true;

    // Export state
    private bool isExporting = false;
    private int currentExportFrame = 0;
    private int totalFramesToExport = 0;
    private float exportStartTime = 0f;

    // PLY file list cache
    private string[] plyFiles = null;
    private string resolvedPlyDirectory = null;

    // Progress tracking
    public bool IsExporting => isExporting;
    public float ExportProgress => totalFramesToExport > 0 ? (float)currentExportFrame / totalFramesToExport : 0f;
    public int CurrentFrame => currentExportFrame;
    public int TotalFrames => totalFramesToExport;

    private void Awake()
    {
        // Auto-find components if not set
        if (sceneFlowCalculator == null)
            sceneFlowCalculator = FindAnyObjectByType<SceneFlowCalculator>(FindObjectsInactive.Include);

        if (multiCameraManager == null)
            multiCameraManager = FindAnyObjectByType<MultiCameraPointCloudManager>(FindObjectsInactive.Include);
    }

    private void Start()
    {
        // Get MultiPointCloudView from manager after initialization
        if (multiCameraManager != null)
        {
            multiPointCloudView = multiCameraManager.GetMultiPointCloudView();
            if (multiPointCloudView == null)
            {
                Debug.LogWarning("[SceneFlowBatchExporter] MultiPointCloudView not yet created by manager. It will be available after entering Play mode.");
            }
        }
    }

    [ContextMenu("Batch Export PLY with Motion Vectors")]
    public void BatchExportPLYWithMotionVectors()
    {
        if (isExporting)
        {
            Debug.LogWarning("[SceneFlowBatchExporter] Export already in progress");
            return;
        }

        if (datasetConfig == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] DatasetConfig not assigned. Please assign it in the Inspector.");
            return;
        }

        StartBatchExport();
    }

    [ContextMenu("Stop Export")]
    public void StopExport()
    {
        if (isExporting)
        {
            isExporting = false;
            Debug.Log("[SceneFlowBatchExporter] Export cancelled by user");
            StopAllCoroutines();
        }
    }

    /// <summary>
    /// Start batch export process
    /// </summary>
    public void StartBatchExport()
    {
        if (isExporting)
        {
            Debug.LogWarning("[SceneFlowBatchExporter] Export already in progress");
            return;
        }

        if (datasetConfig == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] DatasetConfig not assigned");
            return;
        }

        if (sceneFlowCalculator == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] SceneFlowCalculator reference not set");
            return;
        }

        if (multiCameraManager == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] MultiCameraPointCloudManager reference not set");
            return;
        }

        // Get MultiPointCloudView from manager (in case it wasn't available at Start)
        if (multiPointCloudView == null)
        {
            multiPointCloudView = multiCameraManager.GetMultiPointCloudView();
        }

        if (multiPointCloudView == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] MultiPointCloudView not available from manager. Ensure the system is initialized in Play mode.");
            return;
        }

        // Initialize BvhDataCache with DatasetConfig
        BvhDataCache.InitializeWithConfig(datasetConfig);
        BvhData bvhData = BvhDataCache.GetBvhData();
        if (bvhData == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] Failed to load BVH data from DatasetConfig");
            return;
        }

        // Discover PLY directory and files
        if (!DiscoverPlyFiles())
        {
            Debug.LogError("[SceneFlowBatchExporter] Failed to find PLY files. Check inputPlyDirectory or DatasetConfig.");
            return;
        }

        // Determine frame range (use PLY file count as max)
        int maxFrame = plyFiles.Length - 1;
        int actualEndFrame = endFrame > 0 ? Mathf.Min(endFrame, maxFrame) : maxFrame;

        Debug.Log($"[SceneFlowBatchExporter] Frame range: {startFrame} to {actualEndFrame} (total PLY files: {plyFiles.Length})");

        if (startFrame < 1)
        {
            Debug.LogError("[SceneFlowBatchExporter] Start frame must be >= 1 (need previous frame for motion vectors)");
            return;
        }

        if (actualEndFrame < startFrame)
        {
            Debug.LogError($"[SceneFlowBatchExporter] Invalid frame range: {startFrame} to {actualEndFrame}");
            return;
        }

        // Create output directory
        string absoluteOutputPath = Path.Combine(Application.dataPath, "..", outputDirectory);
        Directory.CreateDirectory(absoluteOutputPath);

        // Initialize export state
        currentExportFrame = startFrame;
        totalFramesToExport = actualEndFrame - startFrame + 1;
        exportStartTime = Time.realtimeSinceStartup;
        isExporting = true;

        Debug.Log($"[SceneFlowBatchExporter] Starting batch export: frames {startFrame} to {actualEndFrame} ({totalFramesToExport} frames)");
        Debug.Log($"[SceneFlowBatchExporter] Output directory: {absoluteOutputPath}");
        Debug.Log($"[SceneFlowBatchExporter] Format: {(exportAsAscii ? "ASCII" : "Binary")}");

        // Start coroutine
        StartCoroutine(BatchExportCoroutine(actualEndFrame));
    }

    /// <summary>
    /// Stop batch export process
    /// </summary>
    public void StopBatchExport()
    {
        if (isExporting)
        {
            isExporting = false;
            Debug.Log("[SceneFlowBatchExporter] Export cancelled by user");
            StopAllCoroutines();
        }
    }

    /// <summary>
    /// Coroutine for batch export process
    /// </summary>
    private IEnumerator BatchExportCoroutine(int actualEndFrame)
    {
        string absoluteOutputPath = Path.Combine(Application.dataPath, "..", outputDirectory);

        // Get BVH data and drift correction for frame mapping
        BvhData bvhData = BvhDataCache.GetBvhData();
        BvhPlaybackCorrectionKeyframes driftData = datasetConfig.BvhPlaybackCorrectionKeyframes;
        BvhPlaybackFrameMapper frameMapper = new BvhPlaybackFrameMapper();

        for (int frameIndex = startFrame; frameIndex <= actualEndFrame; frameIndex++)
        {
            if (!isExporting)
            {
                Debug.Log("[SceneFlowBatchExporter] Export cancelled");
                yield break;
            }

            currentExportFrame = frameIndex;

            // Generate output file path
            string filename = $"frame_{frameIndex:D6}_sceneflow.ply";
            string filePath = Path.Combine(absoluteOutputPath, filename);

            // Skip if file exists
            if (skipExistingFiles && File.Exists(filePath))
            {
                Debug.Log($"[SceneFlowBatchExporter] Skipping existing file: {filename}");
                continue;
            }

            // Load PLY file for current frame using MultiPointCloudView
            string plyFilePath = plyFiles[frameIndex];
            multiPointCloudView.LoadFromPLY(plyFilePath);

            // Get the loaded mesh
            Mesh mesh = multiPointCloudView.GetUnifiedMesh();
            if (mesh == null || mesh.vertexCount == 0)
            {
                Debug.LogWarning($"[SceneFlowBatchExporter] Failed to load PLY for frame {frameIndex}");
                continue;
            }

            // Map PLY frame indices to BVH frame indices using frame mapper
            float currentFrameTime = frameIndex * bvhData.FrameTime;
            float previousFrameTime = (frameIndex - 1) * bvhData.FrameTime;

            int currentBvhFrame = frameMapper.GetTargetFrameForTime(currentFrameTime, bvhData, driftData);
            int previousBvhFrame = frameMapper.GetTargetFrameForTime(previousFrameTime, bvhData, driftData);

            Debug.Log($"[SceneFlowBatchExporter] Frame {frameIndex}: Mapped to BVH frames ({previousBvhFrame}, {currentBvhFrame})");

            // Calculate bone segments for frame pair using mapped BVH frames
            sceneFlowCalculator.CalculateBoneSegmentsForFramePair(currentBvhFrame, previousBvhFrame);

            // Calculate motion vectors
            Vector3[] motionVectors = sceneFlowCalculator.CalculateMotionVectorsForMesh(mesh);

            // Get Joint_torso_7 position if available (filename uses frameIndex, position uses currentBvhFrame)
            string[] headerComments = BvhJointUtility.GetJointPositionComments(frameIndex, currentBvhFrame);

            // Export to PLY
            if (exportAsAscii)
            {
                PlyExporter.ExportToPLY_ASCII(mesh, motionVectors, filePath, headerComments);
            }
            else
            {
                PlyExporter.ExportToPLY(mesh, motionVectors, filePath, headerComments);
            }

            // Log progress
            float progress = (float)(frameIndex - startFrame + 1) / totalFramesToExport * 100f;
            float elapsed = Time.realtimeSinceStartup - exportStartTime;
            float estimatedTotal = elapsed / (frameIndex - startFrame + 1) * totalFramesToExport;
            float remaining = estimatedTotal - elapsed;

            Debug.Log($"[SceneFlowBatchExporter] Progress: {progress:F1}% ({frameIndex - startFrame + 1}/{totalFramesToExport}) - " +
                     $"Elapsed: {elapsed:F1}s, Remaining: ~{remaining:F1}s");

            // Yield to avoid blocking
            yield return null;
        }

        // Export complete
        float totalTime = Time.realtimeSinceStartup - exportStartTime;
        Debug.Log($"[SceneFlowBatchExporter] Batch export complete! {totalFramesToExport} frames exported in {totalTime:F1}s ({totalTime / totalFramesToExport:F2}s per frame)");
        Debug.Log($"[SceneFlowBatchExporter] Output directory: {absoluteOutputPath}");

        isExporting = false;
    }

    /// <summary>
    /// Discover PLY directory and cache file list from DatasetConfig
    /// </summary>
    private bool DiscoverPlyFiles()
    {
        if (datasetConfig == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] DatasetConfig not assigned. Cannot discover PLY directory.");
            return false;
        }

        string datasetRoot = datasetConfig.GetPointCloudRootDirectory();
        if (string.IsNullOrEmpty(datasetRoot))
        {
            Debug.LogError("[SceneFlowBatchExporter] Dataset root directory is empty in DatasetConfig.");
            return false;
        }

        // Try multiple possible PLY directory locations
        string[] possibleDirectories = new string[]
        {
            Path.Combine(datasetRoot, "PLY"),
            Path.Combine(datasetRoot, "PLY_WithMotion"),
            Path.Combine(datasetRoot, "Export"),
            datasetRoot
        };

        foreach (string dir in possibleDirectories)
        {
            if (Directory.Exists(dir))
            {
                string[] foundFiles = Directory.GetFiles(dir, "*.ply");
                if (foundFiles.Length > 0)
                {
                    resolvedPlyDirectory = dir;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(resolvedPlyDirectory))
        {
            Debug.LogError($"[SceneFlowBatchExporter] No PLY files found in dataset: {datasetRoot}");
            return false;
        }

        // Get sorted list of PLY files
        plyFiles = Directory.GetFiles(resolvedPlyDirectory, "*.ply");
        System.Array.Sort(plyFiles); // Ensure alphabetical order

        Debug.Log($"[SceneFlowBatchExporter] Found {plyFiles.Length} PLY files in: {resolvedPlyDirectory}");
        return plyFiles.Length > 0;
    }

    /// <summary>
    /// Get estimated time remaining in seconds
    /// </summary>
    public float GetEstimatedTimeRemaining()
    {
        if (!isExporting || currentExportFrame <= startFrame)
            return 0f;

        float elapsed = Time.realtimeSinceStartup - exportStartTime;
        int framesProcessed = currentExportFrame - startFrame + 1;
        float avgTimePerFrame = elapsed / framesProcessed;
        int framesRemaining = totalFramesToExport - framesProcessed;

        return avgTimePerFrame * framesRemaining;
    }

}
