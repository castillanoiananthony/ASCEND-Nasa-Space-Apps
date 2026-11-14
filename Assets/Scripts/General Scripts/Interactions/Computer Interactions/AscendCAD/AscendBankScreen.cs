// ═════════════════════════════════════════════════════════════════════════════
//  AscendBankScreen.cs  -  the ASCEND BANK app (phone and computer use the same screen)
//  Shows the mission fund balance, a pending deposit and the transaction history.
//  The money is AscendEconomy.Balance: the same account AscendCAD spends from.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AscendBankScreen
{
    readonly RectTransform content;
    readonly ScrollRect scroll;
    readonly TMP_FontAsset font;
    readonly float scale;
    bool dirty = true;

    static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;
    static readonly Color Txt = Hex("#E8EAED"), Sub = Hex("#9AA0A6"), Gold = Hex("#FFC850"), RowBg = new Color(1f, 1f, 1f, 0.05f);

    /// <param name="maxWidth">0 = fill the area; otherwise a centred column (for wide computer windows)</param>
    public AscendBankScreen(RectTransform area, TMP_FontAsset font, float textScale = 1f, float maxWidth = 0f, bool withBackground = true)
    {
        this.font = font; scale = textScale;
        if (withBackground) { var bg = area.gameObject.GetComponent<Image>(); if (!bg) bg = area.gameObject.AddComponent<Image>(); bg.color = Hex("#0A1022"); }

        var sroot = Rect("Bank Scroll", area);
        if (maxWidth > 0f) { sroot.anchorMin = new Vector2(0.5f, 0f); sroot.anchorMax = new Vector2(0.5f, 1f); sroot.sizeDelta = new Vector2(maxWidth, 0f); sroot.anchoredPosition = Vector2.zero; }
        else { sroot.anchorMin = Vector2.zero; sroot.anchorMax = Vector2.one; sroot.offsetMin = sroot.offsetMax = Vector2.zero; }
        var hit = sroot.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);
        scroll = sroot.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
        var vp = Rect("Viewport", sroot); vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.offsetMin = vp.offsetMax = Vector2.zero;
        vp.gameObject.AddComponent<RectMask2D>();
        content = Rect("Content", vp);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
        var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(20, 20, 20, 20); v.spacing = 14; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = content;

        var hook = sroot.gameObject.AddComponent<AscendHook>();
        Action mark = () => dirty = true;
        AscendEconomy.Changed += mark;
        hook.onDestroy = () => AscendEconomy.Changed -= mark;
        hook.onEnable = () => dirty = true;
        hook.onUpdate = () => { if (dirty) Rebuild(); };
    }

    public void Rebuild()
    {
        dirty = false;
        for (int i = content.childCount - 1; i >= 0; i--) { var c = content.GetChild(i).gameObject; c.SetActive(false); UnityEngine.Object.Destroy(c); }

        // Balance card
        var card = Img("Card", content, Hex("#0E2A5A"), 26f);
        Le(card, 230);
        var acct = Text(card.rectTransform, "MISSION FUND ACCOUNT", 18, new Color(1f, 1f, 1f, 0.7f), FontStyles.Bold);
        Fill(acct.rectTransform, 28, 0, 28, 24); acct.characterSpacing = 4f;
        var num = Text(card.rectTransform, "**** 2026", 18, new Color(1f, 1f, 1f, 0.55f));
        Fill(num.rectTransform, 28, 0, 28, 24); num.alignment = TextAlignmentOptions.TopRight;
        var bal = Text(card.rectTransform, AscendReport.MoneyFull(AscendEconomy.Balance), 50, Color.white, FontStyles.Bold);
        Fill(bal.rectTransform, 28, 0, 28, 74); bal.enableAutoSizing = true; bal.fontSizeMax = 50f * scale; bal.fontSizeMin = 24f;
        var lab = Text(card.rectTransform, "Available balance", 18, new Color(1f, 1f, 1f, 0.7f));
        Fill(lab.rectTransform, 28, 0, 28, 140);
        var holder = Text(card.rectTransform, "MISSION LEAD   //   ASCEND PHILIPPINES", 15, Gold, FontStyles.Bold);
        Fill(holder.rectTransform, 28, 22, 28, 0); holder.alignment = TextAlignmentOptions.BottomLeft; holder.characterSpacing = 3f;

        if (AscendEconomy.FundHeld && AscendEconomy.PendingFund > 0)
        {
            var pend = Img("Pending", content, new Color(1f, 0.78f, 0.31f, 0.12f), 18f);
            Le(pend, 100);
            var pt = Text(pend.rectTransform, "<b>Coming soon: " + AscendReport.MoneyFull(AscendEconomy.PendingFund) + "</b>\n<size=85%>Your mission money arrives after the briefing in the Meeting Room.</size>", 19, Gold);
            Fill(pt.rectTransform, 20, 12, 20, 12); pt.alignment = TextAlignmentOptions.MidlineLeft; pt.lineSpacing = 10f;
        }

        var head = Text(content, "RECENT ACTIVITY", 18, Sub, FontStyles.Bold);
        head.characterSpacing = 4f;
        var ledger = AscendEconomy.Ledger.AsEnumerable().Reverse().Take(30).ToList();
        if (ledger.Count == 0) Text(content, "No money in or out yet.", 21, Sub);
        foreach (var e in ledger)
        {
            var row = Img("Tx", content, RowBg, 16f);
            Le(row, 88);
            var d = Text(row.rectTransform, "<noparse>" + e.text + "</noparse>\n<size=85%><color=#9AA0A6>" + e.date + "</color></size>", 19, Txt);
            Fill(d.rectTransform, 18, 8, 190, 8); d.alignment = TextAlignmentOptions.MidlineLeft; d.lineSpacing = 8f;
            var a = Text(row.rectTransform, (e.amount >= 0 ? "+" : "-") + AscendReport.Money(Math.Abs(e.amount)), 22, e.amount >= 0 ? Hex("#81C995") : Hex("#F28B82"), FontStyles.Bold);
            Fill(a.rectTransform, 18, 8, 18, 8); a.alignment = TextAlignmentOptions.MidlineRight;
        }
        var foot = Text(content, "AscendCAD pays for your rocket parts from this account.", 17, Sub);
        foot.alignment = TextAlignmentOptions.Center;

        if (content.gameObject.activeInHierarchy)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1f;
        }
    }

    // ───────── helpers ─────────
    static RectTransform Rect(string n, Transform p) { var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false); return (RectTransform)go.transform; }
    static void Fill(RectTransform r, float l, float b, float rt, float t) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rt, -t); }
    static void Le(Component c, float h) { var le = c.gameObject.AddComponent<LayoutElement>(); le.minHeight = h; }

    static Image Img(string n, Transform p, Color c, float radius)
    {
        var img = Rect(n, p).gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        if (radius > 0f) { img.sprite = PhoneArt.Round; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = PhoneArt.RoundRadius / radius; }
        return img;
    }

    TextMeshProUGUI Text(Transform p, string s, float size, Color c, FontStyles st = FontStyles.Normal)
    {
        var t = Rect("Text", p).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = s; t.fontSize = size * scale; t.color = c; t.fontStyle = st; t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal; t.alignment = TextAlignmentOptions.TopLeft;
        return t;
    }
}
