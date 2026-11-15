// ═════════════════════════════════════════════════════════════════════════════
//  AscendGuide.cs  -  player guidance after the briefing, until the launch.
//
//   1. GO TO YOUR COMPUTER      a floating marker over your desk computer
//   2. DESKTOP TOUR (first time) AscendCAD -> PhMail -> Mission Simulator -> Googel -> Bank
//      + a live NEXT STEP line in the computer's taskbar while you build
//   3. EVERYTHING APPROVED       (your rocket is on the pad)
//        - the office EXIT door glows, with a sign
//        - outside: a blue beacon + glowing door on MISSION CONTROL
//        - inside: a marker on the LAUNCH COMPUTER
//   4. LAUNCH COMPUTER TOUR      what every part of the launch screen does
//
//  Also: MeetingCast - the team in the meeting room and Director Reyes
//  (black ASCEND uniform, waves, talks with her hands, looks at you).
//
//  Added automatically by MissionBriefing. Cheap: a few quads, 1-2 small lights.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

public class AscendGuide : MonoBehaviour
{
    enum Step { None, GoToComputer, LeaveOffice, GoToMissionControl, UseLaunchComputer, Done }

    static bool desktopTourDone, consoleTourDone;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { desktopTourDone = consoleTourDone = false; }

    MissionBriefing mb;
    TMP_FontAsset font;
    ComputerInteraction computer;
    AscendLaunchController launch;
    Collider officeExit, mcDoor;
    GuideMarker computerMarker, consoleMarker;
    DoorGlow officeExitGlow, mcGlow;
    GameObject beacon;
    Step step = Step.None;
    string lastTip;
    float lineTimer;
    Vector3 chair;

    static readonly Color Cyan = new Color(0.35f, 0.85f, 1f);

