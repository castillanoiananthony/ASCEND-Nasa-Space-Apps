using UnityEngine;

public class FlyCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Normal movement speed in units per second.")]
    public float moveSpeed = 10f;

    [Tooltip("Multiplier applied when holding Shift.")]
    public float sprintMultiplier = 2.5f;

    [Tooltip("Smoothing factor for keyboard movement.")]
    public float acceleration = 10f;

    [Header("Rotation Settings")]
    [Tooltip("Mouse sensitivity for camera rotation.")]
    public float mouseSensitivity = 2f;

    [Header("Height Constraints")]
    [Tooltip("The minimum Y position (height) the camera can reach.")]
    public float minHeight = 0.5f;

    private float pitch = 0f;
    private float yaw = 0f;
    private Vector3 currentVelocity = Vector3.zero;

    private void Start()
    {
        // Lock and hide the cursor for smooth mouse look
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialize rotation angles to match camera's initial rotation
        Vector3 initialAngles = transform.eulerAngles;
        pitch = initialAngles.x;
        yaw = initialAngles.y;
    }

    private void Update()
    {
        HandleRotation();
        HandleMovement();
        HandleHeightLimit();
        HandleCursorLock();
    }

    private void HandleRotation()
    {
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY;

        // Clamp pitch so the camera doesn't flip upside down
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        // Apply rotation
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void HandleMovement()
    {
        // Read directional inputs: A (-1) / D (+1), S (-1) / W (+1)
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        // Read elevation inputs: E (+1) / Q (-1)
        float elevation = 0f;
        if (Input.GetKey(KeyCode.E)) elevation += 1f;
        if (Input.GetKey(KeyCode.Q)) elevation -= 1f;

        // Combine inputs into a local movement vector
        Vector3 inputDirection = new Vector3(horizontal, 0f, vertical).normalized;
        
        // Translate horizontal inputs relative to camera orientation, plus world vertical input
        Vector3 targetDirection = (transform.right * inputDirection.x) 
                                + (transform.forward * inputDirection.z) 
                                + (Vector3.up * elevation);

        // Calculate actual speed (with sprint check)
        float currentSpeed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);

        // Smooth velocity transition
        Vector3 targetVelocity = targetDirection * currentSpeed;
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, acceleration * Time.deltaTime);

        // Apply position update
        transform.position += currentVelocity * Time.deltaTime;
    }

    private void HandleHeightLimit()
    {
        // Clamp Y coordinate so position never falls below minHeight
        if (transform.position.y < minHeight)
        {
            Vector3 clampedPosition = transform.position;
            clampedPosition.y = minHeight;
            transform.position = clampedPosition;

            // Stop downward momentum when reaching minimum height
            if (currentVelocity.y < 0f)
            {
                currentVelocity.y = 0f;
            }
        }
    }

    private void HandleCursorLock()
    {
        // Press Escape to unlock / lock mouse cursor
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked 
                ? CursorLockMode.None 
                : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
