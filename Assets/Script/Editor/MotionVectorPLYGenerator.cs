using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

/// <summary>
/// Batch processing tool to generate PLY files with pre-computed motion vectors
/// Menu: Window → Shiiba → Generate Motion Vector PLY Files
///
/// Process:
/// 1. Select DatasetConfig (provides BVH path + PLY directory)
/// 2. Click "Generate" button
/// 3. Tool processes all PLY files, calculating motion vectors from BVH skeleton
/// 4. Outputs enhanced PLY files to PLY_WithMotion/ directory
///
/// Motion vectors include drift correction from DatasetConfig keyframes
/// </summary>
public class MotionVectorPLYGenerator : EditorWindow
{
    [MenuItem("Window/Shiiba/Generate Motion Vector PLY Files")]
    public static void ShowWindow()
    {
        GetWindow<MotionVectorPLYGenerator>("Motion Vector PLY Generator");
    }

    private DatasetConfig datasetConfig;
    private string outputFolderName = "PLY_WithMotion";
    private Vector2 scrollPos;
    private string statusLog = "";
    private bool isProcessing = false;
    private int fromFrame = 0;
    private int toFrame = 0; // 0 = all frames

    void OnGUI()
    {
        GUILayout.Label("Motion Vector PLY File Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Dataset Config Selection
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        datasetConfig = EditorGUILayout.ObjectField(
            "Dataset Config",
            datasetConfig,
            typeof(DatasetConfig),
            false
        ) as DatasetConfig;

        EditorGUILayout.Space();

        // Output Settings
        EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);
        outputFolderName = EditorGUILayout.TextField("Output Folder Name", outputFolderName);

        EditorGUILayout.BeginHorizontal();
        fromFrame = EditorGUILayout.IntField(
            new GUIContent("From Frame", "Start frame index (inclusive)"),
            fromFrame
        );
        toFrame = EditorGUILayout.IntField(
            new GUIContent("To Frame (0 = all)", "End frame index (inclusive), or 0 to process all frames from start"),
            toFrame
        );
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Generate Button
        GUI.enabled = !isProcessing && datasetConfig != null;
        if (GUILayout.Button("Generate Motion Vector PLY Files", GUILayout.Height(40)))
        {
            GenerateMotionVectorPLYFiles();
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        // Status Log
        EditorGUILayout.LabelField("Status Log", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
        EditorGUILayout.TextArea(statusLog, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    void GenerateMotionVectorPLYFiles()
    {
        if (datasetConfig == null)
        {
            LogStatus("ERROR: No DatasetConfig selected");
            return;
        }

        isProcessing = true;
        statusLog = "";
        LogStatus("=== Starting Motion Vector PLY Generation ===\n");

        try
        {
            // 1. Load BVH data
            LogStatus("Step 1: Loading BVH data...");
            string bvhPath = datasetConfig.GetBvhFilePath();
            if (string.IsNullOrEmpty(bvhPath) || !File.Exists(bvhPath))
            {
                LogStatus($"ERROR: BVH file not found at {bvhPath}");
                return;
            }

            // 2. Initialize BvhDataCache using DatasetConfig
            BvhDataCache.InitializeWithConfig(datasetConfig);

            // 3. Discover PLY files
            LogStatus("Step 2: Discovering PLY files...");
            string plyDir = datasetConfig.GetPlyDirectory();
            if (string.IsNullOrEmpty(plyDir) || !Directory.Exists(plyDir))
            {
                LogStatus($"ERROR: PLY directory not found at {plyDir}");
                return;
            }

            string[] plyFiles = Directory.GetFiles(plyDir, "*.ply")
                .Where(f => !f.EndsWith(".meta"))
                .OrderBy(f => f)
                .ToArray();

            if (plyFiles.Length == 0)
            {
                LogStatus($"ERROR: No PLY files found in {plyDir}");
                return;
            }

            // Apply frame range
            int startFrame = Mathf.Max(0, fromFrame);
            int endFrame = toFrame > 0 ? Mathf.Min(toFrame, plyFiles.Length - 1) : plyFiles.Length - 1;

            if (startFrame > endFrame || startFrame >= plyFiles.Length)
            {
                LogStatus($"ERROR: Invalid frame range. Start: {startFrame}, End: {endFrame}, Total: {plyFiles.Length}");
                return;
            }

            int framesToProcess = endFrame - startFrame + 1;

            LogStatus($"✓ Found {plyFiles.Length} PLY files");
            LogStatus($"  Processing frames {startFrame} to {endFrame} ({framesToProcess} frames)\n");

            // 4. Create output directory
            LogStatus("Step 3: Creating output directory...");
            string outputDir = Path.Combine(Path.GetDirectoryName(plyDir), outputFolderName);
            Directory.CreateDirectory(outputDir);
            LogStatus($"✓ Output directory: {outputDir}\n");

            // 5. Setup SceneFlowCalculator
            LogStatus("Step 4: Setting up SceneFlowCalculator...");
            GameObject calcGO = new GameObject("BatchSceneFlowCalculator");
            SceneFlowCalculator calculator = calcGO.AddComponent<SceneFlowCalculator>();
            LogStatus("✓ SceneFlowCalculator ready\n");

            // 6. Process each frame
            LogStatus($"Step 5: Processing {framesToProcess} frame(s)...\n");

            for (int i = 0; i < framesToProcess; i++)
            {
                int frameIndex = startFrame + i;

                // Update progress bar
                float progress = (float)(i + 1) / framesToProcess;
                EditorUtility.DisplayProgressBar(
                    "Generating Motion Vector PLY Files",
                    $"Processing frame {frameIndex} ({i + 1}/{framesToProcess})",
                    progress
                );

                try
                {
                    ProcessFrame(frameIndex, plyFiles, calculator, outputDir, BvhDataCache.GetBvhData());
                    LogStatus($"  [Frame {frameIndex}] ✓ Processed {Path.GetFileName(plyFiles[frameIndex])}");
                }
                catch (System.Exception e)
                {
                    LogStatus($"  [Frame {frameIndex}] ✗ ERROR: {e.Message}");
                    Debug.LogError($"Error processing frame {frameIndex}:\n{e}");
                    Debug.LogException(e);
                }

                // Allow UI to update
                if (i % 10 == 0)
                {
                    Repaint();
                }
            }

            EditorUtility.ClearProgressBar();

            // 7. Cleanup
            DestroyImmediate(calcGO);

            LogStatus($"\n=== Generation Complete ===");
            LogStatus($"Processed {framesToProcess} frames");
            LogStatus($"Output location: {outputDir}");
        }
        catch (System.Exception e)
        {
            LogStatus($"\nFATAL ERROR: {e.Message}");
            Debug.LogError($"Fatal error in motion vector generation: {e}");
            EditorUtility.ClearProgressBar();
        }
        finally
        {
            isProcessing = false;
        }
    }

    void ProcessFrame(int frameIndex, string[] plyFiles, SceneFlowCalculator calculator, string outputDir, BvhData bvhData)
    {
        // Load current frame mesh
        Mesh mesh = PlyImporter.ImportFromPLY(plyFiles[frameIndex]);
        if (mesh == null)
        {
            throw new System.Exception($"Failed to import PLY file: {plyFiles[frameIndex]}");
        }

        // Calculate bone segments for frame pair
        int previousFrame = Mathf.Max(0, frameIndex - 1); // Frame 0 uses itself as previous
        calculator.CalculateBoneSegmentsForFramePair(frameIndex, previousFrame);

        // Calculate motion vectors for mesh
        Vector3[] motionVectors = calculator.CalculateMotionVectorsForMesh(mesh);

        if (motionVectors.Length != mesh.vertexCount)
        {
            throw new System.Exception($"Motion vector count mismatch: {motionVectors.Length} vs {mesh.vertexCount}");
        }

        // Export enhanced PLY
        string filename = Path.GetFileName(plyFiles[frameIndex]);
        string outputPath = Path.Combine(outputDir, filename);
        // PlyExporter.ExportToPLY(mesh, motionVectors, outputPath);
        PlyExporter.ExportToPLY(mesh, motionVectors, outputPath);

        // Verify export
        if (!File.Exists(outputPath))
        {
            throw new System.Exception($"Failed to export PLY file: {outputPath}");
        }

        // Cleanup
        DestroyImmediate(mesh);
    }

    void LogStatus(string message)
    {
        statusLog += message + "\n";
        Debug.Log($"[MotionVectorPLYGenerator] {message}");
    }
}