    public void Init(MissionBriefing briefing, TMP_FontAsset f)
    {
        mb = briefing; font = f;
        chair = mb.officeChairSeat ? mb.officeChairSeat.position : mb.chairFeetPosition;

        // the desk computer (closest to the chair) and the launch computer
        float best = float.MaxValue;
        foreach (var c in FindObjectsByType<ComputerInteraction>(FindObjectsInactive.Exclude))
        {
            float d = (c.transform.position - chair).sqrMagnitude;
            if (d < best) { best = d; computer = c; }
        }
        launch = FindAnyObjectByType<AscendLaunchController>();

        // doors: the office exit (closest "Office Door" to the chair) and Mission Control's outside door (farthest from the launch computer)
        float bo = float.MaxValue, bm = -1f;
        foreach (var d in FindObjectsByType<DoorInteraction>(FindObjectsInactive.Exclude))
        {
            var col = d.GetComponent<Collider>(); if (!col) continue;
            string n = d.name.Trim();
            if (n == "Office Door") { float dd = (col.bounds.center - chair).sqrMagnitude; if (dd < bo) { bo = dd; officeExit = col; } }
            if (n == "Mission Control" && launch) { float dd = (col.bounds.center - launch.transform.position).sqrMagnitude; if (dd > bm) { bm = dd; mcDoor = col; } }
        }

        if (computer)
        {
            var r = computer.GetComponentInChildren<Renderer>();
            Vector3 top = r ? new Vector3(r.bounds.center.x, r.bounds.max.y, r.bounds.center.z) : computer.transform.position;
            computerMarker = GuideMarker.Create(top + Vector3.up * 0.55f, "YOUR COMPUTER", Cyan, font);
        }
        if (launch)
        {
            var r = launch.GetComponentInChildren<Renderer>();
            Vector3 top = r ? new Vector3(r.bounds.center.x, r.bounds.max.y, r.bounds.center.z) : launch.transform.position;
            consoleMarker = GuideMarker.Create(top + Vector3.up * 0.7f, "LAUNCH COMPUTER", Cyan, font);
        }
        if (officeExit)
        {
            // the door is thin along X or Z; its sign faces into the office (toward the chair)
            var b = officeExit.bounds;
            bool thinX = b.size.x < b.size.z;
            Vector3 n = thinX ? new Vector3(Mathf.Sign(chair.x - b.center.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(chair.z - b.center.z));
            Vector3 sign = new Vector3(b.center.x, Mathf.Min(b.max.y - 0.35f, b.min.y + 3.3f), b.center.z) + n * ((thinX ? b.extents.x : b.extents.z) + 0.12f);
            officeExitGlow = DoorGlow.Make(officeExit, sign, n, "EXIT", "TO MISSION CONTROL", Cyan, font);
        }
        if (mcDoor && launch)
        {
            var b = mcDoor.bounds;
            Vector3 away = b.center - launch.transform.position; away.y = 0f;
            Vector3 n = Mathf.Abs(away.x) > Mathf.Abs(away.z) ? new Vector3(Mathf.Sign(away.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(away.z));
            bool thinX = Mathf.Abs(n.x) > 0.5f;
            Vector3 sign = new Vector3(b.center.x, b.max.y + 0.45f, b.center.z) + n * ((thinX ? b.extents.x : b.extents.z) + 0.05f);
            mcGlow = DoorGlow.Make(mcDoor, sign, n, "MISSION CONTROL", "LAUNCH COMPUTER INSIDE", Cyan, font);
            beacon = Beacon(new Vector3(b.center.x, b.min.y, b.center.z) + n * 4f, Cyan);
        }
        Show(Step.None);

        ComputerInteraction.Opened += OnComputerOpened;
        AscendLaunchController.ConsoleOpened += OnConsoleOpened;
    }

    void OnDestroy()
    {
        ComputerInteraction.Opened -= OnComputerOpened;
        AscendLaunchController.ConsoleOpened -= OnConsoleOpened;
    }

    // ───────────────────────── what's next? ─────────────────────────
    public static bool RocketReady => AscendLaunchSite.Current;

    // One short line for the computer's taskbar
    public static string NextStep()
    {
        if (RocketReady) return "NEXT:  ALL APPROVED!  CLOSE THE COMPUTER AND GO TO MISSION CONTROL";
        if (AscendTrajectoryControl.Pending) return "NEXT:  MISSION CONTROL IS CHECKING YOUR FLIGHT PATH  -  WATCH PHMAIL";
        if (AscendTrajectoryControl.ConfirmedPlan != null && !AscendMissionControl.HasApproved) return "NEXT:  BUILD AND SUBMIT A NEW DESIGN IN ASCENDCAD";
        if (AscendMissionControl.HasApproved) return "NEXT:  DESIGN APPROVED!  PLAN THE FLIGHT IN THE MISSION SIMULATOR AND SEND IT";
        if (AscendMissionControl.Pending) return "NEXT:  YOUR DESIGN IS BEING REVIEWED  -  CHECK PHMAIL";
        return "NEXT:  BUILD YOUR ROCKET + SATELLITE IN ASCENDCAD, THEN PRESS SUBMIT";
    }

    void Update()
    {
        if (!mb) return;
        Step want = Step.None;
        if (AscendLaunchController.HasLaunched) want = Step.Done;
        else if (MissionBriefing.Progress == MissionBriefing.Stage.Free)
        {
            var p = PlayerPos();
            if (!RocketReady) want = Step.GoToComputer;
            else if (Flat(p - chair) < 15f) want = Step.LeaveOffice;
            else if (launch && Flat(p - launch.transform.position) < 25f) want = Step.UseLaunchComputer;
            else want = Step.GoToMissionControl;
        }
        if (want != step) Show(want);
        if (step != Step.None && step != Step.Done) KeepTip();

        // live NEXT STEP in the computer's taskbar
        lineTimer -= Time.unscaledDeltaTime;
        if (lineTimer <= 0f && computer)
        {
            lineTimer = 0.5f;
            computer.SetGuideLine(MissionBriefing.Progress == MissionBriefing.Stage.Free && !AscendLaunchController.HasLaunched ? NextStep() : "");
        }
        // the desk marker hides while you use the computer
        if (computerMarker) computerMarker.gameObject.SetActive(step == Step.GoToComputer && !(computer && computer.IsOpen));
        if (consoleMarker) consoleMarker.gameObject.SetActive(step == Step.UseLaunchComputer && !(launch && launch.ConsoleIsOpen));
    }

    void Show(Step s)
    {
        step = s;
        if (officeExitGlow) officeExitGlow.On = s == Step.LeaveOffice;
        if (mcGlow) mcGlow.On = s == Step.GoToMissionControl;
        if (beacon) beacon.SetActive(s == Step.GoToMissionControl || s == Step.LeaveOffice);
        switch (s)
        {
            case Step.GoToComputer: Tip("Go to your <b>computer</b> (it has a blue marker) and press <b>E</b>."); break;
            case Step.LeaveOffice: Tip("<b>Everything is approved!</b> Your rocket is on the launch pad.\nLeave the office through the glowing <b>EXIT</b> door."); break;
            case Step.GoToMissionControl: Tip("Walk to <b>Mission Control</b>. Follow the blue light and press <b>E</b> at its door."); break;
            case Step.UseLaunchComputer: Tip("Find the <b>Launch Computer</b> (blue marker) and press <b>E</b>."); break;
            case Step.Done: Tip(""); break;
        }
    }

    void Tip(string t)
    {
        if (t == lastTip) return;
        lastTip = t;
        MissionBriefing.Hint(t, string.IsNullOrEmpty(t) ? 0.01f : 0f);
    }

    // another message replaced our tip? put the guide's tip back after a moment
    float tipCheck;
    void KeepTip()
    {
        tipCheck -= Time.unscaledDeltaTime;
        if (tipCheck > 0f || string.IsNullOrEmpty(lastTip)) return;
        tipCheck = 2.5f;
        if (MissionBriefing.CurrentTip != lastTip && !(computer && computer.IsOpen)) { string t = lastTip; lastTip = null; Tip(t); }
    }

    Vector3 PlayerPos()
    {
        var pc = PlayerController.Instance;
        if (pc) return pc.transform.position;
        var cam = Camera.main;
        return cam ? cam.transform.position : chair;
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    // ───────────────────────── tours ─────────────────────────
    void OnComputerOpened(ComputerInteraction ci)
    {
        if (desktopTourDone || ci != computer || MissionBriefing.Progress != MissionBriefing.Stage.Free) return;
        var steps = new List<GuideTour.Stop>();
        void Add(string app, string title, string body) { if (ci.AppIcon(app)) steps.Add(new GuideTour.Stop(() => ci.AppIcon(app), title, body)); }
        Add(ci.ascendCADAppName, "STEP 1   //   ASCENDCAD",
            "Design your <b>rocket</b> (Rocket Builder) and your <b>satellite</b> (Payload Builder) here. Stay inside your fund, then press <b>SUBMIT</b> to send it to Mission Control.");
        Add(ci.mailAppName, "STEP 2   //   PHMAIL",
            "Check your email. Mission Control answers here: <b>APPROVED</b>, or what you need to fix first.");
        Add(ci.simulatorAppName, "STEP 3   //   MISSION SIMULATOR",
            "When your design is approved, plan the flight path to orbit here and send it. When it is <b>CONFIRMED</b>, your rocket is built on the launch pad.");
        Add("Googel", "NEED HELP?   //   GOOGEL",
            "Search rocket parts and space facts. Great when you are stuck.");
        Add(ci.bankAppName, "YOUR MONEY   //   ASCEND BANK",
            "See your mission fund, what you spent and what is left.");
        steps.Add(new GuideTour.Stop(null, "YOU'RE READY, MISSION LEAD",
            "Your next step always shows in the <b>taskbar</b> at the bottom of the screen.\nStart with <b>AscendCAD</b>!"));
        GuideTour.Run(steps, 1100, font, () => ci && ci.IsOpen, done => { if (done) desktopTourDone = true; });
    }

    void OnConsoleOpened(AscendLaunchController lc)
    {
        if (consoleTourDone) return;
        var steps = new List<GuideTour.Stop>
        {
            new GuideTour.Stop(null, "THE LAUNCH COMPUTER",
                "This is where your mission begins. Here is what everything does."),
            new GuideTour.Stop(() => lc.TourVehicle, "YOUR ROCKET",
                "The rocket on the pad, exactly as Mission Control approved it: how many stages, how tall, how wide and how heavy at liftoff."),
            new GuideTour.Stop(() => lc.TourPayload, "YOUR SATELLITE",
                "The payload: what your rocket carries to space. It rides on top, inside the nose cone (the fairing)."),
            new GuideTour.Stop(() => lc.TourInfo, "ROCKET INFO",
                "Open this for all the numbers: engine thrust, delta-v (how much speed your rocket can gain), every stage and your flight path."),
            new GuideTour.Stop(() => lc.TourTMinus, "T-MINUS",
                "The countdown. Type how many seconds (3 to 60) to count down before liftoff."),
            new GuideTour.Stop(() => lc.TourLaunch, "LAUNCH",
                "When you are ready, press <b>LAUNCH</b>. The countdown starts and your rocket flies!"),
            new GuideTour.Stop(() => lc.TourClose, "NOT READY YET?",
                "Close the screen and come back any time. Good luck, Mission Lead!"),
        };
        GuideTour.Run(steps, 3000, font, () => lc && lc.ConsoleIsOpen, done => { if (done) consoleTourDone = true; });
    }

    // ───────────────────────── world helpers ─────────────────────────
    // A tall soft column of light you can see from far away (one quad-ish cylinder, unlit)
    static GameObject Beacon(Vector3 basePos, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Guide Beacon";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = basePos + Vector3.up * 40f;
        go.transform.localScale = new Vector3(0.6f, 40f, 0.6f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = GuideMarker.Glow(new Color(c.r, c.g, c.b, 0.35f));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        go.AddComponent<GuidePulse>().mat = mr.sharedMaterial;
        go.SetActive(false);
        return go;
    }
}

// Pulses a material's alpha (beacon)
public class GuidePulse : MonoBehaviour
{
    public Material mat;
    void Update()
    {
        if (!mat) return;
        var c = mat.GetColor("_BaseColor");
        c.a = 0.18f + 0.17f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.5f));
        mat.SetColor("_BaseColor", c);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  Floating marker: a spinning diamond + label that always faces you, gently bobbing
// ═════════════════════════════════════════════════════════════════════════════
public class GuideMarker : MonoBehaviour
{
    Transform diamond, label;
    Light glow;
    Vector3 basePos;

    public static GuideMarker Create(Vector3 pos, string text, Color c, TMP_FontAsset font)
    {
        var root = new GameObject("Guide Marker - " + text);
        root.transform.position = pos;
        var m = root.AddComponent<GuideMarker>();
        m.basePos = pos;

        var d = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(d.GetComponent<Collider>());
        d.name = "Diamond";
        d.transform.SetParent(root.transform, false);
        d.transform.localScale = new Vector3(0.22f, 0.22f, 1f);
        var mr = d.GetComponent<MeshRenderer>();
        mr.sharedMaterial = Glow(c);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m.diamond = d.transform;

        var lg = new GameObject("Label");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        var t = lg.AddComponent<TextMeshPro>();
        if (font) t.font = font;
        t.text = text; t.fontSize = 1.1f; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = 10f; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 200);
        t.rectTransform.sizeDelta = new Vector2(3f, 0.4f);
        m.label = lg.transform;

        var l = new GameObject("Glow").AddComponent<Light>();
        l.transform.SetParent(root.transform, false);
        l.type = LightType.Point; l.color = c; l.range = 2.5f; l.intensity = 1f; l.shadows = LightShadows.None;
        m.glow = l;
        root.SetActive(false);
        return m;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        float t = Time.time;
        transform.position = basePos + Vector3.up * (Mathf.Sin(t * 2.2f) * 0.06f);
        if (cam)
        {
            Vector3 to = transform.position - cam.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        }
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f) * Quaternion.Euler(0f, Mathf.Sin(t * 1.5f) * 25f, 0f);
        if (glow) glow.intensity = 0.6f + 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 3f));
    }

