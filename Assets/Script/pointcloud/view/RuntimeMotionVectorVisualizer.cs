using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Runtime motion vector visualizer for Game view rendering
/// Displays colored arrows using Debug.DrawLine() to show motion direction and magnitude
///
/// Usage:
/// 1. Attach to GameObject with MeshFilter (e.g., UnifiedPointCloudViewer)
/// 2. Motion vectors must be stored in UV1 channel (from PLY import)
/// 3. Toggle visualization with "Show Motion Vectors" checkbox
/// 4. Visible in Game view during Play mode
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class RuntimeMotionVectorVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private bool showMotionVectors = true;
    [SerializeField] private float arrowScale = 0.01f;
    [SerializeField] private float minMagnitudeThreshold = 0.001f;

    [Header("Performance")]
    [SerializeField] private int maxVectorsToVisualize = 5000;

    [Header("Color Gradient")]
    [SerializeField] private Gradient motionColorGradient = CreateDefaultGradient();

    [Header("Arrow Head")]
    [SerializeField] private float arrowHeadLength = 0.003f;
    [SerializeField] private float arrowHeadAngle = 20f;

    [Header("Debug Info")]
    [SerializeField] private bool showDebugInfo = false;

    private MeshFilter meshFilter;
    private List<Vector3> motionVectors = new List<Vector3>();
    private Vector3[] vertices;
    private float maxMagnitude = 0f;
    private int visibleCount = 0;

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
            return;
        }

        Mesh mesh = meshFilter.sharedMesh;

        // Get motion vectors from UV1 channel
        motionVectors.Clear();
        mesh.GetUVs(1, motionVectors);

        if (motionVectors.Count == 0)
        {
            return;
        }

        // Get vertices
        vertices = mesh.vertices;

        if (vertices.Length != motionVectors.Count)
        {
            Debug.LogWarning($"[RuntimeMotionVectorVisualizer] Vertex count mismatch: {vertices.Length} vs {motionVectors.Count}");
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

        if (showDebugInfo)
        {
            Debug.Log($"[RuntimeMotionVectorVisualizer] Loaded {motionVectors.Count} motion vectors (max magnitude: {maxMagnitude:F6})");
        }
    }

    void Update()
    {
        if (!showMotionVectors || motionVectors.Count == 0 || vertices == null)
            return;

        DrawMotionVectors();
    }

    void DrawMotionVectors()
    {
        // Subsample for performance
        int step = Mathf.Max(1, vertices.Length / maxVectorsToVisualize);
        visibleCount = 0;

        for (int i = 0; i < vertices.Length && i < motionVectors.Count; i += step)
        {
            Vector3 motion = motionVectors[i];
            float magnitude = motion.magnitude;

            // Skip near-zero motion
            if (magnitude < minMagnitudeThreshold)
                continue;

            // Transform to world space
            Vector3 start = transform.TransformPoint(vertices[i]);
            Vector3 end = start + motion * arrowScale;

            // Color based on magnitude
            float t = maxMagnitude > 0 ? magnitude / maxMagnitude : 0;
            Color color = motionColorGradient.Evaluate(t);

            // Draw arrow shaft
            Debug.DrawLine(start, end, color);

            // Draw arrow head
            DrawArrowHead(end, motion, color);

            visibleCount++;
        }
    }

    void DrawArrowHead(Vector3 tip, Vector3 direction, Color color)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Vector3 normalizedDir = direction.normalized;

        // Create perpendicular vectors
        Vector3 perpendicular = Vector3.Cross(normalizedDir, Vector3.up);
        if (perpendicular.sqrMagnitude < 0.0001f)
        {
            perpendicular = Vector3.Cross(normalizedDir, Vector3.right);
        }
        perpendicular.Normalize();

        Vector3 perpendicular2 = Vector3.Cross(normalizedDir, perpendicular).normalized;

        // Calculate arrow head points
        float angleRad = arrowHeadAngle * Mathf.Deg2Rad;
        float cosAngle = Mathf.Cos(angleRad);
        float sinAngle = Mathf.Sin(angleRad);

        Vector3 basePoint = tip - normalizedDir * arrowHeadLength * cosAngle;

        Vector3 arrowHead1 = basePoint + perpendicular * arrowHeadLength * sinAngle;
        Vector3 arrowHead2 = basePoint - perpendicular * arrowHeadLength * sinAngle;
        Vector3 arrowHead3 = basePoint + perpendicular2 * arrowHeadLength * sinAngle;
        Vector3 arrowHead4 = basePoint - perpendicular2 * arrowHeadLength * sinAngle;

        // Draw arrow head lines
        Debug.DrawLine(tip, arrowHead1, color);
        Debug.DrawLine(tip, arrowHead2, color);
        Debug.DrawLine(tip, arrowHead3, color);
        Debug.DrawLine(tip, arrowHead4, color);
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

    public int GetVisibleCount()
    {
        return visibleCount;
    }

    void OnGUI()
    {
        if (showDebugInfo && showMotionVectors && motionVectors.Count > 0)
        {
            GUI.Label(new Rect(10, 10, 400, 60),
                $"Motion Vectors: {visibleCount} visible / {motionVectors.Count} total\n" +
                $"Max magnitude: {maxMagnitude:F6}\n" +
                $"Arrow scale: {arrowScale:F4}");
        }
    }
}
