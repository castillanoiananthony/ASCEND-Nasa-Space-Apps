// ═════════════════════════════════════════════════════════════════════════════
//  AscendFlight.cs  -  launch the approved rocket with real physics
//
//  Put "AscendLaunchController" on your Launch Computer (it needs a Collider).
//  Look at it and press E: the LAUNCH COMPUTER screen opens (rocket, payload, info, T-minus, LAUNCH).
//  Press LAUNCH: countdown, ignition, liftoff. Esc or X closes the screen.
//
//  Real physics, using the real numbers from the AscendCAD part catalog:
//    - thrust changes with air pressure (sea level -> vacuum), propellant burns away so the rocket gets lighter
//    - gravity weakens with height, air gets thinner with height (density + speed of sound)
//    - drag from the rocket's real width, changing with Mach number
//    - side boosters burn with the core and drop off when empty, stages separate at burnout,
//      the fairing opens high up
//    - a gravity turn: straight up, small pitch kick, then follow the flight path
//  Ways to fail (just like real rockets):
//    - too heavy to leave the pad (thrust-to-weight below 1)
//    - air pressure too high (Max Q) -> breaks apart
//    - no guidance (flight computer + IMU) -> tumbles sideways -> ripped apart
//    - too much g-force -> crushed
//    - out of fuel before space -> falls back and crashes
//  Reaching 100 km (Karman line) = SPACE: the screen fades and the rocket vanishes (next scene comes later).
//
//  Keys while flying: 1 = chase camera (hold right mouse to look), 2 = nose camera, 3 = ground tracking camera,
//  - / + = time speed (up to 2x), H = hide/show the windows, scroll = zoom.
//  Earth missions: at the Karman line the rocket continues in the Solar System scene (AscendOrbit.cs).
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Object = UnityEngine.Object;

public class AscendLaunchController : MonoBehaviour
{
    [Header("Launch Computer (this object needs a Collider)")]
    public KeyCode launchKey = KeyCode.E;
    public float interactDistance = 5f;

    [Header("Data")]
    [Tooltip("Your AscendCAD Data asset. Leave empty to use the one on the computer.")]
    public AscendCADData ascendCADData;

    [Header("Flight")]
    public float countdownSeconds = 10f;
    [Tooltip("World direction the rocket leans toward after liftoff.")]
    public Vector3 downrangeDirection = new Vector3(1f, 0f, 0f);
    [Tooltip("Height where space begins (Karman line).")]
    public float karmanLineKm = 100f;
    [Tooltip("Height where the payload fairing opens.")]
    public float fairingJettisonKm = 85f;
    [Tooltip("How much air push (dynamic pressure, kPa) the rocket can take. Real rockets peak around 30-35 kPa.")]
    public float structuralLimitKPa = 45f;
    [Tooltip("Limit when the top of the rocket is not a nose cone or fairing.")]
    public float bluntNoseLimitKPa = 25f;
    [Tooltip("Most g-force the structure survives.")]
    public float maxGForce = 10f;

    [Header("The Philippines below (shown during the flight)")]
    [Tooltip("Show the real-size Philippines and the sea around the launch site, with Earth's curve.")]
    public bool showPhilippines = true;
    [Tooltip("Where your launch site is: latitude, longitude. Default: Baler, Aurora (east coast of Luzon, so rockets fly out over the Pacific).")]
    public Vector2 launchSiteLatLon = new Vector2(15.76f, 121.56f);
    [Tooltip("Ground height of your world at the launch site (the map sits just under it).")]
    public float groundHeight = 0.5f;
    [Tooltip("Show a layer of clouds about 2.5 km up (you fly through it).")]
    public bool showClouds = true;
    [Tooltip("Optional: a real satellite image of the area (e.g. NASA Blue Marble). Leave empty for the painted map.")]
    public Texture2D customMap;
    [Tooltip("The custom image's edges: west longitude, south latitude, east longitude, north latitude.")]
    public Vector4 customMapBounds = new Vector4(110f, -2f, 138f, 26f);

    [Header("After the Karman line (Earth Watcher / Moon Mapper)")]
    [Tooltip("Continue the flight in orbit in this scene (it must be in File > Build Profiles / Build Settings).")]
    public bool continueInOrbitScene = true;
    public string orbitSceneName = "Solar System";

    [Header("Player")]
    [Tooltip("Movement and mouse-look scripts to switch off during the flight.")]
    public Behaviour[] disableWhileFlying;
    [Tooltip("Things like the crosshair to hide during the flight.")]
    public GameObject[] hideWhileFlying;

    [Header("Look")]
    [Tooltip("Optional TextMesh Pro font.")]
    public TMP_FontAsset font;

    [Header("Events")]
    [Tooltip("Runs when the rocket reaches the Karman line (hook the space scene up here later).")]
    public UnityEvent onReachedSpace;
    public UnityEvent onFlightFailed;

    // ───────────── constants ─────────────
    const double EarthR = 6371000.0, Mu = 3.986004418e14;
    const float G0 = 9.80665f, StepDt = 0.02f;
    const float MinWarp = 0.5f, MaxWarp = 5f;   // time speed slider; it speeds up by itself as the rocket climbs
    static readonly float[] BandKm = { 5f, 20f, 45f, 70f };   // above these heights: 2x, 3x, 4x, 5x

    // ───────────── runtime: parts ─────────────
    class Eng
    {
        public CADPart p; public int part; public string pool; public bool solid, dead, lit; public Grp grp;
        public float igniteTime = float.MaxValue, thrustN, mdot;
        public Transform t; public ParticleSystem flame, smoke; public Light glow; public float size;
    }
    class Grp
    {
        public string key, label; public bool side, attached = true; public int stage; public float dia;
        public readonly List<int> parts = new List<int>(); public readonly List<string> pools = new List<string>();
        public readonly List<Eng> engines = new List<Eng>();
    }

    enum Phase { Idle, Countdown, Flight, Ending }

    // Guide hooks (AscendGuide): the console opened / a launch started, and the parts of the screen to explain
    public static event Action<AscendLaunchController> ConsoleOpened;
    public static event Action LaunchStarted;
    public static bool HasLaunched { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetGuideStatics() { HasLaunched = false; }
    public RectTransform TourVehicle, TourPayload, TourInfo, TourTMinus, TourLaunch, TourClose;
    public bool ConsoleIsOpen => consoleOpen;
    public void DevOpenConsole() { if (phase == Phase.Idle && !consoleOpen && AscendLaunchSite.Current) OpenConsole(); }
    Phase phase = Phase.Idle;

    AscendCADData data;
    AscendRocket info;
    [NonSerialized] CADDesign design;
    CADPart[] P;
    Transform[] partGo;
    bool[] attached;
    int[] stageOf;
    Grp[] grpOf;
    readonly List<Grp> groups = new List<Grp>();
    readonly List<Eng> engines = new List<Eng>();
    readonly Dictionary<string, float> fuel = new Dictionary<string, float>(), fuelStart = new Dictionary<string, float>();
    readonly Dictionary<string, float> need = new Dictionary<string, float>();
    readonly List<int> fairingParts = new List<int>();
    Transform payloadGo;
    float payloadKg, coreDia, rocketHeight;
    int stageCount, curStage;
    bool guided, aeroNose, passive, cansat, engineCut;
    float goalKm = 100f, railDeg;
    string successBig = "100 KM - SPACE!";

    // ───────────── runtime: physics state ─────────────
    double px, py, vx, vy;            // 2D, Earth center at (0, -R) -> launch site at (0, R)
    float tFlight, tilt, throttle = 1f, tumbleRate;
    bool liftedOff, stageBurnout, pendingIgnite, fairingGone, maxQSaid, machSaid, throttleSaid, gThrottleSaid, fallingSaid, ended;
    float burnoutTime, igniteAt, kickStart = -1f;
    float q, mach, gNow, mass, altitude, speed, vUp;
    float maxQ, maxAlt, maxSpeed, maxG, liftoffTWR;
    float warp = 1f; int warpBand = -1; Slider warpSlider;
    AscendTrajectoryGraph graph;
    string stageLabel = "";

    // ───────────── runtime: world ─────────────
    Transform root;
    Vector3 basePos, downDir;
    readonly List<GameObject> debris = new List<GameObject>();
    Camera flightCam, playerCam;
    int camMode = 1;   // 1 chase, 2 nose, 3 ground tracking
    float camYaw, camPitch = 8f, camDist;
    float shake;
    Material skyCopy, skyOriginal;
    GameObject earthMap, cloudLayer;
    float skyExposure = 1f, skyAtmo = 1f;
    CursorLockMode savedLock; bool savedVisible;

    // ───────────── UI ─────────────
    TextMeshProUGUI promptText, bigText, logText, altLabel, endTitle, endBody, warpText, metText, stageText, warpLabel;
    readonly List<(TextMeshProUGUI value, RectTransform fill, Image img)> gauges = new List<(TextMeshProUGUI, RectTransform, Image)>();
    float thrustNow;
    GameObject hudRoot, endCard;
    RectTransform altFill;
    readonly List<(string name, GameObject go)> windows = new List<(string, GameObject)>();
    readonly List<Button> windowBtns = new List<Button>();

    void ToggleWindow(int i)
    {
        if (i < 0 || i >= windows.Count || !windows[i].go) return;
        windows[i].go.SetActive(!windows[i].go.activeSelf);
        RefreshWindowButtons();
    }

    void ToggleAllWindows()
    {
        bool anyOn = windows.Any(w => w.go && w.go.activeSelf);
        foreach (var w in windows) if (w.go) w.go.SetActive(!anyOn);
        RefreshWindowButtons();
    }

    void RefreshWindowButtons()
    {
        for (int i = 0; i < windowBtns.Count && i < windows.Count; i++)
            MissionUI.SetColor(windowBtns[i], windows[i].go && windows[i].go.activeSelf ? MissionUI.Line : MissionUI.LineDim);
    }
    CanvasGroup fade;
    readonly List<(Grp g, RectTransform fill, TextMeshProUGUI label)> fuelRows = new List<(Grp, RectTransform, TextMeshProUGUI)>();
    readonly List<string> log = new List<string>();
    float bigUntil;

    // Launch Computer screen
    GameObject consoleRoot, infoBox;
    RawImage rocketPic, payloadPic;
    RenderTexture rocketTex, payloadTex;
    TextMeshProUGUI conRocket, conPayload, conInfo, conMsg, infoBtnText;
    TMP_InputField tMinusField;
    bool consoleOpen;
    CursorLockMode consoleLock; bool consoleVisible;
    Coroutine flashRoutine;

    // ═════════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═════════════════════════════════════════════════════════════════════════
    void Start()
    {
        if (showPhilippines) AscendEarthMap.Prepare();   // paint the map and clouds in the background
        BuildUI();
        hudRoot.SetActive(false);
        endCard.SetActive(false);
        bigText.text = "";
    }

    void Update()
    {
        if (consoleOpen)
        {
            if (KeyDown(KeyCode.Escape)) CloseConsole();
            return;
        }
        switch (phase)
        {
            case Phase.Idle: IdleUpdate(); break;
            case Phase.Flight: FlightUpdate(); break;
        }
        if (bigText && Time.unscaledTime > bigUntil && phase != Phase.Countdown) bigText.text = "";
    }

    void LateUpdate()
    {
        if (phase == Phase.Flight || phase == Phase.Countdown || (phase == Phase.Ending && flightCam)) UpdateCamera();
    }

    void OnDestroy()
    {
        if (skyOriginal) RenderSettings.skybox = skyOriginal;
        if (rocketTex) rocketTex.Release();
        if (payloadTex) payloadTex.Release();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LAUNCH CUBE INTERACTION
    // ═════════════════════════════════════════════════════════════════════════
    void IdleUpdate()
    {
        bool looking = LookingAtMe();
        if (flashRoutine == null)
            promptText.text = looking ? KeyName(launchKey) + " to use the Launch Computer" : "";
        if (!looking || !KeyDown(launchKey)) return;

        if (!AscendLaunchSite.Current)
        {
            Flash("No rocket on the launch pad yet. Get your design APPROVED in AscendCAD first.");
            return;
        }
        OpenConsole();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LAUNCH COMPUTER SCREEN
    // ═════════════════════════════════════════════════════════════════════════
    void OpenConsole()
    {
        consoleOpen = true;
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        promptText.text = "";
        consoleLock = Cursor.lockState; consoleVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (disableWhileFlying != null) foreach (var b in disableWhileFlying) if (b) b.enabled = false;
        if (hideWhileFlying != null) foreach (var g in hideWhileFlying) if (g) g.SetActive(false);
        conMsg.text = "";
        if (string.IsNullOrEmpty(tMinusField.text)) tMinusField.text = Mathf.RoundToInt(countdownSeconds).ToString();
        infoBox.SetActive(false); infoBtnText.text = "ROCKET INFO   +";
        RefreshConsole();
        consoleRoot.SetActive(true);
        try { ConsoleOpened?.Invoke(this); } catch (Exception e) { Debug.LogException(e); }
    }

    void CloseConsole()
    {
        consoleOpen = false;
        consoleRoot.SetActive(false);
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es) es.SetSelectedGameObject(null);
        Cursor.lockState = consoleLock; Cursor.visible = consoleVisible;
        if (disableWhileFlying != null) foreach (var b in disableWhileFlying) if (b) b.enabled = true;
        if (hideWhileFlying != null) foreach (var g in hideWhileFlying) if (g) g.SetActive(true);
    }

    void ToggleInfo()
    {
        bool on = !infoBox.activeSelf;
        infoBox.SetActive(on);
        infoBtnText.text = on ? "ROCKET INFO   -" : "ROCKET INFO   +";
    }

    void ConsoleLaunch()
    {
        string txt = (tMinusField.text ?? "").Trim().Replace("T-", "").Replace("t-", "").Replace("s", "");
        if (!int.TryParse(txt, out int sec) || sec < 3 || sec > 60)
        {
            conMsg.text = "<color=#FFD166>Type the T-minus countdown in seconds: a number from 3 to 60.</color>";
            return;
        }
        if (!AscendLaunchSite.Current) { conMsg.text = "<color=#FFD166>The rocket is no longer on the pad.</color>"; return; }
        countdownSeconds = sec;
        CloseConsole();
        HasLaunched = true;
        try { LaunchStarted?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        StartCoroutine(LaunchSequence());
    }

    // Grey fixed-width label column for the console readouts
    static string L(string label, int col = 30) => $"<color=#9AA0A6><size=80%>{label}</size></color><pos={col}%>";

    void RefreshConsole()
    {
        var site = AscendLaunchSite.Current;
        var rk = site ? site.GetComponent<AscendRocket>() : null;
        var d = ascendCADData ? AscendCADData.Resolve(ascendCADData) : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
        if (!rk || rk.rocket == null || d == null)
        {
            conRocket.text = "<color=#9AA0A6>NO VEHICLE ON THE PAD</color>"; conPayload.text = "<color=#9AA0A6>-</color>"; conInfo.text = "";
            return;
        }
        var ps = rk.payload != null && !rk.payload.IsEmpty ? CADStats.Payload(rk.payload, d) : null;
        var rs = CADStats.Rocket(rk.rocket, d, ps != null ? ps.wetMass : 0f, AscendMissionGuide.RequiredDeltaV(d));

        // Pictures of the real 3D models
        var bg = Color.black;
        if (rocketTex) rocketTex.Release();
        if (payloadTex) payloadTex.Release();
        rocketTex = payloadTex = null;
        try { rocketTex = AscendSnapshot.Design(rk.rocket, d, 260, 400, bg); } catch (Exception e) { Debug.LogWarning("Launch Computer: rocket picture failed: " + e.Message); }
        try { if (rk.payload != null && !rk.payload.IsEmpty) payloadTex = AscendSnapshot.Design(rk.payload, d, 260, 400, bg); } catch (Exception e) { Debug.LogWarning("Launch Computer: payload picture failed: " + e.Message); }
        rocketPic.texture = rocketTex; rocketPic.color = rocketTex ? Color.white : Color.clear;
        payloadPic.texture = payloadTex; payloadPic.color = payloadTex ? Color.white : Color.clear;

        conRocket.text = $"<size=140%><b>{rk.rocket.name.ToUpperInvariant()}</b></size>\n" +
                         $"{L("STAGES", 50)}{rs.stages.Count}\n{L("HEIGHT", 50)}{rs.height:0.#} m\n{L("DIAMETER", 50)}{rs.width:0.#} m\n{L("LIFTOFF MASS", 50)}{AscendReport.Mass(rs.wetMass)}";
        conPayload.text = rk.payload != null && !rk.payload.IsEmpty
            ? $"<size=140%><b>{rk.payload.name.ToUpperInvariant()}</b></size>\n{L("MASS", 50)}{AscendReport.Mass(ps.wetMass)}\n{L("INSTRUMENTS", 50)}{ps.instruments}\n{L("POWER", 50)}{ps.powerGen:0.#} W" + (ps.satDeltaV > 0f ? $"\n{L("OWN ENGINE", 50)}{ps.satDeltaV:N0} m/s" : "")
            : "<color=#9AA0A6>NO PAYLOAD</color>";

        var sb = new StringBuilder();
        sb.Append($"{L("MISSION")}{AscendMissionGuide.Title}\n");
        var plan = AscendTrajectoryControl.ConfirmedPlan;
        if (plan != null) sb.Append($"{L("FLIGHT PATH")}{plan.name}  (pitch kick {plan.kickDeg:0.#}°, target {(plan.mission == AscendSimMission.SkySensor ? (plan.targetKm * 1000f).ToString("N0") + " m" : plan.targetKm.ToString("N0") + " km")})\n");
        var pad = d.GetPad(AscendEconomy.SelectedPad);
        if (pad != null) sb.Append($"{L("LAUNCH PAD")}{pad.name}\n");
        sb.Append($"{L("LIFTOFF THRUST")}{rs.liftoffThrustkN:N0} kN   (thrust-to-weight {rs.liftoffTWR:0.00})\n");
        sb.Append($"{L("DELTA-V")}{rs.deltaV:N0} m/s   (mission needs {AscendMissionGuide.RequiredDeltaV(d):N0} m/s)\n");
        foreach (var st in rs.stages)
            sb.Append($"{L("STAGE " + (st.index + 1))}{st.engines} engine(s), {st.thrustkN:N0} kN, {AscendReport.Mass(st.propMass)} propellant, burns {st.burnTime:0} s, {st.deltaV:N0} m/s\n");
        sb.Append($"{L("GUIDANCE")}{(rs.Has(CADRole.FlightComputer) && rs.Has(CADRole.IMU) ? "flight computer + IMU (steers itself)" : rs.Has(CADRole.Aero) ? "fins (flies straight up)" : "none!")}\n");
        sb.Append($"{L("PARTS")}{rs.partCount}\n{L("PARTS VALUE")}{AscendReport.Money(rs.partsValue)}");
        conInfo.text = sb.ToString();
    }

    void BuildConsole(RectTransform canvasRoot)
    {
        // Full black screen
        var bgImg = MissionUI.Solid(canvasRoot, MissionUI.Bg, true, "Launch Computer Screen");
        MissionUI.Stretch(bgImg.rectTransform);
        consoleRoot = bgImg.gameObject;

        var panel = MissionUI.Box(bgImg.transform, "Launch Computer", MissionUI.Line, null, true);
        MissionUI.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 960));

        // Header
        MissionUI.Header(panel, "LAUNCH COMPUTER", "ASCEND MISSION CONTROL   //   CONSOLE LC-01", 72f, 140f);
        var close = MissionUI.Btn(panel, "CLOSE", 15, MissionUI.Line, CloseConsole, 120, 40);
        MissionUI.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-20f, -16f), new Vector2(120, 40));
        TourClose = (RectTransform)close.transform;

