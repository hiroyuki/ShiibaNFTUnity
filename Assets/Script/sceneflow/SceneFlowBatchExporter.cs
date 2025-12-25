using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Batch exporter for PLY files with scene flow (motion) vectors
/// Processes all frames and exports point cloud data with calculated motion vectors
/// </summary>
public class SceneFlowBatchExporter : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    [Tooltip("SceneFlowCalculator component for motion vector calculation")]
    private SceneFlowCalculator sceneFlowCalculator;

    [SerializeField]
    [Tooltip("MultiPointCloudView component for point cloud access")]
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

    // Progress tracking
    public bool IsExporting => isExporting;
    public float ExportProgress => totalFramesToExport > 0 ? (float)currentExportFrame / totalFramesToExport : 0f;
    public int CurrentFrame => currentExportFrame;
    public int TotalFrames => totalFramesToExport;

    private void Awake()
    {
        // Auto-find references if not set
        if (sceneFlowCalculator == null)
            sceneFlowCalculator = FindFirstObjectByType<SceneFlowCalculator>();

        if (multiPointCloudView == null)
            multiPointCloudView = FindFirstObjectByType<MultiPointCloudView>();
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

        if (sceneFlowCalculator == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] SceneFlowCalculator reference not set");
            return;
        }

        if (multiPointCloudView == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] MultiPointCloudView reference not set");
            return;
        }

        BvhData bvhData = BvhDataCache.GetBvhData();
        if (bvhData == null)
        {
            Debug.LogError("[SceneFlowBatchExporter] BvhData not available. Ensure MultiCameraPointCloudManager is initialized.");
            return;
        }

        // Determine frame range
        int maxFrame = bvhData.FrameCount - 1;
        int actualEndFrame = endFrame > 0 ? Mathf.Min(endFrame, maxFrame) : maxFrame;

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

            // Calculate bone segments for frame pair (current and previous)
            int previousFrame = frameIndex - 1;
            sceneFlowCalculator.CalculateBoneSegmentsForFramePair(frameIndex, previousFrame);

            // Get point cloud mesh
            Mesh mesh = GetPointCloudMesh();
            if (mesh == null)
            {
                Debug.LogWarning($"[SceneFlowBatchExporter] Failed to get mesh for frame {frameIndex}");
                continue;
            }

            // Calculate motion vectors
            Vector3[] motionVectors = sceneFlowCalculator.CalculateMotionVectorsForMesh(mesh);

            // Export to PLY
            if (exportAsAscii)
            {
                PlyExporter.ExportToPLY_ASCII(mesh, motionVectors, filePath);
            }
            else
            {
                PlyExporter.ExportToPLY(mesh, motionVectors, filePath);
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
    /// Get the current point cloud mesh from MultiPointCloudView
    /// </summary>
    private Mesh GetPointCloudMesh()
    {
        if (multiPointCloudView == null)
            return null;

        Transform unifiedViewer = multiPointCloudView.transform.Find("UnifiedPointCloudViewer");
        if (unifiedViewer == null)
        {
            Debug.LogWarning("[SceneFlowBatchExporter] UnifiedPointCloudViewer not found");
            return null;
        }

        MeshFilter meshFilter = unifiedViewer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[SceneFlowBatchExporter] MeshFilter or mesh not found on UnifiedPointCloudViewer");
            return null;
        }

        return meshFilter.sharedMesh;
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
