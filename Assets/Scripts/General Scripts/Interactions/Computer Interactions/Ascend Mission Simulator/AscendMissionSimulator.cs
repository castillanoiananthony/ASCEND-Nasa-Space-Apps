// ═════════════════════════════════════════════════════════════════════════════
//  AscendMissionSimulator.cs  -  "Ascend Mission Simulator" computer app
//  (replaces the old Ascend Rocket Trajectory Calculator placeholder)
//
//  APPROVED design (AscendCAD)  ->  plan + calculate the trajectory here  ->
//  Send to Mission Control (PhMail)  ->  CONFIRMED  ->  rocket is built on the launch pad  ->  fly it
//
//  Real physics with the rocket's real numbers (same model as the launch in AscendFlight.cs):
//    thrust sea level -> vacuum, burning propellant, gravity 1/r², thinning air, Mach-dependent drag,
//    boosters, staging, fairing, payload release, Earth's rotation, orbit maths (vis-viva),
//    and for the Moon: trans-lunar injection + lunar orbit insertion (patched conics).
//
//  Setup: see the bottom of this header in the chat message (prefab in the app's "Window Content").
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public enum AscendSimMission { SkySensor, EarthWatcher, MoonMapper }

[Serializable]
public class AscendSimPlan
{
    public AscendSimMission mission;
    public string name = "Your trajectory";
    [TextArea] public string about = "";
    [Tooltip("Small tilt right after liftoff that starts the gravity turn (degrees).")]
    public float kickDeg = 3f;
    [Tooltip("Sky Sensor: highest point. Earth Watcher: orbit height. Moon Mapper: parking orbit height.")]
    public float targetKm = 700f;

    public AscendSimPlan Clone() => (AscendSimPlan)MemberwiseClone();

    // Three suggested trajectories for each mission (index 1 = the recommended one)
    public static AscendSimPlan[] Suggestions(AscendSimMission m)
    {
        switch (m)
        {
            case AscendSimMission.SkySensor: return new[]
            {
                new AscendSimPlan { mission = m, name = "Straight Up", kickDeg = 0f, targetKm = 1f, about = "Launch rail straight up. The CanSat comes down close to the launch site." },
                new AscendSimPlan { mission = m, name = "Lean 3°", kickDeg = 3f, targetKm = 1f, about = "Rail tilted 3° away from the crowd, like real CanSat launches. Lands a bit farther away." },
                new AscendSimPlan { mission = m, name = "Lean 6°", kickDeg = 6f, targetKm = 1f, about = "More tilt: lower top point, lands farther downrange." },
            };
            case AscendSimMission.EarthWatcher: return new[]
            {
                new AscendSimPlan { mission = m, name = "Steep", kickDeg = 2f, targetKm = 700f, about = "Leans over slowly: leaves the thick air fast (low air push) but loses more push to gravity." },
                new AscendSimPlan { mission = m, name = "Balanced", kickDeg = 3f, targetKm = 700f, about = "The usual choice: a good mix of low air push and low gravity loss." },
                new AscendSimPlan { mission = m, name = "Shallow", kickDeg = 5f, targetKm = 700f, about = "Leans over early: saves fuel, but flies faster in thick air (higher Max Q)." },
            };
            default: return new[]
            {
                new AscendSimPlan { mission = m, name = "Steep", kickDeg = 3f, targetKm = 250f, about = "Gentle turn to a higher 250 km parking orbit. Safest for the structure." },
                new AscendSimPlan { mission = m, name = "Balanced", kickDeg = 4f, targetKm = 200f, about = "Standard 200 km parking orbit, then the burn toward the Moon." },
                new AscendSimPlan { mission = m, name = "Shallow", kickDeg = 6f, targetKm = 180f, about = "Low 180 km parking orbit: saves the most fuel for the trip to the Moon." },
            };
        }
    }

    public static AscendSimPlan Suggested(AscendSimMission m) => Suggestions(m)[1].Clone();

    public static AscendSimPlan SuggestedOld(AscendSimMission m)
    {
        switch (m)
        {
            case AscendSimMission.SkySensor: return new AscendSimPlan { mission = m, kickDeg = 0f, targetKm = 1f };
            case AscendSimMission.EarthWatcher: return new AscendSimPlan { mission = m, kickDeg = 3f, targetKm = 700f };
            default: return new AscendSimPlan { mission = m, kickDeg = 4f, targetKm = 200f };
        }
    }

    public static string Title(AscendSimMission m) =>
        m == AscendSimMission.SkySensor ? "Sky Sensor (CanSat)" : m == AscendSimMission.EarthWatcher ? "Earth Watcher" : "Moon Mapper";

    public static string Goal(AscendSimMission m) =>
        m == AscendSimMission.SkySensor ? "Fly your CanSat about 1 km up, then let it float down on its parachute while it measures the air."
      : m == AscendSimMission.EarthWatcher ? "Put your satellite in orbit 700 km above Earth, flying over the poles so it can photograph the whole planet."
      : "Reach a parking orbit around Earth, fire the engine toward the Moon, then slow down into a 50 km orbit around it.";

    public static string Expert(AscendSimMission m) =>
        m == AscendSimMission.SkySensor ? "Suborbital, vertical. Engine cut-off when the ballistic apogee reaches the target; release at apogee."
      : m == AscendSimMission.EarthWatcher ? "Sun-synchronous orbit (~98° inclination, slightly against Earth's spin). Gravity turn, coast to apoapsis, circularization burn."
      : "Eastward launch to a ~200 km parking orbit, trans-lunar injection (~3.1 km/s), ~5-day transfer, lunar orbit insertion (~0.8-0.9 km/s).";
}

public class AscendSimEvent
{
    public float t; public double x, y; public string kid, expert; public Color color;
}

public class AscendSimResult
{
    public AscendSimPlan plan;
    public string rocketName = "", payloadName = "";
    public readonly List<double> xs = new List<double>(), ys = new List<double>();
    public readonly List<float> ts = new List<float>(), alts = new List<float>(), speeds = new List<float>();
    public readonly List<AscendSimEvent> events = new List<AscendSimEvent>();
    public bool go, orbit, crashed, failed, tliDone;
    public string verdict = "", kid = "", expert = "", fix = "", advice = "";
    public float apogeeKm, apoKm, periKm, maxQ, maxG, liftoffTWR, dvLeft, satDv, flightTime;
    // Orbit (Earth-centered, meters)
    public double a, e, omega; public int dir = -1;
    // Moon
    public float loiNeed, loiHave, transferDays;
    // Sky Sensor parachute
    public double releaseX, releaseY;
}