    public static Material Glow(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(sh) { name = "Guide Glow" };
        m.SetColor("_BaseColor", c);
        if (c.a < 0.99f)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
        m.SetFloat("_Cull", 0f);
        return m;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  Screen tour: dims everything except one UI element, frames it, and explains it.
//  BACK / NEXT / SKIP. Ends by itself if the screen it explains is closed.
// ═════════════════════════════════════════════════════════════════════════════
public class GuideTour : MonoBehaviour
{
    public class Stop
    {
        public Func<RectTransform> target; public string title, body;
        public Stop(Func<RectTransform> t, string title, string body) { target = t; this.title = title; this.body = body; }
    }

    List<Stop> stops;
    int index;
    Func<bool> alive;
    Action<bool> done;
    RectTransform root, frame, callout;
    Image[] dims = new Image[4];
    TextMeshProUGUI counter, title, body, nextLabel;
    Button back;
    float t;

    public static GuideTour Run(List<Stop> stops, int sortOrder, TMP_FontAsset font, Func<bool> alive, Action<bool> done)
    {
        var go = new GameObject("Guide Tour", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = sortOrder;
        var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        var tour = go.AddComponent<GuideTour>();
        tour.stops = stops; tour.alive = alive; tour.done = done;
        tour.Build(font);
        tour.Go(0);
        return tour;
    }

    void Build(TMP_FontAsset font)
    {
        root = (RectTransform)transform;
        for (int i = 0; i < 4; i++) { dims[i] = MissionUI.Solid(root, new Color(0f, 0f, 0f, 0.72f), true, "Dim"); }
        frame = MissionUI.Box(root, "Highlight", MissionUI.Accent, null, true);
        callout = MissionUI.Box(root, "Callout", MissionUI.Line, new Color(0f, 0f, 0f, 0.95f), true, true);
        callout.sizeDelta = new Vector2(560f, 300f);
        callout.pivot = new Vector2(0f, 1f); callout.anchorMin = callout.anchorMax = Vector2.zero;
        counter = MissionUI.Lbl(callout, "", 14, font); counter.color = MissionUI.Accent;
        MissionUI.Place(counter.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -20f), new Vector2(500f, 24f)); counter.rectTransform.pivot = new Vector2(0f, 1f);
        title = MissionUI.Txt(callout, "", 24, Color.white, FontStyles.Bold, font); title.characterSpacing = 4f;
        MissionUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -50f), new Vector2(510f, 34f)); title.rectTransform.pivot = new Vector2(0f, 1f);
        title.enableAutoSizing = true; title.fontSizeMax = 24f; title.fontSizeMin = 14f; title.textWrappingMode = TextWrappingModes.NoWrap;
        body = MissionUI.Txt(callout, "", 19, MissionUI.Text, FontStyles.Normal, font); body.lineSpacing = 12f;
        MissionUI.Place(body.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -94f), new Vector2(510f, 140f)); body.rectTransform.pivot = new Vector2(0f, 1f);

