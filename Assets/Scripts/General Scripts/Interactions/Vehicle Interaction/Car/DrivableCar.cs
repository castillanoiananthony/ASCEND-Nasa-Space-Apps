using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Put this on an EMPTY parent of the car model (Rotation 0,0,0, Scale 1), with the model's
/// front pointing along the parent's blue Z arrow.
/// Look at the car + E to get in, WASD / arrows to drive, Space = handbrake, E to get out.
/// The view is from the driver's seat.
/// </summary>
[DisallowMultipleComponent]
public class DrivableCar : MonoBehaviour
{
    [Header("Enter / Exit")]
    [Tooltip("How close (metres) you need to be to the car to get in. You don't have to aim at it.")]
    public float interactDistance = 3f;
    public string rideCarPrompt = "Press <b>E</b> to ride car";
    public string exitCarPrompt = "Press <b>E</b> to exit car";
    public string controlsHint = "<b>WASD</b> drive    <b>Space</b> handbrake";
    public Vector2 promptPosition = new Vector2(0f, -80f);
    public float promptFontSize = 28f;
    [Tooltip("Optional. Defaults to the object tagged Player.")]
    public GameObject player;
    [Tooltip("Optional. Defaults to Camera.main at Start.")]
    public Camera playerCamera;
    [Tooltip("Height of the Player's pivot above the ground when getting out.")]
    public float exitHeightAboveGround = 1f;

    [Header("Driver View")]
    [Tooltip("Optional. An empty child placed where the driver's eyes are, facing forward. Leave empty to guess from the car's size.")]
    public Transform driverView;
    [Tooltip("Nudges the guessed seat position (metres, car's local axes). You can tweak it while driving.")]
    public Vector3 driverViewOffset = Vector3.zero;
    public float fieldOfView = 70f;
    public float mouseSensitivity = 0.12f;
    public float maxLookYaw = 110f;
    public float maxLookPitch = 60f;

    [Header("Driving")]
    [Tooltip("m/s² of push from the engine.")]
    public float acceleration = 12f;
    [Tooltip("m/s² when pressing the opposite direction.")]
    public float brakeStrength = 25f;
    [Tooltip("m/s (30 = 108 km/h).")]
    public float maxForwardSpeed = 30f;
    public float maxReverseSpeed = 8f;
    public float steerDegreesPerSecond = 90f;
    [Tooltip("How hard the tyres resist sliding sideways.")]
    public float grip = 8f;
    [Tooltip("Grip while holding the handbrake (lower = drifts).")]
    public float handbrakeGrip = 1.5f;
    [Tooltip("Slow-down when you let go of the throttle.")]
    public float coastDrag = 0.6f;
    [Tooltip("How strongly the car rights itself.")]
    public float uprightStrength = 6f;
    public float mass = 1200f;

    [Header("UI")]
    [Tooltip("Optional. Defaults to the first Canvas in the scene.")]
    public Canvas uiCanvas;
    public TMP_FontAsset font;
    [Tooltip("Speedometer, bottom middle.")]
    public Vector2 speedPosition = new Vector2(0f, 60f);
    public float speedFontSize = 30f;
    [Tooltip("'Press E to exit car', at the very bottom.")]
    public Vector2 exitPromptPosition = new Vector2(0f, 20f);
    public float exitPromptFontSize = 20f;
    [Tooltip("WASD / Space controls, top right.")]
    public Vector2 controlsPosition = new Vector2(-30f, -30f);
    public float controlsFontSize = 18f;

    // ---------------------------------------------------------------------------------------

    Rigidbody rb;
    BoxCollider body;
    Bounds localBounds;
    Vector3 seatAuto;

    bool driving;
    Camera carCam;
    float lookYaw, lookPitch;

    float throttleIn, steerIn, steer;
    bool handbrakeIn;

    TextMeshProUGUI promptText, speedText, exitText, controlsText;
    readonly RaycastHit[] groundHits = new RaycastHit[8];

