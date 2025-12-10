using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Validation tool to test motion vector PLY import
/// Menu: Window → Shiiba → Validate Motion Vector PLY
///
/// Tests:
/// 1. Import PLY file with motion vectors
/// 2. Verify UV1 channel contains motion data
/// 3. Display statistics (min/max/average magnitude)
/// </summary>
public class MotionVectorPLYValidator : EditorWindow
{
    [MenuItem("Window/Shiiba/Validate Motion Vector PLY")]
    public static void ShowWindow()
    {
        GetWindow<MotionVectorPLYValidator>("Motion Vector PLY Validator");
    }

    private string plyFilePath = "";
    private Vector2 scrollPos;
    private string validationResults = "";

    void OnGUI()
    {
        GUILayout.Label("Motion Vector PLY Validator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // File Selection
        EditorGUILayout.BeginHorizontal();
        plyFilePath = EditorGUILayout.TextField("PLY File Path", plyFilePath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string selectedPath = EditorUtility.OpenFilePanel(
                "Select PLY File with Motion Vectors",
                "Assets/Data/Datasets",
                "ply"
            );
            if (!string.IsNullOrEmpty(selectedPath))
            {
                plyFilePath = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Quick select buttons
        EditorGUILayout.LabelField("Quick Select (Totori Dataset)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Frame 0"))
        {
            plyFilePath = "/Volumes/horristicSSD2T/repos/ShiibaNFTUnity/Assets/Data/Datasets/Totori/PLY_WithMotion/Totori_Best000000.ply";
        }
        if (GUILayout.Button("Frame 1"))
        {
            plyFilePath = "/Volumes/horristicSSD2T/repos/ShiibaNFTUnity/Assets/Data/Datasets/Totori/PLY_WithMotion/Totori_Best000001.ply";
        }
        if (GUILayout.Button("Frame 5"))
        {
            plyFilePath = "/Volumes/horristicSSD2T/repos/ShiibaNFTUnity/Assets/Data/Datasets/Totori/PLY_WithMotion/Totori_Best000005.ply";
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Validate Button
        GUI.enabled = !string.IsNullOrEmpty(plyFilePath);
        if (GUILayout.Button("Validate PLY File", GUILayout.Height(40)))
        {
            ValidatePLYFile();
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        // Results
        EditorGUILayout.LabelField("Validation Results", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(400));
        EditorGUILayout.TextArea(validationResults, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    void ValidatePLYFile()
    {
        validationResults = "";
        Log("=== Motion Vector PLY Validation ===\n");

        if (string.IsNullOrEmpty(plyFilePath))
        {
            Log("ERROR: No file path specified");
            return;
        }

        if (!System.IO.File.Exists(plyFilePath))
        {
            Log($"ERROR: File not found: {plyFilePath}");
            return;
        }

        try
        {
            Log($"File: {System.IO.Path.GetFileName(plyFilePath)}");
            Log($"Path: {plyFilePath}\n");

            // Import PLY file
            Log("Step 1: Importing PLY file...");
            Mesh mesh = PlyImporter.ImportFromPLY(plyFilePath);

            if (mesh == null)
            {
                Log("ERROR: Failed to import PLY file");
                return;
            }

            Log($"✓ Mesh imported successfully");
            Log($"  Vertex count: {mesh.vertexCount:N0}\n");

            // Check for motion vectors in UV1 channel
            Log("Step 2: Checking for motion vectors in UV1 channel...");
            List<Vector3> motionVectors = new List<Vector3>();
            mesh.GetUVs(1, motionVectors);

            if (motionVectors.Count == 0)
            {
                Log("✗ ERROR: No motion vectors found in UV1 channel");
                Log("  The PLY file may not have motion vector properties (vx, vy, vz)");
                return;
            }

            if (motionVectors.Count != mesh.vertexCount)
            {
                Log($"⚠ WARNING: Motion vector count mismatch");
                Log($"  Expected: {mesh.vertexCount:N0}");
                Log($"  Found: {motionVectors.Count:N0}");
            }
            else
            {
                Log($"✓ Motion vectors found in UV1 channel");
                Log($"  Count: {motionVectors.Count:N0}\n");
            }

            // Analyze motion vector statistics
            Log("Step 3: Analyzing motion vectors...");

            float minMagnitude = float.MaxValue;
            float maxMagnitude = float.MinValue;
            float totalMagnitude = 0f;
            int zeroVectors = 0;
            Vector3 minVector = Vector3.zero;
            Vector3 maxVector = Vector3.zero;

            foreach (var mv in motionVectors)
            {
                float magnitude = mv.magnitude;
                totalMagnitude += magnitude;

                if (magnitude < 0.0001f)
                {
                    zeroVectors++;
                }

                if (magnitude < minMagnitude)
                {
                    minMagnitude = magnitude;
                    minVector = mv;
                }

                if (magnitude > maxMagnitude)
                {
                    maxMagnitude = magnitude;
                    maxVector = mv;
                }
            }

            float avgMagnitude = totalMagnitude / motionVectors.Count;

            Log("Motion Vector Statistics:");
            Log($"  Min magnitude: {minMagnitude:F6}");
            Log($"  Max magnitude: {maxMagnitude:F6}");
            Log($"  Avg magnitude: {avgMagnitude:F6}");
            Log($"  Zero vectors: {zeroVectors:N0} ({(zeroVectors * 100f / motionVectors.Count):F2}%)");
            Log($"\n  Min vector: ({minVector.x:F4}, {minVector.y:F4}, {minVector.z:F4})");
            Log($"  Max vector: ({maxVector.x:F4}, {maxVector.y:F4}, {maxVector.z:F4})\n");

            // Sample motion vectors
            Log("Step 4: Sample motion vectors (first 10):");
            int sampleCount = Mathf.Min(10, motionVectors.Count);
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 mv = motionVectors[i];
                Log($"  [{i}] ({mv.x:F4}, {mv.y:F4}, {mv.z:F4}) | magnitude: {mv.magnitude:F6}");
            }

            Log("\n=== Validation Complete ===");
            Log("✓ PLY file successfully imported with motion vectors");
            Log("✓ Motion data is available in UV1 channel");

            if (zeroVectors < motionVectors.Count * 0.9f)
            {
                Log("✓ Motion vectors appear valid (< 90% are zero)");
            }
            else
            {
                Log("⚠ WARNING: Most motion vectors are zero - this might indicate an issue");
            }

            // Cleanup
            DestroyImmediate(mesh);
        }
        catch (System.Exception e)
        {
            Log($"\nERROR: {e.Message}");
            Debug.LogError($"Validation error: {e}");
        }
    }

    void Log(string message)
    {
        validationResults += message + "\n";
    }
}
