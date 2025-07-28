using UnityEngine;
using UnityEngine.InputSystem;

public class MouseOrbitCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    
    [Header("Distance")]
    public float distance = 3.0f;
    public float minDistance = 0.5f;
    public float maxDistance = 10f;
    
    [Header("Rotation")]
    public float rotationSensitivity = 2.0f;
    public float yMinLimit = -80f;
    public float yMaxLimit = 80f;
    
    [Header("Zoom")]
    public float zoomSensitivity = 2000.0f;
    public bool smoothZoom = true;
    
    [Header("Panning")]
    public float panSensitivity = 1.0f;
    public bool enablePanning = true;
    
    [Header("Smoothing")]
    public bool smoothRotation = false;
    public float rotationSmoothing = 8.0f;
    public bool smoothPanning = true;
    public float panSmoothing = 8.0f;

    private float x = 0.0f;
    private float y = 0.0f;
    private float targetDistance;
    private Vector3 targetOffset = Vector3.zero;
    
    // Smooth interpolation targets
    private float smoothX, smoothY;
    private float smoothDistance;
    private Vector3 smoothOffset;

    void Start()
    {
        if (target != null)
        {
            // Calculate initial angles based on current position relative to target
            Vector3 direction = transform.position - target.position;
            distance = direction.magnitude;
            
            x = smoothX = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            y = smoothY = Mathf.Asin(direction.y / distance) * Mathf.Rad2Deg;
        }
        else
        {
            Vector3 angles = transform.eulerAngles;
            x = smoothX = angles.y;
            y = smoothY = angles.x;
        }
        
        targetDistance = smoothDistance = distance;
        smoothOffset = targetOffset;
    }

    void Update()
    {
        if (target == null) return;
        
        HandleInput();
        UpdateCamera();
    }

    void HandleInput()
    {
        if (Mouse.current == null) return;

        // Rotation with left mouse button
        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            x += mouseDelta.x * rotationSensitivity * 0.1f;
            y -= mouseDelta.y * rotationSensitivity * 0.1f;
            y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
        }

        // Panning with middle mouse button
        if (enablePanning && Mouse.current.middleButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            
            // Convert mouse movement to world space panning
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            
            Vector3 panMovement = (-right * mouseDelta.x + up * mouseDelta.y) * panSensitivity * 0.01f;
            targetOffset += panMovement;
        }

        // Zooming with scroll wheel
        Vector2 scroll = Mouse.current.scroll.ReadValue();
        if (scroll.y != 0)
        {
            float zoomInput = scroll.y * zoomSensitivity * 0.05f;
            targetDistance = Mathf.Clamp(targetDistance - zoomInput, minDistance, maxDistance);
        }

        // Reset view with right click
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            targetOffset = Vector3.zero;
        }
    }

    void UpdateCamera()
    {
        // Smooth rotation
        if (smoothRotation)
        {
            smoothX = Mathf.LerpAngle(smoothX, x, rotationSmoothing * Time.deltaTime);
            smoothY = Mathf.LerpAngle(smoothY, y, rotationSmoothing * Time.deltaTime);
        }
        else
        {
            smoothX = x;
            smoothY = y;
        }

        // Smooth distance
        if (smoothZoom)
        {
            smoothDistance = Mathf.Lerp(smoothDistance, targetDistance, rotationSmoothing * Time.deltaTime);
        }
        else
        {
            smoothDistance = targetDistance;
        }

        // Smooth panning offset
        if (smoothPanning)
        {
            smoothOffset = Vector3.Lerp(smoothOffset, targetOffset, panSmoothing * Time.deltaTime);
        }
        else
        {
            smoothOffset = targetOffset;
        }

        // Apply camera transformation
        Quaternion rotation = Quaternion.Euler(smoothY, smoothX, 0);
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -smoothDistance);
        Vector3 targetPos = target.position + smoothOffset;
        
        transform.position = rotation * negDistance + targetPos;
        transform.rotation = rotation;
        
        // Update distance for external access
        distance = smoothDistance;
    }
}