    // ---------------------------------------------------------------------------------------

    void Start()
    {
        if (!playerCamera) playerCamera = Camera.main;
        if (!player)
        {
            var p = GameObject.FindWithTag("Player");
            if (p) player = p;
            else if (playerCamera && playerCamera.transform.parent) player = playerCamera.transform.parent.gameObject;
        }
        if (!uiCanvas) uiCanvas = FindFirstObjectByType<Canvas>();

        localBounds = ComputeLocalBounds();
        SetupPhysics();

        // Rough driver's eye position: left side, near the top, roughly the middle of the car.
        seatAuto = new Vector3(
            localBounds.center.x - localBounds.extents.x * 0.35f,
            localBounds.min.y + localBounds.size.y * 0.78f,
            localBounds.center.z - localBounds.extents.z * 0.05f);
    }

    void Update()
    {
        if (!driving)
        {
            bool near = IsPlayerNear();
            SetText(ref promptText, "Car Prompt", near ? rideCarPrompt : null,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), promptPosition, promptFontSize, TextAlignmentOptions.Center);
            if (near && KeyE()) Enter();
            return;
        }

        if (KeyE()) { Exit(); return; }

        ReadDriveInput();
        MouseLook();

        float kmh = rb.linearVelocity.magnitude * 3.6f;

