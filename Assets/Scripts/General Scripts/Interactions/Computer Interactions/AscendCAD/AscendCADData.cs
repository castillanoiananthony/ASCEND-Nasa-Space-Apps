// ═════════════════════════════════════════════════════════════════════════════
//  AscendCADData.cs  -  "AscendCAD Models and Data"
//  Everything AscendCAD needs lives in this file:
//    - Part catalog (rocket + satellite parts with real-world mass / cost / performance)
//    - Economy (government fund, receipts, owned-parts inventory)
//    - Design data, physics stats (delta-v, TWR, power budget) and the realism review
//    - PhMail (in-game email; the review board answers through it)
//    - Procedural 3D models (or your own prefabs) + spawning the approved rocket
//    - Real-size launch pads built from code
//    - The AscendCAD and PhMail app screens shown inside ComputerInteraction
//
//  Data asset: Project window > right-click > Create > AscendCAD > AscendCAD Data
//  (comes pre-filled; drag your own 3D models onto any part's "Model Prefab").
//  Then drag the asset into ComputerInteraction > Ascend CAD Data.
//  1 Unity unit = 1 meter everywhere.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Object = UnityEngine.Object;

public enum CADBuilder { Rocket, Payload }
public enum CADShape { Cylinder, Frustum, NoseCone, Fairing, Engine, Booster, SolidStage, Sphere, Box, Panel, Wing, Dish, Fin, GridFin, Leg, Rod, Camera, OgiveTank, Orbiter, Capsule, Tower, None }
public enum CADAttach { Stack, Radial, RadialUpright, Internal }
public enum CADLink { Root, Top, Bottom, Radial, RadialUpright, Cluster, Internal }
public enum CADPropellant { None, Kerolox, Hydrolox, Methalox, Hypergolic, Solid, ColdGas, Electric, Monoprop }
public enum CADRole
{
    Structure, Tank, Engine, SolidMotor, Decoupler, Fairing, PayloadAdapter, NoseCone, Recovery, Aero,
    FlightComputer, IMU, GNSS, FlightTermination, Telemetry, CommandReceiver, Battery, PowerDistribution, Wiring,
    SatStructure, Separation, SolarPanel, SatBattery, EPS, OBC, Transceiver, Antenna,
    ADCSSensor, ADCSActuator, ADCSIntegrated, SatPropulsion, Thermal, Instrument
}

// ───────────────────────────── One catalog part ─────────────────────────────
[Serializable]
public class CADPart
{
    public string id = "part.new";
    public string name = "New Part";
    public CADBuilder builder = CADBuilder.Rocket;
    public string category = "Misc";
    public string subcategory = "General";
    public CADRole role = CADRole.Structure;
    public CADShape shape = CADShape.Cylinder;
    public CADAttach attach = CADAttach.Stack;
    [TextArea(2, 6)] public string description;
    [Tooltip("Where the numbers come from. Anything marked 'est.' is an engineering estimate.")]
    public string source;

    [Header("3D model (optional). 1 unit = 1 m, pivot at center, +Y up (radial parts: +Z points outward)")]
    public GameObject modelPrefab;
    public bool tintPrefab;
    public Color defaultColor = new Color(0.93f, 0.93f, 0.95f);

    [Header("Size (meters)")]
    public float diameter = 1f;        // bottom diameter / nozzle exit diameter
    public float topDiameter = -1f;    // frustum top diameter (-1 = same as bottom)
    public float length = 1f;          // height along Y
    public Vector3 boxSize = Vector3.one; // boxes + radial parts: x width, y height, z depth (outward)
    [Tooltip("Size while folded for launch (solar wings, sunshields, big mirrors). Zero = same as deployed. Used for the fairing fit check.")]
    public Vector3 stowedSize;

    [Header("Mass & cost")]
    public float dryMassKg;
    public float propellantKg;
    public double costUSD;
    public bool costEstimated = true;

    [Header("Propulsion")]
    public CADPropellant propellant;
    public float thrustSLkN, thrustVackN, ispSL, ispVac, burnTimeS;

    [Header("Power, data, volume")]
    public float powerGenW;
    public float powerUseW;
    [Range(0f, 1f)] public float dutyCycle = 1f;
    public float batteryWh;
    public float dataRateKbps;
    [Tooltip("Liters this part takes up when installed inside something.")] public float volumeL;
    [Tooltip("Liters available inside this part for internal components.")] public float internalVolumeL;

    [Tooltip("Side-mounted part whose engines burn the core stage's propellant and stay with it (Space Shuttle orbiter).")]
    public bool crossfeed;

    [Header("Payload interface")]
    public float maxPayloadKg;
    public float innerDiameter, innerHeight;

    public float WetMassKg => dryMassKg + propellantKg;
    public float TopDia => topDiameter > 0f ? topDiameter : diameter;
    public bool IsBoxy => shape == CADShape.Box;
    public float StackHeight => IsBoxy ? boxSize.y : length;
    public float MaxRadius => IsBoxy ? Mathf.Max(boxSize.x, boxSize.z) * 0.5f : Mathf.Max(diameter, TopDia) * 0.5f;
    public bool IsBody => attach == CADAttach.Stack || attach == CADAttach.RadialUpright;
}

// ───────────────────────────── Launch pads (bought once, kept forever) ─────────────────────────────
[Serializable]
public class CADLaunchPad
{
    public string id = "pad.new";
    public string name = "New Launch Pad";
    [TextArea(2, 5)] public string description;
    public string source;
    [Tooltip("Use AscendCAD's own real-size pad built from code. Turn off to use Model Prefab / Scene Object Name instead.")]
    public bool useBuiltInModel = true;
    [Tooltip("Your 3D model (e.g. NASA's Mobile Launcher). Spawned at the Launch Site Position when this pad is used. If there's no Image, the card shows a one-time snapshot of it.")]
    public GameObject modelPrefab;
    [Tooltip("Picture shown on the pad card. Used first; the model is only snapshotted when this is empty.")]
    public Sprite image;
    [Tooltip("OR the exact name of a pad object already in your scene. The rocket is placed on it and no copy is spawned.")]
    public string sceneObjectName;
    public double costUSD;
    public bool costEstimated = true;
    [Tooltip("Already owned at the start of the game.")]
    public bool ownedFromStart;
    [Tooltip("Support vehicle (e.g. Crawler-Transporter), not a launch pad. It can't be chosen, only bought.")]
    public bool isTransporter;

    [Header("Limits (0 = no limit)")]
    public float maxHeightM;
    public float maxWidthM;
    public float maxLiftoffMassT;
    [Tooltip("Id of something you must own before this pad can be used (the Mobile Launcher needs the Crawler).")]
    public string requiresId;
    [Tooltip("Pads moved by a transporter: their own mass in tonnes.")]
    public float ownMassT;
    [Tooltip("Transporters: the most they can carry (rocket + launcher) in tonnes.")]
    public float carryCapacityT;

    [Header("Placement (meters, 1 unit = 1 m). Ignored by the built-in model.")]
    [Tooltip("Where the rocket's base sits relative to the pad (deck height, offset over the flame hole).")]
    public Vector3 rocketOffset;
}

// ───────────────────────────── The data asset ─────────────────────────────
[CreateAssetMenu(menuName = "AscendCAD/AscendCAD Data", fileName = "AscendCADData")]
public class AscendCADData : ScriptableObject
{
    [Header("Economy (USD)")]
    [Tooltip("Starting government fund. The Philippine Space Agency's 2026 appropriation is about P1.14 billion (~US$20M); default is ~1.5x that.")]
    public double governmentFundUSD = 30000000d;
    [Range(0f, 0.5f)] public float assemblyIntegrationRate = 0.10f;
    [Range(0f, 0.5f)] public float testingQARate = 0.05f;
    [Tooltip("Keep money and inventory between play sessions (PlayerPrefs).")]
    public bool saveProgress = false;

    [Header("Email & review")]
    public string agencyEmail = "ascend.philippines@phmail.com";
    public string playerEmail = "design.team@phmail.com";
    public float reviewDelaySeconds = 6f;

    [Header("Realism rules")]
    [Tooltip("Delta-v needed to reach low Earth orbit, including gravity and drag losses.")]
    public float requiredDeltaV = 9400f;
    public float minLiftoffTWR = 1.2f;
    public float maxLiftoffTWR = 6f;
    [Range(0.3f, 1f)] public float sunlitFraction = 0.62f;
    public float powerMargin = 1.2f;
    public float maxDiameterStepRatio = 1.25f;

    [Header("Launch site")]
    [Tooltip("Where the launch pad's ground (and the rocket) is placed. Put it at ground level. 1 Unity unit = 1 meter everywhere.")]
    public Vector3 launchSitePosition = new Vector3(0f, 1f, 0f);
    public bool replacePreviousRocket = true;

    [Header("Interface")]
    [Tooltip("Makes every AscendCAD / PhMail text and button bigger.")]
    [Range(0.8f, 2f)] public float uiFontScale = 1.3f;
    [Tooltip("Unlimited money for testing (lets you afford Saturn V, the Shuttle, etc.).")]
    public bool sandboxMode = false;

    [Header("Performance (weak PCs: keep Low Spec Mode ON)")]
    [Tooltip("Fewer polygons, cheaper shader, lower-res CAD view, no anti-aliasing.")]
    public bool lowSpecMode = true;
    [Range(0.4f, 1f)] public float viewportResolution = 0.75f;

    [Header("Rendering")]
    [Tooltip("Optional lit material for parts. Empty = your render pipeline's default Lit material.")]
    public Material partMaterial;
    [Tooltip("An EMPTY layer used only by the CAD cameras.")]
    [Range(0, 31)] public int cadLayer = 31;
    public TMP_FontAsset labelFont;

    [Header("Launch pads (bought once, kept forever)")]
    public List<CADLaunchPad> launchPads = new List<CADLaunchPad>();

    [Header("Parts (rocket + payload)")]
    public List<CADPart> parts = new List<CADPart>();

    [HideInInspector] public int catalogVersion;
    public const int CatalogVersion = 5;

    [NonSerialized] Dictionary<string, CADPart> lookup;
    static AscendCADData fallback;
    public static AscendCADData Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = null; fallback = null; }

    void Reset() { parts = AscendCADDefaults.Build(); launchPads = AscendCADDefaults.BuildPads(); catalogVersion = CatalogVersion; }

    public CADLaunchPad GetPad(string id)
    {
        if (string.IsNullOrEmpty(id) || launchPads == null) return null;
        foreach (var p in launchPads) if (p != null && p.id == id) return p;
        return null;
    }

    // Refreshes the catalog to the latest built-in parts, keeping any 3D models you assigned (matched by id)
    public void UpgradeCatalog()
    {
        var old = new Dictionary<string, CADPart>();
        if (parts != null) foreach (var p in parts) if (p != null && !string.IsNullOrEmpty(p.id)) old[p.id] = p;
        parts = AscendCADDefaults.Build();
        foreach (var p in parts) if (old.TryGetValue(p.id, out var o)) { p.modelPrefab = o.modelPrefab; p.tintPrefab = o.tintPrefab; }
        // Pads: keep your models, pictures, scene links and placement tweaks
        var oldPads = new Dictionary<string, CADLaunchPad>();
        if (launchPads != null) foreach (var p in launchPads) if (p != null && !string.IsNullOrEmpty(p.id)) oldPads[p.id] = p;
        launchPads = AscendCADDefaults.BuildPads();
        foreach (var p in launchPads)
            if (oldPads.TryGetValue(p.id, out var o))
            {
                p.modelPrefab = o.modelPrefab; p.image = o.image; p.sceneObjectName = o.sceneObjectName; p.useBuiltInModel = o.useBuiltInModel;
                if (o.rocketOffset != Vector3.zero) p.rocketOffset = o.rocketOffset;
            }
        catalogVersion = CatalogVersion;
        lookup = null;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
    void OnValidate()
    {
        lookup = null;
        if (Application.isPlaying && Active == this) AscendEconomy.SyncFund(this);   // fund edits apply live
    }

    [ContextMenu("Load default parts (overwrites!)")]
    void LoadDefaults()
    {
        parts = AscendCADDefaults.Build();
        launchPads = AscendCADDefaults.BuildPads();
        catalogVersion = CatalogVersion;
        lookup = null;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Delete saved progress")]
    void DeleteSave() => AscendEconomy.DeleteSave();

    public CADPart Get(string id)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<string, CADPart>();
            foreach (var p in parts) if (p != null && !string.IsNullOrEmpty(p.id)) lookup[p.id] = p;
        }
        return id != null && lookup.TryGetValue(id, out var r) ? r : null;
    }

    public static AscendCADData CreateDefault()
    {
        var d = CreateInstance<AscendCADData>();
        d.name = "AscendCAD Defaults";
        d.parts = AscendCADDefaults.Build();
        d.launchPads = AscendCADDefaults.BuildPads();
        d.catalogVersion = CatalogVersion;
        return d;
    }

    // Use the assigned asset, or the built-in defaults if none is assigned
    public static AscendCADData Resolve(AscendCADData d)
    {
        if (d)
        {
            if (d.parts == null || d.parts.Count == 0 || d.launchPads == null || d.launchPads.Count == 0 || d.catalogVersion < CatalogVersion) d.UpgradeCatalog();
            return Active = d;
        }
        if (Active) return Active;
        if (!fallback)
        {
            fallback = CreateDefault();
            Debug.LogWarning("AscendCAD: no AscendCAD Data asset is assigned on ComputerInteraction > Ascend CAD, so the built-in defaults " +
                             "(US$30M fund) are used. Drag your AscendCAD Data asset into that field to use your own settings.");
        }
        return Active = fallback;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  DEFAULT CATALOG. Fills a new data asset. Real figures where published,
//  otherwise marked "est.". Tanks are computed from volume x propellant density.
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendCADDefaults
{
    static List<CADPart> list;
    static CADBuilder bld;
    static string cat, sub;

    const CADPropellant KL = CADPropellant.Kerolox, ML = CADPropellant.Methalox, HL = CADPropellant.Hydrolox, HY = CADPropellant.Hypergolic;
    static readonly Color White = new Color(0.94f, 0.94f, 0.95f), EngineGrey = new Color(0.32f, 0.32f, 0.34f),
        Foam = new Color(0.86f, 0.47f, 0.18f), Steel = new Color(0.74f, 0.75f, 0.78f), Carbon = new Color(0.26f, 0.27f, 0.30f),
        Gold = new Color(0.86f, 0.69f, 0.27f), Alu = new Color(0.80f, 0.81f, 0.83f), Black = new Color(0.12f, 0.12f, 0.13f);

    // Round a price to the nearest $1,000 (Math.Round only accepts 0-15 digits)
    static double RoundK(double v) => Math.Round(v / 1000.0) * 1000.0;

    static void Cat(CADBuilder b, string c) { bld = b; cat = c; }
    static void Sub(string s) { sub = s; }
    static string F(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    static CADPart Add(CADPart p)
    {
        p.builder = bld; p.category = cat; p.subcategory = sub;
        p.costEstimated = string.IsNullOrEmpty(p.source) || p.source.Contains("est");
        list.Add(p);
        return p;
    }

    static CADPart Engine(string id, string name, CADPropellant prop, float tSL, float tVac, float ispSL, float ispVac,
        float mass, double cost, float exitDia, float len, string desc, string src) =>
        Add(new CADPart { id = id, name = name, role = CADRole.Engine, shape = CADShape.Engine, attach = CADAttach.Stack,
            propellant = prop, thrustSLkN = tSL, thrustVackN = tVac, ispSL = ispSL, ispVac = ispVac, dryMassKg = mass,
            costUSD = cost, diameter = exitDia, length = len, description = desc, source = src, defaultColor = EngineGrey });

    static CADPart Solid(string id, string name, bool booster, float totalKg, float propKg, float tSL, float tVac,
        float ispSL, float ispVac, float burn, float dia, float len, double cost, string desc, string src) =>
        Add(new CADPart { id = id, name = name, role = CADRole.SolidMotor,
            shape = booster ? CADShape.Booster : CADShape.SolidStage,
            attach = booster ? CADAttach.RadialUpright : CADAttach.Stack, propellant = CADPropellant.Solid,
            dryMassKg = totalKg - propKg, propellantKg = propKg, thrustSLkN = tSL, thrustVackN = tVac, ispSL = ispSL,
            ispVac = ispVac, burnTimeS = burn, diameter = dia, length = len, costUSD = cost, description = desc, source = src,
            defaultColor = White });

    static CADPart Body(string id, string name, CADRole role, CADShape shape, float dia, float topDia, float len,
        float mass, double cost, string desc, Color col, string src = "est.") =>
        Add(new CADPart { id = id, name = name, role = role, shape = shape, attach = CADAttach.Stack, diameter = dia,
            topDiameter = topDia, length = len, dryMassKg = mass, costUSD = cost, description = desc, defaultColor = col, source = src });

    static CADPart Inside(string id, string name, CADRole role, float mass, double cost, float volL, float useW, float duty,
        string desc, string src = "est.") =>
        Add(new CADPart { id = id, name = name, role = role, shape = CADShape.None, attach = CADAttach.Internal,
            dryMassKg = mass, costUSD = cost, volumeL = volL, powerUseW = useW, dutyCycle = duty, description = desc, source = src });

    static CADPart Outside(string id, string name, CADRole role, CADShape shape, Vector3 size, float mass, double cost,
        float useW, float duty, Color col, string desc, string src = "est.") =>
        Add(new CADPart { id = id, name = name, role = role, shape = shape, attach = CADAttach.Radial, boxSize = size,
            length = size.z, dryMassKg = mass, costUSD = cost, powerUseW = useW, dutyCycle = duty, defaultColor = col,
            description = desc, source = src });

    static string PropLabel(CADPropellant p)
    {
        switch (p)
        {
            case KL: return "Kerolox";
            case ML: return "Methalox";
            case HL: return "Hydrolox";
            default: return "Hypergolic";
        }
    }

    // Tank mass, propellant and price are calculated, not typed in
    static void Tank(CADPropellant p, float d, float len)
    {
        float bulk, dryFrac, perKg, propPerKg; string mat; Color col;
        switch (p)
        {
            case KL:
                bulk = 1030f; propPerKg = 0.55f;
                if (d <= 1.2f) { mat = "carbon composite"; dryFrac = 0.035f; perKg = 2200f; col = Carbon; }
                else { mat = "aluminum-lithium"; dryFrac = 0.045f; perKg = 1100f; col = White; }
                break;
            case ML:
                bulk = 830f; propPerKg = 0.30f;
                if (d >= 3.7f) { mat = "stainless steel"; dryFrac = 0.07f; perKg = 250f; col = Steel; }
                else { mat = "aluminum-lithium"; dryFrac = 0.05f; perKg = 1100f; col = White; }
                break;
            case HL:
                bulk = 360f; propPerKg = 1.0f; mat = "aluminum-lithium with spray-on foam"; dryFrac = 0.11f; perKg = 1300f; col = Foam;
                break;
            default:
                bulk = 1180f; propPerKg = 110f; mat = "titanium / aluminum"; dryFrac = 0.08f; perKg = 3000f; col = Alu;
                break;
        }
        float vol = Mathf.PI * d * d * 0.25f * len * 0.92f;           // 8% lost to domes + ullage
        float prop = Mathf.Round(vol * bulk);
        float dry = Mathf.Round(Mathf.Max(5f, prop * dryFrac));
        double cost = RoundK(dry * perKg + prop * propPerKg);
        Add(new CADPart
        {
            id = "tank." + p.ToString().ToLowerInvariant() + "." + F(d) + "x" + F(len),
            name = PropLabel(p) + " Tank Ø" + F(d) + " m x " + F(len) + " m",
            role = CADRole.Tank, shape = CADShape.Cylinder, attach = CADAttach.Stack, propellant = p,
            diameter = d, length = len, dryMassKg = dry, propellantKg = prop, costUSD = cost, defaultColor = col,
            description = $"Common-bulkhead {PropLabel(p).ToLowerInvariant()} tank in {mat}. Holds {prop / 1000f:0.##} t of propellant ({vol:0.#} m³ usable).",
            source = $"Computed: volume x bulk density {bulk:0} kg/m³; structure ≈{dryFrac * 100f:0.#}% of propellant at ${perKg:0}/kg (est.)"
        });
    }

    static void Adapter(float a, float b)
    {
        float avg = (a + b) * 0.5f, m = Mathf.Round(30f * avg * avg);
        Body("str.adapter." + F(a) + "-" + F(b), "Adapter Ø" + F(a) + " m to Ø" + F(b) + " m", CADRole.Structure, CADShape.Frustum,
            a, b, Mathf.Abs(a - b) * 1.5f + 0.4f, m, RoundK(m * 1500.0),
            "Conical section joining two stack diameters so loads flow smoothly.", White);
    }

    static void Fairing(string id, float d, float len, float mass, double cost, string desc, string src = "est.")
    {
        var f = Body(id, "Payload Fairing Ø" + F(d) + " m", CADRole.Fairing, CADShape.Fairing, d, -1f, len, mass, cost,
            desc + " Splits open and falls away after the first stage.", White, src);
        f.innerDiameter = d * 0.88f; f.innerHeight = len * 0.78f;
    }

    static void PAF(string id, string name, float bottom, float top, float len, float mass, double cost, float maxKg)
    {
        var p = Body(id, name, CADRole.PayloadAdapter, CADShape.Frustum, bottom, top, len, mass, cost,
            "Cone that bolts the satellite's separation system to the upper stage.", Alu);
        p.maxPayloadKg = maxKg;
    }

    public static List<CADPart> Build()
    {
        list = new List<CADPart>();
        float[] dias = { 1.2f, 1.8f, 2.5f, 3.7f, 5f, 6.6f, 10.1f };
        float[] lens = { 1.5f, 3f, 6f, 12f };

        // ═════════════ ROCKET: PROPULSION ═════════════
        Cat(CADBuilder.Rocket, "Propulsion");
        Sub("Liquid Engines - Sea Level");
        Engine("eng.rutherford", "Rutherford", KL, 24.9f, 25.8f, 311f, 320f, 35f, 350000, 0.32f, 0.9f,
            "Rocket Lab's 3D-printed, battery-pumped small engine. Nine power Electron's first stage.", "Thrust/Isp: Rocket Lab; vac Isp, mass & cost est.");
        Engine("eng.merlin1d", "Merlin 1D", KL, 845f, 914f, 282f, 311f, 470f, 1000000, 0.92f, 2.9f,
            "SpaceX's workhorse gas-generator engine; nine per Falcon 9 booster. Throttles and restarts for landings.", "SpaceX public figures; cost est. ~$1M");
        Engine("eng.yf100", "YF-100", KL, 1200f, 1340f, 300f, 335f, 1900f, 2500000, 1.25f, 3.0f,
            "China's staged-combustion kerosene engine for the Long March 5/6/7 family.", "CALT public figures; mass, dims & cost est.");
        Engine("eng.be4", "BE-4", ML, 2400f, 2650f, 310f, 340f, 2300f, 8000000, 1.8f, 5.0f,
            "Blue Origin's staged-combustion methane engine; two per Vulcan, seven per New Glenn.", "Thrust: Blue Origin; Isp, mass & cost est.");
        Engine("eng.raptor2", "Raptor 2", ML, 2256f, 2450f, 327f, 350f, 1630f, 1000000, 1.3f, 3.1f,
            "SpaceX full-flow staged-combustion methane engine; 33 on Super Heavy.", "SpaceX public figures; vac thrust & cost est.");
        Engine("eng.vulcain21", "Vulcain 2.1", HL, 960f, 1370f, 318f, 431f, 2100f, 12000000, 2.1f, 3.4f,
            "Europe's hydrogen core engine for Ariane 6; burns about 8 minutes from liftoff.", "ArianeGroup public figures; SL values & cost est.");
        Engine("eng.rs25", "RS-25", HL, 1860f, 2280f, 366f, 452f, 3177f, 100000000, 2.3f, 4.3f,
            "The Shuttle main engine, now on SLS. Superb efficiency, extreme price.", "NASA / L3Harris; ~$100M per new engine reported");
        Engine("eng.f1", "F-1", KL, 6770f, 7770f, 263f, 304f, 8400f, 90000000, 3.7f, 5.8f,
            "Saturn V's monster: the most powerful single-chamber liquid engine ever flown.", "NASA historical; modern cost est.");

        Sub("Liquid Engines - Vacuum");
        Engine("eng.rutherfordvac", "Rutherford Vacuum", KL, 0f, 25.8f, 0f, 343f, 40f, 400000, 0.65f, 1.3f,
            "Electron's second-stage engine with a large vacuum nozzle. Cannot run at sea level.", "Rocket Lab; mass & cost est.");
        Engine("eng.aj10", "AJ10-118K", HY, 0f, 43.7f, 0f, 319f, 125f, 3500000, 1.5f, 2.7f,
            "Restartable storable-propellant upper-stage engine (Delta II heritage).", "Aerojet public figures; dims & cost est.");
        Engine("eng.rl10", "RL10C-1", HL, 0f, 101.8f, 0f, 449.7f, 190f, 17000000, 1.45f, 2.2f,
            "Expander-cycle hydrogen engine that powers Centaur. Very efficient, very expensive.", "L3Harris public figures; cost est.");
        Engine("eng.mvac", "Merlin Vacuum", KL, 0f, 981f, 0f, 348f, 490f, 1500000, 3.3f, 4.4f,
            "Falcon 9's second-stage engine with a radiatively cooled niobium nozzle.", "SpaceX public figures; mass, dims & cost est.");
        Engine("eng.rvac", "Raptor Vacuum", ML, 0f, 2530f, 0f, 380f, 1900f, 1800000, 2.3f, 4.6f,
            "Raptor with a large vacuum nozzle for Starship's upper stage.", "SpaceX; mass, dims & cost est.");

        Sub("Solid Motors & Boosters");
        Solid("srb.gem40", "GEM-40 Booster", true, 13080f, 11765f, 446f, 499f, 245f, 274f, 64f, 1.02f, 11f, 2500000,
            "Graphite-epoxy strap-on booster (Delta II). Attach to the side of a core stage.", "Northrop Grumman public figures; cost est.");
        Solid("srb.gem60", "GEM-60 Booster", true, 33638f, 29698f, 826f, 880f, 245f, 275f, 90f, 1.55f, 13f, 5000000,
            "Strap-on booster from the Delta IV Medium+ family.", "Northrop Grumman public figures; cost est.");
        Solid("srb.gem63", "GEM-63 Booster", true, 49300f, 44200f, 1663f, 1800f, 245f, 275f, 94f, 1.6f, 20.1f, 7000000,
            "Atlas V strap-on booster; the stretched XL flies on Vulcan.", "Northrop Grumman public figures; cost est.");
        Solid("srb.p241", "P241 (EAP) Booster", true, 277000f, 240000f, 6500f, 7080f, 250f, 275f, 130f, 3.05f, 31.6f, 40000000,
            "Ariane 5's giant side booster; over 90% of liftoff thrust.", "ESA / ArianeGroup public figures; cost est.");
        Solid("srb.shuttle", "Space Shuttle SRB", true, 590000f, 500000f, 12500f, 13800f, 242f, 268f, 124f, 3.71f, 45.6f, 50000000,
            "The largest solid boosters ever flown operationally.", "NASA historical; cost est.");
        Solid("srb.castor30xl", "Castor 30XL Stage", false, 26300f, 24900f, 0f, 474f, 0f, 294f, 156f, 2.34f, 6.0f, 10000000,
            "Solid upper stage (Antares). Stack it like a tank; it carries its own propellant.", "Northrop Grumman public figures; cost est.");
        Solid("srb.star48", "Star 48B Kick Motor", false, 2141f, 2010f, 0f, 68.6f, 0f, 292f, 84f, 1.24f, 2.03f, 3000000,
            "Small spherical-case solid motor for final orbit raising.", "Northrop Grumman public figures; cost est.");

        // ═════════════ ROCKET: TANKS (computed) ═════════════
        Cat(CADBuilder.Rocket, "Propellant Tanks");
        Sub("Kerolox (RP-1 / LOX)"); foreach (var d in dias) foreach (var l in lens) Tank(KL, d, l);
        Sub("Methalox (CH4 / LOX)"); foreach (var d in dias) foreach (var l in lens) Tank(ML, d, l);
        Sub("Hydrolox (LH2 / LOX)"); foreach (var d in dias) foreach (var l in lens) Tank(HL, d, l);
        Sub("Hypergolic (MMH / NTO)"); foreach (var d in new[] { 1.2f, 1.8f, 2.5f }) foreach (var l in new[] { 1f, 2f, 4f }) Tank(HY, d, l);

        // ═════════════ ROCKET: STRUCTURE ═════════════
        Cat(CADBuilder.Rocket, "Structure");
        Sub("Interstages & Decouplers");
        foreach (var d in dias)
        {
            float m = Mathf.Round(60f * d * d);
            Body("str.interstage." + F(d), "Interstage Decoupler Ø" + F(d) + " m", CADRole.Decoupler, CADShape.Cylinder, d, -1f,
                Mathf.Clamp(d * 1.1f, 1f, 5.5f), m, RoundK(m * 1800.0),
                "Joins two stages and separates them with pneumatic pushers. Everything above it is the next stage. Room inside for avionics.",
                Carbon).internalVolumeL = Mathf.Round(40f * d * d);
        }
        Sub("Diameter Adapters");
        for (int i = 0; i < dias.Length - 1; i++) { Adapter(dias[i], dias[i + 1]); Adapter(dias[i + 1], dias[i]); }
        Sub("Nose Cones");
        foreach (var d in dias)
        {
            float m = Mathf.Round(14f * d * d);
            Body("str.nose." + F(d), "Nose Cone Ø" + F(d) + " m", CADRole.NoseCone, CADShape.NoseCone, d, -1f, d * 2.2f, m,
                RoundK(m * 1500.0), "Low-drag tangent-ogive nose for vehicles or boosters without a fairing.", White);
        }
        Sub("Avionics Bays");
        foreach (var d in dias)
        {
            float m = Mathf.Round(25f * d * d);
            Body("str.avbay." + F(d), "Avionics Bay Ø" + F(d) + " m", CADRole.Structure, CADShape.Cylinder, d, -1f,
                d <= 1.8f ? 0.5f : 0.8f, m, RoundK(m * 2500.0),
                "Short ring that houses guidance, power and telemetry boxes. Install avionics inside it.", Alu)
                .internalVolumeL = Mathf.Round(120f * d * d);
        }

        // ═════════════ ROCKET: PAYLOAD INTEGRATION ═════════════
        Cat(CADBuilder.Rocket, "Payload Integration");
        Sub("Payload Fairings");
        Fairing("fair.1.2", 1.2f, 2.5f, 55f, 350000, "Electron-class composite fairing.");
        Fairing("fair.1.8", 1.8f, 3.6f, 140f, 700000, "Small-launcher composite fairing.");
        Fairing("fair.2.5", 2.5f, 6f, 400f, 1600000, "Medium fairing for 100-500 kg satellites.");
        Fairing("fair.3.7", 3.7f, 9.5f, 1100f, 3500000, "Large carbon-fiber fairing.");
        Fairing("fair.5.2", 5.2f, 13.1f, 1900f, 6000000, "Falcon-class 5.2 m fairing.", "SpaceX: ~$6M reported; mass est.");
        foreach (var fp in list) if (fp.role == CADRole.Fairing && fp.maxPayloadKg <= 0f) fp.maxPayloadKg = Mathf.Round(Mathf.Pow(fp.diameter, 2.6f) * 330f / 100f) * 100f;
        Sub("Payload Adapters");
        PAF("paf.small", "Small Payload Adapter (0.6 m)", 1.0f, 0.6f, 0.3f, 8f, 45000, 400f);
        PAF("paf.937", "PAF 937 Adapter", 1.6f, 0.937f, 0.6f, 35f, 120000, 3000f);
        PAF("paf.1194", "PAF 1194 Adapter", 2.0f, 1.194f, 0.7f, 50f, 160000, 5000f);
        PAF("paf.1666", "PAF 1666 Adapter", 2.6f, 1.666f, 0.9f, 95f, 300000, 10000f);

        // ═════════════ ROCKET: AVIONICS (internal) ═════════════
        Cat(CADBuilder.Rocket, "Avionics (install inside a bay)");
        Sub("Guidance & Navigation");
        Inside("av.fc", "Flight Computer (triple-redundant)", CADRole.FlightComputer, 12f, 650000, 15f, 60f, 1f,
            "Three processors vote on every decision; runs guidance, sequencing and engine control.");
        Inside("av.imu.rlg", "Navigation IMU (ring-laser)", CADRole.IMU, 9f, 280000, 8f, 40f, 1f,
            "Navigation-grade gyros and accelerometers: knows attitude and velocity without outside help.");
        Inside("av.imu.mems", "MEMS IMU (budget)", CADRole.IMU, 1.2f, 45000, 1f, 5f, 1f,
            "Cheap and light, but drifts faster. Pair it with GNSS.");
        Inside("av.gnss", "GNSS Receiver (space-qualified)", CADRole.GNSS, 2.5f, 60000, 2f, 10f, 1f,
            "GPS/Galileo receiver that works at rocket speeds; corrects IMU drift and feeds range safety.");
        Sub("Range Safety & Telemetry");
        Inside("av.afts", "Autonomous Flight Termination System", CADRole.FlightTermination, 18f, 900000, 12f, 20f, 1f,
            "Ends the flight automatically if the rocket leaves its safety corridor. Required by every range.");
        Inside("av.tlm", "S-band Telemetry Transmitter", CADRole.Telemetry, 3.5f, 90000, 3f, 45f, 1f,
            "Streams thousands of sensor channels and video to the ground.");
        Inside("av.cmd", "Command / Range Receiver", CADRole.CommandReceiver, 2f, 55000, 2f, 10f, 1f,
            "Receives ground commands, including range-safety instructions.");
        Sub("Power");
        Inside("av.batt", "Avionics Li-ion Battery (28 V, 2 kWh)", CADRole.Battery, 14f, 110000, 8f, 0f, 1f,
            "Screened space-grade cells powering computers, valves and actuators.").batteryWh = 2000f;
        Inside("av.batt.backup", "Backup Battery (600 Wh)", CADRole.Battery, 6f, 35000, 3f, 0f, 1f,
            "Independent supply for the flight termination system and critical loads.").batteryWh = 600f;
        Inside("av.pdu", "Power Distribution Unit", CADRole.PowerDistribution, 9f, 160000, 8f, 15f, 1f,
            "The rocket's fuse box: switches and protects every circuit.");
        Inside("av.harness", "Wiring Harness Kit", CADRole.Wiring, 45f, 280000, 20f, 0f, 1f,
            "Hand-built cable bundles for power, data and pyro lines.");

        // ═════════════ ROCKET: RECOVERY & AERO ═════════════
        Cat(CADBuilder.Rocket, "Recovery & Aero");
        Sub("Fins");
        Outside("aero.fin.s", "Aero Fin (small)", CADRole.Aero, CADShape.Fin, new Vector3(0.03f, 0.8f, 0.45f), 6f, 15000, 0f, 1f, White,
            "Passive stabilizing fin for small rockets.");
        Outside("aero.fin.l", "Aero Fin (large)", CADRole.Aero, CADShape.Fin, new Vector3(0.06f, 2.0f, 1.0f), 60f, 120000, 0f, 1f, White,
            "Large stabilizing fin.");
        Outside("rec.gridfin", "Titanium Grid Fin", CADRole.Recovery, CADShape.GridFin, new Vector3(0.12f, 1.2f, 1.5f), 250f, 500000, 0f, 1f, Steel,
            "Steers a returning booster through the atmosphere. Place near the top of the first stage.");
        Sub("Landing");
        Outside("rec.leg", "Carbon Landing Leg", CADRole.Recovery, CADShape.Leg, new Vector3(0.45f, 7.5f, 0.35f), 600f, 600000, 0f, 1f, Carbon,
            "Folds against the booster, deploys before touchdown. Use symmetry x4.");

        BuildSounding();
        BuildHeritage();
        BuildPayloadParts();
        BuildCanSat();
        BuildHeritageSpacecraft();
        return list;
    }

    // ───────────── Small hobby rockets for the Sky Sensor (CanSat) mission ─────────────
    // Motor numbers: J-class ~560 N·s and K-class ~930 N·s (total impulse = propellant x Isp x g0), like real CanSat competition rockets.
    static void BuildSounding()
    {
        Cat(CADBuilder.Rocket, "Sounding Rockets (CanSat)");
        Sub("Hobby Motors");
        Solid("snd.motor.j", "K700 Motor Section Ø15.6 cm", false, 2.5f, 0.90f, 0.69f, 0.73f, 195f, 205f, 2.5f, 0.156f, 0.9f, 900,
            "High-power rocket motor in its mount tube (K class, about 1,700 N·s). Lifts a full-size CanSat rocket to about 1.2 km. Once lit it can't be switched off. It goes at the very bottom.",
            "Typical K-class reload: ~1,700 N·s total impulse, Isp ~195 s (Tripoli/NAR class); price est. incl. reusable case");
        Solid("snd.motor.k", "L850 Motor Section Ø15.6 cm", false, 3.1f, 1.20f, 0.82f, 0.86f, 195f, 205f, 2.8f, 0.156f, 1.1f, 1400,
            "Bigger motor (L class, about 2,300 N·s). Flies higher, so the CanSat falls for longer. Needs a Level 2 certified flyer.",
            "Typical L-class reload: ~2,300 N·s, Isp ~195 s; price est. incl. reusable case");
        Sub("Airframe");
        Body("snd.tube", "Fiberglass Body Tube Ø15.6 cm x 1.2 m", CADRole.Structure, CADShape.Cylinder, 0.156f, -1f, 1.2f, 2.2f, 650,
            "Strong fiberglass tube that forms the rocket's body. The flight electronics go inside it.", White).internalVolumeL = 15f;
        var bay = Body("snd.bay", "CanSat Payload Bay Ø15.6 cm x 0.6 m", CADRole.Fairing, CADShape.Cylinder, 0.156f, -1f, 0.6f, 1.0f, 450,
            "Tube section that holds the CanSat. At the top of the flight the nose pops off and the CanSat is pushed out.", White);
        bay.innerDiameter = 0.14f; bay.innerHeight = 0.5f; bay.maxPayloadKg = 4f;
        Body("snd.nose", "Ogive Nose Cone Ø15.6 cm", CADRole.NoseCone, CADShape.NoseCone, 0.156f, -1f, 0.6f, 0.8f, 300,
            "Pointy fiberglass nose so the air flows smoothly around the rocket.", White);
        Outside("snd.fin", "G10 Fiberglass Fin", CADRole.Aero, CADShape.Fin, new Vector3(0.006f, 0.3f, 0.2f), 0.25f, 120, 0f, 1f, White,
            "Keeps the rocket flying straight, like the feathers on an arrow. Use 3 or 4 near the bottom.");
        Sub("Flight Electronics");
        Inside("snd.alt", "Altimeter Flight Computer", CADRole.FlightComputer, 0.05f, 350, 0.05f, 0.3f, 1f,
            "Senses height from air pressure and pops the nose to release the CanSat at the top of the flight.");
        Inside("snd.imu", "6-Axis IMU Board", CADRole.IMU, 0.02f, 120, 0.02f, 0.1f, 1f, "Gyro and accelerometer that sense tilt and speed.");
        Inside("snd.gps", "GPS Radio Tracker", CADRole.Telemetry, 0.1f, 600, 0.1f, 0.5f, 1f, "Sends the rocket's position so the recovery team can find it.");
        Inside("snd.batt", "Li-Po Flight Battery", CADRole.Battery, 0.1f, 40, 0.05f, 0f, 1f, "Powers the altimeter and the tracker.").batteryWh = 10f;
    }

    // ───────────── CanSat parts (soda-can sized satellite) ─────────────
    static void BuildCanSat()
    {
        Cat(CADBuilder.Payload, "CanSat (Sky Sensor)");
        Sub("CanSat Parts");
        Cyl("can.shell", "CanSat Shell (soda-can size)", 0.066f, 0.115f, 0.06f, 150, 0.3f,
            "66 x 115 mm can, the size of a soda can, just like real CanSat competitions. Everything else goes inside.");
        Sep("can.eject", "CanSat Ejection Spring Plate", false, 0.066f, 0.01f, 0.01f, 80, 1f, "Pushes the CanSat out of the rocket at the top of the flight.");
        Outside("can.chute", "CanSat Parachute (packed)", CADRole.Recovery, CADShape.Box, new Vector3(0.05f, 0.03f, 0.03f), 0.04f, 120, 0f, 1f,
            new Color(1f, 0.45f, 0.1f), "Opens after release so the CanSat floats down slowly (about 8 m/s).");
        Inside("can.obc", "Microcontroller Board (Arduino class)", CADRole.OBC, 0.01f, 60, 0.02f, 0.3f, 1f, "The CanSat's little brain: reads the sensors and sends the data.");
        InstIn("can.sensor", "Temperature + Air Pressure Sensor", 0.005f, 40, 0.005f, 0.01f, 1f, 1,
            "Measures how warm the air is and how hard it pushes (air pressure). The main science of a CanSat!");
        InstIn("can.humid", "Humidity Sensor", 0.004f, 30, 0.004f, 0.01f, 1f, 1, "Measures how much water vapor is in the air.");
        Inside("can.radio", "LoRa Radio 433 MHz", CADRole.Transceiver, 0.01f, 90, 0.01f, 0.5f, 0.3f,
            "Sends the measurements to the ground station while the CanSat falls.").dataRateKbps = 5f;
        Outside("can.ant", "Wire Antenna", CADRole.Antenna, CADShape.Rod, new Vector3(0.005f, 0.005f, 0.17f), 0.005f, 20, 0f, 1f,
            new Color(0.7f, 0.7f, 0.72f), "Simple quarter-wave wire antenna for the radio. Wrapped around the can until release.").stowedSize = new Vector3(0.005f, 0.005f, 0.005f);
        Inside("can.batt", "CanSat Battery 2 Wh", CADRole.SatBattery, 0.05f, 40, 0.03f, 0f, 1f, "Small Li-ion cell: plenty for a few minutes of flight.").batteryWh = 2f;
    }

    // ───────────── Heritage hardware (used by the prebuilt Saturn V / Shuttle) ─────────────
    static void BuildHeritage()
    {
        Cat(CADBuilder.Rocket, "Heritage Hardware");
        Sub("Saturn V / Apollo");
        Engine("eng.j2", "J-2", HL, 486f, 1033f, 200f, 421f, 1788f, 25000000, 2.0f, 3.4f,
            "Restartable hydrogen engine of the Saturn V S-II (x5) and S-IVB (x1) stages.", "NASA historical; modern cost est.");
        Body("str.interstage.saturn", "S-II/S-IVB Conical Interstage Ø10.1 to 6.6 m", CADRole.Decoupler, CADShape.Frustum, 10.1f, 6.6f, 5.65f,
            3650f, 9000000, "Tapered decoupler between Saturn V's second and third stages. The S-IVB's J-2 hides inside it.", White, "NASA historical; cost est.");
        Body("apollo.sla", "Spacecraft-LM Adapter (Lunar Module inside)", CADRole.Structure, CADShape.Frustum, 6.6f, 3.9f, 8.5f,
            16900f, 300000000, "Tapered shroud carrying the 15 t Lunar Module below the Apollo CSM.", Alu, "NASA: SLA ~1.8 t + LM ~15.1 t; cost est.");
        var csm = Body("apollo.csm", "Apollo Command/Service Module", CADRole.Structure, CADShape.Capsule, 3.9f, -1f, 11f,
            30300f, 250000000, "Three-person command capsule on top of the service module with its big SPS engine.", Alu, "NASA: ~30.3 t fueled; cost est.");
        csm.internalVolumeL = 400f;
        Body("apollo.les", "Launch Escape System", CADRole.Fairing, CADShape.Tower, 3.9f, -1f, 10f,
            4200f, 20000000, "Solid-rocket tower that pulls the capsule to safety in an emergency. Jettisoned during ascent.", White, "NASA: ~4.2 t; cost est.");

        Sub("Space Shuttle");
        Add(new CADPart
        {
            id = "sts.et", name = "Space Shuttle External Tank", role = CADRole.Tank, shape = CADShape.OgiveTank, attach = CADAttach.Stack,
            propellant = HL, diameter = 8.4f, length = 46.9f, dryMassKg = 26500f, propellantKg = 735000f, costUSD = 75000000,
            defaultColor = Foam, description = "The giant orange tank feeding the orbiter's three RS-25s. Orbiter and SRBs attach to its sides.",
            source = "NASA: Super Lightweight Tank ~26.5 t empty, ~735 t propellant; cost est."
        });
        Add(new CADPart
        {
            id = "sts.orbiter", name = "Space Shuttle Orbiter (3x RS-25)", role = CADRole.Engine, shape = CADShape.Orbiter,
            attach = CADAttach.RadialUpright, crossfeed = true, propellant = HL, diameter = 5.2f, length = 37.2f,
            thrustSLkN = 5580f, thrustVackN = 6840f, ispSL = 366f, ispVac = 452f, dryMassKg = 100000f, costUSD = 1700000000,
            internalVolumeL = 3000f, innerDiameter = 4.6f, innerHeight = 18.3f, maxPayloadKg = 24400f, defaultColor = White,
            description = "Winged orbiter. Mount it on the side of the External Tank; its RS-25s burn the tank's hydrogen. Satellites ride in its 18 m payload bay; avionics install inside.",
            source = "NASA: ~100 t with OMS propellant, 24.4 t payload to LEO; Endeavour cost ~$1.7B"
        });
    }

    // ───────────── Satellite helpers ─────────────
    static CADPart Bus(string id, string name, Vector3 size, float mass, double cost, float internalL, string desc, string src = "est.") =>
        Add(new CADPart { id = id, name = name, role = CADRole.SatStructure, shape = CADShape.Box, attach = CADAttach.Stack,
            boxSize = size, length = size.y, dryMassKg = mass, costUSD = cost, internalVolumeL = internalL,
            description = desc, source = src, defaultColor = Alu });

    static CADPart Cyl(string id, string name, float d, float len, float mass, double cost, float internalL, string desc) =>
        Add(new CADPart { id = id, name = name, role = CADRole.SatStructure, shape = CADShape.Cylinder, attach = CADAttach.Stack,
            diameter = d, length = len, dryMassKg = mass, costUSD = cost, internalVolumeL = internalL,
            description = desc, source = "est.", defaultColor = Gold });

    static CADPart Sep(string id, string name, bool box, float d, float h, float mass, double cost, float maxKg, string desc) =>
        Add(new CADPart { id = id, name = name, role = CADRole.Separation, shape = box ? CADShape.Box : CADShape.Cylinder,
            attach = CADAttach.Stack, diameter = d, length = h, boxSize = new Vector3(d, h, d), dryMassKg = mass, costUSD = cost,
            maxPayloadKg = maxKg, description = desc, source = "est.", defaultColor = Black });

    static CADPart Solar(string id, string name, CADShape shape, Vector3 size, float watts, float mass, double cost, string desc, string src) =>
        Add(new CADPart { id = id, name = name, role = CADRole.SolarPanel, shape = shape, attach = CADAttach.Radial,
            boxSize = size, length = size.z, powerGenW = watts, dryMassKg = mass, costUSD = cost,
            description = desc, source = src, defaultColor = White });

    static CADPart InstIn(string id, string name, float mass, double cost, float volL, float useW, float duty, float kbps, string desc, string src = "est.")
    {
        var p = Inside(id, name, CADRole.Instrument, mass, cost, volL, useW, duty, desc, src);
        p.dataRateKbps = kbps;
        return p;
    }

    static CADPart InstOut(string id, string name, CADShape shape, Vector3 size, float mass, double cost, float useW, float duty, float kbps,
        string desc, string src = "est.")
    {
        var p = Outside(id, name, CADRole.Instrument, shape, size, mass, cost, useW, duty, Black, desc, src);
        p.dataRateKbps = kbps;
        return p;
    }

    // ───────────── Launch pads ─────────────
    public static List<CADLaunchPad> BuildPads() => new List<CADLaunchPad>
    {
        new CADLaunchPad
        {
            id = "pad.basic", name = "Basic Launch Mount", ownedFromStart = true, costUSD = 0, costEstimated = false,
            maxHeightM = 40f, maxWidthM = 6f, maxLiftoffMassT = 150f,
            description = "Government-provided concrete pad with a hold-down clamp and a small umbilical mast. Fine for small launchers like Electron.",
            source = "Electron: 18 m tall, 1.2 m wide, ~13 t (Rocket Lab). Limits est."
        },
        new CADLaunchPad
        {
            id = "pad.rail", name = "CanSat Launch Rail", costUSD = 6000,
            maxHeightM = 5f, maxWidthM = 0.3f, maxLiftoffMassT = 0.05f,
            description = "6 m steel launch rail on a tripod, with a blast plate and a wired launch controller. Small sounding rockets slide up the rail until they are fast enough for their fins to keep them straight. Only for hobby / CanSat rockets.",
            source = "Typical high-power rocketry 1515 rail (~6 m), as used at CanSat competitions. Limits and cost est."
        },
        new CADLaunchPad
        {
            id = "pad.gantry", name = "Gantry Launch Pad", costUSD = 150000000,
            maxHeightM = 80f, maxWidthM = 14f, maxLiftoffMassT = 1300f,
            description = "Fixed pad with a service gantry tower, flame trench and propellant farm. The rocket is erected and serviced right at the pad. Medium rockets up to Falcon 9 class.",
            source = "Falcon 9: ~70 m tall, 3.7 m wide, ~550 t. Limits and cost est."
        },
        new CADLaunchPad
        {
            id = "pad.ml", name = "Mobile Launcher", costUSD = 900000000,
            maxHeightM = 125f, maxWidthM = 36f, maxLiftoffMassT = 3600f, requiresId = "pad.crawler", ownMassT = 5000f,
            rocketOffset = new Vector3(0f, 7.6f, 0f),
            description = "Two-story launch platform with a ~116 m umbilical tower. The rocket is stacked on it indoors, then the Crawler carries both to the pad. Built for super-heavy rockets.",
            source = "NASA: SLS Mobile Launcher tower ~116 m. Mass, limits and cost est."
        },
        new CADLaunchPad
        {
            id = "pad.crawler", name = "Crawler-Transporter", isTransporter = true, costUSD = 150000000, carryCapacityT = 8165f,
            description = "Tracked giant that carries the Mobile Launcher and its rocket to the pad at about 1.6 km/h. Required to use the Mobile Launcher.",
            source = "NASA: Crawler-Transporter 2 upgraded to ~8,165 t capacity. Cost est."
        },
    };

    // ───────────── Heritage spacecraft (used by the prebuilt satellites) ─────────────
    static void BuildHeritageSpacecraft()
    {
        Cat(CADBuilder.Payload, "Heritage Spacecraft");
        Sub("Philippine Satellites");
        Bus("ph.bus.d1", "Diwata-1 Bus (55 x 35 x 55 cm)", new Vector3(0.55f, 0.55f, 0.35f), 25f, 2500000, 85f,
            "The Philippines' first microsatellite body, built by Filipino engineers with Tohoku and Hokkaido universities. Includes structure, harness and thermal hardware.",
            "Size: Diwata-1 (eoPortal); mass and cost est.");
        Bus("ph.bus.d2", "Diwata-2 Bus (50 cm cube)", new Vector3(0.5f, 0.5f, 0.5f), 27.5f, 2800000, 100f,
            "Diwata-2's 50 cm cube body: the same bus design as Diwata-1, with room for an amateur radio unit and deployable paddles.",
            "Size: PHL-Microsat (50 x 50 x 50 cm); mass and cost est.");
        Solar("ph.dsap", "Deployable Solar Array Paddle (DSAP)", CADShape.Wing, new Vector3(0.01f, 0.45f, 0.45f), 30f, 0.9f, 60000,
            "Diwata-2's experimental fold-out paddle; stowed against the side for launch.", "Heritage: Diwata-2 DSAP; numbers est.");
        InstOut("sci.erc", "Enhanced Resolution Camera (ERC)", CADShape.Camera, new Vector3(0.1f, 0.1f, 0.15f), 0.8f, 60000, 3f, 0.2f, 10000,
            "Sharpens the multispectral images (pan-sharpening). Flown on Diwata-2.", "Payload: Diwata-2 (STAMINA4Space); numbers est.");
        InstIn("sci.cam.tiny", "Tiny RGB Camera (CubeSat)", 0.02f, 3000, 0.02f, 0.2f, 0.1f, 100,
            "Matchbox-size camera for Earth snapshots, as on the BIRDS CubeSats.", "Heritage: Maya-1 / BIRDS-2; numbers est.");
        Inside("sat.adcs.passive", "Passive Magnetic Attitude (magnet + hysteresis rods)", CADRole.ADCSIntegrated, 0.03f, 600, 0.02f, 0f, 1f,
            "A permanent magnet lines the CubeSat up with Earth's field and hysteresis rods damp the wobble. No power needed. Used on BIRDS CubeSats like Maya-1.",
            "Heritage: BIRDS-2 (Maya-1); numbers est.");
        Sep("sat.sep.springs", "CubeSat Separation Springs & Switches", true, 0.1f, 0.008f, 0.02f, 1500, 6f,
            "Spring plungers and kill switches on the CubeSat rails. The deployer itself stays with the rocket.");

        Sub("Hayabusa (JAXA)");
        Bus("hay.bus", "Hayabusa Bus (1.6 x 1.1 x 1.0 m)", new Vector3(1.6f, 1.1f, 1.0f), 190f, 40000000, 900f,
            "Box body of JAXA's asteroid sample-return probe (launched 2003 on an M-V rocket).", "Size: JAXA/ISAS; mass and cost est.");
        Solar("hay.solar", "Hayabusa Solar Paddle", CADShape.Wing, new Vector3(0.03f, 1.1f, 2.05f), 1300f, 15f, 6000000,
            "One of two fixed paddles; 5.7 m tip to tip with the body.", "Span: JAXA/ISAS (5.7 m); power, mass and cost est.");
        var ion = Outside("hay.ion", "µ10 Ion Engine System (4 thrusters)", CADRole.SatPropulsion, CADShape.Camera, new Vector3(0.6f, 0.6f, 0.4f),
            59f, 15000000, 1100f, 0.3f, Black, "Microwave-discharge xenon ion engines: tiny thrust, huge efficiency. They ran for about 40,000 thruster-hours.",
            "JAXA: 4 thrusters, Isp ~3,000 s, ~60 kg xenon; system mass and power est.");
        ion.propellant = CADPropellant.Electric; ion.propellantKg = 60f; ion.ispVac = 3000f;
        var rcs = Inside("hay.rcs", "Bipropellant RCS (12 thrusters)", CADRole.SatPropulsion, 20f, 4000000, 120f, 10f, 0.05f,
            "Chemical thrusters for touchdowns and attitude control.", "~70 kg chemical propellant (JAXA via NSS); hardware est.");
        rcs.propellant = CADPropellant.Hypergolic; rcs.propellantKg = 70f; rcs.ispVac = 290f;
        Outside("hay.hga", "1.5 m High-Gain Antenna", CADRole.Antenna, CADShape.Dish, new Vector3(1.5f, 1.5f, 0.5f), 6f, 5000000, 0f, 1f, White,
            "Deep-space X-band dish for talking to Earth from the asteroid.", "est.");
        Outside("hay.capsule", "Sample Return Capsule", CADRole.Instrument, CADShape.Dish, new Vector3(0.4f, 0.4f, 0.2f), 17f, 8000000, 0f, 1f, Gold,
            "Heat-shielded capsule that carried the Itokawa grains back to Earth in 2010.", "est.");
        Outside("hay.horn", "Sampler Horn", CADRole.Instrument, CADShape.Rod, new Vector3(0.15f, 0.15f, 1.0f), 5f, 3000000, 5f, 0.01f, Steel,
            "Funnel that touched the asteroid and caught kicked-up grains.", "est.");
        InstOut("hay.amica", "AMICA Multiband Imaging Camera", CADShape.Camera, new Vector3(0.15f, 0.15f, 0.2f), 1.6f, 3000000, 6f, 0.3f, 2000,
            "Telescopic camera with color filters for mapping the asteroid.", "Instrument: JAXA; numbers est.");
        InstOut("hay.lidar", "LIDAR Laser Altimeter", CADShape.Camera, new Vector3(0.25f, 0.25f, 0.3f), 3.7f, 4000000, 17f, 0.3f, 10,
            "Measures the distance to the surface for navigation and shape mapping.", "Instrument: JAXA; numbers est.");
        InstIn("hay.nirs", "NIRS Near-Infrared Spectrometer", 2.4f, 3000000, 5f, 5f, 0.3f, 50,
            "Maps the asteroid's minerals.", "Instrument: JAXA; numbers est.");
        InstOut("hay.xrs", "XRS X-Ray Fluorescence Spectrometer", CADShape.Box, new Vector3(0.2f, 0.2f, 0.15f), 3f, 3000000, 5f, 0.3f, 20,
            "Measures the elements on the surface.", "Instrument: JAXA; numbers est.");
        InstOut("hay.minerva", "MINERVA Hopping Rover", CADShape.Box, new Vector3(0.12f, 0.1f, 0.12f), 0.59f, 2000000, 0f, 1f, 1,
            "Tiny rover that hopped using an internal flywheel.", "est.");

        Sub("James Webb (NASA/ESA/CSA)");
        Bus("jwst.bus", "Webb Spacecraft Bus", new Vector3(2.6f, 1.2f, 2.6f), 2000f, 1500000000, 3000f,
            "Hot-side bus with power, computers, propulsion and pointing. Everything below the sunshield.", "Total observatory ~6,200 kg (ESA); bus share est.");
        var tower = Bus("jwst.isim", "Deployable Tower + Instrument Module (ISIM)", new Vector3(2.2f, 2.6f, 1.8f), 1500f, 1500000000, 3000f,
            "Cold-side structure holding the four science instruments behind the mirror.", "est.");
        tower.defaultColor = Black;
        var shield = Outside("jwst.shield", "5-Layer Sunshield (21.2 x 14.2 m)", CADRole.Thermal, CADShape.Panel, new Vector3(21.2f, 14.2f, 0.25f),
            1000f, 1000000000, 0f, 1f, new Color(0.82f, 0.78f, 0.86f), "Tennis-court-size kapton shield that keeps the telescope at about -233 °C. Folded for launch.",
            "Size: NASA/ESA (21.2 x 14.2 m); mass and cost est.");
        shield.stowedSize = new Vector3(4.0f, 2.4f, 1.2f);
        var mirror = Outside("jwst.mirror", "Primary Mirror 6.5 m (18 gold segments)", CADRole.Instrument, CADShape.Dish, new Vector3(6.5f, 6.5f, 1.2f),
            705f, 2500000000, 20f, 1f, Gold, "18 gold-coated beryllium hexagons. The side wings fold back for launch.",
            "NASA: 6.5 m, 705 kg; cost est.");
        mirror.stowedSize = new Vector3(3.9f, 6.5f, 2.0f);
        var sec = Outside("jwst.secondary", "Secondary Mirror Tripod", CADRole.Instrument, CADShape.Rod, new Vector3(0.15f, 0.15f, 7.0f),
            200f, 300000000, 5f, 1f, Black, "Holds the small secondary mirror about 7 m in front of the primary. Folded for launch.", "est.");
        sec.stowedSize = new Vector3(0.5f, 0.5f, 1.5f);
        Solar("jwst.solar", "Webb Solar Array (2 kW)", CADShape.Wing, new Vector3(0.05f, 1.3f, 5.9f), 2000f, 60f, 100000000,
            "Fold-out array on the hot side.", "CSA: 2,000 W; size, mass and cost est.");
        InstIn("jwst.nircam", "NIRCam Near-Infrared Camera", 160f, 500000000, 600f, 40f, 1f, 30000, "Webb's main camera, 0.6-5 µm.", "Instrument: NASA/UArizona; numbers est.");
        InstIn("jwst.nirspec", "NIRSpec Spectrograph", 196f, 500000000, 900f, 40f, 1f, 30000, "Takes spectra of up to 100 objects at once (ESA).", "Instrument: ESA; numbers est.");
        InstIn("jwst.miri", "MIRI Mid-Infrared Instrument + Cryocooler", 160f, 500000000, 600f, 90f, 1f, 20000, "5-28 µm camera and spectrograph, chilled to about 7 K.", "Instrument: ESA/NASA; numbers est.");
        InstIn("jwst.niriss", "FGS/NIRISS Guider + Spectrograph", 82f, 300000000, 300f, 30f, 1f, 10000, "Fine guidance sensor and slitless spectrograph (Canada).", "Instrument: CSA; numbers est.");
        Sep("sat.sep.clamp1666", "Clamp Band 1666 mm", false, 1.67f, 0.12f, 45f, 400000, 8000f, "Large clamp band for multi-tonne spacecraft.");
    }

    static void BuildPayloadParts()
    {
        // ═════════════ PAYLOAD: STRUCTURE ═════════════
        Cat(CADBuilder.Payload, "Satellite Structure");
        Sub("CubeSat Frames");
        Bus("sat.str.1u", "1U CubeSat Frame", new Vector3(0.1f, 0.1135f, 0.1f), 0.10f, 3500, 0.85f,
            "10 x 10 x 11.35 cm aluminum frame, the classic CubeSat unit (Maya-class).");
        Bus("sat.str.3u", "3U CubeSat Frame", new Vector3(0.1f, 0.3405f, 0.1f), 0.30f, 6000, 2.7f,
            "Three units long. Popular for imaging and science CubeSats.");
        Bus("sat.str.6u", "6U CubeSat Frame", new Vector3(0.226f, 0.366f, 0.1f), 0.90f, 11000, 5.4f,
            "Double-wide 3U: room for a real camera plus 3-axis ADCS.");
        Bus("sat.str.12u", "12U CubeSat Frame", new Vector3(0.226f, 0.366f, 0.226f), 1.6f, 15500, 11f,
            "Large CubeSat class for serious Earth-observation payloads.");
        Bus("sat.str.16u", "16U CubeSat Frame", new Vector3(0.226f, 0.454f, 0.226f), 2.0f, 18200, 14.5f,
            "The biggest common CubeSat frame.", "EnduroSat store list price $18,200; mass est.");
        Sub("SmallSat Buses");
        Bus("sat.str.phl50", "50 kg Microsat Bus (Diwata class)", new Vector3(0.55f, 0.55f, 0.35f), 9f, 250000, 85f,
            "Honeycomb-panel box with the same 55 x 35 x 55 cm envelope as Diwata-1 (~50 kg when complete).",
            "Envelope: Diwata-1 (eoPortal); structure mass & cost est.");
        Bus("sat.str.100", "100 kg SmallSat Bus", new Vector3(0.6f, 0.8f, 0.6f), 18f, 500000, 220f,
            "Aluminum honeycomb bus for 100 kg-class missions.");
        Bus("sat.str.200", "200 kg SmallSat Bus", new Vector3(0.8f, 1.0f, 0.8f), 30f, 850000, 500f,
            "ESPA-class bus with room for big optics or radar.");
        Cyl("sat.str.cyl05", "Cylindrical Bus Ø0.5 m", 0.5f, 0.6f, 8f, 200000, 90f, "Spin-friendly cylinder bus for science missions.");
        Cyl("sat.str.cyl10", "Cylindrical Bus Ø1.0 m", 1.0f, 1.2f, 40f, 1200000, 700f, "Large cylinder bus for heavy instruments and propulsion.");
        Sub("Separation Systems");
        Sep("sat.sep.cubesat", "CubeSat Deployer Interface", true, 0.3f, 0.04f, 2.5f, 30000, 30f,
            "Spring-rail dispenser interface; pushes the CubeSat out at about 1-2 m/s.");
        Sep("sat.sep.lb8", "Motorized Lightband 8in", false, 0.25f, 0.05f, 0.6f, 55000, 60f, "Low-shock motorized separation ring for small satellites.");
        Sep("sat.sep.lb15", "Motorized Lightband 15in", false, 0.42f, 0.06f, 1.2f, 85000, 180f, "Low-shock separation ring for microsatellites.");
        Sep("sat.sep.lb24", "Motorized Lightband 24in", false, 0.66f, 0.07f, 2.5f, 120000, 500f, "Separation ring for 100-500 kg satellites.");
        Sep("sat.sep.clamp", "Clamp Band 937 mm", false, 1.0f, 0.1f, 20f, 250000, 2500f, "Marman clamp band for large satellites.");

        // ═════════════ PAYLOAD: POWER ═════════════
        Cat(CADBuilder.Payload, "Power");
        Sub("Solar Panels");
        Solar("sat.sol.1u", "1U Body Solar Panel", CADShape.Panel, new Vector3(0.1f, 0.1f, 0.006f), 2.3f, 0.05f, 3000,
            "Triple-junction cells on a 1U face (~2.3 W in full sun).", "EnduroSat $2,600-3,400; ~2.3 W/U typical");
        Solar("sat.sol.3u", "3U Body Solar Panel", CADShape.Panel, new Vector3(0.1f, 0.34f, 0.006f), 7f, 0.14f, 6450,
            "Body-mounted panel for a 3U face.", "EnduroSat $6,400-6,500; output est.");
        Solar("sat.sol.6u", "6U Body Solar Panel", CADShape.Panel, new Vector3(0.226f, 0.366f, 0.006f), 15f, 0.3f, 16200,
            "Body-mounted panel for a 6U face.", "EnduroSat $16,200; output est.");
        Solar("sat.sol.3ud", "3U Deployable Solar Array", CADShape.Wing, new Vector3(0.008f, 0.34f, 0.2f), 20f, 0.3f, 22100,
            "Hinged wing that unfolds after deployment. Wings average far more power than body panels.", "EnduroSat $22,100; output est.");
        Solar("sat.sol.6ud", "6U Deployable Solar Array", CADShape.Wing, new Vector3(0.008f, 0.366f, 0.45f), 36f, 0.68f, 48400,
            "Large CubeSat wing.", "EnduroSat $48,400; ~677 g per array; output est.");
        Solar("sat.sol.sm", "SmallSat Body Panel 0.5 m", CADShape.Panel, new Vector3(0.5f, 0.5f, 0.02f), 75f, 1.6f, 90000,
            "GaAs panel for microsat faces.", "est.");
        Solar("sat.sol.wing", "SmallSat Deployable Wing 1 x 2 m", CADShape.Wing, new Vector3(0.03f, 1.0f, 2.0f), 500f, 9f, 450000,
            "Sun-tracking wing for 100-200 kg satellites.", "est.");
        Solar("sat.sol.big", "Large Deployable Wing 2 x 4 m", CADShape.Wing, new Vector3(0.04f, 2.0f, 4.0f), 2000f, 32f, 1600000,
            "High-power wing for radar or electric propulsion.", "est.");
        Sub("Batteries & EPS");
        Inside("sat.eps.cube", "CubeSat EPS Board", CADRole.EPS, 0.2f, 12000, 0.15f, 0.2f, 1f,
            "Maximum-power-point tracking, regulators and switched outputs for every subsystem.");
        Inside("sat.bat.40", "CubeSat Battery 40 Wh", CADRole.SatBattery, 0.3f, 6000, 0.12f, 0f, 1f, "Li-ion pack for 1U-3U missions.").batteryWh = 40f;
        Inside("sat.bat.99", "CubeSat Battery 99 Wh (4S2P)", CADRole.SatBattery, 0.65f, 14000, 0.3f, 0f, 1f,
            "Larger Li-ion pack for 6U-16U missions.", "Capacity: EnduroSat EPS spec; price est.").batteryWh = 99f;
        Inside("sat.eps.pcdu", "SmallSat PCDU", CADRole.EPS, 4f, 250000, 4f, 8f, 1f, "Power conditioning and distribution unit for microsats.");
        Inside("sat.bat.1k", "SmallSat Li-ion 1 kWh", CADRole.SatBattery, 7f, 120000, 5f, 0f, 1f, "Microsat battery pack.").batteryWh = 1000f;
        Inside("sat.bat.3k", "SmallSat Li-ion 3.4 kWh", CADRole.SatBattery, 22f, 380000, 15f, 0f, 1f,
            "Battery sized like NanoAvionics' 500 kg-class bus.", "Capacity: NanoAvionics MP42D; mass & cost est.").batteryWh = 3400f;

        // ═════════════ PAYLOAD: COMMAND & DATA ═════════════
        Cat(CADBuilder.Payload, "Command & Data");
        Sub("Computers");
        Inside("sat.obc.cube", "CubeSat On-Board Computer", CADRole.OBC, 0.13f, 16000, 0.12f, 0.9f, 1f,
            "Runs flight software, stores telemetry, schedules imaging.", "~130 g, ~0.9 W, ~$16k (mission budgets)");
        Inside("sat.obc.rad", "Rad-Hard SmallSat Computer", CADRole.OBC, 3f, 350000, 4f, 12f, 1f, "Radiation-hardened processor for long missions.");
        Inside("sat.obc.mmu", "Mass Memory Unit 1 TB", CADRole.OBC, 1.5f, 120000, 1.5f, 6f, 1f, "Stores imagery until a ground pass.");
        Sub("Radios");
        Inside("sat.com.uhf", "UHF Transceiver", CADRole.Transceiver, 0.09f, 9000, 0.1f, 2.5f, 0.25f,
            "Command and telemetry link (9.6 kbps). Needs a UHF antenna.", "~90 g (mission budgets); price est.").dataRateKbps = 9.6f;
        Inside("sat.com.s", "S-band Transmitter", CADRole.Transceiver, 0.2f, 28000, 0.15f, 6f, 0.15f,
            "Mid-rate downlink for images (~4 Mbps).").dataRateKbps = 4000f;
        Inside("sat.com.x", "X-band SDR Transmitter", CADRole.Transceiver, 0.365f, 40000, 0.2f, 7.5f, 0.1f,
            "High-rate downlink for imaging CubeSats.", "~365 g, 7.5 W, ~$40k (mission budgets); rate est.").dataRateKbps = 100000f;
        Inside("sat.com.xhr", "SmallSat X-band High-Rate", CADRole.Transceiver, 3f, 400000, 3f, 40f, 0.1f,
            "Hundreds of Mbps for high-resolution imagery.").dataRateKbps = 800000f;
        Sub("Antennas");
        Outside("sat.ant.uhf", "UHF Deployable Antenna", CADRole.Antenna, CADShape.Rod, new Vector3(0.02f, 0.02f, 0.5f), 0.085f, 5500, 0f, 1f, Steel,
            "Tape-spring whip that unrolls in orbit.", "~85 g (mission budgets); price est.");
        Outside("sat.ant.s", "S-band Patch Antenna", CADRole.Antenna, CADShape.Panel, new Vector3(0.08f, 0.08f, 0.01f), 0.05f, 6000, 0f, 1f, Gold,
            "Flat patch antenna for S-band.");
        Outside("sat.ant.x", "X-band 4x4 Patch Array", CADRole.Antenna, CADShape.Panel, new Vector3(0.1f, 0.1f, 0.01f), 0.04f, 10000, 0f, 1f, Gold,
            "High-gain flat array for X-band.", "~$10k (mission budgets)");
        Outside("sat.ant.dish", "X-band Dish 0.5 m", CADRole.Antenna, CADShape.Dish, new Vector3(0.5f, 0.5f, 0.25f), 3f, 250000, 0f, 1f, White,
            "Parabolic high-gain antenna for big data volumes.");

        // ═════════════ PAYLOAD: ADCS ═════════════
        Cat(CADBuilder.Payload, "Attitude Control (ADCS)");
        Sub("Sensors");
        Inside("sat.adcs.sun", "Fine Sun Sensor", CADRole.ADCSSensor, 0.03f, 3250, 0.02f, 0.1f, 1f, "0.2° sun direction sensor.", "CubeSpace list price $3,250");
        Inside("sat.adcs.mag", "ADCS Magnetometer", CADRole.ADCSSensor, 0.05f, 2300, 0.02f, 0.1f, 1f, "Measures Earth's field for detumbling.", "CubeSpace list price $2,300");
        Inside("sat.adcs.cubestar", "CubeStar Star Tracker", CADRole.ADCSSensor, 0.055f, 15390, 0.06f, 0.3f, 1f,
            "Miniature star tracker (~55 g).", "CubeSpace list price $15,390");
        Inside("sat.adcs.twinkle", "Twinkle Star Tracker", CADRole.ADCSSensor, 0.06f, 45000, 0.06f, 0.5f, 1f,
            "High-accuracy CubeSat star tracker.", "satcatalog avg $40-50k; mass est.");
        Inside("sat.adcs.gnss", "Satellite GNSS Receiver", CADRole.ADCSSensor, 0.1f, 12000, 0.05f, 1f, 1f, "Position and precise time.");
        Inside("sat.adcs.st", "SmallSat Star Tracker", CADRole.ADCSSensor, 1.5f, 220000, 2f, 6f, 1f, "Arcsecond-class tracker for microsats.");
        Sub("Actuators");
        Inside("sat.adcs.mtq", "Magnetorquer Set (3 rods)", CADRole.ADCSActuator, 0.1f, 3700, 0.08f, 0.6f, 1f,
            "Pushes against Earth's magnetic field to dump momentum.", "Based on CubeSpace $870-1,650 per rod");
        Inside("sat.adcs.rws", "CubeWheel S Reaction Wheel", CADRole.ADCSActuator, 0.06f, 5170, 0.04f, 0.25f, 1f, "Small reaction wheel.", "CubeSpace $5,170, 60 g");
        Inside("sat.adcs.rwm", "CubeWheel M Reaction Wheel", CADRole.ADCSActuator, 0.15f, 7540, 0.07f, 0.5f, 1f, "Medium reaction wheel.", "CubeSpace $7,540, 150 g");
        Inside("sat.adcs.rwl", "CubeWheel L Reaction Wheel", CADRole.ADCSActuator, 0.225f, 8640, 0.11f, 0.8f, 1f, "Large CubeSat wheel.", "CubeSpace $8,640, 225 g");
        Inside("sat.adcs.rw0", "NanoAvionics RW0 Wheel", CADRole.ADCSActuator, 0.14f, 4450, 0.07f, 0.15f, 1f,
            "Sealed brushless wheel (vendor sells in sets of 4).", "EUR 4,100 starting price (~$4,450); mass est.");
        Inside("sat.adcs.rwbig", "SmallSat Reaction Wheel", CADRole.ADCSActuator, 1.1f, 65000, 1.2f, 3f, 1f, "0.1 Nms-class wheel for microsats.");
        Sub("Integrated ADCS");
        Inside("sat.adcs.3ax", "CubeADCS 3-Axis Small", CADRole.ADCSIntegrated, 0.35f, 37000, 0.35f, 1.2f, 1f,
            "Complete 3-axis package: computer, sensors, wheels and torquers.", "CubeSpace base price $37,000; mass est.");
        Inside("sat.adcs.ymom", "CubeADCS Y-Momentum", CADRole.ADCSIntegrated, 0.3f, 26780, 0.3f, 0.7f, 1f,
            "Nadir pointing with one biased wheel; cheaper, less agile.", "CubeSpace base price $26,780");

        // ═════════════ PAYLOAD: PROPULSION / THERMAL ═════════════
        Cat(CADBuilder.Payload, "Propulsion");
        Sub("Thrusters");
        var cg = Inside("sat.prop.cold", "Cold-Gas Thruster 0.5U", CADRole.SatPropulsion, 0.6f, 55000, 0.5f, 1f, 0.1f, "Simple butane/nitrogen thruster for small maneuvers.");
        cg.propellantKg = 0.1f; cg.ispVac = 65f; cg.propellant = CADPropellant.ColdGas;
        var ep = Inside("sat.prop.feep", "Electric Thruster 1U (FEEP)", CADRole.SatPropulsion, 0.8f, 140000, 1f, 25f, 0.2f, "Very efficient, very low thrust.");
        ep.propellantKg = 0.2f; ep.ispVac = 2000f; ep.propellant = CADPropellant.Electric;
        var hz = Inside("sat.prop.hydrazine", "Hydrazine Propulsion Module", CADRole.SatPropulsion, 10f, 1100000, 30f, 12f, 1f, "Monopropellant thrusters for microsats.");
        hz.propellantKg = 15f; hz.ispVac = 220f; hz.propellant = CADPropellant.Monoprop;
        var hall = Inside("sat.prop.hall", "Hall Thruster System 300 W", CADRole.SatPropulsion, 7f, 800000, 20f, 300f, 0.15f, "Xenon Hall thruster for orbit raising.");
        hall.propellantKg = 8f; hall.ispVac = 1400f; hall.propellant = CADPropellant.Electric;
        Cat(CADBuilder.Payload, "Thermal");
        Sub("Thermal Control");
        Inside("sat.th.mli", "CubeSat MLI Blanket Kit", CADRole.Thermal, 0.05f, 2500, 0.05f, 0f, 1f, "Multilayer insulation to hold temperatures steady.");
        Inside("sat.th.heat", "Kapton Heater Set", CADRole.Thermal, 0.04f, 1800, 0.02f, 1.5f, 0.3f, "Keeps the battery warm in eclipse.");
        Inside("sat.th.mlibig", "SmallSat MLI Blankets", CADRole.Thermal, 2.5f, 60000, 1f, 0f, 1f, "Gold-foil blankets for microsats.");
        Outside("sat.th.rad", "Radiator Panel 0.5 m", CADRole.Thermal, CADShape.Panel, new Vector3(0.5f, 0.5f, 0.012f), 1.8f, 45000, 0f, 1f, White,
            "White-painted panel that dumps electronics heat to space.");

        // ═════════════ PAYLOAD: SCIENTIFIC INSTRUMENTS ═════════════
        Cat(CADBuilder.Payload, "Scientific Instruments");
        Sub("Earth Observation");
        InstOut("sci.hpt", "High Precision Telescope (Diwata class)", CADShape.Camera, new Vector3(0.2f, 0.2f, 0.45f), 7f, 650000, 12f, 0.2f, 50000,
            "Few-meter resolution telescope for disaster damage assessment, as flown on Diwata-1/2.", "Heritage: Diwata-1; mass, power & cost est.");
        InstOut("sci.smi", "Multispectral Imager w/ LCTF (Diwata class)", CADShape.Camera, new Vector3(0.15f, 0.15f, 0.25f), 3f, 300000, 8f, 0.2f, 20000,
            "Tunable-filter imager, 80 m resolution, 420-1050 nm: vegetation and ocean color.", "Specs: Diwata-1 SMI (eoPortal); mass & cost est.");
        InstOut("sci.wfc", "Wide Field Camera", CADShape.Camera, new Vector3(0.06f, 0.06f, 0.08f), 0.3f, 15000, 1.5f, 0.3f, 2000,
            "7 km resolution, 180 x 134° view for watching typhoons.", "Specs: Diwata-1 WFC (eoPortal); mass & cost est.");
        InstOut("sci.mfc", "Middle Field Camera", CADShape.Camera, new Vector3(0.08f, 0.08f, 0.1f), 0.5f, 25000, 2f, 0.3f, 4000,
            "185 m color camera that helps locate high-resolution images.", "Specs: Diwata-1 MFC; mass & cost est.");
        InstIn("sci.mantis", "Mantis Imager (1U)", 0.5f, 150000, 0.63f, 6f, 0.2f, 30000,
            "RGB / multispectral / hyperspectral imager; 16 m at 500 km.", "Dragonfly Aerospace: 0.5 kg, 4-6 W, 16 m GSD; price est.");
        InstIn("sci.ms100", "MultiScape100 CIS", 1.2f, 280000, 2.2f, 5.8f, 0.2f, 40000,
            "Multispectral push-broom imager for 3U/6U CubeSats.", "satcatalog: 1.2 kg, 5.8 W; price est.");
        InstIn("sci.hs100", "HyperScape100 Hyperspectral", 1.2f, 380000, 2.2f, 7.5f, 0.2f, 60000,
            "Up to 32 hyperspectral bands; 4.75 m GSD at 500 km.", "Simera Sense / satcatalog: 1.2 kg, 7.5 W; price est.");
        InstOut("sci.monitor", "Monitor Imager (PAN + 4 MS)", CADShape.Camera, new Vector3(0.2f, 0.2f, 0.4f), 6f, 750000, 14f, 0.2f, 80000,
            "Higher-resolution multispectral camera for 50-100 kg satellites.", "Dragonfly Aerospace: 6 kg, 14 W; price est.");
        InstOut("sci.ms200", "MultiScape200 CIS", CADShape.Camera, new Vector3(0.3f, 0.3f, 0.6f), 12.1f, 1600000, 5.8f, 0.2f, 120000,
            "1.5 m resolution, 7 VNIR bands.", "Simera Sense / satcatalog: 12.1 kg, 5.8 W; price est.");
        InstIn("sci.tir", "Thermal Infrared Camera", 0.6f, 90000, 0.6f, 3f, 0.25f, 5000, "Microbolometer camera for fires, volcanoes and urban heat.");
        Sub("Space Science");
        InstOut("sci.mag", "Fluxgate Magnetometer on Boom", CADShape.Rod, new Vector3(0.03f, 0.03f, 1.0f), 0.4f, 35000, 0.6f, 1f, 2,
            "Measures Earth's magnetic field far from the spacecraft's own noise.");
        InstOut("sci.cip", "Compact Ionospheric Probe", CADShape.Box, new Vector3(0.1f, 0.1f, 0.1f), 1f, 70000, 12f, 0.3f, 50,
            "Measures ionospheric plasma density and temperature.", "Power/duty: CIP mission budget (12 W, 30%); mass & cost est.");
        InstIn("sci.dose", "Radiation Dosimeter", 0.15f, 18000, 0.1f, 0.4f, 1f, 1, "Maps the radiation environment along the orbit.");
        InstOut("sci.gamma", "Gamma / X-ray Burst Detector", CADShape.Box, new Vector3(0.2f, 0.2f, 0.15f), 2.5f, 320000, 5f, 1f, 200, "Watches for gamma-ray bursts and solar flares.");
        InstOut("sci.tele30", "30 cm Space Telescope", CADShape.Camera, new Vector3(0.4f, 0.4f, 0.9f), 28f, 4500000, 35f, 0.5f, 50000, "Astronomy telescope for a 100-200 kg satellite.");
        InstOut("sci.uvvis", "UV-Vis Spectrometer", CADShape.Camera, new Vector3(0.15f, 0.15f, 0.3f), 3.5f, 520000, 9f, 0.3f, 3000, "Measures ozone, NO2 and aerosols.");
        Sub("Radio & Tracking");
        InstIn("sci.ais", "AIS Ship-Tracking Receiver", 0.15f, 25000, 0.1f, 1f, 1f, 10, "Tracks ships across Philippine waters (MULA carries AIS).", "Payload type: PhilSA MULA; numbers est.");
        InstIn("sci.adsb", "ADS-B Aircraft Receiver", 0.15f, 25000, 0.1f, 1.5f, 1f, 10, "Tracks aircraft over oceans.", "Payload type: PhilSA MULA; numbers est.");
        InstIn("sci.aru", "Amateur Radio Unit", 0.3f, 12000, 0.2f, 3f, 0.2f, 10, "Store-and-forward ham radio for disaster comms (Diwata-2 heritage).", "Heritage: Diwata-2 ARU; numbers est.");
        InstIn("sci.gnssro", "GNSS Radio Occultation Receiver", 1.5f, 260000, 1.5f, 8f, 1f, 200, "Profiles atmospheric temperature and humidity for weather models.");
        InstIn("sci.iot", "IoT Store-and-Forward Receiver", 0.2f, 22000, 0.1f, 2f, 0.5f, 20, "Collects data from remote sensors (farms, rivers, buoys).");
        InstOut("sci.sar", "Compact X-band SAR", CADShape.Panel, new Vector3(1.5f, 0.5f, 0.08f), 45f, 9000000, 600f, 0.1f, 500000,
            "Radar that sees through clouds and at night. Needs a lot of power.");
        Sub("Weather");
        InstOut("sci.mwr", "Microwave Sounder", CADShape.Camera, new Vector3(0.4f, 0.4f, 0.5f), 16f, 3200000, 30f, 1f, 300, "Temperature and moisture profiles inside typhoons.");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  DESIGN DATA (what the player built). Positions are in meters.
// ═════════════════════════════════════════════════════════════════════════════
[Serializable]
public class CADLabel
{
    public string text = "ASCEND";
    public float angleDeg;
    public float height01 = 0.5f;
    public float sizeM = 0.4f;
    public Color color = Color.black;
    public bool vertical;
}

[Serializable]
public class CADPlaced
{
    public string partId;
    public int parent = -1;
    public CADLink link = CADLink.Root;
    public Vector3 pos;
    public Quaternion rot = Quaternion.identity;
    public Color color = Color.white;
    public int symGroup = -1;
    [Tooltip("Engines only: wrap the engine in a skirt so it is hidden.")]
    public bool shroud;
    public List<CADLabel> labels = new List<CADLabel>();
}

[Serializable]
public class CADDesign
{
    public string name = "Untitled";
    public CADBuilder builder;
    public List<CADPlaced> parts = new List<CADPlaced>();
    public int nextSymGroup = 1;

    public bool IsEmpty => parts.Count == 0;
    public CADDesign Clone() => JsonUtility.FromJson<CADDesign>(JsonUtility.ToJson(this));

    public void CollectSubtree(int i, HashSet<int> set)
    {
        if (i < 0 || !set.Add(i)) return;
        for (int j = 0; j < parts.Count; j++) if (parts[j].parent == i) CollectSubtree(j, set);
    }

    public void Shift(int root, Vector3 delta)
    {
        var set = new HashSet<int>();
        CollectSubtree(root, set);
        foreach (int i in set) parts[i].pos += delta;
    }

    public List<int> SymGroup(int i)
    {
        var r = new List<int> { i };
        int g = parts[i].symGroup;
        if (g >= 0) for (int j = 0; j < parts.Count; j++) if (j != i && parts[j].symGroup == g) r.Add(j);
        return r;
    }

    public void RemoveSet(HashSet<int> rm)
    {
        var map = new int[parts.Count];
        var keep = new List<CADPlaced>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (rm.Contains(i)) map[i] = -1;
            else { map[i] = keep.Count; keep.Add(parts[i]); }
        }
        foreach (var p in keep)
            if (p.parent >= 0) { p.parent = map[p.parent]; if (p.parent < 0) p.link = CADLink.Root; }
        parts = keep;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  STATS: mass, size, delta-v per stage, TWR, max payload, satellite power budget
// ═════════════════════════════════════════════════════════════════════════════
public class CADStageInfo
{
    public int index, engines;
    public float startMass, propMass, thrustkN, deltaV, burnTime;
}

public class CADStats
{
    public const float G0 = 9.80665f;

    public float wetMass, dryMass, propMass, height, width, stowedHeight, stowedWidth;
    public double partsValue;
    public int partCount;
    public Bounds bounds;
    // rocket
    public float payloadMass, liftoffThrustkN, liftoffTWR, deltaV, maxPayloadLEO, requiredDv;
    public readonly List<CADStageInfo> stages = new List<CADStageInfo>();
    public readonly List<string> engineIssues = new List<string>();
    // satellite
    public float powerGen, powerUse, batteryWh, batteryNeedWh, volumeUsed, volumeCap, downlinkKbps, satDeltaV;
    public int instruments;
    public readonly Dictionary<CADRole, int> roles = new Dictionary<CADRole, int>();

    public int Count(CADRole r) => roles.TryGetValue(r, out var n) ? n : 0;
    public bool Has(params CADRole[] rs) { foreach (var r in rs) if (Count(r) > 0) return true; return false; }

    // Folded-for-launch box: explicit stowed size, or automatic for solar wings and booms (folded flat against the body)
    public static Bounds StowedBounds(CADPart p)
    {
        var b = LocalBounds(p);
        Vector3 st = p.stowedSize;
        if (st == Vector3.zero)
        {
            if (p.shape == CADShape.Wing || p.shape == CADShape.Rod) st = new Vector3(p.boxSize.x, p.boxSize.y, Mathf.Min(p.boxSize.z, 0.08f));
            else return b;
        }
        bool outward = p.attach == CADAttach.Radial;
        return new Bounds(outward ? new Vector3(0f, 0f, st.z * 0.5f) : Vector3.zero, st);
    }

    public static Bounds LocalBounds(CADPart p)
    {
        switch (p.shape)
        {
            case CADShape.Box: return new Bounds(Vector3.zero, p.boxSize);
            case CADShape.Panel: case CADShape.Wing: case CADShape.Dish: case CADShape.Fin: case CADShape.GridFin:
            case CADShape.Leg: case CADShape.Rod: case CADShape.Camera:
                return new Bounds(new Vector3(0f, 0f, p.boxSize.z * 0.5f), p.boxSize);
            case CADShape.None: return new Bounds(Vector3.zero, Vector3.zero);
            case CADShape.Orbiter: { float sc = p.diameter / 5.2f; return new Bounds(new Vector3(0f, 0f, 3.65f * sc), new Vector3(23.8f * sc, p.length, 11.9f * sc)); }
            default: float r = p.MaxRadius; return new Bounds(Vector3.zero, new Vector3(2f * r, p.length, 2f * r));
        }
    }

    static CADStats Common(CADDesign d, AscendCADData data)
    {
        var s = new CADStats();
        bool any = false, anyS = false; var b = new Bounds(); var sb = new Bounds();
        foreach (var pl in d.parts)
        {
            var p = data.Get(pl.partId); if (p == null) continue;
            s.partCount++; s.wetMass += p.WetMassKg; s.dryMass += p.dryMassKg; s.propMass += p.propellantKg; s.partsValue += p.costUSD;
            s.roles[p.role] = s.Count(p.role) + 1;
            if (p.attach == CADAttach.Internal) continue;
            var lb = LocalBounds(p); var lsb = StowedBounds(p);
            for (int c = 0; c < 8; c++)
            {
                var k = new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1);
                var w = pl.pos + pl.rot * (lb.center + Vector3.Scale(lb.extents, k));
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                var ws = pl.pos + pl.rot * (lsb.center + Vector3.Scale(lsb.extents, k));
                if (!anyS) { sb = new Bounds(ws, Vector3.zero); anyS = true; } else sb.Encapsulate(ws);
            }
        }
        s.bounds = b; s.height = b.size.y; s.width = Mathf.Max(b.size.x, b.size.z);
        s.stowedHeight = sb.size.y; s.stowedWidth = Mathf.Max(sb.size.x, sb.size.z);
        return s;
    }

    public static Bounds DesignBounds(CADDesign d, AscendCADData data) => Common(d, data).bounds;

    // ───────────── Rocket ─────────────
    class Burn
    {
        public float mdot, vSum, vSumVac, prop, wet;
        public float V => mdot > 0f ? vSum / mdot : 0f;
        public float VVac => mdot > 0f && vSumVac > 0f ? vSumVac / mdot : V;
    }
    class StagePlan { public float wet, thrustSL; public int engines; public Burn core = new Burn(); public List<Burn> sides = new List<Burn>(); }

    public static bool IsCore(CADDesign d, int i)
    {
        int guard = 0;
        while (i >= 0 && guard++ < 10000)
        {
            var l = d.parts[i].link;
            if (l == CADLink.Root) return true;
            if (l != CADLink.Top && l != CADLink.Bottom) return false;
            i = d.parts[i].parent;
        }
        return false;
    }

    static int SideRoot(CADDesign d, int i)
    {
        int guard = 0;
        while (i >= 0 && guard++ < 10000) { if (d.parts[i].link == CADLink.RadialUpright) return i; i = d.parts[i].parent; }
        return -1;
    }

    static float Isp(CADPart p, bool firstStage) =>
        firstStage ? (p.ispSL > 0f ? (p.ispSL + p.ispVac) * 0.5f : p.ispVac * 0.5f) : p.ispVac;

    // requiredDv: delta-v the mission needs (default = low Earth orbit). maxPayloadLEO is the payload the rocket can carry for that.
    public static CADStats Rocket(CADDesign d, AscendCADData data, float payloadKg, float requiredDv = -1f)
    {
        var s = Common(d, data);
        s.payloadMass = payloadKg;
        s.requiredDv = requiredDv > 0f ? requiredDv : data.requiredDeltaV;
        int n = d.parts.Count;
        if (n == 0) return s;

        var P = new CADPart[n];
        var core = new bool[n]; var side = new int[n];
        for (int i = 0; i < n; i++) { P[i] = data.Get(d.parts[i].partId); core[i] = IsCore(d, i); side[i] = SideRoot(d, i); }
        // Crossfeed side parts (Shuttle orbiter) stay with the core and burn its propellant
        for (int i = 0; i < n; i++) if (side[i] >= 0 && P[side[i]] != null && P[side[i]].crossfeed) side[i] = -1;

        // Stages are split by decouplers in the core stack (bottom = stage 1)
        var dec = new List<float>();
        for (int i = 0; i < n; i++) if (core[i] && P[i] != null && P[i].role == CADRole.Decoupler) dec.Add(d.parts[i].pos.y);
        dec.Sort();
        int S = dec.Count + 1;
        var stageOf = new int[n];
        for (int i = 0; i < n; i++)
        {
            int a = i;
            while (!core[a] && d.parts[a].parent >= 0) a = d.parts[a].parent;
            float y = d.parts[a].pos.y; int st = 0;
            foreach (var v in dec) if (v < y - 1e-3f) st++;
            stageOf[i] = (P[i] != null && P[i].role == CADRole.Fairing) ? 0 : st; // fairing drops after stage 1
        }

        var plan = new List<StagePlan>();
        for (int k = 0; k < S; k++) plan.Add(new StagePlan());
        var coreProp = new Dictionary<(int, CADPropellant), float>();
        var sideProp = new Dictionary<(int, CADPropellant), float>();
        var sideBurn = new Dictionary<int, Burn>();

        for (int i = 0; i < n; i++)
        {
            var p = P[i]; if (p == null) continue;
            int k = stageOf[i];
            plan[k].wet += p.WetMassKg;
            if (side[i] >= 0)
            {
                if (!sideBurn.TryGetValue(side[i], out var b)) { b = new Burn(); sideBurn[side[i]] = b; plan[k].sides.Add(b); }
                b.wet += p.WetMassKg;
            }
            if (p.role == CADRole.Tank)
            {
                var dict = side[i] >= 0 ? sideProp : coreProp;
                var key = (side[i] >= 0 ? side[i] : k, p.propellant);
                dict.TryGetValue(key, out var v); dict[key] = v + p.propellantKg;
            }
        }

        var counted = new HashSet<(int, CADPropellant, bool)>();
        for (int i = 0; i < n; i++)
        {
            var p = P[i];
            if (p == null || (p.role != CADRole.Engine && p.role != CADRole.SolidMotor)) continue;
            int k = stageOf[i]; bool isSide = side[i] >= 0; bool first = k == 0;
            var burn = isSide ? sideBurn[side[i]] : plan[k].core;

            if (p.role == CADRole.SolidMotor)
            {
                float md = p.burnTimeS > 0f ? p.propellantKg / p.burnTimeS : 0f;
                burn.mdot += md; burn.vSum += md * Isp(p, first) * G0; burn.prop += p.propellantKg;
                if (first) plan[k].thrustSL += p.thrustSLkN > 0f ? p.thrustSLkN : 0f;
                plan[k].engines++;
                continue;
            }

            int owner = isSide ? side[i] : k;
            var pdict = isSide ? sideProp : coreProp;
            if (first && p.ispSL <= 0f) { s.engineIssues.Add($"{p.name} is a vacuum engine; it can't fire at sea level on stage 1."); continue; }
            if (!pdict.ContainsKey((owner, p.propellant))) { s.engineIssues.Add($"{p.name} (stage {k + 1}) has no {AscendReport.PropName(p.propellant)} tank feeding it."); continue; }

            float mdot = p.thrustVackN * 1000f / (Mathf.Max(1f, p.ispVac) * G0);
            burn.mdot += mdot; burn.vSum += mdot * Isp(p, first) * G0; burn.vSumVac += mdot * p.ispVac * G0;
            if (counted.Add((owner, p.propellant, isSide))) burn.prop += pdict[(owner, p.propellant)];
            if (first) plan[k].thrustSL += p.thrustSLkN;
            plan[k].engines++;
        }
        for (int k = 0; k < S; k++)
            if (plan[k].core.mdot <= 0f && coreProp.Keys.Any(x => x.Item1 == k))
                s.engineIssues.Add($"Stage {k + 1} has propellant but no working engine.");

        float liftoff = plan.Sum(x => x.wet) + payloadKg;
        s.liftoffThrustkN = plan[0].thrustSL;
        s.liftoffTWR = liftoff > 0f ? s.liftoffThrustkN * 1000f / (liftoff * G0) : 0f;
        s.deltaV = Simulate(plan, payloadKg, s.stages);
        s.requiredDv = requiredDv > 0f ? requiredDv : data.requiredDeltaV;
        s.maxPayloadLEO = MaxPayload(plan, s.requiredDv);
        return s;
    }

    static float Simulate(List<StagePlan> plan, float payload, List<CADStageInfo> info)
    {
        float total = 0f;
        for (int k = 0; k < plan.Count; k++)
        {
            var st = plan[k];
            float above = payload;
            for (int j = k + 1; j < plan.Count; j++) above += plan[j].wet;
            float m = st.wet + above, m0 = m, dv = 0f, t = 0f, coreLeft = st.core.prop;

            if (st.sides.Count > 0)   // boosters burn together with the core, then drop
            {
                float sideMdot = 0f, sideV = 0f, sideWet = 0f, tA = float.MaxValue;
                foreach (var b in st.sides) { sideMdot += b.mdot; sideV += b.vSum; sideWet += b.wet; if (b.mdot > 0f) tA = Mathf.Min(tA, b.prop / b.mdot); }
                if (st.core.mdot > 0f) tA = Mathf.Min(tA, coreLeft / st.core.mdot);
                if (tA == float.MaxValue) tA = 0f;
                float mdot = st.core.mdot + sideMdot;
                if (mdot > 0f && tA > 0f)
                {
                    float m1 = Mathf.Max(1f, m - mdot * tA);
                    dv += (st.core.vSum + sideV) / mdot * Mathf.Log(m / m1); m = m1; t += tA;
                }
                coreLeft -= st.core.mdot * tA;
                m -= Mathf.Max(0f, sideWet - sideMdot * tA);
            }
            if (coreLeft > 0f && st.core.mdot > 0f)
            {
                float m1 = Mathf.Max(1f, m - coreLeft);
                // After boosters separate the core is high in thin air, so it runs at vacuum efficiency
                float v = st.sides.Count > 0 ? st.core.VVac : st.core.V;
                dv += v * Mathf.Log(m / m1); t += coreLeft / st.core.mdot; m = m1;
            }
            total += dv;
            info?.Add(new CADStageInfo { index = k, startMass = m0, propMass = st.core.prop + st.sides.Sum(b => b.prop),
                deltaV = dv, burnTime = t, engines = st.engines, thrustkN = st.thrustSL });
        }
        return total;
    }

    static float MaxPayload(List<StagePlan> plan, float required)
    {
        if (Simulate(plan, 0f, null) < required) return 0f;
        float lo = 0f, hi = Mathf.Max(100f, plan.Sum(x => x.wet) * 0.1f);
        int guard = 0;
        while (Simulate(plan, hi, null) >= required && hi < 1e7f && guard++ < 40) hi *= 2f;
        for (int i = 0; i < 40; i++) { float mid = (lo + hi) * 0.5f; if (Simulate(plan, mid, null) >= required) lo = mid; else hi = mid; }
        return lo;
    }

    // ───────────── Satellite ─────────────
    public static CADStats Payload(CADDesign d, AscendCADData data)
    {
        var s = Common(d, data);
        float gen = 0f, use = 0f, prop = 0f, ispW = 0f;
        foreach (var pl in d.parts)
        {
            var p = data.Get(pl.partId); if (p == null) continue;
            if (p.role == CADRole.SolarPanel) gen += p.powerGenW * (p.shape == CADShape.Wing ? 0.85f : 0.4f); // wings track the Sun
            use += p.powerUseW * p.dutyCycle;
            s.batteryWh += p.batteryWh;
            if (p.role == CADRole.SatStructure) s.volumeCap += p.internalVolumeL;
            if (p.attach == CADAttach.Internal) s.volumeUsed += p.volumeL;
            if (p.role == CADRole.Transceiver) s.downlinkKbps = Mathf.Max(s.downlinkKbps, p.dataRateKbps);
            if (p.role == CADRole.Instrument) s.instruments++;
            if (p.role == CADRole.SatPropulsion && p.propellantKg > 0f) { prop += p.propellantKg; ispW += p.ispVac * p.propellantKg; }
        }
        s.powerGen = gen * data.sunlitFraction;
        s.powerUse = use;
        s.batteryNeedWh = use * 0.6f / 0.3f;  // ~36 min eclipse, max 30% depth of discharge
        if (prop > 0f && s.wetMass > prop) s.satDeltaV = ispW / prop * G0 * Mathf.Log(s.wetMass / (s.wetMass - prop));
        return s;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  REVIEW: the realism double-check (used for the data sheet and the email)
// ═════════════════════════════════════════════════════════════════════════════
public class CADReview
{
    public readonly List<string> passed = new List<string>(), warnings = new List<string>(), errors = new List<string>();
    public bool Approved => errors.Count == 0;

    void Check(bool ok, string pass, string fail, bool fatal = true)
    {
        if (ok) passed.Add(pass); else (fatal ? errors : warnings).Add(fail);
    }

    static float TopWidth(CADPart p) => p.IsBoxy ? Mathf.Max(p.boxSize.x, p.boxSize.z) : (p.shape == CADShape.NoseCone || p.shape == CADShape.Fairing ? 0.05f : p.TopDia);
    static float BottomWidth(CADPart p) => p.IsBoxy ? Mathf.Max(p.boxSize.x, p.boxSize.z) : p.diameter;

    public static CADReview Run(CADDesign rocket, CADDesign payload, AscendCADData data, out CADStats rs, out CADStats ps, string padId = null)
    {
        var rv = RunChecks(rocket, payload, data, out rs, out ps);
        if (rocket.IsEmpty) return rv;
        var pad = data.GetPad(padId ?? AscendEconomy.SelectedPad);
        if (pad == null || pad.isTransporter) { rv.errors.Add("No launch pad chosen. Pick one on the Launch Pad tab."); return rv; }
        var why = AscendPads.Problems(pad, rs, data);
        var lockWhy = AscendPads.Lock(pad, data);
        if (!AscendEconomy.OwnsPad(pad, data)) rv.errors.Add($"The {pad.name} hasn't been bought yet.");
        else if (lockWhy != null) rv.errors.Add(lockWhy);
        else if (why.Count > 0) foreach (var w in why) rv.errors.Add($"{pad.name}: {w}");
        else rv.passed.Add($"Launch pad: {pad.name} can handle this rocket.");
        return rv;
    }

    static CADReview RunChecks(CADDesign rocket, CADDesign payload, AscendCADData data, out CADStats rs, out CADStats ps)
    {
        var r = new CADReview();
        var mission = AscendMissionGuide.Mission;
        bool sky = mission == AscendSimMission.SkySensor;
        string mName = AscendMissionGuide.Title;
        float req = AscendMissionGuide.RequiredDeltaV(data);
        ps = payload != null && !payload.IsEmpty ? CADStats.Payload(payload, data) : null;
        rs = CADStats.Rocket(rocket, data, ps != null ? ps.wetMass : 0f, req);
        if (rocket.IsEmpty) { r.errors.Add("No launch vehicle was submitted."); return r; }

        // Propulsion
        r.Check(rs.stages.Count > 0 && rs.stages[0].engines > 0, "Stage 1 has engines.", "Stage 1 has no engines that can fire at sea level.");
        r.Check(rs.liftoffTWR >= data.minLiftoffTWR, $"Liftoff thrust-to-weight {rs.liftoffTWR:0.00} (min {data.minLiftoffTWR:0.0}).",
            $"Liftoff thrust-to-weight is {rs.liftoffTWR:0.00}; it must be at least {data.minLiftoffTWR:0.0} to climb off the pad.");
        r.Check(rs.liftoffTWR <= data.maxLiftoffTWR, "Liftoff acceleration within structural limits.",
            $"Liftoff thrust-to-weight {rs.liftoffTWR:0.00} is very high; g-loads exceed typical limits.", false);
        r.Check(rs.deltaV >= req, $"Delta-v {rs.deltaV:N0} m/s covers the {req:N0} m/s needed for {mName}.",
            $"Delta-v is {rs.deltaV:N0} m/s; {mName} needs about {req:N0} m/s including gravity and drag losses.");
        foreach (var e in rs.engineIssues) r.errors.Add(e);

        // Avionics
        r.Check(rs.Has(CADRole.FlightComputer), "Flight computer installed.", "No flight computer: the rocket can't guide itself.");
        if (sky) r.Check(rs.Has(CADRole.IMU) || rs.Has(CADRole.Aero), "Fins / IMU keep the rocket flying straight.", "No fins and no IMU: a small rocket needs fins (or an IMU) to fly straight.");
        else r.Check(rs.Has(CADRole.IMU), "Inertial measurement unit installed.", "No IMU: the rocket can't sense attitude or velocity.");
        r.Check(rs.Has(CADRole.Battery), "Avionics battery installed.", "No avionics battery.");
        r.Check(rs.Has(CADRole.FlightTermination), "Flight termination system installed.", "No flight termination system: range safety will not allow launch.", !sky);
        r.Check(rs.Has(CADRole.Telemetry), "Telemetry transmitter installed.", "No telemetry transmitter: the launch team would fly blind.", !sky);
        r.Check(rs.Has(CADRole.GNSS), "GNSS receiver installed.", "No GNSS receiver: tracking relies on the IMU alone.", false);
        r.Check(rs.Has(CADRole.PowerDistribution), "Power distribution unit installed.", "No power distribution unit.", false);
        r.Check(rs.Has(CADRole.Wiring), "Wiring harness included.", "No wiring harness kit listed.", false);

        // Aerodynamics + stack fit
        int top = -1; float topY = float.MinValue;
        for (int i = 0; i < rocket.parts.Count; i++)
        {
            var p = data.Get(rocket.parts[i].partId);
            if (p == null || !CADStats.IsCore(rocket, i)) continue;
            float y = rocket.parts[i].pos.y + p.StackHeight * 0.5f;
            if (y > topY) { topY = y; top = i; }
        }
        var tp = top >= 0 ? data.Get(rocket.parts[top].partId) : null;
        bool aeroTop = tp != null && (tp.role == CADRole.NoseCone || tp.role == CADRole.Fairing || tp.shape == CADShape.OgiveTank);
        r.Check(aeroTop, "Aerodynamic nose on top of the stack.", $"The top of the stack is a {tp?.name ?? "bare part"}: add a nose cone or fairing.", ps != null);

        for (int i = 0; i < rocket.parts.Count; i++)
        {
            var pl = rocket.parts[i];
            if (pl.parent < 0 || (pl.link != CADLink.Top && pl.link != CADLink.Bottom)) continue;
            var a = data.Get(rocket.parts[pl.parent].partId); var b = data.Get(pl.partId);
            if (a == null || b == null || a.role == CADRole.Engine || b.role == CADRole.Engine) continue;
            float da = pl.link == CADLink.Top ? TopWidth(a) : BottomWidth(a);
            float db = pl.link == CADLink.Top ? BottomWidth(b) : TopWidth(b);
            if (Mathf.Max(da, db) / Mathf.Max(0.01f, Mathf.Min(da, db)) > data.maxDiameterStepRatio)
                r.warnings.Add($"Size mismatch between {a.name} and {b.name}: use a diameter adapter.");
        }

        // Payload integration + satellite
        if (ps == null) { r.warnings.Add("No payload submitted: the vehicle will be reviewed as a test flight."); return r; }
        CADPart fairing = null, adapter = null;
        foreach (var pl in rocket.parts)
        {
            var p = data.Get(pl.partId); if (p == null) continue;
            if ((p.role == CADRole.Fairing || p.innerDiameter > 0f) && p.innerDiameter > 0f && fairing == null) fairing = p;
            if (p.role == CADRole.PayloadAdapter && (adapter == null || p.maxPayloadKg > adapter.maxPayloadKg)) adapter = p;
        }
        if (adapter == null && fairing != null && fairing.maxPayloadKg > 0f) adapter = fairing;   // built-in payload attach fitting
        r.Check(fairing != null, "Payload fairing installed.", "No payload fairing: the satellite would be destroyed by air loads.");
        if (fairing != null)
            r.Check(ps.stowedWidth <= fairing.innerDiameter && ps.stowedHeight <= fairing.innerHeight,
                $"Satellite folded for launch ({ps.stowedWidth:0.##} x {ps.stowedHeight:0.##} m) fits the fairing.",
                $"Satellite folded for launch is {ps.stowedWidth:0.##} m wide x {ps.stowedHeight:0.##} m tall; the fairing only fits Ø{fairing.innerDiameter:0.##} x {fairing.innerHeight:0.##} m.");
        r.Check(adapter != null, "Payload adapter installed.", "No payload adapter between the upper stage and the satellite.");
        if (adapter != null)
            r.Check(ps.wetMass <= adapter.maxPayloadKg, $"{adapter.name} rated for {adapter.maxPayloadKg:N0} kg.",
                $"{adapter.name} is only rated for {adapter.maxPayloadKg:N0} kg; satellite is {ps.wetMass:N0} kg.");
        r.Check(ps.wetMass <= rs.maxPayloadLEO, $"Rocket can carry the {ps.wetMass:N1} kg payload for {mName} (max {rs.maxPayloadLEO:N1} kg).",
            $"Payload is {ps.wetMass:N1} kg but this rocket can only carry {rs.maxPayloadLEO:N1} kg for {mName}.");

        if (sky)
        {
            // A CanSat is simple: it only flies for a few minutes
            r.Check(ps.Has(CADRole.SatStructure), "CanSat shell present.", "No CanSat shell / frame.");
            CADPart ej = null;
            foreach (var pl in payload.parts) { var p = data.Get(pl.partId); if (p != null && p.role == CADRole.Separation) ej = p; }
            r.Check(ej != null, "Ejection system installed.", "No ejection system: the CanSat can't leave the rocket.");
            if (ej != null) r.Check(ps.wetMass <= ej.maxPayloadKg, $"{ej.name} rated for {ej.maxPayloadKg:N1} kg.", $"{ej.name} is rated for {ej.maxPayloadKg:N1} kg; CanSat is {ps.wetMass:N2} kg.");
            r.Check(ps.Has(CADRole.Recovery), "Parachute packed.", "No parachute: the CanSat would crash.");
            r.Check(ps.Has(CADRole.OBC), "Computer board installed.", "No computer board to read the sensors.");
            r.Check(ps.Has(CADRole.Transceiver) && ps.Has(CADRole.Antenna), "Radio + antenna installed.", "No radio link (needs a radio AND an antenna).");
            r.Check(ps.batteryWh >= ps.powerUse * 0.5f, $"Battery {ps.batteryWh:0.#} Wh lasts the flight.", $"Battery {ps.batteryWh:0.#} Wh is too small; needs {ps.powerUse * 0.5f:0.#} Wh for 30 minutes.");
            r.Check(ps.instruments > 0, $"{ps.instruments} sensor(s) on board.", "No sensor: the CanSat has nothing to measure.");
            r.Check(ps.volumeUsed <= ps.volumeCap + 1e-3f, $"Inside volume {ps.volumeUsed:0.###}/{ps.volumeCap:0.###} L.", $"Inside parts need {ps.volumeUsed:0.###} L but the shell only holds {ps.volumeCap:0.###} L.");
            return r;
        }

        r.Check(ps.Has(CADRole.SatStructure), "Satellite structure present.", "Satellite has no structure.");
        CADPart sep = null;
        foreach (var pl in payload.parts) { var p = data.Get(pl.partId); if (p != null && p.role == CADRole.Separation) sep = p; }
        r.Check(sep != null, "Separation system installed.", "No separation system: the satellite can't leave the rocket.");
        if (sep != null) r.Check(ps.wetMass <= sep.maxPayloadKg, $"{sep.name} rated for {sep.maxPayloadKg:N0} kg.",
            $"{sep.name} is rated for {sep.maxPayloadKg:N0} kg; satellite is {ps.wetMass:N1} kg.");
        r.Check(ps.Has(CADRole.EPS), "Power system (EPS) installed.", "No EPS / power distribution in the satellite.");
        r.Check(ps.Has(CADRole.SolarPanel), "Solar panels installed.", "No solar panels.");
        r.Check(ps.powerGen >= ps.powerUse * data.powerMargin, $"Power positive: {ps.powerGen:0.#} W generated vs {ps.powerUse:0.#} W used.",
            $"Power negative: {ps.powerGen:0.#} W orbit-average generation vs {ps.powerUse:0.#} W needed (+{(data.powerMargin - 1f) * 100f:0}% margin).");
        r.Check(ps.batteryWh >= ps.batteryNeedWh, $"Battery {ps.batteryWh:0} Wh covers eclipse ({ps.batteryNeedWh:0} Wh needed).",
            $"Battery {ps.batteryWh:0} Wh is too small for eclipse; needs {ps.batteryNeedWh:0} Wh.");
        r.Check(ps.Has(CADRole.OBC), "On-board computer installed.", "No on-board computer.");
        r.Check(ps.Has(CADRole.Transceiver) && ps.Has(CADRole.Antenna), "Radio + antenna installed.", "No radio link (needs a transceiver AND an antenna).");
        r.Check(ps.Has(CADRole.ADCSIntegrated) || (ps.Has(CADRole.ADCSActuator) && ps.Has(CADRole.ADCSSensor)),
            "Attitude control (sensors + actuators).", "No attitude control: add an integrated ADCS, or sensors plus wheels/torquers.");
        r.Check(ps.Has(CADRole.Thermal), "Thermal control installed.", "No thermal control: electronics may overheat or freeze.", false);
        r.Check(ps.instruments > 0, $"{ps.instruments} scientific instrument(s) on board.", "No scientific instrument: the satellite has no mission.");
        r.Check(ps.volumeUsed <= ps.volumeCap + 1e-3f, $"Internal volume {ps.volumeUsed:0.##}/{ps.volumeCap:0.##} L.",
            $"Internal components need {ps.volumeUsed:0.##} L but structures only hold {ps.volumeCap:0.##} L.");
        if (mission == AscendSimMission.MoonMapper)
            r.Check(ps.satDeltaV >= AscendMissionGuide.MoonBrakeDv, $"Satellite propulsion {ps.satDeltaV:N0} m/s can brake into orbit around the Moon.",
                $"Satellite propulsion is only {ps.satDeltaV:N0} m/s; braking into orbit around the Moon needs about {AscendMissionGuide.MoonBrakeDv:N0} m/s. Add hydrazine modules.");
        return r;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  ECONOMY: government fund, receipts, owned-parts inventory
//  Paid parts go to inventory, so a rejected design can be rebuilt without paying twice.
// ═════════════════════════════════════════════════════════════════════════════
[Serializable]
public class CADReceiptLine
{
    public string partId, name;
    public int qty, owned;
    public double unit;
    public double Charged => Math.Max(0, qty - owned) * unit;
}

[Serializable]
public class CADReceipt
{
    public string number, date;
    public List<CADReceiptLine> lines = new List<CADReceiptLine>();
    public double parts, ait, testing, total;
}

public static class AscendEconomy
{
    public static double Balance { get; private set; }
    public static readonly Dictionary<string, int> Inventory = new Dictionary<string, int>();
    public static readonly List<CADReceipt> Receipts = new List<CADReceipt>();
    public static event Action Changed;

    static bool ready;
    static AscendCADData cfg;
    static int receiptNo = 1;
    const string SaveKey = "AscendCAD.Economy";

    [Serializable] class SaveData
    {
        public double balance; public int receiptNo; public List<string> ids = new List<string>(); public List<int> counts = new List<int>();
        public List<string> pads = new List<string>(); public string selectedPad;
    }

    // Launch pads are infrastructure: bought once, never used up
    public static readonly HashSet<string> OwnedPads = new HashSet<string>();
    public static string SelectedPad { get; private set; }

    public static bool OwnsPad(CADLaunchPad p, AscendCADData d) => p != null && (p.ownedFromStart || OwnedPads.Contains(p.id));

    public static bool BuyPad(CADLaunchPad p, AscendCADData d)
    {
        if (p == null || OwnsPad(p, d) || p.costUSD > Balance + 0.01) return false;
        Balance -= p.costUSD;
        OwnedPads.Add(p.id);
        Receipts.Add(new CADReceipt
        {
            number = "AP-" + DateTime.Now.ToString("yyMMdd") + "-" + (receiptNo++).ToString("000"), date = DateTime.Now.ToString("MMM d, yyyy h:mm tt"),
            lines = new List<CADReceiptLine> { new CADReceiptLine { partId = p.id, name = p.name, qty = 1, unit = p.costUSD } },
            parts = p.costUSD, total = p.costUSD
        });
        if (!p.isTransporter && string.IsNullOrEmpty(SelectedPad)) SelectedPad = p.id;
        Save(); Changed?.Invoke();
        return true;
    }

    public static void ChoosePad(string id) { SelectedPad = id; Save(); Changed?.Invoke(); }

    static double configuredFund;
    static double FundOf(AscendCADData d) => d.sandboxMode ? 1e13 : d.governmentFundUSD;

    public static void Init(AscendCADData d)
    {
        if (d == null) return;
        if (ready) { SyncFund(d); return; }
        ready = true; cfg = d;
        configuredFund = FundOf(d);
        Balance = configuredFund;
        if (d.saveProgress) Load();
        if (string.IsNullOrEmpty(SelectedPad) && d.launchPads != null)          // start on the free pad
            foreach (var p in d.launchPads) if (p != null && p.ownedFromStart && !p.isTransporter) { SelectedPad = p.id; break; }
    }

    // Editing the Government Fund (or Sandbox Mode) on the asset applies right away, even during Play.
    // Money already spent stays spent: the balance moves by the difference.
    public static void SyncFund(AscendCADData d)
    {
        if (!ready || d == null) return;
        cfg = d;
        double f = FundOf(d);
        if (Math.Abs(f - configuredFund) < 0.5) return;
        Balance = Math.Max(0, Balance + (f - configuredFund));
        configuredFund = f;
        Save(); Changed?.Invoke();
    }

    // Fresh start every Play, even with "Enter Play Mode Options" (no domain reload) turned on
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ready = false; cfg = null; Balance = 0; configuredFund = 0; receiptNo = 1;
        Inventory.Clear(); Receipts.Clear(); OwnedPads.Clear(); SelectedPad = null;
    }

    public static int Owned(string id) => Inventory.TryGetValue(id, out var n) ? n : 0;

    public static Dictionary<string, int> Count(params CADDesign[] ds)
    {
        var c = new Dictionary<string, int>();
        foreach (var d in ds)
            if (d != null) foreach (var p in d.parts) { c.TryGetValue(p.partId, out var n); c[p.partId] = n + 1; }
        return c;
    }

    public static CADReceipt Quote(AscendCADData data, params CADDesign[] ds) => QuoteCounts(data, Count(ds));

    public static CADReceipt QuoteCounts(AscendCADData data, Dictionary<string, int> counts)
    {
        var r = new CADReceipt
        {
            number = "AP-" + DateTime.Now.ToString("yyMMdd") + "-" + receiptNo.ToString("000"),
            date = DateTime.Now.ToString("MMM d, yyyy h:mm tt")
        };
        foreach (var kv in counts)
        {
            var p = data.Get(kv.Key); if (p == null) continue;
            r.lines.Add(new CADReceiptLine { partId = kv.Key, name = p.name, qty = kv.Value, owned = Mathf.Min(Owned(kv.Key), kv.Value), unit = p.costUSD });
        }
        r.lines.Sort((a, b) => b.Charged.CompareTo(a.Charged));
        r.parts = r.lines.Sum(l => l.Charged);
        r.ait = r.parts * data.assemblyIntegrationRate;
        r.testing = r.parts * data.testingQARate;
        r.total = r.parts + r.ait + r.testing;
        return r;
    }

    public static bool Pay(CADReceipt r)
    {
        if (r.total > Balance + 0.01) return false;
        Balance -= r.total;
        foreach (var l in r.lines) { int buy = l.qty - l.owned; if (buy > 0) Inventory[l.partId] = Owned(l.partId) + buy; }
        Receipts.Add(r); receiptNo++;
        Save(); Changed?.Invoke();
        return true;
    }

    public static bool Covered(params CADDesign[] ds)
    {
        foreach (var kv in Count(ds)) if (Owned(kv.Key) < kv.Value) return false;
        return true;
    }

    // The approved vehicle is built from inventory
    public static void Consume(params CADDesign[] ds)
    {
        foreach (var kv in Count(ds)) Inventory[kv.Key] = Mathf.Max(0, Owned(kv.Key) - kv.Value);
        Save(); Changed?.Invoke();
    }

    static void Save()
    {
        if (cfg == null || !cfg.saveProgress) return;
        var s = new SaveData { balance = Balance, receiptNo = receiptNo };
        foreach (var kv in Inventory) { s.ids.Add(kv.Key); s.counts.Add(kv.Value); }
        s.pads.AddRange(OwnedPads); s.selectedPad = SelectedPad;
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(s)); PlayerPrefs.Save();
    }

    static void Load()
    {
        if (!PlayerPrefs.HasKey(SaveKey)) return;
        var s = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
        if (s == null) return;
        Balance = s.balance; receiptNo = Mathf.Max(1, s.receiptNo);
        Inventory.Clear();
        for (int i = 0; i < s.ids.Count && i < s.counts.Count; i++) Inventory[s.ids[i]] = s.counts[i];
        OwnedPads.Clear();
        if (s.pads != null) foreach (var id in s.pads) OwnedPads.Add(id);
        if (!string.IsNullOrEmpty(s.selectedPad)) SelectedPad = s.selectedPad;
    }

    public static void DeleteSave() { PlayerPrefs.DeleteKey(SaveKey); Debug.Log("AscendCAD: saved progress deleted."); }
}

// ═════════════════════════════════════════════════════════════════════════════
//  PHMAIL: in-game email (the calculator will reuse this too)
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendMail
{
    [Serializable]
    public class Message
    {
        public int id;
        public string from, to, subject, body, time;
        public bool read, outgoing, approved;
    }

    public static readonly List<Message> All = new List<Message>();
    public static event Action Changed;
    public static event Action<Message> Received;
    public static int Unread => All.Count(m => !m.outgoing && !m.read);

    static int next = 1;
    static bool welcomed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); next = 1; welcomed = false; }

    static Message Make(string from, string to, string subject, string body) =>
        new Message { id = next++, from = from, to = to, subject = subject, body = body, time = DateTime.Now.ToString("MMM d, h:mm tt") };

    public static Message Send(string from, string to, string subject, string body)
    {
        var m = Make(from, to, subject, body); m.outgoing = true; m.read = true;
        All.Add(m); Changed?.Invoke();
        return m;
    }

    public static Message Deliver(string from, string to, string subject, string body, bool approved = false)
    {
        var m = Make(from, to, subject, body); m.approved = approved;
        All.Add(m); Received?.Invoke(m); Changed?.Invoke();
        return m;
    }

    public static void MarkRead(Message m) { if (m != null && !m.read) { m.read = true; Changed?.Invoke(); } }

    public static void EnsureWelcome(AscendCADData d)
    {
        if (welcomed) return;
        welcomed = true;
        Deliver(d.agencyEmail, d.playerEmail, "Welcome to Ascend Philippines - your mission fund",
            "Good day, design team!\n\n" +
            $"The government has approved a launch program fund of <b>{AscendReport.MoneyFull(d.governmentFundUSD)}</b>.\n\n" +
            "<b>How it works</b>\n" +
            "1. Design a launch vehicle in <b>AscendCAD > Rocket Builder</b> and a satellite in <b>Payload Builder</b>. Use Googel for research.\n" +
            "2. Press <b>SUBMIT</b>: review the receipt and pay. Paid parts stay in your inventory.\n" +
            "3. Check the data sheet, then send the design to this address.\n" +
            "4. Our engineers double-check the realism. If approved, open <b>Ascend Mission Simulator</b> to plan the trajectory.\n" +
            "5. Send the trajectory to Mission Control. When it is CONFIRMED, the rocket is built on the launch pad, ready to fly.\n\n" +
            "Minimum requirements: reach low Earth orbit, range safety (flight termination), a working satellite with power, comms, attitude control and a scientific instrument.\n\n" +
            "Mabuhay and good luck!\n- Ascend Philippines Review Board");
    }
}

// Runs delayed actions even while the computer is closed
public class AscendRunner : MonoBehaviour
{
    static AscendRunner inst;
    readonly List<(float t, Action a)> jobs = new List<(float, Action)>();

    public static void After(float seconds, Action a)
    {
        if (!inst)
        {
            var go = new GameObject("AscendRunner");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<AscendRunner>();
        }
        inst.jobs.Add((Time.unscaledTime + seconds, a));
    }

    void Update()
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime < jobs[i].t) continue;
            var a = jobs[i].a; jobs.RemoveAt(i);
            try { a?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  MISSION CONTROL: submit -> email -> review -> APPROVED design waits for the
//  Ascend Mission Simulator; a CONFIRMED trajectory builds it on the launch pad
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendMissionControl
{
    public static event Action<AscendMail.Message, CADReview> ReviewCompleted;
    public static bool Pending { get; private set; }

    // The latest APPROVED design, waiting for its trajectory to be confirmed in the Ascend Mission Simulator
    public static CADDesign ApprovedRocket { get; private set; }
    public static CADDesign ApprovedPayload { get; private set; }
    public static CADStats ApprovedStats { get; private set; }
    public static string ApprovedPad { get; private set; }
    public static bool HasApproved => ApprovedRocket != null && !ApprovedRocket.IsEmpty;
    public static event Action DesignApproved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Pending = false;
        ApprovedRocket = ApprovedPayload = null; ApprovedStats = null; ApprovedPad = null;
    }

    // Called by the Ascend Mission Simulator when Mission Control confirms the trajectory: build the rocket on the pad
    public static GameObject BuildApproved(AscendCADData data)
    {
        if (!HasApproved) return null;
        var go = AscendLaunchSite.Spawn(data, ApprovedRocket.Clone(), ApprovedPayload?.Clone(), ApprovedStats, ApprovedPad);
        ApprovedRocket = ApprovedPayload = null; ApprovedStats = null;   // one approval = one rocket (its parts are used up)
        return go;
    }

    public static void Submit(AscendCADData data, CADDesign rocket, CADDesign payload)
    {
        var r = rocket.Clone();
        var p = payload != null && !payload.IsEmpty ? payload.Clone() : null;
        string pad = AscendEconomy.SelectedPad;
        CADReview.Run(r, p, data, out var rs0, out var ps0, pad);
        AscendMail.Send(data.playerEmail, data.agencyEmail,
            "Design review request: " + r.name + (p != null ? " + " + p.name : ""),
            "Hello Ascend Philippines review board,\n\nPlease review our launch vehicle and payload design.\n\n" +
            AscendReport.DataSheet(r, p, rs0, ps0, data) + "\n\nThank you,\nDesign Team");
        Pending = true;

        AscendRunner.After(data.reviewDelaySeconds, () =>
        {
            Pending = false;
            var rv = CADReview.Run(r, p, data, out var rs, out var ps, pad);   // the board's independent double-check
            if (rv.Approved && !AscendEconomy.Covered(r, p))
                rv.errors.Add("Some parts in this design were never paid for. Submit again to settle the receipt.");
            var msg = AscendMail.Deliver(data.agencyEmail, data.playerEmail,
                (rv.Approved ? "APPROVED: " : "REJECTED: ") + r.name, AscendReport.ReviewEmail(r, p, rv, rs, ps, data), rv.Approved);
            if (rv.Approved)
            {
                AscendEconomy.Consume(r, p);
                ApprovedRocket = r; ApprovedPayload = p; ApprovedStats = rs; ApprovedPad = pad;   // built after the trajectory is confirmed
                DesignApproved?.Invoke();
            }
            ReviewCompleted?.Invoke(msg, rv);
        });
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  REPORT TEXT (TextMeshPro rich text)
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendReport
{
    const string GoodC = "#3FB950", BadC = "#F85149", WarnC = "#D29922", SubC = "#9AA0A6";

    public static string Money(double v) =>
        v >= 1e9 ? $"${v / 1e9:0.##}B" : v >= 1e6 ? $"${v / 1e6:0.##}M" : v >= 1e3 ? $"${v / 1e3:0.#}K" : $"${v:0}";
    public static string MoneyFull(double v) => "$" + v.ToString("N0");
    public static string Mass(float kg) => kg >= 1000f ? $"{kg / 1000f:0.##} t" : kg >= 1f ? $"{kg:0.##} kg" : $"{kg * 1000f:0} g";
    public static string Rate(float kbps) => kbps >= 1000f ? $"{kbps / 1000f:0.#} Mbps" : $"{kbps:0.#} kbps";
    static string C(string hex, string s) => $"<color={hex}>{s}</color>";
    static void Line(StringBuilder sb, string label, string value) => sb.Append(C(SubC, label + ": ")).Append(value).Append('\n');

    public static string PropName(CADPropellant p)
    {
        switch (p)
        {
            case CADPropellant.Kerolox: return "RP-1/LOX";
            case CADPropellant.Hydrolox: return "LH2/LOX";
            case CADPropellant.Methalox: return "CH4/LOX";
            case CADPropellant.Hypergolic: return "MMH/NTO";
            case CADPropellant.Solid: return "solid";
            case CADPropellant.ColdGas: return "cold gas";
            case CADPropellant.Electric: return "electric";
            case CADPropellant.Monoprop: return "hydrazine";
            default: return "-";
        }
    }

    static string Mounting(CADPart p)
    {
        switch (p.attach)
        {
            case CADAttach.Internal: return "Installs inside a structure / bay";
            case CADAttach.Radial: return "Side mount (symmetry works)";
            case CADAttach.RadialUpright: return "Strap-on booster (side mount)";
            default: return p.role == CADRole.Engine ? "Stack (symmetry > 1 under a tank = engine cluster)" : "Stack (top / bottom)";
        }
    }

    public static string PartInfo(CADPart p)
    {
        if (p == null) return "";
        var sb = new StringBuilder();
        sb.Append(C(SubC, p.category + " / " + p.subcategory)).Append("\n\n");
        if (!string.IsNullOrEmpty(p.description)) sb.Append(p.description).Append("\n\n");
        Line(sb, "Cost", Money(p.costUSD) + (p.costEstimated ? C(SubC, " (est.)") : ""));
        Line(sb, "Mass", p.propellantKg > 0f ? $"{Mass(p.WetMassKg)} wet / {Mass(p.dryMassKg)} dry" : Mass(p.dryMassKg));
        if (p.propellantKg > 0f) Line(sb, "Propellant", $"{Mass(p.propellantKg)} {PropName(p.propellant)}");
        if (p.thrustVackN > 0f) Line(sb, "Thrust", p.thrustSLkN > 0f ? $"{p.thrustSLkN:N0} kN sea level / {p.thrustVackN:N0} kN vac" : $"{p.thrustVackN:N1} kN vac only");
        if (p.ispVac > 0f) Line(sb, "Isp", p.ispSL > 0f ? $"{p.ispSL:0} s SL / {p.ispVac:0} s vac" : $"{p.ispVac:0} s vac");
        if (p.burnTimeS > 0f) Line(sb, "Burn time", $"{p.burnTimeS:0} s");
        if (p.role == CADRole.Engine) Line(sb, "Burns", PropName(p.propellant));
        if (p.attach != CADAttach.Internal)
            Line(sb, "Size", p.IsBoxy || p.attach == CADAttach.Radial
                ? $"{p.boxSize.x:0.###} x {p.boxSize.y:0.###} x {p.boxSize.z:0.###} m"
                : (p.topDiameter > 0f ? $"Ø{p.diameter:0.##} to Ø{p.topDiameter:0.##} m, {p.length:0.##} m tall" : $"Ø{p.diameter:0.##} m x {p.length:0.##} m"));
        if (p.powerGenW > 0f) Line(sb, "Power output", $"{p.powerGenW:0.#} W in full sun");
        if (p.powerUseW > 0f) Line(sb, "Power draw", $"{p.powerUseW:0.##} W ({p.dutyCycle * 100f:0}% duty)");
        if (p.batteryWh > 0f) Line(sb, "Capacity", $"{p.batteryWh:N0} Wh");
        if (p.dataRateKbps > 0f) Line(sb, "Data", Rate(p.dataRateKbps));
        if (p.volumeL > 0f) Line(sb, "Volume", $"{p.volumeL:0.##} L");
        if (p.internalVolumeL > 0f) Line(sb, "Internal space", $"{p.internalVolumeL:0.##} L");
        if (p.maxPayloadKg > 0f) Line(sb, "Max payload", Mass(p.maxPayloadKg));
        if (p.innerDiameter > 0f) Line(sb, "Usable envelope", $"Ø{p.innerDiameter:0.##} m x {p.innerHeight:0.##} m");
        Line(sb, "Mounting", Mounting(p));
        if (!string.IsNullOrEmpty(p.source)) sb.Append("\n<size=85%>").Append(C(SubC, "Source: " + p.source)).Append("</size>");
        return sb.ToString();
    }

    public static string RocketStats(CADStats s, CADStats ps, AscendCADData d)
    {
        var sb = new StringBuilder();
        if (s.partCount == 0) return C(SubC, "No rocket parts yet. Start with a tank or engine.");
        Line(sb, "Parts", $"{s.partCount}   Height {s.height:0.#} m   Width {s.width:0.##} m");
        Line(sb, "Liftoff mass", Mass(s.wetMass + s.payloadMass) + (s.payloadMass > 0f ? C(SubC, $" (payload {Mass(s.payloadMass)})") : ""));
        Line(sb, "Propellant / dry", $"{Mass(s.propMass)} / {Mass(s.dryMass)}");
        Line(sb, "Liftoff thrust", $"{s.liftoffThrustkN:N0} kN");
        bool twrOk = s.liftoffTWR >= d.minLiftoffTWR && s.liftoffTWR <= d.maxLiftoffTWR;
        Line(sb, "Thrust-to-weight", C(twrOk ? GoodC : BadC, $"{s.liftoffTWR:0.00}") + C(SubC, $" (need {d.minLiftoffTWR:0.0}-{d.maxLiftoffTWR:0})"));
        Line(sb, "Total delta-v", C(s.deltaV >= s.requiredDv ? GoodC : BadC, $"{s.deltaV:N0} m/s") + C(SubC, $" / {s.requiredDv:N0} for {AscendMissionGuide.Title}"));
        Line(sb, "Max payload (mission)", s.maxPayloadLEO > 0f ? Mass(s.maxPayloadLEO) : C(BadC, "not enough push for this mission"));
        foreach (var st in s.stages)
            sb.Append(C(SubC, $"  Stage {st.index + 1}: ")).Append($"{st.deltaV:N0} m/s, {st.engines} engine(s), {st.burnTime:0} s burn\n");
        foreach (var e in s.engineIssues) sb.Append(C(BadC, "  ! " + e)).Append('\n');
        Line(sb, "Parts value", Money(s.partsValue));
        return sb.ToString();
    }

    public static string PayloadStats(CADStats s, CADStats rs, AscendCADData d)
    {
        var sb = new StringBuilder();
        if (s == null || s.partCount == 0) return C(SubC, "No satellite parts yet. Start with a CubeSat frame or a bus.");
        Line(sb, "Parts", $"{s.partCount}   Size {s.width:0.##} x {s.height:0.##} m");
        if (Mathf.Abs(s.stowedWidth - s.width) > 0.01f || Mathf.Abs(s.stowedHeight - s.height) > 0.01f)
            Line(sb, "Folded for launch", $"{s.stowedWidth:0.##} x {s.stowedHeight:0.##} m");
        Line(sb, "Mass", Mass(s.wetMass));
        bool pOk = s.powerGen >= s.powerUse * d.powerMargin;
        Line(sb, "Power (orbit avg)", C(pOk ? GoodC : BadC, $"{s.powerGen:0.#} W made") + $" / {s.powerUse:0.#} W used");
        Line(sb, "Battery", C(s.batteryWh >= s.batteryNeedWh ? GoodC : BadC, $"{s.batteryWh:0} Wh") + C(SubC, $" (eclipse needs {s.batteryNeedWh:0} Wh)"));
        Line(sb, "Internal volume", C(s.volumeUsed <= s.volumeCap ? GoodC : BadC, $"{s.volumeUsed:0.##}/{s.volumeCap:0.##} L"));
        Line(sb, "Downlink", s.downlinkKbps > 0f ? Rate(s.downlinkKbps) : C(BadC, "none"));
        Line(sb, "Instruments", s.instruments > 0 ? s.instruments.ToString() : C(BadC, "none"));
        if (s.satDeltaV > 0f) Line(sb, "Propulsion delta-v", $"{s.satDeltaV:0} m/s");
        string Sys(string n, bool ok) => C(ok ? GoodC : BadC, (ok ? "[OK] " : "[--] ") + n) + "  ";
        sb.Append(Sys("Structure", s.Has(CADRole.SatStructure))).Append(Sys("Separation", s.Has(CADRole.Separation)))
          .Append(Sys("EPS", s.Has(CADRole.EPS))).Append(Sys("Computer", s.Has(CADRole.OBC))).Append('\n')
          .Append(Sys("Radio", s.Has(CADRole.Transceiver) && s.Has(CADRole.Antenna)))
          .Append(Sys("ADCS", s.Has(CADRole.ADCSIntegrated) || (s.Has(CADRole.ADCSActuator) && s.Has(CADRole.ADCSSensor))))
          .Append(Sys("Thermal", s.Has(CADRole.Thermal))).Append('\n');
        if (rs != null && rs.partCount > 0)
            Line(sb, "Rocket can lift", rs.maxPayloadLEO >= s.wetMass ? C(GoodC, Mass(rs.maxPayloadLEO)) : C(BadC, Mass(rs.maxPayloadLEO)));
        Line(sb, "Parts value", Money(s.partsValue));
        return sb.ToString();
    }

    public static string Receipt(CADReceipt r, double balance, AscendCADData d)
    {
        var sb = new StringBuilder();
        sb.Append($"<b>ASCEND PHILIPPINES - PROCUREMENT RECEIPT</b>\n{C(SubC, "No. " + r.number + "    " + r.date)}\n\n");
        sb.Append(C(SubC, "<b>Item<pos=58%>Qty<pos=68%>In stock<pos=82%>Amount</b>")).Append('\n');
        foreach (var l in r.lines)
            sb.Append(l.name).Append($"<pos=58%>{l.qty}<pos=68%>{l.owned}<pos=82%>{(l.Charged > 0 ? MoneyFull(l.Charged) : C(SubC, "owned"))}\n");
        sb.Append('\n');
        sb.Append($"Parts subtotal<pos=82%>{MoneyFull(r.parts)}\n");
        sb.Append($"Assembly & integration ({d.assemblyIntegrationRate * 100f:0}%)<pos=82%>{MoneyFull(r.ait)}\n");
        sb.Append($"Testing & quality assurance ({d.testingQARate * 100f:0}%)<pos=82%>{MoneyFull(r.testing)}\n");
        sb.Append($"<b>TOTAL<pos=82%>{MoneyFull(r.total)}</b>\n\n");
        bool ok = r.total <= balance;
        sb.Append($"Fund balance: {MoneyFull(balance)}  ->  after payment: ").Append(C(ok ? GoodC : BadC, MoneyFull(balance - r.total)));
        if (!ok) sb.Append("\n\n").Append(C(BadC, "Insufficient funds. Remove or downgrade parts."));
        return sb.ToString();
    }

    public static string DataSheet(CADDesign rocket, CADDesign payload, CADStats rs, CADStats ps, AscendCADData d)
    {
        var sb = new StringBuilder();
        sb.Append($"<b>LAUNCH VEHICLE: {rocket.name}</b>\n").Append(RocketStats(rs, ps, d)).Append('\n');
        if (payload != null && ps != null) sb.Append($"<b>PAYLOAD: {payload.name}</b>\n").Append(PayloadStats(ps, rs, d));
        else sb.Append(C(SubC, "No payload attached."));
        return sb.ToString();
    }

    public static string Checks(CADReview rv)
    {
        var sb = new StringBuilder();
        foreach (var e in rv.errors) sb.Append(C(BadC, "FAIL  ")).Append(e).Append('\n');
        foreach (var w in rv.warnings) sb.Append(C(WarnC, "WARN  ")).Append(w).Append('\n');
        foreach (var p in rv.passed) sb.Append(C(GoodC, "PASS  ")).Append(p).Append('\n');
        return sb.ToString();
    }

    public static string ReviewEmail(CADDesign rocket, CADDesign payload, CADReview rv, CADStats rs, CADStats ps, AscendCADData d)
    {
        var sb = new StringBuilder("Dear design team,\n\n");
        if (rv.Approved)
            sb.Append($"Our engineers double-checked <b>{rocket.name}</b>{(payload != null ? " carrying <b>" + payload.name + "</b>" : "")} and the design is ")
              .Append(C(GoodC, "<b>APPROVED</b>")).Append(".\n\nThe parts are reserved from your inventory.\n")
              .Append("<b>Next step:</b> open <b>Ascend Mission Simulator</b> on the desktop, calculate the trajectory and send it to Mission Control. ")
              .Append($"When the trajectory is CONFIRMED, the rocket is built on the launch pad at ({d.launchSitePosition.x:0.##}, {d.launchSitePosition.y:0.##}, {d.launchSitePosition.z:0.##}).\n\n");
        else
            sb.Append($"Our engineers reviewed <b>{rocket.name}</b> and the design is ").Append(C(BadC, "<b>NOT APPROVED</b>"))
              .Append($" ({rv.errors.Count} blocking issue{(rv.errors.Count == 1 ? "" : "s")}).\n\nPlease redesign and resubmit. Parts you already paid for stay in your inventory.\n\n");
        sb.Append("<b>Review checklist</b>\n").Append(Checks(rv)).Append("\n<b>Data sheet</b>\n").Append(DataSheet(rocket, payload, rs, ps, d));
        sb.Append("\n\nRegards,\nAscend Philippines Review Board");
        return sb.ToString();
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  3D MODELS: your prefab if assigned, otherwise a textured procedural model
// ═════════════════════════════════════════════════════════════════════════════
public class CADPartTag : MonoBehaviour { public int index = -1; }

// Pieces that keep their own color (orbiter belly, engine bells, engine skirts) instead of the part's paint
public class CADFixedColor : MonoBehaviour { public Color color; }

public class CADCover { public bool skirt, marker; public float radius, markerRadius; public Vector3 offset; public Color color; }

// Bends a TextMeshPro label around a cylinder so it hugs the rocket instead of sticking out as a flat plate
public class AscendCurvedText : MonoBehaviour
{
    public float radius = 1f;
    public bool vertical;
    TMP_Text tmp;
    bool busy;

    void OnEnable() => TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnChanged);
    void OnDisable() => TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnChanged);
    void OnChanged(Object o) { if (!busy && tmp && o == tmp) Bend(); }

    public void Apply()
    {
        if (!tmp) tmp = GetComponent<TMP_Text>();
        if (!tmp) return;
        busy = true; tmp.ForceMeshUpdate(); busy = false;
        Bend();
    }

    void Bend()
    {
        if (radius <= 0f) return;
        var ti = tmp.textInfo;
        for (int m = 0; m < ti.meshInfo.Length; m++)
        {
            var verts = ti.meshInfo[m].vertices;
            if (verts == null) continue;
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                float c = vertical ? v.y : v.x, th = c / radius;
                float nc = radius * Mathf.Sin(th);
                if (vertical) v.y = nc; else v.x = nc;
                v.z += radius * (1f - Mathf.Cos(th));
                verts[i] = v;
            }
        }
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
}

public static class CADModels
{
    struct Piece { public Mesh mesh; public int tex; public bool fix; public Color color; }
    static readonly Dictionary<string, List<Piece>> cache = new Dictionary<string, List<Piece>>();
    static Material lit, ghostOk, ghostBad, gridMat;
    static Texture2D panelTex, solarTex, gridTex;
    static Mesh quad;
    public static bool LowSpec;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color"),
        BaseMapId = Shader.PropertyToID("_BaseMap"), MainTexId = Shader.PropertyToID("_MainTex"), BaseColorMapId = Shader.PropertyToID("_BaseColorMap");

    // ───────────── Materials & textures ─────────────
    public static Material Lit(AscendCADData d)
    {
        if (d && d.partMaterial) return d.partMaterial;
        if (lit) return lit;
        Shader sh = null;
        if (GraphicsSettings.currentRenderPipeline)
        {
            if (LowSpec) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("HDRP/Lit");
        }
        if (!sh) sh = Shader.Find("Standard");
        lit = new Material(sh);
        lit.name = "AscendCAD Part";
        if (lit.HasProperty("_Smoothness")) lit.SetFloat("_Smoothness", 0.5f);
        if (lit.HasProperty("_Glossiness")) lit.SetFloat("_Glossiness", 0.5f);
        if (lit.HasProperty("_Metallic")) lit.SetFloat("_Metallic", 0.15f);
        return lit;
    }

    static Shader Unlit() { var s = Shader.Find("Sprites/Default"); return s ? s : Shader.Find("UI/Default"); }

    static Material Ghost(bool ok)
    {
        if (ok && ghostOk) return ghostOk;
        if (!ok && ghostBad) return ghostBad;
        var m = new Material(Unlit()) { color = ok ? new Color(0.35f, 1f, 0.55f, 0.45f) : new Color(1f, 0.3f, 0.3f, 0.45f), renderQueue = 3100 };
        if (ok) ghostOk = m; else ghostBad = m;
        return m;
    }

    public static Material GridMaterial()
    {
        if (gridMat) return gridMat;
        gridMat = new Material(Unlit()) { mainTexture = Grid, color = new Color(0.92f, 0.95f, 1f, 0.75f) };
        return gridMat;
    }

    static float Hash(int x, int y) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f, 1f);

    static Texture2D MakeTex(int size, Func<int, int, Color32> f)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f(x, y);
        t.SetPixels32(px); t.Apply(true);
        return t;
    }

    static Texture2D Panel => panelTex ? panelTex : panelTex = MakeTex(256, (x, y) =>
    {
        int v = 240 + (int)(Hash(x / 8, y / 8) * 6f) - 3;   // smooth, no panel lines
        return new Color32((byte)v, (byte)v, (byte)v, 255);
    });

    static Texture2D SolarCells => solarTex ? solarTex : solarTex = MakeTex(256, (x, y) =>
        (x % 32 < 2 || y % 32 < 2) ? new Color32(175, 180, 190, 255)
            : new Color32((byte)(35 + y % 32), (byte)(55 + y % 32), 140, 255));

    // One tile = 5 x 5 cells: thick major line on the tile edge, thin minor lines inside
    static Texture2D Grid => gridTex ? gridTex : gridTex = MakeTex(250, (x, y) =>
        (x < 3 || y < 3) ? new Color32(255, 255, 255, 210)
        : (x % 50 < 1 || y % 50 < 1) ? new Color32(255, 255, 255, 90) : new Color32(255, 255, 255, 0));

    static Texture TexFor(int kind) => kind == 1 ? SolarCells : Panel;

    public static void Paint(Renderer r, Color c, Texture tex)
    {
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, c); mpb.SetColor(ColorId, c);
        if (tex) { mpb.SetTexture(BaseMapId, tex); mpb.SetTexture(MainTexId, tex); mpb.SetTexture(BaseColorMapId, tex); }
        r.SetPropertyBlock(mpb);
    }

    public static void Tint(GameObject go, Color c, Color? highlight = null)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.GetComponent<TMP_Text>()) continue;
            var fx = r.GetComponent<CADFixedColor>();
            if (fx) Paint(r, highlight.HasValue ? Color.Lerp(fx.color, highlight.Value, 0.55f) : fx.color, null);
            else Paint(r, c, null);
        }
    }

    static readonly Dictionary<int, Mesh> gridMeshes = new Dictionary<int, Mesh>();

    // Floor grid: tiling is baked into the UVs because the unlit shader ignores texture scale
    public static Mesh GridMesh(int tiles)
    {
        if (gridMeshes.TryGetValue(tiles, out var m) && m) return m;
        m = new Mesh { name = "AscendCAD Grid x" + tiles };
        m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(0, tiles), new Vector2(tiles, tiles), new Vector2(tiles, 0) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateNormals(); m.RecalculateBounds();
        return gridMeshes[tiles] = m;
    }

    public static Mesh Quad()
    {
        if (quad) return quad;
        quad = new Mesh { name = "AscendCAD Grid" };
        quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.RecalculateNormals(); quad.RecalculateBounds();
        return quad;
    }

    // ───────────── Building parts ─────────────
    public static GameObject Build(CADPart p, Color color, int layer, bool colliders, TMP_FontAsset font, List<CADLabel> labels, AscendCADData data,
        CADCover cover = null)
    {
        var go = new GameObject(p.name);
        if (p.modelPrefab)
        {
            var m = Object.Instantiate(p.modelPrefab, go.transform, false);
            if (p.tintPrefab) foreach (var r in m.GetComponentsInChildren<Renderer>()) Paint(r, color, null);
        }
        else
        {
            foreach (var pc in Pieces(p))
            {
                var c = new GameObject("Mesh");
                c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = pc.mesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterial = Lit(data);
                Paint(r, pc.fix ? pc.color : color, TexFor(pc.tex));
                if (pc.fix) c.AddComponent<CADFixedColor>().color = pc.color;
            }
        }
        if (cover != null) AddCover(go.transform, p, cover, layer == data.cadLayer, font, data);
        if (labels != null) foreach (var l in labels) AddLabel(go.transform, p, l, font);
        if (colliders)
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (!mf.GetComponent<Collider>() && !mf.GetComponent<TMP_Text>() && mf.sharedMesh) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        SetLayer(go, layer);
        return go;
    }

    public static GameObject BuildGhost(CADPart p, AscendCADData data, Transform parent)
    {
        var g = Build(p, Color.white, data.cadLayer, false, null, null, data);
        g.name = "Ghost " + p.name;
        g.transform.SetParent(parent, false);
        foreach (var c in g.GetComponentsInChildren<Collider>()) { c.enabled = false; Object.Destroy(c); }
        foreach (var r in g.GetComponentsInChildren<Renderer>()) { r.SetPropertyBlock(null); r.sharedMaterial = Ghost(true); r.shadowCastingMode = ShadowCastingMode.Off; }
        return g;
    }

    public static void SetGhostState(GameObject g, bool ok)
    {
        var m = Ghost(ok);
        foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.sharedMaterial != m) r.sharedMaterial = m;
    }

    public static void SetLayer(GameObject go, int layer) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

    // Distance from a part's axis to its surface in a horizontal direction (used for side mounting + labels)
    public static float SurfaceDistance(CADPart p, Vector3 dir, float h01)
    {
        if (p.IsBoxy)
        {
            float hx = p.boxSize.x * 0.5f, hz = p.boxSize.z * 0.5f;
            float tx = Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
            float tz = Mathf.Abs(dir.z) > 1e-4f ? hz / Mathf.Abs(dir.z) : float.MaxValue;
            return Mathf.Min(tx, tz);
        }
        float R = p.diameter * 0.5f;
        switch (p.shape)
        {
            case CADShape.NoseCone: return OgiveR(R, p.length, h01 * p.length);
            case CADShape.Fairing: return h01 < 0.55f ? R : OgiveR(R, p.length * 0.45f, (h01 - 0.55f) * p.length);
            case CADShape.Engine: return Mathf.Lerp(R, R * 0.35f, h01);
            default: return Mathf.Lerp(R, p.TopDia * 0.5f, h01);
        }
    }

    static float OgiveR(float R, float L, float x)
    {
        float rho = (R * R + L * L) / (2f * R);
        return Mathf.Max(0f, Mathf.Sqrt(Mathf.Max(0f, rho * rho - x * x)) + R - rho);
    }

    static void AddLabel(Transform parent, CADPart p, CADLabel l, TMP_FontAsset font)
    {
        if (string.IsNullOrEmpty(l.text)) return;
        float a = l.angleDeg * Mathf.Deg2Rad;
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        float h = p.StackHeight, r = SurfaceDistance(p, dir, l.height01);
        float face = p.IsBoxy ? (Mathf.Abs(dir.x) > Mathf.Abs(dir.z) ? p.boxSize.z : p.boxSize.x) : Mathf.PI * r;
        var t = CurvedText(parent, l.text, l.sizeM, l.color, font, p.IsBoxy ? 0f : r,
            l.vertical, l.vertical ? h * 0.9f : face * 0.85f, l.vertical ? Mathf.Min(face * 0.6f, h * 0.9f) : h * 0.8f);
        t.transform.localPosition = dir * (r + 0.003f + r * 0.004f) + Vector3.up * ((l.height01 - 0.5f) * h);
        t.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up) * (l.vertical ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity);
    }

    // TextMeshPro text that never sticks out of the part: shrunk to fit, then bent around the body
    static TextMeshPro CurvedText(Transform parent, string text, float sizeM, Color color, TMP_FontAsset font, float radius, bool vertical,
        float maxLength, float maxHeight)
    {
        var go = new GameObject("Label " + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font) t.font = font;
        t.text = text; t.color = color; t.fontSize = Mathf.Max(0.01f, sizeM * 10f);
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap; t.fontStyle = FontStyles.Bold;
        t.overflowMode = TextOverflowModes.Overflow;
        t.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, maxLength * 2f), Mathf.Max(0.5f, maxHeight * 2f));
        t.ForceMeshUpdate();
        var b = t.textBounds.size;
        float k = Mathf.Min(1f, maxLength / Mathf.Max(1e-4f, b.x), maxHeight / Mathf.Max(1e-4f, b.y));
        if (k < 1f) { t.fontSize *= k; t.ForceMeshUpdate(); }
        if (radius > 0.02f)
        {
            var cv = go.AddComponent<AscendCurvedText>();
            cv.radius = radius; cv.vertical = vertical;
            cv.Apply();
        }
        return t;
    }

    // Engine skirt (hides the engine) + "ENGINE INSIDE" marker shown only in the CAD view
    static void AddCover(Transform go, CADPart p, CADCover c, bool cadView, TMP_FontAsset font, AscendCADData data)
    {
        float h = p.length * 0.5f;
        if (c.skirt)
        {
            float R = c.radius;
            string key = "skirt|" + LowSpec + "|" + R.ToString("F4") + "|" + p.length.ToString("F4");
            if (!cache.TryGetValue(key, out var l) || l.Count == 0 || !l[0].mesh)
            {
                l = new List<Piece>
                {
                    Pc(Lathe(new[] { V(R * 0.96f, -h), V(R, -h), V(R, h + 0.01f), V(0f, h + 0.01f) })),
                    Pc(Lathe(new[] { V(R * 0.96f, h), V(R * 0.96f, -h) })),
                };
                cache[key] = l;
            }
            foreach (var pc in l)
            {
                var g = new GameObject("Engine Skirt");
                g.transform.SetParent(go, false);
                g.transform.localPosition = c.offset;
                g.AddComponent<MeshFilter>().sharedMesh = pc.mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = Lit(data);
                Paint(r, c.color, TexFor(0));
                g.AddComponent<CADFixedColor>().color = c.color;
            }
        }
        if (!c.marker || !cadView) return;
        for (int k = 0; k < 4; k++)
        {
            var dir = Quaternion.Euler(0f, 90f * k + 45f, 0f) * Vector3.forward;
            float size = Mathf.Clamp(c.markerRadius * 0.16f, 0.02f, 1.2f);
            var t = CurvedText(go, "ENGINE INSIDE", size, new Color(0.95f, 0.48f, 0.15f), font, c.markerRadius, false,
                Mathf.PI * c.markerRadius * 0.45f, p.length * 0.5f);
            t.transform.localPosition = c.offset + dir * (c.markerRadius + 0.01f);
            t.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
        }
    }

    // Which engines get a skirt / an "ENGINE INSIDE" marker. One per tank (the first engine under it draws it).
    public static CADCover CoverFor(CADDesign d, int i, AscendCADData data)
    {
        var pl = d.parts[i]; var p = data.Get(pl.partId);
        if (p == null || p.shape != CADShape.Engine || pl.link != CADLink.Cluster || pl.parent < 0) return null;
        for (int j = 0; j < i; j++)
        {
            var o = d.parts[j];
            if (o.parent == pl.parent && o.link == CADLink.Cluster && data.Get(o.partId)?.shape == CADShape.Engine) return null;
        }
        bool skirt = false;
        for (int j = 0; j < d.parts.Count; j++) if (d.parts[j].parent == pl.parent && d.parts[j].link == CADLink.Cluster && d.parts[j].shroud) skirt = true;

        var T = d.parts[pl.parent]; var tp = data.Get(T.partId);
        if (tp == null) return null;
        var off = Quaternion.Inverse(pl.rot) * (T.pos - pl.pos); off.y = 0f;
        float tr = (tp.IsBoxy ? Mathf.Min(tp.boxSize.x, tp.boxSize.z) : tp.diameter) * 0.5f;

        // Engine hanging inside an interstage (the tank sits on top of something)?
        float encR = 0f;
        if (T.link == CADLink.Top && T.parent >= 0)
        {
            var B = d.parts[T.parent]; var bp = data.Get(B.partId);
            if (bp != null && !bp.IsBoxy && bp.role != CADRole.Engine && bp.role != CADRole.SolidMotor)
            {
                float bh = bp.StackHeight;
                float h01 = Mathf.Clamp01((pl.pos.y - (B.pos.y - bh * 0.5f)) / Mathf.Max(1e-4f, bh));
                encR = SurfaceDistance(bp, Vector3.forward, h01);
            }
        }
        if (!skirt && encR <= 0f) return null;
        return new CADCover
        {
            skirt = skirt && encR <= 0f, radius = tr * 0.985f, offset = off, color = T.color,
            marker = true, markerRadius = (skirt && encR <= 0f ? tr : encR) + 0.005f
        };
    }
    // ───────────── Procedural meshes ─────────────
    static Vector2 V(float r, float y) => new Vector2(r, y);
    static Piece Pc(Mesh m, int tex = 0) => new Piece { mesh = m, tex = tex };
    static Piece Fx(Mesh m, Color c) => new Piece { mesh = m, fix = true, color = c };

    static List<Piece> Pieces(CADPart p)
    {
        string key = $"{LowSpec}|{p.shape}|{p.diameter}|{p.TopDia}|{p.length}|{p.boxSize.ToString("F4")}|{p.role}";
        if (cache.TryGetValue(key, out var l) && l.Count > 0 && l[0].mesh) return l;
        l = new List<Piece>();
        float R = p.diameter * 0.5f, Rt = p.TopDia * 0.5f, L = p.length, h = L * 0.5f;
        var s = p.boxSize;
        int tex = p.role == CADRole.SolarPanel ? 1 : 0;
        switch (p.shape)
        {
            case CADShape.Cylinder:
            case CADShape.Frustum: l.Add(Pc(Lathe(new[] { V(0, -h), V(R, -h), V(Rt, h), V(0, h) }))); break;
            case CADShape.NoseCone: l.Add(Pc(Lathe(NoseProfile(R, L, 0f)))); break;
            case CADShape.OgiveTank: l.Add(Pc(Lathe(NoseProfile(R, L, 0.8f)))); break;
            case CADShape.Capsule:
                l.Add(Pc(Lathe(new[] { V(0f, -h), V(R, -h), V(R, -h + 0.66f * L), V(R * 0.97f, -h + 0.67f * L), V(R * 0.22f, h - 0.03f * L), V(0f, h) })));
                break;
            case CADShape.Tower:
                l.Add(Pc(Lathe(new[] { V(0f, -h), V(R, -h), V(R * 0.22f, -h + 0.12f * L), V(R * 0.12f, -h + 0.14f * L), V(R * 0.12f, -h + 0.45f * L),
                    V(R * 0.2f, -h + 0.47f * L), V(R * 0.2f, h - 0.12f * L), V(R * 0.08f, h - 0.03f * L), V(0f, h) })));
                break;
            case CADShape.Orbiter: OrbiterPieces(l, p.diameter / 5.2f, L); break;
            case CADShape.Fairing: l.Add(Pc(Lathe(NoseProfile(R, L, 0.55f)))); break;
            case CADShape.Engine: l.Add(Pc(Lathe(EngineProfile(R, L)))); break;
            case CADShape.Booster: l.Add(Pc(Lathe(BoosterProfile(R, L, true)))); break;
            case CADShape.SolidStage: l.Add(Pc(Lathe(BoosterProfile(R, L, false)))); break;
            case CADShape.Sphere:
                var sp = new List<Vector2>();
                for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(-90f, 90f, i / 16f) * Mathf.Deg2Rad; sp.Add(V(R * Mathf.Cos(a), R * Mathf.Sin(a))); }
                l.Add(Pc(Lathe(sp))); break;
            case CADShape.Box: l.Add(Pc(BoxMesh(Vector3.zero, s), tex)); break;
            case CADShape.Panel: case CADShape.Wing: case CADShape.Fin: case CADShape.GridFin: case CADShape.Leg:
                l.Add(Pc(BoxMesh(new Vector3(0f, 0f, s.z * 0.5f), s), tex)); break;
            case CADShape.Camera:
            {
                var body = new Vector3(s.x, s.y, s.z * 0.8f);
                l.Add(Pc(BoxMesh(new Vector3(0f, 0f, body.z * 0.5f), body)));
                float lr = Mathf.Min(s.x, s.y) * 0.34f, lz = s.z * 0.2f;
                l.Add(Pc(Along(Lathe(new[] { V(0, 0), V(lr, 0), V(lr, lz), V(lr * 0.8f, lz), V(0, lz * 0.8f) }, 24), s.z * 0.8f)));
                break;
            }
            case CADShape.Dish:
            {
                float r = s.x * 0.5f, depth = s.z * 0.3f; int N = 12;
                var pr = new List<Vector2> { V(0, 0) };
                for (int i = 1; i <= N; i++) { float t = i / (float)N; pr.Add(V(r * t, depth * t * t)); }
                pr.Add(V(r * 0.97f, depth + 0.01f));
                for (int i = N - 1; i >= 0; i--) { float t = i / (float)N; pr.Add(V(r * 0.97f * t, depth * t * t + 0.012f)); }
                l.Add(Pc(Along(Lathe(pr, 32), 0f)));
                float fr = Mathf.Max(0.004f, r * 0.03f);
                l.Add(Pc(Along(Lathe(new[] { V(0, 0), V(fr, 0), V(fr, s.z), V(0, s.z) }, 10), 0f)));
                break;
            }
            case CADShape.Rod:
            {
                float rr = Mathf.Max(0.003f, s.x * 0.5f);
                l.Add(Pc(Along(Lathe(new[] { V(0, 0), V(rr, 0), V(rr, s.z), V(0, s.z) }, 12), 0f)));
                break;
            }
        }
        cache[key] = l;
        return l;
    }

    // Space Shuttle orbiter: belly faces -Z (toward the tank), tail fin on +Z, engines at the bottom
    static void OrbiterPieces(List<Piece> l, float sc, float L)
    {
        float h = L * 0.5f, le = 4.3f * sc, yb = -h + le, Rf = 2.4f * sc, fl = L - le;
        Color dark = new Color(0.11f, 0.11f, 0.12f), grey = new Color(0.36f, 0.36f, 0.38f);
        l.Add(Pc(Translate(Lathe(NoseProfile(Rf, fl, (fl - 7f * sc) / fl)), new Vector3(0f, yb + fl * 0.5f, 0f))));
        var wing = new[]
        {
            new Vector2(-11.9f * sc, yb + 0.8f * sc), new Vector2(11.9f * sc, yb + 0.8f * sc), new Vector2(11.9f * sc, yb + 3.2f * sc),
            new Vector2(Rf * 0.9f, yb + 21f * sc), new Vector2(-Rf * 0.9f, yb + 21f * sc), new Vector2(-11.9f * sc, yb + 3.2f * sc),
        };
        float wz = -Rf * 0.55f;
        l.Add(Pc(Prism(wing, 0.7f * sc, (a, b, c) => new Vector3(a, b, wz + c))));
        l.Add(Fx(Prism(wing, 0.06f * sc, (a, b, c) => new Vector3(a, b, wz - 0.38f * sc + c)), dark));                 // black belly tiles
        l.Add(Fx(BoxMesh(new Vector3(0f, yb + fl * 0.45f, -Rf * 0.86f), new Vector3(Rf * 1.5f, fl * 0.8f, 0.5f * sc)), dark));
        var fin = new[]
        {
            new Vector2(Rf * 0.8f, yb + 1.5f * sc), new Vector2(Rf * 0.8f, yb + 10.5f * sc),
            new Vector2(Rf + 7.2f * sc, yb + 4.2f * sc), new Vector2(Rf + 7.2f * sc, yb + 1.2f * sc),
        };
        l.Add(Pc(Prism(fin, 0.4f * sc, (a, b, c) => new Vector3(c, b, a))));
        var bell = Lathe(EngineProfile(1.15f * sc, le));
        foreach (var pos in new[] { new Vector3(0f, yb - le * 0.5f, Rf * 0.45f), new Vector3(1.35f * sc, yb - le * 0.5f, -Rf * 0.25f), new Vector3(-1.35f * sc, yb - le * 0.5f, -Rf * 0.25f) })
            l.Add(Fx(Translate(Object.Instantiate(bell), pos), grey));
    }

    static Mesh Translate(Mesh m, Vector3 d)
    {
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] += d;
        m.vertices = v; m.RecalculateBounds();
        return m;
    }

    // Flat polygon extruded by "thick". map(a, b, c) turns polygon coords + thickness into 3D. Double-sided, flat shaded.
    static Mesh Prism(Vector2[] poly, float thick, Func<float, float, float, Vector3> map)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        void Face(Vector3[] f)
        {
            for (int side = 0; side < 2; side++)
            {
                int b = v.Count; v.AddRange(f);
                for (int i = 1; i < f.Length - 1; i++)
                {
                    t.Add(b);
                    if (side == 0) { t.Add(b + i); t.Add(b + i + 1); } else { t.Add(b + i + 1); t.Add(b + i); }
                }
            }
        }
        float c0 = -thick * 0.5f, c1 = thick * 0.5f; int n = poly.Length;
        Face(poly.Select(q => map(q.x, q.y, c0)).ToArray());
        Face(poly.Select(q => map(q.x, q.y, c1)).ToArray());
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            Face(new[] { map(a.x, a.y, c0), map(b.x, b.y, c0), map(b.x, b.y, c1), map(a.x, a.y, c1) });
        }
        var m = new Mesh { name = "AscendCAD Prism" };
        m.SetVertices(v); m.SetTriangles(t, 0);
        var uv = new List<Vector2>(v.Count);
        foreach (var q in v) uv.Add(new Vector2(q.x + q.z, q.y) * 0.5f);
        m.SetUVs(0, uv);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static List<Vector2> NoseProfile(float R, float L, float cylFrac)
    {
        float h = L * 0.5f, cyl = L * cylFrac, ogL = L - cyl;
        var p = new List<Vector2> { V(0, -h), V(R, -h) };
        if (cyl > 0f) p.Add(V(R, -h + cyl));
        for (int i = 1; i <= 16; i++) { float x = ogL * i / 16f; p.Add(V(OgiveR(R, ogL, x), -h + cyl + x)); }
        p.Add(V(0, h));
        return p;
    }

    static List<Vector2> EngineProfile(float Re, float L)
    {
        float yb = -L * 0.5f, yt = L * 0.5f, throatY = yb + L * 0.6f;
        float rt = Mathf.Max(0.02f, Re * 0.22f), rc = Mathf.Max(rt * 1.6f, Mathf.Min(Re * 0.42f, L * 0.12f)), rp = rc * 1.25f;
        float wall = Mathf.Max(0.004f, Re * 0.03f);
        var p = new List<Vector2> { V(0, throatY + 0.01f) };
        const int N = 12;
        for (int i = 0; i <= N; i++) { float s = i / (float)N; p.Add(V(Mathf.Max(0.001f, rt + (Re - rt) * Mathf.Pow(s, 0.55f) - wall), throatY - s * (throatY - yb))); } // inside of bell
        p.Add(V(Re, yb));
        for (int i = N; i >= 0; i--) { float s = i / (float)N; p.Add(V(rt + (Re - rt) * Mathf.Pow(s, 0.55f), throatY - s * (throatY - yb))); } // outside of bell
        p.Add(V(rc, throatY + L * 0.05f));
        p.Add(V(rc, throatY + L * 0.17f));
        p.Add(V(rp, throatY + L * 0.2f));
        p.Add(V(rp, yt - L * 0.03f));
        p.Add(V(rp * 0.6f, yt));
        p.Add(V(0, yt));
        return p;
    }

    static List<Vector2> BoosterProfile(float R, float L, bool nose)
    {
        float h = L * 0.5f;
        var p = new List<Vector2> { V(0, -h), V(R * 0.5f, -h), V(R * 0.35f, -h + L * 0.035f), V(R, -h + L * 0.065f) };
        if (nose) { p.Add(V(R, h - L * 0.12f)); p.Add(V(R * 0.55f, h - L * 0.04f)); p.Add(V(R * 0.15f, h)); }
        else p.Add(V(R, h));
        p.Add(V(0, h));
        return p;
    }

    // Revolves a (radius, height) profile around Y. Profile goes bottom -> top for outward normals.
    static Mesh Lathe(IList<Vector2> prof, int seg = 0)
    {
        if (seg <= 0) seg = LowSpec ? 20 : 36;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        float acc = 0f;
        for (int i = 0; i < prof.Count - 1; i++)
        {
            Vector2 a = prof[i], b = prof[i + 1], t = b - a;
            float len = t.magnitude; if (len < 1e-6f) continue;
            var nn = new Vector2(t.y, -t.x) / len;
            int start = v.Count;
            for (int k = 0; k <= seg; k++)
            {
                float ang = k / (float)seg * Mathf.PI * 2f, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                v.Add(new Vector3(a.x * c, a.y, a.x * s)); v.Add(new Vector3(b.x * c, b.y, b.x * s));
                var nrm = new Vector3(nn.x * c, nn.y, nn.x * s); n.Add(nrm); n.Add(nrm);
                float u = k / (float)seg * 4f;
                uv.Add(new Vector2(u, acc * 0.5f)); uv.Add(new Vector2(u, (acc + len) * 0.5f));
            }
            for (int k = 0; k < seg; k++)
            {
                int a0 = start + 2 * k, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                tris.Add(a0); tris.Add(b0); tris.Add(b1);
                tris.Add(a0); tris.Add(b1); tris.Add(a1);
            }
            acc += len;
        }
        var m = new Mesh { name = "AscendCAD Lathe" };
        if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    // Rotates a Y-axis mesh so it points along +Z (outward for side-mounted parts)
    static Mesh Along(Mesh m, float z)
    {
        var rot = Quaternion.Euler(90f, 0f, 0f);
        var v = m.vertices; var n = m.normals;
        for (int i = 0; i < v.Length; i++) { v[i] = rot * v[i] + new Vector3(0f, 0f, z); n[i] = rot * n[i]; }
        m.vertices = v; m.normals = n; m.RecalculateBounds();
        return m;
    }

    static Mesh BoxMesh(Vector3 c, Vector3 size)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var nn in faces)
        {
            Vector3 vv = Mathf.Abs(nn.y) > 0.5f ? Vector3.forward : Vector3.up;
            Vector3 uu = Vector3.Cross(nn, vv);
            float hu = Mathf.Abs(Vector3.Dot(uu, size)) * 0.5f, hv = Mathf.Abs(Vector3.Dot(vv, size)) * 0.5f, hn = Mathf.Abs(Vector3.Dot(nn, size)) * 0.5f;
            Vector3 fc = c + nn * hn; int b = v.Count;
            v.Add(fc - uu * hu - vv * hv); v.Add(fc - uu * hu + vv * hv); v.Add(fc + uu * hu + vv * hv); v.Add(fc + uu * hu - vv * hv);
            for (int i = 0; i < 4; i++) n.Add(nn);
            const float k = 6f;
            uv.Add(new Vector2(-hu, -hv) * k); uv.Add(new Vector2(-hu, hv) * k); uv.Add(new Vector2(hu, hv) * k); uv.Add(new Vector2(hu, -hv) * k);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }
        var m = new Mesh { name = "AscendCAD Box" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
        return m;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  LAUNCH SITE: places the approved rocket (with satellite in the fairing) on the pad
// ═════════════════════════════════════════════════════════════════════════════
public class AscendRocket : MonoBehaviour
{
    public CADDesign rocket, payload;
    [NonSerialized] public CADStats stats;   // for the trajectory calculator
}

public static class AscendLaunchSite
{
    public static GameObject Current { get; private set; }

    public static GameObject BuildDesign(CADDesign d, AscendCADData data, Transform parent, int layer, bool boxColliders)
    {
        var root = new GameObject(d.name);
        root.transform.SetParent(parent, false);
        for (int i = 0; i < d.parts.Count; i++)
        {
            var pl = d.parts[i];
            var p = data.Get(pl.partId);
            if (p == null || p.attach == CADAttach.Internal) continue;
            var go = CADModels.Build(p, pl.color, layer, false, data.labelFont, pl.labels, data, CADModels.CoverFor(d, i, data));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pl.pos; go.transform.localRotation = pl.rot;
            if (boxColliders)
            {
                var lb = CADStats.LocalBounds(p);
                if (lb.size.sqrMagnitude > 1e-6f) { var bc = go.AddComponent<BoxCollider>(); bc.center = lb.center; bc.size = lb.size; }
            }
        }
        return root;
    }

    public static GameObject CurrentPad { get; private set; }
    static string currentPadId;
    static float currentDeck;

    // Where the rocket's base goes: on the chosen pad's deck
    static Vector3 PadBase(AscendCADData data, string padId, CADStats stats)
    {
        var pad = data.GetPad(padId);
        Vector3 basePos = data.launchSitePosition;
        if (pad == null) return basePos;

        // Real-size pad built from code, sized to this rocket
        if (pad.useBuiltInModel && AscendPadModels.Has(pad))
        {
            float r = stats != null && stats.width > 0f ? stats.width * 0.5f : 2f;
            float h = stats != null && stats.height > 0f ? stats.height : 50f;
            var mover = data.GetPad(pad.requiresId);
            bool crawler = mover != null && AscendEconomy.OwnsPad(mover, data);
            string key = $"{pad.id}|{r:0.0}|{h:0}|{crawler}";
            if (CurrentPad && currentPadId != key) { Object.Destroy(CurrentPad); CurrentPad = null; }
            if (!CurrentPad)
            {
                CurrentPad = AscendPadModels.Build(pad, data, r, h, true, crawler, out currentDeck);
                CurrentPad.transform.position = basePos;
                currentPadId = key;
            }
            return basePos + Vector3.up * currentDeck;
        }

        // Your own model / scene object
        GameObject padObj = !string.IsNullOrEmpty(pad.sceneObjectName) ? GameObject.Find(pad.sceneObjectName) : null;
        if (CurrentPad && (padObj || currentPadId != pad.id)) { Object.Destroy(CurrentPad); CurrentPad = null; }
        if (!padObj && pad.modelPrefab)
        {
            if (!CurrentPad)
            {
                CurrentPad = Object.Instantiate(pad.modelPrefab, data.launchSitePosition, Quaternion.identity);
                CurrentPad.name = "Launch Pad - " + pad.name;
                currentPadId = pad.id;
            }
            padObj = CurrentPad;
        }
        var rot = Quaternion.identity;
        if (padObj) { basePos = padObj.transform.position; rot = Quaternion.Euler(0f, padObj.transform.eulerAngles.y, 0f); }
        return basePos + rot * pad.rocketOffset;
    }

    public static GameObject Spawn(AscendCADData data, CADDesign rocket, CADDesign payload, CADStats stats, string padId = null)
    {
        if (data.replacePreviousRocket && Current) Object.Destroy(Current);
        var site = new GameObject("Ascend Rocket - " + rocket.name);
        var body = BuildDesign(rocket, data, site.transform, 0, true);

        if (payload != null && !payload.IsEmpty)
        {
            var pay = BuildDesign(payload, data, body.transform, 0, true);
            pay.name = "Payload - " + payload.name;
            Vector3 axis = Vector3.zero; float baseY = float.MinValue;
            foreach (var pl in rocket.parts)
            {
                var p = data.Get(pl.partId);
                if (p != null && p.role == CADRole.Fairing && p.innerDiameter > 0f) { axis = pl.pos; baseY = pl.pos.y - p.length * 0.5f; break; }
            }
            if (baseY == float.MinValue)   // no fairing: ride inside a carrier such as the Shuttle orbiter's payload bay
                foreach (var pl in rocket.parts)
                {
                    var p = data.Get(pl.partId);
                    if (p != null && p.innerDiameter > 0f) { axis = pl.pos; baseY = pl.pos.y - p.innerHeight * 0.5f; break; }
                }
            if (baseY == float.MinValue) { var rb = CADStats.DesignBounds(rocket, data); axis = rb.center; baseY = rb.max.y; }
            var pb = CADStats.DesignBounds(payload, data);
            pay.transform.localPosition = new Vector3(axis.x - pb.center.x, baseY - pb.min.y + 0.05f, axis.z - pb.center.z);
        }

        // Lowest point of the rocket sits exactly on the pad's deck
        var renderers = site.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            site.transform.position += PadBase(data, padId, stats) - new Vector3(b.center.x, b.min.y, b.center.z);
        }
        else site.transform.position = PadBase(data, padId, stats);

        var info = site.AddComponent<AscendRocket>();
        info.rocket = rocket.Clone(); info.payload = payload?.Clone(); info.stats = stats;
        Current = site;
        return site;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  LAUNCH PAD MODELS: real-size pads built from code (1 unit = 1 m)
//    Basic Launch Mount  ~ Rocket Lab LC-1: 30 m pad, launch table, strongback mast
//    Gantry Launch Pad   ~ SLC-40 class: raised mount, flame trench, 90 m service tower,
//                          lightning masts, LOX sphere, RP-1 tanks, water tower
//    Mobile Launcher     ~ NASA ML on Pad 39B: 6 pedestals, 50 x 41 x 7.6 m base, 100 m tower,
//                          180 m lightning towers; the Crawler parks beside it if you own one
//    Crawler-Transporter ~ 40 x 35 m, 4 double-tracked trucks
//  The exhaust hole fits the rocket's width and the service arms reach its side.
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendPadModels
{
    static readonly Color Concrete = new Color(0.66f, 0.66f, 0.63f), DarkConcrete = new Color(0.30f, 0.30f, 0.30f),
        Steel = new Color(0.58f, 0.60f, 0.63f), DarkSteel = new Color(0.25f, 0.26f, 0.28f),
        White = new Color(0.92f, 0.92f, 0.93f), Track = new Color(0.17f, 0.17f, 0.18f);

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    public static bool Has(CADLaunchPad p) =>
        p != null && (p.id == "pad.rail" || p.id == "pad.basic" || p.id == "pad.gantry" || p.id == "pad.ml" || p.id == "pad.crawler");

    // deckHeight = how high above the pad's ground the rocket's base sits
    public static GameObject Build(CADLaunchPad p, AscendCADData data, float rocketRadius, float rocketHeight,
        bool colliders, bool parkCrawler, out float deckHeight)
    {
        var k = new Kit();
        float R = Mathf.Max(0.5f, rocketRadius), H = Mathf.Max(10f, rocketHeight);
        switch (p.id)
        {
            case "pad.rail": deckHeight = Rail(k, rocketRadius); break;
            case "pad.basic": deckHeight = Basic(k, R, H); break;
            case "pad.gantry": deckHeight = Gantry(k, R, H); break;
            case "pad.ml": deckHeight = MobileLauncher(k, R, H, parkCrawler); break;
            case "pad.crawler": Crawler(k, Vector3.zero); deckHeight = 0f; break;
            default: deckHeight = 0f; return null;
        }
        return k.Finish("Launch Pad - " + p.name, data, colliders);
    }

    // CanSat launch rail: concrete slab, blast plate, 6 m rail on a tripod, launch controller box
    static float Rail(Kit k, float r)
    {
        const float g = 0.15f, railH = 6f;
        r = Mathf.Clamp(r, 0.03f, 0.15f);
        k.BoxB(V(-3, 0, -3), V(3, g, 3), Concrete);
        k.BoxB(V(-0.5f, g, -0.5f), V(0.5f, g + 0.04f, 0.5f), DarkSteel);                      // blast plate
        float deck = g + 0.3f, rx = r + 0.03f;
        k.Box(V(rx, g + railH * 0.5f, 0), V(0.04f, railH, 0.04f), Steel);                       // the rail
        Vector3 top = V(rx, g + railH * 0.55f, 0);
        foreach (var leg in new[] { V(rx + 1.4f, g, 0), V(rx - 0.7f, g, 1.2f), V(rx - 0.7f, g, -1.2f) })
            k.Beam(top, leg, 0.05f, DarkSteel);                                                    // tripod legs
        k.Box(V(rx, deck - 0.1f, 0), V(0.25f, 0.06f, 0.25f), DarkSteel);                       // rail stop the rocket rests on
        k.BoxB(V(-2.6f, g, 2.0f), V(-2.0f, g + 0.5f, 2.5f), White);                              // launch controller
        k.Beam(V(-2.3f, g + 0.05f, 2.0f), V(0, g + 0.05f, 0), 0.02f, Track);                     // igniter cable
        return deck;
    }

    static float Basic(Kit k, float R, float H)
    {
        const float g = 0.5f;
        k.BoxB(V(-15, 0, -15), V(15, g, 15), Concrete);
        float deck = 4.5f, hole = R + 0.25f, s = Mathf.Max(3f, R + 1.5f);
        k.Frame(-s, s, -s, s, deck - 0.5f, deck, -hole, hole, -hole, hole, Steel);           // launch table
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                k.BoxB(V(sx * (s - 0.8f), g, sz * (s - 0.8f)), V(sx * s, deck - 0.5f, sz * s), Steel);
        k.Box(V(0, g + 0.6f * hole, 0), V(2f * hole, 0.3f, 2f * hole), Quaternion.Euler(-30f, 0f, 0f), DarkSteel); // deflector

        float tw = 2.5f, tx = R + 3f + tw * 0.5f, mastH = Mathf.Clamp(H * 1.1f, 15f, 45f);    // strongback mast
        k.Lattice(V(tx, g, 0), tw, mastH, 2.5f, 0.15f, Steel);
        foreach (float f in new[] { 0.3f, 0.75f })
        {
            float y = deck + H * f;
            if (y < g + mastH - 0.5f) Arm(k, R, tx - tw * 0.5f, y, 1.2f);
        }
        k.BoxB(V(-14, g, 8), V(-8, g + 3.5f, 12), White);                                      // control hut
        return deck;
    }

    static float Gantry(Kit k, float R, float H)
    {
        const float g = 0.6f;
        k.BoxB(V(-55, 0, -55), V(55, g, 55), Concrete);
        float h = Mathf.Clamp(2f * R + 2f, 4f, 14f) * 0.5f, s = Mathf.Max(9f, h + 4f);
        float deck = Mathf.Max(7f, g + 1.3f * h + 1f);

        // Hollow raised mount: exhaust drops in and leaves through the open front (-Z) into the trench
        k.BoxB(V(-s, g, -s), V(-h, deck, s), Concrete);
        k.BoxB(V(h, g, -s), V(s, deck, s), Concrete);
        k.BoxB(V(-h, g, h), V(h, deck, s), Concrete);
        k.BoxB(V(-h, deck - 1.5f, -s), V(h, deck, -h), Concrete);
        k.Box(V(0, g + 0.6f * h, 0), V(2f * h, 0.6f, 2f * h), Quaternion.Euler(-35f, 0f, 0f), DarkSteel);
        float th = R + 0.3f;
        k.Frame(-s, s, -s, s, deck - 0.8f, deck, -th, th, -th, th, DarkSteel);                // hold-down table
        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            k.Box(d * (R + 0.7f) + Vector3.up * (deck + 0.6f), V(0.8f, 1.2f, 0.8f), DarkSteel);

        k.BoxB(V(-h, g, -55), V(h, g + 0.05f, -s), DarkConcrete);                              // flame trench
        k.BoxB(V(-h - 1.2f, g, -55), V(-h, g + 2.5f, -s), Concrete);
        k.BoxB(V(h, g, -55), V(h + 1.2f, g + 2.5f, -s), Concrete);

        float tw = 10f, tx = R + 4f + tw * 0.5f, towerH = 90f;                                  // service tower + arms
        k.Lattice(V(tx, g, 0), tw, towerH, 5f, 0.35f, Steel, 4);
        k.Box(V(tx, g + towerH + 0.25f, 0), V(tw + 1f, 0.5f, tw + 1f), DarkSteel);
        foreach (float f in new[] { 0.3f, 0.62f, 0.88f })
        {
            float y = deck + H * f;
            if (y < g + towerH - 2f) Arm(k, R, tx - tw * 0.5f, y, 2.2f);
        }

        Vector3 m1 = V(-32, g, 38), m2 = V(32, g, 38);                                          // lightning masts + wire
        k.Cyl(m1 + Vector3.up * 55f, 0.8f, 110f, Steel);
        k.Cyl(m2 + Vector3.up * 55f, 0.8f, 110f, Steel);
        k.Beam(m1 + Vector3.up * 110f, m2 + Vector3.up * 110f, 0.08f, DarkSteel);
        k.Beam(m1 + Vector3.up * 110f, V(tx, g + towerH, 0), 0.08f, DarkSteel);

        var lox = V(-40, 10, -25);                                                              // propellant farm
        k.Ball(lox, 14f, White);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                k.Beam(lox + V(sx * 4.5f, -4f, sz * 4.5f), V(-40 + sx * 6f, g, -25 + sz * 6f), 0.6f, Steel);
        for (int i = 0; i < 2; i++)
        {
            float z = 6f + i * 8f;
            k.Cyl(V(-40, g + 3.2f, z), 2.6f, 20f, White, Quaternion.Euler(0f, 0f, 90f));
            k.Box(V(-47, g + 0.5f, z), V(1f, 1f, 4f), Concrete);
            k.Box(V(-33, g + 0.5f, z), V(1f, 1f, 4f), Concrete);
        }
        k.Beam(V(-34, g + 0.8f, -20), V(-s, g + 0.8f, -2), 0.6f, White);

        k.Lattice(V(42, g, -35), 7f, 45f, 7.5f, 0.3f, Steel);                                  // water tower
        k.Cyl(V(42, g + 50f, -35), 5.5f, 10f, White);
        return deck;
    }

    static float MobileLauncher(Kit k, float R, float H, bool parkCrawler)
    {
        const float g = 0.6f, ped = 6.7f, baseT = 7.6f;
        float deck = g + ped + baseT;
        k.BoxB(V(-70, 0, -70), V(70, g, 70), Concrete);                                        // pad hardstand

        float hx = Mathf.Clamp(2f * R + 4f, 10f, 22f) * 0.5f, hz = Mathf.Clamp(2f * R + 3f, 8f, 18f) * 0.5f;
        k.BoxB(V(-hx, g, -70), V(hx, g + 0.05f, hz), DarkConcrete);                            // flame trench
        k.BoxB(V(-hx - 1.5f, g, -70), V(-hx, g + 3f, -24f), Concrete);
        k.BoxB(V(hx, g, -70), V(hx + 1.5f, g + 3f, -24f), Concrete);

        float bx0 = -22f, bx1 = 28f, bz0 = -20.5f, bz1 = 20.5f;
        foreach (float x in new[] { -18f, 3f, 24f })                                           // six pedestals
            foreach (float z in new[] { -16f, 16f })
                k.BoxB(V(x - 1.5f, g, z - 1.5f), V(x + 1.5f, g + ped, z + 1.5f), Concrete);
        k.Frame(bx0, bx1, bz0, bz1, g + ped, deck, -hx, hx, -hz, hz, Steel);                    // two-story base

        float th = R + 0.3f;                                                                    // vehicle support posts
        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            k.Box(d * (th + 0.5f) + Vector3.up * (deck + 0.75f), V(1f, 1.5f, 1f), DarkSteel);

        float tw = 12f, towerH = 100f;                                                          // umbilical tower
        float tx = Mathf.Min(Mathf.Max(R + 4f, hx + 2f) + tw * 0.5f, bx1 - tw * 0.5f);
        k.Lattice(V(tx, deck, 0), tw, towerH, 6.25f, 0.45f, Steel, 4);
        k.Box(V(tx, deck + towerH + 0.25f, 0), V(tw + 1f, 0.5f, tw + 1f), DarkSteel);
        k.Cyl(V(tx, deck + towerH + 6f, 0), 0.4f, 12f, DarkSteel);
        foreach (float f in new[] { 0.2f, 0.45f, 0.7f, 0.88f })
        {
            float y = deck + H * f;
            if (y < deck + towerH - 2f) Arm(k, R, tx - tw * 0.5f, y, 2.5f);
        }

        var masts = new[] { V(-55, g, -50), V(55, g, -50), V(0, g, 62) };                     // 39B lightning towers
        foreach (var m in masts) k.Cyl(m + Vector3.up * 90f, 1.2f, 180f, Steel);
        for (int i = 0; i < 3; i++)
            k.Beam(masts[i] + Vector3.up * 180f, masts[(i + 1) % 3] + Vector3.up * 180f, 0.1f, DarkSteel);

        if (parkCrawler) Crawler(k, V(0, 0, 105));
        return deck;
    }

    static void Crawler(Kit k, Vector3 o)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var c = o + V(sx * 14.5f, 0, sz * 13.5f);
                for (int t = -1; t <= 1; t += 2)
                    k.BoxB(c + V(-6, 0, t * 1.4f - 1f), c + V(6, 2f, t * 1.4f + 1f), Track);
                k.BoxB(c + V(-5.5f, 2f, -2.6f), c + V(5.5f, 3f, 2.6f), DarkSteel);
                k.Cyl(c + Vector3.up * 3.65f, 1.2f, 1.3f, Steel);
            }
        k.BoxB(o + V(-20, 4.3f, -17.5f), o + V(20, 6.1f, 17.5f), Steel);
        k.BoxB(o + V(-21.8f, 2.6f, -17.5f), o + V(-20, 5.6f, -14f), White);                   // control cabs
        k.BoxB(o + V(20, 2.6f, 14f), o + V(21.8f, 5.6f, 17.5f), White);
    }

    // Service arm from the tower face (+X side) to just off the rocket's side
    static void Arm(Kit k, float R, float towerFaceX, float y, float w)
    {
        float x0 = R + 0.3f, x1 = towerFaceX, hw = w * 0.5f;
        if (x1 - x0 < 0.5f) return;
        k.BoxB(V(x0, y - 0.6f, -hw), V(x1, y, hw), DarkSteel);
        k.Beam(V(x0, y + 1.1f, -hw), V(x1, y + 1.1f, -hw), 0.1f, Steel);
        k.Beam(V(x0, y + 1.1f, hw), V(x1, y + 1.1f, hw), 0.1f, Steel);
        k.Beam(V(x1, y - 0.6f, hw * 0.5f), V(x0 + 0.1f, y - 1.6f, hw * 0.5f), 0.35f, Track);  // umbilical hose
    }

    // Everything is merged into one mesh per color (few draw calls even for big towers)
    class Kit
    {
        static Mesh cube, cyl, ball;
        static Mesh Prim(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return m;
        }
        static Mesh Cube => cube ? cube : cube = Prim(PrimitiveType.Cube);
        static Mesh Cylinder => cyl ? cyl : cyl = Prim(PrimitiveType.Cylinder);
        static Mesh Sphere => ball ? ball : ball = Prim(PrimitiveType.Sphere);

        readonly Dictionary<Color, List<CombineInstance>> parts = new Dictionary<Color, List<CombineInstance>>();

        void Add(Mesh m, Matrix4x4 mat, Color c)
        {
            if (!parts.TryGetValue(c, out var l)) parts[c] = l = new List<CombineInstance>();
            l.Add(new CombineInstance { mesh = m, transform = mat });
        }

        public void Box(Vector3 c, Vector3 s, Color col) => Add(Cube, Matrix4x4.TRS(c, Quaternion.identity, s), col);
        public void Box(Vector3 c, Vector3 s, Quaternion r, Color col) => Add(Cube, Matrix4x4.TRS(c, r, s), col);

        public void BoxB(Vector3 a, Vector3 b, Color col)
        {
            Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b), s = mx - mn;
            if (s.x > 1e-4f && s.y > 1e-4f && s.z > 1e-4f) Box((mn + mx) * 0.5f, s, col);
        }

        // Slab from y0 to y1 with a rectangular hole
        public void Frame(float x0, float x1, float z0, float z1, float y0, float y1, float hx0, float hx1, float hz0, float hz1, Color c)
        {
            BoxB(V(x0, y0, z0), V(hx0, y1, z1), c);
            BoxB(V(hx1, y0, z0), V(x1, y1, z1), c);
            BoxB(V(hx0, y0, z0), V(hx1, y1, hz0), c);
            BoxB(V(hx0, y0, hz1), V(hx1, y1, z1), c);
        }

        public void Cyl(Vector3 c, float radius, float height, Color col, Quaternion? r = null) =>
            Add(Cylinder, Matrix4x4.TRS(c, r ?? Quaternion.identity, V(2f * radius, height * 0.5f, 2f * radius)), col);

        public void Ball(Vector3 c, float diameter, Color col) => Add(Sphere, Matrix4x4.TRS(c, Quaternion.identity, Vector3.one * diameter), col);

        public void Beam(Vector3 a, Vector3 b, float t, Color col)
        {
            var d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return;
            var dir = d / len;
            var rot = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
            Add(Cube, Matrix4x4.TRS((a + b) * 0.5f, rot, V(t, t, len)), col);
        }

        // Open steel lattice tower standing on b
        public void Lattice(Vector3 b, float w, float h, float bay, float t, Color c, int floorEvery = 0)
        {
            float hw = w * 0.5f;
            var k4 = new[] { V(-hw, 0, -hw), V(hw, 0, -hw), V(hw, 0, hw), V(-hw, 0, hw) };
            foreach (var q in k4) Beam(b + q, b + q + Vector3.up * h, t * 1.6f, c);
            int n = Mathf.Max(1, Mathf.RoundToInt(h / bay));
            float step = h / n;
            var floor = new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 1f);
            for (int i = 0; i <= n; i++)
            {
                var y = Vector3.up * (i * step);
                for (int s = 0; s < 4; s++)
                {
                    Vector3 a = b + k4[s], e = b + k4[(s + 1) % 4];
                    Beam(a + y, e + y, t, c);
                    if (i < n)
                    {
                        var up = Vector3.up * step;
                        bool flip = ((i + s) & 1) == 0;
                        Beam(a + y + (flip ? Vector3.zero : up), e + y + (flip ? up : Vector3.zero), t * 0.7f, c);
                    }
                }
                if (floorEvery > 0 && i > 0 && i < n && i % floorEvery == 0) Box(b + y, V(w - t, t, w - t), floor);
            }
        }

        public GameObject Finish(string name, AscendCADData data, bool colliders)
        {
            var root = new GameObject(name);
            var owner = root.AddComponent<AscendPadMeshes>();
            foreach (var kv in parts)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(kv.Value.ToArray(), true, true);
                m.RecalculateBounds();
                owner.meshes.Add(m);
                var go = new GameObject("Mesh");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = CADModels.Lit(data);
                CADModels.Paint(r, kv.Key, null);
                if (colliders) go.AddComponent<MeshCollider>().sharedMesh = m;
            }
            return root;
        }
    }
}

// Frees the generated pad meshes when a pad is removed
public class AscendPadMeshes : MonoBehaviour
{
    public readonly List<Mesh> meshes = new List<Mesh>();
    void OnDestroy() { foreach (var m in meshes) if (m) Destroy(m); }
}

// ═════════════════════════════════════════════════════════════════════════════
//  GOOGEL: every AscendCAD part (and launch pad) as a Googel card.
//  Cards are added to your GoogelData asset automatically in the editor, so you
//  can give each one a picture. Size variants (tank sizes, fairings...) share one card.
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendGoogel
{
    public const string RocketPrefix = "AscendCAD Rocket: ", SatPrefix = "AscendCAD Satellite: ", PadCategory = "AscendCAD: Launch Pads";

    static readonly Dictionary<string, string> CatInfo = new Dictionary<string, string>
    {
        { "Propulsion", "Engines and solid motors from AscendCAD, with the thrust and efficiency the builder uses." },
        { "Propellant Tanks", "Every tank size in AscendCAD. Propellant and dry mass are calculated from volume and density." },
        { "Structure", "Interstages, diameter adapters, nose cones and avionics bays." },
        { "Payload Integration", "Fairings and payload adapters that carry your satellite." },
        { "Avionics (install inside a bay)", "Guidance, range safety, telemetry and power boxes. Install them inside an avionics bay or interstage." },
        { "Recovery & Aero", "Fins, grid fins and landing legs." },
        { "Heritage Hardware", "Real Saturn V, Apollo and Space Shuttle hardware used by the prebuilt rockets." },
        { "Satellite Structure", "CubeSat frames, smallsat buses and separation systems." },
        { "sat:Propulsion", "Thrusters that let a satellite change or hold its orbit: cold gas, electric and chemical." },
        { "Power", "Solar panels, batteries and power electronics." },
        { "Command & Data", "Computers, memory, radios and antennas." },
        { "Attitude Control (ADCS)", "Sensors and actuators that point the satellite." },
        { "Thermal", "Insulation, heaters and radiators that keep electronics at a safe temperature." },
        { "Scientific Instruments", "The reason a satellite flies: cameras, spectrometers, receivers and more." },
        { "Heritage Spacecraft", "Hardware from Maya-1, Diwata-1/2, Hayabusa and the James Webb Space Telescope." },
    };

    // History text borrowed from the hand-written Googel cards when a part matches one (e.g. "Merlin 1D", "RS-25")
    static Dictionary<string, GoogelPart> classic;

    public static List<GoogelCategory> Build(AscendCADData d)
    {
        var result = new List<GoogelCategory>();
        if (d == null || d.parts == null) return result;
        classic = new Dictionary<string, GoogelPart>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in GoogelDefaults.Build()) foreach (var g in c.parts) classic[g.title] = g;

        var byCat = new Dictionary<string, GoogelCategory>();
        foreach (var grp in d.parts.Where(x => x != null)
                     .GroupBy(x => (x.builder == CADBuilder.Rocket ? RocketPrefix : SatPrefix) + x.category))
        {
            var first = grp.First();
            string info;
            if (!(first.builder == CADBuilder.Payload && CatInfo.TryGetValue("sat:" + first.category, out info))) CatInfo.TryGetValue(first.category, out info);
            var cat = new GoogelCategory { name = grp.Key, description = info ?? "AscendCAD parts." };
            result.Add(cat);
            foreach (var sub in grp.GroupBy(x => x.subcategory))
            {
                // Families: the same part in several sizes ("Kerolox Tank Ø1.2 m x 3 m", "... Ø3.7 m x 12 m")
                foreach (var fam in sub.GroupBy(FamilyKey))
                {
                    var list = fam.ToList();
                    cat.parts.Add(list.Count > 1 ? FamilyCard(fam.Key, list) : PartCard(list[0]));
                }
            }
        }

        if (d.launchPads != null && d.launchPads.Count > 0)
        {
            var pads = new GoogelCategory { name = PadCategory, description = "Launch pads and support vehicles. Bought once, kept forever. Each has limits on rocket height, width and mass." };
            foreach (var p in d.launchPads) if (p != null) pads.parts.Add(PadCard(p, d));
            result.Add(pads);
        }
        classic = null;
        return result;
    }

    static string FamilyKey(CADPart p) { int i = p.name.IndexOf(" Ø", StringComparison.Ordinal); return i > 0 ? p.name.Substring(0, i) : p.name; }

    static string FirstSentence(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf(". ", StringComparison.Ordinal);
        return i > 0 ? s.Substring(0, i + 1) : s;
    }

    static string Size(CADPart p) =>
        p.IsBoxy || p.attach == CADAttach.Radial ? $"{p.boxSize.x:0.###} x {p.boxSize.y:0.###} x {p.boxSize.z:0.###} m"
        : p.topDiameter > 0f ? $"Ø{p.diameter:0.##} to Ø{p.topDiameter:0.##} m, {p.length:0.##} m tall" : $"Ø{p.diameter:0.##} m x {p.length:0.##} m";

    static string Mounting(CADPart p)
    {
        switch (p.attach)
        {
            case CADAttach.Internal: return "Installs inside a bay, interstage or satellite structure.";
            case CADAttach.Radial: return "Mounts on the side or face of a part (symmetry works).";
            case CADAttach.RadialUpright: return "Straps onto the side of a core stage.";
            default: return p.role == CADRole.Engine ? "Hangs under a tank (symmetry > 1 makes a cluster)." : "Stacks on top of or under other parts.";
        }
    }

    static string Specs(CADPart p)
    {
        var l = new List<string> { $"AscendCAD section: {p.category} > {p.subcategory}" };
        l.Add(p.propellantKg > 0f ? $"Mass: {AscendReport.Mass(p.WetMassKg)} full, {AscendReport.Mass(p.dryMassKg)} empty" : $"Mass: {AscendReport.Mass(p.dryMassKg)}");
        if (p.propellantKg > 0f) l.Add($"Propellant: {AscendReport.Mass(p.propellantKg)} {AscendReport.PropName(p.propellant)}");
        if (p.role == CADRole.Engine) l.Add($"Burns: {AscendReport.PropName(p.propellant)}");
        if (p.thrustVackN > 0f) l.Add(p.thrustSLkN > 0f ? $"Thrust: {p.thrustSLkN:N0} kN sea level, {p.thrustVackN:N0} kN vacuum" : $"Thrust: {p.thrustVackN:N1} kN (vacuum only)");
        if (p.ispVac > 0f) l.Add(p.ispSL > 0f ? $"Specific impulse: {p.ispSL:0} s sea level, {p.ispVac:0} s vacuum" : $"Specific impulse: {p.ispVac:0} s vacuum");
        if (p.burnTimeS > 0f) l.Add($"Burn time: {p.burnTimeS:0} s");
        if (p.attach != CADAttach.Internal) l.Add($"Size: {Size(p)}");
        if (p.stowedSize != Vector3.zero) l.Add($"Folded for launch: {p.stowedSize.x:0.##} x {p.stowedSize.y:0.##} x {p.stowedSize.z:0.##} m");
        if (p.powerGenW > 0f) l.Add($"Power output: {p.powerGenW:0.#} W in full sun");
        if (p.powerUseW > 0f) l.Add($"Power draw: {p.powerUseW:0.##} W ({p.dutyCycle * 100f:0}% of the time)");
        if (p.batteryWh > 0f) l.Add($"Battery capacity: {p.batteryWh:N0} Wh");
        if (p.dataRateKbps > 0f) l.Add($"Data rate: {AscendReport.Rate(p.dataRateKbps)}");
        if (p.volumeL > 0f) l.Add($"Volume: {p.volumeL:0.##} L");
        if (p.internalVolumeL > 0f) l.Add($"Room inside: {p.internalVolumeL:0.##} L");
        if (p.maxPayloadKg > 0f) l.Add($"Max payload: {AscendReport.Mass(p.maxPayloadKg)}");
        if (p.innerDiameter > 0f) l.Add($"Usable space inside: Ø{p.innerDiameter:0.##} m x {p.innerHeight:0.##} m");
        return string.Join("\n", l);
    }

    static string Cost(CADPart p) =>
        AscendReport.MoneyFull(p.costUSD) + (p.costEstimated ? " (estimate)" : "") + ". " +
        (string.IsNullOrEmpty(p.source) ? "" : "Where the numbers come from: " + p.source + ".");

    static GoogelPart Classic(string name)
    {
        if (classic == null) return null;
        if (classic.TryGetValue(name, out var g)) return g;
        foreach (var kv in classic) if (name.StartsWith(kv.Key + " ", StringComparison.OrdinalIgnoreCase) || name.StartsWith(kv.Key + "-", StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    static GoogelPart PartCard(CADPart p)
    {
        var c = Classic(p.name);
        return new GoogelPart
        {
            title = p.name,
            shortDescription = FirstSentence(p.description),
            purpose = (c != null ? c.purpose + "\n\n" : "") + p.description + " " + Mounting(p),
            specs = Specs(p),
            cost = Cost(p),
            history = c != null ? c.history : $"Part of the AscendCAD catalog ({p.category} > {p.subcategory}). Figures follow the real hardware where published; anything marked estimate is an engineering guess."
        };
    }

    static GoogelPart FamilyCard(string key, List<CADPart> fam)
    {
        var p0 = fam[0];
        var rows = new List<string> { $"AscendCAD section: {p0.category} > {p0.subcategory}", $"Sizes available: {fam.Count}" };
        foreach (var p in fam)
        {
            string size = p.name.Length > key.Length ? p.name.Substring(key.Length).Trim() : Size(p);
            string extra = p.propellantKg > 0f ? $"{AscendReport.Mass(p.propellantKg)} propellant, {AscendReport.Mass(p.dryMassKg)} empty"
                : p.maxPayloadKg > 0f ? $"{AscendReport.Mass(p.dryMassKg)}, carries up to {AscendReport.Mass(p.maxPayloadKg)}"
                : p.internalVolumeL > 0f ? $"{AscendReport.Mass(p.dryMassKg)}, {p.internalVolumeL:0} L inside"
                : AscendReport.Mass(p.dryMassKg);
            rows.Add($"{size}: {extra}, {AscendReport.Money(p.costUSD)}");
        }
        return new GoogelPart
        {
            title = key,
            shortDescription = $"{fam.Count} sizes in AscendCAD. " + FirstSentence(p0.description),
            purpose = p0.description + " " + Mounting(p0) + "\n\nBigger sizes hold more but weigh and cost more; match the diameter of the parts above and below it.",
            specs = string.Join("\n", rows),
            cost = $"From {AscendReport.Money(fam.Min(x => x.costUSD))} to {AscendReport.Money(fam.Max(x => x.costUSD))} depending on size" +
                   (p0.costEstimated ? " (estimates)" : "") + ". " + (string.IsNullOrEmpty(p0.source) ? "" : "Where the numbers come from: " + p0.source + "."),
            history = $"Part of the AscendCAD catalog ({p0.category} > {p0.subcategory})."
        };
    }

    static GoogelPart PadCard(CADLaunchPad p, AscendCADData d)
    {
        var l = new List<string> { p.isTransporter ? "Type: Support vehicle" : "Type: Launch pad" };
        if (p.isTransporter) l.Add($"Carries up to: {p.carryCapacityT:N0} t (rocket + launcher)");
        else
        {
            l.Add("Max rocket height: " + (p.maxHeightM > 0 ? p.maxHeightM.ToString("0.#") + " m" : "any"));
            l.Add("Max rocket width: " + (p.maxWidthM > 0 ? p.maxWidthM.ToString("0.#") + " m" : "any"));
            l.Add("Max liftoff mass: " + (p.maxLiftoffMassT > 0 ? p.maxLiftoffMassT.ToString("N0") + " t" : "any"));
            var req = d.GetPad(p.requiresId);
            if (req != null) l.Add("Needs: " + req.name);
        }
        return new GoogelPart
        {
            title = p.name, image = p.image,
            shortDescription = FirstSentence(p.description),
            purpose = p.description + " Buy it once on the AscendCAD Launch Pad tab and choose it for any compatible rocket.",
            specs = string.Join("\n", l),
            cost = (p.ownedFromStart ? "Free: provided by the government." : AscendReport.MoneyFull(p.costUSD) + (p.costEstimated ? " (estimate)." : ".")) +
                   (string.IsNullOrEmpty(p.source) ? "" : " Where the numbers come from: " + p.source + "."),
            history = "Launch infrastructure in AscendCAD."
        };
    }

    // Adds missing categories/cards. With refreshText, existing cards get the latest text (your pictures are always kept).
    public static int Merge(List<GoogelCategory> into, List<GoogelCategory> add, bool refreshText)
    {
        int changed = 0;
        foreach (var c in add)
        {
            var have = into.FirstOrDefault(x => x != null && string.Equals(x.name, c.name, StringComparison.OrdinalIgnoreCase));
            if (have == null) { into.Add(c); changed += c.parts.Count; continue; }
            if (refreshText) have.description = c.description;
            foreach (var p in c.parts)
            {
                var hp = have.parts.FirstOrDefault(x => x != null && string.Equals(x.title, p.title, StringComparison.OrdinalIgnoreCase));
                if (hp == null) { have.parts.Add(p); changed++; continue; }
                if (!refreshText) continue;
                hp.shortDescription = p.shortDescription; hp.purpose = p.purpose; hp.specs = p.specs; hp.cost = p.cost; hp.history = p.history;
                if (!hp.image && p.image) hp.image = p.image;
                changed++;
            }
        }
        return changed;
    }

#if UNITY_EDITOR
    // Picks your AscendCAD Data asset if it's up to date, otherwise the built-in catalog
    static AscendCADData EditorData(out bool temporary)
    {
        temporary = false;
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:AscendCADData"))
        {
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<AscendCADData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (a && a.parts != null && a.parts.Count > 0 && a.catalogVersion >= AscendCADData.CatalogVersion) return a;
        }
        temporary = true;
        return AscendCADData.CreateDefault();
    }

    public static void SyncAsset(GoogelData g, bool refreshText)
    {
        if (!g) return;
        var d = EditorData(out bool temp);
        if (g.categories == null) g.categories = new List<GoogelCategory>();
        int n = Merge(g.categories, Build(d), refreshText);
        if (temp) Object.DestroyImmediate(d);
        if (n > 0) { UnityEditor.EditorUtility.SetDirty(g); UnityEditor.AssetDatabase.SaveAssetIfDirty(g); }
        if (n > 0 || refreshText) Debug.Log($"Googel: {(refreshText ? "updated" : "added")} {n} AscendCAD card(s) in '{g.name}'. Give them pictures in the Inspector.", g);
    }

    // Right-click the GoogelData asset's ⋮ menu
    [UnityEditor.MenuItem("CONTEXT/GoogelData/Add missing AscendCAD parts")]
    static void MenuAdd(UnityEditor.MenuCommand c) => SyncAsset(c.context as GoogelData, false);

    [UnityEditor.MenuItem("CONTEXT/GoogelData/Refresh AscendCAD part text (keeps your pictures)")]
    static void MenuRefresh(UnityEditor.MenuCommand c) => SyncAsset(c.context as GoogelData, true);

    // Automatic: after every script reload, new AscendCAD parts appear in every GoogelData asset
    [UnityEditor.InitializeOnLoadMethod]
    static void AutoSync()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:GoogelData"))
                SyncAsset(UnityEditor.AssetDatabase.LoadAssetAtPath<GoogelData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)), false);
        };
    }
#endif
}

// ═════════════════════════════════════════════════════════════════════════════
//  LAUNCH PAD COMPATIBILITY
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendPads
{
    public static float RocketMassT(CADStats rs) => rs == null ? 0f : (rs.wetMass + rs.payloadMass) / 1000f;

    // Reasons this rocket can't use the pad (empty list = compatible)
    public static List<string> Problems(CADLaunchPad p, CADStats rs, AscendCADData data)
    {
        var l = new List<string>();
        if (p == null || rs == null || rs.partCount == 0) return l;
        float m = RocketMassT(rs);
        if (p.isTransporter)
        {
            float heaviest = 0f; string who = null;
            foreach (var q in data.launchPads) if (q != null && q.requiresId == p.id && q.ownMassT > heaviest) { heaviest = q.ownMassT; who = q.name; }
            if (p.carryCapacityT > 0f && m + heaviest > p.carryCapacityT)
                l.Add($"Rocket {m:N0} t + {who ?? "launcher"} {heaviest:N0} t = {m + heaviest:N0} t, but it can only carry {p.carryCapacityT:N0} t.");
            return l;
        }
        if (p.id != "pad.rail" && rs.width < 0.4f && m < 0.1f)
            l.Add("This is a small hobby rocket: it launches from the CanSat Launch Rail, not from this pad.");
        if (p.maxHeightM > 0f && rs.height > p.maxHeightM) l.Add($"Rocket is {rs.height:0.#} m tall; this pad takes up to {p.maxHeightM:0.#} m.");
        if (p.maxWidthM > 0f && rs.width > p.maxWidthM) l.Add($"Rocket is {rs.width:0.#} m wide; this pad takes up to {p.maxWidthM:0.#} m.");
        if (p.maxLiftoffMassT > 0f && m > p.maxLiftoffMassT) l.Add($"Rocket weighs {m:N0} t; this pad holds up to {p.maxLiftoffMassT:N0} t.");
        var t = data.GetPad(p.requiresId);
        if (t != null && t.carryCapacityT > 0f && m + p.ownMassT > t.carryCapacityT)
            l.Add($"Rocket + launcher ({m + p.ownMassT:N0} t) is too heavy for the {t.name} ({t.carryCapacityT:N0} t).");
        return l;
    }

    // Something you still need to buy before this pad can be used (null = nothing)
    public static string Lock(CADLaunchPad p, AscendCADData data)
    {
        var t = p == null ? null : data.GetPad(p.requiresId);
        return t != null && !AscendEconomy.OwnsPad(t, data) ? $"Needs the {t.name} to roll it out. Buy that first." : null;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  PREBUILT ROCKETS: real vehicles assembled from catalog parts
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendTemplates
{
    public static readonly string[] Names = { "Saturn V", "Falcon 9", "Space Shuttle" };
    public static readonly string[] Blurbs =
    {
        "3 stages: 5x F-1, 5x J-2, 1x J-2, with Apollo on top.",
        "2 stages: 9x Merlin 1D + Merlin Vacuum, grid fins and legs.",
        "Orbiter + External Tank + 2 Solid Rocket Boosters.",
    };

    public static readonly string[] SatNames = { "Maya-1", "Diwata-1", "Diwata-2", "Hayabusa", "James Webb" };
    public static readonly string[] SatBlurbs =
    {
        "Philippines' first CubeSat (1U, 2018) with an APRS ham digipeater.",
        "Philippines' first microsatellite (2016): telescope + multispectral imager.",
        "Diwata-1's successor (2018): fold-out solar paddles + amateur radio unit.",
        "JAXA asteroid sample-return probe (2003) with ion engines.",
        "NASA/ESA/CSA infrared observatory (2021): 6.5 m gold mirror, tennis-court sunshield.",
    };

    public static CADDesign BuildSat(int i, AscendCADData data)
    {
        switch (i)
        {
            case 0: return Maya1(data);
            case 1: return Diwata(data, false);
            case 2: return Diwata(data, true);
            case 3: return Hayabusa(data);
            default: return Webb(data);
        }
    }

    static CADDesign Maya1(AscendCADData data)
    {
        var b = new B(data, "Maya-1", CADBuilder.Payload);
        int sep = b.Root("sat.sep.springs"), cube = b.On(sep, "sat.str.1u");
        b.Side(cube, "sat.sol.1u", 4, 0f, 0f);
        b.Face(cube, "sat.sol.1u", true, Vector2.zero);
        b.Side(cube, "sat.ant.uhf", 1, 0.04f, 45f);
        b.Inside(cube, "sat.obc.cube", "sat.eps.cube", "sat.bat.40", "sat.com.uhf", "sat.adcs.passive", "sci.aru", "sci.cam.tiny", "sat.th.mli");
        return b.Done();
    }

    static CADDesign Diwata(AscendCADData data, bool two)
    {
        var b = new B(data, two ? "Diwata-2" : "Diwata-1", CADBuilder.Payload);
        int sep = b.Root("sat.sep.lb15"), bus = b.On(sep, two ? "ph.bus.d2" : "ph.bus.d1");
        b.Side(bus, "sat.sol.sm", 2, 0f, 0f);                                   // body panels front + back
        if (two) b.Side(bus, "ph.dsap", 2, 0f, 90f);                            // fold-out paddles left + right
        else b.Side(bus, "sat.sol.6u", 2, 0f, 90f);
        // Earth-facing cameras on top
        if (two)
        {
            b.Face(bus, "sci.hpt", true, new Vector2(-0.13f, -0.1f));
            b.Face(bus, "sci.smi", true, new Vector2(0.12f, -0.12f));
            b.Face(bus, "sci.erc", true, new Vector2(0.12f, 0.1f));
            b.Face(bus, "sci.mfc", true, new Vector2(-0.15f, 0.15f));
            b.Face(bus, "sci.wfc", true, new Vector2(0f, 0.15f));
            b.Side(bus, "sat.ant.uhf", 2, 0.2f, 0f);                            // VHF + UHF ham antennas
        }
        else
        {
            b.Face(bus, "sci.hpt", true, new Vector2(-0.15f, 0f));
            b.Face(bus, "sci.smi", true, new Vector2(0.05f, 0f));
            b.Face(bus, "sci.mfc", true, new Vector2(0.2f, -0.08f));
            b.Face(bus, "sci.wfc", true, new Vector2(0.2f, 0.08f));
            b.Side(bus, "sat.ant.uhf", 1, 0.2f, 90f);
        }
        b.Side(bus, "sat.ant.x", 1, -0.2f, 180f);
        b.Inside(bus, "sat.obc.cube", "sat.obc.mmu", "sat.eps.pcdu", "sat.bat.99", "sat.com.x", "sat.com.uhf",
            "sat.adcs.cubestar", "sat.adcs.sun", "sat.adcs.mag", "sat.adcs.gnss", "sat.adcs.mtq",
            "sat.adcs.rwl", "sat.adcs.rwl", "sat.adcs.rwl", "sat.adcs.rwl", "sat.th.mlibig", "sat.th.heat");
        if (two) b.Inside(bus, "sat.obc.cube", "sci.aru");                      // second OBC + amateur radio unit
        return b.Done();
    }

    static CADDesign Hayabusa(AscendCADData data)
    {
        var b = new B(data, "Hayabusa", CADBuilder.Payload);
        int sep = b.Root("sat.sep.clamp"), bus = b.On(sep, "hay.bus");
        b.Side(bus, "hay.solar", 2, 0f, 90f);
        b.Face(bus, "hay.hga", true, Vector2.zero);
        b.Side(bus, "hay.ion", 1, 0f, 180f);
        b.Side(bus, "hay.capsule", 1, 0.25f, 0f, false, -0.45f);
        b.Side(bus, "hay.horn", 1, -0.3f, 0f);
        b.Side(bus, "hay.amica", 1, 0.25f, 0f, false, 0.1f);
        b.Side(bus, "hay.lidar", 1, 0.25f, 0f, false, 0.5f);
        b.Side(bus, "hay.xrs", 1, -0.25f, 0f, false, 0.5f);
        b.Side(bus, "hay.minerva", 1, -0.3f, 0f, false, -0.5f);
        b.Inside(bus, "sat.obc.rad", "sat.eps.pcdu", "sat.bat.1k", "sat.com.x", "sat.adcs.st",
            "sat.adcs.rwbig", "sat.adcs.rwbig", "sat.adcs.rwbig", "sat.adcs.sun", "sat.th.mlibig", "sat.th.heat", "hay.rcs", "hay.nirs");
        return b.Done();
    }

    static CADDesign Webb(AscendCADData data)
    {
        var b = new B(data, "James Webb Space Telescope", CADBuilder.Payload);
        int sep = b.Root("sat.sep.clamp1666"), bus = b.On(sep, "jwst.bus"), isim = b.On(bus, "jwst.isim");
        b.Face(bus, "jwst.shield", true, Vector2.zero);                         // sunshield between the hot bus and the cold telescope
        b.Side(isim, "jwst.mirror", 1, 0.3f, 0f);
        b.Side(isim, "jwst.secondary", 1, 0.3f, 0f);
        b.Side(bus, "jwst.solar", 1, 0f, 180f);
        b.Face(bus, "sat.ant.dish", false, new Vector2(0.8f, 0.8f));            // high-gain antenna pointing at Earth
        b.Inside(bus, "sat.obc.rad", "sat.eps.pcdu", "sat.bat.1k", "sat.com.xhr", "sat.adcs.st", "sat.adcs.st",
            "sat.adcs.rwbig", "sat.adcs.rwbig", "sat.adcs.rwbig", "sat.adcs.rwbig", "sat.adcs.rwbig", "sat.adcs.rwbig",
            "sat.adcs.sun", "sat.prop.hydrazine", "sat.prop.hydrazine", "sat.th.mlibig", "sat.th.heat");
        b.Inside(isim, "jwst.nircam", "jwst.nirspec", "jwst.miri", "jwst.niriss");
        return b.Done();
    }

    static readonly string[] Avionics = { "av.fc", "av.imu.rlg", "av.gnss", "av.afts", "av.tlm", "av.cmd", "av.batt", "av.pdu", "av.harness" };

    public static CADDesign Build(int i, AscendCADData data)
    {
        switch (i)
        {
            case 0: return SaturnV(data);
            case 1: return Falcon9(data);
            default: return Shuttle(data);
        }
    }

    static CADDesign SaturnV(AscendCADData data)
    {
        var b = new B(data, "Saturn V");
        Color white = new Color(0.95f, 0.95f, 0.96f), black = new Color(0.1f, 0.1f, 0.11f);
        // S-IC: kerosene first stage
        int a = b.Root("tank.kerolox.10.1x12"), m = b.On(a, "tank.kerolox.10.1x12"), t = b.On(m, "tank.kerolox.10.1x3");
        b.Engines(a, "eng.f1", 4, true, 45f);
        b.Side(a, "aero.fin.l", 4, -4.8f, 45f, false);
        b.Paint(t, black);
        // S-II: hydrogen second stage (the J-2s hide inside the interstage)
        int i1 = b.On(t, "str.interstage.10.1");
        int s2 = b.On(i1, "tank.hydrolox.10.1x12"), s2b = b.On(s2, "tank.hydrolox.10.1x3"), s2c = b.On(s2b, "tank.hydrolox.10.1x1.5");
        b.Engines(s2, "eng.j2", 4, true, 45f);
        // S-IVB: third stage + Instrument Unit + Apollo
        int i2 = b.On(s2c, "str.interstage.saturn");
        int s3 = b.On(i2, "tank.hydrolox.6.6x6"), s3b = b.On(s3, "tank.hydrolox.6.6x3");
        b.Engines(s3, "eng.j2", 0, true);
        int iu = b.On(s3b, "str.avbay.6.6");
        b.Inside(iu, Avionics);
        int sla = b.On(iu, "apollo.sla"), csm = b.On(sla, "apollo.csm");
        b.On(csm, "apollo.les");
        foreach (int k in new[] { a, m, i1, s2, s2b, s2c, i2, s3, s3b }) b.Paint(k, white);
        return b.Done();
    }

    static CADDesign Falcon9(AscendCADData data)
    {
        var b = new B(data, "Falcon 9");
        Color white = new Color(0.95f, 0.95f, 0.96f);
        int a = b.Root("tank.kerolox.3.7x12"), m1 = b.On(a, "tank.kerolox.3.7x12"), m2 = b.On(m1, "tank.kerolox.3.7x12"), t = b.On(m2, "tank.kerolox.3.7x3");
        b.Engines(a, "eng.merlin1d", 8, true, 0f);        // the Octaweb: 8 around 1
        b.Side(a, "rec.leg", 4, -2.6f, 45f, false);
        b.Side(t, "rec.gridfin", 4, 0f, 45f, false);
        int inter = b.On(t, "str.interstage.3.7");
        int u = b.On(inter, "tank.kerolox.3.7x6"), u2 = b.On(u, "tank.kerolox.3.7x3"), u3 = b.On(u2, "tank.kerolox.3.7x1.5");
        b.Engines(u, "eng.mvac", 0, true);                 // hangs inside the black interstage
        int av = b.On(u3, "str.avbay.3.7");
        b.Inside(av, Avionics);
        int ad = b.On(av, "str.adapter.3.7-5");
        b.On(ad, "fair.5.2");
        b.Paint(av, white); b.Paint(ad, white);
        return b.Done();
    }

    static CADDesign Shuttle(AscendCADData data)
    {
        var b = new B(data, "Space Shuttle");
        int et = b.Root("sts.et");
        b.Side(et, "srb.shuttle", 2, -1.0f, 90f, true);    // boosters left and right
        int orb = b.Side(et, "sts.orbiter", 1, -4.2f, 0f, true)[0];   // orbiter on the front, belly to the tank
        b.Inside(orb, Avionics);
        return b.Done();
    }

    // ═════════════ MISSION GUIDE: auto-designed suggestions (built like the editor's snapping) ═════════════
    static string Fm(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    static readonly string[] AvionicsBasic = { "av.fc", "av.imu.mems", "av.gnss", "av.afts", "av.tlm", "av.batt" };

    // Small hobby rocket for the CanSat (Sky Sensor) mission
    public static CADDesign AutoHobby(AscendCADData data, string name, bool bigMotor, int fins, bool extras)
    {
        var b = new B(data, name);
        int motor = b.Root(bigMotor ? "snd.motor.k" : "snd.motor.j");
        b.Side(motor, "snd.fin", fins, -0.2f, 0f);
        int tube = b.On(motor, "snd.tube");
        if (extras) b.Inside(tube, "snd.alt", "snd.imu", "snd.gps", "snd.batt");
        else b.Inside(tube, "snd.alt", "snd.batt");
        int bay = b.On(tube, "snd.bay");
        b.On(bay, "snd.nose");
        return b.Done();
    }

    // Two-stage liquid rocket: stage 1 tanks + engines, interstage, stage 2 tanks + engine, avionics bay, fairing sized for the payload
    public static CADDesign AutoLiquid(AscendCADData data, string name, float d, string e1, int n1, float[] s1, string p1,
        string e2, float[] s2, string p2, float payW, float payH)
    {
        var b = new B(data, name);
        string T(string prop, float len) => "tank." + prop + "." + Fm(d) + "x" + Fm(len);
        int root = b.Root(T(p1, s1[0])), top = root;
        for (int i = 1; i < s1.Length; i++) top = b.On(top, T(p1, s1[i]));
        b.Engines(root, e1, n1 > 1 ? n1 - 1 : 0, true, 0f);
        int inter = b.On(top, "str.interstage." + Fm(d));
        int u = b.On(inter, T(p2, s2[0])), top2 = u;
        for (int i = 1; i < s2.Length; i++) top2 = b.On(top2, T(p2, s2[i]));
        b.Engines(u, e2, 0, true);
        int av = b.On(top2, "str.avbay." + Fm(d));
        b.Inside(av, AvionicsBasic);
        // Fairing: same width as the rocket if the payload fits, otherwise widen with an adapter
        float[] dias = { 1.2f, 1.8f, 2.5f, 3.7f, 5f };
        float[] fl = { 2.5f, 3.6f, 6f, 9.5f, 13.1f };
        int k = Array.IndexOf(dias, d);
        if (k < 0) k = 0;
        int below = av;
        while (k < dias.Length - 1 && (payW > dias[k] * 0.88f || payH > fl[k] * 0.78f))
        {
            below = b.On(below, "str.adapter." + Fm(dias[k]) + "-" + Fm(dias[k + 1]));
            k++;
        }
        b.On(below, "fair." + (dias[k] >= 5f ? "5.2" : Fm(dias[k])));
        return b.Done();
    }

    // Every rocket the guide considers for a mission
    public static List<CADDesign> AutoRockets(AscendSimMission m, AscendCADData data, float payW, float payH)
    {
        var list = new List<CADDesign>();
        void Try(Func<CADDesign> f) { try { var d = f(); if (d != null) list.Add(d); } catch (Exception) { } }
        if (m == AscendSimMission.SkySensor)
        {
            Try(() => AutoHobby(data, "Tarsier K (basic)", false, 3, false));
            Try(() => AutoHobby(data, "Tarsier K", false, 4, true));
            Try(() => AutoHobby(data, "Tarsier L (higher)", true, 4, true));
            return list;
        }
        var s1Small = new[] { new[] { 12f }, new[] { 12f, 6f }, new[] { 12f, 12f } };
        var s2Small = new[] { new[] { 1.5f }, new[] { 3f }, new[] { 6f } };
        foreach (int n in new[] { 5, 7, 9 })
            foreach (var a in s1Small) foreach (var c in s2Small)
            { var n1 = n; var aa = a; var cc = c; Try(() => AutoLiquid(data, $"Maya-L {n1}R", 1.2f, "eng.rutherford", n1, aa, "kerolox", "eng.rutherfordvac", cc, "kerolox", payW, payH)); }
        var s1Med = new[] { new[] { 12f }, new[] { 12f, 12f }, new[] { 12f, 12f, 12f } };
        var s2Med = new[] { new[] { 2f }, new[] { 4f }, new[] { 4f, 4f } };
        foreach (var a in s1Med) foreach (var c in s2Med)
        { var aa = a; var cc = c; Try(() => AutoLiquid(data, "Agila 1M", 1.8f, "eng.merlin1d", 1, aa, "kerolox", "eng.aj10", cc, "hypergolic", payW, payH)); }
        var s1Big = new[] { new[] { 12f, 12f }, new[] { 12f, 12f, 12f }, new[] { 12f, 12f, 12f, 6f } };
        var s2Big = new[] { new[] { 3f }, new[] { 6f }, new[] { 12f } };
        foreach (int n in new[] { 5, 7, 9 })
            foreach (var a in s1Big) foreach (var c in s2Big)
            { var n1 = n; var aa = a; var cc = c; Try(() => AutoLiquid(data, $"Haribon {n1}M", 3.7f, "eng.merlin1d", n1, aa, "kerolox", "eng.mvac", cc, "kerolox", payW, payH)); }
        return list;
    }

    // Satellites for each mission: tier 0 = budget, 1 = balanced, 2 = best
    public static CADDesign AutoSat(AscendSimMission m, int tier, AscendCADData data)
    {
        if (m == AscendSimMission.SkySensor)
        {
            var b = new B(data, tier == 0 ? "Sky Sensor CanSat" : "Sky Sensor CanSat Plus", CADBuilder.Payload);
            int ej = b.Root("can.eject"), can = b.On(ej, "can.shell");
            b.Face(can, "can.chute", true, Vector2.zero);
            b.Side(can, "can.ant", 1, 0f, 0f);
            b.Inside(can, "can.obc", "can.sensor", "can.radio", "can.batt");
            if (tier > 0) b.Inside(can, "can.humid", "sci.cam.tiny");
            return b.Done();
        }
        if (m == AscendSimMission.EarthWatcher)
        {
            if (tier >= 2) return Diwata(data, false);
            if (tier == 0)
            {
                var b = new B(data, "Bantay 3U", CADBuilder.Payload);
                int sep = b.Root("sat.sep.springs"), f = b.On(sep, "sat.str.3u");
                b.Side(f, "sat.sol.3u", 4, 0f, 0f);
                b.Side(f, "sat.ant.uhf", 1, 0.1f, 45f);
                b.Inside(f, "sat.obc.cube", "sat.eps.cube", "sat.bat.40", "sat.com.uhf", "sat.adcs.3ax", "sci.mantis", "sat.th.mli");
                return b.Done();
            }
            var c = new B(data, "Bantay 6U", CADBuilder.Payload);
            int s6 = c.Root("sat.sep.cubesat"), f6 = c.On(s6, "sat.str.6u");
            c.Side(f6, "sat.sol.6ud", 2, 0f, 90f);
            c.Side(f6, "sat.ant.s", 1, 0f, 0f);
            c.Inside(f6, "sat.obc.cube", "sat.eps.cube", "sat.bat.99", "sat.com.s", "sat.adcs.3ax", "sci.ms100", "sat.th.mli");
            return c.Done();
        }
        // Moon Mapper: needs its own engine to brake into lunar orbit
        var mb = new B(data, tier == 0 ? "Buwan Mapper" : tier == 1 ? "Buwan Mapper Plus" : "Buwan Mapper Pro", CADBuilder.Payload);
        int msep = mb.Root("sat.sep.lb24"), bus = mb.On(msep, "sat.str.100");
        mb.Side(bus, "sat.sol.sm", 4, 0f, 0f);
        mb.Face(bus, tier == 0 ? "sci.mfc" : "sci.monitor", true, new Vector2(-0.15f, 0f));
        if (tier >= 2) { mb.Face(bus, "hay.lidar", true, new Vector2(0.15f, 0f)); mb.Face(bus, "sat.ant.dish", false, new Vector2(0.15f, 0.15f)); }
        else mb.Side(bus, "sat.ant.x", 1, 0.2f, 45f);
        mb.Inside(bus, tier == 0 ? "sat.obc.cube" : "sat.obc.rad", "sat.eps.cube", tier == 0 ? "sat.bat.99" : "sat.bat.1k",
            tier >= 2 ? "sat.com.xhr" : "sat.com.x", "sat.adcs.3ax", "sat.prop.hydrazine", "sat.prop.hydrazine", "sat.th.mli");
        if (tier >= 1) mb.Inside(bus, "sat.prop.hydrazine");
        return mb.Done();
    }

    // Small helper that places parts exactly like the editor's snapping does
    class B
    {
        readonly CADDesign d;
        readonly AscendCADData data;
        public B(AscendCADData data, string name, CADBuilder builder = CADBuilder.Rocket) { this.data = data; d = new CADDesign { name = name, builder = builder }; }

        CADPart P(string id) => data.Get(id) ?? throw new Exception($"Part '{id}' is missing. Select your AscendCAD Data asset and use its menu: Load default parts.");

        int Add(CADPart p, int parent, CADLink link, Vector3 pos, Quaternion rot, int group = -1)
        {
            d.parts.Add(new CADPlaced { partId = p.id, parent = parent, link = link, pos = pos, rot = rot, color = p.defaultColor, symGroup = group });
            return d.parts.Count - 1;
        }

        public int Root(string id) { var p = P(id); return Add(p, -1, CADLink.Root, new Vector3(0f, p.StackHeight * 0.5f, 0f), Quaternion.identity); }

        public int On(int below, string id)
        {
            var bl = d.parts[below]; var bp = P(bl.partId); var p = P(id);
            return Add(p, below, CADLink.Top, bl.pos + Vector3.up * ((bp.StackHeight + p.StackHeight) * 0.5f), Quaternion.identity);
        }

        public void Engines(int tank, string id, int ring, bool center, float startDeg = 0f)
        {
            var T = d.parts[tank]; var tp = P(T.partId); var p = P(id);
            float Rt = tp.diameter * 0.5f, Re = p.MaxRadius, y = -(tp.StackHeight + p.length) * 0.5f;
            if (center) Add(p, tank, CADLink.Cluster, T.pos + Vector3.up * y, Quaternion.identity);
            if (ring <= 0) return;
            float rr = Mathf.Max(Rt - Re * 1.02f, Re * 1.1f);
            if (center) rr = Mathf.Max(rr, Re * 2.05f);
            int g = d.nextSymGroup++;
            for (int k = 0; k < ring; k++)
            {
                var dir = Quaternion.Euler(0f, startDeg + 360f / ring * k, 0f) * Vector3.forward;
                Add(p, tank, CADLink.Cluster, T.pos + dir * rr + Vector3.up * y, Quaternion.identity, g);
            }
        }

        // Top (up = true) or bottom face of a part, at an x/z offset from its center
        public int Face(int target, string id, bool up, Vector2 offset)
        {
            var T = d.parts[target]; var tp = P(T.partId); var p = P(id);
            var face = up ? Vector3.up : Vector3.down;
            return Add(p, target, CADLink.Radial, T.pos + new Vector3(offset.x, tp.StackHeight * 0.5f * face.y, offset.y),
                Quaternion.LookRotation(face, Vector3.forward));
        }

        public List<int> Side(int target, string id, int n, float yOffset, float startDeg, bool upright = false, float sideOffset = 0f)
        {
            var T = d.parts[target]; var tp = P(T.partId); var p = P(id);
            float half = tp.StackHeight * 0.5f, h01 = Mathf.Clamp01((yOffset + half) / (2f * half));
            int g = n > 1 ? d.nextSymGroup++ : -1;
            var r = new List<int>();
            for (int k = 0; k < n; k++)
            {
                var dk = Quaternion.Euler(0f, startDeg + 360f / n * k, 0f) * Vector3.forward;
                float dist = CADModels.SurfaceDistance(tp, dk, h01) + (upright ? p.MaxRadius * 1.02f : 0f);
                var tangent = Vector3.Cross(Vector3.up, dk);
                r.Add(Add(p, target, upright ? CADLink.RadialUpright : CADLink.Radial, T.pos + dk * dist + Vector3.up * yOffset + tangent * sideOffset,
                    Quaternion.LookRotation(dk, Vector3.up), g));
            }
            return r;
        }

        public void Inside(int host, params string[] ids) { var H = d.parts[host]; foreach (var id in ids) Add(P(id), host, CADLink.Internal, H.pos, H.rot); }
        public void Paint(int i, Color c) => d.parts[i].color = c;

        public CADDesign Done()
        {
            float y = CADStats.DesignBounds(d, data).min.y;
            foreach (var p in d.parts) p.pos.y -= y;   // stand on the ground
            return d;
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  MISSION GUIDE: what each mission needs, a live checklist, and money-aware
//  suggestions (Budget / Balanced / Best) for the rocket and the payload
// ═════════════════════════════════════════════════════════════════════════════
public static class AscendMissionGuide
{
    public const float MoonBrakeDv = 900f;   // lunar orbit insertion (~0.85 km/s) + margin
    static bool overridden;
    static AscendSimMission chosen;
    public static event Action MissionChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { overridden = false; MissionChanged = null; }

    // The mission picked in the briefing (or changed in AscendCAD / the Mission Simulator)
    public static AscendSimMission Mission
    {
        get => overridden ? chosen
             : MissionBriefing.SelectedMissionIndex >= 0 ? (AscendSimMission)Mathf.Clamp(MissionBriefing.SelectedMissionIndex, 0, 2)
             : AscendSimMission.EarthWatcher;
        set { overridden = true; chosen = value; MissionChanged?.Invoke(); }
    }

    public static string Title => AscendSimPlan.Title(Mission);

    // Delta-v the rocket must give (including gravity and drag losses)
    public static float RequiredDeltaV(AscendCADData d) =>
        Mission == AscendSimMission.SkySensor ? 200f : Mission == AscendSimMission.EarthWatcher ? d.requiredDeltaV + 400f : d.requiredDeltaV + 3150f;

    public static string Difficulty => Mission == AscendSimMission.SkySensor ? "Easy" : Mission == AscendSimMission.EarthWatcher ? "Medium" : "Hard";

    public struct Item { public bool ok; public string kid, expert; public Item(bool o, string k, string e) { ok = o; kid = k; expert = e; } }

    // ───────────── Live checklist ─────────────
    public static List<Item> Checklist(CADBuilder builder, CADDesign rocket, CADDesign payload, AscendCADData data)
    {
        var l = new List<Item>();
        var m = Mission; bool sky = m == AscendSimMission.SkySensor;
        float req = RequiredDeltaV(data);
        var ps = payload != null && !payload.IsEmpty ? CADStats.Payload(payload, data) : null;

        if (builder == CADBuilder.Rocket)
        {
            var rs = CADStats.Rocket(rocket, data, ps != null ? ps.wetMass : 0f, req);
            bool any = rocket != null && !rocket.IsEmpty;
            l.Add(new Item(any && rs.stages.Count > 0 && rs.stages[0].engines > 0 && rs.engineIssues.Count == 0,
                sky ? "A motor at the bottom" : "Engines that can fire at liftoff", rs.engineIssues.Count > 0 ? rs.engineIssues[0] : "Stage 1 needs sea-level engines or a motor"));
            l.Add(new Item(any && rs.liftoffTWR >= data.minLiftoffTWR, $"Strong enough to lift off", $"Thrust-to-weight {rs.liftoffTWR:0.00} (need {data.minLiftoffTWR:0.0} or more)"));
            l.Add(new Item(any && rs.deltaV >= req, $"Enough push for {Title}", $"Delta-v {rs.deltaV:N0} of {req:N0} m/s"));
            l.Add(new Item(rs.Has(CADRole.FlightComputer), "Flight computer: the rocket's brain", sky ? "Hobby altimeter computer" : "Install it inside an avionics bay"));
            if (sky) l.Add(new Item(rs.Has(CADRole.Aero) || rs.Has(CADRole.IMU), "Fins to fly straight", "3-4 fins near the bottom (or an IMU)"));
            else l.Add(new Item(rs.Has(CADRole.IMU), "IMU: senses which way is up", "Inertial measurement unit"));
            l.Add(new Item(rs.Has(CADRole.Battery), "Battery for the electronics", ""));
            if (!sky)
            {
                l.Add(new Item(rs.Has(CADRole.FlightTermination), "Safety system (flight termination)", "Range safety requires it"));
                l.Add(new Item(rs.Has(CADRole.Telemetry), "Telemetry radio", "So Mission Control can follow the flight"));
            }
            l.Add(new Item(any && TopIsAero(rocket, data), "Pointy top: nose cone or fairing", "Smooth airflow, lower drag and air loads"));
            if (any)
            {
                CADLaunchPad fit = null; bool owned = false;
                foreach (var pad in data.launchPads)
                {
                    if (pad == null || pad.isTransporter || AscendPads.Problems(pad, rs, data).Count > 0) continue;
                    bool own = AscendEconomy.OwnsPad(pad, data) && AscendPads.Lock(pad, data) == null;
                    if (fit == null || (own && !owned)) { fit = pad; owned = own; }
                }
                var sel = data.GetPad(AscendEconomy.SelectedPad);
                bool selOk = sel != null && !sel.isTransporter && AscendEconomy.OwnsPad(sel, data) && AscendPads.Problems(sel, rs, data).Count == 0;
                l.Add(new Item(selOk, "A launch pad that fits (Launch Pad tab)",
                    selOk ? sel.name : fit != null ? (owned ? $"Choose the {fit.name}" : $"Buy the {fit.name} ({AscendReport.Money(fit.costUSD)})") : "No pad can hold this rocket"));
            }
            if (ps == null) l.Add(new Item(false, sky ? "CanSat built (Payload Builder)" : "Satellite built (Payload Builder)", "The guide sizes the rocket for it"));
            else
            {
                CADPart fairing = null;
                if (rocket != null) foreach (var pl in rocket.parts) { var p = data.Get(pl.partId); if (p != null && p.innerDiameter > 0f) { fairing = p; break; } }
                l.Add(new Item(fairing != null && ps.stowedWidth <= fairing.innerDiameter && ps.stowedHeight <= fairing.innerHeight,
                    sky ? "CanSat fits in the payload bay" : "Satellite fits inside the fairing",
                    fairing != null ? $"Folded {ps.stowedWidth:0.##} x {ps.stowedHeight:0.##} m, space Ø{fairing.innerDiameter:0.##} x {fairing.innerHeight:0.##} m" : "No fairing / payload bay yet"));
                l.Add(new Item(any && rs.maxPayloadLEO >= ps.wetMass, "Can carry the payload", $"Payload {AscendReport.Mass(ps.wetMass)}, rocket can carry {AscendReport.Mass(rs.maxPayloadLEO)} for this mission"));
            }
            return l;
        }

        // Payload
        if (ps == null) { l.Add(new Item(false, sky ? "Start with a CanSat shell" : "Start with a frame or bus", "")); return l; }
        if (sky)
        {
            l.Add(new Item(ps.Has(CADRole.SatStructure), "CanSat shell", "Soda-can sized body"));
            l.Add(new Item(ps.Has(CADRole.Separation), "Ejection plate", "Pushes the CanSat out at the top"));
            l.Add(new Item(ps.Has(CADRole.Recovery), "Parachute", "Slow, soft landing"));
            l.Add(new Item(ps.Has(CADRole.OBC), "Computer board", "Reads the sensors"));
            l.Add(new Item(ps.instruments > 0, "Air sensor (temperature + pressure)", "The science of the mission"));
            l.Add(new Item(ps.Has(CADRole.Transceiver) && ps.Has(CADRole.Antenna), "Radio + antenna", "Sends the data down"));
            l.Add(new Item(ps.batteryWh > 0f && ps.batteryWh >= ps.powerUse * 0.5f, "Battery lasts the flight", $"{ps.batteryWh:0.#} Wh for {ps.powerUse:0.##} W"));
            l.Add(new Item(ps.volumeUsed <= ps.volumeCap + 1e-3f, "Everything fits inside", $"{ps.volumeUsed:0.###} of {ps.volumeCap:0.###} L"));
            return l;
        }
        l.Add(new Item(ps.Has(CADRole.SatStructure), "Structure (frame or bus)", ""));
        l.Add(new Item(ps.Has(CADRole.Separation), "Separation system", "To leave the rocket"));
        l.Add(new Item(ps.Has(CADRole.SolarPanel) && ps.powerGen >= ps.powerUse * data.powerMargin, "Solar panels make enough power", $"{ps.powerGen:0.#} W made, {ps.powerUse:0.#} W used (+{(data.powerMargin - 1f) * 100f:0}%)"));
        l.Add(new Item(ps.Has(CADRole.EPS), "Power system (EPS)", ""));
        l.Add(new Item(ps.batteryWh >= ps.batteryNeedWh && ps.batteryWh > 0f, "Battery for the dark side of Earth", $"{ps.batteryWh:0} of {ps.batteryNeedWh:0} Wh"));
        l.Add(new Item(ps.Has(CADRole.OBC), "On-board computer", ""));
        l.Add(new Item(ps.Has(CADRole.Transceiver) && ps.Has(CADRole.Antenna), "Radio + antenna", ""));
        l.Add(new Item(ps.Has(CADRole.ADCSIntegrated) || (ps.Has(CADRole.ADCSActuator) && ps.Has(CADRole.ADCSSensor)), "Pointing control (ADCS)", "To aim the camera"));
        l.Add(new Item(ps.instruments > 0, m == AscendSimMission.MoonMapper ? "Mapping camera / instrument" : "Earth camera / instrument", ""));
        l.Add(new Item(ps.Has(CADRole.Thermal), "Thermal control", "Keeps electronics at a safe temperature"));
        l.Add(new Item(ps.volumeUsed <= ps.volumeCap + 1e-3f, "Everything fits inside", $"{ps.volumeUsed:0.##} of {ps.volumeCap:0.##} L"));
        if (m == AscendSimMission.MoonMapper)
            l.Add(new Item(ps.satDeltaV >= MoonBrakeDv, "Own engine to brake at the Moon", $"{ps.satDeltaV:N0} of {MoonBrakeDv:N0} m/s (add hydrazine modules)"));
        return l;
    }

    public static bool AllOk(List<Item> l) { foreach (var i in l) if (!i.ok) return false; return true; }

    // ───────────── Step-by-step build guide ─────────────
    public class Step { public string text, key, parentId; public CADPart part; public CADLink link; public int needTotal; }

    public static List<Step> Steps(CADDesign d, AscendCADData data)
    {
        var steps = new List<Step>();
        if (d == null) return steps;
        int n = d.parts.Count;
        var done = new bool[n];
        var need = new Dictionary<string, int>();
        bool payload = d.builder == CADBuilder.Payload;
        int cur = 0;
        void Add(CADPart p, int count, string text)
        {
            var pl0 = d.parts[cur];
            string pid = pl0.parent >= 0 ? d.parts[pl0.parent].partId : "";
            string key = StepKey(p.id, pl0.link, pid);
            need.TryGetValue(key, out var c); c += count; need[key] = c;
            steps.Add(new Step { part = p, needTotal = c, text = text, key = key, link = pl0.link, parentId = pid });
        }
        string Name(int i) { var q = i >= 0 ? data.Get(d.parts[i].partId) : null; return q != null ? q.name : "part"; }

        for (int i = 0; i < n; i++)
        {
            if (done[i]) continue;
            cur = i;
            var pl = d.parts[i]; var p = data.Get(pl.partId);
            if (p == null) { done[i] = true; continue; }
            var grp = new List<int> { i };
            if (pl.symGroup >= 0) for (int j = i + 1; j < n; j++) if (d.parts[j].symGroup == pl.symGroup) grp.Add(j);
            foreach (int j in grp) done[j] = true;
            int cnt = grp.Count;
            string sym = cnt > 1 ? $" Set <b>Symmetry x{cnt}</b> first." : "";
            string parent = Name(pl.parent);

            switch (pl.link)
            {
                case CADLink.Root:
                    Add(p, 1, payload ? $"Place the <b>{p.name}</b> first. It is what holds the satellite to the rocket until release."
                                      : $"Place the <b>{p.name}</b> first. It is the bottom of the rocket.");
                    break;
                case CADLink.Top:
                    Add(p, 1, $"Stack the <b>{p.name}</b> on top of the {parent} (click its top half).");
                    break;
                case CADLink.Bottom:
                    Add(p, 1, $"Attach the <b>{p.name}</b> under the {parent} (click its bottom half).");
                    break;
                case CADLink.Cluster:
                {
                    // Upper-stage engine: it sits under a tank that stands on an interstage
                    var tankPl = pl.parent >= 0 ? d.parts[pl.parent] : null;
                    var under = tankPl != null && tankPl.parent >= 0 ? data.Get(d.parts[tankPl.parent].partId) : null;
                    bool upper = under != null && under.role == CADRole.Decoupler;
                    string where = upper ? $"Click the <b>bottom half of the {parent}</b> (the upper tank, NOT the interstage): the engine hides inside the interstage."
                                         : $"Click the <b>bottom half of the {parent}</b>.";
                    Add(p, cnt, (cnt == 1 ? $"Put one <b>{p.name}</b> in the center under the {parent}. Set <b>Symmetry x1</b>. "
                                          : $"Add a ring of {cnt} <b>{p.name}</b> under the {parent}.{sym} ") + where);
                    break;
                }
                case CADLink.RadialUpright:
                    Add(p, cnt, $"Attach {cnt} <b>{p.name}</b> upright on the side of the {parent}.{sym}");
                    break;
                case CADLink.Radial:
                {
                    var pp = pl.parent >= 0 ? data.Get(d.parts[pl.parent].partId) : null;
                    float dy = pl.parent >= 0 ? pl.pos.y - d.parts[pl.parent].pos.y : 0f;
                    string where = pp != null && Mathf.Abs(dy) >= pp.StackHeight * 0.45f ? (dy > 0 ? "on the top face of" : "on the bottom face of") : "on the side of";
                    Add(p, cnt, $"Attach {(cnt > 1 ? cnt + " " : "the ")}<b>{p.name}</b> {where} the {parent}.{sym}");
                    break;
                }
                case CADLink.Internal:
                {
                    // Group every copy of this part installed in the same host
                    int same = 1;
                    for (int j = i + 1; j < n; j++)
                        if (!done[j] && d.parts[j].link == CADLink.Internal && d.parts[j].parent == pl.parent && d.parts[j].partId == pl.partId) { done[j] = true; same++; }
                    Add(p, same, $"Install {(same > 1 ? same + " x " : "the ")}<b>{p.name}</b> inside the {parent} (pick it, then click the {parent}).");
                    break;
                }
            }
        }
        return steps;
    }

    public static string StepKey(string id, CADLink link, string parentId) => id + "|" + link + "|" + parentId;

    // How many of this part the design has in the right place (same attachment type, on the same kind of part)
    public static int CountOf(CADDesign d, Step st)
    {
        int c = 0;
        if (d == null) return 0;
        foreach (var pl in d.parts)
        {
            if (pl.partId != st.part.id) continue;
            string pid = pl.parent >= 0 && pl.parent < d.parts.Count ? d.parts[pl.parent].partId : "";
            bool sameLink = pl.link == st.link || (IsSide(pl.link) && IsSide(st.link)) || (IsStack(pl.link) && IsStack(st.link));
            if (sameLink && (pid == st.parentId || st.link == CADLink.Root)) c++;
        }
        return c;
    }
    static bool IsSide(CADLink l) => l == CADLink.Radial || l == CADLink.RadialUpright;
    static bool IsStack(CADLink l) => l == CADLink.Top || l == CADLink.Bottom || l == CADLink.Root;

    public static string ChecklistText(List<Item> l, bool expert = true)
    {
        var sb = new StringBuilder();
        foreach (var i in l)
        {
            sb.Append(i.ok ? "<color=#3FB950>• DONE</color>  " : "<color=#F85149>• TODO</color>  ").Append(i.kid);
            if (expert && !string.IsNullOrEmpty(i.expert)) sb.Append("  <size=85%><color=#9AA0A6>").Append(i.expert).Append("</color></size>");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static bool TopIsAero(CADDesign d, AscendCADData data)
    {
        if (d == null) return false;
        int top = -1; float topY = float.MinValue;
        for (int i = 0; i < d.parts.Count; i++)
        {
            var p = data.Get(d.parts[i].partId);
            if (p == null || !CADStats.IsCore(d, i)) continue;
            float y = d.parts[i].pos.y + p.StackHeight * 0.5f;
            if (y > topY) { topY = y; top = i; }
        }
        var tp = top >= 0 ? data.Get(d.parts[top].partId) : null;
        return tp != null && (tp.role == CADRole.NoseCone || tp.role == CADRole.Fairing || tp.shape == CADShape.OgiveTank);
    }

    // ───────────── Suggestions ─────────────
    public class Option
    {
        public string tier; public CADDesign design; public double cost; public bool works, affordable;
        public float margin; public string summary = "", why = "";
    }

    public static List<Option> Suggest(CADBuilder builder, CADDesign rocket, CADDesign payload, AscendCADData data, out string intro)
    {
        var m = Mission;
        var opts = new List<Option>();
        double fund = AscendEconomy.Balance;

        if (builder == CADBuilder.Payload)
        {
            string[] tiers = { "Budget", "Balanced", "Best" };
            int count = m == AscendSimMission.SkySensor ? 2 : 3;
            for (int t = 0; t < count; t++)
            {
                CADDesign d;
                try { d = AscendTemplates.AutoSat(m, t, data); } catch (Exception) { continue; }
                var st = CADStats.Payload(d, data);
                var o = new Option { tier = count == 2 && t == 1 ? "Best" : tiers[t], design = d, cost = AscendEconomy.Quote(data, d).total };
                o.works = AllOk(Checklist(CADBuilder.Payload, null, d, data));
                o.affordable = o.cost <= fund + 0.01;
                o.summary = $"{AscendReport.Mass(st.wetMass)}, {st.instruments} instrument(s), {st.powerGen:0.#} W power" +
                            (m == AscendSimMission.MoonMapper ? $", own engine {st.satDeltaV:N0} m/s" : "");
                opts.Add(o);
            }
            intro = $"Satellites made for <b>{Title}</b>. Your fund: <b>{AscendReport.Money(fund)}</b>.";
            return opts;
        }

        // Rocket: size it for the current payload (or the balanced suggested payload)
        var pay = payload != null && !payload.IsEmpty ? payload : null;
        bool sizedForBest = false;
        if (pay == null) { try { pay = AscendTemplates.AutoSat(m, m == AscendSimMission.SkySensor ? 1 : 2, data); sizedForBest = true; } catch (Exception) { } }
        var ps = pay != null ? CADStats.Payload(pay, data) : null;
        double payCost = pay != null ? AscendEconomy.Quote(data, pay).total : 0;
        double avail = fund - (payload != null && !payload.IsEmpty ? payCost : 0);
        float req = RequiredDeltaV(data);

        var all = new List<Option>();
        foreach (var d in AscendTemplates.AutoRockets(m, data, ps != null ? ps.stowedWidth : 0.5f, ps != null ? ps.stowedHeight : 0.5f))
        {
            var rs = CADStats.Rocket(d, data, ps != null ? ps.wetMass : 0f, req);
            var o = new Option { design = d, cost = AscendEconomy.Quote(data, d).total };
            // Margin so small building differences can't turn a GO into a NO-GO
            o.works = AllOk(Checklist(CADBuilder.Rocket, d, pay, data)) && rs.deltaV >= req * 1.05f && (ps == null || rs.maxPayloadLEO >= ps.wetMass * 1.25f);
            o.affordable = o.cost <= avail + 0.01;
            o.margin = req > 0 ? rs.deltaV / req : 0f;
            o.summary = $"{rs.stages.Count} stage(s), {AscendReport.Mass(rs.wetMass)}, delta-v {rs.deltaV:N0}/{req:N0} m/s, TWR {rs.liftoffTWR:0.00}, carries {AscendReport.Mass(rs.maxPayloadLEO)}";
            all.Add(o);
        }
        var good = all.Where(o => o.works).OrderBy(o => o.cost).ToList();
        var cheapOk = good.Where(o => o.affordable).ToList();
        if (cheapOk.Count > 0)
        {
            var budget = cheapOk[0];
            budget.tier = "Budget"; opts.Add(budget);
            var balanced = cheapOk.FirstOrDefault(o => o != budget && o.margin >= 1.12f) ?? cheapOk.Skip(cheapOk.Count / 2).FirstOrDefault(o => o != budget);
            if (balanced != null) { balanced.tier = "Balanced"; opts.Add(balanced); }
            var best = cheapOk.Where(o => !opts.Contains(o)).OrderByDescending(o => o.margin).FirstOrDefault();
            if (best != null) { best.tier = "Best"; opts.Add(best); }
        }
        else if (good.Count > 0)
        {
            var o = good[0]; o.tier = "Cheapest that works"; o.why = "Costs more than your fund. Ask for more money (or use Sandbox Mode).";
            opts.Add(o);
        }
        else
        {
            var o = all.OrderByDescending(x => x.margin).FirstOrDefault();
            if (o != null) { o.tier = "Closest try"; o.why = "None of the guide's rockets can do this mission with this payload. Make the payload lighter."; opts.Add(o); }
        }
        intro = $"Rockets made for <b>{Title}</b>" + (ps != null ? $" carrying <b>{pay.name}</b> ({AscendReport.Mass(ps.wetMass)})" : "") +
                (sizedForBest ? " <color=#D29922>(you haven't built a payload yet, so it's sized for the heaviest suggested one; build the payload first for a better fit)</color>" : "") +
                $". Money left for the rocket: <b>{AscendReport.Money(Math.Max(0, avail))}</b>.";
        return opts;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  SMALL COMPONENTS + UI HELPERS
// ═════════════════════════════════════════════════════════════════════════════
public class AscendHook : MonoBehaviour
{
    public Action onUpdate, onLateUpdate, onEnable, onDisable, onDestroy;
    void Update() => onUpdate?.Invoke();
    void LateUpdate() => onLateUpdate?.Invoke();
    void OnEnable() => onEnable?.Invoke();
    void OnDisable() => onDisable?.Invoke();
    void OnDestroy() => onDestroy?.Invoke();
}

public class AscendPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IScrollHandler, IPointerMoveHandler, IPointerExitHandler
{
    public Action<PointerEventData> down, up, beginDrag, drag, endDrag, scroll, move, exit;
    public void OnPointerDown(PointerEventData e) => down?.Invoke(e);
    public void OnPointerUp(PointerEventData e) => up?.Invoke(e);
    public void OnBeginDrag(PointerEventData e) => beginDrag?.Invoke(e);
    public void OnDrag(PointerEventData e) => drag?.Invoke(e);
    public void OnEndDrag(PointerEventData e) => endDrag?.Invoke(e);
    public void OnScroll(PointerEventData e) => scroll?.Invoke(e);
    public void OnPointerMove(PointerEventData e) => move?.Invoke(e);
    public void OnPointerExit(PointerEventData e) => exit?.Invoke(e);
}

public static class AUI
{
    public static TMP_FontAsset Font;
    // Global size multiplier for AscendCAD / PhMail text and buttons
    public static float FontScale = 1f;
    public static float S(float v) => v * FontScale;
    public static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

    public static RectTransform Rect(string n, Transform p)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(p, false);
        return (RectTransform)go.transform;
    }

    public static RectTransform Fill(RectTransform r, float l = 0, float b = 0, float rt = 0, float t = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rt, -t);
        return r;
    }

    public static Image Img(Component c, Color col, bool round = false, bool ray = false)
    {
        var i = c.gameObject.AddComponent<Image>();
        i.color = col; i.raycastTarget = ray;
        if (round) { i.sprite = Rounded; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 1.4f; }
        return i;
    }

    public static TextMeshProUGUI Text(Transform p, string s, float size, Color c, FontStyles st = FontStyles.Normal)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(p, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (Font) t.font = Font;
        t.text = s; t.fontSize = size * FontScale; t.color = c; t.fontStyle = st; t.raycastTarget = false; t.richText = true;
        return t;
    }

    public static LayoutElement LE(Component c, float w = -1, float h = -1, float fw = -1, float fh = -1)
    {
        var le = c.GetComponent<LayoutElement>();
        if (!le) le = c.gameObject.AddComponent<LayoutElement>();
        if (w >= 0) { le.preferredWidth = w; le.minWidth = w; }
        if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
        if (fw >= 0) le.flexibleWidth = fw;
        if (fh >= 0) le.flexibleHeight = fh;
        return le;
    }

    public static RectTransform V(RectTransform r, int l, int rt, int t, int b, float sp)
    {
        var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(l, rt, t, b); v.spacing = sp; v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return r;
    }

    public static RectTransform H(RectTransform r, int l, int rt, int t, int b, float sp, TextAnchor a = TextAnchor.MiddleLeft)
    {
        var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(l, rt, t, b); h.spacing = sp; h.childAlignment = a;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        return r;
    }

    public static Button Btn(Transform p, string label, float size, Color txt, Color bg, Action onClick, float w = -1, float h = 32)
    {
        var r = Rect(label, p);
        var img = Img(r, bg, true, true);
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = ColorBlock.defaultColorBlock;
        cb.highlightedColor = new Color(0.85f, 0.9f, 1f); cb.pressedColor = new Color(0.7f, 0.7f, 0.75f);
        cb.selectedColor = Color.white; cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
        b.colors = cb;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        if (onClick != null) b.onClick.AddListener(() => onClick());
        var t = Text(r, label, size, txt, FontStyles.Bold);
        Fill(t.rectTransform, 6, 0, 6, 0);
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Truncate;
        LE(r, w > 0 ? w * FontScale : w, h > 0 ? h * FontScale : h);
        return b;
    }

    // Returns the content RectTransform of a vertical scroll view filling parent (with insets)
    public static RectTransform Scroll(Transform parent, float l, float b, float rt, float t)
    {
        var root = Rect("Scroll", parent); Fill(root, l, b, rt, t);
        var sr = root.gameObject.AddComponent<ScrollRect>();
        Img(root, new Color(0, 0, 0, 0), false, true);
        sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 35f;
        var vp = Rect("Viewport", root); Fill(vp); vp.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", vp);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1f); content.sizeDelta = Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.viewport = vp; sr.content = content;
        return content;
    }

    public static TMP_InputField Field(Transform p, string placeholder, float size, Color txt, Color bg)
    {
        var r = Rect("Input", p);
        var hit = Img(r, bg, true, true);
        var area = Rect("Text Area", r); Fill(area, 10, 2, 10, 2); area.gameObject.AddComponent<RectMask2D>();
        var ph = Text(area, placeholder, size, new Color(txt.r, txt.g, txt.b, 0.4f));
        var tx = Text(area, "", size, txt);
        foreach (var t in new[] { ph, tx }) { Fill(t.rectTransform); t.alignment = TextAlignmentOptions.MidlineLeft; t.textWrappingMode = TextWrappingModes.NoWrap; }
        tx.richText = false;
        var f = r.gameObject.AddComponent<TMP_InputField>();
        f.textViewport = area; f.textComponent = tx; f.placeholder = ph; f.targetGraphic = hit;
        if (Font) f.fontAsset = Font;
        f.pointSize = size * FontScale; f.lineType = TMP_InputField.LineType.SingleLine;
        f.customCaretColor = true; f.caretColor = txt; f.caretWidth = 2;
        f.navigation = new Navigation { mode = Navigation.Mode.None };
        return f;
    }

    static Sprite rounded;
    public static Sprite Rounded
    {
        get
        {
            if (rounded) return rounded;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    float u = (x + (sx + 0.5f) / 4f) / size * 2f - 1f, v = (y + (sy + 0.5f) / 4f) / size * 2f - 1f;
                    float qx = Mathf.Max(Mathf.Abs(u) - 0.5f, 0f), qy = Mathf.Max(Mathf.Abs(v) - 0.5f, 0f);
                    if (qx * qx + qy * qy <= 0.25f) hits++;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / 16));
            }
            tex.SetPixels32(px); tex.Apply();
            return rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
        }
    }

    public static bool Typing()
    {
        var es = EventSystem.current;
        if (!es || !es.currentSelectedGameObject) return false;
        var f = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
        return f && f.isFocused;
    }

    public static bool KeyDown(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current; if (kb == null) return false;
        switch (k)
        {
            case KeyCode.Delete: return kb.deleteKey.wasPressedThisFrame;
            case KeyCode.F: return kb.fKey.wasPressedThisFrame;
            default: return false;
        }
#else
        return UnityEngine.Input.GetKeyDown(k);
#endif
    }

    public static bool ShiftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current; return kb != null && kb.shiftKey.isPressed;
#else
        return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
#endif
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  ASCENDCAD APP SCREEN (built inside the ComputerInteraction window)
//  Performance: the 3D view only renders when something changes, the part list
//  is built only for opened categories, and stats are recomputed only on edits.
// ═════════════════════════════════════════════════════════════════════════════
public class AscendCADScreen
{
    static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f), PreviewOffset = new Vector3(3000f, 0f, 0f);
    static readonly Color Bg = AUI.Hex("#18191C"), Panel = AUI.Hex("#212226"), Panel2 = AUI.Hex("#2E2F35"), Txt = AUI.Hex("#E8EAED"),
        Sub = AUI.Hex("#9AA0A6"), Accent = AUI.Hex("#F07A26"), Blue = AUI.Hex("#8AB4F8"), Good = AUI.Hex("#3FB950"), Bad = AUI.Hex("#F85149"),
        RowBg = new Color(1f, 1f, 1f, 0.03f);
    static readonly Color[] Swatches =
    {
        AUI.Hex("#F0F0F2"), AUI.Hex("#B8BBC2"), AUI.Hex("#4A4C52"), AUI.Hex("#1C1C1F"), AUI.Hex("#0038A8"), AUI.Hex("#CE1126"),
        AUI.Hex("#FCD116"), AUI.Hex("#F07A26"), AUI.Hex("#2E8B57"), AUI.Hex("#7FB3E6"), AUI.Hex("#D9B25C"), AUI.Hex("#8E5BD9"),
    };

    readonly AscendCADData data;
    readonly RectTransform root;
    readonly TMP_FontAsset font;
    readonly int layer;

    CADBuilder mode = CADBuilder.Rocket;
    readonly CADDesign rocket = new CADDesign { name = "Bayanihan-1", builder = CADBuilder.Rocket };
    readonly CADDesign payload = new CADDesign { name = "Diwata-X", builder = CADBuilder.Payload };
    CADDesign D => mode == CADBuilder.Rocket ? rocket : payload;

    // 3D
    GameObject world, previewObj;
    Transform rocketRoot, payloadRoot, previewRoot, grid;
    Camera cam, pcam;
    Light key, fill;
    RenderTexture rt, prt;
    RawImage view, pview;
    readonly List<GameObject> rocketObjs = new List<GameObject>(), payloadObjs = new List<GameObject>(), ghosts = new List<GameObject>();
    List<GameObject> Objs => mode == CADBuilder.Rocket ? rocketObjs : payloadObjs;
    Transform Root => mode == CADBuilder.Rocket ? rocketRoot : payloadRoot;
    Vector3 target, pTarget;
    float yaw = 35f, pitch = 22f, dist = 25f, pyaw = 30f, ppitch = 15f, pdist = 5f;
    int dirty = 2, pdirty = 2;
    enum Drag { None, Orbit, Pan }
    Drag drag;
    CADPart ghostPart;
    string pkey;

    // State
    CADPart picked;
    int sel = -1, sym = 1;
    float selAngle, selH01 = 0.5f;
    bool labelVertical;
    Color labelColor = Color.black;
    double projected;

    // UI
    RectTransform sideContent, infoTools, insideList, modal;
    TMP_InputField search, nameField, labelField;
    TextMeshProUGUI fundLabel, infoTitle, infoText, statsTitle, statsText, status, vertBtnText, colBtnText;
    Button rocketTab, payloadTab;
    GameObject previewBox;
    RectTransform viewRT, menu, padPanel, padGrid;
    TextMeshProUGUI padHeader;
    Button padTab;
    bool padsOpen;
    Vector2 menuScreen;
    static float S(float v) => AUI.S(v);
    readonly List<(int n, Image img)> symButtons = new List<(int, Image)>();
    readonly HashSet<string> open = new HashSet<string>();

    class Plan
    {
        public bool ok;
        public string msg = "";
        public readonly List<CADPlaced> adds = new List<CADPlaced>();
        public Action<int> after;
    }

    public AscendCADScreen(AscendCADData asset, TMP_FontAsset font, RectTransform body)
    {
        data = AscendCADData.Resolve(asset);
        root = body; this.font = font; layer = data.cadLayer;
        AUI.Font = font;
        AUI.FontScale = data.uiFontScale;
        CADModels.LowSpec = data.lowSpecMode;
        AscendEconomy.Init(data);
        AscendMail.EnsureWelcome(data);

        BuildWorld();
        BuildUI();

        var hook = root.gameObject.AddComponent<AscendHook>();
        hook.onUpdate = Tick;
        hook.onLateUpdate = LateTick;
        hook.onEnable = () => { dirty = pdirty = 2; };
        hook.onDisable = () => { if (cam) cam.enabled = false; if (pcam) pcam.enabled = false; foreach (var c in padCams) if (c) c.enabled = false; };
        hook.onDestroy = Dispose;
        AscendEconomy.Changed += OnEconomy;
        AscendMissionControl.ReviewCompleted += OnReview;
        AscendMissionGuide.MissionChanged += Changed;

        open.Add(CADBuilder.Rocket + "/Propulsion");
        open.Add(CADBuilder.Payload + "/Satellite Structure");
        SetMode(CADBuilder.Rocket);
        Status("Pick a part on the left, then click in the 3D view to place it. Right-drag or left-drag to orbit.", Sub);
    }

    // ───────────────────────────── 3D workspace ─────────────────────────────
    void BuildWorld()
    {
        world = new GameObject("AscendCAD Workspace");
        world.transform.position = Origin;
        rocketRoot = Child("Rocket"); payloadRoot = Child("Payload");
        previewRoot = Child("Preview"); previewRoot.localPosition = PreviewOffset;

        var g = new GameObject("Grid");
        g.transform.SetParent(world.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = CADModels.GridMesh(16);
        var gr = g.AddComponent<MeshRenderer>();
        gr.sharedMaterial = CADModels.GridMaterial();
        gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = false;
        grid = g.transform;

        cam = MakeCam("CAD Camera");
        pcam = MakeCam("CAD Preview Camera");
        prt = new RenderTexture(data.lowSpecMode ? 320 : 448, data.lowSpecMode ? 200 : 280, 16) { name = "AscendCAD Preview", antiAliasing = 1 };
        pcam.targetTexture = prt; pcam.aspect = prt.width / (float)prt.height;
        key = MakeLight("CAD Key Light", Quaternion.Euler(45f, -35f, 0f), 1.15f);
        fill = MakeLight("CAD Fill Light", Quaternion.Euler(-15f, 150f, 0f), 0.45f);
        CADModels.SetLayer(world, layer);

        var main = Camera.main;
        if (main) main.cullingMask &= ~(1 << layer);
        // CAD lights switch on only while a CAD camera renders, so they never light your game world
        RenderPipelineManager.beginCameraRendering += BeginCam;
        RenderPipelineManager.endCameraRendering += EndCam;
        Camera.onPreCull += PreCull;
        Camera.onPostRender += PostRender;
    }

    Transform Child(string n) { var t = new GameObject(n).transform; t.SetParent(world.transform, false); return t; }

    Camera MakeCam(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(world.transform, false);
        var c = go.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = AUI.Hex("#5E636B");   // neutral gray
        c.cullingMask = 1 << layer;
        c.fieldOfView = 35f;
        c.allowHDR = false;
        c.allowMSAA = !data.lowSpecMode;
        c.useOcclusionCulling = false;
        c.enabled = false;
        return c;
    }

    Light MakeLight(string n, Quaternion r, float intensity)
    {
        var go = new GameObject(n);
        go.transform.SetParent(world.transform, false);
        go.transform.rotation = r;
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = intensity; l.shadows = LightShadows.None;
        l.cullingMask = 1 << layer; l.enabled = false;
        return l;
    }

    void Lights(Camera c, bool on) { if (c && (c == cam || c == pcam || padCams.Contains(c)) && key) { key.enabled = on; fill.enabled = on; } }
    void BeginCam(ScriptableRenderContext _, Camera c) => Lights(c, true);
    void EndCam(ScriptableRenderContext _, Camera c) => Lights(c, false);
    void PreCull(Camera c) => Lights(c, true);
    void PostRender(Camera c) => Lights(c, false);

    void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCam;
        RenderPipelineManager.endCameraRendering -= EndCam;
        Camera.onPreCull -= PreCull;
        Camera.onPostRender -= PostRender;
        AscendEconomy.Changed -= OnEconomy;
        AscendMissionControl.ReviewCompleted -= OnReview;
        AscendMissionGuide.MissionChanged -= Changed;
        if (world) Object.Destroy(world);
        if (rt) { rt.Release(); Object.Destroy(rt); }
        if (prt) { prt.Release(); Object.Destroy(prt); }
        foreach (var v in padViews.Values) if (v.rt) { v.rt.Release(); Object.Destroy(v.rt); }
        padViews.Clear(); padCams.Clear();
    }

    // ───────────────────────────── Pictures of the launch pads ─────────────────────────────
    class PadView { public GameObject prefab, root; public Transform pivot; public Camera cam; public RenderTexture rt; public float dist; public bool builtIn; }
    readonly Dictionary<string, PadView> padViews = new Dictionary<string, PadView>();
    readonly HashSet<Camera> padCams = new HashSet<Camera>();
    static readonly Vector3 PadViewOffset = new Vector3(0f, 0f, 40000f);

    // One snapshot per pad (built-in real-size model, or your own prefab), rendered once and reused
    PadView GetPadView(CADLaunchPad p, int index)
    {
        bool builtIn = p.useBuiltInModel && AscendPadModels.Has(p);
        if (!builtIn && !p.modelPrefab) return null;
        var src = builtIn ? null : p.modelPrefab;
        if (padViews.TryGetValue(p.id, out var v) && v.rt && v.prefab == src && v.builtIn == builtIn) return v;
        if (v != null) { if (v.root) Object.Destroy(v.root); if (v.cam) { padCams.Remove(v.cam); Object.Destroy(v.cam.gameObject); } if (v.rt) { v.rt.Release(); Object.Destroy(v.rt); } }

        v = new PadView { prefab = src, builtIn = builtIn };
        v.root = new GameObject("Pad Preview - " + p.name);
        v.root.transform.SetParent(world.transform, false);
        v.root.transform.localPosition = PadViewOffset + Vector3.right * (index * 3000f);
        GameObject model;
        if (builtIn)
        {
            model = AscendPadModels.Build(p, data, 1.85f, 70f, false, false, out _);   // pictured sized for a Falcon 9
            model.transform.SetParent(v.root.transform, false);
        }
        else model = Object.Instantiate(p.modelPrefab, v.root.transform, false);
        model.transform.localPosition = Vector3.zero;
        // Just a picture: switch off its scripts, cameras, lights, audio and physics
        foreach (var b in model.GetComponentsInChildren<Behaviour>(true)) b.enabled = false;
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;

        // Turn around the model's own center, whatever its pivot
        var rends = model.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds(v.root.transform.position, Vector3.one);
        for (int i = 0; i < rends.Length; i++) { if (i == 0) bounds = rends[i].bounds; else bounds.Encapsulate(rends[i].bounds); }
        v.pivot = new GameObject("Pivot").transform;
        v.pivot.SetParent(v.root.transform, false);
        v.pivot.position = bounds.center;
        model.transform.SetParent(v.pivot, true);
        CADModels.SetLayer(v.root, layer);

        v.cam = MakeCam("Pad Preview Camera - " + p.name);
        v.cam.fieldOfView = 30f;
        v.dist = Mathf.Max(0.5f, bounds.extents.magnitude / Mathf.Sin(15f * Mathf.Deg2Rad) * 0.95f);
        var look = Quaternion.Euler(18f, 35f, 0f);
        v.cam.transform.SetPositionAndRotation(v.pivot.position - look * Vector3.forward * v.dist, look);
        v.cam.nearClipPlane = v.dist * 0.01f; v.cam.farClipPlane = v.dist * 4f;
        v.rt = new RenderTexture(data.lowSpecMode ? 256 : 384, data.lowSpecMode ? 192 : 288, 16) { name = "Pad Preview " + p.name, antiAliasing = 1 };
        v.cam.targetTexture = v.rt; v.cam.aspect = v.rt.width / (float)v.rt.height;

        // Render ONE frame, then delete the model copy and camera: no ongoing cost
        padCams.Add(v.cam);
        v.cam.Render();
        padCams.Remove(v.cam);
        if (key) { key.enabled = false; fill.enabled = false; }
        Object.Destroy(v.cam.gameObject); v.cam = null;
        Object.Destroy(v.root); v.root = null; v.pivot = null;
        padViews[p.id] = v;
        return v;
    }

    void LateTick()
    {
        if (!view) return;
        var r = view.rectTransform.rect;
        float scale = (view.canvas ? view.canvas.scaleFactor : 1f) * data.viewportResolution;
        int w = Mathf.Clamp(Mathf.RoundToInt(r.width * scale), 32, 2048), h = Mathf.Clamp(Mathf.RoundToInt(r.height * scale), 32, 2048);
        if (!rt || rt.width != w || rt.height != h)
        {
            if (rt) { cam.targetTexture = null; rt.Release(); Object.Destroy(rt); }
            rt = new RenderTexture(w, h, 24) { name = "AscendCAD View", antiAliasing = data.lowSpecMode ? 1 : 2 };
            cam.targetTexture = rt; view.texture = rt; cam.aspect = w / (float)h;
            dirty = 2;
        }
        if (dirty > 0) PlaceCamera();
        cam.enabled = dirty > 0; if (dirty > 0) dirty--;

        bool pv = pdirty > 0 && previewBox && previewBox.activeInHierarchy && previewObj;
        if (pv)
        {
            var rot = Quaternion.Euler(ppitch, pyaw, 0f);
            pcam.transform.SetPositionAndRotation(pTarget - rot * Vector3.forward * pdist, rot);
            pcam.nearClipPlane = Mathf.Max(0.005f, pdist * 0.02f); pcam.farClipPlane = pdist * 10f + 10f;
        }
        pcam.enabled = pv; if (pdirty > 0) pdirty--;

    }

    void PlaceCamera()
    {
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.SetPositionAndRotation(world.transform.position + target - rot * Vector3.forward * dist, rot);
        if (grid) grid.gameObject.SetActive(cam.transform.position.y > grid.position.y + dist * 0.06f);
        cam.nearClipPlane = Mathf.Clamp(dist * 0.01f, 0.005f, 2f);
        cam.farClipPlane = Mathf.Clamp(dist * 6f, 50f, 1500f);
    }

    void Tick()
    {
        if (modal || AUI.Typing()) return;
        if (AUI.KeyDown(KeyCode.Delete)) DeleteSelected();
        if (AUI.KeyDown(KeyCode.F)) Frame();
    }

    // ───────────────────────────── UI layout ─────────────────────────────
    void BuildUI()
    {
        AUI.Img(root, Bg);

        // Top bar
        var top = AUI.Rect("TopBar", root);
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one; top.pivot = new Vector2(0.5f, 1f);
        float TB = S(48f), SW = S(270f), RW = S(330f);
        top.sizeDelta = new Vector2(0f, TB); top.anchoredPosition = Vector2.zero;
        AUI.Img(top, Panel);
        AUI.H(top, 12, 10, 7, 7, 8);
        var logo = AUI.Text(top, "<color=#F07A26>Ascend</color>CAD", 20, Txt, FontStyles.Bold);
        logo.textWrappingMode = TextWrappingModes.NoWrap; AUI.LE(logo, S(118));
        rocketTab = AUI.Btn(top, "Rocket Builder", 14, Txt, Panel2, () => SetMode(CADBuilder.Rocket), 132, 34);
        payloadTab = AUI.Btn(top, "Payload Builder", 14, Txt, Panel2, () => SetMode(CADBuilder.Payload), 136, 34);
        padTab = AUI.Btn(top, "Launch Pad", 14, Txt, Panel2, OpenPads, 116, 34);
        nameField = AUI.Field(top, "Design name", 14, Txt, Panel2);
        AUI.LE(nameField, S(170), S(34));
        nameField.onEndEdit.AddListener(s => { if (!string.IsNullOrWhiteSpace(s)) D.name = s.Trim(); else nameField.SetTextWithoutNotify(D.name); });
        AUI.LE(AUI.Rect("Spacer", top), -1, -1, 1);
        fundLabel = AUI.Text(top, "", 14, Txt);
        fundLabel.textWrappingMode = TextWrappingModes.NoWrap; fundLabel.alignment = TextAlignmentOptions.MidlineRight;
        AUI.LE(fundLabel, -1, -1, 1);
        fundLabel.overflowMode = TextOverflowModes.Ellipsis;
        AUI.Btn(top, "Mission Guide", 13, Color.white, AUI.Hex("#0B57D0"), ShowGuide, 112, 34);
        AUI.Btn(top, "Prebuilt", 13, Txt, Panel2, ShowTemplates, 76, 34);
        AUI.Btn(top, "Save", 13, Txt, Panel2, SaveDesign, 58, 34);
        AUI.Btn(top, "Load", 13, Txt, Panel2, LoadDesign, 58, 34);
        AUI.Btn(top, "Clear", 13, Txt, Panel2, AskClear, 58, 34);
        AUI.Btn(top, "SUBMIT", 15, Color.white, Accent, StartSubmit, 100, 34);

        // Left: part library
        var side = AUI.Rect("Library", root);
        side.anchorMin = Vector2.zero; side.anchorMax = new Vector2(0f, 1f); side.pivot = new Vector2(0f, 0.5f);
        side.offsetMin = Vector2.zero; side.offsetMax = new Vector2(SW, -TB);
        AUI.Img(side, Panel);
        search = AUI.Field(side, "Search parts...", 14, Txt, Panel2);
        var srt = (RectTransform)search.transform;
        srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = Vector2.one; srt.pivot = new Vector2(0.5f, 1f);
        srt.sizeDelta = new Vector2(-16f, S(34f)); srt.anchoredPosition = new Vector2(0f, -8f);
        search.onValueChanged.AddListener(_ => BuildSidebar());
        sideContent = AUI.Scroll(side, 0, 0, 0, S(34f) + 16f);
        AUI.V(sideContent, 8, 8, 4, 12, 3);

        // Right: part info + stats
        var right = AUI.Rect("Inspector", root);
        right.anchorMin = new Vector2(1f, 0f); right.anchorMax = Vector2.one; right.pivot = new Vector2(1f, 0.5f);
        right.offsetMin = new Vector2(-RW, 0f); right.offsetMax = new Vector2(0f, -TB);
        AUI.Img(right, Panel);
        var rc = AUI.Scroll(right, 0, 0, 0, 0);
        AUI.V(rc, 14, 14, 12, 18, 8);
        infoTitle = AUI.Text(rc, "", 18, Txt, FontStyles.Bold);

        var pb = AUI.Rect("Preview", rc);
        AUI.LE(pb, -1, 190);
        AUI.Img(pb, AUI.Hex("#15161A"), true);
        pview = AUI.Rect("View", pb).gameObject.AddComponent<RawImage>();
        AUI.Fill(pview.rectTransform, 3, 3, 3, 3);
        pview.texture = prt; pview.raycastTarget = true;
        var pp = pview.gameObject.AddComponent<AscendPointer>();
        pp.drag = e => { pyaw += e.delta.x * 0.4f; ppitch = Mathf.Clamp(ppitch - e.delta.y * 0.4f, -85f, 85f); pdirty = 2; };
        pp.scroll = e => { if (Mathf.Abs(e.scrollDelta.y) > 0.01f) { pdist *= e.scrollDelta.y > 0 ? 0.9f : 1.1f; pdirty = 2; } };
        var ph = AUI.Text(pb, "drag to rotate 360°  •  scroll to zoom", 11, Sub);
        ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = new Vector2(1f, 0f); ph.rectTransform.pivot = new Vector2(0.5f, 0f);
        ph.rectTransform.sizeDelta = new Vector2(0f, 18f); ph.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        ph.alignment = TextAlignmentOptions.Center;
        previewBox = pb.gameObject;

        infoText = AUI.Text(rc, "", 13.5f, Txt);
        infoTools = AUI.V(AUI.Rect("Tools", rc), 0, 0, 6, 6, 7);
        BuildTools(infoTools);
        statsTitle = AUI.Text(rc, "", 17, Txt, FontStyles.Bold);
        statsText = AUI.Text(rc, "", 13.5f, Txt);
        statsText.lineSpacing = 3f;
        guideBox = AUI.V(AUI.Rect("Build Guide", rc), 0, 0, 12, 6, 6);
        guideBox.gameObject.SetActive(false);

        // Center: 3D viewport
        var center = AUI.Rect("Viewport", root);
        AUI.Fill(center, SW, 0, RW, TB);
        viewRT = center;
        view = center.gameObject.AddComponent<RawImage>();
        view.raycastTarget = true;
        var vp = center.gameObject.AddComponent<AscendPointer>();
        vp.down = _ => CloseMenu();
        vp.beginDrag = e => drag = (e.button == PointerEventData.InputButton.Middle || AUI.ShiftHeld()) ? Drag.Pan : Drag.Orbit;
        vp.drag = OnViewDrag;
        vp.endDrag = _ => drag = Drag.None;
        vp.up = OnViewUp;
        vp.scroll = e =>
        {
            if (Mathf.Abs(e.scrollDelta.y) < 0.01f) return;
            dist = Mathf.Clamp(dist * (e.scrollDelta.y > 0 ? 0.88f : 1.14f), 0.05f, 2500f);
            dirty = 2;
        };
        vp.move = e => Hover(e.position);
        vp.exit = _ => HideGhosts();

        status = AUI.Text(center, "", 14, Sub);
        var st = status.rectTransform;
        st.anchorMin = new Vector2(0f, 1f); st.anchorMax = Vector2.one; st.pivot = new Vector2(0.5f, 1f);
        st.sizeDelta = new Vector2(-24f, S(44f)); st.anchoredPosition = new Vector2(0f, -8f);

        var bar = AUI.Rect("Toolbar", center);
        bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(1f, 0f); bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, S(40f)); bar.anchoredPosition = Vector2.zero;
        AUI.Img(bar, new Color(0f, 0f, 0f, 0.5f));
        AUI.H(bar, 10, 10, 4, 4, 5);
        var sl = AUI.Text(bar, "Symmetry", 13, Sub); AUI.LE(sl, S(70));
        foreach (int n in new[] { 1, 2, 3, 4, 6, 8 })
        {
            int k = n;
            var b = AUI.Btn(bar, "x" + n, 13, Txt, Panel2, () => SetSym(k), 36, 30);
            symButtons.Add((n, (Image)b.targetGraphic));
        }
        AUI.LE(AUI.Rect("Spacer", bar), -1, -1, 1);
        var help = AUI.Text(bar, "Click part: move / delete   RMB cancel   Drag orbit   Shift/MMB pan   Wheel zoom", 11.5f, Sub);
        help.textWrappingMode = TextWrappingModes.NoWrap; help.overflowMode = TextOverflowModes.Truncate;
        AUI.LE(help, -1, -1, 1);
        AUI.Btn(bar, "Frame (F)", 13, Txt, Panel2, Frame, 84, 30);
        AUI.Btn(bar, "Delete", 13, Color.white, AUI.Hex("#8B2C2C"), DeleteSelected, 66, 30);

        // Launch pad screen
        padPanel = AUI.Rect("LaunchPads", root);
        AUI.Fill(padPanel, 0, 0, 0, TB);
        AUI.Img(padPanel, Bg, false, true);
        var pc = AUI.Scroll(padPanel, 0, 0, 0, 0);
        AUI.V(pc, 24, 24, 18, 24, 14);
        AUI.Text(pc, "Launch Pad", 22, Txt, FontStyles.Bold);
        padHeader = AUI.Text(pc, "", 14, Sub);
        padGrid = AUI.Rect("Cards", pc);
        var gl = padGrid.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(S(340f), S(580f)); gl.spacing = new Vector2(18f, 18f);
        gl.startAxis = GridLayoutGroup.Axis.Horizontal; gl.childAlignment = TextAnchor.UpperLeft;
        padPanel.gameObject.SetActive(false);
    }

    void BuildTools(RectTransform t)
    {
        AUI.Text(t, "Paint", 14, Accent, FontStyles.Bold);
        var sw = AUI.Rect("Swatches", t);
        var gl = sw.gameObject.AddComponent<GridLayoutGroup>();
        gl.cellSize = new Vector2(38f, 24f); gl.spacing = new Vector2(6f, 6f);
        gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 6;
        foreach (var c in Swatches)
        {
            var col = c;
            var r = AUI.Rect("Swatch", sw);
            var img = AUI.Img(r, col, true, true);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img; b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => ApplyColor(col));
        }
        AUI.Text(t, "Label (click the spot on the part first)", 14, Accent, FontStyles.Bold);
        var row = AUI.H(AUI.Rect("LabelRow", t), 0, 0, 0, 0, 6);
        labelField = AUI.Field(row, "e.g. PILIPINAS", 13, Txt, Panel2);
        AUI.LE(labelField, -1, 30, 1);
        AUI.Btn(row, "Add", 13, Color.white, Accent, AddLabel, 54, 30);
        var row2 = AUI.H(AUI.Rect("LabelOpts", t), 0, 0, 0, 0, 6);
        vertBtnText = AUI.Btn(row2, "Horizontal", 12, Txt, Panel2, () => { labelVertical = !labelVertical; vertBtnText.text = labelVertical ? "Vertical" : "Horizontal"; }, 92, 28)
            .GetComponentInChildren<TextMeshProUGUI>();
        colBtnText = AUI.Btn(row2, "Black text", 12, Txt, Panel2, () =>
        {
            labelColor = labelColor == Color.black ? Color.white : Color.black;
            colBtnText.text = labelColor == Color.black ? "Black text" : "White text";
        }, 92, 28).GetComponentInChildren<TextMeshProUGUI>();
        AUI.Btn(row2, "Clear labels", 12, Txt, Panel2, ClearLabels, 98, 28);
        insideList = AUI.V(AUI.Rect("Inside", t), 0, 0, 4, 0, 4);
        AUI.Btn(t, "Delete part", 13, Color.white, AUI.Hex("#8B2C2C"), DeleteSelected, -1, 30);
    }

    // ───────────────────────────── Part library ─────────────────────────────
    void BuildSidebar()
    {
        Clear(sideContent);
        var parts = data.parts.Where(p => p != null && p.builder == mode).ToList();
        string q = search ? search.text.Trim().ToLowerInvariant() : "";
        if (q.Length > 0)
        {
            int n = 0;
            foreach (var p in parts)
                if ((p.name + " " + p.category + " " + p.subcategory + " " + p.role).ToLowerInvariant().Contains(q)) { PartRow(p, 8); if (++n >= 80) break; }
            if (n == 0) AUI.Text(sideContent, "No parts match.", 13, Sub);
            return;
        }
        foreach (var cat in parts.Select(p => p.category).Distinct().ToList())
        {
            string ck = mode + "/" + cat;
            bool co = open.Contains(ck);
            Header(cat, ck, co, 15f, Panel2, 0, 34f);
            if (!co) continue;
            foreach (var sub in parts.Where(p => p.category == cat).Select(p => p.subcategory).Distinct().ToList())
            {
                string sk = ck + "/" + sub;
                bool so = open.Contains(sk);
                Header(sub, sk, so, 13.5f, RowBg, 10, 28f);
                if (!so) continue;
                foreach (var p in parts) if (p.category == cat && p.subcategory == sub) PartRow(p, 20);
            }
        }
    }

    void Header(string label, string k, bool isOpen, float size, Color bg, int indent, float h)
    {
        var b = AUI.Btn(sideContent, (isOpen ? "-  " : "+  ") + label, size, isOpen ? Txt : Sub, bg, () =>
        {
            if (!open.Remove(k)) open.Add(k);
            BuildSidebar();
        }, -1, h);
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.MidlineLeft; t.margin = new Vector4(indent, 0, 0, 0);
    }

    void PartRow(CADPart p, int indent)
    {
        bool on = picked == p;
        var b = AUI.Btn(sideContent, "", 13, Txt, on ? new Color(Accent.r, Accent.g, Accent.b, 0.4f) : RowBg, () => Pick(p), -1, 44);
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.MidlineLeft; t.fontStyle = FontStyles.Normal; t.margin = new Vector4(indent, 0, 0, 0);
        bool afford = p.costUSD <= AscendEconomy.Balance || AscendEconomy.Owned(p.id) > 0;
        t.text = "<noparse>" + p.name + "</noparse>\n<size=85%><color=" + (afford ? "#9AA0A6" : "#F85149") + ">" +
                 AscendReport.Money(p.costUSD) + "  •  " + AscendReport.Mass(p.WetMassKg) +
                 (AscendEconomy.Owned(p.id) > 0 ? "  •  owned " + AscendEconomy.Owned(p.id) : "") + "</color></size>";
    }

    static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; c.SetActive(false); Object.Destroy(c); }
    }

    void Pick(CADPart p)
    {
        picked = picked == p ? null : p;
        sel = -1;
        DestroyGhosts();
        if (picked != null && picked.attach == CADAttach.RadialUpright && sym == 1) SetSym(2);
        if (picked != null)
            Status($"Placing {picked.name}: " + (D.IsEmpty ? "click anywhere to start the design." : HowTo(picked)) + "  Right-click to stop.", Blue);
        BuildSidebar(); Recolor(); ShowInfo();
    }

    static string HowTo(CADPart p)
    {
        switch (p.attach)
        {
            case CADAttach.Internal: return "click a bay, interstage, frame or bus to install it inside.";
            case CADAttach.Radial: return "click the side (or top/bottom face) of a part.";
            case CADAttach.RadialUpright: return "click the side of the core stage.";
            default: return p.role == CADRole.Engine ? "click the bottom half of a tank (symmetry > 1 = engine ring)." : "click the top or bottom half of a part to snap there.";
        }
    }

    void SetSym(int n)
    {
        sym = n;
        foreach (var (k, img) in symButtons) img.color = k == n ? Accent : Panel2;
    }

    // ───────────────────────────── Modes ─────────────────────────────
    void SetMode(CADBuilder m)
    {
        CloseModal(); CloseMenu();
        padsOpen = false; if (padPanel) padPanel.gameObject.SetActive(false);
        if (padTab) ((Image)padTab.targetGraphic).color = Panel2;
        mode = m; picked = null; sel = -1;
        DestroyGhosts();
        bool r = m == CADBuilder.Rocket;
        rocketRoot.gameObject.SetActive(r);
        payloadRoot.gameObject.SetActive(!r);
        grid.localScale = Vector3.one * (r ? 160f : 6f);   // rocket: 2 m cells / 10 m major lines, payload: 10 cm / 50 cm
        grid.GetComponent<MeshFilter>().sharedMesh = CADModels.GridMesh(r ? 16 : 12);
        ((Image)rocketTab.targetGraphic).color = r ? Accent : Panel2;
        ((Image)payloadTab.targetGraphic).color = r ? Panel2 : Accent;
        nameField.SetTextWithoutNotify(D.name);
        search.SetTextWithoutNotify("");
        SetSym(1);
        BuildSidebar();
        Sync(false);
        Frame();
        Changed();
    }

    void Frame()
    {
        bool r = mode == CADBuilder.Rocket;
        var b = D.IsEmpty ? new Bounds(Vector3.up * (r ? 3f : 0.2f), Vector3.one * (r ? 6f : 0.4f)) : CADStats.DesignBounds(D, data);
        target = b.center;
        float rad = Mathf.Max(b.extents.magnitude, r ? 1f : 0.08f);
        dist = rad / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        dirty = 2;
    }

    // ───────────────────────────── Viewport input ─────────────────────────────
    void OnViewDrag(PointerEventData e)
    {
        var d = e.delta;
        if (drag == Drag.Orbit) { yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.25f, -85f, 85f); }
        else if (drag == Drag.Pan)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            target -= (rot * Vector3.right * d.x + rot * Vector3.up * d.y) * dist * 0.0018f;
        }
        dirty = 2;
    }

    void OnViewUp(PointerEventData e)
    {
        if (e.dragging || modal) return;
        if (e.button == PointerEventData.InputButton.Left) Click(e.position);
        else if (e.button == PointerEventData.InputButton.Right)
        {
            if (picked != null) { picked = null; DestroyGhosts(); BuildSidebar(); ShowInfo(); Status("Stopped placing.", Sub); }
            else if (sel >= 0) { sel = -1; CloseMenu(); Recolor(); ShowInfo(); }
        }
    }

    bool Raycast(Vector2 screen, out int idx, out Vector3 point, out Vector3 normal)
    {
        idx = -1; point = normal = Vector3.zero;
        var vr = view.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(vr, screen, null, out var lp)) return false;
        var r = vr.rect;
        var v = new Vector2((lp.x - r.x) / r.width, (lp.y - r.y) / r.height);
        if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return false;
        PlaceCamera();
        if (!Physics.Raycast(cam.ViewportPointToRay(v), out var hit, 5000f, 1 << layer, QueryTriggerInteraction.Ignore)) return false;
        var tag = hit.collider.GetComponentInParent<CADPartTag>();
        if (!tag || tag.index < 0 || tag.index >= D.parts.Count) return false;
        idx = tag.index;
        point = Root.InverseTransformPoint(hit.point);
        normal = Root.InverseTransformDirection(hit.normal);
        return true;
    }

    void Hover(Vector2 screen)
    {
        if (picked == null || modal) { HideGhosts(); return; }
        Raycast(screen, out int idx, out var hp, out var hn);
        var plan = Compute(picked, idx, hp, hn);
        if (plan.adds.Count == 0)
        {
            HideGhosts();
            Status(plan.msg, plan.ok ? Sub : Bad);
            return;
        }
        if (ghostPart != picked) DestroyGhosts();
        ghostPart = picked;
        while (ghosts.Count < plan.adds.Count) ghosts.Add(CADModels.BuildGhost(picked, data, Root));
        for (int i = 0; i < ghosts.Count; i++)
        {
            bool on = i < plan.adds.Count;
            ghosts[i].SetActive(on);
            if (!on) continue;
            ghosts[i].transform.localPosition = plan.adds[i].pos;
            ghosts[i].transform.localRotation = plan.adds[i].rot;
            CADModels.SetGhostState(ghosts[i], plan.ok);
        }
        Status(plan.ok ? $"Click to place {picked.name}" + (plan.adds.Count > 1 ? $" x{plan.adds.Count}" : "") + plan.msg : plan.msg, plan.ok ? Good : Bad);
        dirty = 2;
    }

    void HideGhosts() { foreach (var g in ghosts) if (g && g.activeSelf) { g.SetActive(false); dirty = 2; } }
    void DestroyGhosts() { foreach (var g in ghosts) if (g) Object.Destroy(g); ghosts.Clear(); ghostPart = null; dirty = 2; }

    void Click(Vector2 screen)
    {
        Raycast(screen, out int idx, out var hp, out var hn);
        if (picked != null) { Apply(Compute(picked, idx, hp, hn)); Hover(screen); return; }
        sel = idx;
        if (sel >= 0)
        {
            var T = D.parts[sel]; var tp = data.Get(T.partId);
            var lp = Quaternion.Inverse(T.rot) * (hp - T.pos);
            float half = tp.StackHeight * 0.5f;
            selAngle = Mathf.Atan2(lp.z, lp.x) * Mathf.Rad2Deg;
            selH01 = half > 0f ? Mathf.Clamp01((lp.y + half) / (2f * half)) : 0.5f;
            Status($"Selected {tp.name}. Use the menu to move or delete it; paint and labels are on the right.", Sub);
        }
        Recolor(); ShowInfo();
        if (sel >= 0) ShowMenu(screen); else CloseMenu();
    }

    // ───────────────────────────── Snapping ─────────────────────────────
    static Plan Fail(string m) => new Plan { ok = false, msg = m };
    static CADPlaced New(CADPart p, int parent, CADLink link, Vector3 pos, Quaternion rot) =>
        new CADPlaced { partId = p.id, parent = parent, link = link, pos = pos, rot = rot, color = p.defaultColor };

    Plan Compute(CADPart p, int ti, Vector3 hp, Vector3 hn)
    {
        if (D.IsEmpty)
        {
            if (p.attach == CADAttach.Internal || p.attach == CADAttach.Radial) return Fail("Start with a body part: a tank, engine, frame or bus.");
            var first = new Plan { ok = true };
            first.adds.Add(New(p, -1, CADLink.Root, new Vector3(0f, p.StackHeight * 0.5f, 0f), Quaternion.identity));
            return first;
        }
        if (ti < 0) return Fail("Point at a part to attach " + p.name + " to.");
        var T = D.parts[ti]; var tp = data.Get(T.partId);
        if (tp == null) return Fail("Unknown part.");
        var inv = Quaternion.Inverse(T.rot);
        Vector3 lp = inv * (hp - T.pos), ln = inv * hn;
        switch (p.attach)
        {
            case CADAttach.Internal: return InternalPlan(p, ti, tp, T);
            case CADAttach.Stack: return StackPlan(p, ti, tp, T, lp);
            case CADAttach.RadialUpright: return UprightPlan(p, ti, tp, T, lp, ln);
            default: return RadialPlan(p, ti, tp, T, lp, ln);
        }
    }

    Plan InternalPlan(CADPart p, int ti, CADPart tp, CADPlaced T)
    {
        if (tp.internalVolumeL <= 0f)
            return Fail($"{tp.name} has no room inside. Avionics go in an Avionics Bay or Interstage; satellite parts go in a frame or bus.");
        float used = 0f;
        for (int i = 0; i < D.parts.Count; i++)
            if (D.parts[i].parent == ti && D.parts[i].link == CADLink.Internal) used += data.Get(D.parts[i].partId)?.volumeL ?? 0f;
        if (used + p.volumeL > tp.internalVolumeL + 1e-4f)
            return Fail($"Not enough room in {tp.name}: {used:0.##}/{tp.internalVolumeL:0.##} L used, this needs {p.volumeL:0.##} L.");
        var plan = new Plan { ok = true, msg = $"  (inside {tp.name}: {used + p.volumeL:0.##}/{tp.internalVolumeL:0.##} L)" };
        plan.adds.Add(New(p, ti, CADLink.Internal, T.pos, T.rot));
        return plan;
    }

    Plan StackPlan(CADPart p, int ti, CADPart tp, CADPlaced T, Vector3 lp)
    {
        if (!tp.IsBody) return Fail("Can't stack onto a side-mounted part.");
        if (T.link == CADLink.Cluster) return Fail("Attach to the tank above the engine ring instead.");
        bool top = lp.y >= 0f;
        if (p.role == CADRole.Engine && !top && tp.role == CADRole.Decoupler)
            return Fail("Engines can't hang from an interstage. Click the BOTTOM half of the tank ABOVE the interstage: the upper-stage engine hides inside the interstage, like on Falcon 9.");
        if (p.role == CADRole.Engine && !top) return ClusterPlan(p, ti, tp, T);
        float h = p.StackHeight, hT = tp.StackHeight;
        var up = T.rot * Vector3.up;
        var link = top ? CADLink.Top : CADLink.Bottom;
        Vector3 node = T.pos + up * (top ? hT : -hT) * 0.5f, shift = up * (top ? h : -h);
        var plan = new Plan { ok = true };

        int child = -1;
        for (int i = 0; i < D.parts.Count; i++) if (D.parts[i].parent == ti && D.parts[i].link == link) { child = i; break; }
        bool parentSide = T.parent >= 0 && T.link == (top ? CADLink.Bottom : CADLink.Top);

        if (child >= 0)            // insert between T and the part already on that node
        {
            plan.adds.Add(New(p, ti, link, node + shift * 0.5f, T.rot));
            plan.after = n => { D.parts[child].parent = n; D.parts[child].link = link; D.Shift(child, shift); };
            plan.msg = "  (inserts into the stack)";
        }
        else if (parentSide)       // insert between T and its parent
        {
            int pa = T.parent; var tl = T.link;
            plan.adds.Add(New(p, pa, tl, node - shift * 0.5f, T.rot));
            plan.after = n => { D.parts[ti].parent = n; D.parts[ti].link = tl; D.Shift(ti, -shift); };
            plan.msg = "  (inserts into the stack)";
        }
        else plan.adds.Add(New(p, ti, link, node + shift * 0.5f, T.rot));
        return plan;
    }

    Plan ClusterPlan(CADPart p, int ti, CADPart tp, CADPlaced T)
    {
        float Rt = (tp.IsBoxy ? Mathf.Min(tp.boxSize.x, tp.boxSize.z) : tp.diameter) * 0.5f, Re = p.MaxRadius, rc = 0f;
        float y0 = -(tp.StackHeight + p.length) * 0.5f;
        for (int i = 0; i < D.parts.Count; i++)
        {
            var o = D.parts[i];
            if (o.parent != ti || (o.link != CADLink.Bottom && o.link != CADLink.Cluster)) continue;
            var c = data.Get(o.partId);
            if (c == null || c.role != CADRole.Engine) continue;
            var off = Quaternion.Inverse(T.rot) * (o.pos - T.pos); off.y = 0f;
            if (off.magnitude < 0.05f) rc = c.MaxRadius;   // a center engine is already there
        }
        if (T.link == CADLink.Top && T.parent >= 0)
        {
            var pe = data.Get(D.parts[T.parent].partId);
            if (pe != null && pe.role == CADRole.Engine) rc = Mathf.Max(rc, pe.MaxRadius);
        }
        if (sym == 1)
        {
            if (rc > 0f) return Fail("There's already a center engine under this tank. Use symmetry x2 or more to add a ring around it.");
            var one = new Plan { ok = true, msg = "  (center engine)" };
            one.adds.Add(New(p, ti, CADLink.Cluster, T.pos + T.rot * (Vector3.up * y0), T.rot));
            return one;
        }
        float rr = Mathf.Max(Rt - Re * 1.02f, Re * 1.1f);
        if (rc > 0f) rr = Mathf.Max(rr, rc + Re * 1.05f);
        if (2f * rr * Mathf.Sin(Mathf.PI / sym) < 2f * Re * 0.98f)
            return Fail($"{sym} x {p.name} don't fit under a Ø{Rt * 2f:0.##} m part. Lower the symmetry or use a wider tank.");
        var plan = new Plan { ok = true, msg = rr + Re > Rt * 1.35f ? "  (nozzles stick out past the tank)" : "  (engine ring)" };
        float y = y0;
        for (int k = 0; k < sym; k++)
        {
            var dir = Quaternion.Euler(0f, 360f / sym * k, 0f) * Vector3.forward;
            plan.adds.Add(New(p, ti, CADLink.Cluster, T.pos + T.rot * (dir * rr + Vector3.up * y), T.rot));
        }
        return plan;
    }

    static Vector3 HorizDir(CADPart tp, Vector3 lp, Vector3 ln)
    {
        if (tp.IsBoxy)
        {
            var v = Mathf.Abs(ln.y) > 0.7f ? lp : ln;
            return Mathf.Abs(v.x) / Mathf.Max(1e-4f, tp.boxSize.x) >= Mathf.Abs(v.z) / Mathf.Max(1e-4f, tp.boxSize.z)
                ? new Vector3(v.x >= 0f ? 1f : -1f, 0f, 0f) : new Vector3(0f, 0f, v.z >= 0f ? 1f : -1f);
        }
        var d = new Vector3(lp.x, 0f, lp.z);
        return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
    }

    Plan UprightPlan(CADPart p, int ti, CADPart tp, CADPlaced T, Vector3 lp, Vector3 ln)
    {
        if (!tp.IsBody || T.link == CADLink.Cluster) return Fail("Boosters attach to the side of a tank or core stage.");
        if (T.link == CADLink.RadialUpright) return Fail("Attach boosters to the core stage, not to another booster.");
        var dir = HorizDir(tp, lp, ln);
        float half = tp.StackHeight * 0.5f, y = Mathf.Clamp(lp.y, -half, half), h01 = (y + half) / (2f * half);
        var plan = new Plan { ok = true };
        for (int k = 0; k < sym; k++)
        {
            var dk = Quaternion.Euler(0f, 360f / sym * k, 0f) * dir;
            float d = CADModels.SurfaceDistance(tp, dk, h01) + p.MaxRadius * 1.02f;
            plan.adds.Add(New(p, ti, CADLink.RadialUpright, T.pos + T.rot * (dk * d + Vector3.up * y), T.rot * Quaternion.LookRotation(dk, Vector3.up)));
        }
        return plan;
    }

    Plan RadialPlan(CADPart p, int ti, CADPart tp, CADPlaced T, Vector3 lp, Vector3 ln)
    {
        if (!tp.IsBody) return Fail("Attach to a body part (tank, bus, frame...), not to another side part.");
        float half = tp.StackHeight * 0.5f;
        var plan = new Plan { ok = true };
        bool capable = tp.shape != CADShape.NoseCone && tp.shape != CADShape.Fairing && tp.shape != CADShape.Engine;
        if (capable && Mathf.Abs(ln.y) > 0.8f)   // top / bottom face
        {
            var face = ln.y > 0f ? Vector3.up : Vector3.down;
            float ex = tp.IsBoxy ? tp.boxSize.x * 0.5f : tp.MaxRadius, ez = tp.IsBoxy ? tp.boxSize.z * 0.5f : tp.MaxRadius;
            var lpos = new Vector3(Mathf.Clamp(lp.x, -ex, ex), half * face.y, Mathf.Clamp(lp.z, -ez, ez));
            plan.adds.Add(New(p, ti, CADLink.Radial, T.pos + T.rot * lpos, T.rot * Quaternion.LookRotation(face, Vector3.forward)));
            if (sym > 1) plan.msg = "  (symmetry is off on top/bottom faces)";
            return plan;
        }
        var dir = HorizDir(tp, lp, ln);
        float y = Mathf.Clamp(lp.y, -half, half), h01 = (y + half) / (2f * half);
        var tangent = Vector3.Cross(Vector3.up, dir);
        float side = 0f;
        if (tp.IsBoxy)
        {
            float lim = Mathf.Abs(Vector3.Dot(tp.boxSize * 0.5f, tangent));
            side = Mathf.Clamp(Vector3.Dot(lp, tangent), -lim, lim);
        }
        for (int k = 0; k < sym; k++)
        {
            var q = Quaternion.Euler(0f, 360f / sym * k, 0f);
            Vector3 dk = q * dir, tk = q * tangent;
            var lpos = dk * CADModels.SurfaceDistance(tp, dk, h01) + tk * side + Vector3.up * y;
            plan.adds.Add(New(p, ti, CADLink.Radial, T.pos + T.rot * lpos, T.rot * Quaternion.LookRotation(dk, Vector3.up)));
        }
        return plan;
    }

    void Apply(Plan plan)
    {
        if (plan == null) return;
        if (!plan.ok || plan.adds.Count == 0) { Status(plan.msg, Bad); return; }
        var counts = AscendEconomy.Count(rocket, payload);
        foreach (var a in plan.adds) { counts.TryGetValue(a.partId, out var n); counts[a.partId] = n + 1; }
        var q = AscendEconomy.QuoteCounts(data, counts);
        if (q.total > AscendEconomy.Balance + 0.01)
        {
            Status($"Over budget: the build would cost {AscendReport.Money(q.total)} but the fund only has {AscendReport.Money(AscendEconomy.Balance)}.", Bad);
            return;
        }
        bool wasEmpty = D.IsEmpty;
        int first = D.parts.Count, g = plan.adds.Count > 1 ? D.nextSymGroup++ : -1;
        bool skirted = false;
        foreach (var a in plan.adds)
        {
            a.symGroup = g;
            var ap = data.Get(a.partId);
            if (ap != null && ap.shape == CADShape.Engine && ap.ispSL <= 0f && a.link == CADLink.Cluster && a.parent >= 0)
            {
                var T = D.parts[a.parent];
                var below = T.link == CADLink.Top && T.parent >= 0 ? data.Get(D.parts[T.parent].partId) : null;
                bool inside = below != null && below.role != CADRole.Engine && below.role != CADRole.SolidMotor;
                if (!inside) { a.shroud = true; skirted = true; }   // not inside an interstage
            }
            D.parts.Add(a);
        }
        plan.after?.Invoke(first);
        Ground();
        Sync(true);
        if (wasEmpty) Frame();
        Changed();
        Status($"Placed {picked.name}" + (plan.adds.Count > 1 ? $" x{plan.adds.Count}" : "") + "." +
               (skirted ? " Vacuum engine hidden in a skirt (click it to show it again)." : ""), Good);
    }

    // Keeps the design standing on the grid
    void Ground()
    {
        if (D.IsEmpty) return;
        float y = CADStats.DesignBounds(D, data).min.y;
        if (Mathf.Abs(y) < 1e-4f) return;
        foreach (var p in D.parts) p.pos.y -= y;
    }

    // ───────────────────────────── Scene sync (incremental) ─────────────────────────────
    void Sync(bool full)
    {
        var objs = Objs;
        if (full) { foreach (var o in objs) if (o) Object.Destroy(o); objs.Clear(); }
        while (objs.Count > D.parts.Count) { var o = objs[objs.Count - 1]; if (o) Object.Destroy(o); objs.RemoveAt(objs.Count - 1); }
        for (int i = 0; i < objs.Count; i++)
            if (objs[i]) objs[i].transform.SetLocalPositionAndRotation(D.parts[i].pos, D.parts[i].rot);
        for (int i = objs.Count; i < D.parts.Count; i++) objs.Add(Make(i));
        Physics.SyncTransforms();
        Recolor();
    }

    GameObject Make(int i)
    {
        var pl = D.parts[i]; var p = data.Get(pl.partId);
        if (p == null || p.attach == CADAttach.Internal) return null;
        var go = CADModels.Build(p, pl.color, layer, true, data.labelFont ? data.labelFont : font, pl.labels, data, CADModels.CoverFor(D, i, data));
        go.transform.SetParent(Root, false);
        go.transform.SetLocalPositionAndRotation(pl.pos, pl.rot);
        go.AddComponent<CADPartTag>().index = i;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        return go;
    }

    void Remake(int i)
    {
        var objs = Objs;
        if (i < 0 || i >= objs.Count) return;
        if (objs[i]) Object.Destroy(objs[i]);
        objs[i] = Make(i);
        Physics.SyncTransforms();
        Recolor();
    }

    void Recolor()
    {
        var objs = Objs;
        var selSet = sel >= 0 && sel < D.parts.Count ? new HashSet<int>(D.SymGroup(sel)) : null;
        for (int i = 0; i < objs.Count && i < D.parts.Count; i++)
        {
            if (!objs[i]) continue;
            var p = data.Get(D.parts[i].partId);
            bool s = selSet != null && selSet.Contains(i);
            if (p != null && p.modelPrefab && !p.tintPrefab && !s)
            {
                foreach (var r in objs[i].GetComponentsInChildren<MeshRenderer>()) if (!r.GetComponent<TMP_Text>()) r.SetPropertyBlock(null);
            }
            else CADModels.Tint(objs[i], s ? Color.Lerp(D.parts[i].color, Blue, 0.55f) : D.parts[i].color, s ? Blue : (Color?)null);
        }
        dirty = 2;
    }

    // ───────────────────────────── Editing ─────────────────────────────
    void DeleteSelected()
    {
        if (sel < 0 || sel >= D.parts.Count) { Status("Select a part first (left-click it).", Sub); return; }
        var pl = D.parts[sel];
        string n = data.Get(pl.partId)?.name;
        var remove = new HashSet<int>();
        if (InColumn(sel))
        {
            var col = Column(sel);
            var info = ColumnInfo(col);
            foreach (int m in info.moving[sel]) remove.Add(m);
            var order = col.Where(c => c != sel).ToList();
            if (order.Count > 0) Relayout(order, info);       // parts above slide down to close the gap
        }
        else foreach (int i in D.SymGroup(sel)) D.CollectSubtree(i, remove);
        D.RemoveSet(remove);
        sel = -1;
        CloseMenu();
        Ground(); Sync(true); Changed();
        Status($"Removed {n}" + (remove.Count > 1 ? $" and {remove.Count - 1} attached part(s)." : "."), Sub);
    }

    // ───────────────────────────── Part menu (move up / move down / hide engine / delete) ─────────────────────────────
    void ShowMenu(Vector2 screen)
    {
        CloseMenu();
        if (sel < 0 || sel >= D.parts.Count || !viewRT) return;
        var pl = D.parts[sel]; var p = data.Get(pl.partId);
        if (p == null) return;
        menuScreen = screen;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRT, screen, null, out var lp);

        menu = AUI.Rect("PartMenu", viewRT);
        menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 0.5f); menu.pivot = new Vector2(0f, 1f);
        AUI.Img(menu, Panel, true, true);
        AUI.V(menu, 10, 10, 10, 10, 6);
        var fit = menu.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = AUI.Text(menu, "<noparse>" + p.name + "</noparse>", 14, Txt, FontStyles.Bold);
        title.textWrappingMode = TextWrappingModes.NoWrap; title.overflowMode = TextOverflowModes.Truncate;
        AUI.LE(title, S(200));
        bool movable = pl.link != CADLink.Internal && pl.link != CADLink.Cluster;
        string how = InColumn(sel) ? "(swaps with the part above / below)" : movable ? "(slides along the part it's on)" : "(this part can't move)";
        var hint = AUI.Text(menu, how, 11.5f, Sub); AUI.LE(hint, S(200));
        AUI.Btn(menu, "Move Up", 14, Txt, Panel2, () => MoveSel(1), 200, 34).interactable = movable;
        AUI.Btn(menu, "Move Down", 14, Txt, Panel2, () => MoveSel(-1), 200, 34).interactable = movable;
        if (pl.link == CADLink.Cluster && p.shape == CADShape.Engine)
        {
            bool hidden = false;
            foreach (var o in D.parts) if (o.parent == pl.parent && o.link == CADLink.Cluster && o.shroud) hidden = true;
            AUI.Btn(menu, hidden ? "Show Engine" : "Hide Engine (skirt)", 14, Txt, Panel2, ToggleShroud, 200, 34);
        }
        AUI.Btn(menu, "Delete", 14, Color.white, AUI.Hex("#8B2C2C"), DeleteSelected, 200, 34);
        AUI.Btn(menu, "Close", 13, Sub, RowBg, CloseMenu, 200, 28);

        LayoutRebuilder.ForceRebuildLayoutImmediate(menu);
        var vr = viewRT.rect; var size = menu.rect.size;
        var pos = lp + new Vector2(14f, -14f);
        pos.x = Mathf.Clamp(pos.x, vr.xMin + 8f, vr.xMax - size.x - 8f);
        pos.y = Mathf.Clamp(pos.y, vr.yMin + size.y + S(48f), vr.yMax - 8f);
        menu.anchoredPosition = pos;
    }

    void CloseMenu() { if (menu) Object.Destroy(menu.gameObject); menu = null; }

    void MoveSel(int dir)
    {
        if (sel < 0 || sel >= D.parts.Count) return;
        var pl = D.parts[sel];
        string n = data.Get(pl.partId)?.name;
        if (InColumn(sel))
        {
            var col = Column(sel);
            int a = col.IndexOf(sel), b = a + dir;
            if (b < 0 || b >= col.Count) { Status(dir > 0 ? $"{n} is already at the top of its stack." : $"{n} is already at the bottom of its stack.", Sub); return; }
            var info = ColumnInfo(col);
            var order = new List<int>(col);
            order[a] = col[b]; order[b] = sel;
            Relayout(order, info);
        }
        else
        {
            if (pl.parent < 0 || pl.link == CADLink.Internal || pl.link == CADLink.Cluster) { Status($"{n} can't be moved; delete it and place it again.", Sub); return; }
            var T = D.parts[pl.parent]; var tp = data.Get(T.partId);
            if (tp == null) return;
            float half = tp.StackHeight * 0.5f, step = Mathf.Clamp(tp.StackHeight * 0.1f, 0.02f, 2f) * dir;
            float ly = (Quaternion.Inverse(T.rot) * (pl.pos - T.pos)).y, ny = Mathf.Clamp(ly + step, -half, half);
            if (Mathf.Abs(ny - ly) < 1e-5f) { Status($"{n} is already at the {(dir > 0 ? "top" : "bottom")} of {tp.name}.", Sub); return; }
            var delta = T.rot * Vector3.up * (ny - ly);
            var set = new HashSet<int>();
            foreach (int i in D.SymGroup(sel)) D.CollectSubtree(i, set);
            foreach (int m in set) D.parts[m].pos += delta;
        }
        Ground(); Sync(true); Changed();
        Status($"Moved {n} {(dir > 0 ? "up" : "down")}.", Good);
        ShowMenu(menuScreen);
    }

    void ToggleShroud()
    {
        if (sel < 0 || sel >= D.parts.Count) return;
        var pl = D.parts[sel];
        if (pl.link != CADLink.Cluster) return;
        bool on = true;
        foreach (var o in D.parts) if (o.parent == pl.parent && o.link == CADLink.Cluster && o.shroud) on = false;
        foreach (var o in D.parts) if (o.parent == pl.parent && o.link == CADLink.Cluster) o.shroud = on;
        Sync(true);
        Status(on ? "Engine hidden inside a skirt. It still works; the CAD view marks it ENGINE INSIDE." : "Engine visible again.", Good);
        ShowMenu(menuScreen);
    }

    // ───────────────────────────── Stack columns ─────────────────────────────
    // A column is a straight stack of parts joined top-to-bottom (the core, or one booster with its nose cone)
    bool InColumn(int i) { var l = D.parts[i].link; return l == CADLink.Root || l == CADLink.Top || l == CADLink.Bottom; }

    List<int> Column(int i)
    {
        var set = new HashSet<int>(); var st = new Stack<int>(); st.Push(i);
        while (st.Count > 0)
        {
            int a = st.Pop();
            if (!set.Add(a)) continue;
            var pa = D.parts[a];
            if ((pa.link == CADLink.Top || pa.link == CADLink.Bottom) && pa.parent >= 0) st.Push(pa.parent);
            for (int j = 0; j < D.parts.Count; j++)
                if (D.parts[j].parent == a && (D.parts[j].link == CADLink.Top || D.parts[j].link == CADLink.Bottom)) st.Push(j);
        }
        return set.OrderBy(k => D.parts[k].pos.y).ToList();
    }

    class ColInfo { public int anchorParent = -1; public CADLink anchorLink = CADLink.Root; public float baseY; public Dictionary<int, HashSet<int>> moving; }

    ColInfo ColumnInfo(List<int> col)
    {
        var inCol = new HashSet<int>(col);
        var ci = new ColInfo { moving = new Dictionary<int, HashSet<int>>(), baseY = float.MaxValue };
        foreach (int c in col)
        {
            var pc = D.parts[c];
            if (pc.parent < 0 || !inCol.Contains(pc.parent)) { ci.anchorParent = pc.parent; ci.anchorLink = pc.parent < 0 ? CADLink.Root : pc.link; }
            float h = data.Get(pc.partId)?.StackHeight ?? 0f;
            ci.baseY = Mathf.Min(ci.baseY, pc.pos.y - h * 0.5f);
            var set = new HashSet<int> { c };
            for (int j = 0; j < D.parts.Count; j++) if (D.parts[j].parent == c && !inCol.Contains(j)) D.CollectSubtree(j, set);
            ci.moving[c] = set;
        }
        return ci;
    }

    // Restacks the column in the given order (bottom -> top) with no gaps; attached parts travel along
    void Relayout(List<int> order, ColInfo ci)
    {
        float y = ci.baseY;
        foreach (int c in order)
        {
            float h = data.Get(D.parts[c].partId)?.StackHeight ?? 0f;
            float dy = y + h * 0.5f - D.parts[c].pos.y;
            if (Mathf.Abs(dy) > 1e-6f) foreach (int m in ci.moving[c]) D.parts[m].pos.y += dy;
            y += h;
        }
        for (int k = 0; k < order.Count; k++)
        {
            var q = D.parts[order[k]];
            if (k == 0) { q.parent = ci.anchorParent; q.link = ci.anchorLink; }
            else { q.parent = order[k - 1]; q.link = CADLink.Top; }
        }
    }

    // ───────────────────────────── Launch pads ─────────────────────────────
    void OpenPads()
    {
        CloseMenu(); CloseModal(); DestroyGhosts();
        picked = null; padsOpen = true;
        padPanel.gameObject.SetActive(true);
        padPanel.SetAsLastSibling();
        ((Image)padTab.targetGraphic).color = Accent;
        ((Image)rocketTab.targetGraphic).color = Panel2;
        ((Image)payloadTab.targetGraphic).color = Panel2;
        BuildPads();
        Status("", Sub);
    }

    void BuildPads()
    {
        Clear(padGrid);
        var ps = payload.IsEmpty ? null : CADStats.Payload(payload, data);
        var rs = CADStats.Rocket(rocket, data, ps != null ? ps.wetMass : 0f);
        padHeader.text = rocket.IsEmpty
            ? "Build a rocket first: every pad is checked against your current rocket. Pads are bought once and kept forever."
            : $"Your rocket <b>{rocket.name}</b>: {rs.height:0.#} m tall,  {rs.width:0.#} m wide,  {AscendPads.RocketMassT(rs):N0} t at liftoff.   " +
              $"Fund {AscendReport.Money(AscendEconomy.Balance)}.   Pads are bought once and kept forever.";
        int n = 0;
        foreach (var p in data.launchPads) if (p != null) PadCard(p, rs, !rocket.IsEmpty, n++);
    }

    void PadCard(CADLaunchPad p, CADStats rs, bool haveRocket, int index)
    {
        var card = AUI.Rect(p.name, padGrid);
        AUI.Img(card, Panel, true, true);
        AUI.V(card, 14, 14, 14, 14, 7);

        var ib = AUI.Rect("Picture", card);
        AUI.LE(ib, -1, S(190f));
        AUI.Img(ib, AUI.Hex("#101114"), true);
        PadView view3d;
        if (p.image)                                              // your picture (cheapest)
        {
            var pic = AUI.Rect("Image", ib); AUI.Fill(pic, 6, 6, 6, 6);
            var img = pic.gameObject.AddComponent<Image>(); img.sprite = p.image; img.preserveAspect = true; img.raycastTarget = false;
        }
        else if ((view3d = GetPadView(p, index)) != null)         // no picture: a one-time snapshot of the 3D model
        {
            var pic = AUI.Rect("Model", ib); AUI.Fill(pic, 4, 4, 4, 4);
            var ri = pic.gameObject.AddComponent<RawImage>(); ri.texture = view3d.rt; ri.raycastTarget = false;
        }
        else
        {
            var t = AUI.Text(ib, $"<size=180%><b>{p.name.Substring(0, 1)}</b></size>\n<size=80%>Drag your 3D model into\nAscendCAD Data > Launch Pads > Model Prefab</size>", 13, Sub);
            AUI.Fill(t.rectTransform, 8, 8, 8, 8); t.alignment = TextAlignmentOptions.Center;
        }

        AUI.Text(card, p.isTransporter ? "SUPPORT VEHICLE" : "LAUNCH PAD", 11, Accent, FontStyles.Bold);
        AUI.Text(card, "<noparse>" + p.name + "</noparse>", 19, Txt, FontStyles.Bold);
        AUI.Text(card, p.description, 12.5f, Sub);
        var lim = new System.Text.StringBuilder();
        if (p.isTransporter) lim.Append($"Carries up to <b>{p.carryCapacityT:N0} t</b> (rocket + launcher)");
        else
        {
            lim.Append($"Max height <b>{(p.maxHeightM > 0 ? p.maxHeightM.ToString("0.#") + " m" : "any")}</b>   ");
            lim.Append($"Max width <b>{(p.maxWidthM > 0 ? p.maxWidthM.ToString("0.#") + " m" : "any")}</b>\n");
            lim.Append($"Max liftoff mass <b>{(p.maxLiftoffMassT > 0 ? p.maxLiftoffMassT.ToString("N0") + " t" : "any")}</b>");
            var req = data.GetPad(p.requiresId);
            if (req != null) lim.Append($"\nNeeds the <b>{req.name}</b>");
        }
        AUI.Text(card, lim.ToString(), 12.5f, Txt);
        AUI.LE(AUI.Rect("Space", card), -1, -1, -1, 1);

        bool owned = AscendEconomy.OwnsPad(p, data);
        AUI.Text(card, owned ? (p.ownedFromStart ? "Provided by the government" : "Owned - bought once, yours forever")
                             : AscendReport.MoneyFull(p.costUSD) + (p.costEstimated ? " <size=80%><color=#9AA0A6>(est.)</color></size>" : ""),
            15, owned ? Good : Txt, FontStyles.Bold);
        if (!owned)
        {
            bool afford = p.costUSD <= AscendEconomy.Balance + 0.01;
            var b = AUI.Btn(card, afford ? "Buy" : "Not enough money", 15, Color.white, afford ? Accent : Panel2, () =>
            {
                if (AscendEconomy.BuyPad(p, data)) Status($"Bought the {p.name}. It's yours for every future launch.", Good);
                else Status("Not enough money.", Bad);
                BuildPads();
            }, -1, 38);
            b.interactable = afford;
        }
        else if (p.isTransporter) AUI.Btn(card, "Owned", 15, Good, Panel2, null, -1, 38).interactable = false;
        else if (AscendEconomy.SelectedPad == p.id) AUI.Btn(card, "Selected", 15, Color.white, AUI.Hex("#2E7D46"), null, -1, 38).interactable = false;
        else AUI.Btn(card, "Choose", 15, Color.white, AUI.Hex("#0B57D0"), () =>
        {
            AscendEconomy.ChoosePad(p.id);
            Status($"Launching from the {p.name}.", Good);
            BuildPads(); UpdateFund();
        }, -1, 38);

        // Black overlay when this rocket can't use the pad; dark overlay when something else must be bought first
        var problems = haveRocket ? AscendPads.Problems(p, rs, data) : new List<string>();
        string lockWhy = AscendPads.Lock(p, data);
        if (problems.Count == 0 && lockWhy == null) return;
        var ov = AUI.Rect("Overlay", card);
        ov.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        AUI.Fill(ov);
        AUI.Img(ov, problems.Count > 0 ? new Color(0f, 0f, 0f, 0.97f) : new Color(0.05f, 0.05f, 0.06f, 0.97f), true, true);
        var head = problems.Count > 0
            ? "<size=125%><b><color=#F85149>NOT COMPATIBLE\nWITH ROCKET</color></b></size>\n\n" + string.Join("\n\n", problems)
            : "<size=120%><b><color=#F07A26>LOCKED</color></b></size>\n\n" + lockWhy;
        var ot = AUI.Text(ov, head, 14, Color.white);
        AUI.Fill(ot.rectTransform, 18, 18, 18, 18);
        ot.alignment = TextAlignmentOptions.Center;
    }

    // ───────────────────────────── Prebuilt rockets ─────────────────────────────
    void ShowTemplates()
    {
        CloseMenu();
        bool sats = mode == CADBuilder.Payload;
        var names = sats ? AscendTemplates.SatNames : AscendTemplates.Names;
        var blurbs = sats ? AscendTemplates.SatBlurbs : AscendTemplates.Blurbs;
        var sb = new System.Text.StringBuilder(sats
            ? "Load a real satellite into the Payload Builder. It <b>replaces</b> the current satellite design.\n\n"
            : "Load a real rocket into the Rocket Builder. It <b>replaces</b> the current rocket design.\n\n");
        var built = new CADDesign[names.Length];
        for (int i = 0; i < built.Length; i++)
        {
            try
            {
                built[i] = sats ? AscendTemplates.BuildSat(i, data) : AscendTemplates.Build(i, data);
                double cost = AscendEconomy.Quote(data, built[i]).total;
                string price = $"cost <color={(cost > AscendEconomy.Balance ? "#F85149" : "#3FB950")}>{AscendReport.Money(cost)}</color>";
                sb.Append($"<b>{names[i]}</b>  <color=#9AA0A6>{blurbs[i]}</color>\n   ");
                if (sats)
                {
                    var st = CADStats.Payload(built[i], data);
                    sb.Append($"{AscendReport.Mass(st.wetMass)},  {st.instruments} instrument(s),  {st.powerGen:0.#} W power,  folded {st.stowedWidth:0.##} x {st.stowedHeight:0.##} m,  {price}\n\n");
                }
                else
                {
                    var st = CADStats.Rocket(built[i], data, 0f);
                    sb.Append($"{AscendReport.Mass(st.wetMass)} at liftoff,  {st.stages.Count} stage(s),  delta-v {st.deltaV:N0} m/s,  TWR {st.liftoffTWR:0.00},  {price}\n\n");
                }
            }
            catch (Exception e) { sb.Append($"<b>{names[i]}</b>  <color=#F85149>{e.Message}</color>\n\n"); }
        }
        sb.Append("<color=#9AA0A6>Real hardware is expensive. If one costs more than your fund, turn on <b>Sandbox Mode</b> (or raise the fund) on the AscendCAD Data asset.</color>");
        var buttons = new List<(string, bool, Action)>();
        for (int i = 0; i < built.Length; i++)
        {
            var d = built[i];
            buttons.Add((names[i], d != null, () => LoadTemplate(d)));
        }
        buttons.Add(("Cancel", true, CloseModal));
        ShowModal(sats ? "Prebuilt satellites" : "Prebuilt rockets", sb.ToString(), buttons.ToArray());
    }

    // ───────────── Mission Guide ─────────────
    void ShowGuide()
    {
        CloseMenu();
        bool r = mode == CADBuilder.Rocket;
        var m = AscendMissionGuide.Mission;
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{AscendMissionGuide.Title}</b>  <color=#9AA0A6>({AscendMissionGuide.Difficulty})</color>\n{AscendSimPlan.Goal(m)}\n");
        sb.Append($"<size=88%><color=#9AA0A6>{AscendSimPlan.Expert(m)}</color></size>\n\n");
        var list = AscendMissionGuide.Checklist(mode, rocket, payload, data);
        sb.Append($"<b>CHECKLIST: {(r ? "ROCKET" : "PAYLOAD")}</b>\n").Append(AscendMissionGuide.ChecklistText(list)).Append('\n');

        List<AscendMissionGuide.Option> opts;
        string intro;
        try { opts = AscendMissionGuide.Suggest(mode, rocket, payload, data, out intro); }
        catch (Exception e) { opts = new List<AscendMissionGuide.Option>(); intro = "<color=#F85149>" + e.Message + "</color>"; }
        sb.Append($"<b>SUGGESTIONS: {(r ? "ROCKETS" : "PAYLOADS")}</b>\n{intro}\n\n");
        if (opts.Count == 0) sb.Append("<color=#9AA0A6>No suggestion found. Select your AscendCAD Data asset and use its menu: Load default parts.</color>\n");
        foreach (var o in opts)
        {
            string costC = o.affordable ? "#3FB950" : "#F85149";
            sb.Append($"<color=#F07A26><b>{o.tier}</b></color>  <b>{o.design.name}</b>  <color={costC}>{AscendReport.Money(o.cost)}</color>")
              .Append(o.works ? "  <color=#3FB950>can do the mission</color>" : "  <color=#F85149>not enough yet</color>").Append('\n')
              .Append("<size=90%><color=#9AA0A6>").Append(o.summary).Append("</color></size>\n");
            if (!string.IsNullOrEmpty(o.why)) sb.Append("<size=90%><color=#D29922>").Append(o.why).Append("</color></size>\n");
            sb.Append('\n');
        }
        sb.Append("<color=#9AA0A6>Pick a guide to get <b>step-by-step building instructions</b> on the right, with every part shown in 3D. " +
                  "You build it yourself, part by part. Prices are what the parts cost to buy (parts you already own count as free).</color>");

        var buttons = new List<(string, bool, Action)>();
        foreach (var o in opts) { var oo = o; buttons.Add(("Guide: " + o.tier, true, () => StartGuide(oo))); }
        buttons.Add(("Change mission", true, ShowMissionPicker));
        buttons.Add(("Close", true, CloseModal));
        ShowModal("Mission Guide", sb.ToString(), buttons.ToArray());
    }

    // ───────────── Build guide (right panel) ─────────────
    List<AscendMissionGuide.Step> guideSteps;
    string guideName;
    CADBuilder guideMode;
    RectTransform guideBox;

    void StartGuide(AscendMissionGuide.Option o)
    {
        guideSteps = AscendMissionGuide.Steps(o.design, data);
        guideName = $"{o.design.name} ({o.tier})";
        guideMode = o.design.builder;
        CloseModal();
        if (mode != guideMode) SetMode(guideMode);
        sel = -1; picked = null;
        BuildGuidePanel(); ShowInfo();
        Status("Follow the BUILD GUIDE on the right: press 'Show part' on each step, then place it.", Blue);
    }

    void BuildGuidePanel()
    {
        if (!guideBox) return;
        for (int i = guideBox.childCount - 1; i >= 0; i--) { var c = guideBox.GetChild(i).gameObject; c.SetActive(false); Object.Destroy(c); }
        bool show = guideSteps != null && guideSteps.Count > 0;
        guideBox.gameObject.SetActive(show);
        if (!show) return;
        var design = guideMode == CADBuilder.Rocket ? rocket : payload;
        int doneCount = 0;
        foreach (var st in guideSteps) if (AscendMissionGuide.CountOf(design, st) >= st.needTotal) doneCount++;
        AUI.Text(guideBox, $"BUILD GUIDE  <size=75%><color=#9AA0A6>{doneCount}/{guideSteps.Count} steps done</color></size>", 17, Txt, FontStyles.Bold);
        AUI.Text(guideBox, $"<color=#8AB4F8>{guideName}</color>", 13.5f, Txt);
        if (mode != guideMode)
            AUI.Text(guideBox, $"<color=#D29922>Switch to the {(guideMode == CADBuilder.Rocket ? "Rocket" : "Payload")} Builder to follow this guide.</color>", 13, Txt);
        for (int i = 0; i < guideSteps.Count; i++)
        {
            var st = guideSteps[i];
            bool ok = AscendMissionGuide.CountOf(design, st) >= st.needTotal;
            var t = AUI.Text(guideBox, (ok ? "<color=#3FB950>DONE</color>  " : $"<color=#F07A26>STEP {i + 1}</color>  ") + st.text, 13, ok ? Sub : Txt);
            t.lineSpacing = 2f;
            if (!ok && mode == guideMode)
            {
                var part = st.part;
                AUI.Btn(guideBox, "Show part: " + part.name, 12, Color.white, Panel2, () => Pick(part), -1, 30);
            }
        }
        if (doneCount == guideSteps.Count)
        {
            var chk = AscendMissionGuide.Checklist(guideMode, rocket, payload, data);
            var bad = chk.Where(x => !x.ok).ToList();
            if (bad.Count == 0) AUI.Text(guideBox, "<color=#3FB950><b>All steps done and the mission check passes!</b></color> Press SUBMIT.", 13.5f, Txt);
            else
                AUI.Text(guideBox, "<color=#D29922><b>All steps done, but the mission check still finds:</b></color>\n" + AscendMissionGuide.ChecklistText(bad), 13.5f, Txt);
        }
        AUI.Btn(guideBox, "Hide build guide", 12, Txt, Panel2, () => { guideSteps = null; BuildGuidePanel(); }, -1, 30);
    }

    void ShowMissionPicker()
    {
        var sb = new System.Text.StringBuilder("Pick the mission to design for. The checklist, the review board and the suggestions follow it.\n\n");
        foreach (AscendSimMission m in Enum.GetValues(typeof(AscendSimMission)))
            sb.Append($"<b>{AscendSimPlan.Title(m)}</b>{(m == AscendMissionGuide.Mission ? "  <color=#3FB950>(current)</color>" : "")}\n{AscendSimPlan.Goal(m)}\n\n");
        var buttons = new List<(string, bool, Action)>();
        foreach (AscendSimMission m in Enum.GetValues(typeof(AscendSimMission)))
        {
            var mm = m;
            buttons.Add((AscendSimPlan.Title(m), true, () => { AscendMissionGuide.Mission = mm; ShowGuide(); }));
        }
        buttons.Add(("Back", true, ShowGuide));
        ShowModal("Choose mission", sb.ToString(), buttons.ToArray());
    }

    void LoadTemplate(CADDesign d)
    {
        CloseModal();
        if (d == null) return;
        if (mode != d.builder) SetMode(d.builder);
        var target = d.builder == CADBuilder.Rocket ? rocket : payload;
        target.name = d.name; target.parts = d.parts; target.nextSymGroup = d.nextSymGroup;
        sel = -1; picked = null; DestroyGhosts(); CloseMenu();
        nameField.SetTextWithoutNotify(target.name);
        Sync(true); Frame(); Changed(); BuildSidebar();
        double cost = AscendEconomy.Quote(data, rocket, payload).total;
        Status($"Loaded {d.name}." + (cost > AscendEconomy.Balance ? $" The build costs {AscendReport.Money(cost)}, more than the fund: turn on Sandbox Mode to buy it." : ""),
            cost > AscendEconomy.Balance ? Bad : Good);
    }

    void RemoveOne(int i)
    {
        string n = data.Get(D.parts[i].partId)?.name;
        int keepSel = sel;
        D.RemoveSet(new HashSet<int> { i });
        sel = keepSel > i ? keepSel - 1 : keepSel;
        Sync(true); Changed();
        Status($"Removed {n}.", Sub);
    }

    void ApplyColor(Color c)
    {
        if (sel < 0) { Status("Select a part to paint (left-click it).", Sub); return; }
        foreach (int i in D.SymGroup(sel)) D.parts[i].color = c;
        Recolor(); ShowInfo();
    }

    void AddLabel()
    {
        if (sel < 0) { Status("Click the spot on a part where the label should go.", Sub); return; }
        string t = labelField.text.Trim();
        if (t.Length == 0) { Status("Type the label text first.", Bad); return; }
        var p = data.Get(D.parts[sel].partId);
        float size = Mathf.Clamp((p.IsBoxy ? Mathf.Min(p.boxSize.x, p.boxSize.y) : p.diameter) * 0.14f, 0.01f, 1.2f);
        if (labelVertical) size = Mathf.Min(size, p.StackHeight / (0.65f * Mathf.Max(3, t.Length)));
        D.parts[sel].labels.Add(new CADLabel { text = t, angleDeg = selAngle, height01 = selH01, sizeM = size, color = labelColor, vertical = labelVertical });
        Remake(sel); ShowInfo();
        Status($"Label \"{t}\" added. It will also appear on the real rocket.", Good);
    }

    void ClearLabels()
    {
        if (sel < 0) return;
        D.parts[sel].labels.Clear();
        Remake(sel); ShowInfo();
    }

    // ───────────────────────────── Info + stats ─────────────────────────────
    void Changed()
    {
        var ps = payload.IsEmpty ? null : CADStats.Payload(payload, data);
        var rs = CADStats.Rocket(rocket, data, ps != null ? ps.wetMass : 0f, AscendMissionGuide.RequiredDeltaV(data));
        projected = AscendEconomy.Quote(data, rocket, payload).total;
        bool r = mode == CADBuilder.Rocket;
        statsTitle.text = r ? "ROCKET STATS" : "SATELLITE STATS";
        statsText.text = r ? AscendReport.RocketStats(rs, ps, data) : AscendReport.PayloadStats(ps, rs, data);
        UpdateFund();
        if (padsOpen) BuildPads();
        ShowInfo();
        BuildGuidePanel();
    }

    void UpdateFund()
    {
        bool over = projected > AscendEconomy.Balance + 0.01;
        var pad = data.GetPad(AscendEconomy.SelectedPad);
        fundLabel.text = $"<color=#9AA0A6>Fund</color> {AscendReport.Money(AscendEconomy.Balance)}   <color=#9AA0A6>To pay</color> " +
                         $"<color={(over ? "#F85149" : "#3FB950")}>{AscendReport.Money(projected)}</color>" +
                         $"   <color=#9AA0A6>Pad</color> {(pad != null ? pad.name : "<color=#F85149>none</color>")}";
    }

    void OnEconomy() { Changed(); BuildSidebar(); }

    void OnReview(AscendMail.Message m, CADReview rv) =>
        Status(rv.Approved ? "APPROVED! Next: open Ascend Mission Simulator to plan the trajectory. Details in PhMail."
                           : "Design rejected. Open PhMail to see what the board found.", rv.Approved ? Good : Bad);

    void ShowInfo()
    {
        CADPart p = picked; CADPlaced pl = null;
        if (p == null && sel >= 0 && sel < D.parts.Count) { pl = D.parts[sel]; p = data.Get(pl.partId); }
        infoTools.gameObject.SetActive(pl != null);
        if (p == null)
        {
            infoTitle.text = (mode == CADBuilder.Rocket ? "Build a rocket" : "Build a satellite") + "  <size=70%><color=#8AB4F8>" + AscendMissionGuide.Title + "</color></size>";
            var list = AscendMissionGuide.Checklist(mode, rocket, payload, data);
            int done = list.Count(x => x.ok);
            infoText.text = $"<b>MISSION GUIDE</b>  <color=#9AA0A6>{done}/{list.Count} done</color>\n" + AscendMissionGuide.ChecklistText(list, false) +
                "<color=#8AB4F8>Press Mission Guide (top bar) for ready-made suggestions that fit your money.</color>\n\n" + (mode == CADBuilder.Rocket
                ? "1. Pick an engine and place it.\n2. Stack tanks on top (same propellant!).\n3. Add an Interstage, then the upper stage.\n4. Avionics Bay + flight computer, IMU, battery, FTS, telemetry inside.\n5. Payload adapter, then a fairing on top.\n\n<color=#9AA0A6>Use Googel for research.</color>"
                : "1. Place a frame or bus.\n2. Put a separation system under it.\n3. Install EPS, battery, computer, radio and ADCS inside.\n4. Add solar panels, an antenna and a scientific instrument outside.\n\n<color=#9AA0A6>Keep the power budget positive!</color>");
            previewBox.SetActive(false);
            return;
        }
        infoTitle.text = (picked != null ? "<color=#F07A26>Placing:</color> " : "") + "<noparse>" + p.name + "</noparse>";
        infoText.text = AscendReport.PartInfo(p);
        bool show = p.attach != CADAttach.Internal;
        previewBox.SetActive(show);
        if (show) SetPreview(p, pl != null ? pl.color : p.defaultColor, pl?.labels);
        if (pl != null) BuildInsideList(sel, p);
    }

    void SetPreview(CADPart p, Color c, List<CADLabel> labels)
    {
        string k = p.id + ColorUtility.ToHtmlStringRGB(c) + (labels?.Count ?? 0);
        if (k == pkey && previewObj) return;
        pkey = k;
        if (previewObj) Object.Destroy(previewObj);
        previewObj = CADModels.Build(p, c, layer, false, data.labelFont ? data.labelFont : font, labels, data);
        previewObj.transform.SetParent(previewRoot, false);
        var rs = previewObj.GetComponentsInChildren<Renderer>();
        var b = new Bounds(previewRoot.position, Vector3.one * 0.1f);
        for (int i = 0; i < rs.Length; i++) { if (i == 0) b = rs[0].bounds; else b.Encapsulate(rs[i].bounds); rs[i].shadowCastingMode = ShadowCastingMode.Off; }
        pTarget = b.center;
        pdist = Mathf.Max(0.05f, b.extents.magnitude) / Mathf.Sin(pcam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
        pdirty = 2;
    }

    void BuildInsideList(int i, CADPart tp)
    {
        Clear(insideList);
        if (tp.internalVolumeL <= 0f) return;
        float used = 0f; var kids = new List<int>();
        for (int j = 0; j < D.parts.Count; j++)
            if (D.parts[j].parent == i && D.parts[j].link == CADLink.Internal) { kids.Add(j); used += data.Get(D.parts[j].partId)?.volumeL ?? 0f; }
        AUI.Text(insideList, $"Installed inside ({used:0.##}/{tp.internalVolumeL:0.##} L)", 14, Accent, FontStyles.Bold);
        if (kids.Count == 0) { AUI.Text(insideList, "Empty. Pick an internal part, then click this part.", 12.5f, Sub); return; }
        foreach (int j in kids)
        {
            int idx = j;
            var row = AUI.H(AUI.Rect("Item", insideList), 0, 0, 0, 0, 6);
            var t = AUI.Text(row, "<noparse>" + data.Get(D.parts[j].partId)?.name + "</noparse>", 12.5f, Txt);
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Truncate;
            AUI.LE(t, -1, 24, 1);
            AUI.Btn(row, "x", 12, Txt, Panel2, () => RemoveOne(idx), 28, 24);
        }
    }

    void Status(string msg, Color c) { if (status) { status.text = msg; status.color = c; } }

    // ───────────────────────────── Save / Load / Clear ─────────────────────────────
    string SaveKey => "AscendCAD.Design." + mode;

    void SaveDesign()
    {
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(D));
        PlayerPrefs.Save();
        Status($"Saved \"{D.name}\".", Good);
    }

    void LoadDesign()
    {
        if (!PlayerPrefs.HasKey(SaveKey)) { Status("No saved design yet.", Bad); return; }
        var d = JsonUtility.FromJson<CADDesign>(PlayerPrefs.GetString(SaveKey));
        if (d == null) return;
        var bad = new HashSet<int>();
        for (int i = 0; i < d.parts.Count; i++) if (data.Get(d.parts[i].partId) == null) d.CollectSubtree(i, bad);
        d.RemoveSet(bad);
        D.name = d.name; D.parts = d.parts; D.nextSymGroup = d.nextSymGroup;
        sel = -1; picked = null; DestroyGhosts();
        nameField.SetTextWithoutNotify(D.name);
        Sync(true); Frame(); Changed();
        Status($"Loaded \"{D.name}\".", Good);
    }

    void AskClear() => ShowModal("Clear this design?", "Every part in the " + (mode == CADBuilder.Rocket ? "rocket" : "satellite") +
        " will be removed. Parts you already paid for stay in your inventory.",
        ("Clear", true, () => { D.parts.Clear(); D.nextSymGroup = 1; sel = -1; Sync(true); Frame(); Changed(); CloseModal(); }),
        ("Cancel", true, CloseModal));

    // ───────────────────────────── Submit flow ─────────────────────────────
    void StartSubmit()
    {
        if (AscendMissionControl.Pending) { Status("A design is already being reviewed. Watch PhMail for the answer.", Bad); return; }
        if (rocket.IsEmpty) { Status("Build a rocket first (Rocket Builder tab).", Bad); return; }
        {
            var pad = data.GetPad(AscendEconomy.SelectedPad);
            var rsC = CADStats.Rocket(rocket, data, payload.IsEmpty ? 0f : CADStats.Payload(payload, data).wetMass);
            string why = pad == null || pad.isTransporter ? "No launch pad chosen."
                : !AscendEconomy.OwnsPad(pad, data) ? $"You don't own the {pad.name} yet."
                : AscendPads.Lock(pad, data) ?? (AscendPads.Problems(pad, rsC, data).Count > 0 ? $"This rocket doesn't fit the {pad.name}." : null);
            if (why != null) { OpenPads(); Status(why + " Choose a compatible launch pad.", Bad); return; }
        }
        var q = AscendEconomy.Quote(data, rocket, payload);
        bool ok = q.total <= AscendEconomy.Balance + 0.01;
        ShowModal("Receipt", AscendReport.Receipt(q, AscendEconomy.Balance, data),
            (q.total > 0 ? "Pay " + AscendReport.MoneyFull(q.total) : "Continue (all parts owned)", ok, () =>
            {
                if (q.total > 0 && !AscendEconomy.Pay(q)) { Status("Payment failed: not enough money.", Bad); return; }
                ShowDataSheet();
            }),
            ("Back to design", true, CloseModal));
    }

    void ShowDataSheet()
    {
        var pay = payload.IsEmpty ? null : payload;
        var rv = CADReview.Run(rocket, pay, data, out var rs, out var ps);
        string head = rv.Approved
            ? "<color=#3FB950><b>Preliminary check passed.</b></color> The review board will double-check everything.\n\n"
            : $"<color=#D29922><b>{rv.errors.Count} problem(s) found.</b></color> You can still send it, but the board will probably reject it.\n\n";
        ShowModal("Design data sheet", head + "<b>Checklist</b>\n" + AscendReport.Checks(rv) + "\n" + AscendReport.DataSheet(rocket, pay, rs, ps, data),
            ("Send to " + data.agencyEmail, true, () =>
            {
                AscendMissionControl.Submit(data, rocket, payload);
                CloseModal();
                Status($"Sent to {data.agencyEmail}. The review board will reply in PhMail shortly.", Blue);
            }),
            ("Back to design", true, CloseModal));
    }

    void ShowModal(string title, string body, params (string label, bool enabled, Action action)[] buttons)
    {
        CloseModal();
        HideGhosts();
        modal = AUI.Rect("Modal", root);
        AUI.Fill(modal);
        AUI.Img(modal, new Color(0f, 0f, 0f, 0.6f), false, true);
        var panel = AUI.Rect("Panel", modal);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        var size = root.rect.size;
        panel.sizeDelta = new Vector2(Mathf.Min(S(820f), size.x - 60f), Mathf.Min(S(620f), size.y - 60f));
        AUI.Img(panel, Panel, true, true);
        var t = AUI.Text(panel, title, 20, Txt, FontStyles.Bold);
        t.rectTransform.anchorMin = new Vector2(0f, 1f); t.rectTransform.anchorMax = Vector2.one; t.rectTransform.pivot = new Vector2(0.5f, 1f);
        t.rectTransform.sizeDelta = new Vector2(-40f, S(36f)); t.rectTransform.anchoredPosition = new Vector2(0f, -14f);
        var content = AUI.Scroll(panel, 20, S(40f) + 30f, 20, S(36f) + 22f);
        AUI.V(content, 0, 8, 0, 8, 0);
        AUI.Text(content, body, 14.5f, Txt).lineSpacing = 4f;
        var row = AUI.Rect("Buttons", panel);
        row.anchorMin = Vector2.zero; row.anchorMax = new Vector2(1f, 0f); row.pivot = new Vector2(0.5f, 0f);
        row.sizeDelta = new Vector2(-40f, S(40f)); row.anchoredPosition = new Vector2(0f, 14f);
        AUI.H(row, 0, 0, 0, 0, 8, TextAnchor.MiddleRight);
        // Few buttons keep their natural width on the right; many buttons share the row equally so they always fit
        bool share = buttons.Length > 3;
        bool first = true;
        foreach (var (label, enabled, action) in buttons)
        {
            var b = AUI.Btn(row, label, share ? 13 : 14, enabled ? Color.white : Sub, first ? Accent : Panel2, action,
                share ? -1 : Mathf.Max(120f, label.Length * 8.5f + 40f), 36);
            if (share) { var le = b.GetComponent<LayoutElement>(); le.minWidth = 0f; le.preferredWidth = 0f; le.flexibleWidth = 1f; }
            b.interactable = enabled;
            first = false;
        }
    }

    void CloseModal()
    {
        if (modal) Object.Destroy(modal.gameObject);
        modal = null;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  PHMAIL APP SCREEN
// ═════════════════════════════════════════════════════════════════════════════
public class AscendMailScreen
{
    static readonly Color Bg = AUI.Hex("#18191C"), Panel = AUI.Hex("#212226"), Panel2 = AUI.Hex("#2E2F35"), Txt = AUI.Hex("#E8EAED"),
        Sub = AUI.Hex("#9AA0A6"), Accent = AUI.Hex("#0B57D0"), RowBg = new Color(1f, 1f, 1f, 0.03f), RowSel = new Color(0.04f, 0.34f, 0.82f, 0.35f);

    readonly AscendCADData data;
    readonly RectTransform root;
    RectTransform list;
    TextMeshProUGUI header, body;
    Button inboxBtn, sentBtn;
    AscendMail.Message current;
    bool showSent, dirty = true;

    public AscendMailScreen(AscendCADData asset, TMP_FontAsset font, RectTransform window)
    {
        data = AscendCADData.Resolve(asset);
        root = window;
        AUI.Font = font; AUI.FontScale = data.uiFontScale;
        AscendEconomy.Init(data);
        AscendMail.EnsureWelcome(data);
        Build();
        var hook = root.gameObject.AddComponent<AscendHook>();
        hook.onUpdate = () => { if (dirty) Rebuild(); };   // rebuilds only after mail changes, only while visible
        hook.onDestroy = () => AscendMail.Changed -= MarkDirty;
        AscendMail.Changed += MarkDirty;
    }

    void MarkDirty() => dirty = true;

    void Build()
    {
        AUI.Img(root, Bg);
        var top = AUI.Rect("TopBar", root);
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one; top.pivot = new Vector2(0.5f, 1f);
        top.sizeDelta = new Vector2(0f, 48f); top.anchoredPosition = Vector2.zero;
        AUI.Img(top, Panel);
        AUI.H(top, 14, 12, 7, 7, 8);
        var logo = AUI.Text(top, "<color=#4285F4>Ph</color>Mail", 21, Txt, FontStyles.Bold);
        logo.textWrappingMode = TextWrappingModes.NoWrap; AUI.LE(logo, 90);
        inboxBtn = AUI.Btn(top, "Inbox", 14, Txt, Panel2, () => { showSent = false; current = null; Rebuild(); }, 110, 34);
        sentBtn = AUI.Btn(top, "Sent", 14, Txt, Panel2, () => { showSent = true; current = null; Rebuild(); }, 90, 34);
        AUI.LE(AUI.Rect("Spacer", top), -1, -1, 1);
        var acct = AUI.Text(top, data.playerEmail, 13, Sub);
        acct.textWrappingMode = TextWrappingModes.NoWrap; acct.alignment = TextAlignmentOptions.MidlineRight;
        AUI.LE(acct, 260);

        var left = AUI.Rect("List", root);
        left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(0f, 1f); left.pivot = new Vector2(0f, 0.5f);
        left.offsetMin = Vector2.zero; left.offsetMax = new Vector2(330f, -48f);
        AUI.Img(left, Panel);
        list = AUI.Scroll(left, 0, 0, 0, 0);
        AUI.V(list, 8, 8, 8, 8, 4);

        var right = AUI.Rect("Reader", root);
        AUI.Fill(right, 330, 0, 0, 48);
        var rc = AUI.Scroll(right, 0, 0, 0, 0);
        AUI.V(rc, 28, 28, 22, 28, 12);
        header = AUI.Text(rc, "", 15, Txt);
        body = AUI.Text(rc, "", 15, Txt);
        body.lineSpacing = 6f;
    }

    void Rebuild()
    {
        dirty = false;
        ((Image)inboxBtn.targetGraphic).color = showSent ? Panel2 : Accent;
        ((Image)sentBtn.targetGraphic).color = showSent ? Accent : Panel2;
        int unread = AscendMail.Unread;
        inboxBtn.GetComponentInChildren<TextMeshProUGUI>().text = unread > 0 ? $"Inbox ({unread})" : "Inbox";

        for (int i = list.childCount - 1; i >= 0; i--) { var c = list.GetChild(i).gameObject; c.SetActive(false); Object.Destroy(c); }
        var msgs = AscendMail.All.Where(m => m.outgoing == showSent).Reverse().ToList();
        if (msgs.Count == 0) AUI.Text(list, showSent ? "Nothing sent yet." : "No messages.", 14, Sub);
        foreach (var m in msgs)
        {
            var msg = m;
            var b = AUI.Btn(list, "", 13, Txt, m == current ? RowSel : RowBg, () => Open(msg), -1, 62);
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            t.alignment = TextAlignmentOptions.MidlineLeft; t.margin = new Vector4(8, 0, 4, 0);
            t.fontStyle = m.read ? FontStyles.Normal : FontStyles.Bold;
            string who = m.outgoing ? "To: " + m.to : m.from;
            string tag = m.outgoing ? "" : m.subject.StartsWith("APPROVED") ? "<color=#3FB950>• </color>" : m.subject.StartsWith("REJECTED") ? "<color=#F85149>• </color>" : "";
            t.text = $"{(m.read ? "" : "<color=#8AB4F8>• </color>")}<noparse>{who}</noparse>\n{tag}<size=92%><noparse>{m.subject}</noparse></size>\n<size=80%><color=#9AA0A6>{m.time}</color></size>";
        }

        if (current == null)
        {
            header.text = "<color=#9AA0A6>Select a message to read it.</color>";
            body.text = "";
        }
    }

    void Open(AscendMail.Message m)
    {
        current = m;
        header.text = $"<size=150%><b><noparse>{m.subject}</noparse></b></size>\n" +
                      $"<color=#9AA0A6>From:</color> {m.from}\n<color=#9AA0A6>To:</color> {m.to}\n<color=#9AA0A6>{m.time}</color>";
        body.text = m.body;
        AscendMail.MarkRead(m);   // raises Changed -> list refresh
        dirty = true;
    }
}
