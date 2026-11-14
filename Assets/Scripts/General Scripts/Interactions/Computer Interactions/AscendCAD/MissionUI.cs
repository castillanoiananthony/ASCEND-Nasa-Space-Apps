// ═════════════════════════════════════════════════════════════════════════════
//  MissionUI.cs  -  formal "mission control" look for the Launch Computer,
//  the flight HUD and the Satellite Control console.
//
//  Black screens, thin white lines, outline-only boxes and buttons (no fill colors),
//  small corner brackets, grey uppercase labels and white values.
//  Used by AscendFlight.cs and AscendOrbits.cs.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class MissionUI
{
    // ── Palette ──
    public static readonly Color Bg      = Color.black;
    public static readonly Color HudBg   = new Color(0f, 0f, 0f, 0.82f);   // black behind HUD windows over the 3D view
    public static readonly Color Line    = new Color(1f, 1f, 1f, 0.7f);
    public static readonly Color LineDim = new Color(1f, 1f, 1f, 0.22f);
    public static readonly Color Text    = new Color(0.95f, 0.96f, 0.97f);
    public static readonly Color Label   = new Color(0.6f, 0.63f, 0.67f);
    public static readonly Color Accent  = new Color(0.45f, 0.85f, 1f);
    public static readonly Color Go      = new Color(0.25f, 0.85f, 0.4f);
    public static readonly Color Caution = new Color(1f, 0.75f, 0.25f);
    public static readonly Color Alert   = new Color(1f, 0.3f, 0.25f);
    public const string LabelHex = "#9AA0A6";

    // ── Basics ──
    public static RectTransform Rect(string n, Transform p)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(p, false);
        return (RectTransform)go.transform;
    }

    public static RectTransform Stretch(RectTransform r, float l = 0, float b = 0, float rt = 0, float t = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rt, -t);
        return r;
    }

    public static RectTransform Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = anchor; r.anchoredPosition = pos; r.sizeDelta = size;
        return r;
    }

    // A plain rectangle (used for black backgrounds and for lines)
    public static Image Solid(Transform p, Color c, bool raycast = false, string name = "Rect")
    {
        var img = Rect(name, p).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = raycast;
        return img;
    }

    public static TextMeshProUGUI Txt(Transform p, string s, float size, Color c, FontStyles st = FontStyles.Normal, TMP_FontAsset font = null)
    {
        var t = Rect("Text", p).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font; else if (AUI.Font) t.font = AUI.Font;
        t.text = s; t.fontSize = size; t.color = c; t.fontStyle = st;
        t.richText = true; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    // Small grey uppercase label with wide letter spacing
    public static TextMeshProUGUI Lbl(Transform p, string s, float size = 14f, TMP_FontAsset font = null)
    {
        var t = Txt(p, s.ToUpperInvariant(), size, Label, FontStyles.Normal, font);
        t.characterSpacing = 8f;
        return t;
    }

    // ── Lines and outlines ──
    static Image Edge(RectTransform r, Color c, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        var img = Solid(r, c, false, "Border");
        var e = img.rectTransform;
        e.anchorMin = aMin; e.anchorMax = aMax; e.pivot = pivot; e.sizeDelta = size; e.anchoredPosition = pos;
        img.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return img;
    }

    // Outline only: four thin lines on the edges, nothing inside
    public static RectTransform Border(RectTransform r, Color c, float w = 1.5f)
    {
        Edge(r, c, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, w), Vector2.zero);
        Edge(r, c, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, w), Vector2.zero);
        Edge(r, c, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(w, 0), Vector2.zero);
        Edge(r, c, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(w, 0), Vector2.zero);
        return r;
    }

    // Heavier L-shaped brackets on the four corners (the "console screen" look)
    public static void Corners(RectTransform r, Color c, float len = 22f, float w = 3f)
    {
        for (int i = 0; i < 4; i++)
        {
            var a = new Vector2(i % 2, i / 2);
            float sx = a.x == 0 ? 1 : -1, sy = a.y == 0 ? 1 : -1;
            Edge(r, c, a, a, a, new Vector2(len, w), new Vector2(-sx * 1f, -sy * 1f));
            Edge(r, c, a, a, a, new Vector2(w, len), new Vector2(-sx * 1f, -sy * 1f));
        }
    }

    // A horizontal line across the parent at a distance from the top
    public static Image HLine(RectTransform p, float fromTop, Color c, float inset = 0f, float w = 1f)
    {
        var img = Solid(p, c, false, "Line");
        var r = img.rectTransform;
        r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
        r.sizeDelta = new Vector2(-inset * 2f, w); r.anchoredPosition = new Vector2(0, -fromTop);
        return img;
    }

    // Outline-only box (optionally on a black background)
    public static RectTransform Box(Transform p, string name, Color line, Color? bg = null, bool corners = false, bool raycast = false)
    {
        var r = Rect(name, p);
        var img = r.gameObject.AddComponent<Image>();
        img.color = bg ?? Color.clear; img.raycastTarget = raycast;
        Border(r, line);
        if (corners) Corners(r, Text);
        return r;
    }

    // Title strip: title on the left, small grey subtitle on the right, line under it
    public static void Header(RectTransform panel, string title, string right, float h = 72f, float rightInset = 0f, TMP_FontAsset font = null)
    {
        var t = Txt(panel, title, 30, Text, FontStyles.Bold, font);
        t.characterSpacing = 6f; t.alignment = TextAlignmentOptions.MidlineLeft;
        Place(t.rectTransform, new Vector2(0, 1), new Vector2(30, 0), new Vector2(900, h));
        t.rectTransform.pivot = new Vector2(0, 1);
        if (!string.IsNullOrEmpty(right))
        {
            var r = Lbl(panel, right, 14, font);
            r.alignment = TextAlignmentOptions.MidlineRight;
            r.rectTransform.anchorMin = new Vector2(0, 1); r.rectTransform.anchorMax = new Vector2(1, 1); r.rectTransform.pivot = new Vector2(0.5f, 1);
            r.rectTransform.offsetMin = new Vector2(30f, -h); r.rectTransform.offsetMax = new Vector2(-30f - rightInset, 0f);
        }
        HLine(panel, h, Line, 0f, 1.5f);
    }

    // ── Controls ──

    // Outline-only button: colored frame + colored label, label turns white on hover
    public static Button Btn(Transform p, string label, float size, Color c, Action onClick, float w = -1, float h = 44, TMP_FontAsset font = null)
    {
        var r = Rect(label, p);
        var hit = r.gameObject.AddComponent<Image>();
        hit.color = Color.clear; hit.raycastTarget = true;
        Border(r, c);
        var t = Txt(r, label, size, Color.white, FontStyles.Bold, font);
        Stretch(t.rectTransform, 8, 0, 8, 0);
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap; t.characterSpacing = 6f;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = Mathf.Min(11f, size);
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = t;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        SetColor(b, c);
        if (onClick != null) b.onClick.AddListener(() => onClick());
        var le = r.gameObject.AddComponent<LayoutElement>();
        if (w > 0) le.preferredWidth = le.minWidth = w;
        if (h > 0) le.preferredHeight = le.minHeight = h;
        return b;
    }

    // Recolor an outline button (frame + label)
    public static void SetColor(Button b, Color c)
    {
        if (!b) return;
        var cb = ColorBlock.defaultColorBlock;
        cb.normalColor = c; cb.selectedColor = c; cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.7f, 0.7f, 0.7f); cb.disabledColor = new Color(c.r, c.g, c.b, 0.3f);
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        foreach (Transform ch in b.transform)
            if (ch.name == "Border") { var i = ch.GetComponent<Image>(); if (i) i.color = c; }
    }

    // Outline-only text field
    public static TMP_InputField Field(Transform p, string placeholder, float size, TMP_FontAsset font = null)
    {
        var r = Rect("Input", p);
        var hit = r.gameObject.AddComponent<Image>();
        hit.color = Color.clear; hit.raycastTarget = true;
        Border(r, Line);
        var area = Rect("Text Area", r); Stretch(area, 14, 2, 14, 2); area.gameObject.AddComponent<RectMask2D>();
        var ph = Txt(area, placeholder, size, new Color(1f, 1f, 1f, 0.3f), FontStyles.Normal, font);
        var tx = Txt(area, "", size, Text, FontStyles.Bold, font);
        foreach (var t in new[] { ph, tx }) { Stretch(t.rectTransform); t.alignment = TextAlignmentOptions.MidlineLeft; t.textWrappingMode = TextWrappingModes.NoWrap; }
        tx.richText = false;
        var f = r.gameObject.AddComponent<TMP_InputField>();
        f.textViewport = area; f.textComponent = tx; f.placeholder = ph; f.targetGraphic = hit;
        if (font) f.fontAsset = font; else if (AUI.Font) f.fontAsset = AUI.Font;
        f.pointSize = size; f.lineType = TMP_InputField.LineType.SingleLine;
        f.customCaretColor = true; f.caretColor = Accent; f.caretWidth = 2;
        f.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        f.navigation = new Navigation { mode = Navigation.Mode.None };
        return f;
    }

    // A thin progress bar: dim track line with a solid indicator. Returns the indicator (set anchorMax.x = 0..1).
    public static RectTransform Bar(RectTransform track, Color c, out Image img)
    {
        var line = Solid(track, LineDim, false, "Track").rectTransform;
        Stretch(line);
        img = Solid(track, c, false, "Value");
        var f = img.rectTransform;
        f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(0f, 1f); f.offsetMin = f.offsetMax = Vector2.zero;
        return f;
    }
}
