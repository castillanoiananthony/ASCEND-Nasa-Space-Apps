using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 5f;      // real walking ~1.4 m/s; 2.5-3 feels good in games
    [SerializeField] private float sprintSpeed = 10f;   // real running ~4-6 m/s
    [SerializeField] private KeyCode sprintKey = KeyCode.R;
    [SerializeField] private float gravity = -19.62f;
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("Body (average adult)")]
    [Tooltip("Resize the Character Controller to a real person on start. The feet stay where they are.")]
    [SerializeField] private bool applyBodySize = true;
    [Tooltip("Standing height in meters. Average adult ~1.7.")]
    [SerializeField] private float bodyHeight = 1.7f;
    [Tooltip("Half the shoulder width in meters. 0.3 = 60 cm wide, fits through doors.")]
    [SerializeField] private float bodyRadius = 0.3f;
    [Tooltip("On start, place the feet exactly on the floor below (fixes spawning sunk into the floor).")]
    [SerializeField] private bool snapToFloorOnStart = true;
    [Tooltip("On start, give every visible mesh in the scene that has no collider a Mesh Collider, so you can't walk through buildings.")]
    [SerializeField] private bool addMissingColliders = true;

    [Header("Look Settings")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Eye height above the FLOOR in meters (average adult ~1.6). Works with any Character Controller height/center.")]
    [SerializeField] private float cameraHeight = 1.6f;
    [SerializeField] private float mouseSensitivity = 3.5f;
    [SerializeField] private float verticalLookLimit = 80f;

    [Header("Head Bob - Walk")]
    [SerializeField] private float walkBobFrequency = 6f;
    [SerializeField] private float walkBobAmplitude = 0.02f;
    [SerializeField] private float walkBobRoll = 0.15f;

    [Header("Head Bob - Sprint")]
    [SerializeField] private float sprintBobFrequency = 8.5f;
    [SerializeField] private float sprintBobAmplitude = 0.055f;
    [SerializeField] private float sprintBobRoll = 0.6f;

    [Header("Head Bob - Smoothing")]
    [SerializeField] private bool enableHeadBob = true;
    [SerializeField] private float bobBlendSpeed = 6f;

    private CharacterController controller;
    private Vector3 velocity;
    private float xRotation;

    // Head bob state
    private Vector3 cameraDefaultLocalPos;
    private float bobTimer;
    private float currentBobFrequency;
    private float currentBobAmplitude;
    private float currentBobRoll;
    private float bobRollAngle;

    private Vector3 moveInput;
    private bool isGrounded;

    public bool IsSprinting { get; private set; }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (applyBodySize) ApplyBodySize();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform != null)
        {
            FitNearClip();
            Vector3 pos = cameraTransform.localPosition;
            cameraDefaultLocalPos = new Vector3(pos.x, EyeLocalY(), pos.z);
            cameraTransform.localPosition = cameraDefaultLocalPos;
        }

        currentBobFrequency = walkBobFrequency;
    }

    // The camera can't see closer than its near clip plane (Unity default 0.3 m). If that plane is wider than the body,
    // its corners poke into walls you stand against and you see through them. Size it so it always stays inside the body.
    private void FitNearClip()
    {
        var cam = cameraTransform.GetComponent<Camera>();
        if (!cam) return;
        float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * Mathf.Max(cam.aspect, 16f / 9f);
        float corner = Mathf.Sqrt(1f + tanV * tanV + tanH * tanH);   // distance to a frustum corner per meter of near plane
        float room = controller.radius * transform.lossyScale.x - 0.03f;   // keep a little gap for head bob sway
        cam.nearClipPlane = Mathf.Clamp(room / corner, 0.01f, cam.nearClipPlane);
    }

    // Real-person capsule. Keeps the feet at the same spot so the player doesn't sink or float.
    private void ApplyBodySize()
    {
        float feetY = controller.center.y - controller.height * 0.5f;
        controller.radius = Mathf.Clamp(bodyRadius, 0.05f, bodyHeight * 0.5f);
        controller.height = Mathf.Max(bodyHeight, controller.radius * 2f);
        controller.center = new Vector3(controller.center.x, feetY + controller.height * 0.5f, controller.center.z);
        controller.stepOffset = Mathf.Min(controller.stepOffset, controller.height * 0.25f);
    }

    // Camera height is measured from the bottom of the capsule (the feet), not from the Player's pivot.
    // With Center Y = 0 and Height = 2 the feet are 1 m below the pivot, so the eyes go to local y = 0.6.
    private float EyeLocalY()
    {
        if (!controller) return cameraHeight;
        float groundY = controller.center.y - controller.height * 0.5f - controller.skinWidth;   // where the feet touch the floor
        return groundY + cameraHeight;
    }

    IEnumerator Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Wait a moment so colliders created by other scripts at start (e.g. AddColliderToChildren) exist first
        yield return null;
        if (addMissingColliders) AddMissingColliders();
        yield return new WaitForFixedUpdate();
        if (snapToFloorOnStart) SnapToFloor();
    }

    // Buildings and props imported without colliders are walk-through. Give each solid mesh one.
    private void AddMissingColliders()
    {
        int added = 0;
        foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var mesh = mf.sharedMesh;
            if (!mesh || mesh.vertexCount < 3) continue;
            var go = mf.gameObject;
            if (go.GetComponent<Collider>()) continue;                        // already solid
            var mr = go.GetComponent<MeshRenderer>();
            if (!mr || !mr.enabled) continue;                                 // invisible helpers stay walk-through
            if (mf.transform.IsChildOf(transform)) continue;                  // not the player itself
            if (go.GetComponentInParent<Canvas>()) continue;                  // not UI
            if (go.GetComponent<TMPro.TMP_Text>()) continue;                  // not 3D text
            if (mf.transform.root.name == "AscendCAD Workspace") continue;    // not the CAD editor's own scene
            if (mf.GetComponentInParent<Rigidbody>()) continue;               // moving physics objects keep their own setup
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            added++;
        }
        if (added > 0) Debug.Log($"PlayerController: added {added} Mesh Collider(s) to scene meshes that had none.");
    }

    // Finds the floor under the player and stands the capsule exactly on it
    private void SnapToFloor()
    {
        float scale = transform.lossyScale.y;
        float feetWorld = transform.position.y + (controller.center.y - controller.height * 0.5f) * scale;
        float radius = controller.radius * 0.9f * scale;
        // Start at waist height so lamps or shelves above the head are never mistaken for the floor
        Vector3 from = new Vector3(transform.position.x, feetWorld + controller.height * scale * 0.5f, transform.position.z);

        RaycastHit best = default;
        float bestDist = float.MaxValue;
        foreach (var h in Physics.SphereCastAll(from, radius, Vector3.down, controller.height * scale + 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == controller || h.collider.transform.IsChildOf(transform)) continue;   // skip ourselves
            if (h.distance > 0f && h.distance < bestDist) { bestDist = h.distance; best = h; }
        }
        if (bestDist == float.MaxValue) return;   // no floor found: leave the player where it is

        float floorY = from.y - bestDist - radius;           // bottom of the sphere at the hit
        float delta = floorY + controller.skinWidth - feetWorld;
        if (Mathf.Abs(delta) < 0.001f) return;
        controller.enabled = false;
        transform.position += Vector3.up * delta;
        controller.enabled = true;
        velocity = Vector3.zero;
    }

    void Update()
    {
        HandleMovement();
        HandleHeadBob();
        HandleLook();
    }

    private void HandleLook()
    {
        if (cameraTransform == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation = Mathf.Clamp(xRotation - mouseY, -verticalLookLimit, verticalLookLimit);

        // Pitch from mouse, tiny roll from head bob
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, bobRollAngle);
        transform.Rotate(0f, mouseX, 0f);
    }

    private void HandleMovement()
    {
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        moveInput = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));

        // Prevent faster diagonal movement
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        // Sprint only while holding R and actually moving
        IsSprinting = Input.GetKey(sprintKey) && moveInput.sqrMagnitude > 0.01f;
        float speed = IsSprinting ? sprintSpeed : walkSpeed;

        Vector3 move = transform.TransformDirection(moveInput) * speed;

        // Jump
        if (isGrounded && Input.GetButtonDown("Jump"))
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        // Gravity
        velocity.y += gravity * Time.deltaTime;

        // One Move call per frame
        controller.Move((move + velocity) * Time.deltaTime);
    }

    private void HandleHeadBob()
    {
        if (cameraTransform == null) return;

        // Re-read every frame so Camera Height / capsule edits apply live during Play
        cameraDefaultLocalPos.y = EyeLocalY();

        if (!enableHeadBob)
        {
            cameraTransform.localPosition = cameraDefaultLocalPos;
            bobRollAngle = 0f;
            return;
        }

        bool isMoving = moveInput.sqrMagnitude > 0.01f && isGrounded;

        // Pick target values based on state
        float targetFrequency = IsSprinting ? sprintBobFrequency : walkBobFrequency;
        float targetAmplitude = 0f;
        float targetRoll = 0f;

        if (isMoving)
        {
            float inputStrength = moveInput.magnitude;
            targetAmplitude = (IsSprinting ? sprintBobAmplitude : walkBobAmplitude) * inputStrength;
            targetRoll = (IsSprinting ? sprintBobRoll : walkBobRoll) * inputStrength;
        }

        // Smoothly blend between idle / walk / sprint (no snapping)
        float blend = bobBlendSpeed * Time.deltaTime;
        currentBobFrequency = Mathf.Lerp(currentBobFrequency, targetFrequency, blend);
        currentBobAmplitude = Mathf.Lerp(currentBobAmplitude, targetAmplitude, blend);
        currentBobRoll = Mathf.Lerp(currentBobRoll, targetRoll, blend);

        // Only advance the step cycle while moving
        if (isMoving)
            bobTimer += Time.deltaTime * currentBobFrequency;

        // Vertical: two dips per cycle (one per footstep)
        // Horizontal: one sway per cycle (left foot, right foot)
        float bobY = Mathf.Sin(bobTimer * 2f) * currentBobAmplitude;
        float bobX = Mathf.Cos(bobTimer) * currentBobAmplitude * 0.5f;

        cameraTransform.localPosition = cameraDefaultLocalPos + new Vector3(bobX, bobY, 0f);
        bobRollAngle = Mathf.Cos(bobTimer) * currentBobRoll;
    }
}
