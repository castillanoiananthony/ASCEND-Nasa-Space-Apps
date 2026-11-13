using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  Googel Data ASSET. Create one with:  Project window > right-click >
//  Create > Googel > Googel Data.  It comes pre-filled with all the rocket parts.
//  Edit text and drag an image onto each part right in the Inspector,
//  then drag the asset into ComputerInteraction > Googel > Data.
// ─────────────────────────────────────────────────────────────────────────────
[CreateAssetMenu(menuName = "Googel/Googel Data", fileName = "GoogelData")]
public class GoogelData : ScriptableObject
{
    public List<GoogelCategory> categories = new List<GoogelCategory>();

    // Runs when the asset is first created (and on Reset): fill with the default parts
    void Reset() => categories = GoogelDefaults.Build();

    [ContextMenu("Log parts missing images")]
    void LogMissingImages()
    {
        var sb = new System.Text.StringBuilder();
        int missing = 0;
        foreach (var c in categories)
            foreach (var p in c.parts)
                if (!p.image) { missing++; sb.AppendLine("   " + p.title + "      (" + c.name + ")"); }
        Debug.Log(missing == 0 ? "Googel: every part has an image!" : "Googel: " + missing + " parts still need an image:\n" + sb);
    }

    [ContextMenu("Load default rocket parts (overwrites!)")]
    void LoadDefaults()
    {
        categories = GoogelDefaults.Build();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}

[Serializable]
public class GoogelCategory
{
    public string name = "New Category";
    [TextArea(1, 3)] public string description;
    public List<GoogelPart> parts = new List<GoogelPart>();
}

[Serializable]
public class GoogelPart
{
    public string title = "New Part";
    [Tooltip("The picture shown on the card and the Read more page.")]
    public Sprite image;
    [Tooltip("Shown on the card, before 'Read more'. Keep it to 1-2 sentences.")]
    [TextArea(2, 4)] public string shortDescription;
    [TextArea(3, 8)] public string purpose;
    [Tooltip("One bullet per line. 'Label: value' makes the label bold.")]
    [TextArea(3, 10)] public string specs;
    [TextArea(2, 6)] public string cost;
    [TextArea(3, 8)] public string history;

    [NonSerialized] public string category; // filled in at runtime
    public string Id => category + "/" + title;
}

// Built-in rocket part content. Used to fill a new Googel Data asset,
// and as a backup if no asset is assigned. You normally edit the ASSET, not this file.
public static class GoogelDefaults
{
    static List<GoogelCategory> list;
    static GoogelCategory current;

    static void Cat(string name, string description)
    {
        current = new GoogelCategory { name = name, description = description };
        list.Add(current);
    }

    static void Part(string title, string shortDesc, string purpose, string specs, string cost, string history)
    {
        current.parts.Add(new GoogelPart
        {
            title = title, shortDescription = shortDesc, purpose = purpose,
            specs = specs.Replace("|", "\n"), cost = cost, history = history
        });
    }

