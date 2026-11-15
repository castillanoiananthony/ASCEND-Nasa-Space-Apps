// ═════════════════════════════════════════════════════════════════════════════
//  IntroStory.cs  -  the black-screen background story shown after you open a
//  world in the Main Menu, while the Hangar loads in the background.
//  SPACE = next line, S = skip. Then the Hangar opens with the office intro.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Small bits of state passed between scenes
public static class GameFlow
{
    // Set by the Main Menu when a world is opened: the Hangar starts with the office intro
    public static bool PlayOfficeIntro;
    // Set by the Main Menu's Trailer button: the Universe scene plays the 30 s trailer, then goes back
    public static bool PlayTrailer;
    // The trailer plays once when the game is opened, before the Main Menu
    public static bool IntroTrailerShown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { PlayOfficeIntro = false; PlayTrailer = false; IntroTrailerShown = false; }

    public static bool KeyDown(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        string n = k.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        else if (n == "Return") n = "Enter";
        return Enum.TryParse(n, out Key key) && key != Key.None && kb[key].wasPressedThisFrame;
#else
        return Input.GetKeyDown(k);
#endif
    }
}

public static class IntroStory
{
    public static readonly string[] Lines =
    {
        "You are one of the best rocket engineers in the world.",
        "You have built rockets and satellites for space agencies all over the planet.",
        "Now the Philippines has its own space agency: <b>ASCEND</b>.",
        "They have hired you to lead their next mission.",
        "Your job: design the rocket, spend the money wisely, and fly it.",
        "Your first day starts now.",
    };

    public static IEnumerator Run(TMP_FontAsset font, float lettersPerSecond = 40f)
    {
        var cgo = new GameObject("Intro Story", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.Object.DontDestroyOnLoad(cgo);   // stays black while the Hangar takes over
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2000;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)cgo.transform;

        var bg = MissionUI.Solid(root, Color.black, true, "Black");
        MissionUI.Stretch(bg.rectTransform);

        var head = MissionUI.Lbl(root, "ASCEND   //   PERSONNEL FILE   //   MISSION LEAD", 14, font);
        MissionUI.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(60, -50), new Vector2(1200, 30));
        head.rectTransform.pivot = new Vector2(0f, 1f); head.alignment = TextAlignmentOptions.Left;

        var text = MissionUI.Txt(root, "", 40, MissionUI.Text, FontStyles.Normal, font);
        text.alignment = TextAlignmentOptions.Center; text.lineSpacing = 30f;
        MissionUI.Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1200, 400));

        var count = MissionUI.Lbl(root, "", 14, font);
        MissionUI.Place(count.rectTransform, new Vector2(1f, 0f), new Vector2(-60, 50), new Vector2(300, 30));
        count.rectTransform.pivot = new Vector2(1f, 0f); count.alignment = TextAlignmentOptions.Right;

        // Key hints, bottom left
        var hints = MissionUI.Txt(root, "<color=#FFFFFF>SPACE</color>   CONTINUE        <color=#FFFFFF>S</color>   SKIP", 18, MissionUI.Label, FontStyles.Bold, font);
        hints.characterSpacing = 6f; hints.alignment = TextAlignmentOptions.Left;
        MissionUI.Place(hints.rectTransform, new Vector2(0f, 0f), new Vector2(60, 50), new Vector2(900, 30));
        hints.rectTransform.pivot = new Vector2(0f, 0f);

        var group = text.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < Lines.Length; i++)
        {
            count.text = (i + 1).ToString("00") + " / " + Lines.Length.ToString("00");
            text.text = Lines[i];
            text.maxVisibleCharacters = 0;
            text.ForceMeshUpdate();
            int total = text.textInfo.characterCount;
            group.alpha = 1f;
            float shown = 0f;
            bool skip = false;
            yield return null;
            // type the line (SPACE shows it all at once)
            while (shown < total)
            {
                if (GameFlow.KeyDown(KeyCode.S)) { skip = true; break; }
                if (GameFlow.KeyDown(KeyCode.Space)) { shown = total; break; }
                shown += Time.unscaledDeltaTime * lettersPerSecond;
                text.maxVisibleCharacters = (int)shown;
                yield return null;
            }
            text.maxVisibleCharacters = 99999;
            if (skip) break;
            yield return null;
            // wait for SPACE
            while (true)
            {
                if (GameFlow.KeyDown(KeyCode.S)) { skip = true; break; }
                if (GameFlow.KeyDown(KeyCode.Space)) break;
                yield return null;
            }
            if (skip) break;
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.25f; yield return null; }
        }
        text.text = ""; hints.text = ""; count.text = "";
        head.text = "ASCEND HEADQUARTERS   //   LOADING";
        // The caller loads the next scene; the Hangar's MissionBriefing fades in and destroys this canvas.
    }

    // Called by the Hangar once its own black screen is up
    public static void Remove()
    {
        var go = GameObject.Find("Intro Story");
        if (go) UnityEngine.Object.Destroy(go);
    }
}