        // Speedometer: bottom middle
        SetText(ref speedText, "Car Speed", $"<b>{kmh:0}</b> km/h",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), speedPosition, speedFontSize, TextAlignmentOptions.Bottom);
        // Exit prompt: very bottom
        SetText(ref exitText, "Car Exit Prompt", exitCarPrompt,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), exitPromptPosition, exitPromptFontSize, TextAlignmentOptions.Bottom);
        // Controls: top right
        SetText(ref controlsText, "Car Controls", controlsHint,
                new Vector2(1f, 1f), new Vector2(1f, 1f), controlsPosition, controlsFontSize, TextAlignmentOptions.TopRight);
    }

    void HideHud()
    {
        SetText(ref speedText, null, null, default, default, default, 0f, default);
        SetText(ref exitText, null, null, default, default, default, 0f, default);
        SetText(ref controlsText, null, null, default, default, default, 0f, default);
    }

    void LateUpdate()
    {
        if (!driving || !carCam) return;

        Vector3 pos;
        Quaternion baseRot;
        if (driverView)
        {
            pos = transform.InverseTransformPoint(driverView.position);
            baseRot = Quaternion.Inverse(transform.rotation) * driverView.rotation;
        }
        else
        {
            pos = seatAuto + driverViewOffset;
            baseRot = Quaternion.identity;
        }
        carCam.transform.localPosition = pos;
        carCam.transform.localRotation = baseRot * Quaternion.Euler(lookPitch, lookYaw, 0f);
    }

    void OnDisable()
    {
        if (driving) Exit();
        SetText(ref promptText, null, null, default, default, default, 0f, default);
    }

    // ---------------------------------------------------------------------------------------
    // Getting in and out

    void Enter()
    {
        driving = true;
        lookYaw = lookPitch = 0f;
        steer = 0f;
        SetText(ref promptText, null, null, default, default, default, 0f, default);

        if (!carCam) CreateCarCamera();
        carCam.gameObject.SetActive(true);
        carCam.tag = "MainCamera";

        if (player) player.SetActive(false);
        else if (playerCamera) playerCamera.gameObject.SetActive(false);

        rb.isKinematic = false;
        rb.WakeUp();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Exit()
    {
        driving = false;
        throttleIn = steerIn = 0f;
        handbrakeIn = false;

        // Step out on the driver's (left) side, on the ground.
        Vector3 doorPos = transform.TransformPoint(new Vector3(localBounds.min.x, localBounds.center.y, localBounds.center.z))
                          - transform.right * 1f;
        Vector3 exitPos = doorPos;
        if (Physics.Raycast(doorPos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            exitPos = hit.point + Vector3.up * exitHeightAboveGround;

        if (carCam)
        {
            carCam.tag = "Untagged";
            carCam.gameObject.SetActive(false);
        }

        if (player)
        {
            player.transform.SetPositionAndRotation(exitPos, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            player.SetActive(true);
        }
        else if (playerCamera)
        {
            playerCamera.gameObject.SetActive(true);
        }

        HideHud();
        // The car brakes to a stop by itself and then parks (see FixedUpdate).
    }

    void CreateCarCamera()
    {
        var go = new GameObject("Car Camera");
        go.transform.SetParent(transform, false);
        carCam = go.AddComponent<Camera>();
        if (playerCamera) carCam.CopyFrom(playerCamera);
        carCam.fieldOfView = fieldOfView;
        carCam.nearClipPlane = 0.05f;
        go.AddComponent<AudioListener>();
        go.SetActive(false);
    }

    // ---------------------------------------------------------------------------------------
    // Physics

    void SetupPhysics()
    {
        // Mesh colliders on the model can't move with a physics car, so one box does the job.
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.gameObject != gameObject) c.enabled = false;

        body = GetComponent<BoxCollider>();
        if (!body)
        {
            body = gameObject.AddComponent<BoxCollider>();
            body.center = localBounds.center;
            body.size = localBounds.size;
        }
        // No friction on the body: grip is handled in code so the car slides smoothly over the terrain.
        body.sharedMaterial = new PhysicsMaterial("Car Body")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.centerOfMass = localBounds.center - Vector3.up * localBounds.extents.y * 0.6f; // low = hard to flip
        rb.isKinematic = true; // parked until someone gets in
    }

    void FixedUpdate()
    {
        if (rb.isKinematic) return;

        bool grounded = CheckGround(out Vector3 groundNormal);
        Vector3 vel = rb.linearVelocity;
        Vector3 fwd = transform.forward;
        Vector3 right = transform.right;
        float fwdSpeed = Vector3.Dot(vel, fwd);

        float throttle = driving ? throttleIn : 0f;
        bool handbrake = driving ? handbrakeIn : true; // nobody inside = brake to a stop

        if (grounded)
        {
            // Engine / brakes
            if (Mathf.Abs(throttle) > 0.01f)
            {
                bool braking = fwdSpeed * throttle < -0.5f; // pressing against the way we're moving
                if (braking)
                {
                    rb.AddForce(-fwd * Mathf.Sign(fwdSpeed) * brakeStrength, ForceMode.Acceleration);
                }
                else
                {
                    float limit = throttle > 0f ? maxForwardSpeed : maxReverseSpeed;
                    if (Mathf.Abs(fwdSpeed) < limit)
                        rb.AddForce(fwd * throttle * acceleration, ForceMode.Acceleration);
                }
            }
            else
            {
                rb.AddForce(-fwd * fwdSpeed * coastDrag, ForceMode.Acceleration);
            }

            if (handbrake)
                rb.AddForce(-fwd * Mathf.Clamp(fwdSpeed * 3f, -brakeStrength, brakeStrength), ForceMode.Acceleration);

            // Tyre grip: kill sideways sliding
            float sideSpeed = Vector3.Dot(vel, right);
            float g = (driving && handbrakeIn) ? handbrakeGrip : grip;
            rb.AddForce(-right * sideSpeed * g, ForceMode.Acceleration);

            // Don't roll down hills when stopped
            if (Mathf.Abs(throttle) < 0.01f && vel.magnitude < 2f)
                rb.AddForce(-Vector3.ProjectOnPlane(Physics.gravity, groundNormal), ForceMode.Acceleration);

            // Steering: only turns while moving, reversed when backing up (like a real car)
            float steerFactor = Mathf.Clamp01(Mathf.Abs(fwdSpeed) / 4f) * Mathf.Sign(fwdSpeed);
            float yawRate = (driving ? steer : 0f) * steerDegreesPerSecond * Mathf.Deg2Rad * steerFactor;
            Vector3 av = rb.angularVelocity;
            float currentYaw = Vector3.Dot(av, transform.up);
            rb.angularVelocity = av + transform.up * (yawRate - currentYaw);
        }

        // Stay upright: follow the slope on the ground, level out in the air
        Vector3 wantedUp = grounded ? groundNormal : Vector3.up;
        rb.AddTorque(Vector3.Cross(transform.up, wantedUp) * uprightStrength, ForceMode.Acceleration);

        // Park once it has stopped with nobody inside
        if (!driving && grounded && vel.magnitude < 0.3f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    bool CheckGround(out Vector3 normal)
    {
        Vector3 origin = transform.TransformPoint(new Vector3(localBounds.center.x, localBounds.min.y + 0.3f, localBounds.center.z));
        int n = Physics.RaycastNonAlloc(origin, -transform.up, groundHits, 0.8f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (groundHits[i].collider.attachedRigidbody == rb) continue;
            normal = groundHits[i].normal;
            return true;
        }
        normal = Vector3.up;
        return false;
    }

    Bounds ComputeLocalBounds()
    {
        bool any = false;
        Bounds b = default;
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(local, Vector3.zero); any = true; }
                else b.Encapsulate(local);
            }
        }
        if (!any) b = new Bounds(Vector3.up * 0.75f, new Vector3(1.8f, 1.5f, 4.5f));
        return b;
    }

    // ---------------------------------------------------------------------------------------
    // Input

    void ReadDriveInput()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) { throttleIn = steerIn = 0f; handbrakeIn = false; return; }
        throttleIn = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                   - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
        steerIn = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
        handbrakeIn = kb.spaceKey.isPressed;
#else
        throttleIn = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                   - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        steerIn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        handbrakeIn = Input.GetKey(KeyCode.Space);
#endif
        steer = Mathf.MoveTowards(steer, steerIn, Time.deltaTime * 4f); // smooth steering wheel
    }

    void MouseLook()
    {
        Vector2 d;
#if ENABLE_INPUT_SYSTEM
        d = Mouse.current != null ? Mouse.current.delta.ReadValue() * mouseSensitivity : Vector2.zero;
#else
        d = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity * 20f;
#endif
        lookYaw = Mathf.Clamp(lookYaw + d.x, -maxLookYaw, maxLookYaw);
        lookPitch = Mathf.Clamp(lookPitch - d.y, -maxLookPitch, maxLookPitch);
    }

    static bool KeyE()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }

    /// <summary>True when the player is standing within interactDistance of the car's body.</summary>
    bool IsPlayerNear()
    {
        Transform p = player ? player.transform : (playerCamera ? playerCamera.transform : null);
        if (!p || !p.gameObject.activeInHierarchy || !body) return false;
        Vector3 closest = body.ClosestPoint(p.position);
        return (closest - p.position).sqrMagnitude <= interactDistance * interactDistance;
    }

    // ---------------------------------------------------------------------------------------
    // UI

    /// <summary>Shows text on the canvas (creating it the first time), or hides it when text is null.</summary>
    void SetText(ref TextMeshProUGUI label, string objName, string text, Vector2 anchor, Vector2 pivot,
                 Vector2 pos, float size, TextAlignmentOptions align)
    {
        if (text == null)
        {
            if (label && label.gameObject.activeSelf) label.gameObject.SetActive(false);
            return;
        }
        if (!uiCanvas) return;

        if (!label)
        {
            var go = new GameObject(objName, typeof(RectTransform));
            go.transform.SetParent(uiCanvas.transform, false);
            label = go.AddComponent<TextMeshProUGUI>();
            if (font) label.font = font;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        // Applied every time so position/size can be tweaked in the Inspector while playing.
        var rt = (RectTransform)label.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(900f, 80f);
        label.alignment = align;
        label.fontSize = size;

        label.text = text;
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
    }
}
