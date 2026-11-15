// ═════════════════════════════════════════════════════════════════════════════
//  AscendPartGuide.cs  -  the detailed "about this part" text in AscendCAD's
//  right panel (under the measurements):
//     WHAT IT DOES       plain words first, then the engineering view
//     COMPATIBLE WITH    real parts from the catalogue that fit / work with it
//     NEEDS / WORKS WITH which other systems it depends on
//     BY THE NUMBERS     figures worked out from its data (TWR, mass ratio...)
//     TIPS               common mistakes and good practice
//  Everything is generated from the part data, so new parts get it for free.
// ═════════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public static class AscendPartGuide
{
    const string Head = "#8AB4F8", Sub = "#9AA0A6", Good = "#3FB950", Warn = "#D29922";
    const float G0 = 9.80665f;

    static string C(string hex, string s) => $"<color={hex}>{s}</color>";
    static void H(StringBuilder sb, string title) => sb.Append("\n<b>").Append(C(Head, title)).Append("</b>\n");
    static void Bullet(StringBuilder sb, string s) => sb.Append(C(Sub, "•  ")).Append(s).Append('\n');

    public static string Build(CADPart p, AscendCADData data)
    {
        if (p == null) return "";
        var sb = new StringBuilder();
        var all = data != null ? data.parts.Where(x => x != null && x != p).ToList() : new List<CADPart>();

        // ── WHAT IT DOES ──
        H(sb, "WHAT IT DOES");
        var (kid, expert) = Explain(p);
        sb.Append(kid).Append('\n');
        if (!string.IsNullOrEmpty(expert)) sb.Append("<size=90%>").Append(C(Sub, expert)).Append("</size>\n");

        // ── HOW IT ATTACHES ──
        H(sb, "HOW IT ATTACHES");
        sb.Append(Attach(p)).Append('\n');

        // ── COMPATIBLE WITH ──
        var compat = Compatible(p, all);
        if (compat.Count > 0)
        {
            H(sb, "COMPATIBLE WITH");
            foreach (var (label, list) in compat)
            {
                if (list.Count == 0) continue;
                sb.Append(C(Sub, label + ":")).Append('\n');
                foreach (var n in list.Take(6)) Bullet(sb, "<noparse>" + n + "</noparse>");
                if (list.Count > 6) sb.Append(C(Sub, $"   + {list.Count - 6} more")).Append('\n');
            }
        }

        // ── NEEDS / WORKS WITH ──
        var needs = Needs(p);
        if (needs.Count > 0)
        {
            H(sb, "NEEDS / WORKS WITH");
            foreach (var n in needs) Bullet(sb, n);
        }

        // ── BY THE NUMBERS ──
        var nums = Numbers(p, all);
        if (nums.Count > 0)
        {
            H(sb, "BY THE NUMBERS");
            foreach (var n in nums) Bullet(sb, n);
        }

        // ── TIPS ──
        var tips = Tips(p);
        if (tips.Count > 0)
        {
            H(sb, "TIPS");
            foreach (var t in tips) Bullet(sb, C(Warn, "!") + "  " + t);
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    static (string kid, string expert) Explain(CADPart p)
    {
        switch (p.role)
        {
            case CADRole.Tank: return ("Holds the rocket's fuel and oxidizer (the stuff the fuel needs to burn in space, where there is no air).",
                $"Propellant tank for {AscendReport.PropName(p.propellant)}. Its full-to-empty mass ratio sets how much speed (delta-v) a stage can gain.");
            case CADRole.Engine: return ("Burns propellant and pushes hot gas out of the nozzle very fast. Pushing gas down pushes the rocket up.",
                p.ispSL > 0f ? "Liquid rocket engine. Sea-level engines have short nozzles for thick air; their thrust and efficiency (Isp) rise as the air thins."
                             : "Vacuum engine: a huge nozzle made for space. In thick air near the ground it loses most of its thrust, so use it on upper stages.");
            case CADRole.SolidMotor: return ("A solid rocket: the propellant is a rubbery solid that burns from the inside out. Once lit, it can't be turned off.",
                "Solid motor / booster. Very high thrust for its size and simple, but no throttle or restart. Usually strapped on for extra push at liftoff.");
            case CADRole.Decoupler: return ("Holds two stages together, then lets go when the lower stage is empty so the rocket drops dead weight.",
                "Interstage / decoupler. Marks a stage boundary: everything below separates at staging. Upper-stage engines often hang inside it.");
            case CADRole.Fairing: return ("The nose cover that protects your satellite from the air and heat during the climb. It opens and falls away in space.",
                "Payload fairing. Sets the usable envelope (diameter x height) for the payload. Jettisoned above the dense atmosphere to save mass.");
            case CADRole.PayloadAdapter: return ("Connects the rocket to the satellite or to a wider nose cover.",
                "Payload adapter / diameter transition. Carries the payload loads and lets a narrow rocket wear a wider fairing.");
            case CADRole.NoseCone: return ("A pointy tip that lets the rocket cut through the air instead of pushing it like a wall.",
                "Aerodynamic nose. Lowers drag and the load on the structure at Max Q. A flat-topped rocket suffers far more drag.");
            case CADRole.Recovery: return ("Helps bring parts back safely (legs, grid fins, parachutes) so they can fly again.",
                "Recovery hardware. Adds dry mass now to save cost on later flights; it does not help reach orbit.");
            case CADRole.Aero: return ("Fins keep the rocket pointing straight, like the feathers on an arrow.",
                "Passive aerodynamic stabilizer: moves the centre of pressure behind the centre of mass. Enough for small sounding rockets without guidance.");
            case CADRole.FlightComputer: return ("The rocket's brain. It steers, times the stages and makes all the decisions in flight.",
                "Flight computer running guidance, navigation and control (GNC). Needs an IMU to know its attitude; without both, the rocket can't steer.");
            case CADRole.IMU: return ("Feels which way the rocket is pointing and how it is moving, like the balance sense in your ear.",
                "Inertial measurement unit (gyroscopes + accelerometers). Feeds attitude and acceleration to the flight computer.");
            case CADRole.GNSS: return ("Finds the rocket's exact position using navigation satellites (like the GPS in a phone).",
                "GNSS receiver. Corrects IMU drift and gives range safety an independent position.");
            case CADRole.FlightTermination: return ("A safety system: if the rocket goes the wrong way, it is destroyed safely before it can hurt anyone.",
                "Autonomous flight termination system (AFTS). Required by range safety for every launch.");
            case CADRole.Telemetry: return ("Radios the rocket's health and position down to Mission Control during flight.",
                "Telemetry transmitter. Mission Control needs it to follow the flight and confirm events like staging.");
            case CADRole.CommandReceiver: return ("Lets Mission Control send commands up to the rocket.",
                "Command receiver (uplink). Used for holds, safing and, with the FTS, range safety commands.");
            case CADRole.Battery: return ("Stores electricity for the rocket's computers and radios during the flight.",
                "Avionics battery. Sized for the powered flight plus margin; the vehicle is on internal power after liftoff.");
            case CADRole.PowerDistribution: return ("Shares electricity from the battery to every system, with fuses to keep faults from spreading.",
                "Power distribution unit (PDU): switching, protection and regulation for the avionics bus.");
            case CADRole.Wiring: return ("The cables that connect every electronic part together.",
                "Harness. Often 5-10% of avionics mass on real vehicles.");
            case CADRole.SatStructure: return ("The satellite's body: a frame that holds every part together and survives the shaking of launch.",
                "Bus / primary structure. Sets the internal volume available for components and the mounting faces for panels and instruments.");
            case CADRole.Separation: return ("Lets go of the satellite in orbit and pushes it gently away from the rocket.",
                "Separation system (springs, clamp band or deployer). The mechanical and electrical interface to the launcher.");
            case CADRole.SolarPanel: return ("Turns sunlight into electricity. In orbit there are no plugs, so this is how the satellite gets power.",
                "Photovoltaic array. Output depends on sun angle and is zero in Earth's shadow (about a third of each low orbit).");
            case CADRole.SatBattery: return ("Stores solar power so the satellite keeps working while it flies through Earth's shadow.",
                "Spacecraft battery, charged in sunlight and drained in eclipse. Usually sized so each eclipse uses only a small fraction of it.");
            case CADRole.EPS: return ("Controls the satellite's power: charges the battery and feeds every part safely.",
                "Electrical power system: array regulation, battery charge control and protected power rails.");
            case CADRole.OBC: return ("The satellite's computer. It runs the mission, stores photos and talks to every other part.",
                "On-board computer / command and data handling (C&DH).");
            case CADRole.Transceiver: return ("The satellite's radio: it sends photos and data home and receives commands.",
                "Transceiver. Its data rate sets how much you can download each time you pass over a ground station.");
            case CADRole.Antenna: return ("Points the radio's signal toward Earth so it can be heard far away.",
                "Antenna. Must match the transceiver's frequency band.");
            case CADRole.ADCSSensor: return ("Senses which way the satellite is facing (using the Sun, stars or Earth's magnetic field).",
                "ADCS attitude sensor. Combined with actuators to point the satellite.");
            case CADRole.ADCSActuator: return ("Turns the satellite to face the right way, without using fuel.",
                "ADCS actuator (reaction wheel or magnetorquer). Needs attitude sensors to know where to turn.");
            case CADRole.ADCSIntegrated: return ("A complete pointing system in one box: it senses and turns the satellite.",
                "Integrated ADCS: sensors, actuators and control in one unit.");
            case CADRole.SatPropulsion: return ("Small thrusters so the satellite can change its orbit or avoid space junk.",
                "Spacecraft propulsion for orbit raising, station keeping and end-of-life deorbit.");
            case CADRole.Thermal: return ("Keeps the satellite's parts at a safe temperature: in orbit it is burning hot in sunlight and freezing in shadow.",
                "Thermal control (insulation blankets, heaters, radiators).");
            case CADRole.Instrument: return ("The reason the satellite exists: a camera or sensor that does the science or takes the photos.",
                "Payload instrument. Drives the power, data, pointing and mass needs of the whole satellite.");
            default: return ("A structural part that holds things together and carries the loads.", "Primary structure.");
        }
    }

    static string Attach(CADPart p)
    {
        switch (p.attach)
        {
            case CADAttach.Internal: return "Goes <b>inside</b> a bay or satellite body. It takes " + (p.volumeL > 0f ? $"<b>{p.volumeL:0.##} L</b> of space" : "a little space") + " inside its host.";
            case CADAttach.Radial: return "Mounts on the <b>side</b> of another part. Use symmetry to place 2, 3 or 4 evenly around it.";
            case CADAttach.RadialUpright: return "Straps onto the <b>side</b> of a stage as a booster. It drops away when empty.";
            default:
                if (p.role == CADRole.Engine) return "Mounts <b>under</b> a tank. With symmetry above 1 you get an engine cluster.";
                return p.TopDia != p.diameter
                    ? $"Stacks <b>on top / below</b> other parts. Bottom Ø{p.diameter:0.##} m, top Ø{p.TopDia:0.##} m, so it changes the rocket's width."
                    : $"Stacks <b>on top / below</b> other parts with a Ø{p.diameter:0.##} m joint.";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.06f;
    static IEnumerable<string> Names(IEnumerable<CADPart> ps) => ps.OrderBy(x => x.costUSD).Select(x => x.name).Distinct();

    static List<(string, List<string>)> Compatible(CADPart p, List<CADPart> all)
    {
        var res = new List<(string, List<string>)>();
        var same = all.Where(x => x.builder == p.builder).ToList();
        switch (p.role)
        {
            case CADRole.Engine:
            case CADRole.SolidMotor:
                if (p.propellant != CADPropellant.Solid && p.propellant != CADPropellant.None)
                {
                    var tanks = same.Where(x => x.role == CADRole.Tank && x.propellant == p.propellant).ToList();
                    res.Add(($"Tanks with {AscendReport.PropName(p.propellant)} (same propellant)", Names(tanks).ToList()));
                }
                res.Add(("Other engines that burn the same propellant", Names(same.Where(x => (x.role == CADRole.Engine || x.role == CADRole.SolidMotor) && x.propellant == p.propellant)).ToList()));
                break;
            case CADRole.Tank:
                res.Add(($"Engines that burn {AscendReport.PropName(p.propellant)}", Names(same.Where(x => x.role == CADRole.Engine && x.propellant == p.propellant)).ToList()));
                res.Add(($"Parts that stack on top (Ø{p.TopDia:0.##} m)", Names(same.Where(x => x.attach == CADAttach.Stack && x.role != CADRole.Engine && Near(x.diameter, p.TopDia))).ToList()));
                break;
            case CADRole.SatStructure:
                res.Add(("Fits inside (by volume)", Names(same.Where(x => x.attach == CADAttach.Internal && x.volumeL > 0f && x.volumeL <= p.internalVolumeL)).ToList()));
                res.Add(("Separation systems", Names(same.Where(x => x.role == CADRole.Separation)).ToList()));
                break;
            default:
                if (p.attach == CADAttach.Internal)
                {
                    var hosts = all.Where(x => x.builder == p.builder && x.internalVolumeL > 0f && x.internalVolumeL >= p.volumeL);
                    res.Add(("Fits inside these hosts", Names(hosts).ToList()));
                    res.Add(("Alternatives (same job)", Names(same.Where(x => x.role == p.role)).ToList()));
                }
                else if (p.internalVolumeL > 0f)
                    res.Add(($"Can hold inside ({p.internalVolumeL:0.##} L)", Names(same.Where(x => x.attach == CADAttach.Internal && x.volumeL > 0f && x.volumeL <= p.internalVolumeL)).ToList()));
                if (p.attach == CADAttach.Stack && p.role != CADRole.Engine)
                {
                    res.Add(($"Fits below it (top Ø{p.diameter:0.##} m)", Names(same.Where(x => x.attach == CADAttach.Stack && x.role != CADRole.Fairing && x.role != CADRole.NoseCone && Near(x.TopDia, p.diameter))).ToList()));
                    if (p.role != CADRole.Fairing && p.role != CADRole.NoseCone)
                        res.Add(($"Fits on top (Ø{p.TopDia:0.##} m)", Names(same.Where(x => x.attach == CADAttach.Stack && x.role != CADRole.Engine && Near(x.diameter, p.TopDia))).ToList()));
                }
                if (p.role == CADRole.Fairing && p.innerDiameter > 0f)
                    res.Add(("Satellites that fit inside (folded)", Names(all.Where(x => x.role == CADRole.SatStructure && Mathf.Max(x.boxSize.x, x.boxSize.z, x.diameter) <= p.innerDiameter && x.StackHeight <= p.innerHeight)).ToList()));
                if (p.attach == CADAttach.Radial || p.attach == CADAttach.RadialUpright)
                    res.Add(("Alternatives (same job)", Names(same.Where(x => x.role == p.role)).ToList()));
                break;
        }
        return res.Where(r => r.Item2.Count > 0).ToList();
    }

    static List<string> Needs(CADPart p)
    {
        var n = new List<string>();
        switch (p.role)
        {
            case CADRole.Engine:
                n.Add($"A tank above it with <b>{AscendReport.PropName(p.propellant)}</b>. A different propellant won't feed it.");
                n.Add("A flight computer + IMU in an Avionics Bay, so the rocket can steer.");
                break;
            case CADRole.SolidMotor: n.Add("Nothing to feed: the propellant is inside. Pair with a guided core stage for orbit."); break;
            case CADRole.Tank: n.Add($"An engine underneath that burns <b>{AscendReport.PropName(p.propellant)}</b>."); break;
            case CADRole.Decoupler: n.Add("An engine on the stage above, or the upper stage can't continue after separation."); break;
            case CADRole.Fairing: n.Add("A payload adapter below it if the rocket is narrower than the fairing."); n.Add("A satellite that fits the usable envelope when folded."); break;
            case CADRole.FlightComputer: n.Add("An <b>IMU</b> (without one, the computer is blind)."); n.Add("A battery and power distribution in the same bay."); break;
            case CADRole.IMU: n.Add("A <b>flight computer</b> to use its data."); break;
            case CADRole.FlightTermination: n.Add("Required by range safety: Mission Control rejects rockets without one."); break;
            case CADRole.Telemetry: case CADRole.CommandReceiver: n.Add("Power from the avionics battery."); break;
            case CADRole.SolarPanel: n.Add("An <b>EPS</b> to regulate the power and a <b>battery</b> for the shadow side of each orbit."); break;
            case CADRole.SatBattery: n.Add("Solar panels to recharge it, and an EPS to manage charging."); break;
            case CADRole.EPS: n.Add("Solar panels (input) and a battery (storage)."); break;
            case CADRole.OBC: n.Add("Power from the EPS, and a radio to send its data home."); break;
            case CADRole.Transceiver: n.Add("An <b>antenna</b> on the outside of the satellite."); n.Add("Power: radios use a lot while transmitting."); break;
            case CADRole.Antenna: n.Add("A <b>transceiver</b> inside to drive it."); break;
            case CADRole.ADCSSensor: n.Add("ADCS actuators (reaction wheels / magnetorquers) to act on what it senses."); break;
            case CADRole.ADCSActuator: n.Add("ADCS sensors so it knows which way to turn."); break;
            case CADRole.Instrument:
                n.Add("Power (EPS + solar + battery), a computer to store data, and a radio to send it home.");
                n.Add("Pointing (ADCS) so it looks at the right place on Earth.");
                break;
            case CADRole.SatStructure: n.Add("A separation system under it, and room inside for power, computer, radio and ADCS."); break;
            case CADRole.Separation: n.Add("A satellite structure on top of it."); break;
        }
        if (p.powerUseW > 0f) n.Add($"Uses <b>{p.powerUseW:0.##} W</b>{(p.dutyCycle < 1f ? $" for {p.dutyCycle * 100f:0}% of the time" : "")}: count it in your power budget.");
        return n;
    }

    static List<string> Numbers(CADPart p, List<CADPart> all)
    {
        var n = new List<string>();
        if (p.thrustVackN > 0f || p.thrustSLkN > 0f)
        {
            float f = Mathf.Max(p.thrustSLkN, 0f) > 0f ? p.thrustSLkN : p.thrustVackN;
            float twr = f * 1000f / (Mathf.Max(1f, p.WetMassKg) * G0);
            n.Add($"Thrust-to-weight on its own: <b>{twr:0.#}</b>  {C(Sub, "(can lift its own weight that many times)")}");
            n.Add($"Can hold up about <b>{AscendReport.Mass(f * 1000f / G0 / 1.3f)}</b> at liftoff  {C(Sub, "(for a healthy thrust-to-weight of 1.3)")}");
        }
        if (p.ispVac > 0f)
            n.Add($"Exhaust speed in vacuum: <b>{p.ispVac * G0 / 1000f:0.00} km/s</b>  {C(Sub, "(Isp x g0; higher = more delta-v per kg of fuel)")}");
        if (p.ispSL > 0f && p.ispVac > 0f) n.Add($"Gains <b>{(p.ispVac / p.ispSL - 1f) * 100f:0}%</b> efficiency from sea level to vacuum.");
        if (p.role == CADRole.Engine && p.thrustVackN > 0f && p.ispVac > 0f)
            n.Add($"Burns <b>{p.thrustVackN * 1000f / (p.ispVac * G0):0.#} kg</b> of propellant every second at full thrust.");
        if (p.role == CADRole.Tank && p.propellantKg > 0f)
        {
            float ratio = p.WetMassKg / Mathf.Max(1f, p.dryMassKg);
            n.Add($"Full-to-empty mass ratio: <b>{ratio:0.0} : 1</b>  {C(Sub, $"(the tank is {p.dryMassKg / p.WetMassKg * 100f:0.#}% of its full mass)")}");
            var eng = all.Where(x => x.role == CADRole.Engine && x.propellant == p.propellant && x.ispVac > 0f).OrderByDescending(x => x.ispVac).FirstOrDefault();
            if (eng != null)
            {
                float dv = eng.ispVac * G0 * Mathf.Log((p.WetMassKg + eng.dryMassKg) / (p.dryMassKg + eng.dryMassKg));
                n.Add($"With one {eng.name} and nothing else: <b>{dv:N0} m/s</b> of delta-v  {C(Sub, "(orbit needs about 9,400 m/s in total)")}");
            }
        }
        if (p.powerGenW > 0f) n.Add($"About <b>{p.powerGenW * 0.62f:0.#} W</b> on average over an orbit  {C(Sub, "(no power for ~35% of each orbit in Earth's shadow)")}");
        if (p.batteryWh > 0f) n.Add($"Runs a 10 W satellite for <b>{p.batteryWh / 10f:0.#} h</b>; one 35-min eclipse at 10 W uses <b>{5.8f / p.batteryWh * 100f:0.#}%</b> of it.");
        if (p.dataRateKbps > 0f) n.Add($"A 10-minute pass downloads about <b>{p.dataRateKbps * 600f / 8000f:0.#} MB</b>.");
        if (p.maxPayloadKg > 0f) n.Add($"Carries up to <b>{AscendReport.Mass(p.maxPayloadKg)}</b>.");
        if (p.costUSD > 0 && p.WetMassKg > 0f) n.Add($"Cost per kilogram: <b>{AscendReport.Money(p.costUSD / p.WetMassKg)}</b>.");
        return n;
    }

    static List<string> Tips(CADPart p)
    {
        var t = new List<string>();
        switch (p.role)
        {
            case CADRole.Engine:
                if (p.ispSL <= 0f) t.Add("Vacuum-only engine: on a first stage it barely works. Put it on the upper stage.");
                else t.Add("Total thrust at liftoff should be 1.2 to 1.5 times the rocket's weight.");
                break;
            case CADRole.SolidMotor: t.Add("Can't be throttled or shut off: watch the g-force and Max Q."); break;
            case CADRole.Tank: t.Add("Every tank in a stage must hold the same propellant as that stage's engines."); break;
            case CADRole.Fairing: t.Add("Measure your satellite folded: it must fit the usable envelope, not the outer size."); break;
            case CADRole.FlightComputer: case CADRole.IMU: t.Add("No flight computer + IMU = no steering: the rocket tumbles after liftoff."); break;
            case CADRole.SolarPanel: t.Add("Make more power than you use. Mission Control wants a margin above your total draw."); break;
            case CADRole.Instrument: t.Add("Higher-resolution cameras make more data: check your radio can send it home."); break;
            case CADRole.Recovery: t.Add("Recovery parts add mass that doesn't help reach orbit: leave them off if you're short on delta-v."); break;
            case CADRole.Decoupler: t.Add("More stages = more delta-v, but each stage needs its own engine."); break;
        }
        if (p.costEstimated) t.Add("Cost is an engineering estimate, not a published price.");
        return t;
    }
}
