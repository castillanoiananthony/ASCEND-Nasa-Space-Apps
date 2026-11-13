using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Put this on the cube you want to use as a screen (it needs a Collider).
/// Drag any Camera into "Feed Camera" and the cube shows what that camera sees.
/// Look at the cube and press E to switch the feed on/off.
/// During a launch it can pull back for a wide shot, then switch to the rocket's nose.
/// </summary>
public class CameraScreen : MonoBehaviour
{
    public enum ScreenSide { MinusZ, PlusZ, MinusX, PlusX }

    [Header("Feed")]
    [Tooltip("The camera whose view is shown on the screen.")]
    public Camera feedCamera;
    public bool startOn = true;
    [Tooltip("Vertical resolution of the feed. Width follows the screen's shape.")]
    public int resolution = 512;
    [Tooltip("Optional. The feed camera keeps looking at this (e.g. the Rocket).")]
    public Transform trackTarget;
    [Tooltip("Optional. If Track Target is empty, the object with this tag is tracked instead. Leave blank for a fixed camera.")]
    public string trackTag = "";

    [Header("Launch Shots")]
    [Tooltip("Follow the launch: wide shot, then nose cam. Turn off for a camera that never moves.")]
    public bool useLaunchShots = true;
    [Tooltip("Optional. Defaults to the RocketLaunchConsole in the scene.")]
    public RocketLaunchConsole launchConsole;
    [Tooltip("The T+ time when the camera starts pulling back.")]
    public float widenAtTime = 20f;
    [Tooltip("How far it pulls back, as a multiple of its starting distance from the rocket (1.6 = 60% farther).")]
    public float widenDistanceMultiplier = 1.6f;
    [Tooltip("How many seconds the pull-back takes.")]
    public float widenMoveSeconds = 3f;
    [Tooltip("The T+ time when the feed switches to the rocket's nose cam.")]
    public float noseAtTime = 45f;

    [Header("Screen")]
    [Tooltip("Which face of the cube is the screen. Try another if the picture shows on the wrong side.")]
    public ScreenSide screenSide = ScreenSide.PlusX;
    [Tooltip("Optional. Leave empty and the script makes an unlit material.")]
    public Material screenMaterialOverride;

    [Header("Interaction")]
    public float interactDistance = 4f;
    public string promptWhenOn = "Press <b>E</b> to turn camera off";
    public string promptWhenOff = "Press <b>E</b> to turn camera on";
    public Vector2 promptPosition = new Vector2(0f, -80f);
    public float promptFontSize = 28f;
    [Tooltip("Optional. Defaults to Camera.main at Start.")]
    public Camera playerCamera;
    [Tooltip("Optional. Defaults to the first Canvas in the scene.")]
    public Canvas uiCanvas;
    public TMP_FontAsset font;

    bool isOn;
    RenderTexture rt;
    Material screenMat;
    TextMeshProUGUI promptText;

    // Where the camera was placed in the scene, so it can go back after a launch.
    Vector3 homePos;
    Quaternion homeRot;
    float homeFar;
    bool movedByLaunch;
    bool widenPlanned;
    Vector3 widenToPos;

    Transform taggedTarget;
    Transform cachedCenterTarget;
    Vector3 cachedCenterLocal;

    // ---------------------------------------------------------------------------------------

    void Start()
    {
        if (!playerCamera) playerCamera = Camera.main;
        if (!uiCanvas) uiCanvas = FindFirstObjectByType<Canvas>();
        if (!launchConsole) launchConsole = FindFirstObjectByType<RocketLaunchConsole>();

        if (!feedCamera)
        {
            Debug.LogWarning($"[CameraScreen] No Feed Camera assigned on '{name}'.");
            enabled = false;
            return;
        }
        if (feedCamera == playerCamera)
            Debug.LogWarning("[CameraScreen] Feed Camera is your player camera. Use a separate camera.");

        // The feed camera must never take over the main display or the audio.
        var listener = feedCamera.GetComponent<AudioListener>();
        if (listener) listener.enabled = false;
        if (feedCamera.CompareTag("MainCamera")) feedCamera.tag = "Untagged";

        homePos = feedCamera.transform.position;
        homeRot = feedCamera.transform.rotation;
        homeFar = feedCamera.farClipPlane;

        BuildScreen();
        SetOn(startOn);
    }