        // Body (scrolls if the info is open)
        var body = AUI.Scroll(panel, 30, 30, 30, 96);
        AUI.V(body, 0, 0, 0, 10, 18);

        // Rocket + payload cards
        var cards = AUI.Rect("Cards", body);
        AUI.H(cards, 0, 0, 0, 0, 18, TextAnchor.UpperLeft);
        AUI.LE(cards, -1, 430);
        conRocket = Card(cards, "VEHICLE", out rocketPic);
        conPayload = Card(cards, "PAYLOAD", out payloadPic);
        TourVehicle = (RectTransform)conRocket.transform.parent; TourPayload = (RectTransform)conPayload.transform.parent;

        // Info dropdown
        var infoBtn = MissionUI.Btn(body, "ROCKET INFO   +", 18, MissionUI.Line, ToggleInfo, -1, 50);
        infoBtnText = infoBtn.GetComponentInChildren<TextMeshProUGUI>();
        TourInfo = (RectTransform)infoBtn.transform;
        infoBtnText.alignment = TextAlignmentOptions.MidlineLeft;
        infoBtnText.rectTransform.offsetMin = new Vector2(20f, 0f);
        var ib = MissionUI.Box(body, "Info", MissionUI.LineDim);
        AUI.V(ib, 24, 24, 18, 18, 0);
        conInfo = MissionUI.Txt(ib, "", 17, MissionUI.Text);
        conInfo.lineSpacing = 10f;
        infoBox = ib.gameObject;

        // T-minus
        var row = AUI.Rect("T-minus", body);
        AUI.H(row, 0, 0, 0, 0, 18);
        AUI.LE(row, -1, 60);
        TourTMinus = row;
        var lab = MissionUI.Lbl(row, "T-MINUS  (SEC)", 18);
        lab.color = MissionUI.Text;
        AUI.LE(lab, 260, 60);
        lab.alignment = TextAlignmentOptions.MidlineLeft;
        tMinusField = MissionUI.Field(row, "10", 26);
        tMinusField.contentType = TMP_InputField.ContentType.IntegerNumber;
        tMinusField.characterLimit = 2;
        AUI.LE(tMinusField, 140, 56);
        var hint = MissionUI.Txt(row, "3 to 60.  Countdown starts when you press LAUNCH.", 16, MissionUI.Label);
        AUI.LE(hint, -1, 56, 1); hint.alignment = TextAlignmentOptions.MidlineLeft;

        conMsg = MissionUI.Txt(body, "", 18, MissionUI.Text);

        // Launch
        var launch = MissionUI.Btn(body, "LAUNCH", 36, MissionUI.Alert, ConsoleLaunch, -1, 84);
        launch.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 30f;
        TourLaunch = (RectTransform)launch.transform;

