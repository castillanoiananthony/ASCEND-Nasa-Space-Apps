using UnityEngine;
using UnityEngine.InputSystem;

public class FreeFlyCamera : MonoBehaviour
{
    [Header("Look Settings")]
    public float mouseSensitivity = 0.2f;
    public bool holdRightClickToLook = true;

    [Header("Movement Settings")]
    public float movementSpeed = 500.0f;
    public float fastMoveMultiplier = 3.0f;
    public float smoothTime = 5.0f;

    private float rotationX = 0.0f;
    private float rotationY = 0.0f;
    private Vector3 targetVelocity;
    private Vector3 currentVelocity;

    void Start()
    {
        Vector3 rot = transform.eulerAngles;
        rotationX = rot.x;
        rotationY = rot.y;
    }

    void Update()
    {
        HandleRotation();
        HandleMovement();
    }

    void HandleRotation()
    {
        if (Mouse.current == null) return;

        if (holdRightClickToLook)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (!Mouse.current.rightButton.isPressed) return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;
        rotationY += mouseDelta.x;
        rotationX -= mouseDelta.y;
        rotationX = Mathf.Clamp(rotationX, -89f, 89f);

        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
    }

    void HandleMovement()
    {
        if (Keyboard.current == null) return;

        float moveForward = 0f;
        float moveRight = 0f;

        if (Keyboard.current.wKey.isPressed) moveForward += 1f;
        if (Keyboard.current.sKey.isPressed) moveForward -= 1f;
        if (Keyboard.current.dKey.isPressed) moveRight += 1f;
        if (Keyboard.current.aKey.isPressed) moveRight -= 1f;

        float moveUp = 0f;
        if (Keyboard.current.eKey.isPressed) moveUp += 1f;
        if (Keyboard.current.qKey.isPressed) moveUp -= 1f;

        Vector3 inputDir = (transform.forward * moveForward + transform.right * moveRight + transform.up * moveUp).normalized;

        float currentSpeed = movementSpeed;
        if (Keyboard.current.leftShiftKey.isPressed)
        {
            currentSpeed *= fastMoveMultiplier;
        }

        targetVelocity = inputDir * currentSpeed;
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * smoothTime);
        transform.position += currentVelocity * Time.deltaTime;
    }
}
