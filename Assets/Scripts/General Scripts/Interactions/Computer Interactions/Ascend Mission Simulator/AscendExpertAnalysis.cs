// ═════════════════════════════════════════════════════════════════════════════
//  AscendExpertAnalysis.cs  -  EXPERT ANALYSIS inside the Ascend Mission Simulator
//  (optional; the normal simulator stays simple). Inspired by NASA's open-source
//  General Mission Analysis Tool, GMAT (software.nasa.gov, GSC-19468-1), using the
//  same standard astrodynamics:
//
//    ORBIT        Keplerian elements -> Cartesian state, period, speeds, energy,
//                 J2 node / perigee drift, sun-synchronous inclination, eclipse,
//                 ground track (two-body + J2 secular, Earth rotation)
//    LAUNCH       launch azimuth from site latitude + inclination, Earth-rotation
//                 boost, delta-v budget vs YOUR rocket
//    MANEUVERS    Hohmann transfer, plane change, combined burn
//    PROPULSION   Tsiolkovsky rocket equation, propellant needed, burn time
//    GROUND       ground-station coverage, pass length, data per pass
//
//  LOAD MY MISSION fills the fields from your approved rocket + confirmed flight plan.
//  SEND REPORT TO PHMAIL mails the whole analysis.
//  Everything is plain closed-form maths: cheap on any computer.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AscendExpertAnalysis : MonoBehaviour
{
    // ── constants (WGS-84 / EGM96 values, as used by GMAT) ──
    const double Mu = 3.986004418e14, Re = 6378137.0, J2 = 1.08262668e-3, WE = 7.2921159e-5, G0 = 9.80665;
    const double SunRate = 2.0 * Math.PI / (365.2422 * 86400.0);   // node rate for a sun-synchronous orbit

    static readonly Color Bg = AUI.Hex("#18191C"), Panel = AUI.Hex("#212226"), Panel2 = AUI.Hex("#2E2F35"), Txt = AUI.Hex("#E8EAED"),
        Sub = AUI.Hex("#9AA0A6"), Accent = AUI.Hex("#F07A26"), Blue = AUI.Hex("#8AB4F8");

    AscendCADData data;
    int tab;
    readonly List<(Button b, RectTransform page)> tabs = new List<(Button, RectTransform)>();
    TextMeshProUGUI output, plotTitle, srcNote;
    RawImage mapImg, overlayImg;
    Texture2D overlay; Color32[] px;
    const int MW = 720, MH = 360;
    readonly Dictionary<string, TMP_InputField> f = new Dictionary<string, TMP_InputField>();
    string lastReport = "";

    // ═════════════════════════════════════════════════════════════════════════
    public static AscendExpertAnalysis Open(RectTransform appRoot, AscendCADData data)
    {
        var ex = appRoot.GetComponentInChildren<AscendExpertAnalysis>(true);
        if (!ex)
        {
            var r = AUI.Rect("Expert Analysis", appRoot); AUI.Fill(r);
            ex = r.gameObject.AddComponent<AscendExpertAnalysis>();
            ex.data = data;
            ex.Build(r);
        }
        ex.gameObject.SetActive(true);
        ex.transform.SetAsLastSibling();
        ex.Recalc();
        return ex;
    }

    void Build(RectTransform r)
    {
        AUI.Img(r, Bg, false, true);
        // top bar
        var top = AUI.Rect("Top", r);
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one; top.pivot = new Vector2(0.5f, 1f); top.sizeDelta = new Vector2(0f, AUI.S(56f));
        AUI.Img(top, Panel); AUI.H(top, 14, 14, 8, 8, 8);
        var t = AUI.Text(top, "<color=#F07A26>EXPERT</color> ANALYSIS  <size=70%><color=#9AA0A6>// GMAT-STYLE MISSION MATHS (OPTIONAL)</color></size>", 19, Txt, FontStyles.Bold);
        t.textWrappingMode = TextWrappingModes.NoWrap; AUI.LE(t, AUI.S(520));
        string[] names = { "ORBIT", "LAUNCH", "MANEUVERS", "PROPULSION", "GROUND" };
        for (int i = 0; i < names.Length; i++) { int k = i; var b = AUI.Btn(top, names[i], 13, Txt, Panel2, () => SetTab(k), 112, 38); tabs.Add((b, null)); }
        AUI.LE(AUI.Rect("Spacer", top), -1, -1, 1);
        AUI.Btn(top, "LOAD MY MISSION", 13, Color.white, AUI.Hex("#2E7D5B"), LoadMission, 160, 38);
        AUI.Btn(top, "SEND REPORT TO PHMAIL", 13, Color.white, AUI.Hex("#0B57D0"), SendReport, 210, 38);
        AUI.Btn(top, "CLOSE  X", 13, Color.white, AUI.Hex("#8B2A2A"), () => gameObject.SetActive(false), 110, 38);

        // left: inputs
        float LW = AUI.S(380f), TB = AUI.S(56f);
        var left = AUI.Rect("Inputs", r);
        left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0f, 1f); left.pivot = new Vector2(0f, 0.5f);
        left.offsetMin = Vector2.zero; left.offsetMax = new Vector2(LW, -TB);
        AUI.Img(left, Panel);
        var lc = AUI.Scroll(left, 0, 0, 0, 0); AUI.V(lc, 16, 16, 14, 16, 8);
        for (int i = 0; i < tabs.Count; i++) { var p = AUI.Rect(names[i], lc); AUI.V(p, 0, 0, 0, 0, 8); tabs[i] = (tabs[i].b, p); }

        // ORBIT
        var P = tabs[0].page;
        Head(P, "ORBIT SHAPE (KEPLERIAN)");
        Field(P, "peri", "Perigee altitude", "km", "700");
        Field(P, "apo", "Apogee altitude", "km", "700");
        Field(P, "inc", "Inclination  i", "deg", "98.2");
        Field(P, "raan", "RAAN  (node)  Ω", "deg", "0");
        Field(P, "aop", "Arg. of perigee  ω", "deg", "0");
        Field(P, "ta", "True anomaly  ν", "deg", "0");
        Field(P, "orbits", "Ground track: orbits", "#", "3");
        Note(P, "Altitudes above the equator (Earth radius 6,378.137 km). 98.2° at 700 km is sun-synchronous, like many Earth-watching satellites.");
        // LAUNCH
        P = tabs[1].page;
        Head(P, "LAUNCH SITE");
        Field(P, "lat", "Site latitude", "deg", "15.76");
        Field(P, "lon", "Site longitude", "deg", "121.56");
        Head(P, "TARGET ORBIT");
        Field(P, "linc", "Target inclination", "deg", "98.2");
        Field(P, "lalt", "Target altitude (circular)", "km", "700");
        Field(P, "gloss", "Gravity loss (estimate)", "m/s", "1500");
        Field(P, "dloss", "Drag + steering loss", "m/s", "200");
        Field(P, "rdv", "Your rocket's delta-v", "m/s", "0");
        Note(P, "Default site: Baler, Aurora (east coast of Luzon). LOAD MY MISSION puts in your approved rocket's delta-v.");
        // MANEUVERS
        P = tabs[2].page;
        Head(P, "HOHMANN TRANSFER");
        Field(P, "h1", "From circular altitude", "km", "200");
        Field(P, "h2", "To circular altitude", "km", "700");
        Head(P, "PLANE CHANGE");
        Field(P, "di", "Inclination change  Δi", "deg", "0");
        Note(P, "Two burns: one to stretch the orbit, one at the top to round it out. A plane change is cheapest where you fly slowest (the high orbit).");
        // PROPULSION
        P = tabs[3].page;
        Head(P, "ENGINE + MASSES");
        Field(P, "isp", "Specific impulse  Isp", "s", "348");
        Field(P, "thrust", "Thrust", "kN", "981");
        Field(P, "mwet", "Mass at ignition (wet)", "kg", "116000");
        Field(P, "mdry", "Mass at burnout (dry)", "kg", "4500");
        Head(P, "OR: PROPELLANT FOR A GOAL");
        Field(P, "dvgoal", "Delta-v wanted", "m/s", "3000");
        Note(P, "Tsiolkovsky: Δv = Isp · g0 · ln(m wet / m dry). Defaults: a Falcon 9-like second stage.");
        // GROUND
        P = tabs[4].page;
        Head(P, "GROUND STATION");
        Field(P, "glat", "Station latitude", "deg", "14.65");
        Field(P, "glon", "Station longitude", "deg", "121.06");
        Field(P, "elev", "Minimum elevation", "deg", "10");
        Field(P, "galt", "Satellite altitude", "km", "700");
        Field(P, "rate", "Downlink rate", "kbps", "2000");
        Note(P, "Default station: Quezon City, Metro Manila. Below the minimum elevation, buildings and hills block the signal.");
        foreach (var kv in f) kv.Value.onValueChanged.AddListener(_ => Recalc());

        // right: plot + results
        var right = AUI.Rect("Results", r);
        right.anchorMin = Vector2.zero; right.anchorMax = Vector2.one; right.offsetMin = new Vector2(LW, 0f); right.offsetMax = new Vector2(0f, -TB);
        var plotBox = AUI.Rect("Plot", right);
        plotBox.anchorMin = new Vector2(0f, 1f); plotBox.anchorMax = Vector2.one; plotBox.pivot = new Vector2(0.5f, 1f);
        plotBox.sizeDelta = new Vector2(-28f, AUI.S(330f)); plotBox.anchoredPosition = new Vector2(0f, -12f);
        plotTitle = AUI.Text(plotBox, "", 13, Sub, FontStyles.Bold);
        plotTitle.rectTransform.anchorMin = new Vector2(0f, 1f); plotTitle.rectTransform.anchorMax = Vector2.one; plotTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
        plotTitle.rectTransform.sizeDelta = new Vector2(0f, AUI.S(22f));
        var frame = AUI.Rect("Frame", plotBox); AUI.Fill(frame, 0, 0, 0, AUI.S(26f));
        var fit = AUI.Rect("Fit", frame);
        fit.anchorMin = fit.anchorMax = new Vector2(0.5f, 0.5f);
        var arf = fit.gameObject.AddComponent<AspectRatioFitter>(); arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent; arf.aspectRatio = 2f;
        mapImg = AUI.Rect("Map", fit).gameObject.AddComponent<RawImage>(); AUI.Fill(mapImg.rectTransform); mapImg.raycastTarget = false;
        var em = Resources.Load<Material>("AscendEarth");
        if (em && em.HasProperty("_DayTex")) mapImg.texture = em.GetTexture("_DayTex");
        mapImg.color = new Color(0.55f, 0.55f, 0.6f);
        overlayImg = AUI.Rect("Overlay", fit).gameObject.AddComponent<RawImage>(); AUI.Fill(overlayImg.rectTransform); overlayImg.raycastTarget = false;
        overlay = new Texture2D(MW, MH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        px = new Color32[MW * MH]; overlayImg.texture = overlay;

        var resBox = AUI.Rect("Text", right); AUI.Fill(resBox, 14, 10, 14, AUI.S(352f));
        AUI.Img(resBox, Panel, true);
        var rc = AUI.Scroll(resBox, 0, 0, 0, 0); AUI.V(rc, 18, 18, 14, 16, 6);
        output = AUI.Text(rc, "", 13.5f, Txt); output.lineSpacing = 6f;
        srcNote = AUI.Text(rc, "<size=85%><color=#9AA0A6>Method: two-body Keplerian motion with the J2 (Earth oblateness) secular terms, as in NASA GMAT's analytic propagators. Results are first estimates for mission design; GMAT's full numerical propagators add drag, Sun/Moon gravity and higher harmonics.</color></size>", 12, Sub);
        SetTab(0);
    }

    void Head(RectTransform p, string s) { var t = AUI.Text(p, s, 13, Blue, FontStyles.Bold); AUI.LE(t, -1, AUI.S(26)); }
    void Note(RectTransform p, string s) { var t = AUI.Text(p, s, 11.5f, Sub); t.lineSpacing = 4f; }

    void Field(RectTransform p, string key, string label, string unit, string def)
    {
        var row = AUI.Rect(key, p); AUI.H(row, 0, 0, 0, 0, 8); AUI.LE(row, -1, AUI.S(34));
        var l = AUI.Text(row, label, 12.5f, Txt); l.textWrappingMode = TextWrappingModes.NoWrap; AUI.LE(l, -1, -1, 1); l.overflowMode = TextOverflowModes.Ellipsis;
        var fld = AUI.Field(row, def, 13, Txt, Panel2); AUI.LE(fld, AUI.S(110), AUI.S(32));
        fld.text = def; fld.contentType = TMP_InputField.ContentType.DecimalNumber;
        var u = AUI.Text(row, unit, 11.5f, Sub); u.textWrappingMode = TextWrappingModes.NoWrap; AUI.LE(u, AUI.S(40));
        f[key] = fld;
    }

    double V(string k, double fallback = 0)
    {
        if (!f.TryGetValue(k, out var fld)) return fallback;
        return double.TryParse(fld.text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
    void Set(string k, double v, string fmt = "0.##") { if (f.TryGetValue(k, out var fld)) fld.SetTextWithoutNotify(v.ToString(fmt, CultureInfo.InvariantCulture)); }

    void SetTab(int k)
    {
        tab = k;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].page) tabs[i].page.gameObject.SetActive(i == k);
            tabs[i].b.GetComponent<Image>().color = i == k ? Accent : Panel2;
        }
        Recalc();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Calculations
    // ═════════════════════════════════════════════════════════════════════════
    static string N(double v, string fmt = "N0") => double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString(fmt, CultureInfo.InvariantCulture);
    static void L(StringBuilder sb, string label, string value, string note = "")
        => sb.Append($"<color=#9AA0A6>{label}</color><pos=48%><b>{value}</b>" + (note.Length > 0 ? $"  <size=85%><color=#9AA0A6>{note}</color></size>" : "") + "\n");
    static void H(StringBuilder sb, string s) => sb.Append($"\n<b><color=#8AB4F8>{s}</color></b>\n");
    const double D2R = Math.PI / 180.0;

    void Recalc()
    {
        if (!output) return;
        var sb = new StringBuilder();
        Clear();
        switch (tab)
        {
            case 0: Orbit(sb); break;
            case 1: Launch(sb); break;
            case 2: Maneuvers(sb); break;
            case 3: Propulsion(sb); break;
            default: Ground(sb); break;
        }
        output.text = sb.ToString();
        lastReport = sb.ToString();
        overlay.SetPixels32(px); overlay.Apply(false);
    }

    // ── ORBIT ──
    void Orbit(StringBuilder sb)
    {
        double hp = V("peri", 700), ha = Math.Max(hp, V("apo", 700)), inc = V("inc", 98.2) * D2R, raan = V("raan") * D2R, aop = V("aop") * D2R, ta = V("ta") * D2R;
        double rp = Re + hp * 1000, ra = Re + ha * 1000, a = (rp + ra) / 2, e = (ra - rp) / (ra + rp), p = a * (1 - e * e);
        double T = 2 * Math.PI * Math.Sqrt(a * a * a / Mu), n = 2 * Math.PI / T;
        double vp = Math.Sqrt(Mu * (2 / rp - 1 / a)), va = Math.Sqrt(Mu * (2 / ra - 1 / a));
        double eps = -Mu / (2 * a), h = Math.Sqrt(Mu * p);
        double k2 = 1.5 * n * J2 * (Re / p) * (Re / p);
        double dRaan = -k2 * Math.Cos(inc), dAop = 0.5 * k2 * (5 * Math.Cos(inc) * Math.Cos(inc) - 1);
        double cosSSO = -SunRate / k2;
        // state vector (ECI, J2000-like frame) from the elements
        State(a, e, inc, raan, aop, ta, out var r, out var v);
        double rmean = a, ecl = Math.Asin(Math.Min(1.0, Re / rmean)) / Math.PI * T;

        sb.Append("<b>ORBIT</b>  " + (e < 1e-4 ? "circular" : "elliptical") + $" {hp:0} x {ha:0} km, {inc / D2R:0.##}°\n");
        H(sb, "SHAPE AND SIZE");
        L(sb, "Semi-major axis  a", N(a / 1000, "N1") + " km");
        L(sb, "Eccentricity  e", N(e, "0.00000"));
        L(sb, "Semi-latus rectum  p", N(p / 1000, "N1") + " km");
        L(sb, "Period  T", $"{T / 60:0.00} min", $"{86400 / T:0.00} orbits a day");
        H(sb, "SPEED AND ENERGY");
        L(sb, "Speed at perigee", N(vp, "N1") + " m/s", $"{vp * 3.6:N0} km/h");
        L(sb, "Speed at apogee", N(va, "N1") + " m/s");
        L(sb, "Specific energy  ε", N(eps / 1e6, "0.000") + " MJ/kg");
        L(sb, "Angular momentum  h", N(h / 1e6, "N1") + " km²/s");
        L(sb, "Escape speed here", N(Math.Sqrt(2 * Mu / rp), "N0") + " m/s", "speed needed to leave Earth for good");
        H(sb, "STATE VECTOR  (ECI, AT ν = " + N(ta / D2R, "0.#") + "°)");
        L(sb, "Position  X, Y, Z", $"{r.x / 1000:N1}, {r.y / 1000:N1}, {r.z / 1000:N1} km");
        L(sb, "Velocity  VX, VY, VZ", $"{v.x:N1}, {v.y:N1}, {v.z:N1} m/s");
        H(sb, "EARTH'S BULGE (J2) EFFECTS");
        L(sb, "Node drift  dΩ/dt", N(dRaan / D2R * 86400, "0.0000") + " °/day", dRaan > 0 ? "moves east" : "moves west");
        L(sb, "Perigee drift  dω/dt", N(dAop / D2R * 86400, "0.0000") + " °/day");
        L(sb, "Sun-synchronous inclination", Math.Abs(cosSSO) <= 1 ? N(Math.Acos(cosSSO) / D2R, "0.00") + "°" : "none at this height",
            Math.Abs(dRaan - SunRate) < SunRate * 0.02 ? "<color=#3FB950>this orbit IS sun-synchronous</color>" : "keeps the same local time every pass");
        H(sb, "SUNLIGHT");
        L(sb, "Longest eclipse (shadow)", $"{ecl / 60:0.0} min", $"{ecl / T * 100:0}% of each orbit: the battery must cover it");

        // ground track
        int orbits = Mathf.Clamp((int)V("orbits", 3), 1, 16);
        plotTitle.text = $"GROUND TRACK   //   {orbits} ORBIT{(orbits > 1 ? "S" : "")}   //   START ●   (two-body + J2)";
        Track(a, e, inc, raan, aop, ta, n, dRaan, dAop, T * orbits, new Color32(255, 200, 80, 255));
    }

    static void State(double a, double e, double i, double O, double w, double nu, out Vector3d r, out Vector3d v)
    {
        double p = a * (1 - e * e), rr = p / (1 + e * Math.Cos(nu));
        double xp = rr * Math.Cos(nu), yp = rr * Math.Sin(nu);
        double k = Math.Sqrt(Mu / p), vxp = -k * Math.Sin(nu), vyp = k * (e + Math.Cos(nu));
        double cO = Math.Cos(O), sO = Math.Sin(O), ci = Math.Cos(i), si = Math.Sin(i), cw = Math.Cos(w), sw = Math.Sin(w);
        double r11 = cO * cw - sO * sw * ci, r12 = -cO * sw - sO * cw * ci;
        double r21 = sO * cw + cO * sw * ci, r22 = -sO * sw + cO * cw * ci;
        double r31 = sw * si, r32 = cw * si;
        r = new Vector3d(r11 * xp + r12 * yp, r21 * xp + r22 * yp, r31 * xp + r32 * yp);
        v = new Vector3d(r11 * vxp + r12 * vyp, r21 * vxp + r22 * vyp, r31 * vxp + r32 * vyp);
    }

    // propagate (Kepler's equation + J2 secular drift) and draw lat/lon on the map
    void Track(double a, double e, double inc, double raan, double aop, double ta0, double n, double dRaan, double dAop, double span, Color32 col)
    {
        double E0 = 2 * Math.Atan(Math.Sqrt((1 - e) / (1 + e)) * Math.Tan(ta0 / 2)), M0 = E0 - e * Math.Sin(E0);
        int steps = 1400; Vector2 prev = Vector2.zero; bool has = false, first = true;
        for (int s = 0; s <= steps; s++)
        {
            double t = span * s / steps, M = M0 + n * t, E = M;
            for (int it = 0; it < 8; it++) E -= (E - e * Math.Sin(E) - M) / (1 - e * Math.Cos(E));
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2));
            State(a, e, inc, raan + dRaan * t, aop + dAop * t, nu, out var r, out _);
            double lat = Math.Asin(r.z / r.mag), lon = Math.Atan2(r.y, r.x) - WE * t;
            var q = LatLon(lat / D2R, lon / D2R);
            if (has && Mathf.Abs(q.x - prev.x) < MW * 0.5f) Line(prev, q, col);
            if (first) { Dot(q, new Color32(255, 255, 255, 255), 4); first = false; }
            prev = q; has = true;
        }
    }

    static Vector2 LatLon(double lat, double lon)
    {
        lon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        return new Vector2((float)((lon + 180.0) / 360.0 * (MW - 1)), (float)((lat + 90.0) / 180.0 * (MH - 1)));
    }

    // ── LAUNCH ──
    void Launch(StringBuilder sb)
    {
        double lat = V("lat", 15.76), lon = V("lon", 121.56), inc = V("linc", 98.2), alt = V("lalt", 700);
        double r = Re + alt * 1000, vOrb = Math.Sqrt(Mu / r), vRot = WE * Re * Math.Cos(lat * D2R);
        bool reach = inc >= Math.Abs(lat) - 1e-6 && inc <= 180 - Math.Abs(lat) + 1e-6;
        double incUse = reach ? inc : Math.Abs(lat);
        double sinB = Math.Cos(incUse * D2R) / Math.Cos(lat * D2R);
        double beta = Math.Asin(Math.Max(-1, Math.Min(1, sinB)));      // inertial azimuth (from north, toward east)
        double vx = vOrb * Math.Sin(beta) - vRot, vy = vOrb * Math.Cos(beta);
        double vNeed = Math.Sqrt(vx * vx + vy * vy), betaR = Math.Atan2(vx, vy);
        double raise = Math.Sqrt(2 * Mu * (1 / Re - 1 / r));            // climbing out of the gravity well (ideal)
        double budget = vNeed + V("gloss", 1500) + V("dloss", 200);
        double planeFix = reach ? 0 : 2 * vOrb * Math.Sin(Math.Abs(inc - Math.Abs(lat)) * D2R / 2);
        double total = budget + planeFix, rdv = V("rdv");

        sb.Append($"<b>LAUNCH</b>  from {lat:0.00}°, {lon:0.00}° into a {alt:0} km, {inc:0.##}° orbit\n");
        H(sb, "WHICH WAY TO FLY");
        if (!reach) sb.Append($"<color=#F85149>From latitude {Math.Abs(lat):0.##}° the lowest inclination you can launch into directly is {Math.Abs(lat):0.##}°.</color> A plane change in orbit costs about <b>{planeFix:N0} m/s</b> more.\n");
        L(sb, "Launch azimuth (inertial)", $"{beta / D2R:0.00}° from north", beta / D2R < 0 ? "toward the west" : "toward the east");
        L(sb, "Launch azimuth (on the pad)", $"{betaR / D2R:0.00}° from north", "corrected for Earth's spin");
        L(sb, "Also possible", $"{180 - betaR / D2R:0.00}°", "the same orbit, flown the other way");
        H(sb, "SPEED");
        L(sb, "Orbital speed at " + alt.ToString("0") + " km", N(vOrb, "N0") + " m/s", $"{vOrb * 3.6:N0} km/h");
        L(sb, "Earth's spin at the site", N(vRot, "N0") + " m/s", "free speed from the rotating Earth");
        L(sb, "Spin help for this orbit", N(vOrb - vNeed, "N0") + " m/s", vOrb - vNeed >= 0 ? "eastward launches save fuel" : "westward (retrograde) launches pay extra");
        L(sb, "Speed to gain", N(vNeed, "N0") + " m/s");
        L(sb, "Climb (ideal, no losses)", N(raise, "N0") + " m/s", "energy to lift to " + alt.ToString("0") + " km");
        H(sb, "DELTA-V BUDGET");
        L(sb, "Speed to gain", N(vNeed, "N0") + " m/s");
        L(sb, "+ Gravity loss", N(V("gloss", 1500), "N0") + " m/s", "fighting gravity on the way up");
        L(sb, "+ Drag + steering", N(V("dloss", 200), "N0") + " m/s");
        if (planeFix > 0) L(sb, "+ Plane change", N(planeFix, "N0") + " m/s");
        L(sb, "= TOTAL NEEDED", "<color=#FFC850>" + N(total, "N0") + " m/s</color>");
        if (rdv > 0) L(sb, "Your rocket has", (rdv >= total ? "<color=#3FB950>" : "<color=#F85149>") + N(rdv, "N0") + " m/s</color>",
            rdv >= total ? $"margin {rdv - total:N0} m/s ({(rdv - total) / total * 100:0.#}%)" : $"short by {total - rdv:N0} m/s");

        plotTitle.text = "LAUNCH SITE ●   //   FIRST ORBIT AFTER LAUNCH (FLOWN FROM THE SITE)";
        // one orbit that passes over the site at t = 0, heading along the launch azimuth
        double i = incUse * D2R, u = Math.Asin(Math.Max(-1, Math.Min(1, Math.Sin(lat * D2R) / Math.Sin(i))));
        if (Math.Cos(beta) < 0) u = Math.PI - u;
        double dl = Math.Atan2(Math.Cos(i) * Math.Sin(u), Math.Cos(u));
        double raan = lon * D2R - dl;
        double n = Math.Sqrt(Mu / (r * r * r)), k2 = 1.5 * n * J2 * (Re / r) * (Re / r);
        Track(r, 0, i, raan, 0, u, n, -k2 * Math.Cos(i), 0, 2 * Math.PI / n, new Color32(255, 200, 80, 255));
        Dot(LatLon(lat, lon), new Color32(80, 220, 120, 255), 5);
    }

    // ── MANEUVERS ──
    void Maneuvers(StringBuilder sb)
    {
        double h1 = V("h1", 200), h2 = V("h2", 700), di = V("di") * D2R;
        double r1 = Re + h1 * 1000, r2 = Re + h2 * 1000, at = (r1 + r2) / 2;
        double v1 = Math.Sqrt(Mu / r1), v2 = Math.Sqrt(Mu / r2);
        double vtp = Math.Sqrt(Mu * (2 / r1 - 1 / at)), vta = Math.Sqrt(Mu * (2 / r2 - 1 / at));
        double dv1 = Math.Abs(vtp - v1), dv2 = Math.Abs(v2 - vta), tof = Math.PI * Math.Sqrt(at * at * at / Mu);
        double planeOnly = 2 * v2 * Math.Sin(di / 2);
        double combined = Math.Sqrt(vta * vta + v2 * v2 - 2 * vta * v2 * Math.Cos(di));
        double planeLow = 2 * v1 * Math.Sin(di / 2);

        sb.Append($"<b>MANEUVERS</b>  {h1:0} km → {h2:0} km" + (di > 0 ? $", Δi {di / D2R:0.##}°" : "") + "\n");
        H(sb, "HOHMANN TRANSFER  (2 BURNS)");
        L(sb, "Circular speed (start)", N(v1, "N1") + " m/s");
        L(sb, "Burn 1 (stretch the orbit)", N(dv1, "N1") + " m/s", h2 >= h1 ? "prograde" : "retrograde");
        L(sb, "Transfer orbit", $"{h1:0} x {h2:0} km", $"e = {(Math.Abs(r2 - r1) / (r1 + r2)):0.0000}");
        L(sb, "Coast (half an orbit)", $"{tof / 60:0.0} min");
        L(sb, "Burn 2 (round it out)", N(dv2, "N1") + " m/s");
        L(sb, "Circular speed (end)", N(v2, "N1") + " m/s");
        L(sb, "TOTAL", "<color=#FFC850>" + N(dv1 + dv2, "N1") + " m/s</color>");
        if (di > 0)
        {
            H(sb, "PLANE CHANGE  Δi = " + N(di / D2R, "0.##") + "°");
            L(sb, "Alone, in the high orbit", N(planeOnly, "N0") + " m/s");
            L(sb, "Alone, in the low orbit", N(planeLow, "N0") + " m/s", "more expensive: you fly faster there");
            L(sb, "Combined with burn 2", N(combined, "N0") + " m/s", $"saves {dv2 + planeOnly - combined:N0} m/s");
            L(sb, "TOTAL WITH PLANE CHANGE", "<color=#FFC850>" + N(dv1 + combined, "N0") + " m/s</color>");
        }
        sb.Append("\n<size=90%><color=#9AA0A6>Why it works: a burn at one side of the orbit raises the opposite side. Two small burns are far cheaper than flying straight up.</color></size>\n");

        // diagram: start orbit, end orbit, transfer half-ellipse (to scale)
        plotTitle.text = "TRANSFER DIAGRAM (TO SCALE)   //   START (BLUE)   END (GREEN)   TRANSFER (ORANGE)";
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(6, 9, 14, 240);
        float scale = (MH * 0.46f) / (float)Math.Max(r1, r2); Vector2 c = new Vector2(MW * 0.5f, MH * 0.5f);
        Circle(c, (float)Re * scale, new Color32(40, 90, 160, 255), true);
        Circle(c, (float)r1 * scale, new Color32(115, 215, 255, 255), false);
        Circle(c, (float)r2 * scale, new Color32(80, 220, 120, 255), false);
        double et = Math.Abs(r2 - r1) / (r1 + r2); Vector2 prev = Vector2.zero; bool has = false;
        for (int s = 0; s <= 180; s++)
        {
            double nu = s * D2R, rr = at * (1 - et * et) / (1 + et * Math.Cos(nu));
            float sign = r2 >= r1 ? 1f : -1f;
            var q = c + new Vector2((float)(sign * rr * Math.Cos(nu)) * scale, (float)(rr * Math.Sin(nu)) * scale);
            if (has) Line(prev, q, new Color32(255, 170, 60, 255)); prev = q; has = true;
        }
        Dot(c + new Vector2((float)r1 * scale * (r2 >= r1 ? 1 : -1), 0), Color.white, 4);
        Dot(c + new Vector2(-(float)r2 * scale * (r2 >= r1 ? 1 : -1), 0), Color.white, 4);
    }

    // ── PROPULSION ──
    void Propulsion(StringBuilder sb)
    {
        double isp = V("isp", 348), F = V("thrust", 981) * 1000, mw = V("mwet", 116000), md = Math.Max(1, V("mdry", 4500)), goal = V("dvgoal", 3000);
        double ve = isp * G0, dv = ve * Math.Log(mw / md), mp = mw - md, mdot = F / ve, burn = mp / mdot;
        double mpGoal = md * (Math.Exp(goal / ve) - 1);
        sb.Append($"<b>PROPULSION</b>  Isp {isp:0} s, {F / 1000:N0} kN\n");
        H(sb, "THE ROCKET EQUATION");
        L(sb, "Exhaust speed  ve = Isp·g0", N(ve, "N0") + " m/s");
        L(sb, "Mass ratio  m wet / m dry", N(mw / md, "0.00"));
        L(sb, "Propellant", N(mp, "N0") + " kg", $"{mp / mw * 100:0.#}% of the wet mass");
        L(sb, "DELTA-V", "<color=#FFC850>" + N(dv, "N0") + " m/s</color>", "Δv = ve · ln(m wet / m dry)");
        H(sb, "BURN");
        L(sb, "Propellant flow", N(mdot, "N1") + " kg/s");
        L(sb, "Burn time at full thrust", $"{burn:0.0} s", $"{burn / 60:0.0} min");
        L(sb, "Acceleration at ignition", N(F / mw / G0, "0.00") + " g");
        L(sb, "Acceleration at burnout", N(F / md / G0, "0.00") + " g", F / md / G0 > 6 ? "<color=#FFC850>high: real stages throttle down</color>" : "");
        H(sb, "PROPELLANT FOR " + N(goal, "N0") + " m/s");
        L(sb, "Propellant needed", N(mpGoal, "N0") + " kg", $"with {md:N0} kg dry mass");
        L(sb, "Wet mass needed", N(md + mpGoal, "N0") + " kg");
        if (mpGoal > mp) sb.Append($"<color=#F85149>Your stage holds {mp - mpGoal:N0} kg too little for this.</color>\n");
        sb.Append("\n<size=90%><color=#9AA0A6>Doubling the propellant does NOT double the delta-v: it grows with the logarithm of the mass ratio. That's why rockets use stages.</color></size>\n");

        // bar chart: delta-v vs propellant fraction
        plotTitle.text = "DELTA-V vs PROPELLANT FRACTION (SAME ENGINE)   //   YOU ARE HERE ●";
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(6, 9, 14, 240);
        double maxDv = ve * Math.Log(1 / 0.03);
        Vector2 prev = Vector2.zero; bool has = false;
        for (int s = 0; s <= 97; s++)
        {
            double fr = s / 100.0, d = ve * Math.Log(1 / (1 - fr));
            var q = new Vector2(20 + (float)fr * (MW - 40), 20 + (float)(d / maxDv) * (MH - 40));
            if (has) Line(prev, q, new Color32(115, 215, 255, 255)); prev = q; has = true;
        }
        double myFr = mp / mw;
        Dot(new Vector2(20 + (float)myFr * (MW - 40), 20 + (float)(dv / maxDv) * (MH - 40)), new Color32(255, 200, 80, 255), 6);
        double orbitLine = 9400.0 / maxDv * (MH - 40) + 20;
        for (int x = 20; x < MW - 20; x += 2) if (orbitLine < MH) Dot(new Vector2(x, (float)orbitLine), new Color32(80, 220, 120, 200), 1);
    }

    // ── GROUND STATION ──
    void Ground(StringBuilder sb)
    {
        double glat = V("glat", 14.65), glon = V("glon", 121.06), el = V("elev", 10) * D2R, alt = V("galt", 700), rate = V("rate", 2000);
        double r = Re + alt * 1000;
        double lam = Math.Acos(Re * Math.Cos(el) / r) - el;                // Earth central angle of the coverage circle
        double radius = Re * lam / 1000, T = 2 * Math.PI * Math.Sqrt(r * r * r / Mu);
        double maxPass = 2 * lam / (2 * Math.PI) * T, avgPass = maxPass * Math.PI / 4;
        double slant = Math.Sqrt(r * r - Re * Re * Math.Cos(el) * Math.Cos(el)) - Re * Math.Sin(el);
        double mb = rate * avgPass / 8000.0;
        double passesDay = 2 * (86400 / T) * (2 * lam) / (2 * Math.PI * Math.Max(0.2, Math.Cos(glat * D2R)));   // near-polar orbit: up and down tracks, packed closer away from the equator
        sb.Append($"<b>GROUND STATION</b>  {glat:0.00}°, {glon:0.00}°, min elevation {el / D2R:0}°\n");
        H(sb, "WHAT IT CAN SEE");
        L(sb, "Coverage radius", N(radius, "N0") + " km", "satellites inside this circle can talk to it");
        L(sb, "Farthest distance (slant range)", N(slant / 1000, "N0") + " km");
        L(sb, "Radio delay at that range", N(slant / 299792.458, "0.00") + " ms");
        H(sb, "PASSES");
        L(sb, "Longest pass (overhead)", $"{maxPass / 60:0.0} min");
        L(sb, "Typical pass", $"{avgPass / 60:0.0} min");
        L(sb, "Passes per day (rough)", N(passesDay, "0.#"), "polar orbits: more passes far from the equator");
        H(sb, "DATA");
        L(sb, "Data in a typical pass", N(mb, "N1") + " MB", $"at {rate:N0} kbps");
        L(sb, "Data per day (rough)", N(mb * passesDay, "N0") + " MB");
        L(sb, "Photos per day (≈ 5 MB each)", N(mb * passesDay / 5, "N0"));

        plotTitle.text = "COVERAGE CIRCLE (GREEN) AT " + alt.ToString("0") + " km   //   STATION ●";
        // coverage circle on the map
        var c = new Color32(80, 220, 120, 255);
        for (int s = 0; s <= 360; s++)
        {
            double az = s * D2R, la = glat * D2R, lo = glon * D2R;
            double plat = Math.Asin(Math.Sin(la) * Math.Cos(lam) + Math.Cos(la) * Math.Sin(lam) * Math.Cos(az));
            double plon = lo + Math.Atan2(Math.Sin(az) * Math.Sin(lam) * Math.Cos(la), Math.Cos(lam) - Math.Sin(la) * Math.Sin(plat));
            Dot(LatLon(plat / D2R, plon / D2R), c, 2);
        }
        Dot(LatLon(glat, glon), new Color32(255, 255, 255, 255), 5);
    }

    // ═════════════════════════════════════════════════════════════════════════
    void LoadMission()
    {
        var plan = AscendTrajectoryControl.ConfirmedPlan;
        double alt = plan != null ? plan.targetKm : 700;
        if (plan != null && plan.mission == AscendSimMission.SkySensor) alt = 400;
        foreach (var k in new[] { "peri", "apo", "lalt", "h2", "galt" }) Set(k, alt, "0");
        CADStats st = AscendMissionControl.ApprovedStats;
        CADDesign rocket = AscendMissionControl.ApprovedRocket;
        string from = "your APPROVED rocket";
        if (st == null || rocket == null)
        {
            var site = AscendLaunchSite.Current ? AscendLaunchSite.Current.GetComponent<AscendRocket>() : null;
            if (site && site.rocket != null && data)
            {
                var ps = site.payload != null && !site.payload.IsEmpty ? CADStats.Payload(site.payload, data).wetMass : 0f;
                st = CADStats.Rocket(site.rocket, data, ps, AscendMissionGuide.RequiredDeltaV(data)); from = "the rocket on the launch pad";
            }
        }
        if (st != null && st.stages.Count > 0)
        {
            Set("rdv", st.deltaV, "0");
            var top = st.stages[st.stages.Count - 1];
            double m0 = top.startMass, mf = top.startMass - top.propMass;
            if (top.deltaV > 0 && mf > 0)
            {
                double isp = top.deltaV / Math.Log(m0 / mf) / G0;
                Set("isp", isp, "0"); Set("mwet", m0, "0"); Set("mdry", mf, "0"); Set("thrust", top.thrustkN, "0");
            }
            output.text = $"<color=#3FB950>Loaded {from}: {rocket?.name ?? "rocket"}</color>, delta-v {st.deltaV:N0} m/s, upper stage and orbit height {alt:0} km.\n\n";
        }
        else output.text = "<color=#FFC850>No approved rocket yet: only the orbit height was loaded.</color>\n\n";
        Recalc();
    }

    void SendReport()
    {
        if (!data) return;
        string tabName = new[] { "Orbit", "Launch", "Maneuvers", "Propulsion", "Ground station" }[tab];
        string body = "Expert analysis (GMAT-style) - " + tabName + "\n\n" + lastReport.Replace("<pos=48%>", "   ") +
                      "\nMethod: two-body Keplerian motion with J2 secular terms (NASA GMAT analytic models). First estimates for mission design.";
        AscendMail.Send(data.playerEmail, data.playerEmail, "Expert analysis: " + tabName, body);
        output.text = "<color=#3FB950>Report sent to your PhMail.</color>\n\n" + lastReport;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  drawing
    // ═════════════════════════════════════════════════════════════════════════
    void Clear()
    {
        var clear = new Color32(0, 0, 0, 0); var grid = new Color32(255, 255, 255, 28);
        for (int i = 0; i < px.Length; i++) px[i] = clear;
        for (int lon = -150; lon <= 150; lon += 30) { var q = LatLon(0, lon); for (int y = 0; y < MH; y++) px[y * MW + (int)q.x] = grid; }
        for (int lat = -60; lat <= 60; lat += 30) { var q = LatLon(lat, 0); for (int x = 0; x < MW; x++) px[(int)q.y * MW + x] = grid; }
        bool map = tab == 0 || tab == 1 || tab == 4;
        if (mapImg) mapImg.enabled = map;
        if (overlayImg) overlayImg.gameObject.SetActive(true);
    }

    void Line(Vector2 a, Vector2 b, Color32 c) { int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b))); for (int i = 0; i <= n; i++) Dot(Vector2.Lerp(a, b, i / (float)n), c, 1); }
    void Dot(Vector2 p, Color32 c, int r)
    {
        int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
        for (int y = cy - r + 1; y < cy + r; y++) for (int x = cx - r + 1; x < cx + r; x++) if (x >= 0 && x < MW && y >= 0 && y < MH) px[y * MW + x] = c;
    }
    void Circle(Vector2 c, float r, Color32 col, bool filled)
    {
        if (filled) { for (int y = (int)(c.y - r); y <= c.y + r; y++) for (int x = (int)(c.x - r); x <= c.x + r; x++) if ((new Vector2(x, y) - c).sqrMagnitude <= r * r && x >= 0 && x < MW && y >= 0 && y < MH) px[y * MW + x] = col; return; }
        int n = Mathf.Max(32, (int)(r * 6)); Vector2 prev = c + new Vector2(r, 0);
        for (int i = 1; i <= n; i++) { float a = i * Mathf.PI * 2 / n; var q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; Line(prev, q, col); prev = q; }
    }

    void OnDestroy() { if (overlay) Destroy(overlay); }

    struct Vector3d
    {
        public double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double mag => Math.Sqrt(x * x + y * y + z * z);
    }
}