        consoleRoot.SetActive(false);
    }

    // A card: outline box, small title, model picture in a thin frame on the left, text on the right
    static TextMeshProUGUI Card(Transform parent, string title, out RawImage pic)
    {
        var c = MissionUI.Box(parent, "Card", MissionUI.Line);
        AUI.LE(c, -1, 430, 1);
        var lab = MissionUI.Lbl(c, title, 14);
        MissionUI.Place(lab.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(300f, 26f));
        MissionUI.HLine(c, 40f, MissionUI.LineDim, 0f);

        var picR = MissionUI.Box(c, "Model", MissionUI.LineDim);
        picR.anchorMin = new Vector2(0f, 0f); picR.anchorMax = new Vector2(0f, 1f); picR.pivot = new Vector2(0f, 0.5f);
        picR.offsetMin = new Vector2(18f, 18f); picR.offsetMax = new Vector2(18f + 230f, -56f);
        var inner = MissionUI.Rect("Picture", picR); MissionUI.Stretch(inner, 2, 2, 2, 2);
        pic = inner.gameObject.AddComponent<RawImage>();
        pic.raycastTarget = false;

        var t = MissionUI.Txt(c, "", 18, MissionUI.Text);
        t.lineSpacing = 12f;
        t.alignment = TextAlignmentOptions.TopLeft;
        var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(268f, 18f); tr.offsetMax = new Vector2(-18f, -56f);
        return t;
    }

    bool LookingAtMe()
    {
        Camera cam = Camera.main;
        if (!cam) return false;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide)) return false;
        return hit.transform == transform || hit.transform.IsChildOf(transform);
    }

    void Flash(string msg)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashR(msg));
    }

    IEnumerator FlashR(string msg)
    {
        promptText.text = msg;
        yield return new WaitForSecondsRealtime(3f);
        promptText.text = "";
        flashRoutine = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PREPARE: read the real rocket (parts, stages, boosters, tanks, engines)
    // ═════════════════════════════════════════════════════════════════════════
    bool Prepare()
    {
        var site = AscendLaunchSite.Current;
        info = site ? site.GetComponent<AscendRocket>() : null;
        if (!info || info.rocket == null || info.rocket.IsEmpty) return false;
        data = ascendCADData ? AscendCADData.Resolve(ascendCADData) : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
        design = info.rocket;

        int n = design.parts.Count;
        P = new CADPart[n]; partGo = new Transform[n]; attached = new bool[n]; stageOf = new int[n]; grpOf = new Grp[n];
        groups.Clear(); engines.Clear(); fuel.Clear(); fuelStart.Clear(); fairingParts.Clear();

        var core = new bool[n]; var side = new int[n];
        for (int i = 0; i < n; i++)
        {
            P[i] = data.Get(design.parts[i].partId);
            core[i] = CADStats.IsCore(design, i);
            side[i] = SideRoot(design, i);
            attached[i] = P[i] != null;
        }
        for (int i = 0; i < n; i++) if (side[i] >= 0 && P[side[i]] != null && P[side[i]].crossfeed) side[i] = -1;

        // Stages are split by decouplers in the core stack (bottom = stage 1), same rule as AscendCAD
        var dec = new List<float>();
        for (int i = 0; i < n; i++) if (core[i] && P[i] != null && P[i].role == CADRole.Decoupler) dec.Add(design.parts[i].pos.y);
        dec.Sort();
        stageCount = dec.Count + 1;
        for (int i = 0; i < n; i++)
        {
            int a = i, guard = 0;
            while (!core[a] && design.parts[a].parent >= 0 && guard++ < 10000) a = design.parts[a].parent;
            float y = design.parts[a].pos.y; int st = 0;
            foreach (var v in dec) if (v < y - 1e-3f) st++;
            stageOf[i] = st;
        }

        // Groups: one per core stage, one per side booster
        var byKey = new Dictionary<string, Grp>();
        for (int i = 0; i < n; i++)
        {
            if (P[i] == null) continue;
            string key = side[i] >= 0 ? "side" + side[i] : "core" + stageOf[i];
            if (!byKey.TryGetValue(key, out var g))
            {
                g = new Grp { key = key, side = side[i] >= 0, stage = stageOf[i] };
                g.label = g.side ? "Booster" : "Stage " + (stageOf[i] + 1);
                byKey[key] = g; groups.Add(g);
            }
            g.parts.Add(i); grpOf[i] = g;
            if (P[i].IsBody && P[i].role != CADRole.Fairing) g.dia = Mathf.Max(g.dia, P[i].diameter);
            if (P[i].role == CADRole.Fairing) fairingParts.Add(i);

            if (P[i].role == CADRole.Tank && P[i].propellantKg > 0f) AddFuel(g, key + "|" + P[i].propellant, P[i].propellantKg);
            if (P[i].role == CADRole.SolidMotor && P[i].propellantKg > 0f) AddFuel(g, "solid" + i, P[i].propellantKg);
        }
        for (int k = 0; k < stageCount; k++)
            if (!byKey.ContainsKey("core" + k)) { var g = new Grp { key = "core" + k, stage = k, label = "Stage " + (k + 1) }; byKey[g.key] = g; groups.Add(g); }

        // Engines
        for (int i = 0; i < n; i++)
        {
            var p = P[i];
            if (p == null || (p.role != CADRole.Engine && p.role != CADRole.SolidMotor)) continue;
            var g = grpOf[i];
            var e = new Eng { p = p, part = i, grp = g, solid = p.role == CADRole.SolidMotor };
            e.pool = e.solid ? "solid" + i : g.key + "|" + p.propellant;
            if (!fuel.ContainsKey(e.pool)) e.dead = true;   // no tank of the right propellant feeding it
            engines.Add(e); g.engines.Add(e);
        }

        // Payload mass
        payloadKg = 0f;
        if (info.payload != null)
            foreach (var pl in info.payload.parts) { var pp = data.Get(pl.partId); if (pp != null) payloadKg += pp.WetMassKg; }

        // Map design parts to their 3D objects (AscendLaunchSite.BuildDesign makes them in order, skipping internal parts)
        Transform body = site.transform.childCount > 0 ? site.transform.GetChild(0) : null;
        int child = 0;
        if (body)
        {
            for (int i = 0; i < n; i++)
            {
                if (P[i] == null || P[i].attach == CADAttach.Internal) continue;
                if (child < body.childCount) partGo[i] = body.GetChild(child);
                child++;
            }
            for (int c = 0; c < body.childCount; c++)
                if (body.GetChild(c).name.StartsWith("Payload - ")) payloadGo = body.GetChild(c);
        }

        // Core width for drag
        coreDia = 0f;
        foreach (var g in groups) if (!g.side) coreDia = Mathf.Max(coreDia, g.dia);
        foreach (int i in fairingParts) coreDia = Mathf.Max(coreDia, P[i].diameter);
        if (coreDia <= 0f) coreDia = 1f;

        // Guidance + nose shape
        var rs = info.stats ?? CADStats.Rocket(design, data, payloadKg);
        guided = rs.Has(CADRole.FlightComputer) && rs.Has(CADRole.IMU);
        passive = !guided && rs.Has(CADRole.Aero);           // fins keep a small rocket pointing straight up
        cansat = AscendMissionGuide.Mission == AscendSimMission.SkySensor;
        goalKm = cansat ? (AscendTrajectoryControl.ConfirmedPlan != null ? AscendTrajectoryControl.ConfirmedPlan.targetKm : 1f) : karmanLineKm;
        railDeg = cansat && AscendTrajectoryControl.ConfirmedPlan != null ? AscendTrajectoryControl.ConfirmedPlan.kickDeg : 0f;
        aeroNose = TopIsAero();
        return true;
    }

    void AddFuel(Grp g, string pool, float kg)
    {
        fuel.TryGetValue(pool, out var v); fuel[pool] = v + kg;
        fuelStart.TryGetValue(pool, out var s); fuelStart[pool] = s + kg;
        if (!g.pools.Contains(pool)) g.pools.Add(pool);
    }

    static int SideRoot(CADDesign d, int i)
    {
        int guard = 0;
        while (i >= 0 && guard++ < 10000) { if (d.parts[i].link == CADLink.RadialUpright) return i; i = d.parts[i].parent; }
        return -1;
    }

    bool TopIsAero()
    {
        int top = -1; float topY = float.MinValue;
        for (int i = 0; i < design.parts.Count; i++)
        {
            var p = P[i];
            if (p == null || !CADStats.IsCore(design, i)) continue;
            float y = design.parts[i].pos.y + p.StackHeight * 0.5f;
            if (y > topY) { topY = y; top = i; }
        }
        var tp = top >= 0 ? P[top] : null;
        return tp != null && (tp.role == CADRole.NoseCone || tp.role == CADRole.Fairing || tp.shape == CADShape.OgiveTank);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LAUNCH SEQUENCE
    // ═════════════════════════════════════════════════════════════════════════
    IEnumerator LaunchSequence()
    {
        if (!Prepare()) { Flash("This rocket can't be read. Rebuild it in AscendCAD."); yield break; }

        phase = Phase.Countdown;
        promptText.text = "";
        TakeOverPlayer(true);
        SetupWorld();
        PointMonitorsAt(root);
        SetupCamera();
        SetupSky();
        BuildFuelRows();
        log.Clear();
        hudRoot.SetActive(true);
        AddLog("Rocket on the pad: " + design.name + "  (" + AscendReport.Mass(TotalMass()) + ")");
        if (passive) AddLog("Fins keep the rocket flying straight up.");
        else if (!guided) AddLog("<color=#F85149>Warning: no flight computer + IMU. The rocket can't steer!</color>");
        if (cansat) AddLog($"Mission: release the CanSat at about {goalKm:0.#} km.");

        // Countdown (Space skips)
        float t = countdownSeconds;
        while (t > 0f)
        {
            int s = Mathf.CeilToInt(t);
            bigText.text = "<size=60%>T-MINUS</size>\n" + s;
            if (KeyDown(KeyCode.Space)) break;
            t -= Time.deltaTime;
            yield return null;
        }

        // Ignition
        // Liquid engines are held down 1.2 s while they reach full thrust.
        // Solid motors can't be held: they light at release, so a solid-only rocket leaves the pad at once.
        bool liquidFirst = false;
        foreach (var e in engines) if (e.grp.stage == 0 && e.grp.attached && !e.dead && !e.solid) liquidFirst = true;
        holdTime = liquidFirst ? 1.2f : 0f;
        IgniteStage(0, 0f);
        foreach (var e in engines) if (e.grp.stage == 0 && e.solid) e.igniteTime = holdTime;
        ShowBig("IGNITION!", 1.5f);
        AddLog("Ignition! Engines start up.");
        phase = Phase.Flight;
    }

    void SetupWorld()
    {
        var site = AscendLaunchSite.Current.transform;
        var rends = site.GetComponentsInChildren<Renderer>();
        Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(site.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        rocketHeight = Mathf.Max(5f, b.size.y);
        focusMid = rocketHeight * 0.5f; focusTop = rocketHeight; focusBottom = 0f;
        basePos = new Vector3(b.center.x, b.min.y, b.center.z);

        root = new GameObject("Ascend Flight").transform;
        root.position = basePos;
        site.SetParent(root, true);
        foreach (var c in site.GetComponentsInChildren<Collider>()) c.enabled = false;  // moved by our own physics

        downDir = Vector3.ProjectOnPlane(downrangeDirection, Vector3.up);
        if (downDir.sqrMagnitude < 1e-4f) downDir = Vector3.right;
        downDir.Normalize();

        // The real-size Philippines under the launch site (with Earth's curve)
        if (earthMap) Destroy(earthMap);
        if (showPhilippines)
        {
            try { earthMap = AscendEarthMap.Build(new Vector3(basePos.x, groundHeight, basePos.z), launchSiteLatLon.x, launchSiteLatLon.y, customMap, customMapBounds); }
            catch (Exception e) { Debug.LogWarning("Ascend Flight: Philippines map failed: " + e.Message); }
            if (showClouds)
            {
                if (cloudLayer) Destroy(cloudLayer);
                try { cloudLayer = AscendEarthMap.BuildClouds(new Vector3(basePos.x, groundHeight, basePos.z), launchSiteLatLon.x, launchSiteLatLon.y); cloudLayer.SetActive(false); }
                catch (Exception e) { Debug.LogWarning("Ascend Flight: clouds failed: " + e.Message); }
            }
        }

        // Physics state
        px = 0; py = EarthR; vx = 0; vy = 0;
        tFlight = 0f; tilt = 0f; throttle = 1f; tumbleRate = 0f; curStage = 0;
        liftedOff = stageBurnout = pendingIgnite = fairingGone = maxQSaid = machSaid = throttleSaid = gThrottleSaid = fallingSaid = ended = false;
        kickStart = -1f; maxQ = maxAlt = maxSpeed = maxG = 0f; warp = 1f; warpBand = -1; engineCut = false;
        if (warpSlider) warpSlider.SetValueWithoutNotify(warp);
        if (graph) graph.Clear(cansat ? goalKm : karmanLineKm);
        mass = TotalMass();
        liftoffTWR = 0f;
        karmanSaid = false; topLitAt = 0f;

        // Exhaust effects on every engine (big clusters share effects to stay fast)
        var perStage = new Dictionary<Grp, int>();
        foreach (var e in engines)
        {
            if (e.dead || partGo[e.part] == null) continue;
            perStage.TryGetValue(e.grp, out var c);
            int clusterSize = e.grp.engines.Count;
            bool show = clusterSize <= 9 || c % Mathf.CeilToInt(clusterSize / 9f) == 0;
            perStage[e.grp] = c + 1;
            if (!show) continue;
            e.t = partGo[e.part];
            e.size = Mathf.Max(0.3f, e.p.diameter) * (clusterSize > 9 ? 1.8f : 1f);
            Vector3 nozzle = e.t.position - e.t.up * (e.p.length * 0.5f);
            e.flame = AscendFX.Flame(e.t, nozzle, e.size, e.solid);
            e.smoke = AscendFX.Smoke(e.t, nozzle, e.size);
            var lg = new GameObject("Engine Glow");
            lg.transform.SetParent(e.t, false);
            lg.transform.position = nozzle - e.t.up * e.size;
            e.glow = lg.AddComponent<Light>();
            e.glow.type = LightType.Point; e.glow.color = new Color(1f, 0.6f, 0.25f); e.glow.range = e.size * 30f; e.glow.intensity = 0f;
            e.glow.shadows = LightShadows.None;
        }
    }

    float TotalMass()
    {
        float m = payloadKg;
        for (int i = 0; i < P.Length; i++) if (attached[i] && P[i] != null) m += P[i].dryMassKg;
        foreach (var g in groups) if (g.attached) foreach (var pool in g.pools) m += fuel[pool];
        return Mathf.Max(1f, m);
    }

    float holdTime = 1.2f;
    bool karmanSaid;
    float topLitAt, karmanAt;

    // The flight continues in orbit right here in the same scene (no scene change)
    bool OrbitSceneOk() => continueInOrbitScene && !cansat;

    // Straight on to the Solar System scene (no fade, no big text): the upper stage + satellite continue in orbit
    void GoToOrbit()
    {
        if (ended) return;
        ended = true;
        phase = Phase.Ending;
        var keep = new List<int>();
        for (int i = 0; i < P.Length; i++)
            if (P[i] != null && attached[i] && (grpOf[i] == null || !grpOf[i].side)) keep.Add(i);
        string kid = $"Your rocket {design.name} reached space and is heading into orbit!";
        string expert = $"Karman line crossed, upper stage burning at T+{FormatTime(tFlight)}, {speed / 1000f:0.00} km/s, max Q {maxQ / 1000f:0.0} kPa, max {maxG:0.0} g.";
        SendReport(true, "REACHED SPACE", kid, expert);
        onReachedSpace?.Invoke();
        var plan = AscendTrajectoryControl.ConfirmedPlan;
        AscendOrbitTransfer.Begin(data, design, info.payload, keep, AscendMissionGuide.Mission,
            plan != null ? plan.targetKm : 700f, SceneManager.GetActiveScene().name, tFlight, maxQ, maxG);
        StartCoroutine(HandOverToOrbit());
    }

    // Fade to black, tidy up the launch, and let the orbit director take over in this scene
    IEnumerator HandOverToOrbit()
    {
        yield return Fade(0f, 1f, 1.4f);
        CleanUp();
        new GameObject("Ascend Orbit").AddComponent<AscendOrbitDirector>();   // shows its own black cover until ready
        yield return null;
        fade.alpha = 0f;
        phase = Phase.Idle;
    }

    void IgniteStage(int k, float at)
    {
        foreach (var e in engines) if (e.grp.stage == k && e.grp.attached) e.igniteTime = at;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  FLIGHT LOOP
    // ═════════════════════════════════════════════════════════════════════════
    void SetWarp(float v, bool fromSlider = false)
    {
        warp = Mathf.Clamp(Mathf.Round(v * 2f) / 2f, MinWarp, MaxWarp);
        if (!fromSlider && warpSlider) warpSlider.SetValueWithoutNotify(warp);
    }
    // separation shots play in real time so you can watch them
    float EffectiveWarp => Time.time < sepShotUntil ? Mathf.Min(1f, warp) : warp;

    // Higher = faster: 1x near the ground, then 2x, 3x, 4x and 5x as the air thins (you can still move the slider)
    void AutoWarp()
    {
        if (!liftedOff || fallingSaid) return;
        int band = 0;
        while (band < BandKm.Length && altitude >= BandKm[band] * 1000f) band++;
        if (band == warpBand) return;
        bool first = warpBand < 0;
        warpBand = band;
        if (!first && band + 1 > warp) { SetWarp(band + 1); AddLog($"Climbing fast: time speed <b>{warp:0}x</b>. <color=#9AA0A6>(slider: bottom left)</color>"); }
    }

    void FlightUpdate()
    {
        if (KeyDown(KeyCode.Minus)) SetWarp(warp - 0.5f);
        if (KeyDown(KeyCode.Equals)) SetWarp(warp + 0.5f);
        AutoWarp();
        if (KeyDown(KeyCode.Alpha1)) SetCam(1);
        if (KeyDown(KeyCode.Alpha2)) SetCam(2);
        if (KeyDown(KeyCode.Alpha3)) SetCam(3);
        if (KeyDown(KeyCode.C)) SetCam(camMode % 3 + 1);
        if (KeyDown(KeyCode.H)) ToggleAllWindows();

        float sim = Mathf.Min(Time.deltaTime, 0.1f) * EffectiveWarp;
        while (sim > 0f && phase == Phase.Flight)
        {
            float dt = Mathf.Min(StepDt, sim);
            Step(dt);
            sim -= dt;
        }
        if (phase != Phase.Flight) return;
        PlaceRocket();
        UpdateEffects();
        UpdateSky();
        UpdateHud();
    }

    void Step(float dt)
    {
        tFlight += dt;
        double r = Math.Sqrt(px * px + py * py);
        double h = r - EarthR;
        double ux = px / r, uy = py / r;          // local up
        double ex = uy, ey = -ux;                 // local east (downrange)
        vUp = (float)(vx * ux + vy * uy);
        float vE = (float)(vx * ex + vy * ey);
        speed = (float)Math.Sqrt(vx * vx + vy * vy);
        altitude = (float)Math.Max(0.0, h);

        // Atmosphere
        float pr = Mathf.Exp(-altitude / 8000f);
        float rho = 1.225f * Mathf.Exp(-altitude / 8500f);
        float sound = Mathf.Sqrt(1.4f * 287f * AirTemp(altitude));
        mach = speed / sound;
        q = 0.5f * rho * speed * speed;

        // Throttle: ease off for Max Q and high g (liquid engines only, like real rockets)
        float qLimit = QLimit;
        // (same autopilot as the Ascend Mission Simulator, so a GO there means a good flight here)
        throttle = Mathf.Min(1f, throttle + 0.5f * dt);
        if (guided)
        {
            float fMax = 0f;
            foreach (var e in engines) if (!e.solid && EngineCanRun(e)) fMax += Mix(e.p.thrustVackN * 1000f, e.p.thrustSLkN * 1000f, pr);
            if (fMax > 0f)
            {
                float gCap = Mathf.Min(4.5f, GLimit * 0.7f);
                float gThr = Mathf.Clamp(gCap * G0 * TotalMass() / fMax, 0.3f, 1f);
                if (gThr < throttle) { throttle = gThr; if (!gThrottleSaid && throttle < 0.95f) { gThrottleSaid = true; AddLog("Engines easing off to keep the g-force safe. <color=#9AA0A6>(g-limit throttle)</color>"); } }
                if (q > qLimit * 0.55f)
                {
                    float qThr = Mathf.Clamp(1f - (q - qLimit * 0.55f) / (qLimit * 0.35f), 0.3f, 1f);
                    if (qThr < throttle) { throttle = qThr; if (!throttleSaid && throttle < 0.95f) { throttleSaid = true; AddLog("Easing the engines for Max Q. <color=#9AA0A6>(throttle down)</color>"); } }
                }
            }
        }

        // Thrust: each engine asks its tank for fuel; tanks share between engines
        need.Clear();
        foreach (var e in engines)
        {
            e.lit = EngineCanRun(e);
            if (!e.lit) { e.thrustN = 0f; e.mdot = 0f; continue; }
            e.thrustN = EngineThrust(e, pr, out e.mdot);
            need.TryGetValue(e.pool, out var nd); need[e.pool] = nd + e.mdot * dt;
        }
        float F = 0f;
        foreach (var e in engines)
        {
            if (!e.lit) continue;
            float scale = 1f;
            if (need.TryGetValue(e.pool, out var nd) && nd > 0f) scale = Mathf.Clamp01(fuel[e.pool] / nd);
            F += e.thrustN * scale;
            fuel[e.pool] = Mathf.Max(0f, fuel[e.pool] - e.mdot * dt * scale);
            if (fuel[e.pool] < 0.05f) fuel[e.pool] = 0f;   // a few grams left = empty (rounding can leave crumbs that never reach zero)
        }
        mass = TotalMass();
        thrustNow = F;

        // Guidance: straight up, small pitch kick, then follow the flight path (gravity turn)
        float flightPathDeg = speed > 20f ? Mathf.Atan2(vE, vUp) * Mathf.Rad2Deg : 0f;
        float want = tilt;
        if (cansat && (guided || passive)) want = speed > 20f ? Mathf.Clamp(flightPathDeg, 0f, 90f) : railDeg;   // launch rail tilt, then the nose follows the airflow
        else if (!guided)
        {
            if (passive) want = 0f;
            else if (liftedOff && altitude > 50f) { tumbleRate = Mathf.Min(25f, tumbleRate + 3f * dt); want = tilt + tumbleRate * dt; }
        }
        else if (!liftedOff || speed < 60f) want = 0f;
        else
        {
            // Gravity turn: pitch kick, then follow the airflow, leaning over only as fast as the speed allows
            if (kickStart < 0f) kickStart = tFlight;
            float kick = AscendTrajectoryControl.ConfirmedPlan != null ? AscendTrajectoryControl.ConfirmedPlan.kickDeg : 3f;
            if (tFlight - kickStart < 8f) want = kick * Mathf.Clamp01((tFlight - kickStart) / 4f);
            else if (altitude < 45000f) want = Mathf.Max(0f, Mathf.Min(flightPathDeg, kick + 80f * Mathf.Clamp01((speed - 60f) / 1600f)));
            else want = Mathf.Clamp(flightPathDeg, 0f, 88f);
        }
        bool kicking = guided && !cansat && kickStart >= 0f && tFlight - kickStart < 8f;
        tilt = guided ? Mathf.MoveTowards(tilt, want, (kicking ? 4f : 15f) * dt) : want;
        float tr = tilt * Mathf.Deg2Rad;
        double tx = ux * Math.Cos(tr) + ex * Math.Sin(tr), ty = uy * Math.Cos(tr) + ey * Math.Sin(tr);

        // Forces
        double g = Mu / (r * r);
        double ax = tx * F / mass - ux * g, ay = ty * F / mass - uy * g;
        float area = DragArea();
        float drag = q * Cd(mach) * area;
        if (speed > 0.1f) { ax -= drag * vx / speed / mass; ay -= drag * vy / speed / mass; }
        gNow = (F + drag) / mass / G0;

        // Hold-down clamps until the engines are at full thrust and can lift the rocket
        if (!liftedOff)
        {
            float upAcc = F / mass - (float)g;
            if (tFlight >= holdTime && upAcc > 0f)
            {
                liftedOff = true;
                liftoffTWR = F / (mass * G0);
                ShowBig("LIFTOFF!", 2f);
                AddLog($"LIFTOFF! <color=#9AA0A6>(thrust-to-weight {liftoffTWR:0.00})</color>");
            }
            else
            {
                vx = vy = 0;
                if (tFlight > 6f)
                {
                    float twr = F / (mass * G0);
                    Fail("TOO HEAVY!",
                        "The engines roared, but the rocket was too heavy to leave the pad.",
                        $"Liftoff thrust-to-weight {twr:0.00} (needs more than 1.0, ideally 1.2-1.5). Add engines/boosters or make it lighter.", false);
                }
                return;
            }
        }

        vx += ax * dt; vy += ay * dt;
        px += vx * dt; py += vy * dt;

        // Records
        maxQ = Mathf.Max(maxQ, q); maxAlt = Mathf.Max(maxAlt, altitude); maxSpeed = Mathf.Max(maxSpeed, speed); maxG = Mathf.Max(maxG, gNow);

        // Fun events
        if (!machSaid && mach >= 1f) { machSaid = true; AddLog("Faster than sound! <color=#9AA0A6>(Mach 1)</color>"); }
        if (!maxQSaid && maxQ > 3000f && q < maxQ * 0.97f) { maxQSaid = true; ShowBig("MAX Q", 1.5f); AddLog($"Max Q: the air pushed hardest here. <color=#9AA0A6>({maxQ / 1000f:0.0} kPa at {altitude / 1000f:0.0} km)</color>"); }

        // Failures
        float aoa = speed > 30f ? Mathf.Abs(Mathf.DeltaAngle(tilt, flightPathDeg)) : 0f;
        if (!guided && q > qLimit)
        { Fail("RIPPED APART!", "The air pushed too hard and the rocket broke into pieces.", $"Dynamic pressure {q / 1000f:0.0} kPa exceeded the {qLimit / 1000f:0} kPa structural limit" + (aeroNose ? "." : " (blunt nose: add a nose cone or fairing).") + " Climb more steeply or throttle down through Max Q.", true); return; }
        if (!guided && q * Mathf.Sin(aoa * Mathf.Deg2Rad) > 6000f)
        { Fail("TURNED SIDEWAYS!", "The rocket couldn't steer, turned sideways and the air ripped it apart.", $"Angle of attack {aoa:0}° at {q / 1000f:0.0} kPa. A rocket needs a flight computer AND an IMU to steer.", true); return; }
        if (!guided && gNow > GLimit)
        { Fail("CRUSHED!", "The rocket sped up so hard that it was crushed by its own push.", $"Axial load {gNow:0.0} g exceeded {GLimit:0} g. Use throttleable liquid engines or a heavier upper stage.", true); return; }

        // Staging
        if (liftedOff) Staging();

        // Fairing
        // (when the flight continues in orbit, the nose cover stays on and opens there, just before the satellite is released)
        if (!fairingGone && !OrbitSceneOk() && altitude >= fairingJettisonKm * 1000f && fairingParts.Count > 0 && (curStage > 0 || stageCount == 1))
        {
            fairingGone = true;
            StartSepShot(fairingParts, 5f);
            foreach (int i in fairingParts) Detach(i, 4f);
            RecomputeFocus();
            ShowBig("FAIRING SEP", 1.5f);
            AddLog("Fairing open! The nose cover falls away. <color=#9AA0A6>(fairing jettison)</color>");
        }

        // Sky Sensor: cut the engine for the target height, release the CanSat at the top
        if (cansat && liftedOff)
        {
            float ballistic = altitude + Mathf.Max(0f, vUp) * Mathf.Max(0f, vUp) / (2f * 9.81f);
            if (!engineCut && ballistic >= goalKm * 1000f && engines.Any(e => e.lit && !e.solid))
            { engineCut = true; AddLog("Engine cut-off: coasting up to the target height."); }
            if (vUp < 0f && altitude > 20f && maxAlt >= goalKm * 600f) { CanSatSuccess(); return; }
        }

        // Out of fuel before space
        if (!fallingSaid && vUp < 0f && !AnyFuelLeft())
        {
            fallingSaid = true;
            SetWarp(Mathf.Max(warp, 3f));
            ShowBig("OUT OF FUEL", 2f);
            AddLog($"Out of fuel! Highest point {maxAlt / 1000f:0.0} km. Falling back to Earth...");
        }

        // Ground impact
        if (liftedOff && h <= 0.0 && vUp < 0f)
        {
            bool reachedHigh = maxAlt > 2000f;
            Fail("CRASHED!", reachedHigh ? $"The rocket ran out of fuel at {maxAlt / 1000f:0.0} km, fell back and crashed." : "The rocket fell back to the ground and crashed.",
                $"Apogee {maxAlt / 1000f:0.0} km of {goalKm:0.#} km. More delta-v needed: more propellant, more efficient engines (higher Isp), or less mass.", true);
            return;
        }

        // SPACE!
        if (!cansat && altitude >= karmanLineKm * 1000f)
        {
            if (!karmanSaid) { karmanSaid = true; karmanAt = tFlight; AddLog($"Crossed the Karman line ({karmanLineKm:0} km): you're in space!"); }
            if (!OrbitSceneOk()) { ReachSpace(); return; }
            // Keep flying until the first stage has dropped and the second stage is burning,
            // then continue in orbit (Solar System scene) without a break
            bool topStage = curStage >= stageCount - 1 && !pendingIgnite;
            bool ready = topStage && tFlight - topLitAt > 8f && Time.time > sepShotUntil && Time.time > igniteShotUntil;
            if (ready || altitude > 600000f || tFlight - karmanAt > 120f || (!AnyFuelLeft() && vUp < 0f)) { GoToOrbit(); return; }
        }
    }

    // Small rockets are much stronger for their size than big ones (Electron-size = 1x)
    float SizeK => Mathf.Clamp(1.2f / Mathf.Max(0.05f, coreDia), 1f, 20f);
    float QLimit => (aeroNose ? structuralLimitKPa : bluntNoseLimitKPa) * 1000f * SizeK;
    float GLimit => maxGForce * Mathf.Clamp(SizeK, 1f, 4f);

    bool EngineCanRun(Eng e)
    {
        if (engineCut && !e.solid) return false;
        if (e.dead || !e.grp.attached || e.grp.stage != curStage || tFlight < e.igniteTime) return false;
        return fuel.TryGetValue(e.pool, out var f) && f > 0f;
    }

    float EngineThrust(Eng e, float pr, out float mdot)
    {
        var p = e.p;
        if (e.solid)
        {
            float burn = p.burnTimeS > 0f ? p.burnTimeS : 60f;
            mdot = p.propellantKg / burn;
            float fv = p.thrustVackN > 0f ? p.thrustVackN * 1000f : mdot * Mathf.Max(1f, p.ispVac) * G0;
            float fs = p.thrustSLkN > 0f ? p.thrustSLkN * 1000f : (p.ispSL > 0f ? mdot * p.ispSL * G0 : 0f);
            return Mix(fv, fs, pr);
        }
        float ramp = Mathf.Clamp01((tFlight - e.igniteTime) / 1f);
        float fvac = p.thrustVackN * 1000f;
        mdot = fvac / (Mathf.Max(1f, p.ispVac) * G0) * throttle * ramp;
        return Mix(fvac, p.thrustSLkN * 1000f, pr) * throttle * ramp;
    }

    // Thrust goes from the sea-level value to the vacuum value as the air thins.
    // Vacuum-only engines lose most of their thrust in thick air (flow separation).
    static float Mix(float fv, float fs, float pr) => fs > 0f ? Mathf.Lerp(fv, fs, pr) : fv * Mathf.Clamp01(1f - 2.5f * pr);

    static float AirTemp(float h)
    {
        if (h < 11000f) return 288.15f - 0.0065f * h;
        if (h < 20000f) return 216.65f;
        if (h < 47000f) return 216.65f + 0.0018f * (h - 20000f);
        return 265f;
    }

    // Drag coefficient vs Mach: low subsonic, peak in the transonic zone, lower when supersonic
    float Cd(float m)
    {
        float cd = m < 0.8f ? 0.30f : m < 1.2f ? Mathf.Lerp(0.30f, 0.55f, (m - 0.8f) / 0.4f) : Mathf.Lerp(0.55f, 0.25f, Mathf.Clamp01((m - 1.2f) / 3.8f));
        return aeroNose ? cd : cd * 1.8f;
    }

    float DragArea()
    {
        float a = Mathf.PI * 0.25f * coreDia * coreDia;
        foreach (var g in groups) if (g.side && g.attached) a += Mathf.PI * 0.25f * g.dia * g.dia;
        return a;
    }

    bool AnyFuelLeft()
    {
        foreach (var e in engines)
            if (!e.dead && e.grp.attached && e.grp.stage >= curStage && fuel.TryGetValue(e.pool, out var f) && f > 0f) return true;
        return false;
    }

    void Staging()
    {
        // Side boosters drop when empty
        foreach (var g in groups)
        {
            if (!g.side || !g.attached || g.stage != curStage || g.engines.Count == 0) continue;
            bool empty = g.pools.All(pool => fuel[pool] <= 0f);
            if (!empty) continue;
            StartSepShot(g.parts, 5f);
            DetachGroup(g, 6f);
            AddLog("Boosters empty! Dropping them to save weight. <color=#9AA0A6>(booster separation)</color>");
            ShowBig("BOOSTER SEP", 1.5f);
        }

        bool burning = engines.Any(e => e.grp.attached && e.grp.stage == curStage && !e.dead && fuel.TryGetValue(e.pool, out var f) && f > 0f);
        if (!burning && !stageBurnout && !pendingIgnite)
        {
            stageBurnout = true; burnoutTime = tFlight;
            if (curStage < stageCount - 1) AddLog($"Stage {curStage + 1} out of fuel. <color=#9AA0A6>(MECO, main engine cut-off)</color>");
        }
        if (stageBurnout && curStage < stageCount - 1 && tFlight - burnoutTime > 1f)
        {
            var dropping = new List<int>();
            foreach (var g in groups) if (g.attached && g.stage == curStage) dropping.AddRange(g.parts.Where(i => !fairingParts.Contains(i)));
            StartSepShot(dropping, 7f);
            foreach (var g in groups) if (g.attached && g.stage == curStage) DetachGroup(g, 3f);
            curStage++;
            stageBurnout = false; pendingIgnite = true; igniteAt = tFlight + 1.2f;
            ShowBig("STAGE SEP", 1.5f);
            AddLog("Stage separation! The empty stage falls away.");
        }
        if (pendingIgnite && tFlight >= igniteAt)
        {
            pendingIgnite = false;
            IgniteStage(curStage, tFlight);
            topLitAt = tFlight;
            // close-up of the upper stage lighting its engine, seen from below and the side
            float len = Mathf.Max(4f, focusTop - focusBottom);
            camDist = Mathf.Clamp(len * 2.6f, 14f, rocketHeight * 1.8f);
            camPitch = -14f;
            sepShotUntil = Time.time;
            igniteShotUntil = Time.time + 6f;
            ShowBig("STAGE " + (curStage + 1) + " IGNITION", 1.8f);
            if (engines.Any(e => e.grp.stage == curStage && !e.dead))
                AddLog($"Stage {curStage + 1} engine on! <color=#9AA0A6>(second-stage ignition)</color>");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  WORLD: move the rocket, drop parts, effects, sky
    // ═════════════════════════════════════════════════════════════════════════
    void PlaceRocket()
    {
        double theta = Math.Atan2(px, py);
        float down = (float)(EarthR * theta);
        root.position = basePos + downDir * down + Vector3.up * altitude;
        Vector3 axis = Vector3.up * Mathf.Cos(tilt * Mathf.Deg2Rad) + downDir * Mathf.Sin(tilt * Mathf.Deg2Rad);
        root.rotation = Quaternion.FromToRotation(Vector3.up, axis);
    }

    Vector3 WorldVelocity()
    {
        double r = Math.Sqrt(px * px + py * py);
        double ux = px / r, uy = py / r, ex = uy, ey = -ux;
        return Vector3.up * (float)(vx * ux + vy * uy) + downDir * (float)(vx * ex + vy * ey);
    }

    // ── Separation shot: a puff of gas and a camera that shows the part falling away ──
    Transform sepTarget;
    float sepShotUntil, igniteShotUntil;
    Vector3 sepOff; bool sepCamInit;

    void StartSepShot(IEnumerable<int> parts, float seconds)
    {
        Bounds b = default; bool any = false; Transform first = null;
        foreach (int i in parts)
        {
            if (i < 0 || i >= partGo.Length || !attached[i] || !partGo[i]) continue;
            if (!first) first = partGo[i];
            foreach (var r in partGo[i].GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        }
        if (!first) return;
        // the gas puff comes out where the parts join (top of the part that falls away)
        Vector3 joint = any ? b.center + root.up * Vector3.Dot(b.extents, new Vector3(Mathf.Abs(root.up.x), Mathf.Abs(root.up.y), Mathf.Abs(root.up.z))) : first.position;
        AscendFX.Puff(joint, Mathf.Max(1f, coreDia), root.up, WorldVelocity());
        sepTarget = first;
        sepShotUntil = Time.time + seconds;
        sepCamInit = false;
    }

    void DetachGroup(Grp g, float push)
    {
        if (!g.attached) return;
        g.attached = false;
        foreach (int i in g.parts) if (!fairingParts.Contains(i) || g.side) Detach(i, push);
        foreach (var e in g.engines) StopEffects(e);
        RecomputeFocus();
    }

    // The cameras look at what is still flying (after a stage drops, that's the upper stage)
    float focusMid, focusTop, focusBottom;
    void RecomputeFocus()
    {
        if (!root) return;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < partGo.Length; i++)
        {
            if (!attached[i] || !partGo[i]) continue;
            foreach (var r in partGo[i].GetComponentsInChildren<Renderer>())
            {
                float y0 = root.InverseTransformPoint(r.bounds.center).y, e = r.bounds.extents.magnitude * 0.6f;
                lo = Mathf.Min(lo, y0 - e); hi = Mathf.Max(hi, y0 + e);
            }
        }
        if (lo > hi) return;
        focusBottom = lo; focusTop = hi; focusMid = (lo + hi) * 0.5f;
    }

    void Detach(int i, float push)
    {
        attached[i] = false;
        var t = partGo[i];
        if (!t) return;
        Vector3 center = root.position + root.up * rocketHeight * 0.5f;
        Vector3 outward = Vector3.ProjectOnPlane(t.position - center, root.up);
        Vector3 kick = outward.sqrMagnitude > 0.01f ? outward.normalized * push : -root.up * push * 0.5f;
        MakeDebris(t, WorldVelocity() + kick);
    }

    void MakeDebris(Transform t, Vector3 vel)
    {
        t.SetParent(null, true);
        var d = t.gameObject.AddComponent<AscendDebris>();
        d.velocity = vel;
        d.spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(4f, 18f);
        d.groundY = basePos.y - 1f;
        debris.Add(t.gameObject);
    }

    void StopEffects(Eng e)
    {
        if (e.flame) { var em = e.flame.emission; em.rateOverTime = 0f; }
        if (e.smoke) { var em = e.smoke.emission; em.rateOverTime = 0f; }
        if (e.glow) e.glow.intensity = 0f;
    }

    void UpdateEffects()
    {
        float pr = Mathf.Exp(-altitude / 8000f);
        foreach (var e in engines)
        {
            if (!e.flame) continue;
            bool on = e.lit && e.grp.attached;
            float power = on ? Mathf.Clamp01(e.thrustN / Mathf.Max(1f, e.p.thrustVackN * 1000f)) : 0f;
            var em = e.flame.emission; em.rateOverTime = on ? 160f * Mathf.Max(0.3f, power) : 0f;
            var sh = e.flame.shape; sh.angle = Mathf.Lerp(30f, 6f, pr);          // plume spreads out in thin air
            var main = e.flame.main; main.startSize = e.size * Mathf.Lerp(3.2f, 1.3f, pr);
            if (e.smoke) { var sem = e.smoke.emission; sem.rateOverTime = on ? 45f * pr : 0f; }
            if (e.glow) e.glow.intensity = on ? (3f + UnityEngine.Random.Range(-0.6f, 0.6f)) * Mathf.Max(0.3f, power) : 0f;
        }
        if (liftedOff && altitude < 3000f) shake = Mathf.Max(shake, 0.15f * (1f - altitude / 3000f));
    }

    void SetupSky()
    {
        if (AscendSky.Active) return;   // the AscendSky controller handles the sky (clouds, sun, fade to space)
        skyOriginal = RenderSettings.skybox;
        if (!skyOriginal) return;
        skyCopy = new Material(skyOriginal);   // edit a copy so the real skybox asset never changes
        RenderSettings.skybox = skyCopy;
        if (skyCopy.HasProperty("_Exposure")) skyExposure = skyCopy.GetFloat("_Exposure");
        if (skyCopy.HasProperty("_AtmosphereThickness")) skyAtmo = skyCopy.GetFloat("_AtmosphereThickness");
    }

    void UpdateSky()
    {
        if (AscendSky.Active)
        {
            float camAlt = flightCam ? flightCam.transform.position.y - groundHeight : altitude;
            AscendSky.SetAltitude(Mathf.Max(altitude, camAlt));
            return;
        }
        if (!skyCopy) return;
        float k = Mathf.Clamp01(altitude / 70000f);   // the sky turns black as the air runs out
        if (skyCopy.HasProperty("_Exposure")) skyCopy.SetFloat("_Exposure", Mathf.Lerp(skyExposure, skyExposure * 0.08f, k));
        if (skyCopy.HasProperty("_AtmosphereThickness")) skyCopy.SetFloat("_AtmosphereThickness", Mathf.Lerp(skyAtmo, 0.05f, k));
        if (flightCam && flightCam.clearFlags == CameraClearFlags.SolidColor)
            flightCam.backgroundColor = Color.Lerp(new Color(0.45f, 0.65f, 0.9f), Color.black, k);
    }

    void RestoreSky()
    {
        AscendSky.ResetAltitude();
        if (skyOriginal) RenderSettings.skybox = skyOriginal;
        if (skyCopy) Destroy(skyCopy);
        skyCopy = null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CAMERA
    // ═════════════════════════════════════════════════════════════════════════
    void SetupCamera()
    {
        playerCam = Camera.main;
        var go = new GameObject("Ascend Launch Camera");
        flightCam = go.AddComponent<Camera>();
        if (playerCam)
        {
            flightCam.CopyFrom(playerCam);
            flightCam.depth = playerCam.depth + 5f;
            playerCam.enabled = false;
        }
        flightCam.nearClipPlane = 0.5f;
        flightCam.farClipPlane = 3000000f;   // see the whole Philippines and the horizon from high up
        flightCam.fieldOfView = 60f;
        camDist = Mathf.Max(35f, rocketHeight * 1.8f);
        camYaw = Quaternion.LookRotation(-Vector3.Cross(downDir, Vector3.up)).eulerAngles.y;
        camPitch = 6f;
        camMode = 1;
    }

    void UpdateCamera()
    {
        if (!flightCam || !root) return;
        Vector3 target = root.position + root.up * focusMid;

        if (phase == Phase.Flight || phase == Phase.Countdown)
        {
            Vector2 md = RightMouseHeld() ? MouseDelta() : Vector2.zero;
            camYaw += md.x * 3f; camPitch = Mathf.Clamp(camPitch - md.y * 3f, -60f, 80f);
            camDist = Mathf.Clamp(camDist * (1f - Scroll() * 0.1f), Mathf.Max(6f, (focusTop - focusBottom) * 0.8f), rocketHeight * 20f + 200f);
        }

        if (camMode == 1 && sepTarget && Time.time < sepShotUntil)
        {
            // Separation shot: from the side, frame the rocket AND the part that just came off
            Vector3 dropC = sepTarget.position;
            var rs = sepTarget.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var rr in rs) bb.Encapsulate(rr.bounds); dropC = bb.center; }
            Vector3 mid = (target + dropC) * 0.5f;
            float gap = Vector3.Distance(target, dropC);
            Vector3 side = Vector3.Cross(downDir, Vector3.up).normalized;
            float len = Mathf.Max(4f, focusTop - focusBottom);
            float dist = Mathf.Clamp(gap * 1.3f + len * 2f, 14f, rocketHeight * 6f + 40f);
            if (gap > rocketHeight * 5f + 60f) sepShotUntil = Time.time;   // too far apart now: back to the chase camera
            // smooth the camera RELATIVE to the rocket: at kilometres per second a world-space lag would leave it far behind
            Vector3 wantOff = side * dist + Vector3.up * dist * 0.15f - root.up * dist * 0.1f;
            if (!sepCamInit) { sepOff = flightCam.transform.position - mid; sepCamInit = true; }
            sepOff = Vector3.Lerp(sepOff, wantOff, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            flightCam.transform.position = mid + sepOff;
            flightCam.transform.rotation = Quaternion.Slerp(flightCam.transform.rotation, Quaternion.LookRotation(mid - flightCam.transform.position, Vector3.up), 1f - Mathf.Exp(-Time.deltaTime * 4f));
            flightCam.fieldOfView = 60f;
        }
        else if (camMode == 2)
        {
            // Nose camera: on the side of the nose, looking down the rocket at the engines and the Earth
            Vector3 side = Vector3.Cross(downDir, Vector3.up).normalized;
            Vector3 sideR = Vector3.ProjectOnPlane(side, root.up).normalized;
            if (sideR.sqrMagnitude < 0.01f) sideR = root.right;
            float r = Mathf.Max(0.3f, coreDia * 0.5f);
            flightCam.transform.position = root.position + root.up * (focusTop - 0.08f * (focusTop - focusBottom)) + sideR * (r + 0.35f);
            flightCam.transform.rotation = Quaternion.LookRotation(-root.up + sideR * 0.12f, sideR);
            flightCam.fieldOfView = 75f;
        }
        else if (camMode == 3)
        {
            Vector3 side = Vector3.Cross(downDir, Vector3.up);
            Vector3 pos = basePos - downDir * 450f + side * 150f + Vector3.up * 3f;
            flightCam.transform.position = pos;
            flightCam.transform.LookAt(target);
            float dist = Vector3.Distance(pos, target);
            flightCam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(rocketHeight * 1.5f / dist) * Mathf.Rad2Deg, 0.3f, 60f);
        }
        else
        {
            flightCam.fieldOfView = 60f;
            if (Time.time < igniteShotUntil) target = root.position + root.up * Mathf.Lerp(focusBottom, focusMid, 0.4f);
            Vector3 pos = target + Quaternion.Euler(camPitch, camYaw, 0f) * new Vector3(0f, 0f, -camDist);
            if (pos.y < basePos.y + 1f) pos.y = basePos.y + 1f;
            flightCam.transform.position = pos;
            flightCam.transform.LookAt(target);
        }

        float camH = Mathf.Max(0f, flightCam.transform.position.y - groundHeight);
        // Near clip: bigger up high (keeps the far ground sharp), but never closer than a quarter of the
        // distance to the rocket, or a small custom rocket gets cut out of the picture
        float toRocket = Vector3.Distance(flightCam.transform.position, target);
        float closest = Mathf.Max(0.05f, toRocket - rocketHeight * 0.75f);
        flightCam.nearClipPlane = Mathf.Clamp(Mathf.Min(camDist * 0.01f + camH * 0.0005f, closest * 0.25f), camMode == 2 ? 0.05f : 0.1f, 60f);
        if (earthMap)
        {
            var mp = earthMap.transform.position;
            mp.y = groundHeight - Mathf.Max(0.4f, camH * 0.02f);
            earthMap.transform.position = mp;
        }
        // The flat cloud layer takes over from the sky clouds as you climb through them
        if (cloudLayer) cloudLayer.SetActive(AscendSky.Active ? camH > (AscendSky.Active.cloudBase + AscendSky.Active.cloudTop) * 0.5f : camH > 700f);

        if (shake > 0f)
        {
            flightCam.transform.position += UnityEngine.Random.insideUnitSphere * shake;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 0.6f);
        }
    }

    void SetCam(int m)
    {
        camMode = Mathf.Clamp(m, 1, 3);
        if (phase == Phase.Flight || phase == Phase.Countdown)
            ShowBig(camMode == 1 ? "<size=45%>CAMERA 1: CHASE</size>" : camMode == 2 ? "<size=45%>CAMERA 2: NOSE</size>" : "<size=45%>CAMERA 3: GROUND TRACKING</size>", 1.2f);
    }

    // Point the Mission Control screen's camera (CameraScreen "Track Target") at the rocket
    void PointMonitorsAt(Transform t)
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!mb || mb.GetType().Name != "CameraScreen") continue;
            var ty = mb.GetType();
            foreach (var n in new[] { "trackTarget", "TrackTarget", "target" })
            {
                var f = ty.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(Transform)) { f.SetValue(mb, t); break; }
                var pr = ty.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (pr != null && pr.CanWrite && pr.PropertyType == typeof(Transform)) { pr.SetValue(mb, t); break; }
            }
        }
    }

    void TakeOverPlayer(bool flying)
    {
        MissionBriefing.SetUiVisible(!flying);
        if (flying) { savedLock = Cursor.lockState; savedVisible = Cursor.visible; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        else { Cursor.lockState = savedLock; Cursor.visible = savedVisible; }
        if (disableWhileFlying != null) foreach (var b in disableWhileFlying) if (b) b.enabled = !flying;
        if (hideWhileFlying != null) foreach (var g in hideWhileFlying) if (g) g.SetActive(!flying);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ENDINGS
    // ═════════════════════════════════════════════════════════════════════════
    void Fail(string title, string kid, string expert, bool explode)
    {
        if (ended) return;
        ended = true;
        phase = Phase.Ending;
        foreach (var e in engines) StopEffects(e);
        if (explode && root)
        {
            Vector3 c = root.position + root.up * rocketHeight * 0.5f;
            AscendFX.Boom(c, Mathf.Clamp(rocketHeight / 15f, 1f, 6f));
            shake = 1.2f;
            Vector3 v = WorldVelocity();
            for (int i = 0; i < partGo.Length; i++)
                if (attached[i] && partGo[i]) { attached[i] = false; MakeDebris(partGo[i], v + UnityEngine.Random.insideUnitSphere * 25f); }
            if (payloadGo) MakeDebris(payloadGo, v + UnityEngine.Random.insideUnitSphere * 15f);
        }
        AddLog("<color=#F85149>" + title + "</color> " + kid);
        SendReport(false, title, kid, expert);
        onFlightFailed?.Invoke();
        StartCoroutine(EndRoutine(false, title, kid, expert));
    }

    void CanSatSuccess()
    {
        if (ended) return;
        ended = true;
        phase = Phase.Ending;
        successBig = "CANSAT RELEASED!";
        AddLog("CanSat released! The parachute opens.");
        string kid = $"Your CanSat was released at {maxAlt / 1000f:0.00} km and is floating down on its parachute, measuring the air all the way!";
        string expert = $"Apogee {maxAlt:N0} m at T+{FormatTime(tFlight)}, max Q {maxQ / 1000f:0.0} kPa, max {maxG:0.0} g. Descent ~8 m/s, about {maxAlt / 8f / 60f:0.#} min to land.";
        SendReport(true, "CANSAT RELEASED", kid, expert);
        onReachedSpace?.Invoke();
        StartCoroutine(EndRoutine(true, "MISSION SUCCESS!", kid, expert));
    }

    void ReachSpace()
    {
        if (ended) return;
        successBig = "100 KM - SPACE!";
        ended = true;
        phase = Phase.Ending;
        AddLog("WELCOME TO SPACE! You crossed the Karman line.");
        string kid = $"Your rocket {design.name} flew past {karmanLineKm:0} km, where space begins!";
        string expert = $"Karman line at T+{FormatTime(tFlight)}, {speed / 1000f:0.00} km/s, max Q {maxQ / 1000f:0.0} kPa, max {maxG:0.0} g, stage {curStage + 1} of {stageCount}.";
        SendReport(true, "REACHED SPACE", kid, expert);
        onReachedSpace?.Invoke();
        StartCoroutine(EndRoutine(true, "WELCOME TO SPACE!", kid, expert));
    }

    IEnumerator EndRoutine(bool success, string title, string kid, string expert)
    {
        if (success)
        {
            ShowBig(successBig, 2f);
            yield return Fade(0f, 1f, 1f);

            // Earth missions: the rocket keeps flying in the Solar System scene
            if (!cansat && continueInOrbitScene && !string.IsNullOrEmpty(orbitSceneName) && Application.CanStreamedLevelBeLoaded(orbitSceneName))
            {
                var keep = new List<int>();
                int top = stageCount - 1;
                for (int i = 0; i < P.Length; i++)
                    if (P[i] != null && stageOf[i] == top && !fairingParts.Contains(i) && (grpOf[i] == null || !grpOf[i].side)) keep.Add(i);
                var plan = AscendTrajectoryControl.ConfirmedPlan;
                AscendOrbitTransfer.Begin(data, design, info.payload, keep, AscendMissionGuide.Mission,
                    plan != null ? plan.targetKm : 700f, SceneManager.GetActiveScene().name, tFlight, maxQ, maxG);
                StartCoroutine(HandOverToOrbit());
                yield break;
            }
            if (!cansat && continueInOrbitScene)
                Debug.LogWarning($"Ascend Flight: add the '{orbitSceneName}' scene to File > Build Profiles (Scene List) so the flight can continue in orbit.");
            if (root) Destroy(root.gameObject);
        }
        else
        {
            // Watch the result for a moment
            float t = 0f;
            while (t < 4f)
            {
                t += Time.deltaTime;
                UpdateSky();
                yield return null;
            }
        }

        endTitle.text = title;
        endTitle.color = success ? new Color(0.25f, 0.9f, 0.4f) : new Color(1f, 0.35f, 0.3f);
        string fix = success ? "" : AscendMissionSim.FixFor(title);
        endBody.text = kid + "\n\n<size=80%><color=#9AA0A6>Engineer's note: " + expert + "</color></size>\n\n" +
                       (string.IsNullOrEmpty(fix) ? "" : "<size=85%><b><color=#FFC850>How to prevent it next time</color></b>\n" + fix + "</size>\n\n") +
                       $"<size=85%>Highest point <b>{maxAlt / 1000f:0.0} km</b>   Top speed <b>{maxSpeed * 3.6f:N0} km/h</b>   Max Q <b>{maxQ / 1000f:0.0} kPa</b>   Max <b>{maxG:0.0} g</b></size>\n\n" +
                       "<size=70%><color=#9AA0A6>A flight report was sent to PhMail.</color></size>\n\n" +
                       "<color=#FFC850>" + KeyName(launchKey) + " to continue</color>";
        endCard.SetActive(true);
        while (!KeyDown(launchKey)) yield return null;
        yield return null;

        endCard.SetActive(false);
        if (!success) yield return Fade(0f, 1f, 0.6f);
        CleanUp();
        yield return Fade(1f, 0f, 0.6f);
        phase = Phase.Idle;
    }

    void CleanUp()
    {
        hudRoot.SetActive(false);
        bigText.text = "";
        foreach (var d in debris) if (d) Destroy(d);
        debris.Clear();
        if (root) Destroy(root.gameObject);
        root = null;
        if (flightCam) Destroy(flightCam.gameObject);
        if (playerCam) playerCam.enabled = true;
        if (earthMap) Destroy(earthMap);
        if (cloudLayer) Destroy(cloudLayer);
        earthMap = cloudLayer = null;
        RestoreSky();
        TakeOverPlayer(false);
        foreach (var row in fuelRows) if (row.fill) Destroy(row.fill.parent.parent.gameObject);
        fuelRows.Clear();
    }

    void SendReport(bool success, string title, string kid, string expert)
    {
        if (data == null) return;
        var sb = new StringBuilder();
        string mission = MissionBriefing.SelectedMission != null ? MissionBriefing.SelectedMission.title : "Test flight";
        sb.Append("Hello Design Team,\n\nHere is the flight report for <b>").Append(design.name).Append("</b>.\n\n");
        sb.Append("<b>Mission:</b> ").Append(mission).Append('\n');
        sb.Append("<b>Result:</b> ").Append(success ? "<color=#3FB950>SUCCESS - reached space</color>" : "<color=#F85149>" + title + "</color>").Append("\n\n");
        sb.Append(kid).Append("\n\n");
        sb.Append("<b>FLIGHT DATA</b>\n");
        sb.Append($"- Liftoff thrust-to-weight: {liftoffTWR:0.00}\n");
        sb.Append(cansat ? $"- Highest point: {maxAlt:N0} m (target {goalKm * 1000f:N0} m)\n" : $"- Highest point: {maxAlt / 1000f:0.0} km (space starts at {karmanLineKm:0} km)\n");
        sb.Append($"- Top speed: {maxSpeed * 3.6f:N0} km/h ({maxSpeed:N0} m/s)\n");
        sb.Append($"- Max Q (biggest air push): {maxQ / 1000f:0.0} kPa\n");
        sb.Append($"- Max g-force: {maxG:0.0} g\n");
        sb.Append($"- Flight time: {FormatTime(tFlight)}\n");
        sb.Append($"- Stages used: {curStage + 1} of {stageCount}\n\n");
        sb.Append("<b>ENGINEER'S NOTE</b>\n").Append(expert).Append("\n\n");
        if (!success) { string fx = AscendMissionSim.FixFor(title); if (!string.IsNullOrEmpty(fx)) sb.Append("<b>HOW TO PREVENT IT NEXT TIME</b>\n").Append(fx).Append("\n\n"); }
        sb.Append("- Ascend Philippines Flight Dynamics");
        AscendMail.Deliver(data.agencyEmail, data.playerEmail, (success ? "FLIGHT SUCCESS: " : "FLIGHT FAILED: ") + design.name, sb.ToString(), success);
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        fade.alpha = from;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            fade.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        fade.alpha = to;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HUD
    // ═════════════════════════════════════════════════════════════════════════
    void ShowBig(string s, float seconds) { bigText.text = s; bigUntil = Time.unscaledTime + seconds; }

    void AddLog(string s)
    {
        log.Add("<color=#FFC850>T+" + FormatTime(tFlight) + "</color>  " + s);
        while (log.Count > 7) log.RemoveAt(0);
        if (logText) logText.text = string.Join("\n", log);
    }

    static string FormatTime(float t) { int s = Mathf.Max(0, (int)t); return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

    void UpdateHud()
    {
        int boosters = groups.Count(g => g.side && g.attached);
        stageLabel = $"Stage {curStage + 1} of {stageCount}" + (boosters > 0 ? $"  +  {boosters} booster{(boosters > 1 ? "s" : "")}" : "");
        float qLimit = QLimit;
        double theta = Math.Atan2(px, py);
        float downKm = (float)(EarthR * theta) / 1000f;
        float pres = 101.325f * Mathf.Exp(-altitude / 8000f);
        float twrNow = mass > 0f ? thrustNow / (mass * G0) : 0f;
        float pitch = 90f - tilt;
        float temp = AirTemp(altitude) - 273.15f;
        float maxThrust = 0f;
        foreach (var e in engines) if (e.grp.attached && e.grp.stage == curStage && !e.dead) maxThrust += Mathf.Max(e.p.thrustVackN, e.p.thrustSLkN) * 1000f;

        metText.text = $"<mspace=0.6em>T+{FormatTime(tFlight)}</mspace>";
        stageText.text = stageLabel + "   |   " + (liftedOff ? (engines.Any(e => e.lit) ? "<color=#3FB950>ENGINES ON</color>" : "<color=#FFC850>COASTING</color>") : "<color=#FFC850>ON THE PAD</color>");

        string M(string s) => "<mspace=0.56em>" + s + "</mspace>";
        string U(string s) => " <size=55%><color=#9AA0A6>" + s + "</color></size>";
        void G(int i, string v, float frac, bool warn = false)
        {
            if (i >= gauges.Count) return;
            gauges[i].value.text = v;
            gauges[i].fill.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
            gauges[i].img.color = warn ? MissionUI.Alert : frac > 0.8f ? MissionUI.Caution : MissionUI.Accent;
        }
        G(0, altitude < 10000f ? M($"{altitude:N0}") + U("m") : M($"{altitude / 1000f:0.00}") + U("km"), altitude / (goalKm * 1000f));
        G(1, M($"{speed * 3.6f:N0}") + U($"km/h  ({speed:N0} m/s)"), speed / 8000f);
        G(2, M($"{vUp:+0;-0}") + U("m/s"), Mathf.Abs(vUp) / 3000f, vUp < -5f && liftedOff);
        G(3, M($"{downKm:0.0}") + U("km"), downKm / 500f);
        G(4, M($"{gNow:0.0}") + U("g"), gNow / GLimit, gNow > GLimit * 0.8f);
        G(5, M($"{mach:0.00}"), mach / 25f);
        G(6, M($"{q / 1000f:0.0}") + U($"kPa  (limit {qLimit / 1000f:0})"), q / qLimit, q > qLimit * 0.85f);
        G(7, M($"{pres:0.00}") + U($"kPa  ({pres / 101.325f * 100f:0.#}% of sea level)"), pres / 101.325f);
        G(8, M($"{thrustNow / 1000f:N0}") + U("kN"), maxThrust > 0f ? thrustNow / maxThrust : 0f);
        G(9, M($"{throttle * 100f:0}") + U("%"), throttle);
        G(10, M(AscendReport.Mass(mass)), 1f);
        G(11, M($"{twrNow:0.00}"), twrNow / 4f, liftedOff && twrNow > 0f && twrNow < 1f && altitude < 20000f);
        G(12, M($"{pitch:0.0}") + U("°"), pitch / 90f);
        G(13, M($"{temp:0}") + U("°C"), Mathf.InverseLerp(-90f, 30f, temp));

        foreach (var row in fuelRows)
        {
            float start = row.g.pools.Sum(p => fuelStart[p]);
            float left = row.g.attached ? row.g.pools.Sum(p => fuel[p]) : 0f;
            float f = start > 0f ? left / start : 0f;
            row.fill.anchorMax = new Vector2(f, 1f);
            row.label.text = row.g.label + (row.g.attached ? $"  {f * 100f:0}%" : "  <color=#9AA0A6>dropped</color>");
        }

        float af = Mathf.Clamp01(altitude / (goalKm * 1000f));
        altFill.anchorMax = new Vector2(af, 1f);
        altLabel.text = cansat ? $"{altitude:N0} m of {goalKm * 1000f:N0} m  <color=#9AA0A6>(CanSat release height)</color>"
                               : $"{altitude / 1000f:0.0} km of {karmanLineKm:0} km  <color=#9AA0A6>(space starts at the Karman line)</color>";
        warpLabel.text = $"TIME  {EffectiveWarp:0.0}x";
        if (graph && liftedOff) graph.Add(tFlight, downKm, altitude / 1000f);
    }

    void BuildFuelRows()
    {
        fuelRows.Clear();
        var list = groups.Where(g => g.pools.Count > 0).OrderBy(g => g.side ? -1 : g.stage).ToList();
        int i = 0;
        foreach (var g in list)
        {
            if (i >= 6) break;
            if (g.side) g.label = "Booster " + (list.Where(x => x.side).ToList().IndexOf(g) + 1);
            var row = Gui.Rect("Fuel " + g.label, fuelPanel);
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-32f, 40f); row.anchoredPosition = new Vector2(0f, -48f - i * 44f);
            var label = Gui.Text(row, g.label, 15, Color.white, font, FontStyles.Bold);
            label.rectTransform.anchorMin = new Vector2(0f, 0.5f); label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var trt = Gui.Rect("Bar", row); trt.anchorMin = new Vector2(0f, 0.12f); trt.anchorMax = new Vector2(1f, 0.12f); trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(0f, 4f); trt.anchoredPosition = Vector2.zero;
            var frt = MissionUI.Bar(trt, g.side ? MissionUI.Caution : MissionUI.Accent, out _);
            frt.anchorMax = Vector2.one;
            fuelRows.Add((g, frt, label));
            i++;
        }
        fuelPanel.sizeDelta = new Vector2(fuelPanel.sizeDelta.x, 56f + i * 44f);
    }

    RectTransform fuelPanel;

    void BuildUI()
    {
        var canvasGO = new GameObject("Ascend Flight UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var canvasRoot = (RectTransform)canvasGO.transform;

        // Prompt (small, bottom)
        promptText = Gui.Text(canvasRoot, "", 24, Color.white, font, FontStyles.Bold);
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.outlineWidth = 0.2f; promptText.outlineColor = new Color32(0, 0, 0, 220);
        var prt = promptText.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
        prt.sizeDelta = new Vector2(1400, 44); prt.anchoredPosition = new Vector2(0, 50);

        // HUD
        var hud = Gui.Rect("HUD", canvasRoot); Gui.Stretch(hud);
        hudRoot = hud.gameObject;
        Color accent = MissionUI.Accent, labelC = MissionUI.Label;

        // FLIGHT COMPUTER (right side): live telemetry from the instruments
        var fc = MissionUI.Box(hud, "Flight Computer", MissionUI.Line, MissionUI.HudBg, true);
        fc.anchorMin = new Vector2(1f, 0f); fc.anchorMax = new Vector2(1f, 1f); fc.pivot = new Vector2(1f, 0.5f);
        fc.sizeDelta = new Vector2(560f, -48f); fc.anchoredPosition = new Vector2(-24f, 0f);
        var fcTitle = MissionUI.Lbl(fc, "FLIGHT COMPUTER", 15, font);
        fcTitle.color = MissionUI.Text;
        Gui.Stretch(fcTitle.rectTransform, 24, 0, 20, 16); fcTitle.alignment = TextAlignmentOptions.TopLeft;
        var fcSub = MissionUI.Lbl(fc, "TELEMETRY", 12, font);
        Gui.Stretch(fcSub.rectTransform, 24, 0, 20, 18); fcSub.alignment = TextAlignmentOptions.TopRight;
        MissionUI.HLine(fc, 44f, MissionUI.LineDim, 16f);
        metText = Gui.Text(fc, "", 42, Color.white, font, FontStyles.Bold);
        Gui.Stretch(metText.rectTransform, 24, 0, 20, 54); metText.alignment = TextAlignmentOptions.TopLeft;
        stageText = Gui.Text(fc, "", 16, labelC, font);
        Gui.Stretch(stageText.rectTransform, 24, 0, 20, 106); stageText.alignment = TextAlignmentOptions.TopLeft;

        string[] names = { "ALTITUDE", "VELOCITY", "VERTICAL SPEED", "DOWNRANGE", "ACCELERATION", "MACH", "DYNAMIC PRESSURE (Q)", "AIR PRESSURE", "THRUST", "THROTTLE", "MASS", "THRUST / WEIGHT", "PITCH (above horizon)", "AIR TEMPERATURE" };
        gauges.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            int col = i % 2, rowI = i / 2;
            var tile = MissionUI.Box(fc, "Gauge", MissionUI.LineDim);
            tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0f, 1f);
            tile.sizeDelta = new Vector2(250f, 82f);
            tile.anchoredPosition = new Vector2(24f + col * 262f, -140f - rowI * 90f);
            var lab = MissionUI.Lbl(tile, names[i], 11, font);
            lab.characterSpacing = 4f;
            Gui.Stretch(lab.rectTransform, 12, 0, 8, 9); lab.alignment = TextAlignmentOptions.TopLeft;
            var val = Gui.Text(tile, "-", 25, Color.white, font, FontStyles.Bold);
            Gui.Stretch(val.rectTransform, 12, 14, 8, 26); val.alignment = TextAlignmentOptions.Left;
            var track = Gui.Rect("Bar", tile);
            track.anchorMin = new Vector2(0f, 0f); track.anchorMax = new Vector2(1f, 0f); track.pivot = new Vector2(0.5f, 0f);
            track.sizeDelta = new Vector2(-24f, 3f); track.anchoredPosition = new Vector2(0f, 10f);
            var fr = MissionUI.Bar(track, accent, out Image fillImg);
            gauges.Add((val, fr, fillImg));
        }

        // Fuel (inside the flight computer, under the gauges)
        fuelPanel = Gui.Rect("Fuel", fc);
        fuelPanel.anchorMin = fuelPanel.anchorMax = fuelPanel.pivot = new Vector2(0f, 1f);
        fuelPanel.sizeDelta = new Vector2(524f, 100f); fuelPanel.anchoredPosition = new Vector2(16f, -140f - 7 * 90f - 4f);
        var ft = MissionUI.Lbl(fuelPanel, "PROPELLANT", 12, font);
        Gui.Stretch(ft.rectTransform, 8, 0, 8, 14); ft.alignment = TextAlignmentOptions.TopLeft;

        // Mission log (top left)
        var logBox = MissionUI.Box(hud, "Mission Log", MissionUI.Line, MissionUI.HudBg, true);
        logBox.anchorMin = logBox.anchorMax = logBox.pivot = new Vector2(0f, 1f);
        logBox.sizeDelta = new Vector2(600, 300); logBox.anchoredPosition = new Vector2(24, -24);
        var lt = MissionUI.Lbl(logBox, "MISSION LOG", 15, font);
        lt.color = MissionUI.Text;
        Gui.Stretch(lt.rectTransform, 20, 0, 20, 14); lt.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.HLine(logBox, 42f, MissionUI.LineDim, 14f);
        logText = Gui.Text(logBox, "", 17, Color.white, font);
        Gui.Stretch(logText.rectTransform, 20, 14, 20, 54); logText.alignment = TextAlignmentOptions.TopLeft;
        logText.lineSpacing = 4f;

        // Height bar to the goal (bottom, left of the flight computer)
        var altBox = MissionUI.Box(hud, "Altitude", MissionUI.Line, MissionUI.HudBg, true);
        altBox.anchorMin = altBox.anchorMax = altBox.pivot = new Vector2(0f, 0f);
        altBox.sizeDelta = new Vector2(1000, 96); altBox.anchoredPosition = new Vector2(24, 24);
        altLabel = Gui.Text(altBox, "", 18, Color.white, font, FontStyles.Bold);
        Gui.Stretch(altLabel.rectTransform, 24, 40, 24, 12); altLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var altTrack = Gui.Rect("Bar", altBox);
        altTrack.anchorMin = new Vector2(0f, 0f); altTrack.anchorMax = new Vector2(1f, 0f); altTrack.pivot = new Vector2(0.5f, 0f);
        altTrack.sizeDelta = new Vector2(-48f, 6f); altTrack.anchoredPosition = new Vector2(0f, 24f);
        altFill = MissionUI.Bar(altTrack, accent, out _);
        // tick marks every 10%
        for (int i = 0; i <= 10; i++)
        {
            var tick = MissionUI.Solid(altTrack, MissionUI.LineDim, false, "Tick").rectTransform;
            tick.anchorMin = tick.anchorMax = new Vector2(i / 10f, 0f); tick.pivot = new Vector2(0.5f, 1f);
            tick.sizeDelta = new Vector2(1.5f, i % 5 == 0 ? 10f : 6f); tick.anchoredPosition = new Vector2(0f, -2f);
        }

        // Time speed: - / + (up to 2x)
        var warpBox = MissionUI.Box(hud, "Controls", MissionUI.Line, MissionUI.HudBg, true);
        warpBox.anchorMin = warpBox.anchorMax = warpBox.pivot = new Vector2(0f, 0f);
        warpBox.sizeDelta = new Vector2(1000, 64); warpBox.anchoredPosition = new Vector2(24, 132);
        if (font && !AUI.Font) AUI.Font = font;
        // Time speed slider (0.5x - 5x). It speeds up by itself as the rocket climbs; drag it any time.
        warpLabel = Gui.Text(warpBox, "", 19, Color.white, font, FontStyles.Bold);
        var wl = warpLabel.rectTransform; wl.anchorMin = wl.anchorMax = wl.pivot = new Vector2(0f, 0.5f); wl.sizeDelta = new Vector2(150, 46); wl.anchoredPosition = new Vector2(16, 0);
        warpLabel.alignment = TextAlignmentOptions.MidlineLeft;
        warpSlider = AscendTrajectoryGraph.MakeSlider(warpBox, new Vector2(170f, 0f), new Vector2(160f, 30f), MinWarp, MaxWarp, 1f);
        warpSlider.onValueChanged.AddListener(v => { SetWarp(v, true); var es = UnityEngine.EventSystems.EventSystem.current; if (es && es.currentSelectedGameObject == warpSlider.gameObject) es.SetSelectedGameObject(null); });   // never keeps keyboard focus
        var sep = MissionUI.Solid(warpBox, MissionUI.LineDim, false, "Divider").rectTransform;
        sep.anchorMin = sep.anchorMax = sep.pivot = new Vector2(0f, 0.5f); sep.sizeDelta = new Vector2(1f, 40f); sep.anchoredPosition = new Vector2(346, 0);
        warpText = Gui.Text(warpBox, "1 CHASE   2 NOSE   3 GROUND CAM   //   RMB LOOK   SCROLL ZOOM   - / + TIME   H HIDE", 13, labelC, font);
        warpText.characterSpacing = 3f;
        var wrt = warpText.rectTransform; wrt.anchorMin = new Vector2(0f, 0f); wrt.anchorMax = new Vector2(1f, 1f); wrt.offsetMin = new Vector2(364, 0); wrt.offsetMax = new Vector2(-16, 0);

        // Trajectory graph (left, between the log and the controls): height vs distance downrange
        graph = AscendTrajectoryGraph.Build(hud, new Vector2(24f, 212f), new Vector2(600f, 340f), font);
        warpText.alignment = TextAlignmentOptions.MidlineLeft;

        // Window bar (top middle): show / hide each window
        windows.Clear();
        windows.Add(("COMPUTER", fc.gameObject)); windows.Add(("LOG", logBox.gameObject)); windows.Add(("HEIGHT", altBox.gameObject)); windows.Add(("CONTROLS", warpBox.gameObject)); windows.Add(("GRAPH", graph.gameObject));
        var bar = MissionUI.Box(hud, "Window Bar", MissionUI.Line, MissionUI.HudBg);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(134f * (windows.Count + 1) + 16f, 52f); bar.anchoredPosition = new Vector2(20f, -16f);
        windowBtns.Clear();
        for (int i = 0; i <= windows.Count; i++)
        {
            int k = i;
            string label = i < windows.Count ? windows[i].name : "HIDE ALL (H)";
            var b = MissionUI.Btn(bar, label, 13, MissionUI.Line, () => { if (k < windows.Count) ToggleWindow(k); else ToggleAllWindows(); }, 128, 38, font);
            MissionUI.Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(8f + i * 134f, 0f), new Vector2(128f, 38f));
            b.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 3f;
            windowBtns.Add(b);
        }

        // Big center messages
        bigText = Gui.Text(canvasRoot, "", 100, Color.white, font, FontStyles.Bold);
        Gui.Stretch(bigText.rectTransform, 0, 200, 0, 200);
        bigText.alignment = TextAlignmentOptions.Center;
        bigText.characterSpacing = 8f;
        bigText.outlineWidth = 0.15f; bigText.outlineColor = new Color32(0, 0, 0, 230);

        // Fade (on top)
        var f = Gui.Img(canvasRoot, Color.black, 0f);
        Gui.Stretch(f.rectTransform);
        fade = f.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f; fade.blocksRaycasts = false; fade.interactable = false;

        // End card (above the fade, so it shows on a black screen too)
        var card = MissionUI.Box(canvasRoot, "Flight Result", MissionUI.Line, MissionUI.Bg, true);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(1200, 860);
        endCard = card.gameObject;
        var endHead = MissionUI.Lbl(card, "ASCEND MISSION CONTROL   //   FLIGHT RESULT", 14, font);
        Gui.Stretch(endHead.rectTransform, 40, 0, 40, 26); endHead.alignment = TextAlignmentOptions.Top;
        MissionUI.HLine(card, 60f, MissionUI.LineDim, 40f);
        endTitle = Gui.Text(card, "", 60, Color.white, font, FontStyles.Bold);
        endTitle.characterSpacing = 6f;
        Gui.Stretch(endTitle.rectTransform, 40, 700, 40, 76); endTitle.alignment = TextAlignmentOptions.Center;
        MissionUI.HLine(card, 170f, MissionUI.LineDim, 40f);
        endBody = Gui.Text(card, "", 23, Color.white, font);
        Gui.Stretch(endBody.rectTransform, 60, 30, 60, 196); endBody.alignment = TextAlignmentOptions.Top;

        // Launch Computer screen (above the HUD, below the fade)
        if (font && !AUI.Font) AUI.Font = font;
        BuildConsole(canvasRoot);
        consoleRoot.transform.SetSiblingIndex(f.transform.GetSiblingIndex());

    }

    // ═════════════════════════════════════════════════════════════════════════
    //  INPUT (works with the old Input Manager and the new Input System)
    // ═════════════════════════════════════════════════════════════════════════
    static string KeyName(KeyCode k) => k == KeyCode.Mouse0 ? "Click" : "Press " + k;

    static bool KeyDown(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        if (k == KeyCode.Mouse0) return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        var kb = Keyboard.current;
        if (kb == null) return false;
        string n = k.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        else if (n == "Return") n = "Enter";
        if (Enum.TryParse(n, out Key key) && key != Key.None) return kb[key].wasPressedThisFrame;
        return false;
#else
        return Input.GetKeyDown(k);
#endif
    }

    static Vector2 MouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.05f : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 0.5f;
#endif
    }

    static bool RightMouseHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    static float Scroll()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Small UI helpers
    // ═════════════════════════════════════════════════════════════════════════
    static class Gui
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        public static Image Img(Transform parent, Color c, float radius)
        {
            var img = Rect("Image", parent).gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            if (radius > 0f) { img.sprite = AscendFX.RoundSprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 64f / radius; }
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string s, float size, Color c, TMP_FontAsset font, FontStyles st = FontStyles.Normal)
        {
            var t = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = s; t.fontSize = size; t.color = c; t.fontStyle = st; t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  DEBRIS: dropped stages, boosters, fairings and pieces after an explosion
// ═════════════════════════════════════════════════════════════════════════════
public class AscendDebris : MonoBehaviour
{
    public Vector3 velocity, spin;
    public float groundY;
    float life;

    void Update()
    {
        float dt = Time.deltaTime;
        float h = Mathf.Max(0f, transform.position.y - groundY);
        float airDrag = 0.15f * Mathf.Exp(-h / 8500f);      // thin air up high, thick air low down
        velocity += Vector3.down * 9.81f * dt;
        velocity *= Mathf.Max(0f, 1f - airDrag * dt);
        transform.position += velocity * dt;
        transform.Rotate(spin * dt, Space.World);
        life += dt;
        if (transform.position.y <= groundY)
        {
            AscendFX.Boom(transform.position, 0.8f);
            Destroy(gameObject);
        }
        else if (life > 180f) Destroy(gameObject);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  EFFECTS: engine flames, smoke, explosions (made from code, no assets needed)
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendFX
{
    static Material additive, alpha;
    static Texture2D soft;
    static Sprite round;

    public static Sprite RoundSprite
    {
        get
        {
            if (round) return round;
            const int size = 160; const float r = 64f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, 0f, fx - (size - r)), dy = Mathf.Max(r - fy, 0f, fy - (size - r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f)));
            }
            tex.SetPixels32(px); tex.Apply();
            return round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }
    }

    static Texture2D Soft()
    {
        if (soft) return soft;
        const int s = 64;
        soft = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
            float a = Mathf.Clamp01(1f - d); a *= a;
            px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        soft.SetPixels32(px); soft.Apply();
        return soft;
    }

    static Material Make(bool add)
    {
        string[] names = add
            ? new[] { "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Legacy Shaders/Particles/Additive", "Sprites/Default" }
            : new[] { "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended", "Sprites/Default" };
        Shader sh = null;
        foreach (var n in names) { sh = Shader.Find(n); if (sh) break; }
        var m = new Material(sh);
        var tex = Soft();
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", add ? 2f : 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)(add ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(add ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        return m;
    }

    static Material Additive => additive ? additive : (additive = Make(true));
    static Material Alpha => alpha ? alpha : (alpha = Make(false));

    static ParticleSystem New(string name, Transform parent, Vector3 worldPos, Material mat, bool worldSpace)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        go.transform.rotation = parent ? parent.rotation * Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;   // +Z points down the nozzle
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return ps;
    }

    public static ParticleSystem Flame(Transform engine, Vector3 nozzle, float size, bool solid)
    {
        var ps = New("Flame", engine, nozzle, Additive, false);
        var main = ps.main;
        main.loop = true; main.duration = 1f; main.maxParticles = 400;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(size * 25f, size * 40f);
        main.startSize = size * 1.3f;
        main.startColor = solid ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.7f, 0.35f))
                                : new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.55f), new Color(1f, 0.55f, 0.2f));
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = size * 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.4f), new GradientColorKey(new Color(0.9f, 0.25f, 0.1f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        ps.Play();
        return ps;
    }

    public static ParticleSystem Smoke(Transform engine, Vector3 nozzle, float size)
    {
        var ps = New("Smoke", engine, nozzle, Alpha, true);
        var main = ps.main;
        main.loop = true; main.duration = 1f; main.maxParticles = 1500;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(size * 8f, size * 14f);
        main.startSize = size * 3f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0.55f), new Color(0.7f, 0.7f, 0.72f, 0.45f));
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15f; sh.radius = size * 0.5f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 5f));
        ps.Play();
        return ps;
    }

    // White gas puff at a stage separation (moves with the rocket for a moment, then fades)
    public static void Puff(Vector3 pos, float size, Vector3 axis, Vector3 velocity)
    {
        var holder = new GameObject("Separation Puff");
        holder.transform.position = pos;
        holder.transform.rotation = Quaternion.LookRotation(axis);
        holder.AddComponent<AscendPuffMover>().velocity = velocity;
        var ps = New("Gas", holder.transform, pos, Alpha, false);
        ps.transform.localRotation = Quaternion.identity;
        var m = ps.main;
        m.loop = false; m.duration = 0.3f; m.maxParticles = 120;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(size * 2f, size * 6f);
        m.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.8f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.96f, 1f, 0.8f), new Color(0.8f, 0.82f, 0.86f, 0.6f));
        var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = size * 0.5f; sh.radiusThickness = 0f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3f));
        ps.Play();
        Object.Destroy(holder, 4f);
    }

    public static void Boom(Vector3 pos, float scale)
    {
        var holder = new GameObject("Explosion");
        holder.transform.position = pos;

        var fire = New("Fire", holder.transform, pos, Additive, true);
        var m = fire.main;
        m.loop = false; m.duration = 0.5f; m.maxParticles = 200;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(10f * scale, 35f * scale);
        m.startSize = new ParticleSystem.MinMaxCurve(6f * scale, 14f * scale);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.45f, 0.1f));
        var fe = fire.emission; fe.rateOverTime = 0f; fe.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(80 * Mathf.Min(scale, 2f))) });
        var fs = fire.shape; fs.shapeType = ParticleSystemShapeType.Sphere; fs.radius = 2f * scale;
        var fc = fire.colorOverLifetime; fc.enabled = true;
        var fg = new Gradient();
        fg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.5f), new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fc.color = fg;
        fire.Play();

        var smoke = New("Smoke", holder.transform, pos, Alpha, true);
        var s = smoke.main;
        s.loop = false; s.duration = 1f; s.maxParticles = 150;
        s.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
        s.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 14f * scale);
        s.startSize = new ParticleSystem.MinMaxCurve(10f * scale, 22f * scale);
        s.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.15f, 0.15f, 0.8f), new Color(0.35f, 0.33f, 0.3f, 0.7f));
        var se = smoke.emission; se.rateOverTime = 0f; se.SetBursts(new[] { new ParticleSystem.Burst(0.1f, (short)(50 * Mathf.Min(scale, 2f))) });
        var ss = smoke.shape; ss.shapeType = ParticleSystemShapeType.Sphere; ss.radius = 3f * scale;
        var sc = smoke.colorOverLifetime; sc.enabled = true;
        var sg = new Gradient();
        sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        sc.color = sg;
        smoke.Play();

        var light = new GameObject("Flash").AddComponent<Light>();
        light.transform.SetParent(holder.transform, false);
        light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.3f); light.range = 120f * scale; light.intensity = 8f;
        holder.AddComponent<AscendFlashFade>().flash = light;

        Object.Destroy(holder, 10f);
    }
}

