using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Runtime component to visualize motion vectors stored in mesh UV1 channel
/// Draws colored arrows in Scene view to show motion direction and magnitude
///
/// Usage:
/// 1. Attach to GameObject with MeshFilter (e.g., UnifiedPointCloudViewer)
/// 2. Motion vectors must be stored in UV1 channel (from PLY import)
/// 3. Toggle visualization with "Show Motion Vectors" checkbox
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class MotionVectorVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private bool showMotionVectors = true;
    [SerializeField] private float arrowScale = 0.01f;
    [SerializeField] private float minMagnitudeThreshold = 0.001f;

    [Header("Performance")]
    [SerializeField] private int maxVectorsToVisualize = 10000;
    [SerializeField] private bool useFrustumCulling = true;

    [Header("Color Gradient")]
    [SerializeField] private Gradient motionColorGradient = CreateDefaultGradient();

    [Header("Debug Info")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private bool showMatchingLines = false;

    private MeshFilter meshFilter;
    private List<Vector3> motionVectors = new List<Vector3>();
    private Vector3[] vertices;
    private float maxMagnitude = 0f;

    static Gradient CreateDefaultGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.blue, 0f),
                new GradientColorKey(Color.green, 0.5f),
                new GradientColorKey(Color.red, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        LoadMotionVectors();
    }

    void OnValidate()
    {
        if (Application.isPlaying && meshFilter != null)
        {
            LoadMotionVectors();
        }
    }

    void LoadMotionVectors()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[MotionVectorVisualizer] No mesh found");
            return;
        }

    Mesh mesh = meshFilter.sharedMesh;

        // Get motion vectors from UV1 channel
        motionVectors.Clear();
        mesh.GetUVs(1, motionVectors);

        if (motionVectors.Count == 0)
        {
            Debug.LogWarning("[MotionVectorVisualizer] No motion vectors found in UV1 channel");
            return;
        }

        // Get vertices
        vertices = mesh.vertices;

        if (vertices.Length != motionVectors.Count)
        {
            Debug.LogWarning($"[MotionVectorVisualizer] Vertex count mismatch: {vertices.Length} vs {motionVectors.Count}");
        }

        // Find max magnitude for color scaling
        maxMagnitude = 0f;
        foreach (var mv in motionVectors)
        {
            float mag = mv.magnitude;
            if (mag > maxMagnitude)
            {
                maxMagnitude = mag;
            }
        }

        Debug.Log($"[MotionVectorVisualizer] Loaded {motionVectors.Count} motion vectors (max magnitude: {maxMagnitude:F6})");
    }

    void OnDrawGizmos()
    {
        if (!showMotionVectors || motionVectors.Count == 0 || vertices == null)
            return;

        DrawMotionVectors();
    }

    void DrawMotionVectors()
    {
        // Get Scene view camera for frustum culling
        Camera sceneCamera = null;
        Plane[] frustumPlanes = null;

#if UNITY_EDITOR
        if (UnityEditor.SceneView.lastActiveSceneView != null)
        {
            sceneCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
            if (useFrustumCulling && sceneCamera != null)
            {
                frustumPlanes = GeometryUtility.CalculateFrustumPlanes(sceneCamera);
            }
        }
#endif

        // Subsample for performance
        int step = Mathf.Max(1, vertices.Length / maxVectorsToVisualize);
        int visibleCount = 0;
        int totalDrawn = 0;

        for (int i = 0; i < vertices.Length && i < motionVectors.Count; i += step)
        {
            Vector3 motion = motionVectors[i];
            float magnitude = motion.magnitude;

            // Skip near-zero motion
            if (magnitude < minMagnitudeThreshold)
                continue;

            // Transform to world space
            Vector3 start = transform.TransformPoint(vertices[i]);

            // Frustum culling - check start point
            if (frustumPlanes != null)
            {
                Bounds pointBounds = new Bounds(start, Vector3.one * 0.01f);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, pointBounds))
                    continue;
            }

            Vector3 end = start + motion * arrowScale;

            // Frustum culling - check end point
            if (frustumPlanes != null)
            {
                Bounds endBounds = new Bounds(end, Vector3.one * 0.01f);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, endBounds))
                    continue;
            }

            visibleCount++;

            // Color based on magnitude
            float t = maxMagnitude > 0 ? magnitude / maxMagnitude : 0;
            Gizmos.color = motionColorGradient.Evaluate(t);

            // Draw arrow
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.005f);

            totalDrawn++;
        }

        // Debug info
#if UNITY_EDITOR
        if (showDebugInfo && sceneCamera != null && visibleCount > 0)
        {
            Vector3 labelPos = sceneCamera.transform.position + sceneCamera.transform.forward * 2f;
            UnityEditor.Handles.Label(
                labelPos,
                $"Motion Vectors: {visibleCount} visible | Max magnitude: {maxMagnitude:F6}"
            );
        }
#endif
    }

    // Public API for runtime control
    public void SetShowMotionVectors(bool show)
    {
        showMotionVectors = show;
    }

    public void SetArrowScale(float scale)
    {
        arrowScale = scale;
    }

    public void RefreshMotionVectors()
    {
        LoadMotionVectors();
    }

    public int GetMotionVectorCount()
    {
        return motionVectors.Count;
    }

    public float GetMaxMagnitude()
    {
        return maxMagnitude;
    }
}
