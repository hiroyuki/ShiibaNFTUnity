using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Custom editor for SinglePointCloudView with debug image export functionality.
/// </summary>
[CustomEditor(typeof(SinglePointCloudView))]
public class SinglePointCloudViewEditor : Editor
{
    private string exportDirectory = "DebugImages";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SinglePointCloudView view = (SinglePointCloudView)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Debug Image Export", EditorStyles.boldLabel);

        // Export directory field
        EditorGUILayout.BeginHorizontal();
        exportDirectory = EditorGUILayout.TextField("Export Directory", exportDirectory);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Export Directory", exportDirectory, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                exportDirectory = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Export button
        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("Export Current Frame Images", GUILayout.Height(30)))
        {
            ExportDebugImages(view);
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Image export is only available in Play mode after frames have been processed.", MessageType.Info);
        }
        GUI.enabled = true;

        EditorGUILayout.Space(5);

        // Show current frame info
        if (Application.isPlaying)
        {
            var frameController = view.GetFrameController();
            if (frameController != null)
            {
                EditorGUILayout.LabelField("Current Frame Info", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Device Name", frameController.DeviceName);
                EditorGUILayout.LabelField("Current Timestamp", view.GetCurrentTimestamp().ToString());
                EditorGUILayout.LabelField("Total Frames", view.GetTotalFrameCount().ToString());
                EditorGUILayout.LabelField("FPS", view.GetFps().ToString());
            }
        }
    }

    private void ExportDebugImages(SinglePointCloudView view)
    {
        var frameController = view.GetFrameController();
        if (frameController == null)
        {
            EditorUtility.DisplayDialog("Export Failed", "Frame controller is not initialized.", "OK");
            return;
        }

        var device = frameController.Device;
        if (device == null)
        {
            EditorUtility.DisplayDialog("Export Failed", "Sensor device is not available.", "OK");
            return;
        }

        // Create export directory if it doesn't exist
        if (!Path.IsPathRooted(exportDirectory))
        {
            // If relative path, make it relative to project root
            exportDirectory = Path.Combine(Application.dataPath, "..", exportDirectory);
        }

        try
        {
            // Calculate current frame index
            ulong currentTimestamp = view.GetCurrentTimestamp();
            int frameIndex = 0;
            int fps = view.GetFps();
            if (fps > 0 && currentTimestamp > 0)
            {
                // Estimate frame index from timestamp
                frameIndex = (int)(currentTimestamp / (1_000_000_000UL / (ulong)fps));
            }

            // Export images
            DebugImageExporter.ExportSensorImages(device, exportDirectory, frameIndex);

            EditorUtility.DisplayDialog(
                "Export Successful",
                $"Debug images exported to:\n{exportDirectory}\n\nDevice: {device.GetDeviceName()}\nFrame: {frameIndex}",
                "OK"
            );
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Export Failed", $"Error: {ex.Message}", "OK");
            Debug.LogError($"Failed to export debug images: {ex}");
        }
    }
}