// Fades an explosion's flash of light
public class AscendFlashFade : MonoBehaviour
{
    public Light flash;
    void Update() { if (flash) flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, Time.deltaTime * 6f); }
}


// ═════════════════════════════════════════════════════════════════════════════
//  The Philippines seen from the rocket: a real-size map (1 unit = 1 m) bent to Earth's curve,
//  plus a layer of clouds. Islands come from simplified coastlines (latitude / longitude); mountains,
//  forests, farmland, cities and the sea depth are painted in code (made in the background, no files needed).
//  For a photo-real look, put a satellite image of the area in "Custom Map" (e.g. NASA Blue Marble, free).
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendEarthMap
{
    public const float Lon0 = 110f, Lon1 = 138f, Lat0 = -2f, Lat1 = 26f;   // area covered (wider than the view from 100 km)
    const double EarthR = 6371000.0;
    const int W = 2048, H = 2048, CW = 1024;
    const float CloudHalfKm = 260f;
    static Texture2D tex, cloudTex;
    static Material mat, cloudMat;
    static System.Threading.Tasks.Task<Color32[]> mapJob, cloudJob;

    static readonly float[][] Islands =
    {
        // Luzon
        new float[] { 120.58f,18.5f, 120.8f,18.62f, 121.1f,18.62f, 121.6f,18.36f, 122.05f,18.52f, 122.25f,18.45f, 122.15f,17.9f, 122.2f,17.3f, 122.45f,17.1f, 122.5f,16.6f,
                      122.2f,16.25f, 121.9f,16.2f, 121.6f,15.9f, 121.55f,15.6f, 121.4f,15.3f, 121.6f,14.9f, 121.65f,14.55f, 121.62f,14.2f, 121.85f,14.0f, 122.2f,13.95f,
                      122.3f,14.15f, 122.65f,14.2f, 122.9f,14.05f, 123.05f,13.85f, 123.3f,13.95f, 123.55f,13.75f, 123.75f,13.85f, 123.95f,13.75f, 124.15f,13.55f, 124.15f,13.1f,
                      124.05f,12.75f, 123.95f,12.55f, 123.75f,12.65f, 123.5f,12.9f, 123.25f,13.0f, 123.0f,13.15f, 122.95f,13.45f, 122.7f,13.3f, 122.6f,13.5f, 122.45f,13.75f,
                      122.25f,13.85f, 122.05f,13.75f, 121.85f,13.7f, 121.6f,13.6f, 121.3f,13.6f, 121.05f,13.7f, 120.95f,13.75f, 120.75f,13.9f, 120.65f,14.15f, 120.6f,14.35f,
                      120.9f,14.5f, 121.0f,14.55f, 120.85f,14.75f, 120.6f,14.6f, 120.45f,14.45f, 120.3f,14.6f, 120.25f,14.85f, 120.05f,14.95f, 119.95f,15.4f, 119.85f,15.85f,
                      119.9f,16.2f, 119.95f,16.4f, 120.2f,16.05f, 120.35f,16.1f, 120.3f,16.5f, 120.4f,16.9f, 120.45f,17.4f, 120.4f,17.8f, 120.55f,18.2f },
        new float[] { 120.3f,13.5f, 120.9f,13.5f, 121.5f,13.1f, 121.4f,12.4f, 121.0f,12.2f, 120.6f,12.6f, 120.4f,13.0f },                              // Mindoro
        new float[] { 119.5f,11.4f, 119.9f,10.9f, 119.7f,10.4f, 119.0f,10.0f, 118.4f,9.3f, 117.6f,8.6f, 117.2f,8.4f, 117.4f,8.7f, 118.3f,9.7f, 118.8f,10.4f, 119.3f,11.2f }, // Palawan
        new float[] { 121.9f,11.9f, 122.6f,11.6f, 122.6f,10.7f, 122.0f,10.5f, 121.9f,10.8f, 121.9f,11.4f },                                         // Panay
        new float[] { 123.0f,10.9f, 123.5f,10.8f, 123.2f,9.9f, 123.2f,9.1f, 122.9f,9.0f, 122.4f,9.6f, 122.4f,10.5f, 122.9f,10.9f },                 // Negros
        new float[] { 124.0f,11.3f, 124.0f,10.6f, 123.6f,9.6f, 123.3f,9.4f, 123.4f,10.0f, 123.7f,10.6f, 123.9f,11.0f },                             // Cebu
        new float[] { 123.8f,10.1f, 124.3f,10.2f, 124.6f,9.8f, 124.1f,9.6f, 123.8f,9.6f },                                                          // Bohol
        new float[] { 124.3f,11.6f, 125.0f,11.3f, 125.1f,10.6f, 125.2f,10.0f, 124.8f,10.0f, 124.7f,10.7f, 124.4f,10.9f, 124.3f,11.3f },             // Leyte
        new float[] { 124.2f,12.5f, 125.3f,12.5f, 125.7f,11.4f, 125.5f,11.0f, 125.0f,11.3f, 124.6f,11.7f, 124.3f,12.0f },                           // Samar
        new float[] { 123.2f,12.5f, 123.8f,12.2f, 124.0f,11.9f, 123.6f,11.9f, 123.3f,12.2f },                                                       // Masbate
        new float[] { 124.0f,14.0f, 124.4f,14.0f, 124.4f,13.6f, 124.1f,13.6f },                                                                     // Catanduanes
        new float[] { 121.8f,15.0f, 122.2f,14.9f, 122.1f,14.6f, 121.8f,14.7f },                                                                     // Polillo
        // Mindanao
        new float[] { 121.9f,6.95f, 122.1f,7.45f, 122.25f,7.8f, 122.6f,8.0f, 123.0f,8.15f, 123.35f,8.6f, 123.75f,8.6f, 123.85f,8.25f, 124.25f,8.2f, 124.55f,8.5f,
                      124.8f,9.0f, 125.15f,9.0f, 125.45f,9.8f, 125.6f,9.5f, 126.0f,9.3f, 126.2f,9.0f, 126.35f,8.5f, 126.55f,7.8f, 126.6f,7.3f, 126.2f,6.3f,
                      125.85f,6.95f, 125.65f,7.25f, 125.4f,6.95f, 125.5f,6.4f, 125.4f,5.6f, 125.15f,5.9f, 124.75f,5.9f, 124.2f,6.15f, 124.0f,6.6f, 124.1f,7.15f,
                      124.3f,7.4f, 123.95f,7.55f, 123.6f,7.75f, 123.4f,7.6f, 123.1f,7.45f, 122.85f,7.45f, 122.5f,7.25f, 122.2f,6.95f },
        new float[] { 120.8f,6.1f, 121.3f,6.1f, 121.2f,5.8f, 120.9f,5.9f },                                                                         // Jolo
        new float[] { 121.8f,6.7f, 122.3f,6.7f, 122.2f,6.4f, 121.8f,6.4f },                                                                         // Basilan
        // Neighbours: Borneo, Taiwan, Sulawesi tip, Halmahera
        new float[] { 110f,-2f, 110f,1.5f, 111.5f,2.5f, 113.0f,3.2f, 114.0f,4.6f, 115.5f,5.4f, 116.7f,7.0f, 117.5f,6.5f, 118.5f,5.8f, 119.3f,5.2f, 118.3f,4.4f, 117.7f,4.0f, 117.6f,2.5f, 118.9f,1.0f, 117.9f,0.0f, 117.4f,-2f },
        new float[] { 120.1f,23.0f, 120.7f,22.0f, 120.9f,21.9f, 121.4f,23.0f, 121.9f,24.6f, 121.9f,25.2f, 121.5f,25.3f, 120.7f,24.6f, 120.2f,23.8f },
        new float[] { 120.4f,1.2f, 122.0f,1.0f, 123.5f,0.9f, 124.5f,1.4f, 125.2f,1.6f, 124.9f,0.6f, 123.5f,0.4f, 121.5f,0.5f, 120.3f,0.3f, 119.7f,-1f, 120.4f,-2f, 121.5f,-2f, 120.9f,-0.6f },
        new float[] { 127.5f,2.2f, 128.0f,1.6f, 128.7f,1.3f, 128.0f,0.8f, 127.9f,0.0f, 127.4f,0.6f, 127.6f,1.2f },
    };

    // Big cities (longitude, latitude, size)
    static readonly float[] Cities = { 121.0f,14.6f,1.6f, 123.9f,10.3f,0.8f, 125.6f,7.1f,0.8f, 122.55f,10.7f,0.45f, 122.95f,10.67f,0.4f,
                                       124.65f,8.48f,0.45f, 122.07f,6.92f,0.4f, 120.6f,16.4f,0.35f, 120.6f,15.15f,0.45f, 121.17f,13.95f,0.35f };

    // Start painting in the background (call early, e.g. when the scene starts)
    public static void Prepare()
    {
        if (mapJob == null && !tex) mapJob = System.Threading.Tasks.Task.Run(() => PaintMap());
        if (cloudJob == null && !cloudTex) cloudJob = System.Threading.Tasks.Task.Run(() => PaintClouds());
    }

    public static GameObject Build(Vector3 site, float lat, float lon, Texture2D custom, Vector4 customBounds)
    {
        var go = new GameObject("Philippines (real size)");
        go.transform.position = site;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        bool useCustom = custom && customBounds.z > customBounds.x && customBounds.w > customBounds.y;
        mf.sharedMesh = useCustom ? MakeMesh(lat, lon, customBounds.x, customBounds.z, customBounds.y, customBounds.w, 0.0)
                                  : MakeMesh(lat, lon, Lon0, Lon1, Lat0, Lat1, 0.0);
        mr.sharedMaterial = useCustom ? Opaque(custom, "Custom Map") : GetMaterial();
        return go;
    }

    // Cloud layer about 2.5 km up, around the launch site
    public static GameObject BuildClouds(Vector3 site, float lat, float lon)
    {
        var go = new GameObject("Clouds");
        go.transform.position = site + Vector3.up * 2500f;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        float dLat = CloudHalfKm / 111.2f, dLon = dLat / Mathf.Cos(lat * Mathf.Deg2Rad);
        mf.sharedMesh = MakeMesh(lat, lon, lon - dLon, lon + dLon, lat - dLat, lat + dLat, 2500.0);
        mr.sharedMaterial = GetCloudMaterial();
        return go;
    }

    // A grid over the area; each point placed at its real distance from the launch site and dropped for Earth's curve
    static Mesh MakeMesh(float lat0, float lon0, float lonA, float lonB, float latA, float latB, double height)
    {
        const int N = 160;
        var v = new Vector3[(N + 1) * (N + 1)];
        var uv = new Vector2[v.Length];
        double kLat = Math.PI * EarthR / 180.0, R = EarthR + height;
        for (int j = 0; j <= N; j++)
        for (int i = 0; i <= N; i++)
        {
            float lon = Mathf.Lerp(lonA, lonB, i / (float)N), lat = Mathf.Lerp(latA, latB, j / (float)N);
            double north = (lat - lat0) * kLat;
            double east = (lon - lon0) * kLat * Math.Cos(lat * Math.PI / 180.0);
            double drop = (north * north + east * east) / (2.0 * R);
            v[j * (N + 1) + i] = new Vector3((float)east, (float)-drop, (float)north);
            uv[j * (N + 1) + i] = new Vector2(i / (float)N, j / (float)N);
        }
        var tri = new int[N * N * 6];
        int t = 0;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
            tri[t++] = a; tri[t++] = c; tri[t++] = b;
            tri[t++] = b; tri[t++] = c; tri[t++] = d;
        }
        var m = new Mesh { name = "Earth patch" };
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = v; m.uv = uv; m.triangles = tri;
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 8000000f);
        return m;
    }

    static Shader FindShader(params string[] names)
    {
        foreach (var n in names) { var sh = Shader.Find(n); if (sh) return sh; }
        return null;
    }

    static Material Opaque(Texture t, string name)
    {
        var m = new Material(FindShader("Universal Render Pipeline/Unlit", "Unlit/Texture", "Universal Render Pipeline/Lit", "Standard")) { name = name };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        return m;
    }

    static Material GetMaterial()
    {
        if (mat) return mat;
        if (!tex)
        {
            Prepare();
            var px = mapJob.Result;   // waits only if the background painting isn't finished yet
            tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Philippines", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.SetPixels32(px); tex.Apply(true, true);
            mapJob = null;
        }
        mat = Opaque(tex, "Philippines Map");
        return mat;
    }

    static Material GetCloudMaterial()
    {
        if (cloudMat) return cloudMat;
        if (!cloudTex)
        {
            Prepare();
            var px = cloudJob.Result;
            cloudTex = new Texture2D(CW, CW, TextureFormat.RGBA32, true) { name = "Clouds", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            cloudTex.SetPixels32(px); cloudTex.Apply(true, true);
            cloudJob = null;
        }
        var sh = FindShader("Universal Render Pipeline/Unlit", "Unlit/Transparent", "Sprites/Default");
        var m = new Material(sh) { name = "Clouds" };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", cloudTex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", cloudTex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        // URP transparent setup
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        cloudMat = m;
        return m;
    }

    // ───────────── painting (runs on a background thread: plain C# only) ─────────────
    static Color32[] PaintMap()
    {
        var land = new float[W * H];
        foreach (var poly in Islands) Fill(poly, land, W, H);
        var soft = Blur(land, W, H, 3);
        var mid = Blur(land, W, H, 14);
        var wide = Blur(land, W, H, 40);

        // Height: mountains run along the middle of the islands
        var height = new float[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int k = y * W + x;
            if (soft[k] < 0.5f) continue;
            float fx = x / (float)W * 90f, fy = y / (float)H * 90f;
            float ridge = 1f - Math.Abs(Noise(fx * 0.9f, fy * 0.9f) * 2f - 1f);
            float n = Fbm(fx, fy, 5);
            float inland = Clamp01((mid[k] - 0.45f) * 2.2f) * 0.6f + Clamp01((wide[k] - 0.35f) * 2f) * 0.4f;
            height[k] = inland * (0.25f + 0.45f * n + 0.3f * ridge * ridge);
        }

        Color deep = C(3, 18, 52), sea = C(8, 42, 92), shallow = C(28, 120, 140), reef = C(60, 165, 170);
        Color farm = C(96, 122, 58), farm2 = C(120, 128, 70), forest = C(28, 70, 30), jungle = C(18, 52, 22), rock = C(98, 88, 66), peak = C(130, 120, 104), sand = C(205, 190, 150), city = C(120, 118, 112);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int k = y * W + x;
            float fx = x / (float)W * 90f, fy = y / (float)H * 90f;
            Color c;
            if (soft[k] >= 0.5f)
            {
                float h = height[k];
                float n2 = Noise(fx * 7f + 3f, fy * 7f + 9f);
                c = Lerp(Lerp(farm, farm2, n2), forest, Smooth(0.18f, 0.35f, h));
                c = Lerp(c, jungle, Smooth(0.35f, 0.5f, h) * 0.8f);
                c = Lerp(c, rock, Smooth(0.55f, 0.7f, h));
                c = Lerp(c, peak, Smooth(0.72f, 0.85f, h));
                c = Lerp(sand, c, Clamp01((soft[k] - 0.5f) * 8f));
                // light from the north-west gives the mountains shape
                int xa = Math.Max(0, x - 1), xb = Math.Min(W - 1, x + 1), ya = Math.Min(H - 1, y + 1), yb = Math.Max(0, y - 1);
                float shade = 1f + (height[ya * W + xa] - height[yb * W + xb]) * 26f;
                c *= Math.Max(0.55f, Math.Min(1.35f, shade));
                // cities
                float lon = Lon0 + (x + 0.5f) / W * (Lon1 - Lon0), lat = Lat0 + (y + 0.5f) / H * (Lat1 - Lat0);
                for (int i = 0; i < Cities.Length; i += 3)
                {
                    float dx = (lon - Cities[i]) / (Cities[i + 2] * 0.12f), dy = (lat - Cities[i + 1]) / (Cities[i + 2] * 0.12f);
                    float d2 = dx * dx + dy * dy;
                    if (d2 < 4f) c = Lerp(c, city, (float)Math.Exp(-d2 * 1.4f) * (0.55f + 0.45f * Noise(fx * 40f, fy * 40f)));
                }
            }
            else
            {
                float near = Clamp01(wide[k] * 2.5f);
                c = Lerp(deep, sea, Smooth(0f, 0.6f, near));
                c = Lerp(c, shallow, Smooth(0.12f, 0.45f, mid[k]));
                c = Lerp(c, reef, Smooth(0.3f, 0.5f, soft[k]) * 0.8f);
                c *= 0.94f + Noise(fx * 3f, fy * 3f) * 0.12f;
            }
            // thin far-away clouds (the real cloud layer is drawn separately near the launch site)
            float cl = Fbm(fx * 0.5f + 40f, fy * 0.5f + 7f, 5);
            c = Lerp(c, new Color(0.93f, 0.94f, 0.96f), Smooth(0.58f, 0.78f, cl) * 0.55f);
            px[k] = new Color32((byte)(Clamp01(c.r) * 255), (byte)(Clamp01(c.g) * 255), (byte)(Clamp01(c.b) * 255), 255);
        }
        return px;
    }

    static Color32[] PaintClouds()
    {
        var px = new Color32[CW * CW];
        for (int y = 0; y < CW; y++)
        for (int x = 0; x < CW; x++)
        {
            float fx = x / (float)CW * 40f, fy = y / (float)CW * 40f;
            float big = Fbm(fx * 0.25f + 11f, fy * 0.25f + 3f, 3);                  // where the cloud fields are
            float puff = Fbm(fx + 5f, fy + 17f, 6);                                  // the puffy cumulus
            float d = puff * 0.75f + big * 0.45f - 0.55f;
            float a = Smooth(0.0f, 0.22f, d);
            // fade out at the edge of the layer
            float ex = Math.Abs(x / (float)CW * 2f - 1f), ey = Math.Abs(y / (float)CW * 2f - 1f);
            a *= 1f - Smooth(0.75f, 1f, Math.Max(ex, ey));
            float shade = 0.78f + 0.22f * Smooth(0.05f, 0.35f, d);                  // thick middles are brighter on top
            byte g = (byte)(Clamp01(shade) * 255);
            px[y * CW + x] = new Color32(g, g, (byte)Math.Min(255, g + 6), (byte)(Clamp01(a * 0.92f) * 255));
        }
        return px;
    }

    static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    static Color Lerp(Color a, Color b, float t) { t = Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f); }
    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    static float Smooth(float a, float b, float v) { float t = Clamp01((v - a) / (b - a)); return t * t * (3f - 2f * t); }

    // Value noise (plain C#, safe on any thread)
    static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }
    }

    static float Noise(float x, float y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float tx = x - xi, ty = y - yi;
        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
        float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
    }

    static float Fbm(float x, float y, int oct)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < oct; i++) { sum += Noise(x, y) * amp; norm += amp; x = x * 2.03f + 17.1f; y = y * 2.03f + 5.3f; amp *= 0.5f; }
        return sum / norm;
    }

    // Fill one coastline polygon (scanline: find where each row crosses the coast, fill between pairs)
    static void Fill(float[] poly, float[] land, int W, int H)
    {
        int n = poly.Length / 2;
        var xs = new List<float>();
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < n; i++) { float y = Row(poly[i * 2 + 1], H); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        for (int row = Math.Max(0, (int)Math.Floor(minY)); row <= Math.Min(H - 1, (int)Math.Ceiling(maxY)); row++)
        {
            float yc = row + 0.5f;
            xs.Clear();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float x1 = Col(poly[i * 2], W), y1 = Row(poly[i * 2 + 1], H), x2 = Col(poly[j * 2], W), y2 = Row(poly[j * 2 + 1], H);
                if ((y1 <= yc && y2 > yc) || (y2 <= yc && y1 > yc)) xs.Add(x1 + (yc - y1) / (y2 - y1) * (x2 - x1));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = Math.Max(0, (int)Math.Ceiling(xs[k])); x <= Math.Min(W - 1, (int)Math.Floor(xs[k + 1])); x++)
                    land[row * W + x] = 1f;
        }
    }

    static float Col(float lon, int W) => (lon - Lon0) / (Lon1 - Lon0) * W;
    static float Row(float lat, int H) => (lat - Lat0) / (Lat1 - Lat0) * H;

    // Fast box blur (two passes, running sums)
    static float[] Blur(float[] src, int W, int H, int r)
    {
        var tmp = new float[W * H]; var dst = new float[W * H];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < H; y++)
        {
            float sum = 0f;
            for (int x = -r; x <= r; x++) sum += src[y * W + Math.Max(0, Math.Min(W - 1, x))];
            for (int x = 0; x < W; x++)
            {
                tmp[y * W + x] = sum * inv;
                sum += src[y * W + Math.Min(W - 1, x + r + 1)] - src[y * W + Math.Max(0, x - r)];
            }
        }
        for (int x = 0; x < W; x++)
        {
            float sum = 0f;
            for (int y = -r; y <= r; y++) sum += tmp[Math.Max(0, Math.Min(H - 1, y)) * W + x];
            for (int y = 0; y < H; y++)
            {
                dst[y * W + x] = sum * inv;
                sum += tmp[Math.Min(H - 1, y + r + 1) * W + x] - tmp[Math.Max(0, y - r) * W + x];
            }
        }
        return dst;
    }
}


