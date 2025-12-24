#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Playables;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// BvhPlaybackCorrectionKeyframes のカスタムインスペクター
/// キーフレーム一覧をボタン化し、クリックでタイムラインをジャンプ
/// </summary>
[CustomEditor(typeof(BvhPlaybackCorrectionKeyframes))]
public class BvhPlaybackCorrectionKeyframesEditor : Editor
{
    private BvhPlaybackCorrectionKeyframes driftCorrectionData;
    private SerializedProperty keyframesProperty;
    private SerializedProperty interpolationTypeProperty;
    private SerializedProperty isEnabledProperty;

    // Track previous position and rotation values to detect changes
    private Dictionary<int, Vector3> previousKeyframePositions = new Dictionary<int, Vector3>();
    private Dictionary<int, Vector3> previousKeyframeRotations = new Dictionary<int, Vector3>();

    // Track foldout state for each keyframe
    private Dictionary<int, bool> keyframeFoldoutStates = new Dictionary<int, bool>();

    // Track previous keyframe count to detect when new keyframes are added
    private int previousKeyframeCount = 0;

    private void OnEnable()
    {
        driftCorrectionData = (BvhPlaybackCorrectionKeyframes)target;
        keyframesProperty = serializedObject.FindProperty("keyframes");
        interpolationTypeProperty = serializedObject.FindProperty("interpolationType");
        isEnabledProperty = serializedObject.FindProperty("isEnabled");

        // Initialize position and rotation tracking
        RefreshKeyframePositionCache();
        RefreshKeyframeRotationCache();

        // Initialize keyframe count tracking
        previousKeyframeCount = driftCorrectionData.GetKeyframeCount();
    }

    private void RefreshKeyframePositionCache()
    {
        previousKeyframePositions.Clear();
        var keyframes = driftCorrectionData.GetAllKeyframes();
        for (int i = 0; i < keyframes.Count; i++)
        {
            previousKeyframePositions[i] = keyframes[i].anchorPositionRelative;
        }
    }

