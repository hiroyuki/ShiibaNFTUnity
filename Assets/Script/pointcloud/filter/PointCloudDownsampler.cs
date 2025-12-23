using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Point cloud downsampler component for uniform reduction of point cloud density.
/// Samples vertices uniformly based on target percentage with real-time preview and PLY export.
/// </summary>
public class PointCloudDownsampler : MonoBehaviour
{
    [Header("Downsampling Configuration")]
    [SerializeField]
    [Tooltip("Percentage of vertices to keep (1-100%)")]
    [Range(1f, 100f)]
    private float targetVertexPercentage = 20f;

    [SerializeField]
    [Tooltip("Enable real-time preview during Play mode")]
    private bool enablePreview = true;

    [Header("Visualization")]
    [SerializeField]
    [Tooltip("Point size for downsampled vertices (overrides global setting)")]
    [Range(0.1f, 20f)]
    private float pointSize = 8.0f;

    [SerializeField]
    [Tooltip("Override vertex colors with custom color for visualization")]
    private bool overrideColors = false;

    [SerializeField]
    [Tooltip("Color for kept vertices (only used when Override Colors is enabled)")]
    private Color keptVertexColor = Color.green;

    [SerializeField]
    [Tooltip("Show discarded vertices as separate mesh (debug mode)")]
    private bool showDiscardedVertices = false;

    [SerializeField]
    [Tooltip("Color for discarded vertices (optional visualization)")]
    private Color discardedVertexColor = new Color(1f, 0f, 0f, 0.2f);

    [Header("PLY Export")]
    [SerializeField]
    [Tooltip("Enable automatic PLY export")]
    private bool enablePlyExport = false;

    [SerializeField]
    [Tooltip("Subdirectory name for filtered PLY files")]
    private string plyExportSubdirectory = "PLY_Filtered";

    [SerializeField]
    [Tooltip("Export every frame during playback")]
    private bool exportPerFrame = false;

    [Header("Debug Info")]
    [SerializeField]
    [Tooltip("Show downsampling statistics in console")]
    private bool showDebugInfo = true;

    [Header("Status (Read-Only)")]
    [SerializeField, ReadOnlyWhenPlaying]
    private int originalVertexCount = 0;

    [SerializeField, ReadOnlyWhenPlaying]
    private int currentDownsampledCount = 0;

    [SerializeField, ReadOnlyWhenPlaying]
    private float lastProcessingTimeMs = 0f;

    // Runtime state
    private Mesh lastProcessedMesh = null;
    private int lastMeshVertexCount = 0;
    private Mesh cachedOriginalMesh = null;
    private Mesh cachedDownsampledMesh = null;
    private DownsampledMeshData cachedDownsampledData = null; // Store original colors for export
    private GameObject downsampledVisualizerObject = null; // Separate GameObject for downsampled visualization
    private GameObject discardedVisualizerObject = null;
    private bool isDownsamplingActive = false;
    private bool hasAppliedInitialDownsampling = false;

    #region Context Menu Methods

    [ContextMenu("Apply Downsampling")]
    public void ApplyDownsamplingManual()
    {
        ApplyDownsampling();
    }

[ContextMenu("Export Downsampled PLY")]
    public void ExportDownsampledPLYManual()
    {
        if (!isDownsamplingActive || cachedDownsampledMesh == null)
        {
            Debug.LogWarning("[PointCloudDownsampler] No downsampled mesh available. Apply downsampling first.");
            return;
        }

        int frameIndex = GetCurrentFrameIndex();
        ExportDownsampledPLY(cachedDownsampledMesh, frameIndex);
    }

