
using UnityEngine;

[ExecuteAlways]
public class CameraPositionGizmo : MonoBehaviour
{
    public Color gizmoColor = Color.green;
    public float size = 0.1f;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, size);

        // ラベルで名前も表示（Sceneビュー用）
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.1f, gameObject.name);
        #endif
    }
}