// ═════════════════════════════════════════════════════════════════════════════
//  THE PHYSICS (no graphics): runs a whole mission in a fraction of a second
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendMissionSim
{
    public const double EarthR = 6371000.0, Mu = 3.986004418e14;
    public const double MoonDist = 384400e3, MoonR = 1737.4e3, MuMoon = 4.9048695e12, MoonV = 1022.0;
    const float G0 = 9.80665f;
    const double SpinSpeed = 450.0;   // Earth's surface speed at ~14.6°N (Philippines), m/s

    class Eng { public CADPart p; public string pool; public bool solid, dead; public Grp g; public float ignite = float.MaxValue, mdot, thrust; }
    class Grp { public bool side, attached = true; public int stage; public float dia; public readonly List<int> parts = new List<int>(); public readonly List<string> pools = new List<string>(); public readonly List<Eng> engines = new List<Eng>(); }
    enum Mode { Ascent, CoastToApo, Circularize, TLI, Coast, Done }

    public static AscendSimResult Run(CADDesign d, CADDesign payload, AscendCADData data, AscendSimPlan plan)
    {
        var res = new AscendSimResult { plan = plan.Clone(), rocketName = d != null ? d.name : "", payloadName = payload != null ? payload.name : "" };
        if (d == null || d.IsEmpty) { Verdict(res, false, "NO ROCKET", "There is no rocket to simulate.", "Design and get a rocket APPROVED in AscendCAD first."); return res; }

        // ───── Read the rocket (same rules as AscendCAD and the launch) ─────
        int n = d.parts.Count;
        var P = new CADPart[n]; var core = new bool[n]; var side = new int[n]; var stageOf = new int[n]; var attached = new bool[n];
        for (int i = 0; i < n; i++) { P[i] = data.Get(d.parts[i].partId); core[i] = CADStats.IsCore(d, i); side[i] = SideRoot(d, i); attached[i] = P[i] != null; }
        for (int i = 0; i < n; i++) if (side[i] >= 0 && P[side[i]] != null && P[side[i]].crossfeed) side[i] = -1;
        var dec = new List<float>();
        for (int i = 0; i < n; i++) if (core[i] && P[i] != null && P[i].role == CADRole.Decoupler) dec.Add(d.parts[i].pos.y);
        dec.Sort();
        int S = dec.Count + 1;
        for (int i = 0; i < n; i++)
        {
            int a = i, guard = 0;
            while (!core[a] && d.parts[a].parent >= 0 && guard++ < 10000) a = d.parts[a].parent;
            int st = 0; foreach (var v in dec) if (v < d.parts[a].pos.y - 1e-3f) st++;
            stageOf[i] = st;
        }

        var groups = new List<Grp>(); var byKey = new Dictionary<string, Grp>(); var grpOf = new Grp[n];
        var fuel = new Dictionary<string, float>(); var fairing = new List<int>();
        bool guided = false, fc = false, imu = false, fins = false;
        for (int i = 0; i < n; i++)
        {
            var p = P[i]; if (p == null) continue;
            if (p.role == CADRole.FlightComputer) fc = true;
            if (p.role == CADRole.IMU) imu = true;
            if (p.role == CADRole.Aero) fins = true;
            string key = side[i] >= 0 ? "side" + side[i] : "core" + stageOf[i];
            if (!byKey.TryGetValue(key, out var g)) { g = new Grp { side = side[i] >= 0, stage = stageOf[i] }; byKey[key] = g; groups.Add(g); }
            g.parts.Add(i); grpOf[i] = g;
            if (p.IsBody && p.role != CADRole.Fairing) g.dia = Mathf.Max(g.dia, p.diameter);
            if (p.role == CADRole.Fairing) fairing.Add(i);
            if (p.role == CADRole.Tank && p.propellantKg > 0f) AddFuel(fuel, g, key + "|" + p.propellant, p.propellantKg);
            if (p.role == CADRole.SolidMotor && p.propellantKg > 0f) AddFuel(fuel, g, "solid" + i, p.propellantKg);
        }
        guided = fc && imu;
        bool passive = !guided && fins;   // fins keep a small rocket pointing straight up
        var engines = new List<Eng>();
        for (int i = 0; i < n; i++)
        {
            var p = P[i]; if (p == null || (p.role != CADRole.Engine && p.role != CADRole.SolidMotor)) continue;
            var g = grpOf[i];
            var e = new Eng { p = p, g = g, solid = p.role == CADRole.SolidMotor };
            e.pool = e.solid ? "solid" + i : (side[i] >= 0 ? "side" + side[i] : "core" + stageOf[i]) + "|" + p.propellant;
            if (!fuel.ContainsKey(e.pool)) e.dead = true;
            engines.Add(e); g.engines.Add(e);
        }

        float payloadKg = 0f;
        if (payload != null && !payload.IsEmpty)
        {
            var ps = CADStats.Payload(payload, data);
            payloadKg = ps.wetMass; res.satDv = ps.satDeltaV;
        }
        float coreDia = 0f;
        foreach (var g in groups) if (!g.side) coreDia = Mathf.Max(coreDia, g.dia);
        foreach (int i in fairing) coreDia = Mathf.Max(coreDia, P[i].diameter);
        if (coreDia <= 0f) coreDia = 1f;
        bool aeroNose = TopIsAero(d, P);
        float sizeK = Mathf.Clamp(1.2f / Mathf.Max(0.05f, coreDia), 1f, 20f);   // small rockets are stronger for their size
        float qLimit = (aeroNose ? 45f : 25f) * 1000f * sizeK, gLimit = 10f * Mathf.Clamp(sizeK, 1f, 4f);
        float gCap = Mathf.Min(4.5f, gLimit * 0.7f);   // the flight computer keeps the g-force below this

        float Mass()
        {
            float m = payloadKg;
            for (int i = 0; i < n; i++) if (attached[i] && P[i] != null) m += P[i].dryMassKg;
            foreach (var g in groups) if (g.attached) foreach (var pool in g.pools) m += fuel[pool];
            return Mathf.Max(1f, m);
        }

        // ───── State ─────
        var plan2 = res.plan;
        bool orbitMission = plan2.mission != AscendSimMission.SkySensor;
        double spin = plan2.mission == AscendSimMission.SkySensor ? 0.0 : plan2.mission == AscendSimMission.EarthWatcher ? -63.0 : SpinSpeed;
        double target = Math.Max(0.2, plan2.targetKm) * 1000.0;
        double px = 0, py = EarthR, vx = spin, vy = 0;
        float t = 0f, tilt = 0f, throttle = 1f, tumble = 0f, kickStart = -1f, burnoutAt = 0f, igniteAt = 0f;
        int cur = 0;
        bool lifted = false, burnout = false, pendingIgnite = false, fairingGone = false, machSaid = false, maxQSaid = false, dryTold = false;
        float maxQ = 0f, maxG = 0f, gNow = 0f, maxAlt = 0f;
        Mode mode = Mode.Ascent;
        // Liquid engines start 1.2 s early and are held down until they reach full thrust.
        // Solid motors can't be held: they light at release (a solid-only rocket leaves the pad at once).
        bool liquidFirst = engines.Any(e => !e.dead && !e.solid && e.g.stage == 0);
        float hold = liquidFirst ? 1.2f : 0f;
        foreach (var e in engines) if (e.g.stage == 0) e.ignite = e.solid ? hold : 0f;
        var need = new Dictionary<string, float>();
        float nextSample = 1f;
        res.ts.Add(0f); res.xs.Add(px); res.ys.Add(py); res.alts.Add(0f); res.speeds.Add(0f);

        void Event(string kid, string expert, Color c) => res.events.Add(new AscendSimEvent { t = t, x = px, y = py, kid = kid, expert = expert, color = c });
        Color cGood = new Color(0.25f, 0.85f, 0.45f), cInfo = new Color(0.55f, 0.75f, 1f), cWarn = new Color(1f, 0.75f, 0.2f), cBad = new Color(1f, 0.33f, 0.3f);

        // Quick burn (treated as instant): uses the rocket equation, stages when a stage runs dry.
        // Used for the circularization and Moon burns so a rocket with enough push always makes it.
        bool ImpulseBurn(float dvNeed)
        {
            float left = dvNeed;
            int guardB = 0;
            while (left > 0.5f && guardB++ < 20)
            {
                var stEng = engines.Where(e => !e.dead && e.g.attached && e.g.stage == cur).ToList();
                var pools = stEng.Select(e => e.pool).Distinct().Where(pl => fuel[pl] > 0f).ToList();
                float f = pools.Sum(pl => fuel[pl]);
                float isp = stEng.Count > 0 ? stEng.Average(e => e.p.ispVac) : 0f;
                float m0 = Mass();
                if (f > 0f && isp > 0f && m0 > f)
                {
                    float dvSt = isp * G0 * Mathf.Log(m0 / (m0 - f));
                    if (dvSt >= left)
                    {
                        float use = m0 - m0 / Mathf.Exp(left / (isp * G0));
                        float k = Mathf.Clamp01(use / f);
                        foreach (var pl in pools) fuel[pl] *= 1f - k;
                        return true;
                    }
                    foreach (var pl in pools) fuel[pl] = 0f;
                    left -= dvSt;
                }
                if (cur >= S - 1) return false;
                foreach (var gg in groups) if (gg.attached && gg.stage == cur) { gg.attached = false; foreach (int i in gg.parts) if (!fairing.Contains(i)) attached[i] = false; }
                cur++;
                Event("Stage separation", $"Stage {cur} drops away", cInfo);
            }
            return left <= 0.5f;
        }

        bool AnyFuel() => engines.Any(e => !e.dead && e.g.attached && e.g.stage >= cur && fuel[e.pool] > 0f);

        int guardSteps = 0;
        while (mode != Mode.Done && t < 4f * 3600f && guardSteps++ < 600000)
        {
            double r = Math.Sqrt(px * px + py * py), h = r - EarthR;
            double ux = px / r, uy = py / r, ex = uy, ey = -ux;
            float alt = (float)Math.Max(0, h);
            bool burning = mode == Mode.Ascent || mode == Mode.Circularize || mode == Mode.TLI;
            float dt = (burning || alt < 120000f) ? 0.05f : (mode == Mode.CoastToApo ? 0.5f : 1f);

            // Air moves with the spinning Earth
            double ax0 = vx - spin * ex * (r / EarthR), ay0 = vy - spin * ey * (r / EarthR);
            double vRel = Math.Sqrt(ax0 * ax0 + ay0 * ay0), vIn = Math.Sqrt(vx * vx + vy * vy);
            float pr = Mathf.Exp(-alt / 8000f), rho = alt > 150000f ? 0f : 1.225f * Mathf.Exp(-alt / 8500f);
            float mach = (float)vRel / Mathf.Sqrt(1.4f * 287f * AirTemp(alt));
            float q = 0.5f * rho * (float)(vRel * vRel);

            // Orbit prediction (vis-viva)
            double vUpIn = vx * ux + vy * uy;
            Orbit(px, py, vx, vy, out double oa, out double oe, out double oApo, out double oPeri, out _);

            // Throttle (liquid engines ease off for Max Q and high g)
            throttle = Mathf.Min(1f, throttle + 0.5f * dt);
            if (guided)
            {
                float fMax = 0f, mNow = Mass();
                foreach (var e in engines)
                    if (burning && !e.dead && !e.solid && e.g.attached && e.g.stage == cur && t >= e.ignite && fuel[e.pool] > 0f)
                        fMax += Thrust(e.p, false, pr, 1f, 1f, out _);
                if (fMax > 0f)
                {
                    throttle = Mathf.Min(throttle, Mathf.Clamp(gCap * G0 * mNow / fMax, 0.3f, 1f));
                    if (q > qLimit * 0.55f) throttle = Mathf.Min(throttle, Mathf.Clamp(1f - (q - qLimit * 0.55f) / (qLimit * 0.35f), 0.3f, 1f));
                }
                // Apoapsis already high enough while still in the air: coast up, then finish above the air
                if (orbitMission && mode == Mode.Ascent && lifted && h < 60000 && !double.IsNaN(oApo) && oApo >= target) throttle = 0f;
            }

            // Thrust
            float F = 0f; need.Clear();
            foreach (var e in engines)
            {
                e.thrust = e.mdot = 0f;
                if (!burning || e.dead || !e.g.attached || e.g.stage != cur || t < e.ignite || fuel[e.pool] <= 0f) continue;
                e.thrust = Thrust(e.p, e.solid, pr, throttle, Mathf.Clamp01((t - e.ignite) / 1f), out e.mdot);
                need.TryGetValue(e.pool, out var nd); need[e.pool] = nd + e.mdot * dt;
            }
            // Solid motors can't be switched off once lit
            if (!burning)
                foreach (var e in engines)
                    if (e.solid && e.g.attached && e.g.stage == cur && t >= e.ignite && fuel[e.pool] > 0f)
                    { e.thrust = Thrust(e.p, true, pr, 1f, 1f, out e.mdot); need.TryGetValue(e.pool, out var nd); need[e.pool] = nd + e.mdot * dt; }
            foreach (var e in engines)
            {
                if (e.mdot <= 0f) continue;
                float sc = need[e.pool] > 0f ? Mathf.Clamp01(fuel[e.pool] / need[e.pool]) : 1f;
                F += e.thrust * sc;
                fuel[e.pool] = Mathf.Max(0f, fuel[e.pool] - e.mdot * dt * sc);
                if (fuel[e.pool] < 0.05f) fuel[e.pool] = 0f;   // a few grams left = empty (rounding can leave crumbs that never reach zero)
            }
            float m = Mass();

            // Guidance
            float fpaRel = vRel > 20 ? Mathf.Atan2((float)(ax0 * ex + ay0 * ey), (float)(ax0 * ux + ay0 * uy)) * Mathf.Rad2Deg : 0f;
            float fpaIn = vIn > 20 ? Mathf.Atan2((float)(vx * ex + vy * ey), (float)(vx * ux + vy * uy)) * Mathf.Rad2Deg : 0f;
            float HoldTilt(float extraUp)
            {
                double gg = Mu / (r * r), vh = vx * ex + vy * ey;
                float aT = F / m;
                if (aT <= 0f) return fpaIn;
                float needUp = (float)(gg - vh * vh / r) + extraUp;
                return 90f - Mathf.Asin(Mathf.Clamp(needUp / aT, -0.5f, 0.97f)) * Mathf.Rad2Deg;
            }
            float want = tilt;
            if (!guided) { if (passive) want = vRel > 20 ? Mathf.Clamp(fpaRel, 0f, 90f) : plan2.kickDeg; else if (lifted && alt > 50f) { tumble = Mathf.Min(25f, tumble + 3f * dt); want = tilt + tumble * dt; } }
            else if (mode == Mode.Circularize) want = HoldTilt(-0.05f * (float)vUpIn);   // burn sideways while holding the height
            else if (mode == Mode.TLI) want = fpaIn;      // burn along the direction of travel
            else if (plan2.mission == AscendSimMission.SkySensor) want = vRel > 20 ? Mathf.Clamp(fpaRel, 0f, 90f) : plan2.kickDeg;   // launch rail tilt, then nose follows the airflow
            else if (!lifted || vRel < 60) want = 0f;
            else
            {
                // Gravity turn: pitch kick, then follow the airflow (no sideways push), leaning over only as fast as the speed allows
                if (kickStart < 0f) kickStart = t;
                if (t - kickStart < 8f) want = plan2.kickDeg * Mathf.Clamp01((t - kickStart) / 4f);
                else if (alt < 45000f) want = Mathf.Max(0f, Mathf.Min(fpaRel, plan2.kickDeg + 80f * Mathf.Clamp01(((float)vRel - 60f) / 1600f)));
                else
                {
                    want = Mathf.Min(88f, fpaIn);
                    if (vUpIn < 250 && F > 0f) want = Mathf.Min(want, HoldTilt(0.02f * (250f - (float)vUpIn)));   // weak upper stage: don't fall back
                }
            }
            bool kicking = mode == Mode.Ascent && kickStart >= 0f && t - kickStart < 8f;
            tilt = guided ? Mathf.MoveTowards(tilt, want, (kicking ? 4f : 15f) * dt) : want;
            float tr = tilt * Mathf.Deg2Rad;
            double tx = ux * Math.Cos(tr) + ex * Math.Sin(tr), ty = uy * Math.Cos(tr) + ey * Math.Sin(tr);

            // Forces
            double g = Mu / (r * r);
            double drag = q * Cd(mach, aeroNose) * DragArea(coreDia, groups);
            double ax = tx * F / m - ux * g, ay = ty * F / m - uy * g;
            if (vRel > 0.1) { ax -= drag * ax0 / vRel / m; ay -= drag * ay0 / vRel / m; }
            gNow = (float)((F + drag) / m / G0);

            // Hold-down clamps
            if (!lifted)
            {
                if (t >= hold && F / m > g)
                {
                    lifted = true; res.liftoffTWR = F / (m * G0);
                    Event("Liftoff!", $"Thrust-to-weight {res.liftoffTWR:0.00}", cGood);
                }
                else
                {
                    vx = spin * ex; vy = spin * ey;
                    if (t > 6f)
                    {
                        float twr = F / (m * G0);
                        Fail(res, "TOO HEAVY", "The engines can't lift the rocket off the pad.", $"Liftoff thrust-to-weight {twr:0.00}; it must be above 1.0 (1.2-1.5 is ideal).");
                        break;
                    }
                    t += dt; continue;
                }
            }

            vx += ax * dt; vy += ay * dt; px += vx * dt; py += vy * dt; t += dt;

            maxQ = Mathf.Max(maxQ, q); maxG = Mathf.Max(maxG, gNow); maxAlt = Mathf.Max(maxAlt, alt);
            if (!machSaid && mach >= 1f && alt < 60000f) { machSaid = true; Event("Faster than sound!", "Mach 1", cInfo); }
            if (!maxQSaid && maxQ > 3000f && q < maxQ * 0.97f) { maxQSaid = true; Event("Max Q: the biggest air push", $"{maxQ / 1000f:0.0} kPa at {alt / 1000f:0.0} km", cWarn); }

            // Failures (a rocket with a flight computer + IMU protects itself: it throttles and steers so the air and g-force never break it)
            float aoa = vRel > 30 ? Mathf.Abs(Mathf.DeltaAngle(tilt, fpaRel)) : 0f;
            if (!guided && q > qLimit) { Fail(res, "RIPPED APART", "The air pushes too hard and the rocket breaks apart.", $"Dynamic pressure {q / 1000f:0.0} kPa > {qLimit / 1000f:0} kPa limit" + (aeroNose ? "." : " (blunt nose, add a nose cone or fairing).") + " Use a smaller pitch kick or a lighter, slower climb."); Event("Broke apart", "", cBad); break; }
            if (q * Mathf.Sin(aoa * Mathf.Deg2Rad) > 6000f && lifted && vx * ux + vy * uy < 0 && maxAlt > 20000f)
            { res.crashed = true; Fail(res, "FELL BACK", $"The rocket ran out of push at {maxAlt / 1000f:0} km and fell back to Earth.", "Upper stage too weak: thrust-to-weight too low or not enough delta-v. Use a stronger upper-stage engine or more propellant."); Event("Fell back", "", cBad); break; }
            if (!guided && q * Mathf.Sin(aoa * Mathf.Deg2Rad) > 6000f) { Fail(res, "TURNED SIDEWAYS", "The rocket can't steer, turns sideways and the air rips it apart.", "No flight computer + IMU, so it can't hold its angle of attack."); Event("Tumbled", "", cBad); break; }
            if (!guided && gNow > gLimit) { Fail(res, "CRUSHED", "The rocket speeds up so hard it's crushed by its own push.", $"Axial load {gNow:0.0} g > {gLimit:0} g."); Event("Crushed", "", cBad); break; }
            if (lifted && h <= 0 && vx * ux + vy * uy < 0)
            {
                res.crashed = true;
                if (!res.failed) Fail(res, "FELL BACK", $"The rocket ran out of push at {maxAlt / 1000f:0.0} km and fell back to Earth.", "Not enough delta-v: more propellant, higher-Isp engines or less mass.");
                Event("Crash", "", cBad); break;
            }

            // Staging (boosters, stages) while burning
            if (lifted && (burning || engines.Any(e => e.solid && e.mdot > 0f)))
            {
                foreach (var gg in groups)
                    if (gg.side && gg.attached && gg.stage == cur && gg.engines.Count > 0 && gg.pools.All(pl => fuel[pl] <= 0f))
                    { gg.attached = false; foreach (int i in gg.parts) attached[i] = false; Event("Boosters dropped", "Booster separation", cInfo); }
                bool stageBurning = engines.Any(e => e.g.attached && e.g.stage == cur && !e.dead && fuel[e.pool] > 0f);
                if (!stageBurning && !burnout && !pendingIgnite) { burnout = true; burnoutAt = t; if (cur < S - 1) Event($"Stage {cur + 1} empty", "MECO (main engine cut-off)", cInfo); }
                if (burnout && cur < S - 1 && t - burnoutAt > 1f)
                {
                    foreach (var gg in groups) if (gg.attached && gg.stage == cur) { gg.attached = false; foreach (int i in gg.parts) if (!fairing.Contains(i)) attached[i] = false; }
                    cur++; burnout = false; pendingIgnite = true; igniteAt = t + 1.2f;
                    Event("Stage separation", $"Stage {cur} drops away", cInfo);
                }
                if (pendingIgnite && t >= igniteAt)
                {
                    pendingIgnite = false;
                    foreach (var e in engines) if (e.g.stage == cur && e.g.attached) e.ignite = t;
                    Event($"Stage {cur + 1} engine on", "Upper-stage ignition", cInfo);
                }
            }

            if (!fairingGone && fairing.Count > 0 && alt > 85000f) { fairingGone = true; foreach (int i in fairing) attached[i] = false; Event("Fairing open", "Fairing jettison (nose cover falls away)", cInfo); }

            // ───── Mission logic ─────
            Orbit(px, py, vx, vy, out oa, out oe, out oApo, out oPeri, out _);
            double vUp = vx * ux + vy * uy;
            switch (mode)
            {
                case Mode.Ascent:
                    if (!orbitMission)
                    {
                        double ballistic = h + Math.Max(0, vUp) * Math.Max(0, vUp) / (2 * g);
                        if (lifted && ballistic >= target) { mode = Mode.Coast; Event("Engine cut-off", $"Coasting up to {target / 1000:0.##} km", cInfo); }
                    }
                    else if (lifted && (oApo >= target || double.IsNaN(oApo)) && h >= 60000)
                    { mode = Mode.CoastToApo; Event("Engines off: coasting up", $"SECO-1, highest point will be {oApo / 1000:0} km", cInfo); }
                    break;
                case Mode.CoastToApo:
                    if (vUp <= 0)
                    {
                        // At the highest point: one burn sideways to make the orbit round
                        double rr = Math.Sqrt(px * px + py * py), hx = px / rr, hy = py / rr, hex = hy, hey = -hx;
                        double vh = vx * hex + vy * hey, sgn = vh >= 0 ? 1.0 : -1.0, vc = Math.Sqrt(Mu / rr);
                        double vUpNow = vx * hx + vy * hy;
                        float dvCirc = (float)Math.Sqrt((vc * sgn - vh) * (vc * sgn - vh) + vUpNow * vUpNow);
                        Event("Circularization burn", $"Burn at the highest point to round out the orbit ({dvCirc:N0} m/s)", cInfo);
                        if (rr - EarthR > 140000 && ImpulseBurn(dvCirc))
                        {
                            vx = hex * vc * sgn; vy = hey * vc * sgn;
                            res.orbit = true;
                            Event("ORBIT!", $"{(rr - EarthR) / 1000:0} km circular orbit", cGood);
                            if (plan2.mission == AscendSimMission.MoonMapper)
                            {
                                double vT = Math.Sqrt(Mu * (2 / rr - 2 / (rr + MoonDist)));
                                Event("Burn toward the Moon", $"Trans-lunar injection (TLI), {vT - vc:N0} m/s", cInfo);
                                if (ImpulseBurn((float)(vT - vc)))
                                {
                                    vx = hex * vT * sgn; vy = hey * vT * sgn;
                                    res.tliDone = true;
                                    Event("On the way to the Moon!", "TLI complete, satellite released", cGood);
                                }
                                else Event("Out of fuel", "Not enough push left for the trip to the Moon", cWarn);
                            }
                            else Event("Satellite released!", "Payload separation", cGood);
                        }
                        else Event("Out of fuel", "Not enough push left to round out the orbit", cWarn);
                        mode = Mode.Done;
                    }
                    break;
                case Mode.Circularize:
                    if (!double.IsNaN(oApo) && oPeri >= Math.Min(Math.Min(target - 25000, oApo - 30000), alt - 5000) && oPeri > 140000)
                    {
                        res.orbit = true;
                        Event("ORBIT!", $"{oApo / 1000:0} x {oPeri / 1000:0} km", cGood);
                        if (plan2.mission == AscendSimMission.MoonMapper)
                        { mode = Mode.TLI; foreach (var e in engines) if (e.g.stage == cur && e.g.attached) e.ignite = t; Event("Burn toward the Moon", "Trans-lunar injection (TLI)", cInfo); }
                        else { Event("Satellite released!", "Payload separation", cGood); mode = Mode.Done; }
                    }
                    break;
                case Mode.TLI:
                    if (oApo + EarthR >= MoonDist * 0.98 || double.IsNaN(oApo)) { res.tliDone = true; Event("On the way to the Moon!", "TLI complete, satellite released", cGood); mode = Mode.Done; }
                    break;
                case Mode.Coast:
                    if (vUp <= 0) { res.releaseX = px; res.releaseY = py; Event("CanSat released! Parachute open", $"Apogee {h / 1000:0.00} km", cGood); mode = Mode.Done; }
                    break;
            }

            // Out of push while trying to burn: keep flying ballistically so we see where it goes
            if ((mode == Mode.Circularize || mode == Mode.TLI || mode == Mode.Ascent) && lifted && !AnyFuel())
            {
                if (!dryTold) { dryTold = true; Event("Out of fuel", "All propellant used", cWarn); }
                if (mode == Mode.Ascent) mode = orbitMission ? Mode.CoastToApo : Mode.Coast;
                else mode = Mode.Done;
            }

            if (t >= nextSample || mode == Mode.Done)
            {
                res.ts.Add(t); res.xs.Add(px); res.ys.Add(py); res.alts.Add(alt); res.speeds.Add((float)vIn);
                nextSample = t + (burning || alt < 120000f ? 1f : 10f);
            }
        }

        // ───── Results ─────
        res.flightTime = t;
        res.maxQ = maxQ; res.maxG = maxG; res.apogeeKm = maxAlt / 1000f;
        Orbit(px, py, vx, vy, out res.a, out res.e, out double apo, out double peri, out res.omega);
        res.apoKm = (float)(apo / 1000.0); res.periKm = (float)(peri / 1000.0);
        res.dir = px * vy - py * vx >= 0 ? 1 : -1;
        res.dvLeft = DvLeft(engines, groups, fuel, cur, S, Mass());
        if (res.failed) { res.advice = Advice(res, plan2, qLimit); return res; }

        if (payload == null || payload.IsEmpty)
        { Verdict(res, false, "NO PAYLOAD", "The rocket has nothing to carry. Add your satellite or CanSat in AscendCAD.", "No payload in the approved design."); return res; }

        switch (plan2.mission)
        {
            case AscendSimMission.SkySensor:
            {
                float ap = res.apogeeKm;
                if (ap < plan2.targetKm * 0.6f) Verdict(res, false, "TOO LOW", $"The CanSat only reaches {ap:0.00} km. It needs about {plan2.targetKm:0.#} km.", "Not enough impulse, or the rocket is too heavy.");
                else if (ap > plan2.targetKm * 3f) Verdict(res, false, "TOO HIGH", $"The CanSat would be released at {ap:0.0} km, much higher than {plan2.targetKm:0.#} km. It could drift far away.", "Solid motors can't be shut down early. Use a liquid engine so the computer can cut it off at the right moment.");
                else Verdict(res, true, "GO", $"The CanSat goes up to {ap:0.00} km, opens its parachute and floats down for about {ap * 1000f / 8f / 60f:0.#} minutes while measuring the air.", $"Apogee {ap:0.00} km, max Q {maxQ / 1000f:0.0} kPa, max {maxG:0.0} g. Descent ~8 m/s under parachute.");
                break;
            }
            case AscendSimMission.EarthWatcher:
            {
                if (!res.orbit) Verdict(res, false, "NO ORBIT", $"The satellite doesn't reach orbit (highest point {res.apogeeKm:0} km). It falls back.", $"Delta-v short. Periapsis {res.periKm:0} km must be above ~150 km. Add propellant or a more efficient upper stage.");
                else if (Mathf.Abs(res.apoKm - plan2.targetKm) > 200f || res.periKm < plan2.targetKm - 200f) Verdict(res, true, "GO (off target)", $"Orbit reached ({res.apoKm:0} x {res.periKm:0} km), a little different from the {plan2.targetKm:0} km goal. The satellite can still work.", "Adjust the pitch kick or target height for a rounder orbit.");
                else Verdict(res, true, "GO", $"Your satellite circles Earth at about {plan2.targetKm:0} km, crossing the poles to photograph the whole planet!", $"Orbit {res.apoKm:0} x {res.periKm:0} km, {res.dvLeft:N0} m/s left in the upper stage, max Q {maxQ / 1000f:0.0} kPa.");
                break;
            }
            default:
            {
                if (!res.orbit) { Verdict(res, false, "NO PARKING ORBIT", $"The rocket doesn't even reach orbit around Earth (highest point {res.apogeeKm:0} km).", "The Moon needs ~12.5 km/s of delta-v in total. Use a much bigger rocket or a lighter spacecraft."); break; }
                if (!res.tliDone) { Verdict(res, false, "CAN'T REACH THE MOON", $"In orbit, but out of fuel before the push toward the Moon was finished (highest point {res.apoKm:N0} km of 384,400 km).", "Trans-lunar injection needs ~3.1 km/s after reaching low Earth orbit."); break; }
                // Patched conics: arrive at the Moon, then brake into a 50 km orbit
                double vInf, rp = MoonR + 50000;
                if (double.IsInfinity(res.a) || res.a <= 0)
                {
                    double rf = Math.Sqrt(px * px + py * py), v2f = vx * vx + vy * vy;
                    vInf = Math.Sqrt(Math.Max(0, v2f - 2 * Mu / rf)) + MoonV * 0.5;   // fast, escaping trajectory
                    res.transferDays = 2.5f;
                }
                else
                {
                    double ra = Math.Min(res.a * (1 + res.e), MoonDist);
                    double vApo = Math.Sqrt(Math.Max(0, Mu * (2 / ra - 1 / res.a)));
                    vInf = Math.Abs(MoonV - vApo);
                    res.transferDays = (float)(Math.PI * Math.Sqrt(res.a * res.a * res.a / Mu) / 86400.0);
                }
                double vp = Math.Sqrt(vInf * vInf + 2 * MuMoon / rp), vc = Math.Sqrt(MuMoon / rp);
                res.loiNeed = (float)(vp - vc);
                res.loiHave = res.dvLeft + res.satDv;
                if (res.loiHave >= res.loiNeed)
                    Verdict(res, true, "GO", $"The spacecraft travels {res.transferDays:0.0} days to the Moon, brakes, and starts mapping it from 50 km up!", $"LOI needs {res.loiNeed:N0} m/s, available {res.loiHave:N0} m/s (upper stage {res.dvLeft:N0} + satellite {res.satDv:N0}).");
                else
                    Verdict(res, false, "CAN'T STOP AT THE MOON", $"The spacecraft reaches the Moon after {res.transferDays:0.0} days but can't slow down, so it flies past.", $"Lunar orbit insertion needs {res.loiNeed:N0} m/s; only {res.loiHave:N0} m/s left. Add satellite propulsion (more propellant) or a bigger upper stage.");
                break;
            }
        }
        res.advice = Advice(res, plan2, qLimit);
        return res;
    }

    // ───── Trajectory advice: which setting to change, and which way ─────
    static string Advice(AscendSimResult r, AscendSimPlan p, float qLimit)
    {
        var l = new List<string>();
        string v = (r.verdict ?? "").ToUpperInvariant();
        float t = p.targetKm;
        if (p.mission == AscendSimMission.SkySensor)
        {
            if (v.StartsWith("TOO HIGH")) l.Add($"<b>Increase the rail tilt</b> (try {p.kickDeg + 3f:0}°): a leaning rocket doesn't climb as high. Or use the smaller motor.");
            else if (v.StartsWith("TOO LOW") || v.StartsWith("FELL"))
                l.Add(p.kickDeg > 0.5f ? $"<b>Decrease the rail tilt</b> (try {Mathf.Max(0f, p.kickDeg - 3f):0}°) so more of the push goes straight up."
                                       : "The rail is already straight up. The rocket needs the bigger motor or less weight.");
            else if (v.StartsWith("TURNED") || v.StartsWith("CRUSHED") || v.StartsWith("TOO HEAVY") || v.StartsWith("RIPPED"))
                l.Add("Changing the trajectory won't fix this one: see HOW TO FIX IT.");
            else if (r.go)
                l.Add(p.kickDeg < 2f ? "Good flight! Tip: a 3° rail tilt carries the falling rocket away from the crowd, like at real CanSat launches."
                                     : $"Good flight! The CanSat is released at {r.apogeeKm * 1000f:N0} m. Less tilt = higher, more tilt = lower.");
        }
        else
        {
            if (v.StartsWith("TURNED") || v.StartsWith("CRUSHED") || v.StartsWith("TOO HEAVY") || v.StartsWith("NO PAYLOAD"))
                l.Add("Changing the trajectory won't fix this one: see HOW TO FIX IT.");
            if (v.StartsWith("RIPPED") || r.maxQ > qLimit * 0.8f)
                l.Add($"<b>Decrease the pitch kick</b> (try {Mathf.Max(1f, p.kickDeg - 1f):0.#}°): leaning over later gets the rocket out of the thick air before it goes really fast. Max Q was {r.maxQ / 1000f:0.0} kPa.");
            bool noOrbit = v.StartsWith("NO ORBIT") || v.StartsWith("NO PARKING") || v.StartsWith("FELL");
            if (noOrbit)
            {
                if (r.apogeeKm >= t * 0.8f)
                    l.Add($"<b>Increase the pitch kick</b> (try {p.kickDeg + 1f:0.#}°): the rocket went up to {r.apogeeKm:N0} km but wasn't fast enough <i>sideways</i> to stay up. Orbits are about going sideways fast!");
                else
                    l.Add($"<b>Lower the {(p.mission == AscendSimMission.MoonMapper ? "parking orbit" : "orbit height")}</b> (try {Mathf.Max(200f, t - 150f):0} km): getting that high costs too much push. If it still fails, the rocket needs more fuel.");
            }
            else if (r.orbit && p.mission == AscendSimMission.EarthWatcher)
            {
                if (Mathf.Abs(r.apoKm - t) > 150f || Mathf.Abs(r.periKm - t) > 150f)
                    l.Add($"The orbit is {r.apoKm:N0} x {r.periKm:N0} km, but the goal is {t:0} km on both sides. <b>{(r.apoKm > t ? "Lower" : "Raise")} the orbit height</b> a little, or change the pitch kick by 0.5° and compare.");
                if (r.dvLeft > 1500f) l.Add($"You have {r.dvLeft:N0} m/s of push to spare: you could <b>raise the orbit height</b> or carry a heavier satellite.");
                else if (r.dvLeft < 300f) l.Add($"Only {r.dvLeft:N0} m/s left: <b>increase the pitch kick by 0.5°</b> (less gravity loss) or <b>lower the orbit height</b> for more margin.");
                if (r.go && l.Count == 0) l.Add("Great orbit! The numbers are close to the goal with fuel to spare.");
            }
            if (p.mission == AscendSimMission.MoonMapper)
            {
                if (v.StartsWith("CAN'T REACH"))
                    l.Add($"<b>Lower the parking orbit</b> (try {Mathf.Max(160f, t - 40f):0} km) and <b>increase the pitch kick</b> (try {p.kickDeg + 1f:0.#}°): both save fuel for the burn toward the Moon.");
                else if (v.StartsWith("CAN'T STOP"))
                    l.Add($"A <b>lower parking orbit</b> (try {Mathf.Max(160f, t - 40f):0} km) keeps a little more fuel, but mostly the satellite needs a bigger engine: see HOW TO FIX IT.");
                else if (r.go) l.Add($"Good plan! {r.loiHave - r.loiNeed:N0} m/s spare after braking at the Moon.");
            }
        }
        l.Add("<color=#9AA0A6>Change one setting at a time, then press CALCULATE and compare.</color>");
        var sb = new StringBuilder();
        foreach (var s in l) sb.Append("• ").Append(s).Append('\n');
        return sb.ToString();
    }

    // ───── helpers ─────
    static void AddFuel(Dictionary<string, float> fuel, Grp g, string pool, float kg)
    {
        fuel.TryGetValue(pool, out var v); fuel[pool] = v + kg;
        if (!g.pools.Contains(pool)) g.pools.Add(pool);
    }

    static int SideRoot(CADDesign d, int i)
    {
        int guard = 0;
        while (i >= 0 && guard++ < 10000) { if (d.parts[i].link == CADLink.RadialUpright) return i; i = d.parts[i].parent; }
        return -1;
    }

    static bool TopIsAero(CADDesign d, CADPart[] P)
    {
        int top = -1; float topY = float.MinValue;
        for (int i = 0; i < d.parts.Count; i++)
        {
            if (P[i] == null || !CADStats.IsCore(d, i)) continue;
            float y = d.parts[i].pos.y + P[i].StackHeight * 0.5f;
            if (y > topY) { topY = y; top = i; }
        }
        var tp = top >= 0 ? P[top] : null;
        return tp != null && (tp.role == CADRole.NoseCone || tp.role == CADRole.Fairing || tp.shape == CADShape.OgiveTank);
    }

    static float Thrust(CADPart p, bool solid, float pr, float throttle, float ramp, out float mdot)
    {
        if (solid)
        {
            float burn = p.burnTimeS > 0f ? p.burnTimeS : 60f;
            mdot = p.propellantKg / burn;
            float fv = p.thrustVackN > 0f ? p.thrustVackN * 1000f : mdot * Mathf.Max(1f, p.ispVac) * G0;
            float fs = p.thrustSLkN > 0f ? p.thrustSLkN * 1000f : (p.ispSL > 0f ? mdot * p.ispSL * G0 : 0f);
            return Mix(fv, fs, pr);
        }
        float fvac = p.thrustVackN * 1000f;
        mdot = fvac / (Mathf.Max(1f, p.ispVac) * G0) * throttle * ramp;
        return Mix(fvac, p.thrustSLkN * 1000f, pr) * throttle * ramp;
    }

    static float Mix(float fv, float fs, float pr) => fs > 0f ? Mathf.Lerp(fv, fs, pr) : fv * Mathf.Clamp01(1f - 2.5f * pr);

    static float AirTemp(float h) => h < 11000f ? 288.15f - 0.0065f * h : h < 20000f ? 216.65f : h < 47000f ? 216.65f + 0.0018f * (h - 20000f) : 265f;

    static float Cd(float m, bool aero)
    {
        float cd = m < 0.8f ? 0.30f : m < 1.2f ? Mathf.Lerp(0.30f, 0.55f, (m - 0.8f) / 0.4f) : Mathf.Lerp(0.55f, 0.25f, Mathf.Clamp01((m - 1.2f) / 3.8f));
        return aero ? cd : cd * 1.8f;
    }

    static float DragArea(float coreDia, List<Grp> groups)
    {
        float a = Mathf.PI * 0.25f * coreDia * coreDia;
        foreach (var g in groups) if (g.side && g.attached) a += Mathf.PI * 0.25f * g.dia * g.dia;
        return a;
    }

    // Orbit from position + velocity: semi-major axis, eccentricity, apoapsis/periapsis heights, periapsis direction
    public static void Orbit(double px, double py, double vx, double vy, out double a, out double e, out double apoAlt, out double periAlt, out double omega)
    {
        double r = Math.Sqrt(px * px + py * py), v2 = vx * vx + vy * vy;
        double eps = v2 / 2 - Mu / r;
        double rv = px * vx + py * vy;
        double exv = ((v2 - Mu / r) * px - rv * vx) / Mu, eyv = ((v2 - Mu / r) * py - rv * vy) / Mu;
        e = Math.Sqrt(exv * exv + eyv * eyv);
        omega = Math.Atan2(eyv, exv);
        if (eps >= 0) { a = double.PositiveInfinity; apoAlt = double.NaN; periAlt = (r - EarthR); return; }
        a = -Mu / (2 * eps);
        apoAlt = a * (1 + e) - EarthR;
        periAlt = a * (1 - e) - EarthR;
    }

    static float DvLeft(List<Eng> engines, List<Grp> groups, Dictionary<string, float> fuel, int cur, int S, float mass)
    {
        float dv = 0f, m = mass;
        for (int k = cur; k < S; k++)
        {
            var stEng = engines.Where(e => !e.dead && e.g.attached && e.g.stage == k).ToList();
            var pools = stEng.Select(e => e.pool).Distinct().ToList();
            float f = pools.Sum(pl => fuel[pl]);
            float isp = stEng.Count > 0 ? stEng.Average(e => e.p.ispVac) : 0f;
            if (f > 0f && isp > 0f && m > f) { dv += isp * G0 * Mathf.Log(m / (m - f)); m -= f; }
        }
        return dv;
    }

    static void Fail(AscendSimResult r, string title, string kid, string expert) { r.failed = true; Verdict(r, false, title, kid, expert); }
    static void Verdict(AscendSimResult r, bool go, string title, string kid, string expert) { r.go = go; r.verdict = title; r.kid = kid; r.expert = expert; r.fix = go ? "" : FixFor(title); }

    // What to change so this doesn't happen again (used by the simulator, the emails and the real launch)
    public static string FixFor(string title)
    {
        string t = (title ?? "").Trim().TrimEnd('!').ToUpperInvariant();
        switch (t)
        {
            case "TOO HEAVY": return "• Add more engines or a bigger motor\n• Use lighter parts or a lighter payload\n• Aim for thrust-to-weight 1.3-1.5 (small hobby rockets: 5 or more)";
            case "RIPPED APART": return "• Try the Steep trajectory: it leaves the thick air sooner\n• Put a nose cone or fairing on top\n• Use liquid engines: the computer eases them off through Max Q\n• Use fewer engines on stage 1 so it climbs a little slower";
            case "TURNED SIDEWAYS": return "• Install a flight computer AND an IMU inside the avionics bay\n• Small rockets: add 3-4 fins near the bottom";
            case "CRUSHED": return "• Use fewer or smaller engines / motors\n• Use liquid engines: they throttle down when the push gets too strong\n• Carry more fuel in the upper stages";
            case "FELL BACK": case "CRASHED": case "NO ORBIT": case "NO PARKING ORBIT":
                return "• Bigger tanks (more propellant)\n• An upper stage with a vacuum engine (higher Isp)\n• A lighter payload\n• Try the Shallow trajectory (less push lost to gravity)\n• Use the Mission Guide suggestions in AscendCAD";
            case "TOO LOW": return "• Use the bigger L850 motor\n• Make the rocket and the CanSat lighter\n• Choose the Straight Up flight path";
            case "TOO HIGH": return "• Use the smaller K700 motor\n• Or choose a flight path with more tilt";
            case "CAN'T REACH THE MOON": return "• A bigger upper stage (more propellant)\n• A more efficient upper engine (vacuum / hydrogen)\n• A lighter spacecraft\n• Try the Shallow trajectory";
            case "CAN'T STOP AT THE MOON": return "• Add hydrazine modules to the satellite (its own engine)\n• Make the satellite lighter\n• Keep more fuel in the upper stage";
            case "NO PAYLOAD": case "NO ROCKET": return "• Build it in AscendCAD and get it APPROVED";
            default: return "";
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  MISSION CONTROL for trajectories: send -> email -> CONFIRMED -> build the rocket
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendTrajectoryControl
{
    public static bool Pending { get; private set; }
    public static AscendSimPlan ConfirmedPlan { get; private set; }
    public static event Action<bool> Answered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Pending = false; ConfirmedPlan = null; }

    public static void Send(AscendCADData data, AscendSimResult r)
    {
        if (Pending || r == null) return;
        var plan = r.plan.Clone();
        AscendMail.Send(data.playerEmail, data.agencyEmail, "Trajectory review request: " + r.rocketName + " - " + AscendSimPlan.Title(plan.mission), Report(r, true));
        Pending = true;
        AscendRunner.After(data.reviewDelaySeconds, () =>
        {
            Pending = false;
            // Flight Dynamics re-runs the numbers on its own before confirming
            var check = AscendMissionControl.HasApproved
                ? AscendMissionSim.Run(AscendMissionControl.ApprovedRocket, AscendMissionControl.ApprovedPayload, data, plan) : null;
            bool ok = check != null && check.go;
            var sb = new StringBuilder("Dear design team,\n\n");
            if (ok)
            {
                sb.Append("Flight Dynamics checked your trajectory for <b>").Append(AscendSimPlan.Title(plan.mission)).Append("</b> and it is ")
                  .Append("<color=#3FB950><b>CONFIRMED</b></color>.\n\nYour rocket is being built on the launch pad right now. ")
                  .Append("Go to the launch control cube and press E to launch!\n\n");
            }
            else
            {
                sb.Append("Flight Dynamics checked your trajectory and it is <color=#F85149><b>NOT CONFIRMED</b></color>.\n\n")
                  .Append(check == null ? "There is no approved design waiting. Get your design APPROVED in AscendCAD first.\n\n"
                                        : "<b>Problem:</b> " + check.kid + "\n<color=#9AA0A6>" + check.expert + "</color>\n\n" +
                                          (string.IsNullOrEmpty(check.fix) ? "" : "<b>How to fix it</b>\n" + check.fix + "\n\n") +
                                          "Change the trajectory or the design and send it again.\n\n");
            }
            if (check != null) sb.Append(Report(check, false));
            sb.Append("\n\nRegards,\nAscend Philippines Flight Dynamics");
            AscendMail.Deliver(data.agencyEmail, data.playerEmail, (ok ? "CONFIRMED: " : "NOT CONFIRMED: ") + "trajectory for " + r.rocketName, sb.ToString(), ok);
            if (ok) { ConfirmedPlan = plan; AscendMissionControl.BuildApproved(data); }
            Answered?.Invoke(ok);
        });
    }

    public static string Report(AscendSimResult r, bool request)
    {
        var sb = new StringBuilder();
        if (request) sb.Append("Hello Flight Dynamics,\n\nPlease check our trajectory.\n\n");
        sb.Append("<b>Mission:</b> ").Append(AscendSimPlan.Title(r.plan.mission)).Append('\n');
        sb.Append("<b>Rocket:</b> ").Append(r.rocketName).Append(string.IsNullOrEmpty(r.payloadName) ? "" : "  <b>Payload:</b> " + r.payloadName).Append('\n');
        sb.Append($"<b>Plan:</b> pitch kick {r.plan.kickDeg:0.#}°, target {r.plan.targetKm:0.#} km\n");
        sb.Append("<b>Result:</b> ").Append(r.go ? "<color=#3FB950>" : "<color=#F85149>").Append(r.verdict).Append("</color>\n");
        sb.Append(r.kid).Append("\n\n<b>Timeline</b>\n");
        foreach (var e in r.events) sb.Append($"T+{(int)e.t / 60:00}:{(int)e.t % 60:00}  {e.kid}" + (string.IsNullOrEmpty(e.expert) ? "" : $" <color=#9AA0A6>({e.expert})</color>") + "\n");
        sb.Append($"\n<b>Numbers</b>\nLiftoff thrust-to-weight {r.liftoffTWR:0.00}\nHighest point {r.apogeeKm:N1} km\nMax Q {r.maxQ / 1000f:0.0} kPa, max {r.maxG:0.0} g\n");
        if (r.orbit) sb.Append($"Orbit {r.apoKm:N0} x {r.periKm:N0} km\n");
        if (r.plan.mission == AscendSimMission.MoonMapper && r.tliDone) sb.Append($"Transfer {r.transferDays:0.0} days, LOI need {r.loiNeed:N0} m/s, have {r.loiHave:N0} m/s\n");
        if (request) sb.Append("\nThank you,\nDesign Team");
        return sb.ToString();
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  THE APP SCREEN (put this on a prefab and drag it into the app's Window Content)
// ═════════════════════════════════════════════════════════════════════════════
[RequireComponent(typeof(RectTransform))]
public class AscendMissionSimulatorApp : MonoBehaviour
{
    [Tooltip("Your AscendCAD Data asset. Leave empty to use the one on the computer.")]
    public AscendCADData ascendCADData;
    [Tooltip("Optional TextMesh Pro font.")]
    public TMP_FontAsset font;

    static readonly Color Bg = AUI.Hex("#18191C"), Panel = AUI.Hex("#212226"), Panel2 = AUI.Hex("#2E2F35"), Txt = AUI.Hex("#E8EAED"),
        Sub = AUI.Hex("#9AA0A6"), Accent = AUI.Hex("#F07A26"), Blue = AUI.Hex("#0B57D0"), Good = AUI.Hex("#3FB950"), Bad = AUI.Hex("#F85149");
    static readonly Vector3 Origin = new Vector3(20000f, -5000f, -40000f), LocalOrigin = new Vector3(26000f, -5000f, -40000f);
    const float EarthUnits = 50f;
    static float S => EarthUnits / (float)AscendMissionSim.EarthR;

    AscendCADData data;
    RectTransform root;
    AscendSimMission mission;
    [NonSerialized] AscendSimPlan plan;
    [NonSerialized] AscendSimResult result;
    [NonSerialized] CADDesign seenDesign;

    // UI refs
    TextMeshProUGUI kickLabel, details;
    RectTransform advBox, detBox;
    Button advBtn, detBtn;
    [NonSerialized] AscendSimResult[] choiceRes;
    [NonSerialized] CADDesign choiceFor;
    int choiceSel = 1;
    readonly List<Image> choiceImgs = new List<Image>();
    RectTransform picRow;
    RawImage rocketPic, payloadPic;
    [NonSerialized] CADDesign picDesign;
    RenderTexture rocketTex, payloadTex;
    Transform clouds, glow;
    AscendSimMission sugMission = (AscendSimMission)(-1);
    TextMeshProUGUI rocketInfo, goalText, suggestText, kickVal, targetVal, targetLabel, status, verdictTitle, verdictBody, numbers, timeline, emptyMsg;
    Image verdictBox;
    Button calcBtn, sendBtn;
    TextMeshProUGUI missionChip, fixText, fixHeader, compareText, adviceHeader, adviceText;
    RectTransform sugBox;
    readonly List<AscendSimResult> compare = new List<AscendSimResult>();
    readonly LineRenderer[] compareLines = new LineRenderer[3];
    static readonly string[] CompareHex = { "#4FC3F7", "#FFD54F", "#CE93D8" };
    RawImage view;
    RectTransform viewRect;
    TextMeshProUGUI[] viewBtnLabels = new TextMeshProUGUI[3];

    // 3D
    GameObject world, globe, local;
    Camera cam; Light sun;
    RenderTexture rt;
    Transform earth, moon, marker;
    LineRenderer ascent, orbitLine, transfer, moonOrbit, chute;
    readonly List<GameObject> eventDots = new List<GameObject>();
    Material lineMat;
    int viewMode;  // 0 = Earth, 1 = Moon / Close-up
    Vector3 camTarget; float camYaw, camPitch = 18f, camDist = 180f;
    float play = -1f; const float PlaySeconds = 10f;
    List<Vector3> pathPts = new List<Vector3>(), transferPts = new List<Vector3>();

    // ═════════════════════════════════════════════════════════════════════════
    void Start()
    {
        root = (RectTransform)transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        data = ascendCADData ? AscendCADData.Resolve(ascendCADData) : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
        if (font) AUI.Font = font; else if (!AUI.Font && data.labelFont) AUI.Font = data.labelFont;
        AUI.FontScale = data.uiFontScale;
        AscendEconomy.Init(data);

        mission = AscendMissionGuide.Mission;
        plan = AscendSimPlan.Suggested(mission);
        viewMode = mission == AscendSimMission.SkySensor ? 1 : 0;

        BuildUI();
        BuildWorld();
        RefreshAll();
        AscendTrajectoryControl.Answered += OnAnswered;
    }

    void OnEnable() { if (cam) cam.enabled = true; }
    void OnDisable() { if (cam) cam.enabled = false; }

    void OnDestroy()
    {
        AscendTrajectoryControl.Answered -= OnAnswered;
        if (world) Destroy(world);
        if (rt) { rt.Release(); Destroy(rt); }
        if (lineMat) Destroy(lineMat);
        if (rocketTex) { rocketTex.Release(); Destroy(rocketTex); }
        if (payloadTex) { payloadTex.Release(); Destroy(payloadTex); }
    }

    void OnAnswered(bool ok)
    {
        if (!status) return;
        SetStatus(ok ? "CONFIRMED! Your rocket is being built on the launch pad. Go and launch it!" : "NOT CONFIRMED. Check PhMail to see why.", ok ? Good : Bad);
        RefreshAll();
    }

    void Update()
    {
        // The approved design changed (new approval or used up): refresh
        var d = AscendMissionControl.ApprovedRocket;
        if (d != seenDesign) { seenDesign = d; RefreshAll(); if (d != null) AutoPlan(); }
        if (AscendMissionGuide.Mission != mission) SetMission(AscendMissionGuide.Mission);   // follows the briefing / Mission Guide
        if (sendBtn) sendBtn.interactable = result != null && result.go && AscendMissionControl.HasApproved && !AscendTrajectoryControl.Pending;

        ResizeTexture();
        AnimatePlayback();
        UpdateCamera();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ACTIONS
    // ═════════════════════════════════════════════════════════════════════════
    void SetMission(AscendSimMission m)
    {
        mission = m; plan = AscendSimPlan.Suggested(m); result = null; ClearCompare(); viewMode = m == AscendSimMission.SkySensor ? 1 : 0;
        choiceFor = null; choiceSel = 1;
        ClearDrawing(); RefreshAll(); SetView(viewMode);
        if (AscendMissionControl.HasApproved) AutoPlan();
    }

    // Flies all three suggested paths with the approved rocket and picks the best one that works
    void EvaluateChoices()
    {
        var list = AscendSimPlan.Suggestions(mission);
        choiceFor = AscendMissionControl.ApprovedRocket;
        choiceRes = new AscendSimResult[list.Length];
        if (choiceFor == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            try { choiceRes[i] = AscendMissionSim.Run(AscendMissionControl.ApprovedRocket, AscendMissionControl.ApprovedPayload, data, list[i]); }
            catch (Exception e) { Debug.LogWarning("Mission Simulator: " + e.Message); }
        }
    }

    void AutoPlan()
    {
        EvaluateChoices();
        int pick = 1;
        if (choiceRes == null || choiceRes.Length < 2 || choiceRes[1] == null || !choiceRes[1].go)
            for (int i = 0; choiceRes != null && i < choiceRes.Length; i++) if (choiceRes[i] != null && choiceRes[i].go) { pick = i; break; }
        ChoosePlan(pick);
    }

    void ChoosePlan(int i)
    {
        var list = AscendSimPlan.Suggestions(mission);
        if (i < 0 || i >= list.Length) return;
        if (!AscendMissionControl.HasApproved) { SetStatus("No approved design yet. Get your rocket APPROVED in AscendCAD first.", Bad); return; }
        if (choiceFor != AscendMissionControl.ApprovedRocket || choiceRes == null) EvaluateChoices();
        choiceSel = i;
        plan = list[i].Clone();
        RefreshPlan();
        result = choiceRes != null && i < choiceRes.Length && choiceRes[i] != null ? choiceRes[i]
               : AscendMissionSim.Run(AscendMissionControl.ApprovedRocket, AscendMissionControl.ApprovedPayload, data, plan);
        ShowResult();
        DrawResult();
        BuildSuggestions();
        SetStatus(result.go ? "It works! Press SEND TO MISSION CONTROL." : "This flight path doesn't work with your rocket. Try another one, or read HOW TO FIX IT.", result.go ? Good : Bad);
    }

    void ToggleAdvanced()
    {
        bool on = !advBox.gameObject.activeSelf;
        advBox.gameObject.SetActive(on);
        advBtn.GetComponentInChildren<TextMeshProUGUI>().text = on ? "Advanced settings  -" : "Advanced settings  +";
    }

    void ToggleDetails()
    {
        bool on = !detBox.gameObject.activeSelf;
        detBox.gameObject.SetActive(on);
        detBtn.GetComponentInChildren<TextMeshProUGUI>().text = on ? "Hide details  -" : "Show details  +";
    }

    void Calculate()
    {
        if (!AscendMissionControl.HasApproved) { SetStatus("No approved design yet. Get your rocket APPROVED in AscendCAD first.", Bad); return; }
        plan.mission = mission;
        result = AscendMissionSim.Run(AscendMissionControl.ApprovedRocket, AscendMissionControl.ApprovedPayload, data, plan);
        choiceSel = -1;
        BuildSuggestions();
        ShowResult();
        DrawResult();
        SetStatus(result.go ? "It works! Press SEND TO MISSION CONTROL." : "These settings don't work. Pick one of the flight paths above.", result.go ? Good : Bad);
    }

    void Send()
    {
        if (result == null || !result.go) return;
        AscendTrajectoryControl.Send(data, result);
        SetStatus("Sent to Mission Control. The answer will arrive in PhMail shortly...", AUI.Hex("#8AB4F8"));
    }

    void Step(ref float v, float d, float min, float max) { v = Mathf.Clamp(Mathf.Round((v + d) * 100f) / 100f, min, max); choiceSel = -1; RefreshPlan(); BuildSuggestions(); }

    void Kick(int s) { Step(ref plan.kickDeg, s * (mission == AscendSimMission.SkySensor ? 1f : 0.5f), 0f, 15f); }

    void Target(int s)
    {
        switch (mission)
        {
            case AscendSimMission.SkySensor: Step(ref plan.targetKm, s * 0.25f, 0.25f, 10f); break;
            case AscendSimMission.EarthWatcher: Step(ref plan.targetKm, s * 50f, 200f, 2000f); break;
            default: Step(ref plan.targetKm, s * 20f, 160f, 600f); break;
        }
    }

    void SetStatus(string s, Color c) { status.text = s; status.color = c; }

    // ═════════════════════════════════════════════════════════════════════════
    //  UI
    // ═════════════════════════════════════════════════════════════════════════
    // Bigger text than AscendCAD (about 24 px on a 1080p screen). The panels grow with it.
    const float TextBoost = 1.4f;
    float LW => 330f * TextBoost;
    float RW => 320f * TextBoost;
    float TB => 72f;

    // AUI's text scale is shared with AscendCAD / PhMail, so only boost it while this app builds its UI
    float BeginBig() { float old = AUI.FontScale; AUI.FontScale = data.uiFontScale * TextBoost; return old; }

    void BuildUI()
    {
        float oldScale = BeginBig();
        AUI.Img(root, Bg);

        // Top bar
        var top = AUI.Rect("TopBar", root);
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one; top.pivot = new Vector2(0.5f, 1f);
        top.sizeDelta = new Vector2(0f, TB); top.anchoredPosition = Vector2.zero;
        AUI.Img(top, Panel);
        AUI.H(top, 16, 16, 8, 8, 10);
        var logo = AUI.Text(top, "<color=#F07A26>Ascend</color> Mission Simulator", 21, Txt, FontStyles.Bold);
        logo.textWrappingMode = TextWrappingModes.NoWrap; AUI.LE(logo, AUI.S(300));
        AUI.LE(AUI.Rect("Spacer", top), -1, -1, 1);
        missionChip = AUI.Text(top, "", 15, Txt);
        missionChip.textWrappingMode = TextWrappingModes.NoWrap; missionChip.alignment = TextAlignmentOptions.MidlineRight;
        AUI.LE(missionChip, -1, -1, 1); missionChip.overflowMode = TextOverflowModes.Ellipsis;

        // Left: mission + rocket + trajectory controls
        var left = AUI.Rect("Left", root);
        left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0f, 1f); left.pivot = new Vector2(0f, 0.5f);
        left.offsetMin = Vector2.zero; left.offsetMax = new Vector2(LW, -TB);
        AUI.Img(left, Panel);
        var lc = AUI.Scroll(left, 0, 0, 0, 0);
        AUI.V(lc, 18, 18, 16, 18, 10);

        Header(lc, "1. MISSION");
        goalText = AUI.Text(lc, "", 14, Txt);
        Header(lc, "2. YOUR ROCKET");
        rocketInfo = AUI.Text(lc, "", 13, Txt); rocketInfo.lineSpacing = 4f;
        picRow = AUI.Rect("Pictures", lc);
        AUI.H(picRow, 0, 0, 0, 0, 10, TextAnchor.MiddleCenter);
        AUI.LE(picRow, -1, AUI.S(200));
        rocketPic = PictureBox(picRow, "ROCKET");
        payloadPic = PictureBox(picRow, "PAYLOAD");
        picRow.gameObject.SetActive(false);
        Header(lc, "3. PICK A FLIGHT PATH");
        sugBox = AUI.Rect("Choices", lc);
        AUI.V(sugBox, 0, 0, 0, 0, 8);
        Header(lc, "4. SEND IT");
        sendBtn = AUI.Btn(lc, "SEND TO MISSION CONTROL", 14, Color.white, Blue, Send, -1, 48);
        status = AUI.Text(lc, "", 13, Sub);
        advBtn = AUI.Btn(lc, "Advanced settings  +", 12, Sub, Panel2, ToggleAdvanced, -1, 34);
        advBox = AUI.Rect("Advanced", lc);
        AUI.V(advBox, 0, 0, 0, 0, 10);
        suggestText = AUI.Text(advBox, "", 13, Txt); suggestText.lineSpacing = 4f;
        kickVal = StepperRow(advBox, "Pitch kick (tilt after liftoff)", () => Kick(-1), () => Kick(1), out kickLabel);
        targetVal = StepperRow(advBox, "Target height", () => Target(-1), () => Target(1), out targetLabel);
        calcBtn = AUI.Btn(advBox, "CALCULATE MY SETTINGS", 14, Color.white, Accent, Calculate, -1, 42);
        AUI.Btn(advBox, "COMPARE ALL 3 IN 3D", 13, Color.white, AUI.Hex("#2E7D5B"), Compare, -1, 36);
        advBox.gameObject.SetActive(false);

        // Right: result + timeline
        var right = AUI.Rect("Right", root);
        right.anchorMin = new Vector2(1f, 0f); right.anchorMax = Vector2.one; right.pivot = new Vector2(1f, 0.5f);
        right.offsetMin = new Vector2(-RW, 0f); right.offsetMax = new Vector2(0f, -TB);
        AUI.Img(right, Panel);
        var rc = AUI.Scroll(right, 0, 0, 0, 0);
        AUI.V(rc, 18, 18, 16, 18, 10);
        Header(rc, "RESULT");
        var vb = AUI.Rect("Verdict", rc);
        verdictBox = AUI.Img(vb, Panel2, true);
        AUI.V(vb, 14, 14, 12, 12, 6);
        verdictTitle = AUI.Text(vb, "Not calculated yet", 22, Txt, FontStyles.Bold);
        verdictBody = AUI.Text(vb, "Get a design APPROVED in AscendCAD. The flight is planned for you as soon as it's approved.", 13, Txt);
        adviceHeader = AUI.Text(rc, "HOW TO IMPROVE IT", 13, AUI.Hex("#8AB4F8"), FontStyles.Bold); MinHeight(adviceHeader, AUI.S(22));
        adviceText = AUI.Text(rc, "", 13, Txt); adviceText.lineSpacing = 4f;
        adviceHeader.gameObject.SetActive(false); adviceText.gameObject.SetActive(false);
        fixHeader = AUI.Text(rc, "HOW TO FIX IT (DESIGN)", 13, Bad, FontStyles.Bold); MinHeight(fixHeader, AUI.S(22));
        fixText = AUI.Text(rc, "", 13, Txt); fixText.lineSpacing = 4f;
        fixHeader.gameObject.SetActive(false); fixText.gameObject.SetActive(false);
        numbers = AUI.Text(rc, "", 13, Txt); numbers.lineSpacing = 4f;
        detBtn = AUI.Btn(rc, "Show details  +", 12, Sub, Panel2, ToggleDetails, -1, 34);
        detBox = AUI.Rect("Details", rc);
        AUI.V(detBox, 0, 0, 0, 0, 10);
        details = AUI.Text(detBox, "", 12, Txt); details.lineSpacing = 4f;
        Header(detBox, "COMPARE FLIGHT PATHS");
        compareText = AUI.Text(detBox, "<color=#9AA0A6>Press COMPARE ALL 3 IN 3D (Advanced settings) to see all three lines.</color>", 12, Txt);
        compareText.lineSpacing = 4f;
        Header(detBox, "TIMELINE");
        timeline = AUI.Text(detBox, "", 12, Txt); timeline.lineSpacing = 4f;
        detBox.gameObject.SetActive(false);

        // Center: 3D view
        var center = AUI.Rect("View", root);
        AUI.Fill(center, LW, 0, RW, TB);
        viewRect = center;
        view = center.gameObject.AddComponent<RawImage>();
        view.color = Color.white;
        var ptr = center.gameObject.AddComponent<AscendPointer>();
        ptr.drag = e => { camYaw += e.delta.x * 0.3f; camPitch = Mathf.Clamp(camPitch - e.delta.y * 0.3f, -85f, 85f); };
        ptr.scroll = e => { camDist = Mathf.Clamp(camDist * (1f - e.scrollDelta.y * 0.08f), MinDist(), MaxDist()); };

        var bar = AUI.Rect("ViewBar", center);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(AUI.S(400f), AUI.S(44f)); bar.anchoredPosition = new Vector2(0f, 12f);
        AUI.H(bar, 0, 0, 0, 0, 8, TextAnchor.MiddleCenter);
        viewBtnLabels[0] = AUI.Btn(bar, "Earth", 13, Txt, Panel2, () => SetView(0), 120, 36).GetComponentInChildren<TextMeshProUGUI>();
        viewBtnLabels[1] = AUI.Btn(bar, "Moon", 13, Txt, Panel2, () => SetView(1), 120, 36).GetComponentInChildren<TextMeshProUGUI>();
        AUI.Btn(bar, "Replay", 13, Color.white, Accent, () => { if (result != null) play = 0f; }, 120, 36);

        emptyMsg = AUI.Text(center, "", 18, Txt, FontStyles.Bold);
        AUI.Fill(emptyMsg.rectTransform, 40, 80, 40, 40);
        emptyMsg.alignment = TextAlignmentOptions.Center;
        AUI.FontScale = oldScale;
    }

    void Header(RectTransform p, string s) { var t = AUI.Text(p, s, 13, AUI.Hex("#F07A26"), FontStyles.Bold); MinHeight(t, AUI.S(26)); }

    // Minimum height only, so a header that wraps onto two lines grows instead of overlapping the next item
    static void MinHeight(Component c, float h) { var le = c.GetComponent<LayoutElement>(); if (!le) le = c.gameObject.AddComponent<LayoutElement>(); le.minHeight = h; }

    TextMeshProUGUI StepperRow(RectTransform p, string label, Action minus, Action plus) => StepperRow(p, label, minus, plus, out _);

    TextMeshProUGUI StepperRow(RectTransform p, string label, Action minus, Action plus, out TextMeshProUGUI labelText)
    {
        labelText = AUI.Text(p, label, 13, Sub);
        var row = AUI.Rect("Row", p);
        AUI.H(row, 0, 0, 0, 0, 8);
        AUI.LE(row, -1, AUI.S(38));
        AUI.Btn(row, "-", 18, Txt, Panel2, minus, 44, 36);
        var v = AUI.Text(row, "", 17, Txt, FontStyles.Bold);
        v.alignment = TextAlignmentOptions.Center; AUI.LE(v, -1, AUI.S(36), 1);
        AUI.Btn(row, "+", 18, Txt, Panel2, plus, 44, 36);
        return v;
    }

    void RefreshAll()
    {
        if (!goalText) return;
        missionChip.text = $"Mission: <b>{AscendSimPlan.Title(mission)}</b>  <color=#9AA0A6>{(mission == AscendSimMission.SkySensor ? "Easy" : mission == AscendSimMission.EarthWatcher ? "Medium" : "Hard")}</color>";
        goalText.text = $"<b>{AscendSimPlan.Title(mission)}</b>\n{AscendSimPlan.Goal(mission)}\n<size=90%><color=#9AA0A6>{AscendSimPlan.Expert(mission)}</color></size>";
        viewBtnLabels[1].text = mission == AscendSimMission.SkySensor ? "Close-up" : mission == AscendSimMission.MoonMapper ? "Moon" : "Launch";

        var d = AscendMissionControl.ApprovedRocket;
        if (d == null)
        {
            rocketInfo.text = "<color=#F85149>No approved design waiting.</color>\nBuild a rocket in AscendCAD, press SUBMIT and wait for the APPROVED email.";
            emptyMsg.text = result == null ? "No approved rocket yet.\n<size=75%><color=#9AA0A6>Get a design APPROVED in AscendCAD, then come back here.</color></size>" : "";
        }
        else
        {
            var rs = AscendMissionControl.ApprovedStats;
            var p = AscendMissionControl.ApprovedPayload;
            rocketInfo.text = $"<b>{d.name}</b>" + (p != null ? $" carrying <b>{p.name}</b>" : " <color=#D29922>(no payload)</color>") + "\n" +
                (rs != null ? $"Liftoff mass {AscendReport.Mass(rs.wetMass + rs.payloadMass)}\nStages {rs.stages.Count}   Fuel power {rs.deltaV:N0} m/s\nPush vs weight {rs.liftoffTWR:0.00}x" : "");
            emptyMsg.text = result == null ? "Pick a flight path on the left." : "";
        }
        RefreshPictures(d, AscendMissionControl.ApprovedPayload);
        RefreshPlan();
    }

    RawImage PictureBox(RectTransform parent, string caption)
    {
        var box = AUI.Rect(caption, parent);
        AUI.Img(box, AUI.Hex("#1B1C20"), true);
        AUI.LE(box, -1, AUI.S(200), 1);
        var img = AUI.Rect("Picture", box).gameObject.AddComponent<RawImage>();
        AUI.Fill(img.rectTransform, 4, AUI.S(20), 4, 4);
        img.raycastTarget = false;
        var t = AUI.Text(box, caption, 10, Sub, FontStyles.Bold);
        t.alignment = TextAlignmentOptions.Bottom;
        AUI.Fill(t.rectTransform, 4, 3, 4, 4);
        return img;
    }

    // Draws the approved rocket and payload once (only again when the design changes)
    void RefreshPictures(CADDesign rocketD, CADDesign payloadD)
    {
        if (!picRow) return;
        if (rocketD == picDesign) return;
        picDesign = rocketD;
        if (rocketTex) { rocketTex.Release(); Destroy(rocketTex); rocketTex = null; }
        if (payloadTex) { payloadTex.Release(); Destroy(payloadTex); payloadTex = null; }
        if (rocketD == null) { picRow.gameObject.SetActive(false); return; }
        var bg = AUI.Hex("#1B1C20");
        try { rocketTex = AscendSnapshot.Design(rocketD, data, 240, 360, bg); } catch (Exception e) { Debug.LogWarning("Mission Simulator: rocket picture failed: " + e.Message); }
        try { payloadTex = payloadD != null ? AscendSnapshot.Design(payloadD, data, 240, 360, bg) : null; } catch (Exception e) { Debug.LogWarning("Mission Simulator: payload picture failed: " + e.Message); }
        rocketPic.texture = rocketTex; rocketPic.color = rocketTex ? Color.white : Color.clear;
        payloadPic.texture = payloadTex; payloadPic.color = payloadTex ? Color.white : Color.clear;
        payloadPic.transform.parent.gameObject.SetActive(payloadTex != null);
        picRow.gameObject.SetActive(rocketTex != null);
    }

    void RefreshPlan()
    {
        if (!kickVal) return;
        switch (mission)
        {
            case AscendSimMission.SkySensor:
                suggestText.text = "<color=#9AA0A6>How it flies: the motor burns, the rocket coasts to the top, then the CanSat pops out and floats down on its parachute.</color>";
                targetLabel.text = "Highest point (apogee)";
                kickLabel.text = "Launch rail tilt";
                break;
            case AscendSimMission.EarthWatcher:
                suggestText.text = "<color=#9AA0A6>How it flies: straight up, then it slowly tips over. The engines stop when the orbit is high enough, and one more burn at the top makes the orbit round.</color>";
                targetLabel.text = "Orbit height";
                kickLabel.text = "Pitch kick (tilt after liftoff)";
                break;
            default:
                suggestText.text = "<color=#9AA0A6>How it flies: into orbit around Earth, a big burn toward the Moon, 3 to 5 days of travel, then a braking burn to circle the Moon.</color>";
                targetLabel.text = "Parking orbit height";
                kickLabel.text = "Pitch kick (tilt after liftoff)";
                break;
        }
        if (sugMission != mission || sugBox.childCount == 0) BuildSuggestions();
        kickVal.text = $"{plan.kickDeg:0.#}°";
        targetVal.text = mission == AscendSimMission.SkySensor ? $"{plan.targetKm:0.##} km" : $"{plan.targetKm:0} km";
    }

    void BuildSuggestions()
    {
        float oldScale = BeginBig();
        try { BuildSuggestionsInner(); } finally { AUI.FontScale = oldScale; }
    }

    void BuildSuggestionsInner()
    {
        sugMission = mission;
        choiceImgs.Clear();
        for (int i = sugBox.childCount - 1; i >= 0; i--) { var c = sugBox.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
        var list = AscendSimPlan.Suggestions(mission);
        bool fresh = choiceRes != null && choiceFor == AscendMissionControl.ApprovedRocket;
        for (int i = 0; i < list.Length; i++)
        {
            var sp = list[i];
            int idx = i;
            bool sel = i == choiceSel;
            var r = fresh && i < choiceRes.Length ? choiceRes[i] : null;
            var card = AUI.Rect("Choice " + sp.name, sugBox);
            var img = AUI.Img(card, sel ? new Color(0.94f, 0.48f, 0.15f, 0.35f) : Panel2, true, true);
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() => ChoosePlan(idx));
            AUI.V(card, 14, 14, 10, 10, 3);
            string state = r == null ? "" : r.go ? "   <color=#3FB950>WORKS</color>" : $"   <color=#F85149>{r.verdict}</color>";
            AUI.Text(card, $"{(sel ? "> " : "")}<b>{sp.name}</b>{(i == 1 ? "  <color=#9AA0A6>(recommended)</color>" : "")}{state}", 15, Txt);
            AUI.Text(card, $"<color=#9AA0A6>{sp.about}</color>", 12, Txt);
            choiceImgs.Add(img);
        }
    }

    void Compare()
    {
        if (!AscendMissionControl.HasApproved) { SetStatus("No approved design yet. Get your rocket APPROVED in AscendCAD first.", Bad); return; }
        compare.Clear();
        var list = AscendSimPlan.Suggestions(mission);
        var sb = new StringBuilder();
        for (int i = 0; i < list.Length; i++)
        {
            var r = AscendMissionSim.Run(AscendMissionControl.ApprovedRocket, AscendMissionControl.ApprovedPayload, data, list[i]);
            compare.Add(r);
            sb.Append($"<color={CompareHex[i]}><b>{list[i].name}</b></color>  ").Append(r.go ? "<color=#3FB950>GO</color>" : "<color=#F85149>" + r.verdict + "</color>");
            if (mission == AscendSimMission.SkySensor) sb.Append($"  top {r.apogeeKm * 1000f:N0} m");
            else if (r.orbit) sb.Append($"  orbit {r.apoKm:N0} x {r.periKm:N0} km");
            else sb.Append($"  top {r.apogeeKm:N0} km");
            sb.Append($"\n<size=90%><color=#9AA0A6>Air push {r.maxQ / 1000f:0.0} kPa, max {r.maxG:0.0} g" + (mission != AscendSimMission.SkySensor ? $", {r.dvLeft:N0} m/s left" : "") + "</color></size>\n");
        }
        compareText.text = sb.ToString();
        DrawCompare();
        emptyMsg.text = "";
        SetStatus("Compared all 3 suggested trajectories. Each line in the 3D view has the trajectory's color.", AUI.Hex("#8AB4F8"));
    }

    void DrawCompare()
    {
        bool closeUp = mission == AscendSimMission.SkySensor;
        for (int i = 0; i < compareLines.Length; i++)
        {
            var lr = compareLines[i];
            if (!lr) continue;
            if (i >= compare.Count) { lr.positionCount = 0; continue; }
            var r = compare[i];
            lr.positionCount = r.xs.Count;
            for (int k = 0; k < r.xs.Count; k++) lr.SetPosition(k, closeUp ? LocalPoint(r.xs[k], r.ys[k]) : G(r.xs[k], r.ys[k]));
        }
        SetView(viewMode);
    }

    void ClearCompare()
    {
        compare.Clear();
        foreach (var lr in compareLines) if (lr) lr.positionCount = 0;
        if (compareText) compareText.text = "<color=#9AA0A6>Press COMPARE ALL 3 IN 3D to fly all suggested trajectories and see their lines.</color>";
    }

    void ShowResult()
    {
        emptyMsg.text = "";
        verdictTitle.text = result.verdict;
        verdictTitle.color = result.go ? Good : Bad;
        verdictBox.color = result.go ? new Color(0.25f, 0.85f, 0.45f, 0.15f) : new Color(1f, 0.33f, 0.3f, 0.15f);
        verdictTitle.text = result.go ? "IT WORKS!" + (result.verdict != "GO" ? "  <size=70%>(" + result.verdict.Replace("GO ", "").Trim('(', ')') + ")</size>" : "") : result.verdict;
        verdictBody.text = result.kid;
        details.text = "<b>Engineer's note</b>\n<color=#9AA0A6>" + result.expert + "</color>\n" +
                       $"Push vs weight at liftoff {result.liftoffTWR:0.00}x\nStrongest air push {result.maxQ / 1000f:0.0} kPa   Max {result.maxG:0.0} g" +
                       (mission != AscendSimMission.SkySensor ? $"\nFuel power left {result.dvLeft:N0} m/s" : "");
        bool showFix = !result.go && !string.IsNullOrEmpty(result.fix);
        fixHeader.gameObject.SetActive(showFix); fixText.gameObject.SetActive(showFix);
        fixText.text = showFix ? result.fix : "";
        bool showAdvice = !result.go && !string.IsNullOrEmpty(result.advice);
        adviceHeader.gameObject.SetActive(showAdvice); adviceText.gameObject.SetActive(showAdvice);
        adviceText.text = result.advice ?? "";
        var sb = new StringBuilder();
        sb.Append(mission == AscendSimMission.SkySensor ? $"Highest point  <b>{result.apogeeKm * 1000f:N0} m</b>\n" : $"Highest point  <b>{result.apogeeKm:N0} km</b>\n");
        if (result.orbit) sb.Append($"Orbit  <b>{result.apoKm:N0} x {result.periKm:N0} km</b>\n");
        if (mission == AscendSimMission.MoonMapper && result.tliDone) sb.Append($"Trip to the Moon  <b>{result.transferDays:0.0} days</b>\n");
        sb.Append($"Flight time  <b>{(int)result.flightTime / 60:00}:{(int)result.flightTime % 60:00}</b>");
        numbers.text = sb.ToString();
        var tl = new StringBuilder();
        foreach (var e in result.events)
        {
            string hex = ColorUtility.ToHtmlStringRGB(e.color);
            tl.Append($"<color=#{hex}>T+{(int)e.t / 60:00}:{(int)e.t % 60:00}</color>  {e.kid}");
            if (!string.IsNullOrEmpty(e.expert)) tl.Append($"\n<size=85%><color=#9AA0A6>      {e.expert}</color></size>");
            tl.Append('\n');
        }
        timeline.text = tl.ToString();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  3D WORLD (Earth, atmosphere, Moon, stars, close-up launch field)
    // ═════════════════════════════════════════════════════════════════════════
    void BuildWorld()
    {
        int layer = data.cadLayer;
        world = new GameObject("Ascend Mission Simulator World");
        world.transform.position = Origin;

        globe = new GameObject("Globe"); globe.transform.SetParent(world.transform, false);
        earth = Sphere("Earth", globe.transform, Vector3.zero, EarthUnits * 2f, EarthTexture(), Color.white).transform;
        var em = earth.GetComponent<Renderer>().sharedMaterial;
        if (em.HasProperty("_Smoothness")) em.SetFloat("_Smoothness", 0.35f);
        // Clouds: a slightly bigger see-through sphere that turns slowly
        clouds = Sphere("Clouds", globe.transform, Vector3.zero, EarthUnits * 2f * 1.012f, CloudTexture(), Color.white).transform;
        MakeTransparent(clouds.GetComponent<Renderer>().sharedMaterial);
        // Atmosphere: a soft blue glow around the edge that always faces the camera
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = "Atmosphere Glow";
        var gc = g.GetComponent<Collider>(); if (gc) Destroy(gc);
        g.transform.SetParent(globe.transform, false);
        g.transform.localScale = Vector3.one * EarthUnits * 2f * GlowScale;
        var gm = new Material(Shader.Find("Sprites/Default")) { mainTexture = GlowTexture(), color = new Color(0.5f, 0.75f, 1f, 1f) };
        g.GetComponent<Renderer>().sharedMaterial = gm;
        glow = g.transform;
        moon = Sphere("Moon", globe.transform, new Vector3(-(float)AscendMissionSim.MoonDist * S, 0f, 0f), (float)AscendMissionSim.MoonR * 2f * S, MoonTexture(), Color.white).transform;
        Stars(globe.transform);

        local = new GameObject("Close-up"); local.transform.SetParent(world.transform, false);
        local.transform.position = LocalOrigin;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(ground.GetComponent<Collider>());
        ground.transform.SetParent(local.transform, false);
        ground.transform.localScale = new Vector3(600f, 0.2f, 600f);
        ground.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        ground.GetComponent<Renderer>().sharedMaterial = LitMat(null, new Color(0.25f, 0.55f, 0.3f));
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(pad.GetComponent<Collider>());
        pad.transform.SetParent(local.transform, false);
        pad.transform.localScale = new Vector3(3f, 1f, 3f);
        pad.GetComponent<Renderer>().sharedMaterial = LitMat(null, new Color(0.6f, 0.6f, 0.62f));
        local.SetActive(false);

        lineMat = new Material(Shader.Find("Sprites/Default"));
        ascent = Line("Ascent", new Color(1f, 0.6f, 0.2f));
        orbitLine = Line("Orbit", new Color(0.4f, 0.85f, 1f));
        transfer = Line("Transfer", new Color(0.7f, 0.6f, 1f));
        moonOrbit = Line("Lunar orbit", Color.white);
        chute = Line("Parachute", new Color(0.4f, 1f, 0.6f));
        for (int i = 0; i < compareLines.Length; i++) compareLines[i] = Line("Compare " + (i + 1), AUI.Hex(CompareHex[i]));
        marker = Sphere("Rocket", world.transform, Vector3.zero, 1f, null, new Color(1f, 0.9f, 0.3f), true).transform;
        marker.gameObject.SetActive(false);

        var sunGo = new GameObject("Sim Sun"); sunGo.transform.SetParent(world.transform, false);
        sunGo.transform.rotation = Quaternion.Euler(25f, -40f, 0f);
        sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.3f; sun.cullingMask = 1 << layer;

        var camGo = new GameObject("Sim Camera"); camGo.transform.SetParent(world.transform, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        cam.cullingMask = 1 << layer; cam.nearClipPlane = 0.05f; cam.farClipPlane = 20000f; cam.fieldOfView = 45f;
        cam.depth = -50;

        SetLayer(world, layer);
        SetView(viewMode);
    }

    static void SetLayer(GameObject g, int layer) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

    GameObject Sphere(string name, Transform parent, Vector3 localPos, float diameter, Texture2D tex, Color c, bool unlit = false)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        Object.Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localPosition = localPos;
        s.transform.localScale = Vector3.one * diameter;
        s.GetComponent<Renderer>().sharedMaterial = unlit ? UnlitMat(c) : LitMat(tex, c);
        return s;
    }

    Material LitMat(Texture2D tex, Color c)
    {
        var m = new Material(CADModels.Lit(data));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (tex) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return m;
    }

    static Material UnlitMat(Color c)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { color = c };
        return m;
    }

    LineRenderer Line(string name, Color c)
    {
        var go = new GameObject(name); go.transform.SetParent(world.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = lineMat; lr.startColor = lr.endColor = c;
        lr.useWorldSpace = true; lr.positionCount = 0; lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        return lr;
    }

    const float GlowScale = 1.2f;
    static Texture2D earthTex, cloudTex, moonTex, glowTex;

    // Smooth 3D-looking noise from Unity's 2D Perlin noise (no seam around the globe)
    static float N3(float x, float y, float z) =>
        (Mathf.PerlinNoise(x, y) + Mathf.PerlinNoise(y, z) + Mathf.PerlinNoise(z, x) + Mathf.PerlinNoise(y + 31.7f, x) + Mathf.PerlinNoise(z + 17.3f, y) + Mathf.PerlinNoise(x + 9.1f, z)) / 6f;

    static float Fbm(Vector3 p, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++) { sum += N3(p.x, p.y, p.z) * amp; norm += amp; amp *= 0.5f; p = p * 2.03f + new Vector3(5.2f, 1.3f, 7.7f); }
        // Averaged Perlin values bunch around 0.5: stretch them back to 0..1
        return Mathf.Clamp01((sum / norm - 0.5f) * 2.4f + 0.5f);
    }

    static Vector3 SpherePoint(int x, int y, int w, int h)
    {
        float lon = (x + 0.5f) / w * Mathf.PI * 2f, lat = ((y + 0.5f) / h - 0.5f) * Mathf.PI;
        return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);

    static Texture2D EarthTexture()
    {
        if (earthTex) return earthTex;
        const int w = 640, h = 320;   // made once per session (about half a second)
        var tex = new Texture2D(w, h, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = "Sim Earth" };
        var px = new Color32[w * h];
        Color deep = C(0.01f, 0.05f, 0.17f), sea = C(0.03f, 0.16f, 0.38f), shore = C(0.07f, 0.33f, 0.50f), ice = C(0.93f, 0.95f, 0.98f);
        Color jungle = C(0.09f, 0.27f, 0.09f), savanna = C(0.47f, 0.47f, 0.24f), desert = C(0.78f, 0.66f, 0.45f), forest = C(0.16f, 0.33f, 0.14f);
        Color grass = C(0.34f, 0.42f, 0.21f), taiga = C(0.12f, 0.24f, 0.14f), tundra = C(0.47f, 0.45f, 0.38f), rock = C(0.40f, 0.34f, 0.28f);
        for (int y = 0; y < h; y++)
        {
            float lat = Mathf.Abs(((y + 0.5f) / h - 0.5f) * 180f);
            for (int x = 0; x < w; x++)
            {
                var p = SpherePoint(x, y, w, h);
                float land = Fbm(p * 1.7f + new Vector3(3f, 7f, 1f), 5);
                float wet = Fbm(p * 2.6f + new Vector3(40f, 2f, 9f), 3);
                float iceLine = 70f + (wet - 0.5f) * 10f;
                Color c;
                if (land < 0.55f)
                {
                    float depth = Mathf.InverseLerp(0.55f, 0.25f, land);
                    c = depth < 0.08f ? Color.Lerp(shore, sea, depth / 0.08f) : Color.Lerp(sea, deep, (depth - 0.08f) / 0.92f);
                    if (lat > iceLine + 6f) c = Color.Lerp(c, ice, 0.85f);                      // sea ice
                }
                else
                {
                    float hi = Mathf.InverseLerp(0.55f, 0.85f, land);                          // height above the coast
                    if (lat < 14f) c = Color.Lerp(jungle, savanna, Mathf.Clamp01((0.5f - wet) * 2f));
                    else if (lat < 34f) c = wet < 0.5f ? Color.Lerp(desert, savanna, wet * 1.6f) : Color.Lerp(savanna, forest, (wet - 0.5f) * 2f);
                    else if (lat < 56f) c = Color.Lerp(grass, forest, wet);
                    else c = Color.Lerp(taiga, tundra, Mathf.InverseLerp(56f, iceLine, lat));
                    c = Color.Lerp(c, rock, Mathf.Clamp01((hi - 0.45f) * 2.2f));                // mountains
                    if (hi > 0.8f || lat > iceLine) c = ice;                                       // snow peaks and ice caps
                }
                px[y * w + x] = c;
            }
        }
        tex.SetPixels32(px); tex.Apply(true);
        return earthTex = tex;
    }

    static Texture2D CloudTexture()
    {
        if (cloudTex) return cloudTex;
        const int w = 384, h = 192;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Sim Clouds" };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float lat = ((y + 0.5f) / h - 0.5f) * 180f;
            float belt = 0.6f + 0.4f * Mathf.Abs(Mathf.Cos(lat * 3f * Mathf.Deg2Rad));      // cloud belts like the real ones
            for (int x = 0; x < w; x++)
            {
                var p = SpherePoint(x, y, w, h);
                float n = Fbm(p * 3.2f + new Vector3(60f, 11f, 4f), 4);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.78f, n * belt + 0.1f)) * 0.85f;
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels32(px); tex.Apply(true);
        return cloudTex = tex;
    }

    // Soft ring that shows as a glowing atmosphere around the planet's edge
    static Texture2D GlowTexture()
    {
        if (glowTex) return glowTex;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Sim Glow" };
        var px = new Color32[n * n];
        float edge = 1f / GlowScale;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f).magnitude;
                float a = r < edge ? Mathf.Pow(r / edge, 10f) * 0.6f : Mathf.Pow(Mathf.Clamp01(1f - (r - edge) / (1f - edge)), 2.2f) * 0.75f;
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels32(px); tex.Apply();
        return glowTex = tex;
    }

    static Texture2D MoonTexture()
    {
        if (moonTex) return moonTex;
        const int w = 512, h = 256;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "Sim Moon" };
        var shade = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = SpherePoint(x, y, w, h);
                float maria = Fbm(p * 1.4f + new Vector3(8f, 3f, 5f), 3);
                float grain = Fbm(p * 9f + new Vector3(2f, 9f, 4f), 3);
                float g = Mathf.Lerp(0.5f, 0.72f, grain);
                if (maria < 0.42f) g *= Mathf.Lerp(0.62f, 1f, maria / 0.42f);                 // dark "seas" of old lava
                shade[y * w + x] = g;
            }
        var rnd = new System.Random(3);
        for (int k = 0; k < 140; k++)                                                           // craters: dark floor, bright rim
        {
            int cx = rnd.Next(w), cy = rnd.Next(h / 8, h - h / 8);
            float rad = (float)(1.5 + Math.Pow(rnd.NextDouble(), 3) * 16);
            int ri = Mathf.CeilToInt(rad * 1.3f);
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    int yy = cy + dy; if (yy < 0 || yy >= h) continue;
                    int xx = (cx + dx + w) % w;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / rad;
                    if (d > 1.3f) continue;
                    float f = d < 0.85f ? 0.8f + 0.1f * d : d < 1.05f ? 1.25f : Mathf.Lerp(1.12f, 1f, (d - 1.05f) / 0.25f);
                    shade[yy * w + xx] *= f;
                }
        }
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) { float g = Mathf.Clamp01(shade[i]); px[i] = new Color(g, g * 0.99f, g * 0.97f); }
        tex.SetPixels32(px); tex.Apply(true);
        return moonTex = tex;
    }

    // Lit material that shows its texture's alpha (URP Lit or Built-in Standard)
    static void MakeTransparent(Material m)
    {
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) { m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
        }
        else
        {
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
        }
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static void Stars(Transform parent)
    {
        var mesh = new Mesh { name = "Stars" };
        var verts = new List<Vector3>(); var tris = new List<int>(); var cols = new List<Color>();
        var rnd = new System.Random(7);
        for (int i = 0; i < 1200; i++)
        {
            Vector3 d = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1).normalized * 9000f;
            float s = 6f + (float)rnd.NextDouble() * 14f;
            Vector3 a = Vector3.Cross(d, Vector3.up).normalized; if (a.sqrMagnitude < 0.1f) a = Vector3.right;
            Vector3 b = Vector3.Cross(d, a).normalized;
            int k = verts.Count;
            verts.Add(d + a * s); verts.Add(d + b * s); verts.Add(d - a * s); verts.Add(d - b * s);
            float br = 0.5f + (float)rnd.NextDouble() * 0.5f;
            for (int j = 0; j < 4; j++) cols.Add(new Color(br, br, br));
            tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3, k, k + 2, k + 1, k, k + 3, k + 2 });
        }
        mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
        var go = new GameObject("Stars"); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = UnlitMat(Color.white);
    }

    // ───── Drawing the result ─────
    Vector3 G(double x, double y) => earth.position + new Vector3((float)(x * S), (float)((y) * S), 0f);
    Vector3 L(double down, double alt) => local.transform.position + new Vector3((float)(down * 0.1), (float)(alt * 0.1), 0f);

    void ClearDrawing()
    {
        ascent.positionCount = orbitLine.positionCount = transfer.positionCount = moonOrbit.positionCount = chute.positionCount = 0;
        foreach (var d in eventDots) if (d) Destroy(d);
        eventDots.Clear();
        marker.gameObject.SetActive(false);
        pathPts.Clear(); transferPts.Clear();
        play = -1f;
    }

    void DrawResult()
    {
        ClearDrawing();
        if (result == null) return;
        bool closeUp = mission == AscendSimMission.SkySensor;

        for (int i = 0; i < result.xs.Count; i++)
            pathPts.Add(closeUp ? LocalPoint(result.xs[i], result.ys[i]) : G(result.xs[i], result.ys[i]));

        foreach (var e in result.events)
        {
            var dot = Sphere("Event", world.transform, Vector3.zero, 1f, null, e.color, true);
            dot.transform.position = closeUp ? LocalPoint(e.x, e.y) : G(e.x, e.y);
            dot.layer = data.cadLayer;
            dot.SetActive(false);
            eventDots.Add(dot);
        }

        // Orbit ring / Moon transfer
        if (result.orbit && mission == AscendSimMission.EarthWatcher) Conic(orbitLine, 0, Math.PI * 2, 180, true);
        if (mission == AscendSimMission.MoonMapper && result.tliDone && !double.IsInfinity(result.a) && result.a > 0)
        {
            // Half ellipse from the burn point to the far point (where the Moon is waiting)
            double p = result.a * (1 - result.e * result.e);
            double rx = result.xs[result.xs.Count - 1], ry = result.ys[result.ys.Count - 1];
            double nu0 = Math.Atan2(ry, rx) - result.omega;
            nu0 = Math.Atan2(Math.Sin(nu0), Math.Cos(nu0)) * result.dir;
            int steps = 160;
            for (int i = 0; i <= steps; i++)
            {
                double nu = nu0 + (Math.PI - nu0) * i / steps;
                double r = p / (1 + result.e * Math.Cos(nu));
                double ang = result.omega + nu * result.dir;
                transferPts.Add(G(r * Math.Cos(ang), r * Math.Sin(ang)));
            }
            double apoAng = result.omega + Math.PI * result.dir;
            moon.position = G(AscendMissionSim.MoonDist * Math.Cos(apoAng), AscendMissionSim.MoonDist * Math.Sin(apoAng));
        }
        play = 0f;
        emptyMsg.text = "";
        SetView(viewMode);
    }

    Vector3 LocalPoint(double x, double y)
    {
        double r = Math.Sqrt(x * x + y * y);
        double down = AscendMissionSim.EarthR * Math.Atan2(x, y), alt = r - AscendMissionSim.EarthR;
        return L(down, alt);
    }

    void Conic(LineRenderer lr, double from, double to, int steps, bool closed)
    {
        double p = result.a * (1 - result.e * result.e);
        lr.positionCount = steps + 1;
        for (int i = 0; i <= steps; i++)
        {
            double nu = from + (to - from) * i / steps;
            double r = p / (1 + result.e * Math.Cos(nu));
            double ang = result.omega + nu;
            lr.SetPosition(i, G(r * Math.Cos(ang), r * Math.Sin(ang)));
        }
        lr.loop = closed;
    }

    void AnimatePlayback()
    {
        if (result == null || play < 0f) return;
        play += Time.deltaTime;
        float total = PlaySeconds + (transferPts.Count > 0 ? 4f : 0f) + 2f;
        float k = Mathf.Clamp01(play / PlaySeconds);
        if (pathPts.Count == 0) { play = -2f; return; }
        int count = Mathf.Clamp(Mathf.RoundToInt(k * pathPts.Count), 1, pathPts.Count);
        ascent.positionCount = count;
        for (int i = 0; i < count; i++) ascent.SetPosition(i, pathPts[i]);
        marker.gameObject.SetActive(pathPts.Count > 0);
        if (pathPts.Count > 0) marker.position = pathPts[count - 1];

        float tNow = count > 0 && count - 1 < result.ts.Count ? result.ts[count - 1] : 0f;
        for (int i = 0; i < eventDots.Count && i < result.events.Count; i++) if (eventDots[i]) eventDots[i].SetActive(result.events[i].t <= tNow + 0.5f);

        if (k >= 1f && transferPts.Count > 0)
        {
            float k2 = Mathf.Clamp01((play - PlaySeconds) / 4f);
            int c2 = Mathf.Max(1, Mathf.RoundToInt(k2 * transferPts.Count));
            transfer.positionCount = c2;
            for (int i = 0; i < c2; i++) transfer.SetPosition(i, transferPts[i]);
            marker.position = transferPts[c2 - 1];
            if (k2 >= 1f && result.go)
            {
                float rr = (float)((AscendMissionSim.MoonR + 50000) * S) * 1.0f;
                moonOrbit.positionCount = 73; moonOrbit.loop = true;
                for (int i = 0; i <= 72; i++) { float a = i / 72f * Mathf.PI * 2f; moonOrbit.SetPosition(i, moon.position + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * rr); }
            }
        }

        // CanSat parachute: drifts down from the top
        if (k >= 1f && mission == AscendSimMission.SkySensor && result.go && chute.positionCount == 0)
        {
            Vector3 top = LocalPoint(result.releaseX, result.releaseY);
            chute.positionCount = 20;
            for (int i = 0; i < 20; i++)
            {
                float f = i / 19f;
                chute.SetPosition(i, new Vector3(top.x + f * (top.y - local.transform.position.y) * 0.6f, Mathf.Lerp(top.y, local.transform.position.y, f), top.z));
            }
        }
        if (play > total) play = -2f;   // finished (keeps the drawing)
    }

    // ───── Camera ─────
    void SetView(int v)
    {
        viewMode = v;
        bool closeUp = mission == AscendSimMission.SkySensor && v == 1;
        local.SetActive(mission == AscendSimMission.SkySensor);
        moon.gameObject.SetActive(mission == AscendSimMission.MoonMapper);
        if (closeUp)
        {
            float top = result != null ? Mathf.Max(30f, result.apogeeKm * 100f) : 100f;
            camTarget = local.transform.position + Vector3.up * top * 0.5f;
            camDist = top * 2.4f; camPitch = 10f; camYaw = 0f;
        }
        else if (v == 1 && mission == AscendSimMission.MoonMapper)
        {
            camTarget = moon.position; camDist = 90f; camPitch = 20f;
        }
        else if (v == 1)
        {
            camTarget = G(0, AscendMissionSim.EarthR); camDist = 25f; camPitch = 15f;  // launch site on the globe
        }
        else
        {
            camTarget = earth.position;
            camDist = mission == AscendSimMission.MoonMapper && result != null && result.tliDone ? 4200f : 190f;
            camPitch = 20f;
        }
        for (int i = 0; i < 2; i++) if (viewBtnLabels[i]) viewBtnLabels[i].transform.parent.GetComponent<Image>().color = i == v ? Accent : Panel2;
    }

    float MinDist() => mission == AscendSimMission.SkySensor && viewMode == 1 ? 5f : viewMode == 1 ? 3f : 60f;
    float MaxDist() => mission == AscendSimMission.SkySensor && viewMode == 1 ? 6000f : 9000f;

    void UpdateCamera()
    {
        if (!cam) return;
        if (viewMode == 1 && mission == AscendSimMission.MoonMapper) camTarget = moon.position;
        cam.transform.position = camTarget + Quaternion.Euler(camPitch, camYaw, 0f) * new Vector3(0f, 0f, -camDist);
        cam.transform.LookAt(camTarget);
        if (glow) glow.rotation = cam.transform.rotation;
        if (clouds) clouds.Rotate(0f, Time.unscaledDeltaTime * 1.5f, 0f, Space.Self);
        float w = camDist * 0.004f;
        foreach (var lr in new[] { ascent, orbitLine, transfer, moonOrbit, chute }) if (lr) lr.widthMultiplier = w;
        foreach (var lr in compareLines) if (lr) lr.widthMultiplier = w * 0.7f;
        if (marker) marker.localScale = Vector3.one * camDist * 0.02f;
        foreach (var d in eventDots) if (d) d.transform.localScale = Vector3.one * camDist * 0.015f;
    }

    void ResizeTexture()
    {
        if (!viewRect || !cam) return;
        var size = viewRect.rect.size;
        int w = Mathf.Clamp(Mathf.RoundToInt(size.x * (data.lowSpecMode ? 0.75f : 1f)), 64, 2048);
        int h = Mathf.Clamp(Mathf.RoundToInt(size.y * (data.lowSpecMode ? 0.75f : 1f)), 64, 2048);
        if (rt && rt.width == w && rt.height == h) return;
        if (rt) { cam.targetTexture = null; rt.Release(); Destroy(rt); }
        rt = new RenderTexture(w, h, 24) { name = "Mission Simulator View", antiAliasing = data.lowSpecMode ? 1 : 2 };
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        view.texture = rt;
    }
}