    [ContextMenu("Batch Export All Frames (Play Mode)")]
    public void BatchExportAllFramesPlayMode()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[PointCloudDownsampler] This batch export requires Play mode. Use 'Batch Export All Frames (Load PLYs)' for Edit mode.");
            return;
        }

        if (!isDownsamplingActive)
        {
            Debug.LogError("[PointCloudDownsampler] Downsampling not active. Enable Preview first and wait for initial mesh to load.");
            return;
        }

        Debug.Log($"[PointCloudDownsampler] 🚀 Starting batch export in Play mode (uses Timeline playback)...");
        StartCoroutine(BatchExportPlayModeCoroutine());
    }

    private System.Collections.IEnumerator BatchExportPlayModeCoroutine()
    {
        // Find Timeline controller
        UnityEngine.Playables.PlayableDirector director = FindFirstObjectByType<UnityEngine.Playables.PlayableDirector>();
        if (director == null)
        {
            Debug.LogError("[PointCloudDownsampler] Cannot find PlayableDirector for Timeline control");
            yield break;
        }

        // Get timeline duration (in seconds)
        double duration = director.duration;
        int fps = 30; // Assume 30 FPS
        int totalFrames = Mathf.FloorToInt((float)duration * fps);

        Debug.Log($"[PointCloudDownsampler] Timeline duration: {duration}s, Total frames: {totalFrames}");

        int exportedCount = 0;
        int skippedCount = 0;

        // Pause timeline playback
        bool wasPlaying = director.state == UnityEngine.Playables.PlayState.Playing;
        director.Pause();

        // Export each frame
        for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            // Seek to frame time
            double frameTime = frameIndex / (double)fps;
            director.time = frameTime;
            director.Evaluate();

            // Wait for mesh update (give it 2 frames to process)
            yield return null;
            yield return null;

            // Export current downsampled mesh
            if (cachedDownsampledMesh != null && cachedDownsampledMesh.vertexCount > 0)
            {
                ExportDownsampledPLY(cachedDownsampledMesh, frameIndex);
                exportedCount++;
            }
            else
            {
                Debug.LogWarning($"[PointCloudDownsampler] Frame {frameIndex}: No downsampled mesh available");
                skippedCount++;
            }

            // Progress feedback every 10 frames
            if (frameIndex % 10 == 0)
            {
                Debug.Log($"[PointCloudDownsampler] Progress: {frameIndex}/{totalFrames} frames exported");
            }
        }

        // Restore playback state
        if (wasPlaying)
        {
            director.Play();
        }

        Debug.Log($"[PointCloudDownsampler] ✅ Batch export complete! Exported: {exportedCount}, Skipped: {skippedCount}");
    }

    [ContextMenu("Batch Export All Frames (Load PLYs)")]
    public void BatchExportAllFramesFromPLY()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("[PointCloudDownsampler] Batch export from PLY files should be run in Edit mode, not Play mode. Use 'Batch Export All Frames (Play Mode)' instead.");
            return;
        }

        Debug.Log($"[PointCloudDownsampler] 🚀 Starting batch export from PLY files...");
        StartCoroutine(BatchExportFromPLYCoroutine());
    }

    private System.Collections.IEnumerator BatchExportFromPLYCoroutine()
    {
        // Get dataset root directory
        string datasetRoot = GetDatasetRoot();
        if (string.IsNullOrEmpty(datasetRoot))
        {
            Debug.LogError("[PointCloudDownsampler] Cannot determine dataset root directory");
            yield break;
        }

        Debug.Log($"[PointCloudDownsampler] Dataset root: {datasetRoot}");

        // Try multiple possible PLY directory locations
        string[] possibleDirectories = new string[]
        {
            Path.Combine(datasetRoot, "PLY"),
            Path.Combine(datasetRoot, "Export"),
            Path.Combine(datasetRoot, "PLY_Motion"),
            datasetRoot
        };

        string plyDirectory = null;
        string[] plyFiles = null;

        foreach (string dir in possibleDirectories)
        {
            Debug.Log($"[PointCloudDownsampler] Checking directory: {dir}");

            if (Directory.Exists(dir))
            {
                string[] foundFiles = Directory.GetFiles(dir, "*.ply");
                Debug.Log($"[PointCloudDownsampler] Found {foundFiles.Length} PLY files in {dir}");

                if (foundFiles.Length > 0)
                {
                    plyDirectory = dir;
                    plyFiles = foundFiles;
                    break;
                }
            }
            else
            {
                Debug.Log($"[PointCloudDownsampler] Directory does not exist: {dir}");
            }
        }

        if (plyFiles == null || plyFiles.Length == 0)
        {
            Debug.LogError($"[PointCloudDownsampler] No PLY files found. Searched in:\n" +
                          $"  - {Path.Combine(datasetRoot, "PLY")}\n" +
                          $"  - {Path.Combine(datasetRoot, "Export")}\n" +
                          $"  - {Path.Combine(datasetRoot, "PLY_Motion")}\n" +
                          $"  - {datasetRoot}");
            yield break;
        }

        Debug.Log($"[PointCloudDownsampler] Using PLY directory: {plyDirectory} ({plyFiles.Length} files)");

        int exportedCount = 0;
        int skippedCount = 0;

        for (int i = 0; i < plyFiles.Length; i++)
        {
            string plyFilePath = plyFiles[i];
            string filename = Path.GetFileNameWithoutExtension(plyFilePath);

            // Import PLY file
            Mesh loadedMesh = PlyImporter.ImportFromPLY(plyFilePath);
            if (loadedMesh == null || loadedMesh.vertexCount == 0)
            {
                Debug.LogWarning($"[PointCloudDownsampler] Failed to load PLY: {filename}");
                skippedCount++;
                continue;
            }

            // Extract mesh data
            Vector3[] vertices = loadedMesh.vertices;
            Color32[] colors = loadedMesh.colors32;
            Vector3[] motionVectors = ExtractMotionVectors(loadedMesh);

            // Apply downsampling
            int targetCount = CalculateTargetVertexCount(vertices.Length);
            DownsampledMeshData result = PointCloudDownsampleUtility.DownsampleUniform(
                vertices,
                colors,
                motionVectors,
                targetCount
            );

            // Create downsampled mesh
            Mesh downsampledMesh = new Mesh();
            downsampledMesh.name = $"Downsampled_{filename}";
            downsampledMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            downsampledMesh.vertices = result.vertices;
            downsampledMesh.colors32 = result.colors;

            if (result.HasMotionVectors)
            {
                downsampledMesh.SetUVs(1, new List<Vector3>(result.motionVectors));
            }

            int[] indices = new int[result.vertices.Length];
            for (int j = 0; j < indices.Length; j++)
                indices[j] = j;

            downsampledMesh.SetIndices(indices, MeshTopology.Points, 0);

            // Export downsampled PLY
            ExportDownsampledPLY(downsampledMesh, i);
            exportedCount++;

            // Progress feedback every 10 frames
            if (i % 10 == 0)
            {
                Debug.Log($"[PointCloudDownsampler] Progress: {i}/{plyFiles.Length} files processed");
            }

            // Cleanup
            Destroy(loadedMesh);
            Destroy(downsampledMesh);

            // Yield to prevent freezing
            yield return null;
        }

        Debug.Log($"[PointCloudDownsampler] ✅ Batch export complete! Exported: {exportedCount}, Skipped: {skippedCount}");
    }

    [ContextMenu("Reset to Original")]
    public void ResetToOriginal()
    {
        // Hide downsampled visualizer (original point cloud remains visible in UnifiedPointCloudViewer)
        if (downsampledVisualizerObject != null)
        {
            downsampledVisualizerObject.SetActive(false);
        }

        // Hide discarded vertices visualizer
        if (discardedVisualizerObject != null)
        {
            discardedVisualizerObject.SetActive(false);
        }

        isDownsamplingActive = false;

        if (showDebugInfo)
            Debug.Log("[PointCloudDownsampler] Reset to original visualization (downsampled viewer hidden)");
    }

    #endregion

    #region Unity Lifecycle

    void OnValidate()
    {
        // Trigger re-downsampling when target vertex count changes in Inspector
        if (Application.isPlaying && isDownsamplingActive)
        {
            if (showDebugInfo)
                Debug.Log("[PointCloudDownsampler] 🔄 Inspector value changed, re-applying downsampling...");

            ApplyDownsampling();
        }
    }

    void Update()
    {
        if (!enablePreview || !Application.isPlaying)
            return;

        // Poll for mesh changes
        Mesh currentMesh = GetCurrentUnifiedMesh();
        if (currentMesh == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("[PointCloudDownsampler] Waiting for unified mesh to become available...");
            return;
        }

        // Detect mesh change (new frame loaded) or initial downsampling needed
        bool meshChanged = (currentMesh != lastProcessedMesh)
                        || (currentMesh.vertexCount != lastMeshVertexCount)
                        || !hasAppliedInitialDownsampling;

        if (meshChanged)
        {
            if (showDebugInfo)
            {
                if (!hasAppliedInitialDownsampling)
                    Debug.Log("[PointCloudDownsampler] ⚡ Triggering INITIAL downsampling...");
                else
                    Debug.Log($"[PointCloudDownsampler] ⚡ Triggering downsampling (mesh changed: {currentMesh.vertexCount} vertices)");
            }

            ApplyDownsampling();
            lastProcessedMesh = currentMesh;
            lastMeshVertexCount = currentMesh.vertexCount;
            hasAppliedInitialDownsampling = true;

            // Optional: per-frame export
            if (enablePlyExport && exportPerFrame)
            {
                int frameIndex = GetCurrentFrameIndex();
                ExportDownsampledPLY(cachedDownsampledMesh, frameIndex);
            }
        }
    }

    void OnDestroy()
    {
        // Cleanup cached meshes
        if (cachedDownsampledMesh != null)
            Destroy(cachedDownsampledMesh);

        if (downsampledVisualizerObject != null)
            Destroy(downsampledVisualizerObject);

        if (discardedVisualizerObject != null)
            Destroy(discardedVisualizerObject);
    }

    #endregion

    #region Core Downsampling Logic

    private void ApplyDownsampling()
    {
        Mesh sourceMesh = GetCurrentUnifiedMesh();
        if (sourceMesh == null || sourceMesh.vertexCount == 0)
        {
            if (showDebugInfo)
                Debug.LogWarning("[PointCloudDownsampler] No mesh available for downsampling");
            return;
        }

        // Cache original mesh
        if (cachedOriginalMesh == null || cachedOriginalMesh != sourceMesh)
        {
            cachedOriginalMesh = sourceMesh;
        }

        // Extract data from source mesh
        Vector3[] vertices = sourceMesh.vertices;
        Color32[] colors = sourceMesh.colors32;
        Vector3[] motionVectors = ExtractMotionVectors(sourceMesh);

        // Update status
        originalVertexCount = vertices.Length;

        // Perform downsampling
        int targetCount = CalculateTargetVertexCount(vertices.Length);
        DownsampledMeshData result = PointCloudDownsampleUtility.DownsampleUniform(
            vertices,
            colors,
            motionVectors,
            targetCount
        );

        // Update status
        currentDownsampledCount = result.keptCount;
        lastProcessingTimeMs = result.processingTimeMs;

        // Cache the downsampled data (preserves original colors for export)
        cachedDownsampledData = result;

        // Create downsampled mesh
        if (cachedDownsampledMesh == null)
        {
            cachedDownsampledMesh = new Mesh();
            cachedDownsampledMesh.name = "DownsampledPointCloud";
        }

        UpdateDownsampledMesh(cachedDownsampledMesh, result);

        // Create or update separate visualizer for downsampled point cloud
        CreateOrUpdateDownsampledVisualizer(cachedDownsampledMesh);
        isDownsamplingActive = true;

        // Optional: visualize discarded vertices
        if (showDiscardedVertices)
        {
            VisualizeDiscardedVertices(vertices, colors, result);
        }
        else if (discardedVisualizerObject != null)
        {
            discardedVisualizerObject.SetActive(false);
        }
    }

    private void UpdateDownsampledMesh(Mesh mesh, DownsampledMeshData data)
    {
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = data.vertices;

        // Apply colors based on visualization mode
        if (overrideColors)
        {
            // Override with kept vertex color for visualization
            Color32[] colors = new Color32[data.vertices.Length];
            Color32 keptColor32 = keptVertexColor;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = keptColor32;
            }
            mesh.colors32 = colors;
        }
        else
        {
            // Use original colors from downsampled data (preserves point cloud color information)
            mesh.colors32 = data.colors;
        }

        // Preserve motion vectors if present
        if (data.HasMotionVectors)
        {
            mesh.SetUVs(1, new List<Vector3>(data.motionVectors));
        }

        // Setup indices for point cloud rendering
        int[] indices = new int[data.vertices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();
    }

    private void CreateOrUpdateDownsampledVisualizer(Mesh downsampledMesh)
    {
        // Create visualizer object if needed
        if (downsampledVisualizerObject == null)
        {
            downsampledVisualizerObject = new GameObject("DownsampledPointCloudViewer");
            downsampledVisualizerObject.transform.SetParent(transform, false);

            MeshFilter meshFilter = downsampledVisualizerObject.AddComponent<MeshFilter>();
            meshFilter.mesh = downsampledMesh;

            MeshRenderer renderer = downsampledVisualizerObject.AddComponent<MeshRenderer>();

            // Setup point cloud material (uses Unlit/VertexColor shader)
            Material pointCloudMaterial = new Material(Shader.Find("Unlit/VertexColor"));
            pointCloudMaterial.SetFloat("_PointSize", pointSize);
            pointCloudMaterial.SetFloat("_Opacity", 1.0f);
            renderer.material = pointCloudMaterial;

            if (showDebugInfo)
                Debug.Log("[PointCloudDownsampler] Created separate downsampled visualizer GameObject");
        }
        else
        {
            // Update existing visualizer
            MeshFilter meshFilter = downsampledVisualizerObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                meshFilter.mesh = downsampledMesh;
            }
        }

        // Update material properties from Inspector values
        MeshRenderer meshRenderer = downsampledVisualizerObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            meshRenderer.material.SetFloat("_PointSize", pointSize);
            meshRenderer.material.SetFloat("_Opacity", 1.0f);
        }

        downsampledVisualizerObject.SetActive(true);
    }

    private void VisualizeDiscardedVertices(Vector3[] originalVertices, Color32[] originalColors, DownsampledMeshData downsampledData)
    {
        // Get indices of discarded vertices
        int targetCount = CalculateTargetVertexCount(originalVertices.Length);
        int[] discardedIndices = PointCloudDownsampleUtility.GetDiscardedIndices(
            originalVertices.Length,
            targetCount
        );

        if (discardedIndices.Length == 0)
            return;

        // Create discarded vertices mesh
        Vector3[] discardedVertices = new Vector3[discardedIndices.Length];
        for (int i = 0; i < discardedIndices.Length; i++)
        {
            discardedVertices[i] = originalVertices[discardedIndices[i]];
        }

        // Create visualizer object if needed
        if (discardedVisualizerObject == null)
        {
            discardedVisualizerObject = new GameObject("DiscardedVerticesVisualizer");
            discardedVisualizerObject.transform.SetParent(transform, false);
            discardedVisualizerObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = discardedVisualizerObject.AddComponent<MeshRenderer>();

            // Setup point cloud material (uses custom point size from downsampler)
            Material pointCloudMaterial = new Material(Shader.Find("Unlit/VertexColor"));
            pointCloudMaterial.SetFloat("_PointSize", pointSize);
            pointCloudMaterial.SetFloat("_Opacity", 1.0f); // Full opacity - color alpha is handled in vertex colors
            renderer.material = pointCloudMaterial;
        }

        // Update material opacity and point size from Inspector values
        MeshRenderer meshRenderer = discardedVisualizerObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            meshRenderer.material.SetFloat("_Opacity", discardedVertexColor.a / 255f);
            meshRenderer.material.SetFloat("_PointSize", pointSize);
        }

        discardedVisualizerObject.SetActive(true);

        // Update mesh
        Mesh discardedMesh = discardedVisualizerObject.GetComponent<MeshFilter>().mesh;
        if (discardedMesh == null)
        {
            discardedMesh = new Mesh();
            discardedVisualizerObject.GetComponent<MeshFilter>().mesh = discardedMesh;
        }

        discardedMesh.Clear();
        discardedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        discardedMesh.vertices = discardedVertices;

        // Use discarded vertex color (with full alpha, opacity controlled by material)
        Color32[] discardedColors = new Color32[discardedVertices.Length];
        // Convert Color to Color32, but override alpha to 255 (full opacity in vertex, transparency via material)
        Color32 baseColor = discardedVertexColor;
        Color32 discardedColor32 = new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
        for (int i = 0; i < discardedColors.Length; i++)
        {
            discardedColors[i] = discardedColor32;
        }
        discardedMesh.colors32 = discardedColors;

        // IMPORTANT: Use Points topology for point cloud rendering
        int[] indices = new int[discardedVertices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        discardedMesh.SetIndices(indices, MeshTopology.Points, 0);
        discardedMesh.RecalculateBounds();
    }

    #endregion

    #region PLY Export

    private void ExportDownsampledPLY(Mesh mesh, int frameIndex)
    {
        if (mesh == null || mesh.vertexCount == 0)
        {
            Debug.LogWarning("[PointCloudDownsampler] Cannot export empty mesh");
            return;
        }

        // Get dataset root directory
        string datasetRoot = GetDatasetRoot();
        if (string.IsNullOrEmpty(datasetRoot))
        {
            Debug.LogError("[PointCloudDownsampler] Cannot determine dataset root directory");
            return;
        }

        // Create export directory
        string exportDir = Path.Combine(datasetRoot, plyExportSubdirectory);
        try
        {
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
                if (showDebugInfo)
                    Debug.Log($"[PointCloudDownsampler] Created export directory: {exportDir}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PointCloudDownsampler] Failed to create export directory: {ex.Message}");
            return;
        }

        // Generate filename
        string fileName = GenerateFilteredPlyFileName(frameIndex);
        string filePath = Path.Combine(exportDir, fileName);

        // Create temporary mesh with original colors for export
        // (in case overrideColors is enabled for visualization)
        Mesh exportMesh = new Mesh();
        exportMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        if (cachedDownsampledData != null)
        {
            // Use cached data with original colors
            exportMesh.vertices = cachedDownsampledData.vertices;
            exportMesh.colors32 = cachedDownsampledData.colors;

            if (cachedDownsampledData.HasMotionVectors)
            {
                exportMesh.SetUVs(1, new List<Vector3>(cachedDownsampledData.motionVectors));
            }
        }
        else
        {
            // Fallback: use current mesh data
            exportMesh.vertices = mesh.vertices;
            exportMesh.colors32 = mesh.colors32;

            List<Vector3> uv1 = new List<Vector3>();
            mesh.GetUVs(1, uv1);
            if (uv1.Count > 0)
            {
                exportMesh.SetUVs(1, uv1);
            }
        }

        // Extract motion vectors
        Vector3[] motionVectors = ExtractMotionVectors(exportMesh);

        // Export using existing PlyExporter
        try
        {
            PlyExporter.ExportToPLY(exportMesh, motionVectors, filePath);

            if (showDebugInfo)
                Debug.Log($"[PointCloudDownsampler] Exported {exportMesh.vertexCount} vertices to: {filePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PointCloudDownsampler] Failed to export PLY: {ex.Message}");
        }
        finally
        {
            // Cleanup temporary mesh
            Destroy(exportMesh);
        }
    }

    private string GenerateFilteredPlyFileName(int frameIndex)
    {
        // Format: filtered_frame_XXXX.ply (preserves original naming, adds prefix)
        // Example: filtered_frame_0042.ply
        return $"filtered_frame_{frameIndex:D4}.ply";
    }

    #endregion

    #region Helper Methods

    private Mesh GetCurrentUnifiedMesh()
    {
        MultiPointCloudView view = FindFirstObjectByType<MultiPointCloudView>();
        if (view == null)
            return null;

        return view.GetUnifiedMesh();
    }

    private MeshFilter GetUnifiedMeshFilter()
    {
        MultiPointCloudView view = FindFirstObjectByType<MultiPointCloudView>();
        if (view == null)
            return null;

        Transform unifiedViewer = view.transform.Find("UnifiedPointCloudViewer");
        if (unifiedViewer == null)
            return null;

        return unifiedViewer.GetComponent<MeshFilter>();
    }

    private Vector3[] ExtractMotionVectors(Mesh mesh)
    {
        if (mesh == null)
            return null;

        List<Vector3> uv1List = new List<Vector3>();
        mesh.GetUVs(1, uv1List);

        if (uv1List.Count == 0)
            return null;

        return uv1List.ToArray();
    }

    /// <summary>
    /// Calculate target vertex count from percentage
    /// </summary>
    private int CalculateTargetVertexCount(int originalCount)
    {
        float percentage = Mathf.Clamp(targetVertexPercentage, 1f, 100f);
        int targetCount = Mathf.Max(1, Mathf.RoundToInt(originalCount * percentage / 100f));
        return targetCount;
    }

    private int GetCurrentFrameIndex()
    {
        // Try to get frame info from Timeline
        PlayableDirector director = FindFirstObjectByType<PlayableDirector>();
        if (director != null && director.playableAsset != null)
        {
            double time = director.time;
            // Assume 30 FPS (or get from DatasetConfig if available)
            int fps = 30; // Default
            return Mathf.FloorToInt((float)time * fps);
        }

        return 0;
    }

    private string GetDatasetRoot()
    {
        // Try to get dataset root from DatasetConfig
        DatasetConfig config = DatasetConfig.GetInstance();
        if (config != null)
        {
            string datasetPath = config.GetPointCloudRootDirectory();
            if (!string.IsNullOrEmpty(datasetPath))
            {
                return datasetPath;
            }
        }

        // Fallback: try to find MultiCameraPointCloudManager
        MultiCameraPointCloudManager manager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (manager != null)
        {
            DatasetConfig managerConfig = manager.GetDatasetConfig();
            if (managerConfig != null)
            {
                string datasetPath = managerConfig.GetPointCloudRootDirectory();
                if (!string.IsNullOrEmpty(datasetPath))
                {
                    return datasetPath;
                }
            }
        }

        // Last resort: use Assets directory
        Debug.LogWarning("[PointCloudDownsampler] Could not determine dataset root from DatasetConfig, using Assets directory as fallback");
        return Application.dataPath;
    }

    #endregion
}

/// <summary>
/// Custom attribute to make fields read-only in the Inspector during Play mode
/// </summary>
public class ReadOnlyWhenPlayingAttribute : PropertyAttribute
{
}
