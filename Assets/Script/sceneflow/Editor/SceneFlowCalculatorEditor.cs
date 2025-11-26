#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Inspector for SceneFlowCalculator
/// Adds "Show Scene Flow" button to the Inspector
/// </summary>
[CustomEditor(typeof(SceneFlowCalculator))]
public class SceneFlowCalculatorEditor : Editor
{
    private SceneFlowCalculator calculator;

    private void OnEnable()
    {
        calculator = (SceneFlowCalculator)target;
    }

    public override void OnInspectorGUI()
    {
        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene Flow Controls", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Show Scene Flow button
        if (GUILayout.Button("Show Scene Flow", GUILayout.Height(40)))
        {
            calculator.OnShowSceneFlow();
            EditorUtility.SetDirty(calculator);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Click 'Show Scene Flow' to calculate scene flow for the current BVH frame. " +
            "Make sure to call Initialize() first and set the current frame using SetFrameInfo().",
            MessageType.Info);
    }
}
#endif