    public static List<GoogelCategory> Build()
    {
        list = new List<GoogelCategory>();

        // ═════════════════════════════ 1. MAIN ENGINES ═════════════════════════════
        Cat("Main Engines", "The primary propulsion system that generates thrust to lift and accelerate the rocket.");

        Part("Merlin 1D",
            "SpaceX's workhorse kerosene engine. Nine of them power every Falcon 9 first stage.",
            "Provides first-stage thrust for Falcon 9 and Falcon Heavy. It can throttle down and restart in flight, which lets boosters perform the boostback, entry and landing burns needed for reuse.",
            "Maker: SpaceX|Propellants: RP-1 kerosene / liquid oxygen|Cycle: Gas generator|Thrust: ~845 kN (190,000 lbf) at sea level|Specific impulse: ~282 s sea level, ~311 s vacuum|Injector: Pintle|Ignition: TEA-TEB (a fluid that ignites on contact with air)",
            "Not officially published. Industry estimates put it around $1 million per engine, which is very cheap for its class because SpaceX builds them in-house in large numbers and reflies them.",
            "The Merlin family first flew on Falcon 1 in 2006. The 1D version debuted on Falcon 9 v1.1 in 2013 and kept getting upgraded; the Block 5 version (2018) is built to fly many times with little refurbishment.");

        Part("Merlin Vacuum",
            "A Merlin tuned for space, with a huge nozzle. It drives the Falcon 9 second stage to orbit.",
            "Powers the second stage of Falcon 9 and Falcon Heavy after stage separation. Its oversized nozzle squeezes more energy out of the exhaust in vacuum, and it can restart several times to reach different orbits.",
            "Maker: SpaceX|Propellants: RP-1 / liquid oxygen|Cycle: Gas generator|Thrust: ~981 kN (220,500 lbf) in vacuum|Specific impulse: ~348 s in vacuum|Nozzle: Radiatively cooled niobium alloy extension, ~165:1 expansion ratio",
            "Not published. Because the second stage is not recovered, a new Merlin Vacuum is used up on every flight, making it one of the main hardware costs of each Falcon 9 launch.",
            "Merlin Vacuum variants have flown since Falcon 9's first launch in 2010. The 1D Vacuum arrived with Falcon 9 v1.1 in 2013, and SpaceX later added a short-nozzle version for missions that don't need maximum performance.");

        Part("RS-25",
            "The Space Shuttle's reusable hydrogen engine, now flying again on NASA's SLS moon rocket.",
            "Burns liquid hydrogen and liquid oxygen at very high efficiency, powering a core stage from liftoff almost all the way to orbit. Four RS-25s sit at the base of the SLS core stage.",
            "Maker: Aerojet Rocketdyne (now L3Harris)|Propellants: Liquid hydrogen / liquid oxygen|Cycle: Fuel-rich staged combustion|Thrust: ~1,860 kN sea level, ~2,280 kN vacuum|Specific impulse: ~366 s sea level, ~452 s vacuum|Chamber pressure: ~20 MPa (about 200 atmospheres)",
            "One of the most expensive rocket engines ever built. NASA's contract for new expendable RS-25s for SLS works out to roughly $100 million per engine.",
            "First flew on STS-1 in 1981 as the Space Shuttle Main Engine, with three on every Shuttle until 2011. Leftover Shuttle engines were upgraded for SLS, which launched Artemis I in 2022.");

        Part("RS-68",
            "The biggest hydrogen-fueled engine ever flown, built cheap and simple for the Delta IV.",
            "Powered the Delta IV Common Booster Core. It traded some efficiency for a simpler, cheaper design than the RS-25, using an ablative nozzle that slowly wears away instead of complex cooling tubes.",
            "Maker: Rocketdyne (later Aerojet Rocketdyne)|Propellants: Liquid hydrogen / liquid oxygen|Cycle: Gas generator|Thrust: ~3,140 kN at sea level (RS-68A)|Specific impulse: ~360 s sea level, ~410 s vacuum|Nozzle: Ablatively cooled (the dark color you see in photos)",
            "Exact prices were never published, but it was designed with far fewer parts than the RS-25 and cost only a fraction as much.",
            "First flew on Delta IV in 2002, with the upgraded RS-68A following in 2012. Its fiery liftoffs, where leftover hydrogen scorched the booster's orange insulation, became a trademark until the final Delta IV Heavy launch in April 2024.");

        Part("F-1",
            "The Saturn V's monster engine. Five of them launched the Apollo astronauts toward the Moon.",
            "Powered the S-IC first stage of the Saturn V, lifting the nearly 3,000-tonne rocket off the pad. It is still the most powerful single-chamber liquid-fueled engine ever flown.",
            "Maker: Rocketdyne|Propellants: RP-1 / liquid oxygen|Cycle: Gas generator|Thrust: ~6,770 kN (1.52 million lbf) at sea level|Specific impulse: ~263 s at sea level|Propellant use: about 2.6 tonnes per second, per engine",
            "Per-engine cost in 1960s dollars isn't well documented. A complete Saturn V launch cost roughly $185 million at the time, which is well over $1 billion in today's money.",
            "Development began in the late 1950s. Violent combustion instability nearly stopped the program until engineers set off small bombs inside test chambers to tune the injector. It first flew on Apollo 4 in 1967 and flew on all 13 Saturn V launches.");

        Part("Rutherford",
            "Rocket Lab's small 3D-printed engine whose fuel pumps are spun by electric motors and batteries.",
            "Powers both stages of the Electron small-satellite rocket: nine engines on the first stage and one vacuum-optimized engine on the second.",
            "Maker: Rocket Lab|Propellants: RP-1 / liquid oxygen|Cycle: Electric-pump-fed (battery-powered motors drive the pumps)|Thrust: ~24 kN per engine at sea level|Specific impulse: ~311 s sea level, ~343 s vacuum version|Manufacturing: Main components 3D printed",
            "Engine price isn't published. A whole Electron launch has been advertised at around $7.5 million, so each engine is a small slice of that.",
            "Unveiled in 2014 and first flew on Electron's debut in 2017. It was the first orbital-class engine to use an electric-pump cycle.");

        Part("Raptor",
            "SpaceX's methane-burning full-flow engine that powers Starship and the Super Heavy booster.",
            "Drives both stages of Starship. Methane burns cleaner than kerosene (less soot, which helps reuse) and could in theory be produced on Mars from carbon dioxide and water.",
            "Maker: SpaceX|Propellants: Liquid methane / liquid oxygen|Cycle: Full-flow staged combustion|Thrust: ~2,260 kN (Raptor 2) to ~2,750 kN (Raptor 3) at sea level|Chamber pressure: ~30-35 MPa, among the highest ever|Super Heavy uses: 33 engines|Starship uses: 6 engines (3 sea-level, 3 vacuum)",
            "Not published. SpaceX has said its goal is to bring each engine well under $1 million through mass production.",
            "Development started around 2012. Raptor first flew in 2019 on the Starhopper test vehicle, Raptor 2 powered the Starship flight tests starting in 2023, and the simplified Raptor 3 was revealed in 2024.");

        Part("BE-4",
            "Blue Origin's large methane engine, powering both ULA's Vulcan and Blue Origin's New Glenn.",
            "Provides first-stage thrust: two engines on ULA's Vulcan Centaur and seven on the New Glenn booster, which is designed to land and fly again.",
            "Maker: Blue Origin|Propellants: Liquid methane (LNG) / liquid oxygen|Cycle: Oxygen-rich staged combustion|Thrust: ~2,400 kN (550,000 lbf) at sea level|Throttling: Deep throttling for booster landings (New Glenn)",
            "Not published. ULA picked it partly to replace the Russian-made RD-180 engine it had been buying for the Atlas V.",
            "Announced in 2014. After a long development, it first flew on Vulcan's debut in January 2024 and on New Glenn's first launch in January 2025.");

        Part("RL10",
            "A small, super-efficient hydrogen engine that has powered American upper stages since 1963.",
            "Powers upper stages such as Centaur, where efficiency matters more than raw thrust. Its expander cycle uses heat soaked up by the engine walls to drive the pumps, so no separate gas generator is needed.",
            "Maker: Originally Pratt & Whitney, now L3Harris (formerly Aerojet Rocketdyne)|Propellants: Liquid hydrogen / liquid oxygen|Cycle: Expander|Thrust: ~100-110 kN depending on version|Specific impulse: Up to ~465 s in vacuum|Restarts: Many restarts per flight",
            "Often reported in the $10-20+ million range per engine, partly because it is still built in small numbers with a lot of hand work.",
            "First flew in 1963 on Centaur, making it the first liquid-hydrogen engine to fly. Versions have flown on Atlas, Titan, Delta and SLS upper stages, and RL10-powered Centaurs sent probes like Voyager and New Horizons on their way.");

        Part("Vulcain",
            "Europe's hydrogen main engine, the heart of the Ariane 5 and Ariane 6 core stages.",
            "Ignites on the launch pad and burns for around eight to nine minutes, powering the core stage from liftoff to near orbit while solid boosters add extra thrust early on.",
            "Maker: ArianeGroup (Europe)|Propellants: Liquid hydrogen / liquid oxygen|Cycle: Gas generator|Thrust: ~1,370 kN in vacuum (Vulcain 2 / 2.1)|Specific impulse: ~430+ s in vacuum|Burn time: ~8-9 minutes",
            "Not published. Vulcain 2.1 for Ariane 6 was simplified (including a 3D-printed gas generator) specifically to cut production cost.",
            "The first Vulcain flew on Ariane 5's maiden launch in 1996, which failed because of a software error, not the engine. Vulcain 2 arrived in 2002, and Vulcain 2.1 flew on Ariane 6's first launch in July 2024.");

        Part("YF-100",
            "China's high-pressure kerosene engine, used across the modern Long March rocket family.",
            "Provides thrust for the Long March 5 boosters and the Long March 6 and Long March 7 first stages.",
            "Maker: Academy of Aerospace Liquid Propulsion Technology (China)|Propellants: RP-1 kerosene / liquid oxygen|Cycle: Oxygen-rich staged combustion|Thrust: ~1,200 kN sea level, ~1,340 kN vacuum|Specific impulse: ~300 s sea level, ~335 s vacuum",
            "Not published.",
            "Developed in the 2000s to replace older engines that burned toxic hypergolic propellants. It first flew in 2015 on Long March 6, followed by Long March 7 and Long March 5 in 2016.");

        // ═════════════════════════════ 2. SOLID ROCKET BOOSTERS ═════════════════════════════
        Cat("Solid Rocket Boosters", "Strap-on motors packed with solid propellant that add huge thrust during the first minutes of flight.");

        Part("Space Shuttle SRB",
            "The twin white boosters that gave the Space Shuttle most of its thrust at liftoff.",
            "Each booster burned for about two minutes, then separated, parachuted into the Atlantic, and was towed back to be refurbished and reused.",
            "Maker: Thiokol (later ATK)|Propellant: Ammonium perchlorate composite (PBAN binder)|Thrust: ~12,500 kN (2.8 million lbf) each at liftoff|Length: ~45.6 m|Diameter: ~3.7 m|Burn time: ~124 s|Segments: 4 propellant segments",
            "Recovering, refurbishing and refilling a set of boosters still cost tens of millions of dollars per flight, so reuse saved hardware but not as much money as originally hoped.",
            "Flew on all 135 Shuttle missions from 1981 to 2011. A failed O-ring seal in a booster joint caused the Challenger disaster in 1986, which led to a full redesign. Five-segment descendants now power NASA's SLS.");

        Part("GEM-40",
            "A graphite-epoxy strap-on booster that gave the Delta II extra kick at liftoff.",
            "Strapped around the Delta II first stage in sets of three, four or nine, letting the rocket's power be matched to the weight of each payload.",
            "Maker: Alliant Techsystems (now Northrop Grumman)|Case: Graphite-epoxy composite (GEM = Graphite Epoxy Motor)|Diameter: ~1.0 m|Thrust: ~500 kN each|Burn time: ~64 s",
            "Not public. Strap-on solids like this are popular because they are a cheap way to add a lot of thrust.",
            "Introduced on Delta II in 1990 and flew until Delta II's final launch in 2018. In the nine-booster setup, six lit on the ground and three lit in the air.");

        Part("GEM-60",
            "A bigger graphite-epoxy booster made for the Delta IV Medium+ rockets.",
            "Added liftoff thrust to Delta IV Medium+ versions, which used two or four boosters depending on how heavy the payload was.",
            "Maker: Alliant Techsystems (now Northrop Grumman)|Diameter: ~1.5 m|Thrust: ~830 kN each|Burn time: ~90 s|Nozzle: Fixed and steerable versions",
            "Not public.",
            "First flew on Delta IV in 2002 and was retired along with the Delta IV Medium in 2019.");

        Part("GEM-63",
            "Northrop Grumman's booster for the Atlas V, with a stretched XL version for Vulcan.",
            "Strapped to the Atlas V (up to five boosters) to lift heavier payloads; the longer GEM 63XL does the same job on ULA's Vulcan Centaur.",
            "Maker: Northrop Grumman|Diameter: ~1.6 m|Length: ~20 m|Thrust: ~1,660 kN each|Burn time: ~94 s|Case: Carbon-fiber composite",
            "Not public. It replaced the Aerojet AJ-60A booster on Atlas V, partly to bring costs down.",
            "First flew on Atlas V in November 2020. The GEM 63XL debuted on Vulcan's first flight in 2024.");

        Part("P241 / EAP",
            "The giant solid boosters strapped to each side of Europe's Ariane 5.",
            "Produced over 90% of Ariane 5's thrust at liftoff, burning for about two minutes before separating and falling into the Atlantic.",
            "Maker: Europropulsion (Avio / ArianeGroup)|Propellant: ~240 tonnes of solid propellant each|Thrust: ~7,000 kN each at peak|Length: ~31 m|Diameter: ~3 m|Burn time: ~130 s",
            "Not public.",
            "Flew on every Ariane 5 from 1996 until its final launch in 2023. EAP stands for 'Etage d'Acceleration a Poudre' (solid booster stage). Ariane 6 replaced them with the P120C booster.");

        Part("Castor 30",
            "A compact solid motor used as the second stage of Northrop Grumman's Antares rocket.",
            "Fires after first-stage separation to push Antares, and the Cygnus cargo ships it carries to the ISS, the rest of the way to orbit.",
            "Maker: Alliant Techsystems (now Northrop Grumman)|Diameter: ~2.3 m|Variants: 30A, 30B, 30XL|Thrust: Roughly 260-470 kN in vacuum, depending on variant|Derived from: The Castor 120 motor",
            "Not public.",
            "First flew on Antares in 2013. The stretched Castor 30XL entered service in 2016 to carry heavier Cygnus spacecraft.");

        // ═════════════════════════════ 3. FUEL & PROPELLANT TANKS ═════════════════════════════
        Cat("Fuel & Propellant Tanks", "Lightweight containers that store fuel and oxidizer, often at extremely cold temperatures.");

        Part("RP-1 Kerosene Tank",
            "Holds highly refined kerosene, a dense and easy-to-store rocket fuel.",
            "Stores RP-1 and feeds it to the engines. Because RP-1 stays liquid at room temperature, the tank needs little insulation compared to cryogenic tanks.",
            "Typical material: Aluminum-lithium or aluminum alloy|Fuel temperature: Room temperature (sometimes chilled to make it denser)|Fuel density: ~0.81 g/cm3|Used on: Falcon 9, Soyuz, Saturn V first stage",
            "Part of the stage's structure. Large tank sections cost from hundreds of thousands to millions of dollars, while the RP-1 itself costs only a few dollars per kilogram or less.",
            "RP-1 ('Rocket Propellant-1') was standardized in the US in the 1950s because ordinary jet fuel had too many impurities that gummed up rocket engines.");

        Part("Liquid Oxygen (LOX) Tank",
            "Stores the oxidizer most rockets burn, chilled to about -183 C.",
            "Holds the liquid oxygen that combines with fuel to make combustion possible in the vacuum of space. LOX is usually the heaviest part of a rocket's propellant load.",
            "Temperature: -183 C (boiling point of oxygen)|Density: ~1.14 g/cm3|Material: Aluminum or aluminum-lithium alloy|Features: Vents for boil-off gas, anti-slosh baffles",
            "Part of the stage cost. Liquid oxygen itself is cheap, often well under a dollar per kilogram.",
            "Used since Robert Goddard's first liquid-fueled rocket in 1926 and the German V-2. It is still the most common rocket oxidizer today.");

        Part("Liquid Hydrogen (LH2) Tank",
            "A very large, heavily insulated tank for ultra-cold, ultra-light liquid hydrogen.",
            "Stores liquid hydrogen, the most efficient chemical rocket fuel. Because hydrogen is so light, its tank has to be huge for the amount of fuel it holds.",
            "Temperature: -253 C|Density: ~0.07 g/cm3 (about 1/14 of water)|Insulation: Spray-on foam or multilayer blankets|Challenge: Hydrogen leaks easily and can make some metals brittle",
            "Usually one of the more expensive tanks to build because of the insulation and the extremely leak-tight construction it needs.",
            "Pioneered on Centaur and the Saturn V upper stages in the 1960s. The hydrogen section of the Shuttle's External Tank was famously covered in orange foam insulation.");

        Part("Liquid Methane (LCH4) Tank",
            "Stores liquid methane, the fuel of the newest generation of reusable rockets.",
            "Holds methane for engines like Raptor and BE-4. Methane is denser than hydrogen and burns cleaner than kerosene.",
            "Temperature: About -162 C|Density: ~0.42 g/cm3|Material: Stainless steel (Starship) or aluminum alloys|Advantage: Similar temperature to LOX, which makes shared tank walls easier",
            "Methane itself is cheap, similar to natural gas. Starship's stainless steel tanks were chosen partly because steel costs far less than aerospace aluminum or carbon fiber.",
            "Methane engines were studied for decades but first reached orbit in 2023 on China's Zhuque-2 rocket, followed by Vulcan in 2024.");

        Part("Hypergolic Propellant Tank",
            "Holds fuels and oxidizers that ignite instantly on contact, with no igniter needed.",
            "Stores propellants such as hydrazine and nitrogen tetroxide for thrusters and engines that must start reliably every time, even after years in space.",
            "Common pairs: Hydrazine, MMH or UDMH + nitrogen tetroxide (N2O4)|Storage: Room temperature, for years|Material: Titanium is common|Hazard: Highly toxic and corrosive",
            "Tanks are often precision titanium parts costing hundreds of thousands of dollars or more, and handling the toxic propellants safely adds a lot of cost too.",
            "Used on the Apollo service and lunar modules, the Shuttle's maneuvering engines, Titan rockets, and the Draco thrusters on SpaceX's Dragon.");

        Part("Solid Propellant Casing",
            "The pressure-tight shell that holds a solid rocket's fuel and acts as its combustion chamber.",
            "Contains the solid propellant and must survive high internal pressure and heat as the propellant burns from the inside out.",
            "Materials: High-strength steel or carbon/graphite-epoxy composite|Inner layers: Insulation and liner to protect the case|Operating pressure: Several MPa",
            "Varies widely. Large steel cases, like the Shuttle SRB segments, were expensive but could be recovered and reused many times.",
            "Composite cases, like those on the GEM boosters, became popular from the 1980s and 1990s to save weight. Shuttle SRB steel cases were reused across many flights.");

        Part("Pressurant Gas Tank",
            "Holds high-pressure gas, usually helium, that pushes propellant toward the engines.",
            "Keeps the propellant tanks pressurized as they empty, so the pumps get a steady flow and thin tank walls stay stiff.",
            "Common gas: Helium (sometimes nitrogen)|Pressure: Often 20-40 MPa|Construction: Usually a COPV|Note: Some rockets, like Starship, use heated propellant gas instead of helium",
            "Helium is expensive and in limited supply worldwide, which is one reason newer rockets try to avoid needing it.",
            "Helium pressurization has been standard since the 1950s and 60s because helium stays a gas even at extremely low temperatures.");

        Part("Composite Overwrapped Pressure Vessel (COPV)",
            "An ultra-light high-pressure tank: a thin metal liner wrapped in carbon fiber.",
            "Stores gases like helium at very high pressure while weighing much less than an all-metal tank of the same strength.",
            "Liner: Aluminum or titanium|Overwrap: Carbon fiber in epoxy|Pressure: Up to ~40+ MPa|Weight: Far lighter than equivalent metal tanks",
            "From a few thousand to hundreds of thousands of dollars each, depending on size and how much testing is needed to qualify it for flight.",
            "Widely used since the Space Shuttle era. A COPV failure inside a liquid oxygen tank was blamed for the 2016 Falcon 9 explosion that destroyed the AMOS-6 satellite.");

        // ═════════════════════════════ 4. ROCKET BODY & STRUCTURE ═════════════════════════════
        Cat("Rocket Body & Structure", "The skeleton and skin of the rocket that hold everything together and survive the forces of flight.");

        Part("First-Stage Tank",
            "The biggest part of the rocket, holding propellant for the first few minutes of flight.",
            "Stores the fuel and oxidizer for the booster engines while also acting as the main load-carrying structure of the rocket.",
            "Typical layout: Separate fuel and oxidizer tanks|Material: Aluminum-lithium, aluminum or stainless steel|Example: Falcon 9's first stage is over 40 m tall and 3.7 m wide",
            "A major share of a rocket's price. Reusing it, as SpaceX does with Falcon 9, saves millions of dollars per flight.",
            "Early rockets carried heavy separate tanks inside a body. Modern rockets use the tank walls themselves as the airframe to save weight.");

        Part("Second-Stage Tank",
            "Smaller tanks that feed the upper-stage engine once the booster drops away.",
            "Provides the propellant the upper stage needs to reach orbital speed and place the payload into the correct orbit.",
            "Size: Usually much smaller than the first stage|Common bulkhead: Often one shared wall between fuel and oxidizer to save weight|Insulation: Needed for long coasts in space",
            "Usually thrown away after each flight, so it is a big part of the cost of every launch.",
            "Centaur pioneered ultra-thin 'balloon' tanks that hold their shape using internal pressure. Many modern upper stages use common-bulkhead designs.");

        Part("Main Rocket Fuselage",
            "The outer body of the rocket that ties all the sections together and handles flight loads.",
            "Carries thrust, drag and bending loads, and protects internal systems like wiring and plumbing.",
            "Construction: Monocoque shells or isogrid (triangle-pattern) panels|Materials: Aluminum alloys, composites, stainless steel|Loads: Thrust, drag, vibration, wind gusts",
            "Depends on size and material; usually counted as part of each stage's manufacturing cost.",
            "Isogrid panels, developed with NASA support around the 1970s, became popular because they make walls that are both strong and light.");

        Part("Nose Cone",
            "The pointed tip that slices through the air and reduces drag.",
            "Reduces drag and aerodynamic heating at the front of the vehicle. On most orbital rockets, the payload fairing itself forms the nose cone.",
            "Common shapes: Ogive, conical, von Karman|Materials: Composites or aluminum|Trade-off: Pointier shapes cut drag but hold less volume",
            "Small nose cones are cheap; on large rockets the cost is part of the payload fairing.",
            "Nose-cone shapes were studied heavily for supersonic missiles in the 1940s and 50s; the low-drag von Karman ogive is still a favorite.");

        Part("Interstage Adapter",
            "The connector between the first and second stages that holds the separation system.",
            "Transfers loads between the stages, protects the upper-stage engine nozzle during climb, and then lets the stages separate cleanly.",
            "Material: Carbon-fiber composite or aluminum|Contains: Separation pushers or pneumatic systems|Falcon 9: The interstage stays on the booster and carries the grid fins",
            "Usually counted as part of stage cost.",
            "Soviet rockets often used 'hot staging', firing the upper engine through an open lattice interstage. SpaceX brought hot staging back for Starship in 2023.");

        Part("Structural Ring",
            "Circular frames that stiffen the rocket body and join sections together.",
            "Keeps the cylinder perfectly round, spreads out concentrated loads (like booster attachment points), and connects tank sections.",
            "Materials: Machined aluminum, steel or titanium|Locations: Tank joints, booster attach points, stage interfaces|Also called: Frames or rings",
            "Individually small, but precision-machined rings can be expensive.",
            "Ring-and-stringer construction was borrowed from aircraft design and has been used in rockets since the 1940s.");

        Part("Engine Mount",
            "The thrust structure that holds the engines and passes their push into the rocket.",
            "Carries the full engine thrust into the stage, supports the actuators that steer the engines, and routes propellant lines.",
            "Falcon 9: 'Octaweb' layout (8 engines in a circle around 1 center engine)|Materials: Aluminum, titanium, composites|Loads: Several meganewtons",
            "Part of the stage cost; complex because of the plumbing, heat and huge forces involved.",
            "Falcon 9 switched from a 3x3 engine grid to the Octaweb in 2013 to simplify manufacturing and better survive an engine failure.");

        Part("Payload Fairing",
            "The protective clamshell that shields the payload from air, heat and noise during launch.",
            "Protects satellites from aerodynamic forces, heating and acoustic vibration, then splits apart and falls away once the rocket is above most of the atmosphere.",
            "Falcon 9 fairing: ~13 m tall, 5.2 m wide|Material: Carbon fiber over aluminum honeycomb|Separation: Pneumatic or pyrotechnic pushers",
            "Falcon 9's fairing has been reported to cost around $6 million, which is why SpaceX recovers and reuses fairing halves.",
            "Fairings have been used since the earliest satellite launches. SpaceX began regularly recovering and reflying its fairings in 2019.");

        Part("Grid Fins",
            "Lattice-shaped fins that steer a returning booster through the atmosphere.",
            "Guide a falling booster during reentry and landing. Their waffle-like shape gives strong control at supersonic speeds in a compact size.",
            "Falcon 9: 4 grid fins (titanium on Block 5)|Actuation: Hydraulic|Advantage: High control power for their size",
            "Titanium grid fins are expensive to make but are reused flight after flight.",
            "Grid fins were used on Soviet missiles and the Soyuz launch escape system. SpaceX first flew them in early 2015 and upgraded to titanium in 2017.");

        Part("Landing Legs",
            "Deployable legs that let a booster land upright on a pad or drone ship.",
            "Unfold just before touchdown to soak up the landing impact and keep the booster standing upright.",
            "Falcon 9: 4 carbon-fiber legs with aluminum honeycomb|Span: ~18 m when deployed|Deployment: Pneumatic",
            "Not public. The legs are a small cost compared with the savings from reusing the whole booster.",
            "Falcon 9 made the first landing of an orbital-class booster in December 2015. Before that, legs were mostly used on landers like Apollo's lunar module.");

        // ═════════════════════════════ 5. UPPER-STAGE COMPONENTS ═════════════════════════════
        Cat("Upper-Stage Components", "Hardware that takes over after the booster drops away and delivers the payload to its final orbit.");

        Part("Upper-Stage Engine",
            "An engine built for space, with a large nozzle and the ability to restart.",
            "Pushes the upper stage and payload up to orbital speed, often in several separate burns to reach the target orbit.",
            "Examples: Merlin Vacuum, RL10, Vinci, Raptor Vacuum|Features: Large expansion nozzle, restart capability|Efficiency: Higher specific impulse in vacuum",
            "Usually used up on every flight, making it a major part of launch cost.",
            "Upper-stage engines like the J-2 (Saturn V) and RL10 made multi-burn missions to the Moon and planets possible.");

        Part("Kick Stage",
            "A small extra stage that gives the final precise push to place satellites in their exact orbits.",
            "Performs final orbit changes or circularizes the orbit after the main upper stage shuts down, often dropping off several satellites in different spots.",
            "Example: Rocket Lab's Electron Kick Stage (Curie engine)|Propellant: Usually storable liquids or solids|Ability: Multiple burns",
            "Usually included in the launch price; small compared to the main stages.",
            "Rocket Lab introduced its kick stage on Electron in 2018.");

        Part("Orbital Transfer Vehicle",
            "A space tug that moves satellites between orbits after launch.",
            "Lets rideshare satellites reach custom orbits that the main rocket isn't going to, saving each satellite from carrying a big engine of its own.",
            "Examples: D-Orbit ION, Impulse Space Mira|Propulsion: Chemical or electric|Payload: Small to medium satellites",
            "Varies; operators charge per kilogram or per satellite on top of the launch price.",
            "Commercial transfer vehicles took off in the 2020s alongside SpaceX's rideshare missions; D-Orbit's ION first flew in 2020.");

        Part("Payload Adapter",
            "The cone-shaped interface that bolts the satellite to the top of the rocket.",
            "Connects the payload to the upper stage, carries launch loads into the satellite, and holds the separation system.",
            "Standard sizes: 937 mm, 1194 mm, 1666 mm interfaces|Material: Aluminum or composite|Includes: Mounting points for the separation system",
            "Varies from tens of thousands to hundreds of thousands of dollars.",
            "Standard interface sizes let satellite makers build a spacecraft that can fly on several different rockets.");

        Part("Payload Dispenser",
            "A frame that holds and releases many satellites from one rocket.",
            "Deploys multiple satellites from a single launch, whether for rideshare missions or big constellations.",
            "Examples: ESPA ring, Starlink stacks, multi-satellite dispensers|Release: Carefully timed so satellites don't collide",
            "Varies by size and complexity; custom dispensers can cost hundreds of thousands of dollars.",
            "The ESPA ring (EELV Secondary Payload Adapter) first flew in 2007 and made it easy to carry small satellites alongside a big one.");

        Part("Spacecraft Separation Mechanism",
            "The latch that releases the satellite from the rocket at exactly the right moment.",
            "Holds the spacecraft tightly through the shaking of launch, then releases it cleanly with a gentle push so it drifts away from the rocket.",
            "Types: Clamp band (Marman clamp), separation nuts and bolts, motorized rings|Push-off: Springs give roughly 0.5-1 m/s separation speed",
            "Flight-qualified systems can cost from tens of thousands to over a hundred thousand dollars.",
            "The Marman clamp, used on countless launches, is named after Marman Products, the company founded by Herbert 'Zeppo' Marx of the Marx Brothers comedy team.");

        // ═════════════════════════════ 6. FLIGHT CONTROL & NAVIGATION ═════════════════════════════
        Cat("Flight Control & Navigation", "The rocket's brain and senses: they know where it is and steer it where it needs to go.");

        Part("Flight Computer",
            "The onboard computer that runs the rocket's software and makes split-second decisions.",
            "Runs guidance, navigation and control software, and sequences everything from engine start to stage separation and payload release.",
            "Redundancy: Often three computers that vote on every decision|Requirements: Radiation-tolerant, vibration-resistant|Example: Falcon 9 uses multiple redundant processors that cross-check each other",
            "Flight-qualified avionics can cost from hundreds of thousands to millions of dollars.",
            "The Saturn V's Launch Vehicle Digital Computer in the 1960s already used triple redundancy; modern rockets still use the same voting idea.");

        Part("Inertial Measurement Unit (IMU)",
            "A sensor package that tracks the rocket's motion and orientation without outside help.",
            "Measures rotation and acceleration so the flight computer can work out the rocket's direction, speed and position at every instant.",
            "Contains: Gyroscopes + accelerometers|Types: Ring laser, fiber optic, MEMS|Update rate: Hundreds of times per second",
            "From thousands of dollars (MEMS) to hundreds of thousands (high-end navigation grade).",
            "Inertial guidance was pioneered on the German V-2 and refined for ICBMs and the Apollo program.");

        Part("Gyroscope",
            "Senses how fast the rocket is rotating around each axis.",
            "Provides rotation-rate data so the rocket can hold its course and correct itself if it starts to tilt or spin.",
            "Types: Ring laser gyro, fiber optic gyro, MEMS|Measures: Pitch, yaw and roll rates",
            "Ranges from a few dollars for MEMS chips to tens of thousands for precision laser gyros.",
            "Early rockets used mechanical spinning gyros; today most use laser or fiber-optic gyros with no moving parts.");

        Part("Accelerometer",
            "Measures acceleration so the computer can work out speed and position.",
            "Senses every push and pull on the rocket; adding these measurements up over time tells the computer how fast it is going and how far it has traveled.",
            "Types: Pendulous, quartz, MEMS|Measures: Linear acceleration along 3 axes",
            "From a few dollars (MEMS) to thousands for navigation-grade sensors.",
            "The V-2 used an integrating accelerometer to decide exactly when to shut off its engine.");

        Part("GNSS Receiver",
            "Uses GPS and other satellite signals to pinpoint the rocket's position.",
            "Corrects the slow drift of inertial sensors and provides accurate tracking for range safety.",
            "Systems: GPS, Galileo, GLONASS, BeiDou|Special note: Space-use receivers need special versions, since normal receivers stop working at very high speed and altitude",
            "Space-qualified receivers typically cost tens of thousands of dollars or more.",
            "GPS became fully operational in 1995 and is now standard equipment on launch vehicles.");

        Part("Flight Control Unit",
            "Turns the computer's commands into actual movements of engines and fins.",
            "Drives the actuators that tilt (gimbal) the engine nozzles, move grid fins, and fire control thrusters.",
            "Controls: Thrust vector control, fins, cold-gas thrusters|Actuators: Hydraulic or electromechanical",
            "Typically tens of thousands to hundreds of thousands of dollars, including actuators.",
            "Early rockets steered with vanes in the exhaust; gimbaled engines became standard in the 1950s and 60s.");

        Part("Navigation System",
            "Combines all the sensors to answer: where are we, and how fast are we going?",
            "Fuses IMU and GNSS data, often with a Kalman filter, into one accurate estimate of position and velocity.",
            "Inputs: IMU, GNSS, sometimes star trackers|Method: Sensor fusion (Kalman filtering)|Output: Position, velocity, orientation",
            "Mostly the cost of its sensors and software development.",
            "The Kalman filter, developed around 1960, was famously used in the Apollo navigation system.");

        Part("Guidance System",
            "Decides the path the rocket should fly to reach its target orbit.",
            "Calculates the best trajectory and the steering commands needed to follow it, updating them in real time as the flight goes on.",
            "Methods: Pre-planned pitch program early on, closed-loop guidance later|Example: Powered Explicit Guidance (used on the Space Shuttle)",
            "Mostly software and engineering cost, often millions of dollars to develop and test.",
            "The Apollo Guidance Computer, built in the 1960s, was one of the first computers to use integrated circuits.");

        Part("Flight Termination System",
            "A safety system that destroys the rocket if it flies dangerously off course.",
            "Protects people and property on the ground by ending the flight if the rocket strays outside safe boundaries.",
            "Types: Command destruct (from the ground) or Autonomous FTS (onboard)|Mechanism: Explosive charges or engine shutdown",
            "Complete systems can cost hundreds of thousands to millions of dollars; autonomous systems also save money on ground equipment.",
            "SpaceX began flying an autonomous flight termination system around 2017, which no longer needed a person on the ground to trigger it.");

        // ═════════════════════════════ 7. ELECTRICAL & POWER ═════════════════════════════
        Cat("Electrical & Power", "Systems that store, control and deliver electricity to every component on board.");

        Part("Lithium-Ion Battery",
            "Rechargeable cells that power the avionics and actuators, and on some rockets even the fuel pumps.",
            "Supplies electricity to flight computers, sensors, valves, radios and actuators. On Rocket Lab's Electron, battery packs also spin the Rutherford engines' pumps.",
            "Energy density: ~150-260 Wh/kg|Common bus: 28 V class on many vehicles|Safety: Cell monitoring, thermal protection, venting|Lifetime needed: Minutes to hours on most rockets",
            "Space-qualified packs cost far more than consumer batteries, often tens to hundreds of thousands of dollars, because every cell is screened and tested.",
            "Earlier rockets and spacecraft relied on silver-zinc batteries. Lithium-ion took over in the 2000s and 2010s thanks to higher energy density and easy recharging for ground tests.");

        Part("Power Distribution Unit",
            "The rocket's fuse box: it routes power to every system and isolates faults.",
            "Distributes battery power to each subsystem, switches loads on and off when commanded, and protects the rest of the rocket if one circuit shorts out.",
            "Functions: Switching, fusing / current limiting, monitoring|Control: Commanded by the flight computer|Telemetry: Reports voltages and currents to the ground",
            "Custom avionics; typically tens of thousands to hundreds of thousands of dollars.",
            "Early rockets used relays and mechanical timers; modern units use solid-state switches that report their health in real time.");

        Part("Voltage Regulator",
            "Keeps the electrical voltage steady so sensitive electronics aren't damaged.",
            "Converts and stabilizes battery voltage, which drops as batteries drain, into clean constant voltages for computers and sensors.",
            "Types: Linear and switching (DC-DC converters)|Outputs: Common levels like 28 V, 12 V, 5 V, 3.3 V|Requirements: Survive vibration, heat and radiation",
            "Individual radiation-tolerant converters can cost hundreds to thousands of dollars each.",
            "Became more important as rockets moved from analog systems to digital computers that need very stable power.");

        Part("Electrical Wiring Harness",
            "Bundles of cables that carry power and data through the whole rocket.",
            "Connects batteries, computers, sensors, valves and separation charges. Harnesses run along the outside of the tanks inside protective covers called raceways.",
            "Carries: Power, data and pyrotechnic firing lines|Protection: Shielding, sleeving, raceway covers|Connectors: Locking, vibration-proof designs",
            "Very labor intensive. Large harnesses are often built by hand and can cost hundreds of thousands of dollars.",
            "Wiring is one of the heaviest and most failure-prone parts of a vehicle, so modern rockets cut weight by sending data over networks instead of one wire per signal.");

        Part("Backup Power Supply",
            "A second power source that takes over if the main one fails.",
            "Keeps critical systems running if a main battery or power line fails, especially the flight computer and the flight termination system.",
            "Form: Independent battery or redundant power bus|Priority: Flight termination usually has its own dedicated batteries|Switchover: Automatic",
            "Adds weight and cost, but is required for safety-critical systems.",
            "During Apollo 12 in 1969, lightning struck the Saturn V shortly after liftoff and knocked the spacecraft's power offline. The crew restored it using backup systems and the mission continued to the Moon.");

        Part("Solar Panel",
            "Panels that generate electricity on long missions, used on some upper stages and space tugs.",
            "Provides power for stages that must survive hours, days or months in orbit, like orbital transfer vehicles and some kick stages. Most rockets don't need them because their flights are short.",
            "Cells: Multi-junction gallium arsenide (~30% efficient)|Layout: Body-mounted or deployable panels|Paired with: Rechargeable batteries for when the Sun is blocked",
            "Space-grade solar cells cost far more than rooftop panels, often hundreds of dollars per watt.",
            "Solar power in space began with the Vanguard 1 satellite in 1958. Today, long-lived upper stages and space tugs increasingly carry their own panels.");

        // ═════════════════════════════ 8. COMMUNICATION & TELEMETRY ═════════════════════════════
        Cat("Communication & Telemetry", "Radio systems that let the rocket talk to the ground and send back thousands of measurements.");

        Part("Radio Transmitter",
            "Sends the rocket's data and live video back down to the ground.",
            "Broadcasts telemetry, video and status data to ground stations and relay satellites throughout the flight.",
            "Common band: S-band (2-4 GHz)|Data rate: Kilobits to megabits per second|Power: A few watts to tens of watts",
            "Space-qualified transmitters typically cost tens of thousands of dollars or more.",
            "Rocket telemetry began in the 1940s. Today, the live onboard views in launch webcasts come straight from these transmitters.");

        Part("Radio Receiver",
            "Listens for commands sent up from the ground, including safety commands.",
            "Receives uplinked commands, such as range-safety instructions, so ground teams can still act on the rocket during flight.",
            "Common band: S-band or UHF|Function: Command uplink|Reliability: Usually redundant",
            "Typically tens of thousands of dollars for flight-qualified units.",
            "Command receivers have been essential for range safety since the earliest missile tests in the 1940s and 50s.");

        Part("Telemetry System",
            "Collects thousands of measurements and packages them up for sending to the ground.",
            "Gathers data from sensors (pressures, temperatures, vibration, positions) and formats it into a data stream that engineers watch live and study after flight.",
            "Measurements: Often thousands of channels|Format: Packetized digital data (PCM)|Backup: Onboard recorders",
            "From tens of thousands to millions of dollars, depending on size and complexity.",
            "Telemetry is how engineers figure out what went wrong after a failure; the 2015 Falcon 9 CRS-7 accident was traced to a failed strut using flight telemetry.");

        Part("Tracking Antenna",
            "Antennas that let ground stations follow the rocket across the sky.",
            "Ground dishes lock onto the rocket's signal and follow it, while onboard antennas are placed so a link is kept no matter which way the rocket points.",
            "Ground: Large steerable dishes|Onboard: Wraparound or patch antennas|Relay: NASA's TDRS satellites can relay rocket data too",
            "Onboard antennas are inexpensive; large ground tracking stations cost millions to build and operate.",
            "NASA's Tracking and Data Relay Satellite (TDRS) system, starting in 1983, allowed almost continuous contact instead of waiting for the rocket to pass over ground stations.");

        Part("GPS/GNSS Antenna",
            "Small antennas that pick up navigation satellite signals.",
            "Feed GPS and other GNSS signals to the receiver for positioning and tracking.",
            "Type: Patch antennas, often placed on opposite sides of the vehicle|Frequencies: GPS L1/L2 and similar|Must survive: High speed, heat and vibration",
            "Usually hundreds to a few thousand dollars each.",
            "Became common on rockets in the 1990s and 2000s as GPS tracking started replacing radar for range safety.");

        Part("Communication Transponder",
            "Receives a signal and instantly answers, letting radar measure the rocket's exact position.",
            "Boosts radar tracking and allows precise measurements of distance and speed for range safety and orbit calculations.",
            "Types: C-band radar transponders, S-band ranging transponders|Function: Replies instantly to an incoming signal",
            "Typically tens of thousands of dollars for flight-qualified units.",
            "C-band radar transponders were standard on US launches for decades; GPS-based tracking is now replacing many of them.");

        // ═════════════════════════════ 9. THERMAL PROTECTION ═════════════════════════════
        Cat("Thermal Protection", "Shields that protect the rocket and its payload from extreme heat and cold.");

        Part("Heat Shield",
            "Protects vehicles from the extreme heat of plunging back through the atmosphere.",
            "Absorbs or blocks the intense heat created when a capsule or stage reenters the atmosphere at very high speed.",
            "Materials: PICA, Avcoat, ceramic tiles, reinforced carbon-carbon|Surface temperature: Can exceed 1,600 C|Example: SpaceX's Dragon uses PICA-X",
            "Large capsule heat shields cost millions of dollars.",
            "Heat shield research took off in the 1950s for missile warheads and the Mercury capsule.");

        Part("Thermal Insulation",
            "Foam and blankets that keep cold propellants cold and electronics comfortable.",
            "Reduces boil-off of super-cold propellants, prevents ice from forming on tanks, and shields equipment from engine heat.",
            "Types: Spray-on foam insulation (SOFI), multilayer insulation (MLI) blankets, cork|Used on: Cryogenic tanks, avionics bays",
            "Relatively cheap as material, but applying it carefully takes a lot of labor.",
            "A piece of foam insulation from the Shuttle's External Tank damaged Columbia's wing during launch in 2003, leading to the loss of the orbiter during reentry.");

        Part("Engine Cooling System",
            "Keeps the engine from melting, often by running cold fuel through its walls.",
            "Protects combustion chambers and nozzles from gases hotter than 3,000 C.",
            "Regenerative: Fuel flows through channels in the chamber walls|Film: A thin layer of fuel along the wall|Radiative: Hot nozzle glows to shed heat (Merlin Vacuum)|Ablative: Material wears away (RS-68)",
            "Built into the engine cost; regenerative channels are among the hardest parts to manufacture.",
            "Regenerative cooling was pioneered in the 1930s and 40s and used on the V-2.");

        Part("Thermal Protection Tiles",
            "Ceramic tiles that protect reusable vehicles from reentry heat.",
            "Insulate the vehicle's structure during reentry and are designed to be reused many times.",
            "Space Shuttle: ~24,000 silica tiles|Starship: ~18,000 hexagonal tiles|Property: Glowing hot on one side, cool enough to touch on the other",
            "Shuttle tiles were famously expensive and slow to inspect and replace; Starship's tiles are designed for mass production.",
            "Developed for the Space Shuttle in the 1970s. SpaceX revived the approach for Starship.");

        Part("Ablative Thermal Protection",
            "Material that slowly chars and wears away, carrying heat with it.",
            "Protects capsules and engine nozzles by sacrificing a layer of material instead of letting heat soak in.",
            "Materials: Avcoat, PICA, phenolic resins|How it works: Charring and gas release carry heat away|Reuse: Usually single use",
            "Varies; large ablative shields cost millions of dollars.",
            "Used on the Apollo capsules; NASA's Orion uses Avcoat today.");

        Part("Temperature Sensors",
            "Tiny sensors that measure temperatures across engines, tanks and electronics.",
            "Monitor temperatures for health checks, help condition propellants, and warn of problems before they become dangerous.",
            "Types: Thermocouples, RTDs, thermistors|Range: From -253 C (liquid hydrogen) to over 1,000 C (engines)",
            "Individual sensors are cheap, from a few dollars to a few hundred each.",
            "During Columbia's 2003 reentry, temperature sensors in the left wing were among the first signs that something was wrong.");

        // ═════════════════════════════ 10. PAYLOAD INTEGRATION ═════════════════════════════
        Cat("Payload Integration", "Hardware and processes that house, attach and release the payload.");

        Part("Payload Bay",
            "The protected space where the payload rides during launch.",
            "Provides a clean, safe area that holds the payload through the shaking, noise and pressure changes of launch.",
            "Space Shuttle cargo bay: ~18.3 m long x 4.6 m wide|On rockets: The volume inside the payload fairing|Environment: Controlled cleanliness, temperature and humidity",
            "Part of the vehicle or fairing cost.",
            "The Shuttle's payload bay carried the Hubble Space Telescope, ISS modules and many satellites.");

        Part("Payload Adapter",
            "The mechanical interface that bolts the satellite onto the launch vehicle.",
            "Joins the satellite to the rocket, carries launch loads, and gives the separation system a place to mount.",
            "Shape: Usually a cone or cylinder|Standard sizes: 937 mm, 1194 mm, 1666 mm|Material: Aluminum or composite",
            "Varies from tens of thousands to hundreds of thousands of dollars.",
            "Standardized adapters let one satellite design launch on many different rockets.");

        Part("Payload Mounting Bracket",
            "Brackets that secure smaller payloads and equipment inside the rocket.",
            "Hold small satellites, experiments or support hardware firmly in place during launch.",
            "Material: Aluminum or titanium|Design: Stiff enough to avoid shaking at resonant frequencies|Testing: Vibration tested before flight",
            "Usually inexpensive, from hundreds to thousands of dollars.",
            "Common on rideshare missions, where many small payloads share one rocket.");

        Part("Payload Deployment System",
            "Hardware that pushes payloads away from the rocket into space.",
            "Releases payloads at the right time and speed so they move safely away from the rocket and each other.",
            "Examples: Spring-loaded CubeSat deployers (P-POD), robotic arms (Shuttle's Canadarm)|Control: Timed commands from the flight computer",
            "CubeSat deployers cost thousands to tens of thousands of dollars; larger systems far more.",
            "The P-POD deployer, first flown in 2003, made launching tiny CubeSats simple and standard.");

        Part("Payload Separation Ring",
            "A ring-shaped separation system that releases the spacecraft cleanly.",
            "Holds the spacecraft during launch and releases it with low shock so delicate instruments aren't damaged.",
            "Examples: Lightband (motorized), clamp-band rings|Shock: Low-shock designs protect sensitive instruments",
            "Typically tens of thousands to over a hundred thousand dollars.",
            "The Lightband was developed by Planetary Systems Corporation, which Rocket Lab acquired in 2021.");

        Part("Payload Encapsulation System",
            "The process and hardware for sealing the payload inside the fairing before launch.",
            "Closes the payload inside the fairing in a clean room, then keeps clean, conditioned air flowing around it until liftoff.",
            "Location: Clean room|Environment: Filtered, temperature-controlled air flows into the fairing until launch|Steps: Mount payload, close fairing halves, move to the rocket",
            "Part of the launch service cost; clean-room facilities are expensive to run.",
            "The James Webb Space Telescope was encapsulated inside its Ariane 5 fairing in December 2021, shortly before its launch.");

        return list;
    }
}
