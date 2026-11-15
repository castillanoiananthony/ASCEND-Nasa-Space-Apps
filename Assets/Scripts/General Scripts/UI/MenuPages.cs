// ═════════════════════════════════════════════════════════════════════════════
//  MenuPages.cs  -  the Main Menu's MULTIPLAYER (coming soon + team roles) and
//  ABOUT GAME pages (About / How to Play / Future Development tabs).
//  Same formal mission-control look as Settings. Edit the text in the arrays
//  below; the layout adjusts itself.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class MenuPages
{
    public static readonly Vector2 PageSize = new Vector2(1640, 980);

    // ───────────────────────── MULTIPLAYER ─────────────────────────
    public static readonly string MultiplayerGoal =
        "Real rockets are never built by one person. In Multiplayer, you and your friends will form one ASCEND " +
        "mission team. Each player takes a real engineering role, owns their part of the rocket, and works with " +
        "the others to design, test and launch the mission together - just like a real space agency.";

    public static readonly (string role, string desc)[] Roles =
    {
        ("CHIEF SYSTEMS ENGINEER",
         "Leads the team and sees the whole rocket. Sets the mission goals, manages the budget and makes sure every part works together."),
        ("STRUCTURES, MATERIALS & CAD LEAD",
         "Builds the rocket's body in AscendCAD. Chooses strong, light materials and keeps the rocket from getting too heavy."),
        ("AERODYNAMICS & COMPUTATIONAL FLUID DYNAMICS",
         "Shapes the rocket to cut through the air. Uses air-flow simulation (CFD) to study drag, heating and stability."),
        ("PROPULSION & ENERGY SYSTEMS",
         "Picks the engines, fuel and power systems. Makes sure there is enough thrust to reach orbit and enough power for the satellite."),
        ("FLIGHT DYNAMICS & CONTROLS",
         "Plans the path to orbit and steers the rocket: guidance, staging at the right moment, and placing the satellite in the right orbit."),
    };

    // ───────────────────────── ABOUT GAME ─────────────────────────
    public static readonly string AboutIntro =
        "ASCEND is a space mission game. You are the new Mission Lead of ASCEND, the space agency of the Philippines. " +
        "Design a real rocket and satellite, stay inside a $30 million budget, launch it, and photograph the Philippines from orbit.";

    public static readonly (string title, string desc)[] About =
    {
        ("REAL PARTS, REAL PHYSICS, REAL BUDGET",
         "Every engine, tank and instrument is based on real hardware. Too heavy or too weak, and your rocket will not reach orbit."),
        ("LEARN BY DOING",
         "Made for players aged 6 and up: simple enough to start right away, deep enough for adults who love engineering."),
        ("FOR THE PHILIPPINES",
         "Satellites help with weather, farming and disaster response. The photos you take help the people back home."),
    };

    public static readonly string Credits =
        "CREDITS:   Earth textures - Solar System Scope (CC BY 4.0), based on NASA data.   Suit by Quaternius (CC-BY) via Poly Pizza.   " +
        "Characters - Quaternius (CC0).   Ground and road textures - Poly Haven (CC0).";

    public static readonly (string title, string desc)[] HowToPlay =
    {
        ("START AT YOUR DESK",   "Press P to open your phone. Read PhMail and pick a mission. Press SPACE to stand up from your chair."),
        ("MEET YOUR TEAM",       "Walk to the glowing Meeting Room door. Your team explains the mission and your budget."),
        ("DESIGN IN ASCENDCAD",  "At your computer, build the rocket (Rocket Builder) and the satellite (Payload Builder). Stay inside the fund, then press SUBMIT."),
        ("PLAN THE FLIGHT",      "Open the Mission Simulator, plan the path to orbit and send it to Mission Control."),
        ("LAUNCH",               "Go to the launch console and press E. Press 1, 2 or 3 to switch cameras while it flies."),
        ("TAKE 5 PHOTOS",        "In orbit: W A S D aim the camera, scroll to zoom, SPACE takes a photo. Your mission report arrives in PhMail."),
    };

    public static readonly string Controls =
        "<color=#FFFFFF>W A S D</color>  MOVE      <color=#FFFFFF>SHIFT</color>  RUN      <color=#FFFFFF>MOUSE</color>  LOOK      " +
        "<color=#FFFFFF>E</color>  USE      <color=#FFFFFF>P</color>  PHONE      <color=#FFFFFF>ESC</color>  PAUSE";

    public static readonly (string goal, string desc)[] Plans =
    {
        ("FIT FOR AGES 6+",                 "The right balance: simple enough for a child to understand, deep enough for an adult to enjoy."),
        ("BETTER UI & NPC INTEGRATION",     "Clearer screens and a living team at ASCEND who talk, react and guide you."),
        ("MORE CRAFT PARTS",                "More engines, tanks, boosters, fairings and satellite instruments to build with."),
        ("EXPANSION OF MISSIONS",           "New goals: weather, disaster monitoring, communications, the Moon and beyond."),
    };

    public static readonly (string goal, string desc)[] Suggestions =
    {
        ("FILIPINO LANGUAGE",               "Play fully in Filipino as well as English, so every young Filipino can learn in their own language."),
        ("REAL PHILIPPINE MISSIONS",        "Missions inspired by real Philippine satellites like Diwata and the Maya CubeSats."),
        ("ENGINEER'S NOTEBOOK",             "Short, friendly notes that unlock as you play and explain thrust, gravity and orbits."),
        ("PHOTO GALLERY",                   "Keep the Earth photos from every mission and share your best shots."),
    };

    const float TitleSize = 26f, DescSize = 21f, Inset = 56f;

    // ─────────────────────────────────────────────────────────────────────────
    public static GameObject BuildMultiplayer(Transform parent, TMP_FontAsset font, Action onBack)
    {
        var panel = Frame(parent, "Multiplayer", "MULTIPLAYER", "ASCEND   //   MISSION TEAM", font, out var root);

        var soon = MissionUI.Txt(panel, "OPENING FEATURE SOON", 54, MissionUI.Accent, FontStyles.Bold, font);
        soon.characterSpacing = 14f; soon.alignment = TextAlignmentOptions.Center;
        TopRow(soon.rectTransform, 98f, 70f, Inset);

        var goal = MissionUI.Txt(panel, MultiplayerGoal, 23, MissionUI.Text, FontStyles.Normal, font);
        goal.alignment = TextAlignmentOptions.Center; goal.lineSpacing = 16f;
        TopRow(goal.rectTransform, 180f, 120f, 90f);

        MissionUI.HLine(panel, 312f, MissionUI.LineDim, Inset);
        var rl = MissionUI.Lbl(panel, "TEAM ROLES", 18, font); rl.alignment = TextAlignmentOptions.Left;
        TopRow(rl.rectTransform, 326f, 30f, Inset);

        float y = 358f;
        for (int i = 0; i < Roles.Length; i++, y += 104f)
            Item(panel, font, (i + 1).ToString("00"), Roles[i].role, Roles[i].desc, Inset, y, PageSize.x - Inset * 2f);

        BackButton(panel, font, onBack);
        return root;
    }

    public static GameObject BuildAbout(Transform parent, TMP_FontAsset font, Action onBack)
    {
        var panel = Frame(parent, "About Game", "ABOUT GAME", "ASCEND   //   MISSION HANDBOOK", font, out var root);
        var tabs = new List<(Button btn, GameObject page)>();
        float w = PageSize.x - Inset * 2f;

        // ── About ──
        var about = Page(panel, "About");
        var intro = MissionUI.Txt(about, AboutIntro, 25, MissionUI.Text, FontStyles.Normal, font);
        intro.lineSpacing = 16f; intro.alignment = TextAlignmentOptions.TopLeft;
        TopRow(intro.rectTransform, 0f, 130f, Inset);
        float y = 160f;
        for (int i = 0; i < About.Length; i++, y += 112f)
            Item(about, font, (i + 1).ToString("00"), About[i].title, About[i].desc, Inset, y, w);
        var cr = MissionUI.Txt(about, Credits, 16, MissionUI.Label, FontStyles.Normal, font);
        cr.lineSpacing = 12f; cr.alignment = TextAlignmentOptions.TopLeft;
        TopRow(cr.rectTransform, y + 14f, 70f, Inset);
        MissionUI.HLine(about, y + 2f, MissionUI.LineDim, Inset);

        // ── How to play ──
        var how = Page(panel, "How To Play");
        float colW = (w - 60f) * 0.5f;
        for (int i = 0; i < HowToPlay.Length; i++)
        {
            float x = i < 3 ? Inset : Inset + colW + 60f;
            Item(how, font, (i + 1).ToString("00"), HowToPlay[i].title, HowToPlay[i].desc, x, (i % 3) * 172f, colW, 3);
        }
        MissionUI.HLine(how, 530f, MissionUI.LineDim, Inset);
        var ck = MissionUI.Lbl(how, "CONTROLS", 18, font); ck.alignment = TextAlignmentOptions.Left;
        TopRow(ck.rectTransform, 548f, 30f, Inset);
        var keys = MissionUI.Txt(how, Controls, 22, MissionUI.Label, FontStyles.Bold, font);
        keys.characterSpacing = 4f; keys.alignment = TextAlignmentOptions.Left; keys.textWrappingMode = TextWrappingModes.NoWrap;
        keys.enableAutoSizing = true; keys.fontSizeMax = 22f; keys.fontSizeMin = 14f;
        TopRow(keys.rectTransform, 590f, 40f, Inset);

        // ── Future development ──
        var future = Page(panel, "Future Development");
        var gl = MissionUI.Lbl(future, "FUTURE GOALS", 18, font); gl.alignment = TextAlignmentOptions.Left;
        Place(gl.rectTransform, Inset, 0f, colW, 30f);
        var sl = MissionUI.Lbl(future, "SUGGESTED IDEAS", 18, font); sl.alignment = TextAlignmentOptions.Left; sl.color = MissionUI.Accent;
        Place(sl.rectTransform, Inset + colW + 60f, 0f, colW, 30f);
        for (int i = 0; i < Plans.Length; i++)
            Item(future, font, (i + 1).ToString("00"), Plans[i].goal, Plans[i].desc, Inset, 46f + i * 140f, colW, 3);
        for (int i = 0; i < Suggestions.Length; i++)
            Item(future, font, "+", Suggestions[i].goal, Suggestions[i].desc, Inset + colW + 60f, 46f + i * 140f, colW, 3);

        // ── Tabs ──
        var pages = new[] { about.gameObject, how.gameObject, future.gameObject };
        var labels = new[] { "ABOUT", "HOW TO PLAY", "FUTURE DEVELOPMENT" };
        float tw = 330f, gap = 16f, tx = Inset;
        for (int i = 0; i < pages.Length; i++)
        {
            int idx = i;
            var b = MissionUI.Btn(panel, labels[i], 19, MissionUI.LineDim, null, tw, 48, font);
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(tx, -94f); brt.sizeDelta = new Vector2(tw, 48f);
            tx += tw + gap;
            tabs.Add((b, pages[i]));
        }
        Action<int> show = sel =>
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].page.SetActive(i == sel);
                MissionUI.SetColor(tabs[i].btn, i == sel ? MissionUI.Accent : MissionUI.LineDim);
            }
        };
        for (int i = 0; i < tabs.Count; i++) { int idx = i; tabs[i].btn.onClick.AddListener(() => show(idx)); }
        show(0);
        // always open on the About tab
        root.AddComponent<OnEnableAction>().action = () => show(0);

        BackButton(panel, font, onBack);
        return root;
    }

    // ─────────────────────────────────────────────────────────────────────────
    public static RectTransform Frame(Transform parent, string name, string title, string right, TMP_FontAsset font, out GameObject root)
    {
        var bg = MissionUI.Solid(parent, new Color(0f, 0f, 0f, 0.84f), true, name);
        MissionUI.Stretch(bg.rectTransform);
        root = bg.gameObject;
        var panel = MissionUI.Box(bg.transform, "Panel", MissionUI.Line, new Color(0f, 0f, 0f, 0.96f), true, true);
        MissionUI.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, PageSize);
        MissionUI.Header(panel, title, right, 76f, 0f, font);
        return panel;
    }

    // content area under the tabs
    static RectTransform Page(RectTransform panel, string name)
    {
        var r = MissionUI.Rect(name, panel);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(0f, 110f); r.offsetMax = new Vector2(0f, -170f);
        return r;
    }

    // a full-width row 'top' pixels from the parent's top
    public static void TopRow(RectTransform r, float top, float h, float inset = 40f)
    {
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
        r.offsetMin = new Vector2(inset, -top - h); r.offsetMax = new Vector2(-inset, -top);
    }

    static void Place(RectTransform r, float x, float top, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(x, -top); r.sizeDelta = new Vector2(w, h);
    }

    // number | TITLE / description (lines = how many description lines to leave room for)
    static void Item(RectTransform parent, TMP_FontAsset font, string num, string title, string desc, float x, float top, float width, int lines = 2)
    {
        var n = MissionUI.Txt(parent, num, TitleSize, MissionUI.Accent, FontStyles.Bold, font);
        n.alignment = TextAlignmentOptions.TopLeft;
        Place(n.rectTransform, x, top, 70f, 36f);

        var t = MissionUI.Txt(parent, title, TitleSize, Color.white, FontStyles.Bold, font);
        t.characterSpacing = 4f; t.alignment = TextAlignmentOptions.TopLeft; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMax = TitleSize; t.fontSizeMin = 16f;
        Place(t.rectTransform, x + 72f, top, width - 72f, 36f);

        var d = MissionUI.Txt(parent, desc, DescSize, MissionUI.Label, FontStyles.Normal, font);
        d.alignment = TextAlignmentOptions.TopLeft; d.lineSpacing = 8f;
        Place(d.rectTransform, x + 72f, top + 38f, width - 72f, 30f * lines);
    }

    public static Button BackButton(RectTransform panel, TMP_FontAsset font, Action onBack)
    {
        var b = MissionUI.Btn(panel, "BACK", 22, MissionUI.Text, onBack, 260, 56, font);
        MissionUI.Place((RectTransform)b.transform, new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(260, 56));
        return b;
    }
}

// Runs an action every time the object is shown (used to reset tabs)
public class OnEnableAction : MonoBehaviour
{
    public Action action;
    void OnEnable() => action?.Invoke();
}
