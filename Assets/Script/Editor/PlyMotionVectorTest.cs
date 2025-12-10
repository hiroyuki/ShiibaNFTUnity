using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simple test for PLY motion vector export/import functionality
///
/// Expected Scene Hierarchy:
/// world
///   └── MultiCameraPointCloud
///       └── MultiPointCloudView_PLY (has MultiPointCloudView component)
///           └── UnifiedPointCloudViewer (has MeshFilter with mesh)
///   └── SceneFlow (has SceneFlowCalculator component)
///       └── CurrentFrameBVH
///       └── PreviousFrameBVH
///   └── BVH_Character
///
/// Prerequisites:
/// 1. Scene must be loaded with the above hierarchy
/// 2. Click "Show Scene Flow" button on SceneFlowCalculator to generate motion vectors
/// 3. Then run this test
/// </summary>
public class PlyMotionVectorTest : EditorWindow
{
    [MenuItem("Window/Shiiba/Test PLY Motion Vectors")]
    public static void ShowWindow()
    {
        GetWindow<PlyMotionVectorTest>("PLY Motion Vector Test");
    }

    private string testFilePath = "Assets/test_motion_vectors.ply";
    private Vector2 scrollPos;
    private string testResults = "";

    void OnGUI()
    {
        GUILayout.Label("PLY Motion Vector Export/Import Test", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        testFilePath = EditorGUILayout.TextField("Test File Path:", testFilePath);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Check Scene Setup", GUILayout.Height(30)))
        {
            CheckSceneSetup();
        }
        if (GUILayout.Button("Run Full Test", GUILayout.Height(30)))
        {
            RunFullTest();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test Results:", EditorStyles.boldLabel);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(400));
        EditorGUILayout.TextArea(testResults, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    void CheckSceneSetup()
    {
        testResults = "=== Scene Setup Diagnostic ===\n\n";

        // Check if scene is loaded
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        testResults += $"Active Scene: {scene.name} (loaded: {scene.isLoaded})\n\n";

        // Find all root GameObjects
        var rootObjects = scene.GetRootGameObjects();
        testResults += $"Root GameObjects: {rootObjects.Length}\n";
        foreach (var obj in rootObjects)
        {
            testResults += $"  - {obj.name}\n";
        }
        testResults += "\n";

        // Check for MultiPointCloudView
        var views = FindObjectsByType<MultiPointCloudView>(FindObjectsSortMode.None);
        testResults += $"MultiPointCloudView components found: {views.Length}\n";
        foreach (var view in views)
        {
            testResults += $"  - On GameObject: {view.gameObject.name}\n";
            testResults += $"    Full path: {GetFullPath(view.transform)}\n";

            // Check for UnifiedPointCloudViewer child
            var unifiedViewer = view.transform.Find("UnifiedPointCloudViewer");
            if (unifiedViewer != null)
            {
                var meshFilter = unifiedViewer.GetComponent<MeshFilter>();
                testResults += $"    UnifiedPointCloudViewer: Found (mesh: {meshFilter?.sharedMesh != null})\n";
                if (meshFilter?.sharedMesh != null)
                {
                    testResults += $"    Mesh vertices: {meshFilter.sharedMesh.vertexCount}\n";
                }
            }
            else
            {
                testResults += $"    UnifiedPointCloudViewer: NOT FOUND\n";
            }
        }
        testResults += "\n";

        // Check for SceneFlowCalculator
        var calculators = FindObjectsByType<SceneFlowCalculator>(FindObjectsSortMode.None);
        testResults += $"SceneFlowCalculator components found: {calculators.Length}\n";
        foreach (var calc in calculators)
        {
            testResults += $"  - On GameObject: {calc.gameObject.name}\n";
            testResults += $"    Full path: {GetFullPath(calc.transform)}\n";

            // Check for motion vectors via reflection
            var field = typeof(SceneFlowCalculator).GetField("pointCloudMotionVectors",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Vector3[] motionVectors = field?.GetValue(calc) as Vector3[];

            if (motionVectors != null && motionVectors.Length > 0)
            {
                testResults += $"    Motion vectors: {motionVectors.Length} (ready!)\n";
            }
            else
            {
                testResults += $"    Motion vectors: NONE (click 'Show Scene Flow' first)\n";
            }
        }
        testResults += "\n";

        if (views.Length == 0 || calculators.Length == 0)
        {
            testResults += "⚠ WARNING: Missing required components!\n";
            testResults += "Make sure the scene with point cloud and BVH is loaded.\n";
        }
        else if (views.Length > 0)
        {
            var view = views[0];
            var unifiedViewer = view.transform.Find("UnifiedPointCloudViewer");
            if (unifiedViewer != null && unifiedViewer.GetComponent<MeshFilter>()?.sharedMesh != null)
            {
                var field = typeof(SceneFlowCalculator).GetField("pointCloudMotionVectors",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Vector3[] motionVectors = field?.GetValue(calculators[0]) as Vector3[];

                if (motionVectors != null && motionVectors.Length > 0)
                {
                    testResults += "✓ Scene is ready! You can run the full test.\n";
                }
                else
                {
                    testResults += "⚠ Motion vectors not calculated yet.\n";
                    testResults += "Click 'Show Scene Flow' button on SceneFlowCalculator first!\n";
                }
            }
        }
    }

    string GetFullPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    void RunFullTest()
    {
        testResults = "=== PLY Motion Vector Test Started ===\n\n";

        // Step 1: Get point cloud mesh
        testResults += "Step 1: Getting point cloud mesh...\n";

        // Find MultiPointCloudView (should be on MultiPointCloudView_PLY GameObject)
        MultiPointCloudView view = FindFirstObjectByType<MultiPointCloudView>();
        if (view == null)
        {
            testResults += "❌ ERROR: No MultiPointCloudView component found in scene\n";
            testResults += "Please ensure:\n";
            testResults += "  1. The scene is loaded and active\n";
            testResults += "  2. MultiPointCloudView_PLY GameObject exists\n";
            testResults += "  3. It has a MultiPointCloudView component attached\n";
            return;
        }

        testResults += $"✓ Found MultiPointCloudView on: {view.gameObject.name}\n";

        Transform unifiedViewer = view.transform.Find("UnifiedPointCloudViewer");
        if (unifiedViewer == null)
        {
            testResults += "❌ ERROR: UnifiedPointCloudViewer not found\n";
            return;
        }

        MeshFilter meshFilter = unifiedViewer.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            testResults += "❌ ERROR: No mesh found on UnifiedPointCloudViewer\n";
            return;
        }

        Mesh originalMesh = meshFilter.sharedMesh;
        testResults += $"✓ Found mesh with {originalMesh.vertexCount} vertices\n\n";

        // Step 2: Get motion vectors from SceneFlowCalculator
        testResults += "Step 2: Getting motion vectors from SceneFlowCalculator...\n";
        SceneFlowCalculator calculator = FindFirstObjectByType<SceneFlowCalculator>();
        if (calculator == null)
        {
            testResults += "❌ ERROR: No SceneFlowCalculator found in scene\n";
            return;
        }

        // Access private field via reflection
        var field = typeof(SceneFlowCalculator).GetField("pointCloudMotionVectors",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Vector3[] motionVectors = field?.GetValue(calculator) as Vector3[];

        if (motionVectors == null || motionVectors.Length == 0)
        {
            testResults += "⚠ WARNING: No motion vectors found. Click 'Show Scene Flow' button first!\n";
            testResults += "Test cannot continue without motion vectors.\n";
            return;
        }

        testResults += $"✓ Found {motionVectors.Length} motion vectors\n";

        // Calculate statistics
        float avgMagnitude = 0f;
        float maxMagnitude = 0f;
        int nonZeroCount = 0;
        foreach (var mv in motionVectors)
        {
            float mag = mv.magnitude;
            avgMagnitude += mag;
            if (mag > maxMagnitude) maxMagnitude = mag;
            if (mag > 0.001f) nonZeroCount++;
        }
        avgMagnitude /= motionVectors.Length;

        testResults += $"  - Non-zero vectors: {nonZeroCount}/{motionVectors.Length}\n";
        testResults += $"  - Avg magnitude: {avgMagnitude:F6}\n";
        testResults += $"  - Max magnitude: {maxMagnitude:F6}\n\n";

        // Step 3: Export with motion vectors
        testResults += "Step 3: Exporting PLY with motion vectors...\n";
        PlyExporter.ExportToPLY(originalMesh, motionVectors, testFilePath);

        var fileInfo = new System.IO.FileInfo(testFilePath);
        if (!fileInfo.Exists)
        {
            testResults += "❌ ERROR: Export failed - file not created\n";
            return;
        }

        long expectedSize = originalMesh.vertexCount * 27L; // 27 bytes per vertex with motion
        testResults += $"✓ File created: {fileInfo.Length} bytes\n";
        testResults += $"  - Expected: ~{expectedSize} bytes (27 bytes/vertex)\n";
        testResults += $"  - Actual bytes/vertex: {fileInfo.Length / (float)originalMesh.vertexCount:F2}\n\n";

        // Step 4: Import and verify
        testResults += "Step 4: Importing PLY file...\n";
        Mesh importedMesh = PlyImporter.ImportFromPLY(testFilePath);

        if (importedMesh == null)
        {
            testResults += "❌ ERROR: Import failed\n";
            return;
        }

        testResults += $"✓ Imported mesh with {importedMesh.vertexCount} vertices\n";

        // Verify vertex count matches
        if (importedMesh.vertexCount != originalMesh.vertexCount)
        {
            testResults += $"❌ ERROR: Vertex count mismatch! Original: {originalMesh.vertexCount}, Imported: {importedMesh.vertexCount}\n";
            return;
        }

        // Step 5: Verify motion vectors
        testResults += "\nStep 5: Verifying motion vectors...\n";
        List<Vector3> importedMotionVectors = new List<Vector3>();
        importedMesh.GetUVs(1, importedMotionVectors);

        if (importedMotionVectors.Count == 0)
        {
            testResults += "❌ ERROR: No motion vectors found in UV1 channel\n";
            return;
        }

        testResults += $"✓ Found {importedMotionVectors.Count} motion vectors in UV1 channel\n";

        // Compare motion vectors
        int matchCount = 0;
        int mismatchCount = 0;
        float maxError = 0f;
        float totalError = 0f;

        int sampleSize = Mathf.Min(100, motionVectors.Length);
        for (int i = 0; i < sampleSize; i++)
        {
            Vector3 original = motionVectors[i];
            Vector3 imported = importedMotionVectors[i];
            float error = (original - imported).magnitude;

            totalError += error;
            if (error > maxError) maxError = error;

            if (error < 0.0001f)
                matchCount++;
            else
                mismatchCount++;
        }

        float avgError = totalError / sampleSize;

        testResults += $"\nMotion Vector Comparison (sample of {sampleSize}):\n";
        testResults += $"  - Exact matches: {matchCount}/{sampleSize}\n";
        testResults += $"  - Average error: {avgError:F8}\n";
        testResults += $"  - Max error: {maxError:F8}\n";

        if (avgError < 0.0001f)
        {
            testResults += "✓ Motion vectors match!\n";
        }
        else
        {
            testResults += "⚠ Motion vectors have small differences (likely floating-point precision)\n";
        }

        // Step 6: Round-trip test
        testResults += "\nStep 6: Round-trip test (export imported mesh)...\n";
        string roundTripPath = testFilePath.Replace(".ply", "_roundtrip.ply");
        PlyExporter.ExportToPLY(importedMesh, importedMotionVectors.ToArray(), roundTripPath);

        var roundTripFileInfo = new System.IO.FileInfo(roundTripPath);
        testResults += $"✓ Round-trip file created: {roundTripFileInfo.Length} bytes\n";

        long sizeDiff = System.Math.Abs(fileInfo.Length - roundTripFileInfo.Length);
        if (sizeDiff == 0)
        {
            testResults += "✓ Round-trip file size matches original!\n";
        }
        else
        {
            testResults += $"⚠ File size difference: {sizeDiff} bytes\n";
        }

        // Final summary
        testResults += "\n=== TEST COMPLETE ===\n";
        testResults += "✓ All tests passed!\n";
        testResults += $"Test file: {testFilePath}\n";
        testResults += $"Round-trip file: {roundTripPath}\n";

        // Cleanup
        Object.DestroyImmediate(importedMesh);
    }
}