        back = MissionUI.Btn(callout, "BACK", 15, MissionUI.LineDim, () => Go(index - 1), 110, 40, font);
        MissionUI.Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(26f, 22f), new Vector2(110, 40)); ((RectTransform)back.transform).pivot = new Vector2(0f, 0f);
        var skip = MissionUI.Btn(callout, "SKIP TOUR", 15, MissionUI.LineDim, () => Finish(true), 150, 40, font);
        MissionUI.Place((RectTransform)skip.transform, new Vector2(0f, 0f), new Vector2(150f, 22f), new Vector2(150, 40)); ((RectTransform)skip.transform).pivot = new Vector2(0f, 0f);
        var next = MissionUI.Btn(callout, "NEXT", 17, MissionUI.Accent, () => Go(index + 1), 150, 40, font);
        MissionUI.Place((RectTransform)next.transform, new Vector2(1f, 0f), new Vector2(-26f, 22f), new Vector2(150, 40)); ((RectTransform)next.transform).pivot = new Vector2(1f, 0f);
        nextLabel = next.GetComponentInChildren<TextMeshProUGUI>();
    }

    void Go(int i)
    {
        if (i >= stops.Count) { Finish(true); return; }
        index = Mathf.Max(0, i);
        var s = stops[index];
        counter.text = (index + 1) + " / " + stops.Count + "   //   GUIDE";
        title.text = s.title; body.text = s.body;
        nextLabel.text = index == stops.Count - 1 ? "GOT IT" : "NEXT";
        back.gameObject.SetActive(index > 0);
        t = 0f;
        Layout();
    }

    void Finish(bool completed)
    {
        done?.Invoke(completed);
        done = null;
        Destroy(gameObject);
    }

    void Update()
    {
        if (alive != null && !alive()) { Finish(false); return; }
        t += Time.unscaledDeltaTime;
        Layout();
        // pulse the highlight frame
        float p = 0.5f + 0.5f * Mathf.Sin(t * 5f);
        foreach (var img in frame.GetComponentsInChildren<Image>()) if (img.transform != frame) img.color = Color.Lerp(MissionUI.Accent, Color.white, p * 0.6f);
    }

    void Layout()
    {
        Vector2 size = root.rect.size;
        RectTransform target = stops[index].target?.Invoke();
        Rect r;
        if (target && target.gameObject.activeInHierarchy)
        {
            var c = new Vector3[4]; target.GetWorldCorners(c);
            Vector2 a = ToLocal(c[0]), b = ToLocal(c[2]);
            r = Rect.MinMaxRect(a.x - 6f, a.y - 6f, b.x + 6f, b.y + 6f);
            frame.gameObject.SetActive(true);
        }
        else { r = new Rect(size.x * 0.5f, size.y * 0.5f, 0f, 0f); frame.gameObject.SetActive(false); }

        // four dim rects around the hole
        SetRect(dims[0].rectTransform, 0f, r.yMax, size.x, size.y);            // above
        SetRect(dims[1].rectTransform, 0f, 0f, size.x, r.yMin);                // below
        SetRect(dims[2].rectTransform, 0f, r.yMin, r.xMin, r.yMax);            // left
        SetRect(dims[3].rectTransform, r.xMax, r.yMin, size.x, r.yMax);        // right
        frame.anchorMin = frame.anchorMax = Vector2.zero; frame.pivot = Vector2.zero;
        frame.anchoredPosition = r.position; frame.sizeDelta = r.size;

        // callout: to the right of the target (or left / below if there is no room), kept on screen
        Vector2 cs = callout.sizeDelta;
        Vector2 pos;   // (top-left corner of the callout)
        if (r.width < 1f) pos = new Vector2((size.x - cs.x) * 0.5f, (size.y + cs.y) * 0.5f);
        else if (r.xMax + 24f + cs.x < size.x - 16f) pos = new Vector2(r.xMax + 24f, r.yMax);                  // right
        else if (r.xMin - 24f - cs.x > 16f) pos = new Vector2(r.xMin - 24f - cs.x, r.yMax);                    // left
        else if (r.yMin - 24f - cs.y > 16f) pos = new Vector2(r.center.x - cs.x * 0.5f, r.yMin - 24f);         // below
        else pos = new Vector2(r.center.x - cs.x * 0.5f, r.yMax + 24f + cs.y);                                 // above
        pos.x = Mathf.Clamp(pos.x, 16f, size.x - cs.x - 16f);
        pos.y = Mathf.Clamp(pos.y, cs.y + 16f, size.y - 16f);
        callout.anchoredPosition = pos;
    }

    Vector2 ToLocal(Vector3 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var p);
        return p + root.rect.size * 0.5f;   // root pivot is the centre: make it bottom-left based
    }

    static void SetRect(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x0, y0);
        rt.sizeDelta = new Vector2(Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  The meeting room: the team around the table and Director Reyes, who briefs you
// ═════════════════════════════════════════════════════════════════════════════
public class MeetingCast : MonoBehaviour
{
    readonly List<CastMember> team = new List<CastMember>();
    CastMember director;
    float nextChatter;

    public static MeetingCast Spawn(MissionBriefing mb, Vector3 room, float facingY, TMP_FontAsset font)
    {
        var go = new GameObject("Meeting Cast");
        var cast = go.AddComponent<MeetingCast>();
        Vector3 fwd = Quaternion.Euler(0f, facingY, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, fwd);

        // Director Reyes: in front of you, at the head of the table, facing you
        var d = mb.director;
        if (d != null && d.model)
        {
            Vector3 p = Ground(room + fwd * mb.directorDistance + side * mb.directorSide);
            cast.director = CastMember.Make(go.transform, d, p, Quaternion.LookRotation(Flat(room - p)), true, font);
        }
        Vector3 focus = cast.director ? cast.director.transform.position : room + fwd * 2f;

        // The team: seated on the meeting room's chairs, facing the table, turned toward the director
        var actors = mb.team != null && mb.team.Length > 0 ? mb.team : null;
        var chairs = FindChairs(room, 22f);
        if (actors != null && chairs.Count > 0)
        {
            // the table runs between the two rows of chairs
            Vector3 mid = Vector3.zero; foreach (var c in chairs) mid += c.pos; mid /= chairs.Count;
            // closest to you first, both sides, skipping the chairs right next to the director
            chairs.Sort((x, y) => Flat(x.pos - room).sqrMagnitude.CompareTo(Flat(y.pos - room).sqrMagnitude));
            int n = Mathf.Max(0, mb.teamSize), placed = 0;
            for (int i = 0; i < chairs.Count && placed < n; i++)
            {
                var c = chairs[i];
                if (Flat(c.pos - focus).magnitude < 1.3f) continue;
                var a = actors[placed % actors.Length];
                if (a == null || !a.model) { placed++; continue; }
                Vector3 toTable = Vector3.Project(mid - c.pos, side);
                if (toTable.sqrMagnitude < 0.01f) toTable = Flat(mid - c.pos);
                Vector3 p = new Vector3(c.pos.x, c.floor, c.pos.z);
                var m = CastMember.Make(go.transform, a, p, Quaternion.LookRotation(toTable.normalized), false, font);
                m.SitOn(c.seatY);
                m.lookTarget = focus + Vector3.up * 1.55f;
                m.Offset(placed * 0.37f);
                cast.team.Add(m);
                placed++;
            }
        }
        return cast;
    }

    struct Chair { public Vector3 pos; public float seatY, floor; }

    // The meeting room model's chairs: the seat + back pieces are about 0.85 m tall and 0.6-0.8 m wide
    static List<Chair> FindChairs(Vector3 room, float radius)
    {
        var list = new List<Chair>();
        var seen = new HashSet<Transform>();
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            var t = r.transform.parent;
            if (!t || !t.name.StartsWith("3DGeom~3_Defintion") || !seen.Add(t)) continue;
            Bounds b = r.bounds;
            foreach (var r2 in t.GetComponentsInChildren<Renderer>()) b.Encapsulate(r2.bounds);
            if (Flat(b.center - room).magnitude > radius || b.size.y < 0.6f || b.size.y > 1.2f) continue;
            float floor = Ground(b.center).y;
            if (floor > b.min.y) floor = b.min.y - 0.35f;
            list.Add(new Chair { pos = b.center, seatY = b.min.y + 0.12f, floor = floor });
        }
        return list;
    }

    // Called by the briefing on every new line
    public void OnLine(int line)
    {
        if (director) director.Gesture(line == 0 ? CastMember.Wave : CastMember.Talk);
    }

    public void Speaking(bool on) { if (director) director.speaking = on; }

    void Update()
    {
        var cam = Camera.main;
        if (director && cam) director.lookTarget = cam.transform.position;
        // the team sits still and listens (no gestures)
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 0.0001f ? Vector3.forward : v; }

    static Vector3 Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        return p;
    }
}

