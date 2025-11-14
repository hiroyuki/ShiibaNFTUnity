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

    private void OnEnable()
    {
        driftCorrectionData = (BvhDriftCorrectionData)target;
        keyframesProperty = serializedObject.FindProperty("keyframes");
        interpolationTypeProperty = serializedObject.FindProperty("interpolationType");
        isEnabledProperty = serializedObject.FindProperty("isEnabled");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

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
                string buttonLabel = $"Frame {i + 1}: {keyframe.timelineTime:F2}s (#{keyframe.bvhFrameNumber})";

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

                // キーフレーム詳細情報
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Position", EditorStyles.miniLabel);
                EditorGUILayout.Vector3Field("Anchor Position", keyframe.anchorPositionRelative);
                if (!string.IsNullOrEmpty(keyframe.note))
                {
                    EditorGUILayout.LabelField("Note", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(keyframe.note, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndVertical();

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
