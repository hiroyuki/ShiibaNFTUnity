using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class BoundingVolumeGizmo : MonoBehaviour
{
    [Header("Bounding Volume Settings")]
    public Color gizmoColor = Color.green;
    public bool showGizmo = true;
    
    private void OnDrawGizmos()
    {
        if (!showGizmo) return;
        
        // Draw wireframe cube showing the bounding volume
        Gizmos.color = gizmoColor;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
    
    private void OnDrawGizmosSelected()
    {
        if (!showGizmo) return;
        
        // Draw filled cube when selected
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
    }
}