// Moves a separation puff along with the rocket's speed for a short moment
public class AscendPuffMover : MonoBehaviour
{
    public Vector3 velocity;
    void Update() { velocity += Vector3.down * 9.81f * Time.deltaTime; transform.position += velocity * Time.deltaTime; }
}


// ═════════════════════════════════════════════════════════════════════════════
//  TRAJECTORY GRAPH: the flight path drawn live (height vs distance downrange),
//  with the goal height as a dashed line. Cheap: one small texture redrawn twice a second.
// ═════════════════════════════════════════════════════════════════════════════
public class AscendTrajectoryGraph : MonoBehaviour
{
    const int W = 552, H = 250;
    Texture2D tex;
    Color32[] px;
    RawImage img;
    TextMeshProUGUI xMax, yMax, readout;
    readonly List<Vector2> pts = new List<Vector2>();   // (downrange km, altitude km)
    float goalKm = 100f, lastT = -1f, redrawAt;
    static readonly Color32 Bg = new Color32(0, 0, 0, 0), Grid = new Color32(255, 255, 255, 28), Goal = new Color32(255, 200, 80, 170),
                            Path = new Color32(115, 215, 255, 255), Head = new Color32(255, 255, 255, 255);

    public static AscendTrajectoryGraph Build(RectTransform parent, Vector2 pos, Vector2 size, TMP_FontAsset font)
    {
        var box = MissionUI.Box(parent, "Trajectory", MissionUI.Line, MissionUI.HudBg, true);
        box.anchorMin = box.anchorMax = box.pivot = Vector2.zero;
        box.sizeDelta = size; box.anchoredPosition = pos;
        var g = box.gameObject.AddComponent<AscendTrajectoryGraph>();
        var title = MissionUI.Lbl(box, "TRAJECTORY", 15, font); title.color = MissionUI.Text;
        MissionUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(300f, 24f)); title.rectTransform.pivot = new Vector2(0f, 1f);
        var sub = MissionUI.Lbl(box, "HEIGHT  vs  DISTANCE", 11, font); sub.alignment = TextAlignmentOptions.Right;
        MissionUI.Place(sub.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -14f), new Vector2(300f, 22f)); sub.rectTransform.pivot = new Vector2(1f, 1f);
        MissionUI.HLine(box, 42f, MissionUI.LineDim, 14f);

        var area = MissionUI.Rect("Plot", box);
        area.anchorMin = area.anchorMax = area.pivot = new Vector2(0f, 1f);
        area.anchoredPosition = new Vector2(30f, -52f); area.sizeDelta = new Vector2(W, H);
        MissionUI.Border(area, MissionUI.LineDim, 1f);
        g.img = area.gameObject.AddComponent<RawImage>(); g.img.raycastTarget = false;
        g.tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        g.px = new Color32[W * H];
        g.img.texture = g.tex;

        g.yMax = MissionUI.Lbl(box, "", 11, font); g.yMax.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.Place(g.yMax.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -56f), new Vector2(200f, 18f)); g.yMax.rectTransform.pivot = new Vector2(0f, 1f);
        g.xMax = MissionUI.Lbl(box, "", 11, font); g.xMax.alignment = TextAlignmentOptions.Right;
        MissionUI.Place(g.xMax.rectTransform, new Vector2(1f, 0f), new Vector2(-20f, 10f), new Vector2(300f, 18f)); g.xMax.rectTransform.pivot = new Vector2(1f, 0f);
        g.readout = MissionUI.Txt(box, "", 13, MissionUI.Text, FontStyles.Bold, font); g.readout.alignment = TextAlignmentOptions.Left;
        MissionUI.Place(g.readout.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 10f), new Vector2(300f, 18f)); g.readout.rectTransform.pivot = new Vector2(0f, 0f);
        g.Clear(100f);
        return g;
    }

    public void Clear(float goal)
    {
        goalKm = Mathf.Max(0.1f, goal);
        pts.Clear(); lastT = -1f;
        Redraw();
    }

    // every half second of flight time
    public void Add(float t, float downKm, float altKm)
    {
        if (lastT >= 0f && t - lastT < 0.5f) { if (Time.unscaledTime > redrawAt) Redraw(); return; }
        lastT = t;
        pts.Add(new Vector2(Mathf.Max(0f, downKm), Mathf.Max(0f, altKm)));
        if (pts.Count > 4000) pts.RemoveAt(0);
        if (Time.unscaledTime > redrawAt) Redraw();
    }

    void Redraw()
    {
        redrawAt = Time.unscaledTime + 0.5f;
        float maxX = 10f, maxY = goalKm * 1.25f;
        foreach (var p in pts) { maxX = Mathf.Max(maxX, p.x * 1.15f); maxY = Mathf.Max(maxY, p.y * 1.15f); }
        maxX = Nice(maxX); maxY = Nice(maxY);
        for (int i = 0; i < px.Length; i++) px[i] = Bg;
        // grid: 4 x 4
        for (int k = 1; k < 4; k++)
        {
            int gx = k * W / 4, gy = k * H / 4;
            for (int y = 0; y < H; y++) px[y * W + gx] = Grid;
            for (int x = 0; x < W; x++) px[gy * W + x] = Grid;
        }
        // goal height (dashed)
        int goalY = Mathf.RoundToInt(goalKm / maxY * (H - 1));
        if (goalY > 0 && goalY < H) for (int x = 0; x < W; x++) if ((x / 6) % 2 == 0) { px[goalY * W + x] = Goal; if (goalY + 1 < H) px[(goalY + 1) * W + x] = Goal; }
        // the path
        Vector2 prev = Vector2.zero; bool has = false;
        foreach (var p in pts)
        {
            var q = new Vector2(p.x / maxX * (W - 1), p.y / maxY * (H - 1));
            if (has) Line(prev, q, Path); else Dot(q, Path, 1);
            prev = q; has = true;
        }
        if (has) Dot(prev, Head, 3);
        tex.SetPixels32(px); tex.Apply(false);
        yMax.text = maxY.ToString(maxY < 10f ? "0.#" : "N0") + " KM  HIGH";
        xMax.text = maxX.ToString(maxX < 10f ? "0.#" : "N0") + " KM  DOWNRANGE";
        readout.text = pts.Count > 0 ? $"<color=#73D7FF>{pts[pts.Count - 1].y:0.0} km</color>  up   <color=#9AA0A6>/</color>  {pts[pts.Count - 1].x:0.0} km  out   <color=#FFC850>--- goal</color>" : "<color=#FFC850>--- goal height</color>";
    }

    static float Nice(float v)
    {
        float m = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(0.01f, v))));
        foreach (float s in new[] { 1f, 2f, 2.5f, 5f, 10f }) if (s * m >= v) return s * m;
        return 10f * m;
    }

    void Line(Vector2 a, Vector2 b, Color32 c)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
        for (int i = 0; i <= n; i++) Dot(Vector2.Lerp(a, b, i / (float)n), c, 1);
    }

    void Dot(Vector2 p, Color32 c, int r)
    {
        int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
        for (int y = cy - r + 1; y < cy + r; y++)
            for (int x = cx - r + 1; x < cx + r; x++)
                if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c;
    }

    void OnDestroy() { if (tex) Destroy(tex); }

    // A thin mission-control slider (track + accent fill + square handle)
    public static Slider MakeSlider(RectTransform parent, Vector2 pos, Vector2 size, float min, float max, float value)
    {
        var r = MissionUI.Rect("Slider", parent);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = size;
        var hit = r.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var track = MissionUI.Solid(r, MissionUI.LineDim, false, "Track").rectTransform;
        track.anchorMin = new Vector2(0f, 0.5f); track.anchorMax = new Vector2(1f, 0.5f); track.sizeDelta = new Vector2(0f, 3f); track.anchoredPosition = Vector2.zero;
        var fillArea = MissionUI.Rect("Fill Area", r); MissionUI.Stretch(fillArea, 0, 13, 0, 13);
        var fill = MissionUI.Solid(fillArea, MissionUI.Accent, false, "Fill"); fill.rectTransform.sizeDelta = Vector2.zero;
        var handleArea = MissionUI.Rect("Handle Area", r); MissionUI.Stretch(handleArea, 8, 0, 8, 0);
        var handle = MissionUI.Box(handleArea, "Handle", MissionUI.Text, Color.black, false, true);
        handle.sizeDelta = new Vector2(14f, 0f);
        var s = r.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform; s.handleRect = handle; s.targetGraphic = handle.GetComponent<Image>();
        s.minValue = min; s.maxValue = max; s.value = value;
        s.navigation = new Navigation { mode = Navigation.Mode.None };
        return s;
    }
}