    private void RefreshKeyframeRotationCache()
    {
        previousKeyframeRotations.Clear();
        var keyframes = driftCorrectionData.GetAllKeyframes();
        for (int i = 0; i < keyframes.Count; i++)
        {
            previousKeyframeRotations[i] = keyframes[i].anchorRotationRelative;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Check if a new keyframe was added via the "+" button
        DetectNewKeyframeAdded();

        // Check for position and rotation changes before rendering
        DetectKeyframePositionChanges();
        DetectKeyframeRotationChanges();

        // ヘッダー
        EditorGUILayout.LabelField("Drift Correction Data", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Active
        EditorGUILayout.PropertyField(isEnabledProperty, new GUIContent("Enabled"));
        EditorGUILayout.Space();

        // Interpolation Type
        EditorGUILayout.PropertyField(interpolationTypeProperty, new GUIContent("Interpolation Type"));
        EditorGUILayout.Space();

        // Keyframes Section
        EditorGUILayout.LabelField("Keyframe Navigation", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Click on a keyframe button to jump the timeline to that position.", MessageType.Info);
        EditorGUILayout.Space();

        // Get keyframes
        var keyframes = driftCorrectionData.GetAllKeyframes();

        if (keyframes.Count == 0)
        {
            EditorGUILayout.HelpBox("No keyframes added yet. Press Shift+A in the scene to add a keyframe.", MessageType.Warning);
        }
        else
        {
            // キーフレームボタンリスト
            EditorGUILayout.LabelField($"Keyframes ({keyframes.Count})", EditorStyles.boldLabel);

            for (int i = 0; i < keyframes.Count; i++)
            {
                var keyframe = keyframes[i];

                // キーフレーム情報を表示
                EditorGUILayout.BeginHorizontal();

                // ボタン: クリックでタイムラインジャンプ
                string buttonLabel = $"Frame {i}: {keyframe.timelineTime:F2}s (#{keyframe.bvhFrameNumber})";

                if (GUILayout.Button(buttonLabel, GUILayout.Height(30)))
                {
                    JumpToKeyframe(keyframe);
                }

                // 削除ボタン
                if (GUILayout.Button("X", GUILayout.Width(30), GUILayout.Height(30)))
                {
                    driftCorrectionData.RemoveKeyframe(keyframe);
                    serializedObject.Update();
                    return; // リストが変更されたので再描画
                }

                EditorGUILayout.EndHorizontal();

                // Foldout state management
                if (!keyframeFoldoutStates.ContainsKey(i))
                {
                    keyframeFoldoutStates[i] = false;
                }

                // キーフレーム詳細情報（折りたたみ可能）
                keyframeFoldoutStates[i] = EditorGUILayout.Foldout(keyframeFoldoutStates[i], "Details", true);

                if (keyframeFoldoutStates[i])
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.LabelField("Position & Rotation", EditorStyles.miniLabel);
                    EditorGUILayout.Vector3Field("Anchor Position", keyframe.anchorPositionRelative);
                    EditorGUILayout.Vector3Field("Anchor Rotation", keyframe.anchorRotationRelative);

                    // デフォルトフレームレートでの参考値を表示
                    float bvhFrameRate = driftCorrectionData.GetBvhFrameRate();
                    int defaultFrameNumber = Mathf.FloorToInt((float)keyframe.timelineTime * bvhFrameRate);
                    EditorGUILayout.LabelField("Frame Number Reference", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Default (at {bvhFrameRate:F1}fps): {defaultFrameNumber}", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField($"Current: {keyframe.bvhFrameNumber}", EditorStyles.wordWrappedLabel);

                    if (!string.IsNullOrEmpty(keyframe.note))
                    {
                        EditorGUILayout.LabelField("Note", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField(keyframe.note, EditorStyles.wordWrappedLabel);
                    }

                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.Space();
            }
        }

        // キーフレーム編集セクション
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Keyframe List (Advanced Edit)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(keyframesProperty, true);

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// Detects when keyframe positions have been modified in the Inspector
    /// Updates BVH_Character position to reflect the keyframe changes in real-time
    /// </summary>
    private void DetectKeyframePositionChanges()
    {
        var keyframes = driftCorrectionData.GetAllKeyframes();

        // Check each keyframe for position changes
        for (int i = 0; i < keyframes.Count; i++)
        {
            Vector3 currentPosition = keyframes[i].anchorPositionRelative;

            if (previousKeyframePositions.TryGetValue(i, out Vector3 previousPosition))
            {
                // Compare positions with small epsilon for floating-point precision
                if (!Mathf.Approximately(currentPosition.x, previousPosition.x) ||
                    !Mathf.Approximately(currentPosition.y, previousPosition.y) ||
                    !Mathf.Approximately(currentPosition.z, previousPosition.z))
                {
                    // Position has changed - update BVH character position
                    UpdateBvhCharacterPosition(currentPosition, previousPosition, keyframes[i]);

                    // Update the cached position
                    previousKeyframePositions[i] = currentPosition;
                }
            }
            else
            {
                // New keyframe added
                previousKeyframePositions[i] = currentPosition;
            }
        }

        // Check for removed keyframes
        var keysToRemove = new List<int>();
        foreach (var key in previousKeyframePositions.Keys)
        {
            if (key >= keyframes.Count)
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            previousKeyframePositions.Remove(key);
        }
    }

    /// <summary>
    /// Detects when keyframe rotations have been modified in the Inspector
    /// Updates BVH_Character rotation to reflect the keyframe changes in real-time
    /// </summary>
    private void DetectKeyframeRotationChanges()
    {
        var keyframes = driftCorrectionData.GetAllKeyframes();

        // Check each keyframe for rotation changes
        for (int i = 0; i < keyframes.Count; i++)
        {
            Vector3 currentRotation = keyframes[i].anchorRotationRelative;

            if (previousKeyframeRotations.TryGetValue(i, out Vector3 previousRotation))
            {
                // Compare rotations with small epsilon for floating-point precision
                if (!Mathf.Approximately(currentRotation.x, previousRotation.x) ||
                    !Mathf.Approximately(currentRotation.y, previousRotation.y) ||
                    !Mathf.Approximately(currentRotation.z, previousRotation.z))
                {
                    // Rotation has changed - update BVH character rotation
                    UpdateBvhCharacterRotation(currentRotation, previousRotation, keyframes[i]);

                    // Update the cached rotation
                    previousKeyframeRotations[i] = currentRotation;
                }
            }
            else
            {
                // New keyframe added
                previousKeyframeRotations[i] = currentRotation;
            }
        }

        // Check for removed keyframes
        var keysToRemove = new List<int>();
        foreach (var key in previousKeyframeRotations.Keys)
        {
            if (key >= keyframes.Count)
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            previousKeyframeRotations.Remove(key);
        }
    }

    /// <summary>
    /// Updates the BVH_Character position based on keyframe position change
    /// Sets position to DatasetConfig.BvhPositionOffset + keyframe correction value
    /// </summary>
    private void UpdateBvhCharacterPosition(Vector3 newPosition, Vector3 oldPosition, BvhKeyframe keyframe)
    {
        // Find BVH_Character in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            Debug.LogWarning("[BvhPlaybackCorrectionKeyframesEditor] BVH_Character not found in scene");
            return;
        }

        // Get DatasetConfig via MultiCameraPointCloudManager
        var manager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (manager == null)
        {
            Debug.LogWarning("[BvhPlaybackCorrectionKeyframesEditor] MultiCameraPointCloudManager not found in scene");
            return;
        }

        DatasetConfig datasetConfig = manager.GetDatasetConfig();
        if (datasetConfig == null)
        {
            Debug.LogWarning("[BvhPlaybackCorrectionKeyframesEditor] DatasetConfig not configured in MultiCameraPointCloudManager");
            return;
        }

        // Calculate the correct position: BaseOffset + KeyframeCorrection
        Transform bvhTransform = bvhCharacter.transform;
        Vector3 baseOffset = datasetConfig.BvhPositionOffset;
        Vector3 updatedPosition = baseOffset + newPosition;

        // Update the BVH_Character position
        bvhTransform.localPosition = updatedPosition;

        Debug.Log($"[BVH Position Updated] Keyframe {keyframe.timelineTime}s:\n" +
                  $"  Base Offset (DatasetConfig): {baseOffset}\n" +
                  $"  Old Correction: {oldPosition}\n" +
                  $"  New Correction: {newPosition}\n" +
                  $"  Final Position: {baseOffset} + {newPosition} = {updatedPosition}");

        // Mark the transform as dirty to ensure changes are saved
        EditorUtility.SetDirty(bvhTransform);
    }

    /// <summary>
    /// Updates the BVH_Character rotation based on keyframe rotation change
    /// Sets rotation to DatasetConfig.BvhRotationOffset + keyframe correction value
    /// </summary>
    private void UpdateBvhCharacterRotation(Vector3 newRotation, Vector3 oldRotation, BvhKeyframe keyframe)
    {
        // Find BVH_Character in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            Debug.LogWarning("[BvhPlaybackCorrectionKeyframesEditor] BVH_Character not found in scene");
            return;
        }

        // Get DatasetConfig via MultiCameraPointCloudManager
        var manager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        if (manager == null)
        {
            Debug.LogWarning("[BvhPlaybackCorrectionKeyframesEditor] MultiCameraPointCloudManager not found in scene");
            return;
        }

        DatasetConfig datasetConfig = manager.GetDatasetConfig();
        if (datasetConfig == null)
        {
            Debug.LogWarning("[BvhPlaybackCorrectionKeyframesEditor] DatasetConfig not configured in MultiCameraPointCloudManager");
            return;
        }

        // Calculate the correct rotation: BaseOffset + KeyframeCorrection
        Transform bvhTransform = bvhCharacter.transform;
        Vector3 baseOffset = datasetConfig.BvhRotationOffset;
        Vector3 updatedEuler = baseOffset + newRotation;

        // Update the BVH_Character rotation
        bvhTransform.localEulerAngles = updatedEuler;

        Debug.Log($"[BVH Rotation Updated] Keyframe {keyframe.timelineTime}s:\n" +
                  $"  Base Offset (DatasetConfig): {baseOffset}\n" +
                  $"  Old Correction: {oldRotation}\n" +
                  $"  New Correction: {newRotation}\n" +
                  $"  Final Rotation: {baseOffset} + {newRotation} = {updatedEuler}");

        // Mark the transform as dirty to ensure changes are saved
        EditorUtility.SetDirty(bvhTransform);
    }

    /// <summary>
    /// 指定されたキーフレーム時刻に、PlayableDirectorをジャンプさせる
    /// </summary>
    private void JumpToKeyframe(BvhKeyframe keyframe)
    {
        // シーン内のPlayableDirectorを取得
        PlayableDirector timeline = FindFirstObjectByType<PlayableDirector>();

        if (timeline == null)
        {
            EditorUtility.DisplayDialog("Error", "PlayableDirector not found in scene.", "OK");
            return;
        }

        // タイムラインを指定時刻にジャンプ
        TimelineUtil.SeekToTime(keyframe.timelineTime);

        // Timeline ウィンドウをリセット（Timeline アセットをダーティマーク）
        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(timeline.playableAsset);

        // Scene ビューを再描画
        UnityEditor.SceneView.RepaintAll();

        Debug.Log($"[BvhPlaybackCorrectionKeyframesEditor] Jumped to keyframe at time={keyframe.timelineTime}s");
    }

    /// <summary>
    /// Detects when a new keyframe is added via the "+" button in the Inspector
    /// Automatically fills in current timeline time, BVH frame, position, and rotation
    /// </summary>
    private void DetectNewKeyframeAdded()
    {
        int currentKeyframeCount = driftCorrectionData.GetKeyframeCount();

        // Check if array size increased (new keyframe added)
        if (currentKeyframeCount > previousKeyframeCount)
        {
            // Get the newly added keyframe (last in list)
            var keyframes = driftCorrectionData.GetAllKeyframes();
            if (keyframes.Count > 0)
            {
                BvhKeyframe newKeyframe = keyframes[keyframes.Count - 1];

                // Get current timeline time
                PlayableDirector timeline = FindFirstObjectByType<PlayableDirector>();
                double currentTime = timeline != null ? timeline.time : 0.0;

                // Get BVH playable asset and current frame
                var bvhAsset = Resources.FindObjectsOfTypeAll<BvhPlayableAsset>().FirstOrDefault();
                int currentFrame = 0;
                Vector3 currentPosition = Vector3.zero;
                Vector3 currentRotation = Vector3.zero;

                if (bvhAsset != null)
                {
                    // Get current frame from BvhPlayableBehaviour
                    var bvhBehaviour = bvhAsset.GetBvhPlayableBehaviour();
                    if (bvhBehaviour != null)
                    {
                        currentFrame = bvhBehaviour.GetCurrentFrame();
                    }

                    // If frame is -1 (uninitialized), calculate from time
                    if (currentFrame == -1)
                    {
                        BvhData bvhData = bvhAsset.GetBvhData();
                        float bvhFrameRate = bvhData != null ? bvhData.FrameRate : 30f;
                        currentFrame = Mathf.FloorToInt((float)(currentTime * bvhFrameRate));
                    }

                    // Get current position and rotation from BVH_Character
                    currentPosition = bvhAsset.GetBvhCharacterPosition();
                    currentRotation = bvhAsset.GetBvhCharacterRotation();
                }

                // Update the new keyframe with current values
                newKeyframe.timelineTime = currentTime;
                newKeyframe.bvhFrameNumber = currentFrame;
                newKeyframe.anchorPositionRelative = currentPosition;
                newKeyframe.anchorRotationRelative = currentRotation;

                // Mark as dirty to save changes
                EditorUtility.SetDirty(driftCorrectionData);

                Debug.Log($"[BvhPlaybackCorrectionKeyframesEditor] Auto-filled new keyframe: " +
                          $"time={currentTime:F2}s, frame={currentFrame}, pos={currentPosition}, rot={currentRotation}");
            }

            // Update the previous count
            previousKeyframeCount = currentKeyframeCount;

            // Refresh caches
            RefreshKeyframePositionCache();
            RefreshKeyframeRotationCache();
        }
        // Check if keyframe was removed
        else if (currentKeyframeCount < previousKeyframeCount)
        {
            previousKeyframeCount = currentKeyframeCount;
            RefreshKeyframePositionCache();
            RefreshKeyframeRotationCache();
        }
    }
}
#endif
