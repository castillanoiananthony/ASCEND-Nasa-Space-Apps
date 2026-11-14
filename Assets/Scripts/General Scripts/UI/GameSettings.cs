// ═════════════════════════════════════════════════════════════════════════════
//  GameSettings.cs  -  player settings (saved) + the settings screen used by the
//  Main Menu (with a Trailer button) and the in-game pause menu (Esc).
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class GameSettings
{
    public enum Character { Male, Female }

    const string KeyChar = "ASCEND.Character", KeySens = "ASCEND.MouseSensitivity";
    public const float MinSensitivity = 0.5f, MaxSensitivity = 10f, DefaultSensitivity = 3.5f;

    public static event Action Changed;

    public static Character PlayerCharacter
    {
        get => (Character)PlayerPrefs.GetInt(KeyChar, 0);
        set { PlayerPrefs.SetInt(KeyChar, (int)value); PlayerPrefs.Save(); Changed?.Invoke(); }
    }

    public static float MouseSensitivity
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(KeySens, DefaultSensitivity), MinSensitivity, MaxSensitivity);
        set { PlayerPrefs.SetFloat(KeySens, Mathf.Clamp(value, MinSensitivity, MaxSensitivity)); PlayerPrefs.Save(); Changed?.Invoke(); }
    }
}