// One person: idle / talk / wave animation, head that turns to look, optional uniform + badge
public class CastMember : MonoBehaviour
{
    public const int Idle = 0, Talk = 1, Wave = 2;
    public Vector3 lookTarget;
    public bool speaking;

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly float[] w = { 1f, 0f, 0f };
    readonly float[] len = { 1f, 1f, 1f };
    int gesture = Idle; float gestureEnd;
    Transform head, neck;
    Quaternion headRest, neckRest;
    float phase, nodTimer;
    // seated pose (built from the leg bones: there is no sitting animation)
    bool seated; float seatY; bool seatPlaced;
    Transform hips, upL, upR, lowL, lowR;
    Quaternion upLRest, upRRest, lowLRest, lowRRest;

    public void SitOn(float seatHeight)
    {
        seated = true; seatY = seatHeight;
        foreach (var t in GetComponentsInChildren<Transform>())
            switch (t.name)
            {
                case "Hips": hips = t; break;
                case "UpperLeg.L": upL = t; upLRest = t.localRotation; break;
                case "UpperLeg.R": upR = t; upRRest = t.localRotation; break;
                case "LowerLeg.L": lowL = t; lowLRest = t.localRotation; break;
                case "LowerLeg.R": lowR = t; lowRRest = t.localRotation; break;
            }
    }

