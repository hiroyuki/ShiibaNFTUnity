using UnityEngine;
using UnityEditor;
using System;

/// <summary>
/// Custom editor for DatasetConfig with PLY export buttons
/// </summary>
[CustomEditor(typeof(DatasetConfig))]
public class DatasetConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("PLY Export Controls", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "These buttons work only when the application is playing and using Binary mode (CPU/GPU/ONESHADER).\n" +
            "Press 'O' key during playback to export current frame.\n" +
            "Press 'Shift+E' during playback to export all frames.",
            MessageType.Info);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

        // Export Current Frame button
        if (GUILayout.Button("Export Current Frame to PLY", GUILayout.Height(30)))
        {
            Debug.Log("Export Current Frame button clicked!");
            ExportCurrentFrame();
        }

        EditorGUILayout.Space(5);

        // Export All Frames button
        if (GUILayout.Button("Export All Frames to PLY", GUILayout.Height(30)))
        {
            Debug.Log("Export All Frames button clicked!");
            ExportAllFrames();
        }

        EditorGUILayout.Space(5);

        // Export Current Frame with Scene Flow button
        if (GUILayout.Button("Export Current Frame with Scene Flow", GUILayout.Height(30)))
        {
            Debug.Log("Export Current Frame with Scene Flow button clicked!");
            ExportCurrentFrameWithSceneFlow();
        }

        EditorGUILayout.Space(5);

        // Export All Frames with Scene Flow button
        if (GUILayout.Button("Export All Frames with Scene Flow", GUILayout.Height(30)))
        {
            Debug.Log("Export All Frames with Scene Flow button clicked!");
            ExportAllFramesWithSceneFlow();
        }

        EditorGUI.EndDisabledGroup();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Export buttons are only available during Play mode.", MessageType.Warning);
        }

        // Downsampled PLY Export Controls
        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("Downsampled PLY Export Controls", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Export downsampled point cloud data. Requires PointCloudDownsampler component in the scene.\n" +
            "Works in Play mode (Timeline playback) or Edit mode (load from PLY files).",
            MessageType.Info);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

        // Export Current Frame Downsampled button
        if (GUILayout.Button("Export Current Frame (Downsampled)", GUILayout.Height(30)))
        {
            Debug.Log("Export Current Frame (Downsampled) button clicked!");
            ExportCurrentFrameDownsampled();
        }

        EditorGUILayout.Space(5);

        // Export All Frames Downsampled button (Play Mode)
        if (GUILayout.Button("Export All Frames (Downsampled - Play Mode)", GUILayout.Height(30)))
        {
            Debug.Log("Export All Frames (Downsampled - Play Mode) button clicked!");
            ExportAllFramesDownsampledPlayMode();
        }

        EditorGUI.EndDisabledGroup();

        // Export All Frames Downsampled button (Edit Mode - Load PLYs)
        EditorGUI.BeginDisabledGroup(Application.isPlaying);

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Export All Frames (Downsampled - Load PLYs)", GUILayout.Height(30)))
        {
            Debug.Log("Export All Frames (Downsampled - Load PLYs) button clicked!");
            ExportAllFramesDownsampledFromPLY();
        }

        EditorGUI.EndDisabledGroup();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play Mode export buttons are only available during Play mode.\nEdit Mode PLY loading is available now.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Edit Mode PLY loading is only available in Edit mode.\nPlay Mode export buttons are available now.", MessageType.Info);
        }
    }

    private void ExportCurrentFrame()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Not Playing", "Please enter Play mode before exporting PLY files.", "OK");
            return;
        }

        // Find MultiCameraPointCloudManager in scene by searching all MonoBehaviours
        MonoBehaviour manager = null;
        var allObjects = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        Debug.Log($"Searching for MultiCamPointCloudManager among {allObjects.Length} MonoBehaviours...");

        foreach (var obj in allObjects)
        {
            if (obj.GetType().Name == "MultiCameraPointCloudManager")
            {
                manager = obj;
                Debug.Log($"Found MultiCameraPointCloudManager: {obj.name}");
                break;
            }
        }

        if (manager == null)
        {
            // List all MonoBehaviour types for debugging
            var types = new System.Collections.Generic.HashSet<string>();
            foreach (var obj in allObjects)
            {
                types.Add(obj.GetType().Name);
            }
            Debug.LogError($"MultiCamPointCloudManager not found in scene. Found {types.Count} unique MonoBehaviour types.");
            Debug.LogError($"Available types: {string.Join(", ", types)}");

            EditorUtility.DisplayDialog("Export Failed",
                "MultiCamPointCloudManager not found in scene.\n\n" +
                "Make sure:\n" +
                "1. The scene is playing\n" +
                "2. MultiCamPointCloudManager exists in the scene\n" +
                "3. Timeline has initialized the point cloud system", "OK");
            return;
        }

        // Trigger export via simulating 'O' key press
        var managerType = manager.GetType();
        var handlerMethod = managerType.GetMethod("GetCurrentHandler");
        var handler = handlerMethod?.Invoke(manager, null);
        if (handler == null)
        {
            Debug.LogError("No active processing mode handler. Cannot export PLY.");
            EditorUtility.DisplayDialog("Export Failed", "No active processing mode handler found.", "OK");
            return;
        }

        if (handler != null && handler.GetType().Name == "BinaryModeHandler")
        {
            // Call the export method directly
            TriggerCurrentFrameExport(handler);
            Debug.Log("Current frame PLY export triggered from DatasetConfig editor.");
        }
        else
        {
            Debug.LogWarning("PLY export is only supported in Binary mode (CPU/GPU/ONESHADER).");
            EditorUtility.DisplayDialog("Export Not Available",
                "PLY export is only supported in Binary mode (CPU/GPU/ONESHADER).\n" +
                "Current mode: " + (handler != null ? handler.GetType().Name : "None"), "OK");
        }
    }

    private void ExportAllFrames()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Not Playing", "Please enter Play mode before exporting PLY files.", "OK");
            return;
        }

        // Find MultiCameraPointCloudManager in scene by searching all MonoBehaviours
        MonoBehaviour manager = null;
        var allObjects = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        foreach (var obj in allObjects)
        {
            if (obj.GetType().Name == "MultiCameraPointCloudManager")
            {
                manager = obj;
                break;
            }
        }

        if (manager == null)
        {
            Debug.LogError("MultiCamPointCloudManager not found in scene. Cannot export PLY.");
            EditorUtility.DisplayDialog("Export Failed",
                "MultiCamPointCloudManager not found in scene.\n\n" +
                "Make sure:\n" +
                "1. The scene is playing\n" +
                "2. MultiCamPointCloudManager exists in the scene\n" +
                "3. Timeline has initialized the point cloud system", "OK");
            return;
        }

        // Trigger export via simulating 'Shift+E' key press
        var managerType = manager.GetType();
        var handlerMethod = managerType.GetMethod("GetCurrentHandler");
        var handler = handlerMethod?.Invoke(manager, null);
        if (handler == null)
        {
            Debug.LogError("No active processing mode handler. Cannot export PLY.");
            EditorUtility.DisplayDialog("Export Failed", "No active processing mode handler found.", "OK");
            return;
        }

        if (handler != null && handler.GetType().Name == "BinaryModeHandler")
        {
            // Show confirmation dialog
            bool confirmed = EditorUtility.DisplayDialog("Export All Frames",
                "This will export all frames to PLY files.\n" +
                "This operation may take a long time depending on the number of frames.\n\n" +
                "Continue?",
                "Export All", "Cancel");

            if (confirmed)
            {
                TriggerAllFramesExport(handler);
                Debug.Log("All frames PLY export triggered from DatasetConfig editor.");
            }
        }
        else
        {
            Debug.LogWarning("PLY export is only supported in Binary mode (CPU/GPU/ONESHADER).");
            EditorUtility.DisplayDialog("Export Not Available",
                "PLY export is only supported in Binary mode (CPU/GPU/ONESHADER).\n" +
                "Current mode: " + (handler != null ? handler.GetType().Name : "None"), "OK");
        }
    }

    private void ExportCurrentFrameWithSceneFlow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Not Playing", "Please enter Play mode before exporting PLY files with scene flow.", "OK");
            return;
        }

        // Find SceneFlowCalculator in scene
        SceneFlowCalculator sceneFlowCalculator = FindFirstObjectByType<SceneFlowCalculator>();
        if (sceneFlowCalculator == null)
        {
            Debug.LogError("SceneFlowCalculator not found in scene. Cannot export PLY with scene flow.");
            EditorUtility.DisplayDialog("Export Failed",
                "SceneFlowCalculator not found in scene.\n\n" +
                "Make sure:\n" +
                "1. SceneFlowCalculator component exists in the scene\n" +
                "2. SceneFlowCalculator is properly configured",
                "OK");
            return;
        }

        // Find MultiCameraPointCloudManager to get current frame info
        MonoBehaviour manager = null;
        var allObjects = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        foreach (var obj in allObjects)
        {
            if (obj.GetType().Name == "MultiCameraPointCloudManager")
            {
                manager = obj;
                break;
            }
        }

        if (manager == null)
        {
            Debug.LogError("MultiCameraPointCloudManager not found in scene. Cannot determine current frame.");
            EditorUtility.DisplayDialog("Export Failed",
                "MultiCameraPointCloudManager not found in scene.\n\n" +
                "Make sure the point cloud system is initialized.", "OK");
            return;
        }

        // Get MultiPointCloudView
        var managerType = manager.GetType();
        var getViewMethod = managerType.GetMethod("GetMultiPointCloudView");
        var multiPointCloudView = getViewMethod?.Invoke(manager, null);

        if (multiPointCloudView == null)
        {
            Debug.LogError("MultiPointCloudView not available. Cannot export PLY.");
            EditorUtility.DisplayDialog("Export Failed",
                "MultiPointCloudView not available.\n" +
                "Make sure Timeline is playing and point cloud is loaded.", "OK");
            return;
        }

        // Get current mesh
        var viewType = multiPointCloudView.GetType();
        var getMeshMethod = viewType.GetMethod("GetUnifiedMesh");
        UnityEngine.Mesh mesh = (UnityEngine.Mesh)getMeshMethod?.Invoke(multiPointCloudView, null);

        if (mesh == null || mesh.vertexCount == 0)
        {
            Debug.LogError("No mesh available to export.");
            EditorUtility.DisplayDialog("Export Failed",
                "No point cloud mesh available.\n" +
                "Make sure a frame is loaded in the Timeline.", "OK");
            return;
        }

        // Get current frame index from Timeline
        var timelineController = FindFirstObjectByType<TimelineController>();
        if (timelineController == null)
        {
            Debug.LogError("TimelineController not found in scene.");
            EditorUtility.DisplayDialog("Export Failed",
                "TimelineController not found in scene.", "OK");
            return;
        }

        // Get PlayableDirector
        var controllerType = timelineController.GetType();
        var directorField = controllerType.GetField("timeline",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        UnityEngine.Playables.PlayableDirector director = (UnityEngine.Playables.PlayableDirector)directorField?.GetValue(timelineController);

        if (director == null)
        {
            Debug.LogError("PlayableDirector not found. Attempting to find it automatically...");

            // Fallback: Try to find PlayableDirector in the scene
            director = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Playables.PlayableDirector>();

            if (director == null)
            {
                EditorUtility.DisplayDialog("Export Failed",
                    "PlayableDirector not found.\n" +
                    "Make sure Timeline is properly set up in the scene.", "OK");
                return;
            }
        }

        // Get current Timeline time
        double currentTime = director.time;
        BvhData bvhData = BvhDataCache.GetBvhData();
        if (bvhData == null)
        {
            Debug.LogError("BVH data not loaded. Cannot calculate frame index.");
            EditorUtility.DisplayDialog("Export Failed",
                "BVH data not loaded.\n" +
                "Make sure BVH file is configured in DatasetConfig.", "OK");
            return;
        }

        // Use BvhPlaybackFrameMapper to map Timeline time to BVH frame
        DatasetConfig config = (DatasetConfig)target;
        BvhPlaybackCorrectionKeyframes driftData = null;

        // Get drift correction data from config using reflection
        var configType = config.GetType();
        var driftField = configType.GetField("bvhDriftCorrectionData",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (driftField != null)
        {
            driftData = (BvhPlaybackCorrectionKeyframes)driftField.GetValue(config);
        }

        // Map Timeline time directly to BVH frame indices using frame mapper
        BvhPlaybackFrameMapper frameMapper = new BvhPlaybackFrameMapper();
        float timelineTime = (float)currentTime;
        float previousTimelineTime = Mathf.Max(0f, timelineTime - bvhData.FrameTime);

        int currentBvhFrame = frameMapper.GetTargetFrameForTime(timelineTime, bvhData, driftData);
        int previousBvhFrame = frameMapper.GetTargetFrameForTime(previousTimelineTime, bvhData, driftData);

        // Calculate point cloud frame number from Timeline time (assuming 30fps for point cloud)
        // TODO: Get actual point cloud frame rate from config
        float pointCloudFps = 30f;  // Adjust this if your point cloud has a different frame rate
        int currentFrame = Mathf.RoundToInt(timelineTime * pointCloudFps);

        Debug.Log($"[DatasetConfigEditor] Exporting frame {currentFrame}: Mapped to BVH frames ({previousBvhFrame}, {currentBvhFrame})");

        // Calculate bone segments for the frame pair
        sceneFlowCalculator.CalculateBoneSegmentsForFramePair(currentBvhFrame, previousBvhFrame);

        // Calculate motion vectors using SceneFlowCalculator
        Vector3[] motionVectors = sceneFlowCalculator.CalculateMotionVectorsForMesh(mesh);

        // Generate output file path (use point cloud frame number for filename)
        string outputDir = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "ExportedPLY_SceneFlow_Single");
        System.IO.Directory.CreateDirectory(outputDir);
        string filename = $"frame_{currentFrame:D6}_sceneflow.ply";  // currentFrame is point cloud frame
        string filePath = System.IO.Path.Combine(outputDir, filename);

        // Get torso_7 position comments (filename uses point cloud frame, position uses BVH frame)
        string[] headerComments = BvhJointUtility.GetJointPositionComments(currentFrame, currentBvhFrame);

        // Export to PLY
        PlyExporter.ExportToPLY(mesh, motionVectors, filePath, headerComments);

        Debug.Log($"Current frame PLY with scene flow exported to: {filePath}");
        EditorUtility.DisplayDialog("Export Complete",
            $"Current frame exported successfully!\n\n" +
            $"Frame: {currentFrame}\n" +
            $"File: {filename}\n" +
            $"Location: {outputDir}",
            "OK");
    }

    private void ExportAllFramesWithSceneFlow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Not Playing", "Please enter Play mode before exporting PLY files with scene flow.", "OK");
            return;
        }

        // Find SceneFlowBatchExporter in scene using reflection
        MonoBehaviour exporter = null;
        var allObjects = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        foreach (var obj in allObjects)
        {
            if (obj.GetType().Name == "SceneFlowBatchExporter")
            {
                exporter = obj;
                break;
            }
        }

        if (exporter == null)
        {
            Debug.LogError("SceneFlowBatchExporter not found in scene. Cannot export PLY with scene flow.");
            EditorUtility.DisplayDialog("Export Failed",
                "SceneFlowBatchExporter not found in scene.\n\n" +
                "Make sure:\n" +
                "1. SceneFlowBatchExporter component exists in the scene\n" +
                "2. SceneFlowCalculator is properly configured\n" +
                "3. MultiPointCloudView is available",
                "OK");
            return;
        }

        // Check if already exporting using reflection
        var exporterType = exporter.GetType();
        var isExportingProperty = exporterType.GetProperty("IsExporting");
        bool isExporting = (bool)isExportingProperty.GetValue(exporter);

        if (isExporting)
        {
            EditorUtility.DisplayDialog("Export in Progress",
                "Scene flow export is already in progress.\n" +
                "Please wait for it to complete.",
                "OK");
            return;
        }

        // Show confirmation dialog
        bool confirmed = EditorUtility.DisplayDialog("Export All Frames with Scene Flow",
            "This will export all frames to PLY files with motion vector data.\n" +
            "Motion vectors are calculated from BVH skeletal motion.\n\n" +
            "This operation may take a long time depending on the number of frames.\n\n" +
            "Continue?",
            "Export All", "Cancel");

        if (confirmed)
        {
            // Call StartBatchExport using reflection
            var startExportMethod = exporterType.GetMethod("StartBatchExport");
            if (startExportMethod != null)
            {
                startExportMethod.Invoke(exporter, null);
                Debug.Log("All frames PLY export with scene flow triggered from DatasetConfig editor.");
            }
            else
            {
                Debug.LogError("StartBatchExport method not found on SceneFlowBatchExporter.");
            }
        }
    }

    private void TriggerCurrentFrameExport(object handler)
    {
        // Access the PlyExportManager via reflection or public method
        var handlerType = handler.GetType();
        var exportManagerField = handlerType.GetField("plyExportManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (exportManagerField != null)
        {
            var exportManager = exportManagerField.GetValue(handler);

            var frameManagerField = handlerType.GetField("frameProcessingManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var frameManager = frameManagerField?.GetValue(handler);

            if (exportManager != null && frameManager != null)
            {
                // Call HandlePlyExportInput with simulated 'O' key press
                var processingTypeProperty = handlerType.GetProperty("ProcessingType");
                var processingType = processingTypeProperty.GetValue(handler);

                var frameManagerType = frameManager.GetType();
                var currentFrameProperty = frameManagerType.GetProperty("CurrentFrameIndex");
                int currentFrame = (int)currentFrameProperty.GetValue(frameManager);

                // Simulate 'O' key press by directly calling the export method
                var exportManagerType = exportManager.GetType();
                var exportMethod = exportManagerType.GetMethod("ExportCurrentFrameToPLY",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (exportMethod != null)
                {
                    exportMethod.Invoke(exportManager, new object[] { processingType, currentFrame });
                }
            }
            else
            {
                Debug.LogError("PlyExportManager not initialized. Make sure Timeline is playing and PLY export is set up.");
                EditorUtility.DisplayDialog("Export Failed",
                    "PLY export system not initialized.\n" +
                    "Make sure Timeline is playing and export has been set up.", "OK");
            }
        }
    }

    private void TriggerAllFramesExport(object handler)
    {
        // Access the PlyExportManager via reflection
        var handlerType = handler.GetType();
        var exportManagerField = handlerType.GetField("plyExportManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (exportManagerField != null)
        {
            var exportManager = exportManagerField.GetValue(handler);

            if (exportManager != null)
            {
                // Get processing type
                var processingTypeProperty = handlerType.GetProperty("ProcessingType");
                var processingType = processingTypeProperty.GetValue(handler);

                // Call StartExportAllFrames via reflection
                var exportManagerType = exportManager.GetType();
                var startExportMethod = exportManagerType.GetMethod("StartExportAllFrames",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (startExportMethod != null)
                {
                    startExportMethod.Invoke(exportManager, new object[] { processingType });
                }
            }
            else
            {
                Debug.LogError("PlyExportManager not initialized. Make sure Timeline is playing and PLY export is set up.");
                EditorUtility.DisplayDialog("Export Failed",
                    "PLY export system not initialized.\n" +
                    "Make sure Timeline is playing and export has been set up.", "OK");
            }
        }
    }

    // Downsampled PLY Export Methods
    private void ExportCurrentFrameDownsampled()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Not Playing", "Please enter Play mode before exporting downsampled PLY files.", "OK");
            return;
        }

        // Find PointCloudDownsampler in scene
        PointCloudDownsampler downsampler = FindFirstObjectByType<PointCloudDownsampler>();
        if (downsampler == null)
        {
            Debug.LogError("PointCloudDownsampler not found in scene. Cannot export downsampled PLY.");
            EditorUtility.DisplayDialog("Export Failed",
                "PointCloudDownsampler not found in scene.\n\n" +
                "Make sure:\n" +
                "1. PointCloudDownsampler component exists in the scene\n" +
                "2. Downsampling has been applied (enable Preview in Inspector)",
                "OK");
            return;
        }

        // Call the export method
        downsampler.ExportDownsampledPLYManual();
        Debug.Log("Current frame downsampled PLY export triggered from DatasetConfig editor.");
    }

    private void ExportAllFramesDownsampledPlayMode()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Not Playing", "Please enter Play mode before exporting downsampled PLY files.", "OK");
            return;
        }

        // Find PointCloudDownsampler in scene
        PointCloudDownsampler downsampler = FindFirstObjectByType<PointCloudDownsampler>();
        if (downsampler == null)
        {
            Debug.LogError("PointCloudDownsampler not found in scene. Cannot export downsampled PLY.");
            EditorUtility.DisplayDialog("Export Failed",
                "PointCloudDownsampler not found in scene.\n\n" +
                "Make sure:\n" +
                "1. PointCloudDownsampler component exists in the scene\n" +
                "2. Downsampling has been applied (enable Preview in Inspector)",
                "OK");
            return;
        }

        // Show confirmation dialog
        bool confirmed = EditorUtility.DisplayDialog("Export All Frames (Downsampled)",
            "This will export all frames to downsampled PLY files using Timeline playback.\n" +
            "This operation may take a long time depending on the number of frames.\n\n" +
            "Continue?",
            "Export All", "Cancel");

        if (confirmed)
        {
            downsampler.BatchExportAllFramesPlayMode();
            Debug.Log("All frames downsampled PLY export (Play Mode) triggered from DatasetConfig editor.");
        }
    }

    private void ExportAllFramesDownsampledFromPLY()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Exit Play Mode", "Please exit Play mode before using PLY loading export.", "OK");
            return;
        }

        // Find PointCloudDownsampler in scene
        PointCloudDownsampler downsampler = FindFirstObjectByType<PointCloudDownsampler>();
        if (downsampler == null)
        {
            Debug.LogError("PointCloudDownsampler not found in scene. Cannot export downsampled PLY.");
            EditorUtility.DisplayDialog("Export Failed",
                "PointCloudDownsampler not found in scene.\n\n" +
                "Make sure PointCloudDownsampler component exists in the scene.",
                "OK");
            return;
        }

        // Show confirmation dialog
        bool confirmed = EditorUtility.DisplayDialog("Export All Frames (Downsampled - Load PLYs)",
            "This will load each PLY file, downsample it, and export the result.\n" +
            "This operation may take a long time depending on the number of PLY files.\n\n" +
            "Continue?",
            "Export All", "Cancel");

        if (confirmed)
        {
            downsampler.BatchExportAllFramesFromPLY();
            Debug.Log("All frames downsampled PLY export (Load PLYs) triggered from DatasetConfig editor.");
        }
    }

}
