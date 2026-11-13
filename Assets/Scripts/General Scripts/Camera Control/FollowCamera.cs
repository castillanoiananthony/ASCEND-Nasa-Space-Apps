using UnityEngine;
using UnityEngine.InputSystem;

public class FollowCamera : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform targetPlanet;

    [Header("Distance & Zoom")]
    [Tooltip("Distance multiplier relative to planet size. Lower = closer (e.g. 1.2 to 2.0)")]
    public float initialMultiplier = 1.8f;
    public float followDistance = 2.0f;
    public float minDistance = 0.05f;
    public float maxDistance = 1000.0f;
    public float zoomSensitivity = 10.0f;

    [Header("Orbit Controls")]
    public float mouseSensitivity = 0.2f;
    public bool holdRightClickToOrbit = true;

    private float rotationX = 0.0f;
    private float rotationY = 0.0f;

    void Start()
    {
        Vector3 rot = transform.eulerAngles;
        rotationX = rot.x;
        rotationY = rot.y;

        // Auto-set starting distance based on target scale
        if (targetPlanet != null)
        {
            UpdateInitialDistance();
        }
    }

    void LateUpdate()
    {
        if (targetPlanet == null) return;

        HandleOrbit();
        HandleZoom();
        UpdatePosition();
    }

    void HandleOrbit()
    {
        if (Mouse.current == null) return;

        if (!holdRightClickToOrbit || Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;
            rotationY += mouseDelta.x;
            rotationX -= mouseDelta.y;
            rotationX = Mathf.Clamp(rotationX, -89f, 89f);
        }
    }

    void HandleZoom()
    {
        if (Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            followDistance -= (scroll / 120f) * zoomSensitivity * (followDistance * 0.1f);
            followDistance = Mathf.Clamp(followDistance, minDistance, maxDistance);
        }
    }

    void UpdatePosition()
    {
        Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -followDistance);

        transform.rotation = rotation;
        transform.position = targetPlanet.position + offset;
    }

    public void UpdateInitialDistance()
    {
        if (targetPlanet != null)
        {
            float planetSize = targetPlanet.localScale.x;
            followDistance = Mathf.Clamp(planetSize * initialMultiplier, minDistance, maxDistance);
        }
    }
}
