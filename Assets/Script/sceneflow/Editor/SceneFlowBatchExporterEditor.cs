using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom editor for SceneFlowBatchExporter
/// Provides buttons for easy batch export control
/// </summary>
[CustomEditor(typeof(SceneFlowBatchExporter))]
public class SceneFlowBatchExporterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SceneFlowBatchExporter exporter = (SceneFlowBatchExporter)target;

        // Show runtime info in Play mode
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Total Frames: {exporter.TotalFrames}");
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Batch Export Controls", EditorStyles.boldLabel);

        // Show export status
        if (exporter.IsExporting)
        {
            EditorGUILayout.HelpBox(
                $"Exporting frame {exporter.CurrentFrame}/{exporter.TotalFrames} ({exporter.ExportProgress * 100:F1}%)\n" +
                $"Estimated time remaining: {exporter.GetEstimatedTimeRemaining():F1}s",
                MessageType.Info
            );

            // Stop button
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Stop Export", GUILayout.Height(30)))
            {
                exporter.StopExport();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Assign DatasetConfig, then click 'Start Batch Export'.\n" +
                "Motion vectors will be calculated from BVH skeleton motion.",
                MessageType.Info
            );

            // Start button
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Start Batch Export", GUILayout.Height(40)))
            {
                if (!Application.isPlaying)
                {
                    EditorUtility.DisplayDialog(
                        "Play Mode Required",
                        "Batch export requires Play mode to be active.\n\nPlease enter Play mode first.",
                        "OK"
                    );
                }
                else
                {
                    exporter.BatchExportPLYWithMotionVectors();
                }
            }
            GUI.backgroundColor = Color.white;
        }

        // Repaint during export to update progress
        if (exporter.IsExporting)
        {
            EditorUtility.SetDirty(target);
            Repaint();
        }
    }
}