    public static CastMember Make(Transform parent, MissionBriefing.Actor a, Vector3 pos, Quaternion rot, bool isDirector, TMP_FontAsset font)
    {
        var g = Instantiate(a.model, pos, rot, parent);
        g.name = isDirector ? "Director Reyes" : "Team Member";
        foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
        // fit height
        var rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); g.transform.localScale *= a.height / Mathf.Max(0.1f, b.size.y); }
        var m = g.AddComponent<CastMember>();
        foreach (var t in g.GetComponentsInChildren<Transform>()) { if (t.name == "Head") { m.head = t; m.headRest = t.localRotation; } else if (t.name == "Neck") { m.neck = t; m.neckRest = t.localRotation; } }

        var anim = g.GetComponentInChildren<Animator>(); if (!anim) anim = g.AddComponent<Animator>();
        anim.applyRootMotion = false;
        m.graph = PlayableGraph.Create(g.name);
        var output = AnimationPlayableOutput.Create(m.graph, "Body", anim);
        m.mixer = AnimationMixerPlayable.Create(m.graph, 3);
        output.SetSourcePlayable(m.mixer);
        var clips = new[] { a.idle, a.talk ? a.talk : a.idle, a.wave ? a.wave : (a.talk ? a.talk : a.idle) };
        for (int i = 0; i < 3; i++)
        {
            if (!clips[i]) continue;
            var cp = AnimationClipPlayable.Create(m.graph, clips[i]);
            m.graph.Connect(cp, 0, m.mixer, i);
            m.len[i] = Mathf.Max(0.3f, clips[i].length);
        }
        m.mixer.SetInputWeight(0, 1f);
        m.graph.Play();
        if (isDirector) Uniform(g, font);
        return m;
    }

    public void Offset(float seconds) { phase = seconds; if (mixer.IsValid() && mixer.GetInput(0).IsValid()) mixer.GetInput(0).SetTime(seconds); }

    public void Gesture(int g)
    {
        if (!mixer.IsValid() || !mixer.GetInput(g).IsValid()) return;
        gesture = g;
        mixer.GetInput(g).SetTime(0f);
        gestureEnd = Time.time + len[g] * (g == Wave ? 1.6f : 1f);
    }

    void Update()
    {
        // start every frame from the rest pose: bones the animation doesn't move would otherwise keep
        // our head turn / leg bend from the last frame and spin around
        if (head) head.localRotation = headRest;
        if (neck) neck.localRotation = neckRest;
        if (seated)
        {
            if (upL) upL.localRotation = upLRest; if (upR) upR.localRotation = upRRest;
            if (lowL) lowL.localRotation = lowLRest; if (lowR) lowR.localRotation = lowRRest;
        }
        if (!mixer.IsValid()) return;
        int want = Time.time < gestureEnd ? gesture : Idle;
        // while the director is speaking, she keeps talking with her hands
        if (speaking && want == Idle && mixer.GetInput(Talk).IsValid()) { Gesture(Talk); want = Talk; }
        float k = 1f - Mathf.Exp(-Time.deltaTime * 6f);
        float sum = 0f;
        for (int i = 0; i < 3; i++) { w[i] = Mathf.Lerp(w[i], i == want ? 1f : 0f, k); sum += w[i]; }
        for (int i = 0; i < 3; i++) if (mixer.GetInput(i).IsValid()) mixer.SetInputWeight(i, w[i] / Mathf.Max(0.001f, sum));
    }

    // body language: the head turns to whoever they look at, with small nods while listening / talking
    void LateUpdate()
    {
        if (seated) Sit();
        if (!head) return;
        Vector3 body = transform.forward;
        Vector3 to = lookTarget - head.position;
        if (to.sqrMagnitude < 0.01f) return;
        Vector3 flatTo = to; flatTo.y = 0f;
        float yaw = Mathf.Clamp(Vector3.SignedAngle(body, flatTo, Vector3.up), -55f, 55f);
        float pitch = Mathf.Clamp(-Mathf.Atan2(to.y, flatTo.magnitude) * Mathf.Rad2Deg, -25f, 25f);
        nodTimer += Time.deltaTime;
        float nod = Mathf.Sin(nodTimer * (speaking ? 5f : 1.3f) + phase) * (speaking ? 3.5f : 1.5f);
        Vector3 right = Quaternion.AngleAxis(yaw, Vector3.up) * transform.right;
        Quaternion turn = Quaternion.AngleAxis(pitch + nod, right) * Quaternion.AngleAxis(yaw, Vector3.up);
        if (neck) neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.35f) * neck.rotation;
        head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.65f) * head.rotation;
    }

    void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }

    // thighs forward, shins down; the first frame lowers the whole body so the hips rest on the seat
    void Sit()
    {
        Vector3 right = transform.right;
        Quaternion thigh = Quaternion.AngleAxis(-88f, right), knee = Quaternion.AngleAxis(88f, right);
        if (upL) upL.rotation = thigh * upL.rotation;
        if (upR) upR.rotation = thigh * upR.rotation;
        if (lowL) lowL.rotation = knee * lowL.rotation;
        if (lowR) lowR.rotation = knee * lowR.rotation;
        if (!seatPlaced && hips)
        {
            seatPlaced = true;
            transform.position += Vector3.up * (seatY + 0.06f - hips.position.y) - transform.forward * 0.12f;
        }
    }

    // Black ASCEND uniform: dark suit, cyan undershirt, "ASCEND" badge on the chest
    static void Uniform(GameObject g, TMP_FontAsset font)
    {
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i].name.ToLowerInvariant();
                Color c;
                if (n.StartsWith("black")) c = new Color(0.025f, 0.027f, 0.032f);
                else if (n.StartsWith("white")) c = new Color(0.06f, 0.42f, 0.55f);
                else if (n.StartsWith("brown") && r.name.Contains("Feet")) c = new Color(0.02f, 0.02f, 0.02f);
                else continue;
                mats[i].color = c;
                if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c);
                if (mats[i].HasProperty("_Smoothness")) mats[i].SetFloat("_Smoothness", 0.45f);
            }
            r.materials = mats;
        }
        Transform chest = null;
        foreach (var t in g.GetComponentsInChildren<Transform>()) if (t.name == "Chest") { chest = t; break; }
        if (!chest) return;
        var badge = new GameObject("ASCEND Badge");
        badge.transform.SetParent(chest, false);
        float inv = 1f / Mathf.Max(0.0001f, chest.lossyScale.x);
        badge.transform.position = chest.position + g.transform.forward * 0.16f + g.transform.right * -0.08f + Vector3.up * 0.08f;
        badge.transform.rotation = Quaternion.LookRotation(-g.transform.forward, Vector3.up);
        badge.transform.localScale = Vector3.one * inv;
        var t2 = badge.AddComponent<TextMeshPro>();
        if (font) t2.font = font;
        t2.text = "ASCEND"; t2.fontSize = 0.32f; t2.color = new Color(0.55f, 0.9f, 1f); t2.alignment = TextAlignmentOptions.Center;
        t2.characterSpacing = 8f; t2.textWrappingMode = TextWrappingModes.NoWrap;
        t2.rectTransform.sizeDelta = new Vector2(0.4f, 0.06f);
    }
}
