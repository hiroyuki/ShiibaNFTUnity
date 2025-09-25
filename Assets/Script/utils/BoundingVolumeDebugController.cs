using UnityEngine;

[RequireComponent(typeof(MeshRenderer))] // Ensures this is attached to a visible object
public class BoundingVolumeDebugController : MonoBehaviour
{
    [Header("Bounding Volume Debug Controls")]
    [SerializeField] private bool showAllPoints = false;
    [SerializeField] private bool refreshOnChange = true;
    
    [Header("Visual Settings")]
    [SerializeField] private Color enabledColor = Color.green;
    [SerializeField] private Color disabledColor = Color.red;
    [SerializeField] private bool changeColorBasedOnMode = true;
    
    private bool lastShowAllPoints;
    private MultiCameraPointCloudManager pointCloudManager;
    private MeshRenderer meshRenderer;
    
    void Start()
    {
        pointCloudManager = FindFirstObjectByType<MultiCameraPointCloudManager>();
        meshRenderer = GetComponent<MeshRenderer>();
        
        lastShowAllPoints = showAllPoints;
        PointCloudSettings.showAllPoints = showAllPoints;
        
        UpdateVisualFeedback();
    }
    
    void Update()
    {
        // Check if showAllPoints setting changed
        if (showAllPoints != lastShowAllPoints)
        {
            lastShowAllPoints = showAllPoints;
            PointCloudSettings.showAllPoints = showAllPoints;

            Debug.Log($"Bounding volume culling: {(showAllPoints ? "DISABLED (showing all points)" : "ENABLED")}");

            UpdateVisualFeedback();

            // Refresh current frame if auto-refresh is enabled
            if (refreshOnChange)
            {
                RefreshCurrentFrame();
            }
        }
    }
    
    [ContextMenu("Refresh Point Cloud")]
    public void RefreshCurrentFrame()
    {
        if (pointCloudManager != null)
        {
            // Force refresh by seeking to frame 0
            pointCloudManager.SeekToFrame(0);
            Debug.Log("Point cloud refreshed");
        }
    }
    
    [ContextMenu("Toggle Show All Points")]
    public void ToggleShowAllPoints()
    {
        showAllPoints = !showAllPoints;
    }
    
    private void UpdateVisualFeedback()
    {
        if (changeColorBasedOnMode && meshRenderer != null)
        {
            // Change BoundingVolume color based on mode
            Color targetColor = showAllPoints ? disabledColor : enabledColor;
            meshRenderer.material.color = targetColor;
        }
    }
}