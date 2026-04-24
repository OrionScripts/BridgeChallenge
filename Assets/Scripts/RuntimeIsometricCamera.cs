using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteAlways]
[DisallowMultipleComponent]
public class RuntimeIsometricCamera : MonoBehaviour
{
    // These defaults match the runtime bridge placement view so a newly-added camera component immediately frames the bridge-building plane.
    [SerializeField] private Vector3 targetPosition = new(0f, 0f, 6f);
    [SerializeField, Min(1f)] private float distance = 32f;
    [SerializeField, Range(20f, 80f)] private float pitch = 60f;
    [SerializeField, Range(-180f, 180f)] private float yaw = 45f;
    [SerializeField, Min(1f)] private float orthographicSize = 18f;

    [Header("Controls")]
    [SerializeField, Min(0.1f)] private float keyboardPanSpeed = 18f;
    [SerializeField, Min(0.01f)] private float mousePanSpeed = 0.035f;
    [SerializeField, Min(0.1f)] private float zoomSpeed = 2f;
    [SerializeField, Min(1f)] private float keyboardRotateSpeed = 90f;
    [SerializeField, Min(0.01f)] private float mouseRotateSpeed = 0.2f;
    [SerializeField, Min(1f)] private float minOrthographicSize = 8f;
    [SerializeField, Min(1f)] private float maxOrthographicSize = 32f;

    private void Awake()
    {
        ApplyView();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        // Each input handler mutates a single bit of camera state. ApplyView is deferred until the end so combined inputs only rebuild the transform once.
        bool changed = false;
        changed |= HandleKeyboardRotate();
        changed |= HandleMouseRotate();
        changed |= HandleKeyboardPan();
        changed |= HandleMousePan();
        changed |= HandleZoom();

        if (changed)
            ApplyView();
    }

    private void OnValidate()
    {
        // Keep inspector edits valid even if the min/max fields are changed out of order.
        maxOrthographicSize = Mathf.Max(maxOrthographicSize, minOrthographicSize);
        orthographicSize = Mathf.Clamp(orthographicSize, minOrthographicSize, maxOrthographicSize);
        ApplyView();
    }

    public void ApplyView()
    {
        // The camera orbits around targetPosition; panning moves the target, while rotation changes the yaw and keeps the ARPG-style pitch consistent.
        Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);

        transform.SetPositionAndRotation(
            targetPosition - viewRotation * Vector3.forward * distance,
            viewRotation);

        if (TryGetComponent(out Camera camera))
        {
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
        }
    }

    private bool HandleKeyboardPan()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return false;

        Vector2 input = Vector2.zero;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            input.x -= 1f;

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            input.x += 1f;

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            input.y -= 1f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            input.y += 1f;

        if (input.sqrMagnitude <= 0f)
            return false;

        // Normalize diagonal movement so W+D is not faster than W alone.
        input = Vector2.ClampMagnitude(input, 1f);
        Pan(input * (keyboardPanSpeed * Time.deltaTime));
        return true;
    }

    private bool HandleKeyboardRotate()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return false;

        float input = 0f;

        if (keyboard.qKey.isPressed)
            input -= 1f;

        if (keyboard.eKey.isPressed)
            input += 1f;

        if (Mathf.Abs(input) <= 0f)
            return false;

        yaw += input * keyboardRotateSpeed * Time.deltaTime;
        return true;
    }

    private bool HandleMouseRotate()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return false;

        if (!keyboard.altKey.isPressed || !mouse.leftButton.isPressed)
            return false;

        Vector2 delta = mouse.delta.ReadValue();

        if (Mathf.Abs(delta.x) <= 0.001f)
            return false;

        yaw += delta.x * mouseRotateSpeed;
        return true;
    }

    private bool HandleMousePan()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null || !mouse.middleButton.isPressed)
            return false;

        Vector2 delta = mouse.delta.ReadValue();

        if (delta.sqrMagnitude <= 0f)
            return false;

        // Screen-space dragging should feel similar when zoomed in or out.
        float zoomScale = orthographicSize / 18f;
        Pan(-delta * (mousePanSpeed * zoomScale));
        return true;
    }

    private bool HandleZoom()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return false;

        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) <= 0.001f)
            return false;

        orthographicSize = Mathf.Clamp(
            orthographicSize - Mathf.Sign(scroll) * zoomSpeed,
            minOrthographicSize,
            maxOrthographicSize);
        return true;
    }

    private void Pan(Vector2 input)
    {
        // Pan is relative to camera yaw so WASD remains intuitive after rotation.
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 right = yawRotation * Vector3.right;
        Vector3 forward = yawRotation * Vector3.forward;
        targetPosition += right * input.x + forward * input.y;
    }
}
