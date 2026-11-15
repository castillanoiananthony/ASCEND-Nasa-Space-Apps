// ═════════════════════════════════════════════════════════════════════════════
//  AscendTutorial.cs  -  the TUTORIAL WORLD (Main Menu > Single Player > TUTORIAL)
//
//  You sit at your desk with AscendCAD open and the ASCEND ACADEMY on top:
//    1  HOW ASCENDCAD WORKS     the screen, the controls, the mission flow
//    2  BUILD A ROCKET          step by step + the rules that matter
//    3  BUILD A SATELLITE       step by step + the [OK] checklist
//    4  ROCKET MAP              Falcon 9 in 3D: click a part to see it and learn it
//    5  SATELLITE MAP           Diwata-2 in 3D, same idea
//    6  WATER BOTTLE ROCKET     build a real bottle rocket, launch it in a simple
//                               physics lab and find the best water fill
//    START BUILDING             closes the academy: AscendCAD is yours (sandbox)
//  The ACADEMY button (top of the screen) brings it back. EXIT TUTORIAL -> Main Menu.
//
//  Based on the reference tutorial in ComputerInteraction.zip, reorganised into lessons.
//  Cheap: one 800x800 render texture, two lights that only light this view.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class AscendTutorial : MonoBehaviour
{
    // ───────────────────────── start-up ─────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded; Try(); }
    static void OnLoaded(Scene s, LoadSceneMode m) => Try();
    static void Try()
    {
        if (!GameFlow.PlayTutorial || FindAnyObjectByType<AscendTutorial>() || !FindAnyObjectByType<ComputerInteraction>()) return;
        new GameObject("Ascend Tutorial").AddComponent<AscendTutorial>();
    }

    ComputerInteraction computer;
    AscendCADData data;
    TMP_FontAsset font;
    GameObject canvasGo, academy, reopenBtn;
    RectTransform content;
    readonly List<(Button btn, GameObject page)> lessons = new List<(Button, GameObject)>();
    int lesson = -1;

    IEnumerator Start()
    {
        // let the scene's own scripts start first, then skip the story
        yield return null; yield return null;
        MissionBriefing.DevSkipBriefing(1);
        AscendGuide.DevSkipTours();
        MissionBriefing.SitAtDesk();
        IntroStory.Remove();
        var loading = GameObject.Find("Tutorial Loading"); if (loading) Destroy(loading);

        computer = FindAnyObjectByType<ComputerInteraction>();
        data = AscendCADData.Resolve(computer ? computer.ascendCAD : null);
        font = data && data.labelFont ? data.labelFont : AUI.Font;
        if (font && !AUI.Font) AUI.Font = font;
        if (computer) { computer.Open(); computer.OpenAppByName(computer.ascendCADAppName); }
        yield return null;
        MissionBriefing.Hint("", 0.01f);
        BuildUI();
        ShowAcademy(true);
        Go(0);
    }

    void OnDestroy()
    {
        ComputerInteraction.InputBlocked = false;
        RenderPipelineManager.beginCameraRendering -= OnBeginCam;
        RenderPipelineManager.endCameraRendering -= OnEndCam;
        if (rt) rt.Release();
        if (studio) Destroy(studio);
        if (canvasGo) Destroy(canvasGo);
    }

    void Update()
    {
        bool cOpen = computer && computer.IsOpen;
        if (reopenBtn) reopenBtn.SetActive(cOpen && academy && !academy.activeSelf);
        if (academy && academy.activeSelf && !cOpen) ShowAcademy(false);
        if (studioCam) studioCam.enabled = academy && academy.activeSelf && (lesson == 3 || lesson == 4 || lesson == 5);
        UpdateStudio();
        UpdateLab();
    }

    void ShowAcademy(bool on)
    {
        if (!academy) return;
        academy.SetActive(on);
        ComputerInteraction.InputBlocked = on;
        if (on && lesson >= 0) Go(lesson);
    }

    void ExitTutorial()
    {
        GameFlow.PlayTutorial = false;
        ComputerInteraction.InputBlocked = false;
        if (computer && computer.IsOpen) computer.Close();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        SceneManager.LoadScene("Main Menu");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI
    // ═════════════════════════════════════════════════════════════════════════
    static readonly Vector2 Size = new Vector2(1800, 1000);

    void BuildUI()
    {
        canvasGo = new GameObject("Ascend Academy", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var cv = canvasGo.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 1100;
        var sc = canvasGo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)canvasGo.transform;

        // reopen button (shown while you build)
        var rb = MissionUI.Btn(root, "ACADEMY  //  TUTORIAL", 16, MissionUI.Accent, () => ShowAcademy(true), 300, 44, font);
        var rbt = (RectTransform)rb.transform; rbt.anchorMin = rbt.anchorMax = rbt.pivot = new Vector2(0.5f, 1f); rbt.anchoredPosition = new Vector2(0f, -6f); rbt.sizeDelta = new Vector2(300f, 44f);
        rb.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
        reopenBtn = rb.gameObject; reopenBtn.SetActive(false);

        var dim = MissionUI.Solid(root, new Color(0.01f, 0.015f, 0.03f, 0.94f), true, "Academy");
        MissionUI.Stretch(dim.rectTransform);
        academy = dim.gameObject;
        var panel = MissionUI.Box(dim.transform, "Panel", MissionUI.Line, Color.black, true, true);
        MissionUI.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, Size);
        MissionUI.Header(panel, "ASCEND ACADEMY", "TUTORIAL WORLD   //   LEARN ASCENDCAD", 72f, 0f, font);

        // left: lessons
        var nav = MissionUI.Rect("Lessons", panel);
        nav.anchorMin = new Vector2(0f, 0f); nav.anchorMax = new Vector2(0f, 1f); nav.pivot = new Vector2(0f, 1f);
        nav.offsetMin = new Vector2(24f, 24f); nav.offsetMax = new Vector2(24f + 330f, -96f);
        var navT = MissionUI.Lbl(nav, "LESSONS", 14, font); navT.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.Place(navT.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(330f, 24f)); navT.rectTransform.pivot = new Vector2(0f, 1f);
        MissionUI.Solid(panel, MissionUI.LineDim, false, "Divider").rectTransform.Let(r => { r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f); r.offsetMin = new Vector2(372f, 24f); r.offsetMax = new Vector2(373f, -96f); });

        content = MissionUI.Rect("Lesson", panel);
        content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(396f, 24f); content.offsetMax = new Vector2(-24f, -96f);

        string[] names = { "01   HOW ASCENDCAD WORKS", "02   BUILD A ROCKET", "03   BUILD A SATELLITE", "04   ROCKET MAP  (3D)", "05   SATELLITE MAP  (3D)", "06   WATER BOTTLE ROCKET" };
        var pages = new GameObject[] { PageHowItWorks(), PageRocket(), PageSatellite(), PageMap(false), PageMap(true), PageWaterLab() };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var b = MissionUI.Btn(nav, names[i], 16, MissionUI.LineDim, () => Go(k), 330, 54, font);
            var t = (RectTransform)b.transform; t.anchorMin = t.anchorMax = t.pivot = new Vector2(0f, 1f);
            t.anchoredPosition = new Vector2(0f, -40f - i * 66f); t.sizeDelta = new Vector2(330f, 54f);
            var lbl = b.GetComponentInChildren<TextMeshProUGUI>(); lbl.alignment = TextAlignmentOptions.MidlineLeft; lbl.rectTransform.offsetMin = new Vector2(18f, 0f);
            pages[i].SetActive(false);
            lessons.Add((b, pages[i]));
        }
        var start = MissionUI.Btn(nav, "START BUILDING  >>", 19, MissionUI.Go, () => ShowAcademy(false), 330, 64, font);
        var st = (RectTransform)start.transform; st.anchorMin = st.anchorMax = st.pivot = new Vector2(0f, 0f); st.anchoredPosition = new Vector2(0f, 80f); st.sizeDelta = new Vector2(330f, 64f);
        var exit = MissionUI.Btn(nav, "EXIT TUTORIAL", 16, MissionUI.LineDim, ExitTutorial, 330, 50, font);
        var ex = (RectTransform)exit.transform; ex.anchorMin = ex.anchorMax = ex.pivot = new Vector2(0f, 0f); ex.anchoredPosition = Vector2.zero; ex.sizeDelta = new Vector2(330f, 50f);
    }

    void Go(int i)
    {
        lesson = i;
        for (int k = 0; k < lessons.Count; k++)
        {
            lessons[k].page.SetActive(k == i);
            MissionUI.SetColor(lessons[k].btn, k == i ? MissionUI.Accent : MissionUI.LineDim);
        }
        if (i == 3) ShowModel(Model.Falcon);
        else if (i == 4) ShowModel(Model.Diwata);
        else if (i == 5) { ShowModel(Model.Water); }
    }

    // ───────────────────────── text lessons ─────────────────────────
    RectTransform Page(string name, string title, string sub)
    {
        var p = MissionUI.Rect(name, content); MissionUI.Stretch(p);
        var t = MissionUI.Txt(p, title, 30, Color.white, FontStyles.Bold, font); t.characterSpacing = 6f;
        MissionUI.Place(t.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(1300f, 40f)); t.rectTransform.pivot = new Vector2(0f, 1f);
        var s = MissionUI.Txt(p, sub, 18, MissionUI.Label, FontStyles.Normal, font);
        MissionUI.Place(s.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -46f), new Vector2(1340f, 54f)); s.rectTransform.pivot = new Vector2(0f, 1f);
        MissionUI.HLine(p, 106f, MissionUI.LineDim, 0f);
        return p;
    }

    RectTransform Steps(RectTransform page)
    {
        var body = AUI.Scroll(page, 0, 0, 0, 120);
        AUI.V(body, 0, 24, 6, 20, 22);
        return body;
    }

    void Step(RectTransform list, string num, string title, string text, Color accent)
    {
        var row = AUI.Rect("Step", list);
        AUI.H(row, 0, 0, 0, 0, 20, TextAnchor.UpperLeft);
        var n = MissionUI.Txt(row, num, 34, accent, FontStyles.Bold, font); AUI.LE(n, 80, 44);
        var col = AUI.Rect("Text", row); AUI.V(col, 0, 0, 0, 0, 6); AUI.LE(col, -1, -1, 1);
        var t = MissionUI.Txt(col, title, 22, Color.white, FontStyles.Bold, font); t.characterSpacing = 3f;
        var b = MissionUI.Txt(col, text, 18, MissionUI.Text, FontStyles.Normal, font); b.lineSpacing = 12f;
    }

    GameObject PageHowItWorks()
    {
        var p = Page("How It Works", "HOW ASCENDCAD WORKS", "AscendCAD is where you design your rocket and your satellite with real parts and a real budget. Here is the screen and the path from idea to launch.");
        var l = Steps(p);
        Step(l, "A", "LEFT  //  THE PARTS LIBRARY", "Every part you can buy: engines, tanks, structures, avionics and satellite parts. Use the <b>search box</b> or open a <b>category</b>, then click a part to pick it up.", MissionUI.Accent);
        Step(l, "B", "CENTER  //  THE 3D WORKSPACE", "Move the picked part over your design: it glows <color=#3FB950><b>GREEN</b></color> where it can snap. Click to place it.\n<b>Right-drag</b> to orbit the camera, <b>scroll</b> to zoom, <b>click</b> a placed part to select it.", MissionUI.Accent);
        Step(l, "C", "RIGHT  //  THE INSPECTOR", "Everything about the selected part: its measurements, cost and mass, <b>what it does, what it is compatible with, what it needs</b> and tips. Below it, the live <b>ROCKET / SATELLITE STATS</b> tell you if your design will work.", MissionUI.Accent);
        Step(l, "D", "TOP  //  THE BUILDERS AND TOOLS", "<b>Rocket Builder</b> and <b>Payload Builder</b> switch between your rocket and your satellite. <b>Mission Guide</b> gives ideas, <b>Prebuilt</b> loads real rockets and satellites to study, <b>Save / Load / Clear</b> manage designs.", MissionUI.Accent);
        Step(l, "1", "DESIGN", "Build the rocket and the satellite. Stay inside your fund.", MissionUI.Go);
        Step(l, "2", "SUBMIT", "Press the orange <b>SUBMIT</b> button: you pay for the parts and the design goes to the ASCEND Review Board.", MissionUI.Go);
        Step(l, "3", "CHECK PHMAIL", "The Review Board answers by email: <b>APPROVED</b>, or what to fix first.", MissionUI.Go);
        Step(l, "4", "PLAN THE FLIGHT", "Open the <b>Mission Simulator</b>, plan the path to orbit and send it. When it is <b>CONFIRMED</b>, your rocket is built on the launch pad.", MissionUI.Go);
        Step(l, "5", "LAUNCH", "Walk to Mission Control, use the Launch Computer and press LAUNCH!", MissionUI.Go);
        return p.gameObject;
    }

    GameObject PageRocket()
    {
        var p = Page("Rocket", "BUILD A ROCKET", "A rocket is a stack: engines at the bottom, fuel tanks above them, then the next stage, then the computers, then the nose cover with your satellite inside.");
        var l = Steps(p);
        Step(l, "1", "ENGINE + TANKS (STAGE 1)", "Place an engine, then stack fuel tanks on top. <b>Match the fuel</b>: Kerolox engines (Merlin 1D, Rutherford) need <b>Kerolox tanks</b>, Methalox engines need Methalox tanks.\nSymmetry above 1 under a tank makes an <b>engine cluster</b> (Falcon 9 has 9).", MissionUI.Accent);
        Step(l, "2", "INTERSTAGE + STAGE 2", "Add an <b>interstage</b> (decoupler): it lets the empty first stage fall away. Above it, the second stage: a smaller tank with a <b>vacuum engine</b> made for space.", MissionUI.Accent);
        Step(l, "3", "AVIONICS BAY", "Add an <b>Avionics Bay</b>, click it and install inside: <b>flight computer + IMU</b> (to steer), battery, power distribution, telemetry radio and the <b>flight termination system</b> (range safety needs it).", MissionUI.Accent);
        Step(l, "4", "ADAPTER + FAIRING", "Narrow rocket, wide nose cover? Put a <b>diameter adapter</b> between them. The <b>fairing</b> protects your satellite: the satellite must fit inside it <b>folded</b>.", MissionUI.Accent);
        Step(l, "!", "THE NUMBERS THAT MATTER", "<b>Thrust-to-weight (TWR) 1.2 or more</b> at liftoff, or the rocket can't leave the pad.\n<b>Delta-v about 9,400 m/s</b> to reach orbit (less for the Sky Sensor mission).\nAll of it shows live in <b>ROCKET STATS</b> on the right.", MissionUI.Caution);
        return p.gameObject;
    }

    GameObject PageSatellite()
    {
        var p = Page("Satellite", "BUILD A SATELLITE", "A satellite is a box full of systems: it must make power, think, talk to Earth, point the right way and stay at a safe temperature. Then it does its job: the camera.");
        var l = Steps(p);
        Step(l, "1", "BODY + SEPARATION", "In <b>Payload Builder</b>, place a frame (a 1U-16U CubeSat or a microsat bus like Diwata's) and snap a <b>separation system</b> under it so it can leave the rocket in orbit.", MissionUI.Accent);
        Step(l, "2", "INSIDE: POWER, BRAIN, RADIO, POINTING", "Click the body and install: an <b>EPS</b> and a <b>battery</b> (for the 36-minute shadow of each orbit), an <b>on-board computer</b>, a <b>radio</b>, and an <b>ADCS</b> so it doesn't tumble. Watch the body's <b>internal litres</b>.", MissionUI.Accent);
        Step(l, "3", "OUTSIDE: SOLAR PANELS, ANTENNA, CAMERA", "Mount <b>solar panels</b> so the power made beats the power used by at least 20%, an <b>antenna</b> for the radio, and at least one <b>instrument</b> like Diwata's telescope or multispectral imager.", MissionUI.Accent);
        Step(l, "4", "ALL [OK]", "In <b>SATELLITE STATS</b> every system must say <color=#3FB950><b>[OK]</b></color>: Structure, Separation, EPS, Computer, Radio, ADCS and Thermal. It must also be lighter than your rocket can lift and fit the fairing folded.", MissionUI.Caution);
        return p.gameObject;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  3D STUDIO (shared by the two maps and the water rocket)
    // ═════════════════════════════════════════════════════════════════════════
    enum Model { None, Falcon, Diwata, Water }
    Model shown = Model.None;
    GameObject studio, modelRoot;
    Transform pivot;
    Camera studioCam;
    Light key, fill;
    RenderTexture rt;
    float yaw = 30f, pitch = 14f, dist = 10f, baseDist = 10f, distWant = 10f; bool spin = true;
    Vector3 focusOff, focusWant;   // the camera flies to the selected part
    int layer = 31;
    readonly Dictionary<string, List<Renderer>> groups = new Dictionary<string, List<Renderer>>();
    readonly Dictionary<Renderer, Material[]> original = new Dictionary<Renderer, Material[]>();
    Material dimMat;
    readonly List<RawImage> views = new List<RawImage>();

    void EnsureStudio()
    {
        if (studio) return;
        layer = data ? data.cadLayer : 31;
        rt = new RenderTexture(800, 800, 24, RenderTextureFormat.ARGB32) { name = "Academy View", antiAliasing = 2 };
        rt.Create();
        studio = new GameObject("Academy Studio");
        studio.transform.position = new Vector3(-12000f, -6000f, 0f);
        pivot = new GameObject("Pivot").transform; pivot.SetParent(studio.transform, false);
        studioCam = new GameObject("Academy Camera").AddComponent<Camera>();
        studioCam.transform.SetParent(studio.transform, false);
        studioCam.clearFlags = CameraClearFlags.SolidColor; studioCam.backgroundColor = new Color(0.03f, 0.045f, 0.07f);
        studioCam.cullingMask = 1 << layer; studioCam.fieldOfView = 30f; studioCam.targetTexture = rt; studioCam.allowHDR = false;
        key = MakeLight("Key", Quaternion.Euler(40f, -35f, 0f), 1.4f, Color.white);
        fill = MakeLight("Fill", Quaternion.Euler(-15f, 145f, 0f), 0.6f, new Color(0.7f, 0.8f, 1f));
        RenderPipelineManager.beginCameraRendering += OnBeginCam;
        RenderPipelineManager.endCameraRendering += OnEndCam;
        dimMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Academy Dim" };
        dimMat.SetColor("_BaseColor", new Color(0.12f, 0.15f, 0.2f));
        foreach (var v in views) v.texture = rt;
    }

    Light MakeLight(string n, Quaternion r, float i, Color c)
    {
        var l = new GameObject(n).AddComponent<Light>();
        l.transform.SetParent(studio.transform, false); l.transform.rotation = r;
        l.type = LightType.Directional; l.intensity = i; l.color = c; l.shadows = LightShadows.None; l.cullingMask = 1 << layer; l.enabled = false;
        return l;
    }

    // these two lights only light the academy view
    void OnBeginCam(ScriptableRenderContext _, Camera c) { if (c == studioCam) { key.enabled = fill.enabled = true; } }
    void OnEndCam(ScriptableRenderContext _, Camera c) { if (c == studioCam) { key.enabled = fill.enabled = false; } }

    RawImage View(RectTransform parent, Vector2 pos, Vector2 size)
    {
        var box = MissionUI.Box(parent, "3D View", MissionUI.LineDim, new Color(0.03f, 0.045f, 0.07f));
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0f, 1f); box.anchoredPosition = pos; box.sizeDelta = size;
        var raw = MissionUI.Rect("Image", box); MissionUI.Stretch(raw, 1, 1, 1, 1);
        var img = raw.gameObject.AddComponent<RawImage>(); img.raycastTarget = true; img.texture = rt;
        img.uvRect = size.x > size.y ? new Rect(0f, 0.5f - size.y / size.x * 0.5f, 1f, size.y / size.x) : new Rect(0.5f - size.x / size.y * 0.5f, 0f, size.x / size.y, 1f);
        var ptr = raw.gameObject.AddComponent<AscendPointer>();
        ptr.drag = e => { spin = false; yaw += e.delta.x * 0.4f; pitch = Mathf.Clamp(pitch - e.delta.y * 0.4f, -80f, 80f); };
        ptr.scroll = e => { if (Mathf.Abs(e.scrollDelta.y) > 0.01f) distWant = Mathf.Clamp(distWant * (e.scrollDelta.y > 0 ? 0.9f : 1.1f), baseDist * 0.03f, baseDist * 2.5f); };
        var hint = MissionUI.Lbl(box, "DRAG TO TURN   //   SCROLL TO ZOOM", 11, font); hint.alignment = TextAlignmentOptions.Bottom;
        MissionUI.Stretch(hint.rectTransform, 0, 8, 0, 0);
        views.Add(img);
        return img;
    }

    void UpdateStudio()
    {
        if (!studioCam || !studioCam.enabled) return;
        if (spin) yaw += 18f * Time.unscaledDeltaTime;
        float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f);
        focusOff = Vector3.Lerp(focusOff, focusWant, k); dist = Mathf.Lerp(dist, distWant, k);
        var r = Quaternion.Euler(pitch, yaw, 0f);
        studioCam.transform.SetPositionAndRotation(pivot.position + focusOff - r * Vector3.forward * dist, r);
        studioCam.nearClipPlane = Mathf.Max(0.005f, dist * 0.01f); studioCam.farClipPlane = Mathf.Max(50f, dist * 10f);
    }

    void ShowModel(Model m)
    {
        EnsureStudio();
        if (m == shown && modelRoot) return;
        shown = m;
        if (modelRoot) Destroy(modelRoot);
        groups.Clear(); original.Clear();
        if (m == Model.Water) { modelRoot = BuildWaterRocket(); }
        else
        {
            var d = m == Model.Falcon ? AscendTemplates.Build(1, data) : AscendTemplates.BuildSat(2, data);
            mapDesign = d;
            modelRoot = AscendLaunchSite.BuildDesign(d, data, pivot, layer, false);
            // which renderers belong to which part (inside parts light up their host)
            var childOf = new Dictionary<int, Transform>();
            int child = 0;
            for (int i = 0; i < d.parts.Count; i++)
            {
                var p = data.Get(d.parts[i].partId);
                if (p == null || p.attach == CADAttach.Internal) continue;
                if (child < modelRoot.transform.childCount) childOf[i] = modelRoot.transform.GetChild(child++);
            }
            for (int i = 0; i < d.parts.Count; i++)
            {
                int host = i;
                while (!childOf.ContainsKey(host) && host >= 0 && d.parts[host].parent >= 0) host = d.parts[host].parent;
                if (!childOf.TryGetValue(host, out var t)) continue;
                AddGroup(d.parts[i].partId, t.GetComponentsInChildren<Renderer>());
            }
        }
        CADModels.SetLayer(modelRoot, layer);
        foreach (var r in modelRoot.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; original[r] = r.sharedMaterials; }
        Frame();
        if (m != Model.Water) RefreshMap(m == Model.Diwata);
    }

    void AddGroup(string key, IEnumerable<Renderer> rs)
    {
        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Renderer>();
        list.AddRange(rs);
    }

    void Frame()
    {
        var rs = modelRoot.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        modelRoot.transform.position -= b.center - pivot.position;
        float rad = Mathf.Max(0.05f, b.extents.magnitude);
        baseDist = dist = distWant = rad / Mathf.Sin(15f * Mathf.Deg2Rad) * 1.05f;
        focusOff = focusWant = Vector3.zero;
        yaw = 30f; pitch = 12f; spin = true;
    }

    void Highlight(string key)
    {
        foreach (var kv in original) if (kv.Key) kv.Key.sharedMaterials = kv.Value;
        if (string.IsNullOrEmpty(key) || !groups.TryGetValue(key, out var sel) || sel.Count == 0) { focusWant = Vector3.zero; distWant = baseDist; return; }
        // fly the camera to the selected part (small parts get close)
        Bounds bb = sel[0].bounds; foreach (var r in sel) if (r) bb.Encapsulate(r.bounds);
        focusWant = bb.center - pivot.position;
        distWant = Mathf.Clamp(bb.extents.magnitude / Mathf.Sin(15f * Mathf.Deg2Rad) * 1.5f, baseDist * (shown == Model.Water ? 0.45f : 0.06f), baseDist);
        var on = new HashSet<Renderer>(sel);
        foreach (var kv in original)
            if (kv.Key && !on.Contains(kv.Key)) { var mats = new Material[kv.Value.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = dimMat; kv.Key.sharedMaterials = mats; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ROCKET MAP / SATELLITE MAP
    // ═════════════════════════════════════════════════════════════════════════
    CADDesign mapDesign;
    readonly RectTransform[] mapLists = new RectTransform[2];
    readonly TextMeshProUGUI[] mapInfo = new TextMeshProUGUI[2];
    readonly List<Button>[] mapBtns = { new List<Button>(), new List<Button>() };

    GameObject PageMap(bool sat)
    {
        var p = Page(sat ? "Satellite Map" : "Rocket Map", sat ? "SATELLITE MAP   //   DIWATA-2" : "ROCKET MAP   //   FALCON 9",
            sat ? "Diwata-2 is a real Philippine microsatellite (2018). Click a part to light it up and learn what it does. Parts inside the body light up the body."
                : "Falcon 9 is one of the most-flown rockets in the world. Click a part to light it up and learn what it does. Parts inside the Avionics Bay light up the bay.");
        View(p, new Vector2(0f, -122f), new Vector2(640f, 738f));
        var lt = MissionUI.Lbl(p, "PARTS", 14, font); lt.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.Place(lt.rectTransform, new Vector2(0f, 1f), new Vector2(664f, -124f), new Vector2(300f, 22f)); lt.rectTransform.pivot = new Vector2(0f, 1f);
        var listBox = MissionUI.Rect("Part List", p);
        listBox.anchorMin = listBox.anchorMax = listBox.pivot = new Vector2(0f, 1f); listBox.anchoredPosition = new Vector2(664f, -150f); listBox.sizeDelta = new Vector2(300f, 710f);
        var list = AUI.Scroll(listBox, 0, 0, 0, 0); AUI.V(list, 0, 10, 0, 6, 6);
        var infoBox = MissionUI.Box(p, "Part Info", MissionUI.LineDim, new Color(0f, 0f, 0f, 0.6f));
        infoBox.anchorMin = infoBox.anchorMax = infoBox.pivot = new Vector2(0f, 1f); infoBox.anchoredPosition = new Vector2(984f, -122f); infoBox.sizeDelta = new Vector2(396f, 738f);
        var info = AUI.Scroll(infoBox, 16, 12, 10, 14); AUI.V(info, 0, 8, 0, 10, 0);
        var it = AUI.Text(info, "", 14f, MissionUI.Text); it.lineSpacing = 6f;
        int k = sat ? 1 : 0;
        mapLists[k] = list; mapInfo[k] = it;
        return p.gameObject;
    }

    void RefreshMap(bool sat)
    {
        int k = sat ? 1 : 0;
        var list = mapLists[k];
        if (!list || mapDesign == null) return;
        if (mapBtns[k].Count > 0) { Highlight(null); mapInfo[k].text = "<color=#9AA0A6>Click a part on the left.</color>\n\n" + Overview(sat); return; }
        var ids = new List<string>(); var counts = new Dictionary<string, int>();
        foreach (var pl in mapDesign.parts) { if (!counts.ContainsKey(pl.partId)) { counts[pl.partId] = 0; ids.Add(pl.partId); } counts[pl.partId]++; }
        foreach (var id in ids)
        {
            var part = data.Get(id); if (part == null) continue;
            string pid = id;
            var b = MissionUI.Btn(list, (part.attach == CADAttach.Internal ? "   " : "") + part.name + (counts[id] > 1 ? "  x" + counts[id] : ""), 13, part.attach == CADAttach.Internal ? MissionUI.LineDim : MissionUI.Line, null, -1, 38, font);
            var lbl = b.GetComponentInChildren<TextMeshProUGUI>(); lbl.alignment = TextAlignmentOptions.MidlineLeft; lbl.rectTransform.offsetMin = new Vector2(12f, 0f);
            b.onClick.AddListener(() => SelectPart(k, pid, b));
            mapBtns[k].Add(b);
        }
        mapInfo[k].text = "<color=#9AA0A6>Click a part on the left.</color>\n\n" + Overview(sat);
    }

    string Overview(bool sat)
    {
        var s = sat ? CADStats.Payload(mapDesign, data) : CADStats.Rocket(mapDesign, data, 0f, AscendMissionGuide.RequiredDeltaV(data));
        return "<b><color=#8AB4F8>THE WHOLE " + (sat ? "SATELLITE" : "ROCKET") + "</color></b>\n" + (sat ? AscendReport.PayloadStats(s, null, data) : AscendReport.RocketStats(s, null, data));
    }

    void SelectPart(int k, string id, Button self)
    {
        foreach (var b in mapBtns[k]) MissionUI.SetColor(b, b == self ? MissionUI.Accent : (b.GetComponentInChildren<TextMeshProUGUI>().text.StartsWith("   ") ? MissionUI.LineDim : MissionUI.Line));
        Highlight(id);
        spin = false;
        var p = data.Get(id);
        mapInfo[k].text = $"<size=125%><b>{p.name}</b></size>\n" + AscendReport.PartInfo(p) + AscendPartGuide.Build(p, data);
        var sr = mapInfo[k].GetComponentInParent<ScrollRect>(); if (sr) sr.verticalNormalizedPosition = 1f;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  WATER BOTTLE ROCKET LAB
    // ═════════════════════════════════════════════════════════════════════════
    float wFill = 0.33f, wBar = 4f, wAngle = 80f; int wBottles = 1, wFins = 3; bool wNose = true;
    TextMeshProUGUI labResult, labPartInfo, labValues;
    RawImage plotImg; Texture2D plot; Color32[] plotPx;
    WaterSim.Result lastRun; float runT = -1f;
    readonly List<Button> waterBtns = new List<Button>();
    const int PW = 520, PH = 300;

    static readonly (string key, string name, string text)[] WaterParts =
    {
        ("bottle", "PRESSURE BOTTLE", "A 2-litre soda bottle, upside down. It is the rocket's body <b>and</b> its tank. Soda bottles are made to hold fizzy drinks under pressure, so they can safely hold pumped-in air (keep it under about 6 bar)."),
        ("water", "WATER  (REACTION MASS)", "The 'fuel'. Squeezed air pushes the water out of the nozzle very fast; pushing the water <b>down</b> pushes the rocket <b>up</b> (Newton's third law). Too little water: nothing to push. Too much: no room for air and a heavy rocket. About <b>one third full</b> is usually best."),
        ("nozzle", "NOZZLE  (BOTTLE NECK)", "The 22 mm opening of the bottle. All the water rushes out here, like the nozzle of a real rocket engine. Real rockets push hot gas instead of water."),
        ("air", "COMPRESSED AIR", "The energy store. A bicycle pump squeezes air into the bottle. When the rocket is released, the air expands and shoves the water out: about 1 second of thrust."),
        ("fins", "FINS", "Cardboard or plastic fins at the bottom keep the rocket pointing forward, like the feathers on an arrow. With no fins (or only 1-2), the rocket tumbles and flies a short, wild path."),
        ("nose", "NOSE CONE", "A pointy top (half a bottle or paper) that lets the rocket slice through the air. It can carry a little weight to keep the front heavier: that makes the rocket more stable."),
        ("pad", "LAUNCH PAD + PUMP", "A tube that fits in the nozzle holds the rocket and the water in. You pump air in through it, then pull the release cord from a safe distance."),
    };

    GameObject PageWaterLab()
    {
        var p = Page("Water Rocket", "WATER BOTTLE ROCKET LAB", "Build a real rocket from soda bottles, water and air. Change the design, launch it, and find the best water fill. Same physics as a real rocket: push mass out the back, go forward.");
        View(p, new Vector2(0f, -122f), new Vector2(430f, 470f));
        // parts of the bottle rocket
        var pl = MissionUI.Rect("Water Parts", p); pl.anchorMin = pl.anchorMax = pl.pivot = new Vector2(0f, 1f); pl.anchoredPosition = new Vector2(0f, -604f); pl.sizeDelta = new Vector2(430f, 120f);
        var grid = AUI.Rect("Grid", pl); AUI.Fill(grid);
        var gl = grid.gameObject.AddComponent<GridLayoutGroup>(); gl.cellSize = new Vector2(138f, 34f); gl.spacing = new Vector2(8f, 8f);
        foreach (var wp in WaterParts)
        {
            var w = wp;
            var b = MissionUI.Btn(grid, Short(w.key), 12, MissionUI.Line, null, -1, 34, font);
            b.onClick.AddListener(() => SelectWater(w.key, b));
            waterBtns.Add(b);
        }
        var pib = MissionUI.Box(p, "Part", MissionUI.LineDim, new Color(0f, 0f, 0f, 0.6f));
        pib.anchorMin = pib.anchorMax = pib.pivot = new Vector2(0f, 1f); pib.anchoredPosition = new Vector2(0f, -734f); pib.sizeDelta = new Vector2(430f, 126f);
        labPartInfo = MissionUI.Txt(pib, "<color=#9AA0A6>Click a part above to learn what it does.</color>", 14, MissionUI.Text, FontStyles.Normal, font);
        MissionUI.Stretch(labPartInfo.rectTransform, 12, 8, 12, 8); labPartInfo.lineSpacing = 6f; labPartInfo.enableAutoSizing = true; labPartInfo.fontSizeMax = 14f; labPartInfo.fontSizeMin = 10f;

        // design controls
        float x = 454f;
        var ct = MissionUI.Lbl(p, "YOUR DESIGN", 14, font); ct.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.Place(ct.rectTransform, new Vector2(0f, 1f), new Vector2(x, -124f), new Vector2(380f, 22f)); ct.rectTransform.pivot = new Vector2(0f, 1f);
        float y = -156f;
        Slider(p, x, ref y, "WATER FILL", 0.05f, 0.95f, wFill, false, v => { wFill = v; Rebuild(); });
        Slider(p, x, ref y, "AIR PRESSURE  (BAR)", 1f, 6f, wBar, false, v => { wBar = v; });
        Slider(p, x, ref y, "BOTTLES", 1f, 3f, wBottles, true, v => { wBottles = Mathf.RoundToInt(v); Rebuild(); });
        Slider(p, x, ref y, "FINS", 0f, 4f, wFins, true, v => { wFins = Mathf.RoundToInt(v); Rebuild(); });
        Slider(p, x, ref y, "LAUNCH ANGLE  (DEG)", 45f, 90f, wAngle, false, v => { wAngle = v; });
        Button nose = null;
        nose = MissionUI.Btn(p, "NOSE CONE:  ON", 15, MissionUI.Line, () => { wNose = !wNose; nose.GetComponentInChildren<TextMeshProUGUI>().text = "NOSE CONE:  " + (wNose ? "ON" : "OFF"); Rebuild(); }, 380, 42, font);
        Pos(nose.transform, x, y - 4f, 380f, 42f); y -= 56f;
        labValues = MissionUI.Txt(p, "", 14, MissionUI.Label, FontStyles.Normal, font);
        MissionUI.Place(labValues.rectTransform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(380f, 70f)); labValues.rectTransform.pivot = new Vector2(0f, 1f);
        y -= 80f;
        var go = MissionUI.Btn(p, "LAUNCH!", 30, MissionUI.Alert, Launch, 380, 74, font);
        go.GetComponentInChildren<TextMeshProUGUI>().characterSpacing = 20f;
        Pos(go.transform, x, y, 380f, 74f); y -= 90f;
        var safe = MissionUI.Txt(p, "<color=#FFC850><b>TRY IT AT HOME, SAFELY:</b></color> with an adult, outdoors in an open field, safety glasses on, stand back 5 m, never pump above 6 bar and never use glass or damaged bottles.", 13, MissionUI.Label, FontStyles.Normal, font);
        MissionUI.Place(safe.rectTransform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(380f, 90f)); safe.rectTransform.pivot = new Vector2(0f, 1f); safe.lineSpacing = 6f;

        // flight plot + results
        float x2 = 858f;
        var pt = MissionUI.Lbl(p, "FLIGHT PATH", 14, font); pt.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.Place(pt.rectTransform, new Vector2(0f, 1f), new Vector2(x2, -124f), new Vector2(300f, 22f)); pt.rectTransform.pivot = new Vector2(0f, 1f);
        var pb = MissionUI.Box(p, "Plot", MissionUI.LineDim, new Color(0.02f, 0.03f, 0.05f));
        pb.anchorMin = pb.anchorMax = pb.pivot = new Vector2(0f, 1f); pb.anchoredPosition = new Vector2(x2, -150f); pb.sizeDelta = new Vector2(PW + 2f, PH + 2f);
        var pr = MissionUI.Rect("Image", pb); MissionUI.Stretch(pr, 1, 1, 1, 1);
        plotImg = pr.gameObject.AddComponent<RawImage>(); plotImg.raycastTarget = false;
        plot = new Texture2D(PW, PH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        plotPx = new Color32[PW * PH]; plotImg.texture = plot;
        var rb = MissionUI.Box(p, "Results", MissionUI.LineDim, new Color(0f, 0f, 0f, 0.6f));
        rb.anchorMin = rb.anchorMax = rb.pivot = new Vector2(0f, 1f); rb.anchoredPosition = new Vector2(x2, -468f); rb.sizeDelta = new Vector2(PW + 2f, 392f);
        labResult = MissionUI.Txt(rb, "", 15, MissionUI.Text, FontStyles.Normal, font);
        MissionUI.Stretch(labResult.rectTransform, 16, 12, 16, 12); labResult.lineSpacing = 8f;
        labResult.text = "<color=#9AA0A6>Set up your rocket and press <b>LAUNCH!</b>\n\nThe lab works out the thrust from the air pressure and the water, then flies the rocket with gravity and air drag, just like the launch simulation does for your real rockets.</color>";
        DrawPlot(null, 0f);
        UpdateValues();
        return p.gameObject;
    }

    static string Short(string key)
    {
        switch (key) { case "bottle": return "BOTTLE"; case "water": return "WATER"; case "nozzle": return "NOZZLE"; case "air": return "AIR"; case "fins": return "FINS"; case "nose": return "NOSE CONE"; default: return "PAD + PUMP"; }
    }

    void Pos(Transform t, float x, float y, float w, float h)
    {
        var r = (RectTransform)t; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
    }

    void Slider(RectTransform p, float x, ref float y, string label, float min, float max, float value, bool whole, Action<float> changed)
    {
        var l = MissionUI.Lbl(p, label, 12, font); l.alignment = TextAlignmentOptions.TopLeft;
        MissionUI.Place(l.rectTransform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(260f, 18f)); l.rectTransform.pivot = new Vector2(0f, 1f);
        var row = MissionUI.Rect("Row", p); row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f); row.anchoredPosition = new Vector2(x, y - 22f); row.sizeDelta = new Vector2(380f, 30f);
        var s = AscendTrajectoryGraph.MakeSlider(row, new Vector2(0f, 0f), new Vector2(380f, 28f), min, max, value);
        s.wholeNumbers = whole;
        s.onValueChanged.AddListener(v => { changed(v); UpdateValues(); });
        y -= 66f;
    }

    void UpdateValues()
    {
        if (!labValues) return;
        float litres = wBottles * 2f;
        labValues.text = $"<color=#FFFFFF>{wFill * 100f:0}%</color> water  =  {litres * wFill:0.0} L of {litres:0} L     <color=#FFFFFF>{wBar:0.0} bar</color>\n" +
                         $"<color=#FFFFFF>{wBottles}</color> bottle{(wBottles > 1 ? "s" : "")}   <color=#FFFFFF>{wFins}</color> fins   nose {(wNose ? "on" : "off")}   <color=#FFFFFF>{wAngle:0}°</color>";
    }

    void SelectWater(string key, Button self)
    {
        foreach (var b in waterBtns) MissionUI.SetColor(b, b == self ? MissionUI.Accent : MissionUI.Line);
        var w = WaterParts.First(x => x.key == key);
        labPartInfo.text = "<b>" + w.name + "</b>\n" + w.text;
        Highlight(key);
        spin = false;
    }

    void Rebuild()
    {
        if (shown != Model.Water) return;
        var keepYaw = yaw; var keepPitch = pitch; var keepSpin = spin;
        shown = Model.None; ShowModel(Model.Water);
        yaw = keepYaw; pitch = keepPitch; spin = keepSpin;
    }

    void Launch()
    {
        lastRun = WaterSim.Run(wFill, wBar, wBottles, wFins, wNose, wAngle);
        // what fill would have been best with everything else the same?
        float bestF = wFill, bestH = 0f;
        for (float f = 0.05f; f <= 0.951f; f += 0.05f) { var r = WaterSim.Run(f, wBar, wBottles, wFins, wNose, wAngle, false); if (r.maxH > bestH) { bestH = r.maxH; bestF = f; } }
        runT = 0f;
        var R = lastRun;
        var sb = new System.Text.StringBuilder();
        sb.Append($"<size=120%><b>{(R.stable ? "<color=#3FB950>GOOD FLIGHT!</color>" : "<color=#F85149>IT TUMBLED!</color>")}</b></size>\n");
        sb.Append($"<color=#9AA0A6>HIGHEST POINT</color><pos=46%><b>{R.maxH:0.0} m</b>  <size=80%><color=#9AA0A6>(a {Mathf.Max(1, Mathf.RoundToInt(R.maxH / 3f))}-storey building)</color></size>\n");
        sb.Append($"<color=#9AA0A6>DISTANCE</color><pos=46%><b>{R.range:0.0} m</b>\n");
        sb.Append($"<color=#9AA0A6>FLIGHT TIME</color><pos=46%><b>{R.time:0.0} s</b>\n");
        sb.Append($"<color=#9AA0A6>TOP SPEED</color><pos=46%><b>{R.vMax * 3.6f:0} km/h</b>\n");
        sb.Append($"<color=#9AA0A6>THRUST TIME</color><pos=46%><b>{R.burn:0.00} s</b>  <size=80%><color=#9AA0A6>(water {R.waterTime:0.00} s + air puff)</color></size>\n");
        sb.Append($"<color=#9AA0A6>PEAK THRUST</color><pos=46%><b>{R.thrustMax:0} N</b>  <size=80%><color=#9AA0A6>(rocket weighs {R.m0 * 9.81f:0.0} N full)</color></size>\n\n");
        if (!R.stable) sb.Append("<color=#FFC850>With fewer than 3 fins the rocket can't keep its nose forward. Add fins!</color>\n");
        else if (Mathf.Abs(bestF - wFill) < 0.06f) sb.Append($"<color=#3FB950>Your water fill is the best for this design!</color>\n");
        else sb.Append($"<color=#8AB4F8>Try <b>{bestF * 100f:0}% water</b>: it would reach about <b>{bestH:0} m</b>.</color> {(wFill > bestF ? "Too much water makes it heavy and leaves little room for air." : "Too little water: not enough mass to push out.")}\n");
        if (!wNose && R.stable) sb.Append("<color=#9AA0A6>A nose cone would cut the air drag.</color>\n");
        sb.Append("\n<size=85%><color=#9AA0A6>Real rockets work the same way: they push mass (hot gas) out of the back very fast. Faster exhaust and more push = higher flight.</color></size>");
        labResult.text = sb.ToString();
    }

    void UpdateLab()
    {
        if (runT < 0f || lastRun == null || !academy || !academy.activeSelf || lesson != 5) return;
        runT += Time.unscaledDeltaTime * 1.2f;
        float k = Mathf.Clamp01(runT / Mathf.Max(0.5f, lastRun.time));
        DrawPlot(lastRun, k);
        if (k >= 1f) runT = -1f;
    }

    void DrawPlot(WaterSim.Result r, float progress)
    {
        if (!plot) return;
        var bg = new Color32(5, 8, 13, 255); var grid = new Color32(255, 255, 255, 22); var ground = new Color32(80, 160, 90, 255);
        for (int i = 0; i < plotPx.Length; i++) plotPx[i] = bg;
        float maxX = 20f, maxY = 12f;
        if (r != null) { maxX = Mathf.Max(maxX, r.range * 1.1f); maxY = Mathf.Max(maxY, r.maxH * 1.15f); }
        float scale = Mathf.Max(maxX / (PW - 20f), maxY / (PH - 20f));   // same scale both ways: the real shape
        for (int gx = 0; gx * 5f / scale < PW; gx++) { int px = 10 + Mathf.RoundToInt(gx * 5f / scale); if (px < PW) for (int y = 0; y < PH; y++) plotPx[y * PW + px] = grid; }
        for (int gy = 0; gy * 5f / scale < PH; gy++) { int py = 10 + Mathf.RoundToInt(gy * 5f / scale); if (py < PH) for (int x = 0; x < PW; x++) plotPx[py * PW + x] = grid; }
        for (int x = 0; x < PW; x++) { plotPx[9 * PW + x] = ground; plotPx[10 * PW + x] = ground; }
        if (r != null && r.path.Count > 1)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt((r.path.Count - 1) * progress));
            Vector2 prev = ToPx(r.path[0], scale);
            for (int i = 1; i <= n; i++)
            {
                var q = ToPx(r.path[i], scale);
                Line(prev, q, r.thrustEnd > i ? new Color32(255, 170, 60, 255) : new Color32(115, 215, 255, 255));
                prev = q;
            }
            Dot(prev, new Color32(255, 255, 255, 255), 3);
            // highest point marker
            int top = 0; for (int i = 1; i < r.path.Count; i++) if (r.path[i].y > r.path[top].y) top = i;
            if (top <= n) Dot(ToPx(r.path[top], scale), new Color32(255, 200, 80, 255), 2);
        }
        plot.SetPixels32(plotPx); plot.Apply(false);
    }

    static Vector2 ToPx(Vector2 m, float scale) => new Vector2(10f + m.x / scale, 10f + m.y / scale);
    void Line(Vector2 a, Vector2 b, Color32 c) { int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b))); for (int i = 0; i <= n; i++) Dot(Vector2.Lerp(a, b, i / (float)n), c, 1); }
    void Dot(Vector2 p, Color32 c, int r)
    {
        int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
        for (int y = cy - r + 1; y < cy + r; y++) for (int x = cx - r + 1; x < cx + r; x++) if (x >= 0 && x < PW && y >= 0 && y < PH) plotPx[y * PW + x] = c;
    }

    // ───────────── the bottle rocket model (real size, metres) ─────────────
    GameObject BuildWaterRocket()
    {
        var root = new GameObject("Water Rocket");
        root.transform.SetParent(pivot, false);
        var bottleMat = Transparent(new Color(0.8f, 0.92f, 1f, 0.28f));
        var waterMat = Lit(new Color(0.15f, 0.45f, 0.95f), 0.85f);
        var airMat = Transparent(new Color(0.85f, 0.95f, 1f, 0.12f));
        var red = Lit(new Color(0.85f, 0.12f, 0.1f), 0.4f);
        var tape = Lit(new Color(0.1f, 0.1f, 0.12f), 0.3f);
        var finMat = Lit(new Color(1f, 0.8f, 0.15f), 0.3f);
        var grey = Lit(new Color(0.45f, 0.47f, 0.5f), 0.4f);
        var green = Lit(new Color(0.2f, 0.55f, 0.25f), 0.2f);

        const float R = 0.05f, body = 0.22f, neckH = 0.035f, shoulder = 0.06f;
        float y = 0.08f;                                // pad height
        // launch pad + pump tube
        Part(root, "pad", PrimitiveType.Cylinder, grey, new Vector3(0f, 0.01f, 0f), new Vector3(0.36f, 0.01f, 0.36f));
        Part(root, "pad", PrimitiveType.Cylinder, grey, new Vector3(0f, 0.05f, 0f), new Vector3(0.016f, 0.05f, 0.016f));
        Part(root, "pad", PrimitiveType.Cube, green, new Vector3(0.14f, 0.025f, 0f), new Vector3(0.05f, 0.03f, 0.05f));
        // nozzle (the bottle neck, pointing down)
        Part(root, "nozzle", PrimitiveType.Cylinder, bottleMat, new Vector3(0f, y + neckH * 0.5f, 0f), new Vector3(0.028f, neckH * 0.5f, 0.028f));
        Part(root, "nozzle", PrimitiveType.Cylinder, red, new Vector3(0f, y + 0.005f, 0f), new Vector3(0.034f, 0.006f, 0.034f));
        y += neckH;
        float bottom = y, total = 0f;
        for (int i = 0; i < wBottles; i++)
        {
            if (i == 0) { Part(root, "bottle", PrimitiveType.Sphere, bottleMat, new Vector3(0f, y + shoulder * 0.5f, 0f), new Vector3(R * 2f, shoulder, R * 2f)); y += shoulder * 0.5f; total += shoulder * 0.5f; }
            Part(root, "bottle", PrimitiveType.Cylinder, bottleMat, new Vector3(0f, y + body * 0.5f, 0f), new Vector3(R * 2f, body * 0.5f, R * 2f));
            y += body; total += body;
            if (i < wBottles - 1) Part(root, "bottle", PrimitiveType.Cylinder, tape, new Vector3(0f, y, 0f), new Vector3(R * 2.06f, 0.015f, R * 2.06f));
        }
        // water sits at the bottom (by the nozzle), air above it
        float wh = Mathf.Max(0.004f, total * wFill);
        Part(root, "water", PrimitiveType.Cylinder, waterMat, new Vector3(0f, bottom + wh * 0.5f, 0f), new Vector3(R * 1.86f, wh * 0.5f, R * 1.86f));
        float ah = Mathf.Max(0.004f, total - wh);
        Part(root, "air", PrimitiveType.Cylinder, airMat, new Vector3(0f, bottom + wh + ah * 0.5f, 0f), new Vector3(R * 1.8f, ah * 0.5f, R * 1.8f));
        // nose cone
        if (wNose)
        {
            var nose = new GameObject("nose", typeof(MeshFilter), typeof(MeshRenderer));
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, y, 0f);
            nose.GetComponent<MeshFilter>().sharedMesh = Cone(R * 1.02f, 0.13f, 24);
            nose.GetComponent<MeshRenderer>().sharedMaterial = red;
            AddGroup("nose", new[] { nose.GetComponent<Renderer>() });
        }
        else Part(root, "bottle", PrimitiveType.Cylinder, bottleMat, new Vector3(0f, y + 0.005f, 0f), new Vector3(R * 2f, 0.005f, R * 2f));
        // fins around the bottom
        for (int i = 0; i < wFins; i++)
        {
            float a = i * 360f / Mathf.Max(1, wFins);
            var f = Part(root, "fins", PrimitiveType.Cube, finMat, Vector3.zero, new Vector3(0.004f, 0.13f, 0.07f));
            f.localRotation = Quaternion.Euler(0f, a, 0f);
            f.localPosition = new Vector3(0f, bottom + 0.06f, 0f) + f.localRotation * new Vector3(0f, 0f, R + 0.035f);
        }
        return root;
    }

    Transform Part(GameObject root, string key, PrimitiveType type, Material m, Vector3 pos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.name = key;
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
        AddGroup(key, new[] { r });
        return g.transform;
    }

    static Mesh Cone(float r, float h, int seg)
    {
        var v = new List<Vector3> { new Vector3(0f, h, 0f) };
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)); }
        for (int i = 1; i <= seg; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
        var m = new Mesh { name = "Nose Cone" };
        m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static Material Transparent(Color c)
    {
        var m = Lit(c, 0.9f);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
        return m;
    }
}