public static class SettingsPanel
{
    /// <summary>
    /// Builds the settings screen (formal mission-control style). Returns the root object (active).
    /// buttons: label + action, shown as a row at the bottom (e.g. BACK, or RESUME / MAIN MENU).
    /// </summary>
    public static GameObject Build(Transform parent, TMP_FontAsset font, Action onTrailer, params (string label, Action action, bool primary)[] buttons)
    {
        var root = MissionUI.Solid(parent, new Color(0f, 0f, 0f, 0.78f), true, "Settings");
        MissionUI.Stretch(root.rectTransform);
        bool trailer = onTrailer != null;
        float h = trailer ? 520f : 410f;
        var panel = MissionUI.Box(root.transform, "Panel", MissionUI.Line, new Color(0f, 0f, 0f, 0.96f), true, true);
        MissionUI.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, h));
        MissionUI.Header(panel, "SETTINGS", "ASCEND   //   MISSION LEAD", 72f, 0f, font);

        float y = -110f;
        // Game character
        var cl = MissionUI.Lbl(panel, "GAME CHARACTER", 15, font); cl.color = MissionUI.Text;
        MissionUI.Place(cl.rectTransform, new Vector2(0f, 1f), new Vector2(40, y), new Vector2(330, 44)); cl.rectTransform.pivot = new Vector2(0f, 1f); cl.alignment = TextAlignmentOptions.MidlineLeft;
        var male = MissionUI.Btn(panel, "MALE", 17, MissionUI.Line, null, 170, 44, font);
        var female = MissionUI.Btn(panel, "FEMALE", 17, MissionUI.Line, null, 170, 44, font);
        MissionUI.Place((RectTransform)male.transform, new Vector2(0f, 1f), new Vector2(390, y), new Vector2(170, 44)); ((RectTransform)male.transform).pivot = new Vector2(0f, 1f);
        MissionUI.Place((RectTransform)female.transform, new Vector2(0f, 1f), new Vector2(580, y), new Vector2(170, 44)); ((RectTransform)female.transform).pivot = new Vector2(0f, 1f);
        Action refreshChar = () =>
        {
            bool m = GameSettings.PlayerCharacter == GameSettings.Character.Male;
            MissionUI.SetColor(male, m ? MissionUI.Accent : MissionUI.LineDim);
            MissionUI.SetColor(female, m ? MissionUI.LineDim : MissionUI.Accent);
        };
        male.onClick.AddListener(() => { GameSettings.PlayerCharacter = GameSettings.Character.Male; refreshChar(); });
        female.onClick.AddListener(() => { GameSettings.PlayerCharacter = GameSettings.Character.Female; refreshChar(); });
        refreshChar();
        MissionUI.HLine(panel, -y + 70f, MissionUI.LineDim, 30f);

        // Mouse sensitivity
        y -= 100f;
        var sl = MissionUI.Lbl(panel, "MOUSE SENSITIVITY", 15, font); sl.color = MissionUI.Text;
        MissionUI.Place(sl.rectTransform, new Vector2(0f, 1f), new Vector2(40, y), new Vector2(330, 44)); sl.rectTransform.pivot = new Vector2(0f, 1f); sl.alignment = TextAlignmentOptions.MidlineLeft;
        var slider = MakeSlider(panel, new Vector2(390, y - 7f), new Vector2(270, 30), GameSettings.MinSensitivity, GameSettings.MaxSensitivity, GameSettings.MouseSensitivity);
        var val = MissionUI.Txt(panel, "", 18, Color.white, FontStyles.Bold, font);
        MissionUI.Place(val.rectTransform, new Vector2(0f, 1f), new Vector2(680, y), new Vector2(100, 44)); val.rectTransform.pivot = new Vector2(0f, 1f); val.alignment = TextAlignmentOptions.MidlineLeft;
        val.text = GameSettings.MouseSensitivity.ToString("0.0");
        slider.onValueChanged.AddListener(v => { v = Mathf.Round(v * 10f) / 10f; GameSettings.MouseSensitivity = v; val.text = v.ToString("0.0"); });
        MissionUI.HLine(panel, -y + 70f, MissionUI.LineDim, 30f);

        // Trailer (main menu only)
        if (trailer)
        {
            y -= 100f;
            var tl = MissionUI.Lbl(panel, "TRAILER", 15, font); tl.color = MissionUI.Text;
            MissionUI.Place(tl.rectTransform, new Vector2(0f, 1f), new Vector2(40, y), new Vector2(330, 44)); tl.rectTransform.pivot = new Vector2(0f, 1f); tl.alignment = TextAlignmentOptions.MidlineLeft;
            var tb = MissionUI.Btn(panel, "WATCH TRAILER", 17, MissionUI.Accent, onTrailer, 360, 44, font);
            MissionUI.Place((RectTransform)tb.transform, new Vector2(0f, 1f), new Vector2(390, y), new Vector2(360, 44)); ((RectTransform)tb.transform).pivot = new Vector2(0f, 1f);
        }

        // Bottom buttons
        float bw = 230f, gap = 20f, total = buttons.Length * bw + (buttons.Length - 1) * gap;
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i];
            var btn = MissionUI.Btn(panel, b.label, 18, b.primary ? MissionUI.Text : MissionUI.LineDim, () => b.action?.Invoke(), bw, 50, font);
            MissionUI.Place((RectTransform)btn.transform, new Vector2(0.5f, 0f), new Vector2(-total * 0.5f + bw * 0.5f + i * (bw + gap), 34), new Vector2(bw, 50));
        }
        return root.gameObject;
    }

    static Slider MakeSlider(RectTransform parent, Vector2 pos, Vector2 size, float min, float max, float value)
    {
        var r = MissionUI.Rect("Slider", parent);
        MissionUI.Place(r, new Vector2(0f, 1f), pos, size); r.pivot = new Vector2(0f, 1f);
        var track = MissionUI.Solid(r, MissionUI.LineDim, false, "Track").rectTransform;
        track.anchorMin = new Vector2(0f, 0.5f); track.anchorMax = new Vector2(1f, 0.5f); track.sizeDelta = new Vector2(0f, 3f); track.anchoredPosition = Vector2.zero;
        var hit = r.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var fillArea = MissionUI.Rect("Fill Area", r); MissionUI.Stretch(fillArea, 0, 13, 0, 13);
        var fill = MissionUI.Solid(fillArea, MissionUI.Accent, false, "Fill"); fill.rectTransform.sizeDelta = Vector2.zero;
        var handleArea = MissionUI.Rect("Handle Area", r); MissionUI.Stretch(handleArea, 8, 0, 8, 0);
        var handle = MissionUI.Box(handleArea, "Handle", MissionUI.Text, Color.black, false, true);
        handle.sizeDelta = new Vector2(16f, 0f);
        var s = r.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform; s.handleRect = handle; s.targetGraphic = handle.GetComponent<Image>();
        s.minValue = min; s.maxValue = max; s.value = value;
        s.navigation = new Navigation { mode = Navigation.Mode.None };
        return s;
    }
}