    void Update()
    {
        bool looking = IsLookingAtThis();
        ShowPrompt(looking);
        if (looking && KeyDown()) SetOn(!isOn);
    }

    void LateUpdate()
    {
        if (!isOn || !feedCamera) return;

        Transform camT = feedCamera.transform;
        Transform target = ResolveTarget();
        bool launching = useLaunchShots && launchConsole && launchConsole.IsLaunching;

        if (!launching)
        {
            if (movedByLaunch) RestoreHome();
            if (target) camT.LookAt(GetCenter(target));
            return;
        }

        float t = launchConsole.MissionTime;

        // Nose shot
        if (t >= noseAtTime && launchConsole.TryGetNoseCamPose(out Vector3 nosePos, out Quaternion noseRot))
        {
            camT.SetPositionAndRotation(nosePos, noseRot);
            feedCamera.farClipPlane = Mathf.Max(homeFar, 10000f);
            movedByLaunch = true;
            return;
        }

        // Wide shot: slide straight back, away from the rocket
        if (t >= widenAtTime)
        {
            Transform rocket = target ? target : launchConsole.Rocket;
            if (!widenPlanned && rocket)
            {
                Vector3 center = GetCenter(rocket);
                Vector3 away = homePos - center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) { away = homeRot * Vector3.back; away.y = 0f; }
                float dist = away.magnitude;
                widenToPos = homePos + away.normalized * Mathf.Max(dist, 1f) * (widenDistanceMultiplier - 1f);
                widenPlanned = true;
            }
            if (widenPlanned)
            {
                float k = Mathf.SmoothStep(0f, 1f, (t - widenAtTime) / Mathf.Max(0.01f, widenMoveSeconds));
                camT.position = Vector3.Lerp(homePos, widenToPos, k);
                feedCamera.farClipPlane = Mathf.Max(homeFar, 10000f);
                movedByLaunch = true;
            }
        }