// small helper so layout code can configure a freshly made RectTransform inline
static class RectLet { public static void Let(this RectTransform r, Action<RectTransform> a) => a(r); }

// ═════════════════════════════════════════════════════════════════════════════
//  WATER ROCKET PHYSICS
//   water phase: the squeezed air (adiabatic, gamma 1.4) pushes water out of a 22 mm nozzle
//                thrust = 2 A (P - Pa), exhaust speed = sqrt(2 (P - Pa) / rho)
//   air phase:   the leftover air puffs out (short, weak)
//   coast:       gravity + air drag; with fewer than 3 fins the rocket tumbles
// ═════════════════════════════════════════════════════════════════════════════
public static class WaterSim
{
    public class Result
    {
        public List<Vector2> path = new List<Vector2>();
        public float maxH, range, time, vMax, burn, waterTime, thrustMax, m0; public bool stable; public int thrustEnd;
    }

    const float Pa = 101325f, Rho = 1000f, G = 9.81f, RhoAir = 1.2f, Gamma = 1.4f, NozzleR = 0.0105f, BodyR = 0.05f;

    public static Result Run(float fill, float bar, int bottles, int fins, bool nose, float angleDeg, bool record = true)
    {
        var r = new Result { stable = fins >= 3 };
        float A = Mathf.PI * NozzleR * NozzleR, Af = Mathf.PI * BodyR * BodyR;
        float Vb = 0.002f * bottles, Vw = Vb * Mathf.Clamp01(fill), Va0 = Vb - Vw, Va = Va0;
        float P0 = bar * 100000f + Pa, P = P0;
        float dry = 0.05f * bottles + 0.012f * fins + (nose ? 0.03f : 0f) + 0.01f;
        float mAir0 = P0 * Va0 / (287f * 293f), mAir = mAir0;
        float cd = r.stable ? (nose ? 0.35f : 0.6f) : 1.1f;
        float heading = angleDeg * Mathf.Deg2Rad;
        Vector2 pos = Vector2.zero, vel = Vector2.zero;
        float t = 0f, nextRec = 0f;
        r.m0 = dry + Rho * Vw + mAir;
        bool waterDone = Vw <= 0f, airDone = false;
        if (record) r.path.Add(pos);
        for (int guard = 0; guard < 400000; guard++)
        {
            float dt = !airDone ? 0.0005f : 0.005f;
            float m = dry + Rho * Mathf.Max(0f, Vw) + mAir;
            float thrust = 0f;
            if (!waterDone)
            {
                P = P0 * Mathf.Pow(Va0 / Va, Gamma);
                if (P <= Pa) { waterDone = true; r.waterTime = t; P0 = P; mAir0 = mAir; }   // air ran out of push before the water did
                else
                {
                    float ve = Mathf.Sqrt(2f * (P - Pa) / Rho);
                    thrust = 2f * A * (P - Pa);
                    float dV = A * ve * dt;
                    Vw -= dV; Va += dV;
                    if (Vw <= 0f) { Vw = 0f; waterDone = true; r.waterTime = t; mAir0 = mAir; P0 = P; }
                }
            }
            else if (!airDone)
            {
                P = P0 * Mathf.Pow(Mathf.Max(1e-6f, mAir / Mathf.Max(1e-6f, mAir0)), Gamma);
                if (P <= Pa * 1.02f || t > 3f) { airDone = true; r.burn = t; r.thrustEnd = r.path.Count; }
                else
                {
                    float rhoG = mAir / Va;
                    float ve = Mathf.Min(Mathf.Sqrt(2f * (P - Pa) / rhoG), 320f);
                    float mdot = rhoG * A * ve;
                    thrust = mdot * ve;
                    mAir = Mathf.Max(0f, mAir - mdot * dt);
                }
            }
            r.thrustMax = Mathf.Max(r.thrustMax, thrust);

            // direction: on the launch tube (first 0.3 m) it follows the launch angle;
            // then a stable rocket points along its velocity, an unstable one tumbles
            float speed = vel.magnitude;
            if (pos.magnitude > 0.3f && speed > 0.5f)
            {
                if (r.stable) heading = Mathf.Atan2(vel.y, vel.x);
                else heading += 9f * dt * (1f + speed * 0.05f);
            }
            Vector2 dir = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
            Vector2 drag = speed > 0.01f ? -vel / speed * (0.5f * RhoAir * speed * speed * cd * Af) : Vector2.zero;
            Vector2 acc = (dir * thrust + drag) / m + Vector2.down * G;
            if (pos.y <= 0f && acc.y < 0f && t < 0.05f) acc = Vector2.zero;   // still sitting on the pad
            vel += acc * dt; pos += vel * dt; t += dt;
            r.vMax = Mathf.Max(r.vMax, vel.magnitude);
            r.maxH = Mathf.Max(r.maxH, pos.y);
            if (record && t >= nextRec) { r.path.Add(pos); nextRec = t + 0.02f; }
            if (pos.y < 0f && t > 0.1f) { pos.y = 0f; break; }
            if (t > 60f) break;
        }
        if (!airDone) { r.burn = t; r.thrustEnd = r.path.Count; }
        if (r.waterTime <= 0f) r.waterTime = r.burn;
        r.range = Mathf.Abs(pos.x); r.time = t;
        if (record) r.path.Add(pos);
        return r;
    }
}
