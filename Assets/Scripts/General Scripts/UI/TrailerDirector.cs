// ═════════════════════════════════════════════════════════════════════════════
//  TrailerDirector.cs  -  the 30-second ASCEND trailer (Main Menu > Options > Trailer).
//  Runs in the Universe scene and plays itself:
//     0- 5 s  THE MEETING   slow push along the conference table, the team around it
//     5-10 s  DESIGN        the rocket builds itself on the pad, part by part
//    10-22 s  LAUNCH        countdown, ignition, liftoff, tracking shot, stage separation
//    22-28 s  ORBIT         HD Earth, the satellite drifting over the Philippines
//    28-30 s  TITLE         ASCEND - Design. Launch. Explore.   -> back to the Main Menu
//  S (or Esc / Space) skips. Letterbox bars, captions, eased camera moves with a little handheld shake.
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
    static readonly Vector3 RigOrigin = new Vector3(0f, 3000f, 0f);   // near the origin: far away, float precision makes small things shake

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
    TextMeshProUGUI caption, sub, title, glow, tagline, count;
    RectTransform line;
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
            bool k = Keyboard.current != null && (Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame);
#else
            bool k = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space);
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
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;   // give the mouse back to the menu
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
            float u = t / secs, k = 0.4f * Ease(u) + 0.6f * u;   // eases in, but never stops dead at the end
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
        StartCoroutine(FadeAt(4.45f, 0.35f));
        yield return Shot(4.85f, k => Vector3.Lerp(a, b, k), k => room + fwd * 12f + Vector3.up * 1.1f, 46f, 40f, 0.03f);
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
        int child = 0; float decY = float.MaxValue, lowEng = float.MaxValue;
        for (int i = 0; i < design.parts.Count; i++) { var p = data.Get(design.parts[i].partId); if (p != null && (p.role == CADRole.Engine || p.role == CADRole.SolidMotor)) lowEng = Mathf.Min(lowEng, design.parts[i].pos.y); }
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
            bool isEngine = p.role == CADRole.Engine || p.role == CADRole.SolidMotor;
            if (isEngine ? design.parts[i].pos.y <= lowEng + 1.5f && lowEng < decY : design.parts[i].pos.y < decY - 0.01f) lowerStage.Add(t);
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
        StartCoroutine(GroundCloud());
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
        // ignition: engines, flame and the smoke cloud in frame, then tilt up as it climbs
        var gOff = new Vector3(rocketH * 0.85f, 2.5f, -rocketH * 1.15f);
        var gAng = Clear(gOff, 0f, 1.2f);
        Vector3 g1 = site + gAng * gOff;
        yield return Shot(3f, k => g1 + Vector3.up * 2f * k, k => rocket.transform.position + Vector3.up * rocketH * Mathf.Lerp(0.22f, 0.5f, k), 50f, 44f, 0.08f, climb);
        // tracking shot from the side
        var tAng = Clear(new Vector3(-rocketH * 1.5f, rocketH * 0.5f, -rocketH * 2f), 0.8f, 1.6f);
        StartCoroutine(FadeAt(2.15f, 0.4f));
        yield return Shot(2.6f, k => rocket.transform.position + tAng * new Vector3(-rocketH * 1.5f, rocketH * (0.2f + 0.4f * k), -rocketH * 2f), k => rocket.transform.position + Vector3.up * rocketH * 0.5f, 42f, 38f, 0.06f, climb);

        // high in a dark sky: stage separation
        rocket.transform.position = basePos + Vector3.up * 52000f;
        AscendSky.SetAltitude(52000f);
        var cleanup = new List<GameObject>();
        StartCoroutine(Fade(1f, 0f, 0.5f));
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
        StartCoroutine(FadeAt(2.95f, 0.7f));   // soft fade to black into orbit, everything still moving
        yield return Shot(3.7f, k => rocket.transform.position + off + Vector3.down * rocketH * 0.6f * k, k => rocket.transform.position + Vector3.up * rocketH * (0.35f - 0.25f * k), 38f, 46f, 0.04f, high);
        foreach (var f in fx) if (f) Destroy(f.gameObject);
        fx.Clear();
        if (dropped) Destroy(dropped.gameObject);
        if (rocket) rocket.SetActive(false);
        if (AscendLaunchSite.CurrentPad) AscendLaunchSite.CurrentPad.SetActive(false);
    }

    // billowing exhaust cloud rolling out across the pad at liftoff
    IEnumerator GroundCloud()
    {
        Vector3 c = site + Vector3.up * 2f;
        // engine glow: lights the smoke and the pad orange, flickering
        var glowL = new GameObject("Engine Glow").AddComponent<Light>();
        glowL.type = LightType.Point; glowL.color = new Color(1f, 0.55f, 0.2f); glowL.range = rocketH * 0.9f; glowL.shadows = LightShadows.None;
        glowL.transform.SetParent(rocket.transform, false);
        glowL.transform.position = site + Vector3.up * 0.5f;
        StartCoroutine(Flicker(glowL));
        for (int wave = 0; wave < 5; wave++)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = (i / 5f + wave * 0.11f) * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a));
                AscendFX.Puff(c + dir * (4f + wave * 3f), 6f + wave * 1.2f, dir, dir * (10f + wave * 2f));
            }
            yield return new WaitForSeconds(0.45f);
        }
    }

    IEnumerator Flicker(Light l)
    {
        for (float a = 0f; l && a < 9f; a += Time.deltaTime)
        {
            l.intensity = Mathf.Min(1f, a * 3f) * (14f + 6f * Mathf.PerlinNoise(a * 9f, 0.3f)) * Mathf.Clamp01(1.5f - a / 6f);
            yield return null;
        }
        if (l) Destroy(l.gameObject);
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
        var satGo = ClassicSatellite();
        satGo.transform.position = RigOrigin;
        satGo.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
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
        StartCoroutine(Fade(1f, 0f, 1f));
        float t = 0f, secs = 5.5f;
        StartCoroutine(FadeAt(5f, 0.5f));
        while (t < secs)
        {
            float u = t / secs, k = 0.4f * Ease(u) + 0.6f * u;
            Quaternion rot = Quaternion.LookRotation(lookEarth - camEarth + Vector3.up * 900f * (1f - k), Vector3.up) * Quaternion.Euler(-4f * k, 6f * k, 0f);
            spaceCam.transform.position = camEarth; spaceCam.transform.rotation = rot;
            rigCam.transform.rotation = rot; rigCam.fieldOfView = spaceCam.fieldOfView = Mathf.Lerp(48f, 42f, k);
            // the satellite drifts across the frame, slowly turning
            rigCam.transform.position = sb.center - rot * Vector3.forward * sz * Mathf.Lerp(1.9f, 1.45f, k) + rot * Vector3.right * sz * Mathf.Lerp(0.45f, -0.25f, k);
            satGo.transform.RotateAround(sb.center, Vector3.up, 5f * Time.deltaTime);
            earth.Spin(0.4f * Time.deltaTime, Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        fade.alpha = 1f;
    }

    // ───────────────────────── 5. TITLE (28-30 s) ─────────────────────────
    // Pure black. A beat of silence. A white line cuts the dark, ASCEND slams together out of wide-spaced letters,
    // a white glow pulses through it, the tagline rises underneath. Hold. Cut to black.
    IEnumerator TitleCard()
    {
        Caption("", "");
        var g = title.transform.parent.GetComponent<CanvasGroup>();
        fade.alpha = 1f; g.alpha = 1f;
        title.text = "ASCEND"; title.alpha = 0f; glow.text = "ASCEND"; glow.alpha = 0f;
        tagline.text = "DESIGN.     LAUNCH.     EXPLORE."; tagline.alpha = 0f;
        line.sizeDelta = new Vector2(0f, 2f);
        yield return new WaitForSeconds(0.35f);                                   // silence
        for (float a = 0f; a < 0.45f; a += Time.deltaTime)                       // the line cuts across
        { line.sizeDelta = new Vector2(Mathf.Lerp(0f, 1100f, 1f - Mathf.Pow(1f - a / 0.45f, 3f)), 2f); yield return null; }
        line.sizeDelta = new Vector2(1100f, 2f);
        var tr = (RectTransform)title.transform;
        for (float a = 0f; a < 1.1f; a += Time.deltaTime)                        // letters collapse together
        {
            float k = 1f - Mathf.Pow(1f - a / 1.1f, 4f);
            title.alpha = Mathf.Clamp01(a / 0.5f);
            title.characterSpacing = glow.characterSpacing = Mathf.Lerp(140f, 34f, k);
            tr.localScale = glow.transform.localScale = Vector3.one * Mathf.Lerp(1.18f, 1f, k);
            yield return null;
        }
        title.characterSpacing = glow.characterSpacing = 34f;
        for (float a = 0f; a < 0.6f; a += Time.deltaTime)                        // the glow pulse + tagline
        {
            float k = a / 0.6f;
            glow.alpha = Mathf.Sin(k * Mathf.PI) * 0.85f;
            glow.transform.localScale = Vector3.one * (1f + 0.06f * k);
            tagline.alpha = Mathf.Clamp01(k * 1.5f);
            ((RectTransform)tagline.transform).anchoredPosition = new Vector2(0f, Mathf.Lerp(-100f, -110f, Ease(k)));
            yield return null;
        }
        glow.alpha = 0f; tagline.alpha = 1f;
        for (float a = 0f; a < 1f; a += Time.deltaTime)                          // hold, slow push in
        { tr.localScale = Vector3.one * (1f + 0.025f * a); yield return null; }
        g.alpha = 0f;                                                            // hard cut
        yield return new WaitForSeconds(0.25f);
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
        // soft dark band rising from the bottom bar, so white captions read over a bright room or sky
        var band = new GameObject("Caption Shade", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        band.transform.SetParent(root, false); band.raycastTarget = false; band.texture = ShadeTexture(); band.color = Color.white;
        band.rectTransform.anchorMin = new Vector2(0f, 0f); band.rectTransform.anchorMax = new Vector2(1f, 0f); band.rectTransform.pivot = new Vector2(0.5f, 0f);
        band.rectTransform.sizeDelta = new Vector2(0f, 140f); band.rectTransform.anchoredPosition = new Vector2(0f, 118f);
        caption = MissionUI.Txt(root, "", 34, Color.white, FontStyles.Bold);
        caption.characterSpacing = 16f; caption.alignment = TextAlignmentOptions.Center;
        caption.outlineWidth = 0.22f; caption.outlineColor = new Color32(0, 0, 0, 230);
        MissionUI.Place(caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 70), new Vector2(1700, 56));   // inside the black bar: always readable
        sub = MissionUI.Lbl(root, "", 15); sub.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(1700, 30));
        count = MissionUI.Txt(root, "", 170, Color.white, FontStyles.Bold); count.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(count.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 260));
        var skip = MissionUI.Txt(root, "PRESS <color=#FFFFFF><b>S</b></color> TO SKIP", 16, MissionUI.Label, FontStyles.Normal); skip.characterSpacing = 8f;
        MissionUI.Place(skip.rectTransform, new Vector2(1f, 1f), new Vector2(-40, -44), new Vector2(420, 30)); skip.alignment = TextAlignmentOptions.Right;

        var f = MissionUI.Solid(root, Color.black, false, "Fade"); MissionUI.Stretch(f.rectTransform); fade = f.gameObject.AddComponent<CanvasGroup>(); fade.blocksRaycasts = false;
        var fl = MissionUI.Solid(root, Color.white, false, "Flash"); MissionUI.Stretch(fl.rectTransform); flash = fl.gameObject.AddComponent<CanvasGroup>(); flash.alpha = 0f; flash.blocksRaycasts = false;

        var tg = MissionUI.Rect("Title Card", root); MissionUI.Stretch(tg); var tcg = tg.gameObject.AddComponent<CanvasGroup>(); tcg.alpha = 0f; tcg.blocksRaycasts = false;
        MissionUI.Stretch(MissionUI.Solid(tg, Color.black, false, "Black").rectTransform);
        glow = MissionUI.Txt(tg, "", 170, Color.white, FontStyles.Bold); glow.alignment = TextAlignmentOptions.Center;
        MissionUI.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1900, 240));
        glow.outlineWidth = 0.35f; glow.outlineColor = new Color32(255, 255, 255, 140);   // soft white halo
        title = MissionUI.Txt(tg, "", 170, Color.white, FontStyles.Bold); title.alignment = TextAlignmentOptions.Center; title.characterSpacing = 34f;
        MissionUI.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1900, 240));
        line = MissionUI.Solid(tg, Color.white, false, "Line").rectTransform;
        MissionUI.Place(line, new Vector2(0.5f, 0.5f), new Vector2(0, -62), new Vector2(0, 2));
        tagline = MissionUI.Lbl(tg, "", 30); tagline.color = new Color(1f, 1f, 1f, 0.85f); tagline.alignment = TextAlignmentOptions.Center; tagline.characterSpacing = 18f;
        MissionUI.Place(tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(1600, 40));
        f.transform.SetAsLastSibling(); fl.transform.SetAsLastSibling(); tg.SetAsLastSibling();
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

    IEnumerator FadeAt(float delay, float secs) { yield return new WaitForSeconds(delay); yield return Fade(0f, 1f, secs); }

    IEnumerator Fade(float from, float to, float secs)
    {
        for (float a = 0f; a < secs; a += Time.deltaTime) { fade.alpha = Mathf.Lerp(from, to, Ease(a / secs)); yield return null; }   // eased: no hard edges
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
    static Texture2D ShadeTexture()
    {
        var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) { float k = 1f - y / 63f; t.SetPixel(0, y, new Color(0f, 0f, 0f, 0.72f * k * k)); }
        t.Apply(); return t;
    }

    // A classic small satellite: gold-foil body, two blue solar wings on booms, a white dish pointing at Earth.
    GameObject ClassicSatellite()
    {
        var root = new GameObject("Trailer Satellite");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material gold = Mat(lit, new Color(0.86f, 0.63f, 0.22f), 1f, 0.62f, FoilTexture());
        Material panel = Mat(lit, new Color(0.45f, 0.55f, 1f), 0.15f, 0.55f, CellTexture());
        Material silver = Mat(lit, new Color(0.78f, 0.79f, 0.8f), 0.9f, 0.6f, null);
        Material white = Mat(lit, new Color(0.93f, 0.93f, 0.92f), 0f, 0.35f, null);
        Material dark = Mat(lit, new Color(0.08f, 0.08f, 0.09f), 0.2f, 0.4f, null);
        Part(root, PrimitiveType.Cube, gold, new Vector3(0f, 0f, 0f), new Vector3(1.1f, 1.3f, 1.1f), Vector3.zero);           // body
        Part(root, PrimitiveType.Cube, silver, new Vector3(0f, 0.68f, 0f), new Vector3(1.14f, 0.06f, 1.14f), Vector3.zero);   // top deck
        Part(root, PrimitiveType.Cube, silver, new Vector3(0f, -0.68f, 0f), new Vector3(1.14f, 0.06f, 1.14f), Vector3.zero);  // bottom deck
        for (int side = -1; side <= 1; side += 2)
        {
            Part(root, PrimitiveType.Cylinder, silver, new Vector3(side * 0.95f, 0f, 0f), new Vector3(0.05f, 0.4f, 0.05f), new Vector3(0f, 0f, 90f)); // boom
            for (int i = 0; i < 2; i++)
            {
                float x = side * (1.55f + i * 1.25f);
                Part(root, PrimitiveType.Cube, panel, new Vector3(x, 0f, 0f), new Vector3(1.2f, 0.03f, 0.95f), Vector3.zero);
                Part(root, PrimitiveType.Cube, silver, new Vector3(x, -0.02f, 0f), new Vector3(1.22f, 0.02f, 0.97f), Vector3.zero);
            }
        }
        // dish on a short mast, facing down to Earth
        Part(root, PrimitiveType.Cylinder, silver, new Vector3(0.25f, -0.85f, 0.2f), new Vector3(0.06f, 0.18f, 0.06f), Vector3.zero);
        Part(root, PrimitiveType.Sphere, white, new Vector3(0.25f, -1.05f, 0.2f), new Vector3(0.75f, 0.14f, 0.75f), Vector3.zero);
        Part(root, PrimitiveType.Cylinder, dark, new Vector3(0.25f, -1.2f, 0.2f), new Vector3(0.03f, 0.12f, 0.03f), Vector3.zero);
        // camera lens + small antenna whip
        Part(root, PrimitiveType.Cylinder, dark, new Vector3(-0.3f, -0.78f, -0.25f), new Vector3(0.28f, 0.1f, 0.28f), Vector3.zero);
        Part(root, PrimitiveType.Cylinder, silver, new Vector3(-0.35f, 0.95f, 0.3f), new Vector3(0.015f, 0.3f, 0.015f), Vector3.zero);
        foreach (var tr in root.GetComponentsInChildren<Transform>()) tr.gameObject.layer = RigLayer;
        return root;
    }

    static Material Mat(Shader sh, Color c, float metal, float smooth, Texture2D tex)
    {
        var m = new Material(sh); m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
        if (tex) m.SetTexture("_BaseMap", tex);
        return m;
    }

    static void Part(GameObject root, PrimitiveType type, Material m, Vector3 pos, Vector3 scale, Vector3 euler)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = euler;
        g.GetComponent<Renderer>().sharedMaterial = m;
        g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // crinkled gold foil (blanket)
    static Texture2D FoilTexture()
    {
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float n = Mathf.PerlinNoise(x * 0.18f, y * 0.18f) * 0.6f + Mathf.PerlinNoise(x * 0.5f + 9f, y * 0.5f) * 0.4f;
            float v = 0.75f + 0.35f * n; t.SetPixel(x, y, new Color(v, v, v));
        }
        t.Apply(); return t;
    }

    // blue solar cells with thin silver gaps
    static Texture2D CellTexture()
    {
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            bool gap = x % 8 == 0 || y % 8 == 0;
            t.SetPixel(x, y, gap ? new Color(0.6f, 0.62f, 0.66f) : new Color(0.06f, 0.12f, 0.32f + 0.05f * ((x / 8 + y / 8) % 2)));
        }
        t.Apply(); t.filterMode = FilterMode.Bilinear; return t;
    }

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