        if (target) camT.LookAt(GetCenter(target));
    }

    void RestoreHome()
    {
        feedCamera.transform.SetPositionAndRotation(homePos, homeRot);
        feedCamera.farClipPlane = homeFar;
        movedByLaunch = false;
        widenPlanned = false;
    }

    void OnDisable()
    {
        ShowPrompt(false);
    }

    void OnDestroy()
    {
        if (feedCamera) feedCamera.targetTexture = null;
        if (rt) { rt.Release(); Destroy(rt); }
        if (screenMat && screenMat != screenMaterialOverride) Destroy(screenMat);
        if (promptText) Destroy(promptText.gameObject);
    }

    /// <summary>Can also be called from your own interaction system or a UI button.</summary>
    public void Toggle() => SetOn(!isOn);

    public void SetOn(bool on)
    {
        isOn = on;
        if (feedCamera) feedCamera.enabled = on; // off = camera stops rendering, saves performance
        SetScreenTexture(on ? rt : Texture2D.blackTexture);
    }

    // ---------------------------------------------------------------------------------------
    // Tracking

    Transform ResolveTarget()
    {
        if (trackTarget) return trackTarget;
        if (string.IsNullOrEmpty(trackTag)) return null;
        if (!taggedTarget)
        {
            var go = GameObject.FindWithTag(trackTag);
            if (go) taggedTarget = go.transform;
        }
        return taggedTarget;
    }

    /// <summary>
    /// Centre of the target's meshes, measured once while it's at rest and then carried along with it.
    /// Particles (fire, smoke) are ignored so they can't drag the aim point around.
    /// </summary>
    Vector3 GetCenter(Transform t)
    {
        if (t != cachedCenterTarget)
        {
            cachedCenterTarget = t;
            bool any = false;
            Bounds b = default;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            cachedCenterLocal = t.InverseTransformPoint(any ? b.center : t.position);
        }
        return t.TransformPoint(cachedCenterLocal);
    }

    // ---------------------------------------------------------------------------------------
    // Screen setup

    void BuildScreen()
    {
        // Size of the chosen face in world units, so the picture isn't stretched.
        Vector3 s = transform.lossyScale;
        bool zFace = screenSide == ScreenSide.MinusZ || screenSide == ScreenSide.PlusZ;
        float faceWidth = Mathf.Abs(zFace ? s.x : s.z);
        float faceHeight = Mathf.Abs(s.y);
        float aspect = faceHeight > 0.0001f ? faceWidth / faceHeight : 16f / 9f;

        int h = Mathf.Max(64, resolution);
        int w = Mathf.Max(64, Mathf.RoundToInt(h * aspect));
        rt = new RenderTexture(w, h, 24) { name = $"{name} Feed" };
        rt.Create();
        feedCamera.targetTexture = rt;

        // A quad sitting just in front of the chosen face of the cube.
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Screen";
        Destroy(quad.GetComponent<Collider>()); // the cube's collider handles interaction
        quad.transform.SetParent(transform, false);

        const float o = 0.501f;
        switch (screenSide)
        {
            case ScreenSide.MinusZ:
                quad.transform.localPosition = new Vector3(0f, 0f, -o);
                quad.transform.localRotation = Quaternion.identity;
                break;
            case ScreenSide.PlusZ:
                quad.transform.localPosition = new Vector3(0f, 0f, o);
                quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                break;
            case ScreenSide.MinusX:
                quad.transform.localPosition = new Vector3(-o, 0f, 0f);
                quad.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                break;
            case ScreenSide.PlusX:
                quad.transform.localPosition = new Vector3(o, 0f, 0f);
                quad.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                break;
        }
        quad.transform.localScale = Vector3.one;

        if (screenMaterialOverride)
        {
            screenMat = screenMaterialOverride;
        }
        else
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Unlit/Texture");
            screenMat = new Material(sh);
        }

        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = screenMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void SetScreenTexture(Texture tex)
    {
        if (!screenMat) return;
        if (screenMat.HasProperty("_BaseMap")) screenMat.SetTexture("_BaseMap", tex); // URP
        if (screenMat.HasProperty("_MainTex")) screenMat.SetTexture("_MainTex", tex); // Built-in
    }

    // ---------------------------------------------------------------------------------------
    // Prompt

    void ShowPrompt(bool show)
    {
        if (!show)
        {
            if (promptText && promptText.gameObject.activeSelf) promptText.gameObject.SetActive(false);
            return;
        }
        if (!uiCanvas) return;

        if (!promptText)
        {
            var go = new GameObject($"{name} Prompt", typeof(RectTransform));
            go.transform.SetParent(uiCanvas.transform, false);
            var prt = (RectTransform)go.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(700f, 60f);

            promptText = go.AddComponent<TextMeshProUGUI>();
            if (font) promptText.font = font;
            promptText.color = Color.white;
            promptText.alignment = TextAlignmentOptions.Center;
            promptText.raycastTarget = false;
        }

        ((RectTransform)promptText.transform).anchoredPosition = promptPosition;
        promptText.fontSize = promptFontSize;
        promptText.text = isOn ? promptWhenOn : promptWhenOff;
        if (!promptText.gameObject.activeSelf) promptText.gameObject.SetActive(true);
    }

    // ---------------------------------------------------------------------------------------
    // Input

    bool IsLookingAtThis()
    {
        Camera cam = playerCamera ? playerCamera : Camera.main;
        if (!cam || !cam.enabled) return false; // e.g. while watching from the rocket cams
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide))
            return hit.transform == transform || hit.transform.IsChildOf(transform);
        return false;
    }

    static bool KeyDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }
}
