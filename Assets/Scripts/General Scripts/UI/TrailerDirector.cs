// ═════════════════════════════════════════════════════════════════════════════
//  TrailerDirector.cs  -  the 30-second ASCEND trailer (Main Menu > Options > Trailer).
//  Runs in the Universe scene and plays itself:
//     0- 5 s  THE MEETING   slow push along the conference table, the team around it
//     5-10 s  DESIGN        the rocket builds itself on the pad, part by part
//    10-22 s  LAUNCH        countdown, ignition, liftoff, tracking shot, stage separation
//    22-28 s  ORBIT         HD Earth, the satellite drifting over the Philippines
//    28-30 s  TITLE         ASCEND - Design. Launch. Explore.   -> back to the Main Menu
//  Esc or Space skips. Letterbox bars, captions, eased camera moves with a little handheld shake.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TrailerDirector : MonoBehaviour
{
    public string mainMenuScene = "Main Menu";
    const int RigLayer = 31;
    static readonly Vector3 RigOrigin = new Vector3(52000f, 52000f, 52000f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook() { SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded; Try(); }
    static void OnLoaded(Scene s, LoadSceneMode m) => Try();
    static void Try()
    {
        if (!GameFlow.PlayTrailer || FindAnyObjectByType<TrailerDirector>() || !FindAnyObjectByType<AscendLaunchController>()) return;
        new GameObject("Trailer Director").AddComponent<TrailerDirector>();
    }

    Camera cam, spaceCam, rigCam;
    CanvasGroup fade, flash;
    TextMeshProUGUI caption, sub, title, tagline, count;
    AscendCADData data;
    Vector3 site; float rocketH = 40f;
    GameObject rocket; Transform rocketBody;
    readonly List<Transform> parts = new List<Transform>();
    readonly List<Transform> engines = new List<Transform>();
    readonly HashSet<Transform> lowerStage = new HashSet<Transform>();
    readonly List<ParticleSystem> fx = new List<ParticleSystem>();
    readonly List<GameObject> spawned = new List<GameObject>();
    Light sun; Quaternion sunRot;
    Quaternion away = Quaternion.identity;   // local -Z points away from the launch tower
    bool skipping;

    IEnumerator Start()
    {
        Time.timeScale = 1f;
        BuildUI();
        fade.alpha = 1f;
        yield return null;
        var loading = GameObject.Find("Trailer Loading"); if (loading) Destroy(loading);
        var intro = GameObject.Find("Intro Story"); if (intro) Destroy(intro);
        MissionBriefing.SetUiVisible(false);
        // hide the game's own HUD (crosshair, hints...) - only screen overlays, monitors in the world stay on
        foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (cv.isRootCanvas && cv.renderMode != RenderMode.WorldSpace && !cv.transform.IsChildOf(transform)) cv.enabled = false;

        // take over the view
        if (PlayerController.Instance) { PlayerController.Instance.enabled = false; }
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.enabled && c.targetTexture == null) c.enabled = false;
        cam = new GameObject("Trailer Camera").AddComponent<Camera>();
        cam.transform.SetParent(transform);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000000f; cam.fieldOfView = 40f;
        cam.cullingMask &= ~(1 << AscendEarth.Layer) & ~(1 << RigLayer);
        try { cam.GetUniversalAdditionalCameraData().renderPostProcessing = true; } catch (Exception) { }
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;

        var ci = FindAnyObjectByType<ComputerInteraction>();
        data = AscendCADData.Resolve(ci ? ci.ascendCAD : null);
        site = data.launchSitePosition;
        sun = RenderSettings.sun; if (sun) sunRot = sun.transform.rotation;

        StartCoroutine(SkipWatcher());
        yield return Meeting();
        yield return Design();
        yield return Launch();
        yield return Orbit();
        yield return TitleCard();
        End();
    }

    IEnumerator SkipWatcher()
    {
        yield return new WaitForSeconds(0.5f);
        while (true)
        {
#if ENABLE_INPUT_SYSTEM
            bool k = Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame);
#else
            bool k = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space);
#endif
            if (k && !skipping) { skipping = true; StopAllCoroutines(); StartCoroutine(SkipOut()); yield break; }
            yield return null;
        }
    }

    IEnumerator SkipOut() { yield return Fade(fade.alpha, 1f, 0.5f); End(); }

    void End()
    {
        if (sun) sun.transform.rotation = sunRot;
        AscendSky.ResetAltitude();
        GameFlow.PlayTrailer = false;
        if (Application.CanStreamedLevelBeLoaded(mainMenuScene)) SceneManager.LoadScene(mainMenuScene);
#if UNITY_EDITOR
        else UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + mainMenuScene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }

    // ───────────────────────── camera helpers ─────────────────────────
    static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // move from A to B over 'secs', looking at a (moving) target, with a soft handheld shake
    IEnumerator Shot(float secs, Func<float, Vector3> pos, Func<float, Vector3> look, float fovA, float fovB, float shake = 0.04f, Action<float> each = null)
    {
        float t = 0f, seed = UnityEngine.Random.value * 100f;
        while (t < secs)
        {
            float k = Ease(t / secs);
            each?.Invoke(t);
            Vector3 p = pos(k);
            Vector3 n = new Vector3(Mathf.PerlinNoise(seed, t * 0.7f) - 0.5f, Mathf.PerlinNoise(seed + 3f, t * 0.7f) - 0.5f, 0f) * shake;
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation(look(k) - p) * Quaternion.Euler(n.y * 8f, n.x * 8f, 0f);
            cam.fieldOfView = Mathf.Lerp(fovA, fovB, k);
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ───────────────────────── 1. THE MEETING (0-5 s) ─────────────────────────
    IEnumerator Meeting()
    {
        var mb = FindAnyObjectByType<MissionBriefing>();
        Vector3 room = mb ? (mb.meetingRoomPoint ? mb.meetingRoomPoint.position : mb.meetingRoomPosition) : new Vector3(811f, 1.08f, 721.2f);
        Vector3 fwd = Quaternion.Euler(0f, mb ? mb.meetingRoomFacingY : -90f, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, fwd);
        // the team, standing along the table
        var walkers = FindObjectsByType<AscendWalker>(FindObjectsSortMode.None);
        for (int i = 0; i < 4 && walkers.Length > 0; i++)
        {
            var w = walkers[i % walkers.Length];
            if (!w.model) continue;
            var g = Instantiate(w.model);
            spawned.Add(g);
            foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
            float sideSign = i % 2 == 0 ? 1f : -1f;
            Vector3 p = room + fwd * (3f + i * 2.4f) + side * sideSign * 2.3f;
            p.y = Ground(p);
            g.transform.position = p;
            g.transform.rotation = Quaternion.LookRotation(-side * sideSign);
            FitHeight(g, i % 2 == 0 ? 1.75f : 1.65f);
            PlayClip(g, i == 1 && w.waveClip ? w.waveClip : (w.idleClip ? w.idleClip : w.walkClip));
        }
        Caption("EVERY MISSION STARTS WITH A PLAN", "ASCEND MISSION CONTROL   //   BRIEFING ROOM");
        StartCoroutine(Fade(1f, 0f, 1.2f));
        Vector3 a = room + Vector3.up * 1.55f - fwd * 0.6f + side * 0.7f, b = room + Vector3.up * 1.3f + fwd * 4.5f - side * 0.4f;
        yield return Shot(4.5f, k => Vector3.Lerp(a, b, k), k => room + fwd * 12f + Vector3.up * 1.1f, 46f, 40f, 0.03f);
        yield return Fade(0f, 1f, 0.35f);
        foreach (var g in spawned) if (g) Destroy(g);
        spawned.Clear();
    }

    // ───────────────────────── 2. DESIGN (5-10 s): the rocket builds itself ─────────────────────────
    IEnumerator Design()
    {
        var design = AscendTemplates.Build(1, data);                    // Falcon 9-class vehicle
        var sat = AscendTemplates.AutoSat(AscendSimMission.EarthWatcher, 0, data);
        var ps = CADStats.Payload(sat, data);
        var st = CADStats.Rocket(design, data, ps.wetMass, 0f);
        rocket = AscendLaunchSite.Spawn(data, design, sat, st, "pad.gantry");
        foreach (var c in rocket.GetComponentsInChildren<Collider>()) c.enabled = false;
        rocketBody = rocket.transform.childCount > 0 ? rocket.transform.GetChild(0) : rocket.transform;
        var rs = rocket.GetComponentsInChildren<Renderer>(); Bounds bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
        rocketH = bb.size.y; site = new Vector3(bb.center.x, bb.min.y, bb.center.z);
        away = TowerFrame();

        // map parts (same order as the build, skipping internal parts) and find the engines + first stage
        int child = 0; float decY = float.MaxValue;
        for (int i = 0; i < design.parts.Count; i++) { var p = data.Get(design.parts[i].partId); if (p != null && p.role == CADRole.Decoupler) decY = Mathf.Min(decY, design.parts[i].pos.y); }
        for (int i = 0; i < design.parts.Count; i++)
        {
            var p = data.Get(design.parts[i].partId);
            if (p == null || p.attach == CADAttach.Internal) continue;
            if (child >= rocketBody.childCount) break;
            var t = rocketBody.GetChild(child++);
            if (t.name.StartsWith("Payload - ")) { child--; continue; }
            parts.Add(t);
            if (p.role == CADRole.Engine || p.role == CADRole.SolidMotor) engines.Add(t);
            if (design.parts[i].pos.y < decY - 0.01f) lowerStage.Add(t);
        }
        // payload is the last part to arrive
        for (int c = 0; c < rocketBody.childCount; c++) if (rocketBody.GetChild(c).name.StartsWith("Payload - ")) parts.Add(rocketBody.GetChild(c));

        // assemble bottom to top: each part swings in from above
        var finalPos = new Dictionary<Transform, Vector3>(); var finalRot = new Dictionary<Transform, Quaternion>();
        parts.Sort((x, y) => x.position.y.CompareTo(y.position.y));
        foreach (var t in parts) { finalPos[t] = t.localPosition; finalRot[t] = t.localRotation; t.gameObject.SetActive(false); }
        Caption("DESIGN IT. PART BY PART.", "ASCENDCAD   //   REAL PARTS, REAL PHYSICS, REAL BUDGET");
        StartCoroutine(Fade(1f, 0f, 0.4f));
        float dur = 5.5f, per = (dur - 1.3f) / Mathf.Max(1, parts.Count);
        Vector3 mid = site + Vector3.up * rocketH * 0.45f;
        float R = Mathf.Max(45f, rocketH * 1.3f);
        var dAng = Clear(mid + new Vector3(0f, -rocketH * 0.2f, -R) - site, 0f, 1f);
        StartCoroutine(Shot(dur, k => mid + dAng * Quaternion.Euler(0f, -18f + 36f * k, 0f) * new Vector3(0f, -rocketH * 0.25f + 6f * k, -R), k => mid + Vector3.up * rocketH * (k - 0.5f) * 0.3f, 50f, 44f, 0.05f));
        for (int i = 0; i < parts.Count; i++)
        {
            var t = parts[i]; t.gameObject.SetActive(true);
            StartCoroutine(FlyIn(t, finalPos[t], finalRot[t], 0.55f, i));
            yield return new WaitForSeconds(per);
        }
        yield return new WaitForSeconds(1.3f);
    }

    IEnumerator FlyIn(Transform t, Vector3 lp, Quaternion lr, float secs, int i)
    {
        Vector3 from = lp + Vector3.up * (8f + i * 0.2f) + new Vector3(Mathf.Sin(i * 1.7f), 0f, Mathf.Cos(i * 1.7f)) * 3f;
        Quaternion fromR = lr * Quaternion.Euler(0f, 90f + i * 23f, 12f);
        for (float a = 0f; a < secs; a += Time.deltaTime)
        {
            float k = 1f - Mathf.Pow(1f - a / secs, 3f);
            t.localPosition = Vector3.LerpUnclamped(from, lp, k); t.localRotation = Quaternion.Slerp(fromR, lr, k);
            yield return null;
        }
        t.localPosition = lp; t.localRotation = lr;
    }

    // ───────────────────────── 3. LAUNCH (10-22 s) ─────────────────────────
    IEnumerator Launch()
    {
        // 3, 2, 1 over a low wide shot of the pad
        Caption("", "");
        var lAng = Clear(new Vector3(-rocketH * 1.6f, 1.6f, -rocketH * 2.2f), 0f, 1f);
        Vector3 low = site + lAng * new Vector3(-rocketH * 1.6f, 1.6f, -rocketH * 2.2f);
        StartCoroutine(Shot(3f, k => low + lAng * Vector3.right * 6f * k, k => site + Vector3.up * rocketH * 0.55f, 34f, 30f, 0.02f));
        for (int i = 3; i >= 1; i--) { count.text = i.ToString(); yield return new WaitForSeconds(1f); }
        count.text = "";

        // ignition
        foreach (var e in engines) if (lowerStage.Contains(e)) Ignite(e, 1.4f);
        if (engines.Count > 0 && fx.Count == 0) foreach (var e in engines) Ignite(e, 1.2f);
        Caption("LAUNCH.", "");
        float t0 = Time.time;
        Vector3 basePos = rocket.transform.position;
        Action<float> climb = _ =>
        {
            float t = Time.time - t0;
            float y = t < 1f ? 0f : 0.5f * 9f * (t - 1f) * (t - 1f);
            rocket.transform.position = basePos + Vector3.up * y;
            cam.transform.position += UnityEngine.Random.insideUnitSphere * Mathf.Clamp01(1.5f - y / 120f) * 0.15f;
        };
        // low angle: the rocket clears the tower
        var gAng = Clear(new Vector3(rocketH * 0.9f, 2.2f, -rocketH * 1.1f), 0.3f, 2f);
        Vector3 g1 = site + gAng * new Vector3(rocketH * 0.9f, 2.2f, -rocketH * 1.1f);
        yield return Shot(3f, k => g1 + Vector3.up * 2f * k, k => rocket.transform.position + Vector3.up * rocketH * 0.75f, 52f, 46f, 0.08f, climb);
        // tracking shot from the side
        var tAng = Clear(new Vector3(-rocketH * 1.5f, rocketH * 0.5f, -rocketH * 2f), 0.8f, 1.6f);
        yield return Shot(2.2f, k => rocket.transform.position + tAng * new Vector3(-rocketH * 1.5f, rocketH * (0.2f + 0.4f * k), -rocketH * 2f), k => rocket.transform.position + Vector3.up * rocketH * 0.5f, 42f, 38f, 0.06f, climb);

        // high in a dark sky: stage separation
        yield return Fade(0f, 1f, 0.2f);
        rocket.transform.position = basePos + Vector3.up * 52000f;
        AscendSky.SetAltitude(52000f);
        var cleanup = new List<GameObject>();
        StartCoroutine(Fade(1f, 0f, 0.25f));
        Caption("", "STAGE SEPARATION   //   T+02:41   //   52 KM");
        Vector3 up = Vector3.up;
        float tSep = Time.time + 1.1f; bool separated = false;
        var dropped = new GameObject("Dropped Stage").transform;
        Vector3 vDrop = Vector3.zero;
        Action<float> high = _ =>
        {
            rocket.transform.position += up * 60f * Time.deltaTime * (separated ? 1.35f : 1f);
            if (!separated && Time.time >= tSep)
            {
                separated = true;
                dropped.position = rocket.transform.position; dropped.rotation = rocket.transform.rotation;
                foreach (var p in lowerStage) if (p) p.SetParent(dropped, true);
                foreach (var f in fx) if (f && f.transform.IsChildOf(dropped)) { var em = f.emission; em.rateOverTime = 0f; }
                AscendFX.Puff(dropped.position + up * rocketH * 0.62f, 4f, up, up * 60f);
                // upper stage lights up
                foreach (var e in engines) if (!e.IsChildOf(dropped)) Ignite(e, 0.9f);
                vDrop = up * 60f;
            }
            if (separated)
            {
                vDrop += Vector3.down * 9.8f * Time.deltaTime;
                dropped.position += vDrop * Time.deltaTime;
                dropped.Rotate(new Vector3(6f, 0f, 14f) * Time.deltaTime, Space.World);
            }
        };
        Vector3 off = new Vector3(rocketH * 1.4f, -rocketH * 0.2f, -rocketH * 1.6f);
        yield return Shot(3f, k => rocket.transform.position + off + Vector3.down * rocketH * 0.6f * k, k => rocket.transform.position + Vector3.up * rocketH * (0.35f - 0.25f * k), 38f, 46f, 0.04f, high);
        yield return Flash();
        foreach (var f in fx) if (f) Destroy(f.gameObject);
        fx.Clear();
        if (dropped) Destroy(dropped.gameObject);
        if (rocket) rocket.SetActive(false);
        if (AscendLaunchSite.CurrentPad) AscendLaunchSite.CurrentPad.SetActive(false);
    }

    void Ignite(Transform e, float size)
    {
        var p = data != null ? e.GetComponentInChildren<Renderer>() : null;
        float d = p ? Mathf.Max(0.6f, p.bounds.size.x) : 1f;
        Vector3 nozzle = (p ? new Vector3(p.bounds.center.x, p.bounds.min.y, p.bounds.center.z) : e.position);
        var f = AscendFX.Flame(e, nozzle, d * size, false); fx.Add(f); var em = f.emission; em.rateOverTime = 260f;
        var s = AscendFX.Smoke(e, nozzle, d * size); fx.Add(s); var es = s.emission; es.rateOverTime = 45f;
    }

    // ───────────────────────── 4. ORBIT (22-28 s) ─────────────────────────
    IEnumerator Orbit()
    {
        var earth = AscendEarth.Create();
        spawned.Add(earth.gameObject);
        Vector3 s = (sun ? -sun.transform.forward : new Vector3(1f, 0.3f, 0f)); s.y = 0f; s = (s.normalized + Vector3.up * 0.12f).normalized;
        if (sun) sun.transform.rotation = Quaternion.LookRotation(-s);
        earth.SetSun(s);
        AscendSky.SetAltitude(700000f);
        // turn the Earth so the Philippines faces the sun-lit side of our view
        Vector3 view = Vector3.ProjectOnPlane(s, Vector3.up).normalized;
        Vector3 ph = earth.WorldDir(12.5f, 122.5f);
        float a = Vector3.SignedAngle(Vector3.ProjectOnPlane(ph, Vector3.up), Quaternion.Euler(0f, -25f, 0f) * view, Vector3.up);
        earth.transform.Rotate(0f, a, 0f, Space.World);

        spaceCam = new GameObject("Trailer Space Camera").AddComponent<Camera>();
        spaceCam.transform.SetParent(transform);
        spaceCam.cullingMask = 1 << AscendEarth.Layer; spaceCam.nearClipPlane = 0.5f; spaceCam.farClipPlane = 60000f; spaceCam.fieldOfView = 45f;
        spaceCam.clearFlags = CameraClearFlags.Skybox; spaceCam.depth = 20f;
        rigCam = new GameObject("Trailer Satellite Camera").AddComponent<Camera>();
        rigCam.transform.SetParent(transform);
        rigCam.clearFlags = CameraClearFlags.Depth; rigCam.cullingMask = 1 << RigLayer; rigCam.nearClipPlane = 0.05f; rigCam.farClipPlane = 5000f; rigCam.depth = 21f;
        try
        {
            var bd = spaceCam.GetUniversalAdditionalCameraData(); bd.renderType = CameraRenderType.Base;
            rigCam.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
            bd.cameraStack.Add(rigCam);
        }
        catch (Exception) { }
        cam.enabled = false;

        // satellite (real size) with its own sun
        var sat = AscendTemplates.AutoSat(AscendSimMission.EarthWatcher, 0, data);
        var satGo = AscendLaunchSite.BuildDesign(sat, data, null, RigLayer, false);
        foreach (var tr in satGo.GetComponentsInChildren<Transform>()) tr.gameObject.layer = RigLayer;
        satGo.transform.position = RigOrigin;
        spawned.Add(satGo);
        var rsun = new GameObject("Trailer Sat Sun").AddComponent<Light>(); rsun.type = LightType.Directional; rsun.cullingMask = 1 << RigLayer; rsun.intensity = 1.7f;
        rsun.transform.rotation = Quaternion.LookRotation(-s); spawned.Add(rsun.gameObject);
        var fill = new GameObject("Trailer Earthshine").AddComponent<Light>(); fill.type = LightType.Directional; fill.cullingMask = 1 << RigLayer; fill.intensity = 0.6f; fill.color = new Color(0.55f, 0.7f, 1f);
        fill.transform.rotation = Quaternion.LookRotation(Vector3.up); spawned.Add(fill.gameObject);
        var srs = satGo.GetComponentsInChildren<Renderer>(); Bounds sb = srs.Length > 0 ? srs[0].bounds : new Bounds(RigOrigin, Vector3.one); foreach (var r in srs) sb.Encapsulate(r.bounds);
        float sz = Mathf.Max(0.5f, sb.size.magnitude);

        // the camera sits 700 km above the Philippines (Earth units: km), the satellite floats just in front
        Vector3 above = ph.normalized;
        Vector3 camEarth = AscendEarth.Center + earth.WorldDir(4f, 118f) * (AscendEarth.RadiusKm + 700f);
        Vector3 lookEarth = AscendEarth.Center + earth.WorldDir(13f, 123f) * AscendEarth.RadiusKm;
        Caption("EXPLORE.", "FOR THE PHILIPPINES   //   700 KM ABOVE HOME");
        StartCoroutine(Fade(1f, 0f, 0.6f));
        float t = 0f, secs = 5.4f;
        while (t < secs)
        {
            float k = Ease(t / secs);
            Quaternion rot = Quaternion.LookRotation(lookEarth - camEarth + Vector3.up * 900f * (1f - k), Vector3.up) * Quaternion.Euler(-4f * k, 6f * k, 0f);
            spaceCam.transform.position = camEarth; spaceCam.transform.rotation = rot;
            rigCam.transform.rotation = rot; rigCam.fieldOfView = spaceCam.fieldOfView = Mathf.Lerp(48f, 42f, k);
            // the satellite drifts across the frame, slowly turning
            rigCam.transform.position = sb.center - rot * Vector3.forward * sz * Mathf.Lerp(3.2f, 2.4f, k) + rot * Vector3.right * sz * Mathf.Lerp(0.8f, -0.5f, k);
            satGo.transform.Rotate(Vector3.up, 6f * Time.deltaTime, Space.World);
            earth.Spin(0.4f * Time.deltaTime, Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        yield return Fade(0f, 1f, 0.5f);
    }

    // ───────────────────────── 5. TITLE (28-30 s) ─────────────────────────
    IEnumerator TitleCard()
    {
        Caption("", "");
        title.text = "ASCEND"; tagline.text = "DESIGN.   LAUNCH.   EXPLORE.";
        var g = title.transform.parent.GetComponent<CanvasGroup>();
        for (float a = 0f; a < 0.7f; a += Time.deltaTime) { g.alpha = a / 0.7f; title.characterSpacing = Mathf.Lerp(60f, 24f, Ease(a / 0.7f)); yield return null; }
        g.alpha = 1f;
        yield return new WaitForSeconds(1.3f);
        for (float a = 0f; a < 0.5f; a += Time.deltaTime) { g.alpha = 1f - a / 0.5f; yield return null; }
    }

    // ───────────────────────── UI ─────────────────────────
    void BuildUI()
    {
        var cgo = new GameObject("Trailer UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        cgo.transform.SetParent(transform);
        var c = cgo.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 2800;
        var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)cgo.transform;
        // letterbox
        foreach (var top in new[] { true, false })
        {
            var bar = MissionUI.Solid(root, Color.black, false, top ? "Bar Top" : "Bar Bottom").rectTransform;
            bar.anchorMin = new Vector2(0f, top ? 1f : 0f); bar.anchorMax = new Vector2(1f, top ? 1f : 0f); bar.pivot = new Vector2(0.5f, top ? 1f : 0f);
            bar.sizeDelta = new Vector2(0f, 118f); bar.anchoredPosition = Vector2.zero;
        }
        caption = MissionUI.Txt(root, "", 40, Color.white, FontStyles.Bold);
        caption.characterSpacing = 16f; caption.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(1700, 60));
        sub = MissionUI.Lbl(root, "", 15); sub.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 42), new Vector2(1700, 30));
        count = MissionUI.Txt(root, "", 170, Color.white, FontStyles.Bold); count.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(count.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 260));
        var skip = MissionUI.Lbl(root, "ESC  SKIP", 12);
        MissionUI.Place(skip.rectTransform, new Vector2(1f, 1f), new Vector2(-40, -44), new Vector2(200, 30)); skip.alignment = TextAlignmentOptions.Right;

        var f = MissionUI.Solid(root, Color.black, false, "Fade"); MissionUI.Stretch(f.rectTransform); fade = f.gameObject.AddComponent<CanvasGroup>(); fade.blocksRaycasts = false;
        var fl = MissionUI.Solid(root, Color.white, false, "Flash"); MissionUI.Stretch(fl.rectTransform); flash = fl.gameObject.AddComponent<CanvasGroup>(); flash.alpha = 0f; flash.blocksRaycasts = false;

        var tg = MissionUI.Rect("Title Card", root); MissionUI.Stretch(tg); var tcg = tg.gameObject.AddComponent<CanvasGroup>(); tcg.alpha = 0f;
        title = MissionUI.Txt(tg, "", 150, Color.white, FontStyles.Bold); title.alignment = TextAlignmentOptions.Center; title.characterSpacing = 24f;
        MissionUI.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1800, 200));
        tagline = MissionUI.Lbl(tg, "", 22); tagline.color = MissionUI.Accent; tagline.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(1600, 40));
        tg.SetAsLastSibling(); f.transform.SetAsLastSibling(); fl.transform.SetAsLastSibling();
    }

    void Caption(string big, string small)
    {
        StopCoroutine(nameof(CaptionFade));
        caption.text = big; sub.text = small;
        StartCoroutine(nameof(CaptionFade));
    }

    IEnumerator CaptionFade()
    {
        for (float a = 0f; a < 0.6f; a += Time.deltaTime) { caption.alpha = sub.alpha = a / 0.6f; yield return null; }
        caption.alpha = sub.alpha = 1f;
    }

    IEnumerator Fade(float from, float to, float secs)
    {
        for (float a = 0f; a < secs; a += Time.deltaTime) { fade.alpha = Mathf.Lerp(from, to, a / secs); yield return null; }
        fade.alpha = to;
    }

    IEnumerator Flash()
    {
        for (float a = 0f; a < 0.12f; a += Time.deltaTime) { flash.alpha = a / 0.12f; yield return null; }
        fade.alpha = 1f;
        for (float a = 0f; a < 0.25f; a += Time.deltaTime) { flash.alpha = 1f - a / 0.25f; yield return null; }
        flash.alpha = 0f;
    }

    // Best camera angle around the pad: tries every 15 degrees and keeps the one where the fewest
    // pad structures (tower, water tower, masts) stand between the camera and the rocket.
    // 'offset' is the camera position relative to the pad (in the 'away' frame); h0..h1 = rocket heights to keep visible.
    bool padColliders;
    Quaternion Clear(Vector3 offset, float h0, float h1)
    {
        var pad = AscendLaunchSite.CurrentPad;
        if (!pad) return away;
        if (!padColliders)
        {
            padColliders = true;   // the pad is a few merged meshes: give them real shapes for the line-of-sight test
            foreach (var mf in pad.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh && !mf.GetComponent<Collider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            Physics.SyncTransforms();
        }
        Quaternion best = away; int bestHits = int.MaxValue; float bestDev = 999f;
        for (int a = 0; a < 360; a += 15)
        {
            var q = away * Quaternion.Euler(0f, a, 0f);
            Vector3 cp = site + q * offset;
            int hits = 0;
            for (int i = 0; i <= 6; i++)
            {
                Vector3 tp = site + Vector3.up * rocketH * Mathf.Lerp(Mathf.Max(0.15f, h0), h1, i / 6f);
                foreach (var h in Physics.RaycastAll(cp, tp - cp, (tp - cp).magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.transform.IsChildOf(pad.transform)) hits++;
            }
            float dev = Mathf.Abs(Mathf.DeltaAngle(0f, a));
            if (hits < bestHits || (hits == bestHits && dev < bestDev)) { best = q; bestHits = hits; bestDev = dev; }
        }
        return best;
    }

    // ───────────────────────── small helpers ─────────────────────────
    // Where is the launch tower? Returns a rotation whose -Z points away from it, so shots never look through the tower.
    Quaternion TowerFrame()
    {
        var pad = AscendLaunchSite.CurrentPad;
        if (!pad) return Quaternion.identity;
        Vector3 sum = Vector3.zero; float wsum = 0f;
        foreach (var r in pad.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds;
            if (b.max.y < site.y + rocketH * 0.35f) continue;                  // only tall things (tower, arms)
            Vector3 d = b.center - site; d.y = 0f;
            if (d.magnitude > rocketH * 2f) continue;
            float w = b.size.x * b.size.y * b.size.z + 0.01f;
            sum += d * w; wsum += w;
        }
        if (wsum <= 0f) return Quaternion.identity;
        Vector3 dir = sum / wsum; dir.y = 0f;
        return dir.sqrMagnitude < 0.01f ? Quaternion.identity : Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    static float Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return hit.point.y;
        return p.y;
    }

    static void FitHeight(GameObject g, float h)
    {
        var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return;
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        g.transform.localScale *= h / Mathf.Max(0.1f, b.size.y);
    }

    static void PlayClip(GameObject g, AnimationClip clip)
    {
        if (!clip) return;
        var anim = g.GetComponentInChildren<Animator>(); if (!anim) anim = g.AddComponent<Animator>();
        anim.applyRootMotion = false;
        UnityEngine.Playables.AnimationPlayableUtilities.PlayClip(anim, clip, out var graph);
        g.AddComponent<GraphCleaner>().graph = graph;
    }

    class GraphCleaner : MonoBehaviour { public UnityEngine.Playables.PlayableGraph graph; void OnDestroy() { if (graph.IsValid()) graph.Destroy(); } }
}
