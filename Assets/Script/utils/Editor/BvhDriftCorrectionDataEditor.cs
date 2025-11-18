#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Playables;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// BvhDriftCorrectionData のカスタムインスペクター
/// キーフレーム一覧をボタン化し、クリックでタイムラインをジャンプ
/// </summary>
[CustomEditor(typeof(BvhDriftCorrectionData))]
public class BvhDriftCorrectionDataEditor : Editor
{
    private BvhDriftCorrectionData driftCorrectionData;
    private SerializedProperty keyframesProperty;
    private SerializedProperty interpolationTypeProperty;
    private SerializedProperty isEnabledProperty;

    // Track previous position and rotation values to detect changes
    private Dictionary<int, Vector3> previousKeyframePositions = new Dictionary<int, Vector3>();
    private Dictionary<int, Vector3> previousKeyframeRotations = new Dictionary<int, Vector3>();

    // Track foldout state for each keyframe
    private Dictionary<int, bool> keyframeFoldoutStates = new Dictionary<int, bool>();

    private void OnEnable()
    {
        driftCorrectionData = (BvhDriftCorrectionData)target;
        keyframesProperty = serializedObject.FindProperty("keyframes");
        interpolationTypeProperty = serializedObject.FindProperty("interpolationType");
        isEnabledProperty = serializedObject.FindProperty("isEnabled");

        // Initialize position and rotation tracking
        RefreshKeyframePositionCache();
        RefreshKeyframeRotationCache();
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
                    int defaultFrameNumber = Mathf.FloorToInt(keyframe.timelineTime * bvhFrameRate);
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
    /// Applies the delta (difference) to the current BVH position
    /// </summary>
    private void UpdateBvhCharacterPosition(Vector3 newPosition, Vector3 oldPosition, BvhKeyframe keyframe)
    {
        // Find BVH_Character in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            Debug.LogWarning("[BvhDriftCorrectionDataEditor] BVH_Character not found in scene");
            return;
        }

        // Calculate the delta (how much the position changed)
        Vector3 positionDelta = newPosition - oldPosition;

        // Apply the delta to the BVH_Character's current position
        Transform bvhTransform = bvhCharacter.transform;
        Vector3 updatedPosition = bvhTransform.localPosition + positionDelta;

        // Update the BVH_Character position
        bvhTransform.localPosition = updatedPosition;

        Debug.Log($"[BVH Position Updated] Keyframe {keyframe.timelineTime}s:\n" +
                  $"  Old Correction: {oldPosition}\n" +
                  $"  New Correction: {newPosition}\n" +
                  $"  Delta Applied: {positionDelta}\n" +
                  $"  BVH_Character New Position: {updatedPosition}");

        // Mark the transform as dirty to ensure changes are saved
        EditorUtility.SetDirty(bvhTransform);
    }

    /// <summary>
    /// Updates the BVH_Character rotation based on keyframe rotation change
    /// Applies the delta (difference) to the current BVH rotation
    /// </summary>
    private void UpdateBvhCharacterRotation(Vector3 newRotation, Vector3 oldRotation, BvhKeyframe keyframe)
    {
        // Find BVH_Character in the scene
        GameObject bvhCharacter = GameObject.Find("BVH_Character");
        if (bvhCharacter == null)
        {
            Debug.LogWarning("[BvhDriftCorrectionDataEditor] BVH_Character not found in scene");
            return;
        }

        // Calculate the delta (how much the rotation changed) in euler angles
        Vector3 rotationDelta = newRotation - oldRotation;

        // Apply the delta to the BVH_Character's current rotation
        Transform bvhTransform = bvhCharacter.transform;
        Vector3 currentEuler = bvhTransform.localEulerAngles;
        Vector3 updatedEuler = currentEuler + rotationDelta;

        // Update the BVH_Character rotation
        bvhTransform.localEulerAngles = updatedEuler;

        Debug.Log($"[BVH Rotation Updated] Keyframe {keyframe.timelineTime}s:\n" +
                  $"  Old Correction: {oldRotation}\n" +
                  $"  New Correction: {newRotation}\n" +
                  $"  Delta Applied: {rotationDelta}\n" +
                  $"  BVH_Character New Rotation: {updatedEuler}");

        // Mark the transform as dirty to ensure changes are saved
        EditorUtility.SetDirty(bvhTransform);
    }

    /// <summary>
    /// 指定されたキーフレーム時刻に、PlayableDirectorをジャンプさせる
    /// </summary>
    private void JumpToKeyframe(BvhKeyframe keyframe)
    {
        // シーン内のPlayableDirectorを取得
        PlayableDirector timeline = FindObjectOfType<PlayableDirector>();

        if (timeline == null)
        {
            EditorUtility.DisplayDialog("Error", "PlayableDirector not found in scene.", "OK");
            return;
        }

        // タイムラインを指定時刻にジャンプ
        timeline.time = keyframe.timelineTime;

        // Evaluate を呼び出して、PrepareFrame を強制的に実行
        timeline.Evaluate();

        // Timeline ウィンドウをリセット（Timeline アセットをダーティマーク）
        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(timeline.playableAsset);

        // Scene ビューを再描画
        UnityEditor.SceneView.RepaintAll();

        Debug.Log($"[BvhDriftCorrectionDataEditor] Jumped to keyframe at time={keyframe.timelineTime}s");
    }
}
#endif
