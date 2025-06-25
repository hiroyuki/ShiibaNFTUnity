using UnityEngine;
using UnityEngine.InputSystem;

public class MouseOrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 3.0f;
    public float xSpeed = 120.0f;
    public float ySpeed = 120.0f;

    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;

    public float zoomSpeed = 2.0f;
    public float minDistance = 0.5f;
    public float maxDistance = 10f;

    private float x = 0.0f;
    private float y = 0.0f;

    private Vector2 mouseDelta;
    private float scrollDelta;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
    }

    void Update()
    {
        // 新InputSystemでマウス移動とスクロール取得
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.isPressed)
                mouseDelta = Mouse.current.delta.ReadValue();
            else
                mouseDelta = Vector2.zero;

            scrollDelta = Mouse.current.scroll.ReadValue().y;
        }
    }   

    void LateUpdate()
    {
        if (target == null) return;

        x += mouseDelta.x * xSpeed * Time.deltaTime;
        y -= mouseDelta.y * ySpeed * Time.deltaTime;
        y = Mathf.Clamp(y, yMinLimit, yMaxLimit);

        distance -= scrollDelta * zoomSpeed * Time.deltaTime;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Quaternion rotation = Quaternion.Euler(y, x, 0);
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -distance);
        transform.position = rotation * negDistance + target.position;
        transform.rotation = rotation;

        scrollDelta = 0; // 毎フレームクリア
    }
}
