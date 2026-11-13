using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

public class ComputerInteraction : MonoBehaviour
{
    [Serializable]
    public class DesktopApp
    {
        public string name = "App";
        [Tooltip("Drag the app's icon sprite here. Empty = colored square with first letter.")]
        public Sprite icon;
        [Tooltip("Optional: UI prefab shown inside the app window. Empty = placeholder.")]
        public GameObject windowContent;
        public Color fallbackColor = new Color(0.2f, 0.45f, 0.9f);

        public DesktopApp() { }
        public DesktopApp(string name, Color color) { this.name = name; fallbackColor = color; }
    }

    [Header("Desktop (auto-created if Computer Canvas is empty)")]
    [Tooltip("Optional: your own desktop UI (Screen Space - Overlay). Leave empty to auto-build one.")]
    public GameObject computerCanvas;
    public Sprite wallpaper;
    public Color wallpaperFallbackColor = new Color(0.08f, 0.12f, 0.2f);

    [Header("Apps (double-click to open)")]
    public DesktopApp[] apps =
    {
        new DesktopApp("AscendCAD", new Color(0.95f, 0.45f, 0.15f)),
        new DesktopApp("Ascend Rocket Trajectory Calculator", new Color(0.2f, 0.5f, 0.95f)),
        new DesktopApp("Googel", new Color(0.25f, 0.7f, 0.35f)),
        new DesktopApp("PhMail", new Color(0.05f, 0.34f, 0.82f)),
    };
    public Vector2 appWindowSize = new Vector2(1100f, 650f);
    public Color windowColor = new Color(0.13f, 0.13f, 0.15f, 1f);
    public Color titleBarColor = new Color(0.2f, 0.2f, 0.23f, 1f);

    [Header("Googel (drag your Googel Data asset into Googel > Data)")]
    public GoogelSettings googel = new GoogelSettings();

    [Header("AscendCAD + PhMail (drag your AscendCAD Data asset here)")]
    [Tooltip("Create > AscendCAD > AscendCAD Data. Empty = built-in default parts.")]
    public AscendCADData ascendCAD;
    [Tooltip("Must match the app name in Apps.")]
    public string ascendCADAppName = "AscendCAD";
    [Tooltip("Must match the app name in Apps. Added automatically if missing.")]
    public string mailAppName = "PhMail";
    [Tooltip("Open AscendCAD full screen the first time (it needs the room).")]
    public bool maximizeAscendCAD = true;

    [Header("Taskbar")]
    public float taskbarHeight = 48f;
    public Color taskbarColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
    [Tooltip("Optional icon overrides. Empty = built-in icons.")]
    public Sprite powerIcon;
    public Sprite wifiIcon;
    public Sprite batteryIcon;

    [Header("Exit Hint (top-left)")]
    public string exitHintText = "Press E to exit";
    public float exitHintSize = 24f;
    public Vector2 exitHintOffset = new Vector2(12f, -12f);

    [Header("Interact Prompt (auto-created)")]
    public string promptText = "Press E to interact";
    public TMP_FontAsset promptFont;    // None = TextMeshPro default font (used for all computer text)
    public float fontSize = 28f;
    public Color textColor = Color.white;
    [Range(-1f, 1f)] public float faceThickness = 0f; // negative = thinner text
    public Vector2 screenOffset = new Vector2(0f, 120f); // pixels up from bottom center

    [Header("Settings")]
    public float interactDistance = 3f;
    public LayerMask interactMask = ~0;
    [Tooltip("Seconds between 'am I looking at the computer' checks. Higher = cheaper.")]
    [Range(0f, 0.25f)] public float lookCheckInterval = 0.05f;

    [Header("While Using the Computer")]
    [Tooltip("Scripts to disable: player movement, mouse look, etc.")]
    public MonoBehaviour[] disableWhileOpen;
    [Tooltip("Objects to hide: crosshair, player HUD, etc.")]
    public GameObject[] hideWhileOpen;

    // Add your own desktop UI under this at runtime (auto-built desktop only)
    public RectTransform DesktopArea { get; private set; }
    public bool IsOpen => isOpen;

    const int DesktopSortOrder = 999;
    const int HintSortOrder = 1000;
    const float TitleBarHeight = 36f;
    const float TitleButtonWidth = 48f;

    Camera cam;
    bool isOpen;
    bool promptVisible;
    bool looking;
    float lookTimer;
    CursorLockMode previousLock;
    bool previousVisible;
    bool[] hiddenPrevState;

    GameObject promptRoot;
    TextMeshProUGUI promptLabel;

    GameObject desktopRoot;     // auto-built desktop
    Canvas desktopCanvas;
    RectTransform windowLayer;
    RectTransform[] windows;
    GameObject[] taskbarIndicators;
    int cascade;

    // Maximize / restore state per window
    bool[] maximized;
    Vector2[] restorePositions;
    Image[] maximizeIcons;

    GoogelScreen googelScreen;
    AscendCADScreen cadScreen;
    AscendMailScreen mailScreen;

    // New-mail toast
    GameObject toastRoot;
    TextMeshProUGUI toastLabel;
    float toastTimer;
    bool mailHooked;

    GameObject hintRoot;
    TextMeshProUGUI hintLabel;
    TextMeshProUGUI timeLabel, dateLabel;
    float clockTimer;

    // ───────────────────────────── Lifecycle ─────────────────────────────

    void Start()
    {
        cam = Camera.main;
        if (computerCanvas) computerCanvas.SetActive(false);
        EnsureMailApp();
        CreatePrompt();
    }

    void OnDisable()
    {
        if (isOpen) Close();
        SetPrompt(false);
    }

    void OnDestroy()
    {
        if (mailHooked) AscendMail.Received -= OnMailReceived;
        if (promptRoot) Destroy(promptRoot);
        if (desktopRoot) Destroy(desktopRoot);
        if (hintRoot) Destroy(hintRoot);
    }

    // Lets you tweak values live in the Inspector during Play mode
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            // Show PhMail in the Inspector's Apps list (so its icon etc. can be set) even before pressing Play
            int before = apps != null ? apps.Length : 0;
            EnsureMailApp();
#if UNITY_EDITOR
            if (apps != null && apps.Length != before) UnityEditor.EditorUtility.SetDirty(this);
#endif
            return;
        }
        ApplyPromptStyle();
        if (hintLabel)
        {
            hintLabel.text = exitHintText;
            hintLabel.fontSize = exitHintSize;
            ((RectTransform)hintLabel.transform.parent).anchoredPosition = exitHintOffset;
        }
    }

    void Update()
    {
        if (isOpen)
        {
            if (Pressed(KeyCode.Escape) || (Pressed(KeyCode.E) && !IsTyping())) { Close(); return; }
            UpdateClock();
            UpdateToast();
            return;
        }

        if (cam == null) { cam = Camera.main; return; }

        // The raycast doesn't need to run every single frame
        lookTimer -= Time.unscaledDeltaTime;
        if (lookTimer <= 0f)
        {
            lookTimer = lookCheckInterval;
            looking = IsLookingAtMe();
            SetPrompt(looking);
        }

        if (looking && Pressed(KeyCode.E)) Open();
    }

    // Older scenes saved the Apps list before PhMail existed: add it so nobody has to edit the Inspector
    void EnsureMailApp()
    {
        if (string.IsNullOrEmpty(mailAppName)) return;
        if (apps == null) apps = new DesktopApp[0];
        foreach (var a in apps) if (IsApp(a, mailAppName)) return;
        var list = new List<DesktopApp>(apps) { new DesktopApp(mailAppName, new Color(0.05f, 0.34f, 0.82f)) };
        apps = list.ToArray();
    }

    // ───────────────────────────── Open / Close ─────────────────────────────

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        SetPrompt(false);

        // Cursor first, so the player is never stuck without a mouse if something below fails
        previousLock = Cursor.lockState;
        previousVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureEventSystem();

        // Desktop
        if (computerCanvas) computerCanvas.SetActive(true);
        else
        {
            if (!desktopRoot) BuildDesktop();
            desktopRoot.SetActive(true);
            clockTimer = 0f;
            UpdateClock();
        }

        // Exit hint
        if (!hintRoot) BuildExitHint();
        hintRoot.SetActive(true);

        // Freeze player
        foreach (var s in disableWhileOpen) if (s) s.enabled = false;

        // Hide crosshair / HUD (remember state so we restore exactly)
        if (hideWhileOpen != null)
        {
            hiddenPrevState = new bool[hideWhileOpen.Length];
            for (int i = 0; i < hideWhileOpen.Length; i++)
            {
                var go = hideWhileOpen[i];
                if (!go) continue;
                hiddenPrevState[i] = go.activeSelf;
                go.SetActive(false);
            }
        }

    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        if (computerCanvas) computerCanvas.SetActive(false);
        if (desktopRoot) desktopRoot.SetActive(false);
        if (hintRoot) hintRoot.SetActive(false);

        // Drop UI focus so keys don't keep going to a desktop input field
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);

        foreach (var s in disableWhileOpen) if (s) s.enabled = true;

        if (hideWhileOpen != null && hiddenPrevState != null)
            for (int i = 0; i < hideWhileOpen.Length && i < hiddenPrevState.Length; i++)
                if (hideWhileOpen[i]) hideWhileOpen[i].SetActive(hiddenPrevState[i]);

        Cursor.lockState = previousLock;
        Cursor.visible = previousVisible;
    }

    // ───────────────────────────── Apps ─────────────────────────────

    public void OpenApp(int index)
    {
        if (windows == null || index < 0 || index >= windows.Length) return;
        bool first = !windows[index];
        if (first) windows[index] = BuildWindow(index);

        windows[index].gameObject.SetActive(true);
        windows[index].SetAsLastSibling();
        if (taskbarIndicators[index]) taskbarIndicators[index].SetActive(true);

        if (first && maximizeAscendCAD && IsApp(apps[index], ascendCADAppName)) SetMaximized(index, true);
    }

    public void OpenAppByName(string appName)
    {
        for (int i = 0; i < apps.Length; i++) if (IsApp(apps[i], appName)) { OpenApp(i); return; }
    }

    public void CloseApp(int index)
    {
        if (windows == null || index < 0 || index >= windows.Length || !windows[index]) return;
        windows[index].gameObject.SetActive(false);
        if (taskbarIndicators[index]) taskbarIndicators[index].SetActive(false);
    }

    // Full screen <-> small window (button in the title bar, or double-click the title bar)
    public void ToggleMaximize(int index)
    {
        if (maximized == null || index < 0 || index >= maximized.Length) return;
        SetMaximized(index, !maximized[index]);
    }

    public void SetMaximized(int index, bool on)
    {
        if (windows == null || index < 0 || index >= windows.Length) return;
        var win = windows[index];
        if (!win || maximized[index] == on) return;
        maximized[index] = on;

        if (on)
        {
            restorePositions[index] = win.anchoredPosition;
            win.anchorMin = Vector2.zero;
            win.anchorMax = Vector2.one;
            win.offsetMin = win.offsetMax = Vector2.zero;
        }
        else
        {
            win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
            win.sizeDelta = appWindowSize;
            win.anchoredPosition = restorePositions[index];
        }

        if (maximizeIcons[index]) maximizeIcons[index].sprite = on ? RestoreSprite : MaximizeSprite;
        win.SetAsLastSibling();
    }

    static bool IsApp(DesktopApp app, string appName) =>
        app != null && !string.IsNullOrEmpty(appName) &&
        string.Equals((app.name ?? "").Trim(), appName.Trim(), StringComparison.OrdinalIgnoreCase);

    // ───────────────────────────── Interact Prompt ─────────────────────────────

    void CreatePrompt()
    {
        promptRoot = CreateOverlayCanvas("InteractPrompt_" + name, 100);

        promptLabel = CreateText("Text", promptRoot.transform, promptText, fontSize);
        promptLabel.alignment = TextAlignmentOptions.Center;

        var rt = promptLabel.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(800f, 100f);

        ApplyPromptStyle();
        promptRoot.SetActive(false);
        promptVisible = false;
    }

    void ApplyPromptStyle()
    {
        if (!promptLabel) return;
        promptLabel.text = promptText;
        promptLabel.fontSize = fontSize;
        promptLabel.color = textColor;
        promptLabel.fontStyle = FontStyles.Normal;
        if (promptFont) promptLabel.font = promptFont;
        promptLabel.rectTransform.anchoredPosition = screenOffset;
        promptLabel.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, faceThickness);
    }

    void SetPrompt(bool show)
    {
        if (promptVisible == show || !promptRoot) return;
        promptVisible = show;
        promptRoot.SetActive(show);
    }

    // ───────────────────────────── Desktop UI ─────────────────────────────

    void BuildDesktop()
    {
        desktopRoot = CreateOverlayCanvas("Desktop_" + name, DesktopSortOrder);
        desktopCanvas = desktopRoot.GetComponent<Canvas>();
        desktopRoot.AddComponent<GraphicRaycaster>();
        var root = desktopRoot.transform;

        // Background color (also shows behind wallpaper edges)
        var bg = CreateRect("Background", root);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = wallpaperFallbackColor;

        // Wallpaper: fills the screen without stretching (crops like a real desktop)
        if (wallpaper)
        {
            var wp = CreateRect("Wallpaper", bg);
            Stretch(wp);
            var img = wp.gameObject.AddComponent<Image>();
            img.sprite = wallpaper;
            img.raycastTarget = false;

            var fit = wp.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = wallpaper.rect.width / wallpaper.rect.height;
        }

        // Icons area (above taskbar)
        DesktopArea = CreateRect("DesktopArea", root);
        Stretch(DesktopArea);
        DesktopArea.offsetMin = new Vector2(0f, taskbarHeight);
        BuildDesktopIcons();

        // App windows sit above icons, below taskbar
        windowLayer = CreateRect("Windows", root);
        Stretch(windowLayer);
        windowLayer.offsetMin = new Vector2(0f, taskbarHeight);
        windows = new RectTransform[apps.Length];
        maximized = new bool[apps.Length];
        restorePositions = new Vector2[apps.Length];
        maximizeIcons = new Image[apps.Length];

        BuildTaskbar(root);
        BuildToast(root);

        // Economy + welcome email exist from the first boot
        try
        {
            var cadData = AscendCADData.Resolve(ascendCAD);
            AscendEconomy.Init(cadData);
            if (!mailHooked) { AscendMail.Received += OnMailReceived; mailHooked = true; }
            AscendMail.EnsureWelcome(cadData);
        }
        catch (Exception e) { Debug.LogException(e); }

        desktopRoot.SetActive(false);
    }

    void BuildDesktopIcons()
    {
        const float cellW = 120f, cellH = 128f, iconSize = 60f;

        // Start the icons below the "Press E to exit" hint (pill = text + padding)
        float hintBottom = Mathf.Abs(exitHintOffset.y) + exitHintSize * 1.3f + 12f;
        float top = hintBottom + 12f;

        for (int i = 0; i < apps.Length; i++)
        {
            int index = i;
            var app = apps[i];

            var cell = CreateRect(app.name, DesktopArea);
            cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f);
            cell.pivot = new Vector2(0f, 1f);
            cell.sizeDelta = new Vector2(cellW, cellH);
            cell.anchoredPosition = new Vector2(16f, -top - i * (cellH + 8f));

            // Hover / selected highlight (single click selects, like Windows)
            var bg = cell.gameObject.AddComponent<Image>();
            bg.sprite = RoundedSprite;
            bg.type = Image.Type.Sliced;
            var btn = cell.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.colors = HoverColors(new Color(1f, 1f, 1f, 0.15f), new Color(0.55f, 0.75f, 1f, 0.3f));

            // Double-click opens
            AddTrigger(cell.gameObject, EventTriggerType.PointerClick, e =>
            {
                if (((PointerEventData)e).clickCount >= 2) OpenApp(index);
            });

            var icon = CreateAppIcon(app, cell, iconSize);
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 1f);
            icon.pivot = new Vector2(0.5f, 1f);
            icon.anchoredPosition = new Vector2(0f, -8f);

            var label = CreateText("Label", cell, app.name, 15f);
            label.alignment = TextAlignmentOptions.Top;
            label.overflowMode = TextOverflowModes.Truncate;
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(-8f, 56f);
            lrt.anchoredPosition = new Vector2(0f, 4f);

            // Soft shadow so names stay readable on any wallpaper
            var mat = label.fontMaterial;
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.8f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
        }
    }

    void BuildTaskbar(Transform root)
    {
        float btnSize = taskbarHeight - 8f;

        var bar = CreateRect("Taskbar", root);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(0f, taskbarHeight);
        bar.gameObject.AddComponent<Image>().color = taskbarColor;

        // ── Left: power button + pinned apps ──
        var left = CreateRow("Left", bar, TextAnchor.MiddleLeft, 4f);
        left.anchorMin = new Vector2(0f, 0f);
        left.anchorMax = new Vector2(0f, 1f);
        left.pivot = new Vector2(0f, 0.5f);
        left.anchoredPosition = new Vector2(6f, 0f);

        var power = CreateTaskbarButton(left, "Power", btnSize, Close);
        AddIcon(power, powerIcon ? powerIcon : PowerSprite, btnSize * 0.5f, btnSize * 0.5f);

        taskbarIndicators = new GameObject[apps.Length];
        for (int i = 0; i < apps.Length; i++)
        {
            int index = i;
            var b = CreateTaskbarButton(left, apps[i].name, btnSize, () => OpenApp(index));
            CreateAppIcon(apps[i], b, btnSize - 14f);

            // Little line under open apps
            var dot = CreateRect("OpenIndicator", b);
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0f);
            dot.pivot = new Vector2(0.5f, 0f);
            dot.sizeDelta = new Vector2(14f, 3f);
            dot.anchoredPosition = new Vector2(0f, 1f);
            var dotImg = dot.gameObject.AddComponent<Image>();
            dotImg.color = new Color(0.55f, 0.75f, 1f);
            dotImg.raycastTarget = false;
            dot.gameObject.SetActive(false);
            taskbarIndicators[i] = dot.gameObject;
        }

        // ── Right: wifi, battery, clock ──
        var tray = CreateRow("Tray", bar, TextAnchor.MiddleRight, 12f);
        tray.anchorMin = new Vector2(1f, 0f);
        tray.anchorMax = new Vector2(1f, 1f);
        tray.pivot = new Vector2(1f, 0.5f);
        tray.anchoredPosition = new Vector2(-14f, 0f);

        AddTrayIcon(tray, wifiIcon ? wifiIcon : WifiSprite, 22f, 22f);

        var battery = CreateRow("Battery", tray, TextAnchor.MiddleLeft, 4f);
        AddTrayIcon(battery, batteryIcon ? batteryIcon : BatterySprite, 26f, 26f);
        var pct = CreateText("Percent", battery, "100%", 15f);
        pct.textWrappingMode = TextWrappingModes.NoWrap;

        var clock = CreateRect("Clock", tray);
        var vl = clock.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.childAlignment = TextAnchor.MiddleRight;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = vl.childForceExpandHeight = false;

        timeLabel = CreateText("Time", clock, "", 15f);
        dateLabel = CreateText("Date", clock, "", 13f);
        foreach (var t in new[] { timeLabel, dateLabel })
        {
            t.alignment = TextAlignmentOptions.Right;
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    // Small "New email" pill (top-right). Click it to open PhMail.
    void BuildToast(Transform root)
    {
        var pill = CreateRect("MailToast", root);
        pill.anchorMin = pill.anchorMax = Vector2.one;
        pill.pivot = Vector2.one;
        pill.anchoredPosition = new Vector2(-16f, -16f);

        var img = pill.gameObject.AddComponent<Image>();
        img.sprite = RoundedSprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(0.05f, 0.34f, 0.82f, 0.95f);
        var btn = pill.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() => { OpenAppByName(mailAppName); toastTimer = 0f; toastRoot.SetActive(false); });

        var layout = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 10, 10);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        toastLabel = CreateText("Text", pill, "", 16f);
        toastLabel.textWrappingMode = TextWrappingModes.NoWrap;
        toastRoot = pill.gameObject;
        toastRoot.SetActive(false);
    }

    void OnMailReceived(AscendMail.Message m)
    {
        if (!toastLabel) return;
        string subject = m.subject.Length > 60 ? m.subject.Substring(0, 57) + "..." : m.subject;
        toastLabel.text = "<b>New email</b>  <noparse>" + subject + "</noparse>  <size=85%>(click to open)</size>";
        toastRoot.SetActive(true);
        toastTimer = 7f;   // only counts down while the computer is open, so you see it when you come back
    }

    void UpdateToast()
    {
        if (toastTimer <= 0f) return;
        toastTimer -= Time.unscaledDeltaTime;
        if (toastTimer <= 0f && toastRoot) toastRoot.SetActive(false);
    }

    RectTransform BuildWindow(int index)
    {
        var app = apps[index];

        var win = CreateRect(app.name + " Window", windowLayer);
        win.sizeDelta = appWindowSize;
        win.anchoredPosition = new Vector2(-60f, 60f) + new Vector2(30f, -30f) * (cascade++ % 5);
        win.gameObject.AddComponent<Image>().color = windowColor;
        AddTrigger(win.gameObject, EventTriggerType.PointerDown, _ => win.SetAsLastSibling());

        // Title bar (drag to move, double-click to maximize / restore)
        var bar = CreateRect("TitleBar", win);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, TitleBarHeight);
        bar.gameObject.AddComponent<Image>().color = titleBarColor;
        AddTrigger(bar.gameObject, EventTriggerType.PointerDown, _ => win.SetAsLastSibling());
        AddTrigger(bar.gameObject, EventTriggerType.Drag, e => DragWindow(index, (PointerEventData)e));
        AddTrigger(bar.gameObject, EventTriggerType.PointerClick, e =>
        {
            if (((PointerEventData)e).clickCount == 2) ToggleMaximize(index);
        });

        var icon = CreateAppIcon(app, bar, 20f);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.anchoredPosition = new Vector2(20f, 0f);

        var title = CreateText("Title", bar, app.name, 15f);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Truncate;
        var trt = title.rectTransform;
        Stretch(trt);
        trt.offsetMin = new Vector2(40f, 0f);
        trt.offsetMax = new Vector2(-TitleButtonWidth * 2f - 8f, 0f);

        // Close button (far right)
        var close = CreateTitleBarButton(bar, "Close", 0f,
            new Color(0.9f, 0.15f, 0.15f, 1f), new Color(0.7f, 0.1f, 0.1f, 1f), () => CloseApp(index));
        var x = CreateText("X", close, "X", 16f);
        Stretch(x.rectTransform);
        x.alignment = TextAlignmentOptions.Center;

        // Maximize / restore button (left of close)
        var max = CreateTitleBarButton(bar, "Maximize", -TitleButtonWidth,
            new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.2f), () => ToggleMaximize(index));
        var maxIcon = CreateRect("Icon", max);
        maxIcon.sizeDelta = new Vector2(13f, 13f);
        var maxImg = maxIcon.gameObject.AddComponent<Image>();
        maxImg.sprite = MaximizeSprite;
        maxImg.raycastTarget = false;
        maximizeIcons[index] = maxImg;

        // Body
        var body = CreateRect("Body", win);
        Stretch(body);
        body.offsetMax = new Vector2(0f, -TitleBarHeight);
        body.gameObject.AddComponent<RectMask2D>();

        if (app.windowContent)
        {
            var content = Instantiate(app.windowContent, body, false);
            if (content.transform is RectTransform crt) Stretch(crt);
        }
        else if (IsGoogel(app))
        {
            googelScreen = new GoogelScreen(googel, promptFont, body);
        }
        else if (IsApp(app, ascendCADAppName))
        {
            cadScreen = new AscendCADScreen(ascendCAD, promptFont, body);
        }
        else if (IsApp(app, mailAppName))
        {
            mailScreen = new AscendMailScreen(ascendCAD, promptFont, body);
        }
        else
        {
            var placeholder = CreateText("Placeholder", body, app.name + "\n<size=60%><color=#888>Coming soon</color></size>", 36f);
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.Center;
        }

        return win;
    }

    RectTransform CreateTitleBarButton(RectTransform bar, string goName, float x, Color hover, Color pressed, Action onClick)
    {
        var rt = CreateRect(goName, bar);
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(TitleButtonWidth, 0f);
        rt.anchoredPosition = new Vector2(x, 0f);

        var img = rt.gameObject.AddComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = HoverColors(hover, new Color(1f, 1f, 1f, 0f));
        cb.pressedColor = pressed;
        btn.colors = cb;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() => onClick());
        return rt;
    }

    void DragWindow(int index, PointerEventData e)
    {
        if (maximized[index]) return; // full-screen windows stay put
        var win = windows[index];
        if (!win) return;

        var p = win.anchoredPosition + e.delta / desktopCanvas.scaleFactor;

        // Keep the title bar reachable
        Vector2 half = windowLayer.rect.size * 0.5f;
        Vector2 size = win.rect.size;
        p.x = Mathf.Clamp(p.x, -half.x - size.x * 0.5f + 80f, half.x + size.x * 0.5f - 80f);
        p.y = Mathf.Clamp(p.y, -half.y - size.y * 0.5f + TitleBarHeight, half.y - size.y * 0.5f);
        win.anchoredPosition = p;
    }

    void BuildExitHint()
    {
        hintRoot = CreateOverlayCanvas("ExitHint_" + name, HintSortOrder);

        // Small dark pill that sizes itself to the text
        var pill = CreateRect("Pill", hintRoot.transform);
        pill.anchorMin = pill.anchorMax = new Vector2(0f, 1f);
        pill.pivot = new Vector2(0f, 1f);
        pill.anchoredPosition = exitHintOffset;

        var pillImg = pill.gameObject.AddComponent<Image>();
        pillImg.sprite = RoundedSprite;
        pillImg.type = Image.Type.Sliced;
        pillImg.color = new Color(0f, 0f, 0f, 0.45f);
        pillImg.raycastTarget = false;

        var layout = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 6, 6);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        hintLabel = CreateText("Text", pill, exitHintText, exitHintSize);
        hintLabel.color = new Color(1f, 1f, 1f, 0.85f);
        hintLabel.alignment = TextAlignmentOptions.MidlineLeft;
        hintLabel.textWrappingMode = TextWrappingModes.NoWrap;

        hintRoot.SetActive(false);
    }

    void UpdateClock()
    {
        if (!timeLabel) return;
        clockTimer -= Time.unscaledDeltaTime;
        if (clockTimer > 0f) return;
        clockTimer = 1f;

        var now = DateTime.Now;
        timeLabel.text = now.ToString("h:mm tt");
        dateLabel.text = now.ToString("M/d/yyyy");
    }

    // ───────────────────────────── UI Helpers ─────────────────────────────

    GameObject CreateOverlayCanvas(string goName, int order)
    {
        var go = new GameObject(goName);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return go;
    }

    TextMeshProUGUI CreateText(string goName, Transform parent, string text, float size)
    {
        var go = new GameObject(goName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = Color.white;
        t.raycastTarget = false;
        if (promptFont) t.font = promptFont;
        return t;
    }

    RectTransform CreateAppIcon(DesktopApp app, Transform parent, float size)
    {
        var rt = CreateRect("Icon", parent);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = false;

        if (app.icon)
        {
            img.sprite = app.icon;
            img.preserveAspect = true;
        }
        else
        {
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = app.fallbackColor;

            string letter = string.IsNullOrEmpty(app.name) ? "?" : app.name.Substring(0, 1).ToUpper();
            var t = CreateText("Letter", rt, letter, size * 0.5f);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
        }
        return rt;
    }

    RectTransform CreateTaskbarButton(Transform parent, string goName, float size, Action onClick)
    {
        var rt = CreateRect(goName, parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;

        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = RoundedSprite;
        img.type = Image.Type.Sliced;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.colors = HoverColors(new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0f));
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(() => onClick());
        return rt;
    }

    static void AddIcon(Transform parent, Sprite sprite, float w, float h)
    {
        var rt = CreateRect("Icon", parent);
        rt.sizeDelta = new Vector2(w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    static void AddTrayIcon(Transform parent, Sprite sprite, float w, float h)
    {
        var rt = CreateRect("Icon", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = w;
        le.preferredHeight = h;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    static RectTransform CreateRow(string goName, Transform parent, TextAnchor align, float spacing)
    {
        var rt = CreateRect(goName, parent);
        var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = align;
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        var f = rt.gameObject.AddComponent<ContentSizeFitter>();
        f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    static ColorBlock HoverColors(Color hover, Color selected)
    {
        var c = ColorBlock.defaultColorBlock;
        c.normalColor = new Color(1f, 1f, 1f, 0f);
        c.highlightedColor = hover;
        c.pressedColor = new Color(hover.r, hover.g, hover.b, Mathf.Min(1f, hover.a * 1.6f));
        c.selectedColor = selected;
        c.disabledColor = new Color(1f, 1f, 1f, 0f);
        c.fadeDuration = 0.08f;
        return c;
    }

    static void AddTrigger(GameObject go, EventTriggerType type, Action<BaseEventData> action)
    {
        var trigger = go.GetComponent<EventTrigger>();
        if (!trigger) trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(d => action(d));
        trigger.triggers.Add(entry);
    }

    static RectTransform CreateRect(string goName, Transform parent)
    {
        var go = new GameObject(goName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ───────────────────────────── Built-in Icons (generated once) ─────────────────────────────

    static Sprite roundedSprite, powerSprite, wifiSprite, batterySprite, maximizeSprite, restoreSprite;

    static Sprite RoundedSprite => roundedSprite ? roundedSprite : roundedSprite = MakeSprite(32, (u, v) =>
    {
        const float r = 0.5f;
        float qx = Mathf.Max(Mathf.Abs(u) - (1f - r), 0f);
        float qy = Mathf.Max(Mathf.Abs(v) - (1f - r), 0f);
        return qx * qx + qy * qy <= r * r;
    }, 10f);

    static Sprite PowerSprite => powerSprite ? powerSprite : powerSprite = MakeSprite(64, (u, v) =>
    {
        float d = Mathf.Sqrt(u * u + v * v);
        bool ring = Mathf.Abs(d - 0.62f) < 0.11f && !(v > 0f && Mathf.Abs(u) < 0.32f);
        bool stem = Mathf.Abs(u) < 0.1f && v > -0.05f && v < 0.9f;
        return ring || stem;
    });

    static Sprite WifiSprite => wifiSprite ? wifiSprite : wifiSprite = MakeSprite(64, (u, v) =>
    {
        float cx = u, cy = v + 0.75f;
        float d = Mathf.Sqrt(cx * cx + cy * cy);
        if (d < 0.17f) return true;
        if (cy <= 0f || Mathf.Abs(cx) > cy) return false; // 90° fan
        return Mathf.Abs(d - 0.55f) < 0.1f || Mathf.Abs(d - 0.95f) < 0.1f || Mathf.Abs(d - 1.35f) < 0.1f;
    });

    static Sprite BatterySprite => batterySprite ? batterySprite : batterySprite = MakeSprite(64, (u, v) =>
    {
        bool outer = u > -0.92f && u < 0.72f && Mathf.Abs(v) < 0.42f;
        bool hole  = u > -0.8f  && u < 0.6f  && Mathf.Abs(v) < 0.3f;
        bool fill  = u > -0.7f  && u < 0.5f  && Mathf.Abs(v) < 0.2f;   // always full = 100%
        bool tip   = u >= 0.72f && u < 0.9f  && Mathf.Abs(v) < 0.17f;
        return (outer && !hole) || fill || tip;
    });

    // Single square outline = "make full screen"
    static Sprite MaximizeSprite => maximizeSprite ? maximizeSprite : maximizeSprite = MakeSprite(64, (u, v) =>
    {
        bool outer = Mathf.Abs(u) < 0.85f && Mathf.Abs(v) < 0.85f;
        bool inner = Mathf.Abs(u) < 0.66f && Mathf.Abs(v) < 0.66f;
        return outer && !inner;
    });

    // Two overlapping squares = "back to small window"
    static Sprite RestoreSprite => restoreSprite ? restoreSprite : restoreSprite = MakeSprite(64, (u, v) =>
    {
        bool Box(float cx, float cy, float h) => Mathf.Abs(u - cx) < h && Mathf.Abs(v - cy) < h;
        bool front = Box(-0.2f, -0.2f, 0.62f) && !Box(-0.2f, -0.2f, 0.43f);
        bool back  = Box(0.2f, 0.2f, 0.62f) && !Box(0.2f, 0.2f, 0.43f) && !Box(-0.2f, -0.2f, 0.62f);
        return front || back;
    });

    // Draws a white anti-aliased shape; inside(u, v) uses coords from -1 to 1
    static Sprite MakeSprite(int size, Func<float, float, bool> inside, float border = 0f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        const int ss = 4; // supersampling for smooth edges
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float u = (x + (sx + 0.5f) / ss) / size * 2f - 1f;
                float v = (y + (sy + 0.5f) / ss) / size * 2f - 1f;
                if (inside(u, v)) hits++;
            }
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / (ss * ss)));
        }
        tex.SetPixels32(px);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    // ───────────────────────────── Input / Raycast ─────────────────────────────

    static void EnsureEventSystem()
    {
        if (EventSystem.current || FindAnyObjectByType<EventSystem>()) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    // Don't close the computer when typing an "e" into a desktop text field
    static bool IsTyping()
    {
        var es = EventSystem.current;
        if (!es || !es.currentSelectedGameObject) return false;
        var sel = es.currentSelectedGameObject;
        var tmp = sel.GetComponent<TMP_InputField>();
        if (tmp && tmp.isFocused) return true;
        var legacy = sel.GetComponent<InputField>();
        return legacy && legacy.isFocused;
    }

    bool IsLookingAtMe()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.IsChildOf(transform);
        return false;
    }

    bool Pressed(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        if (key == KeyCode.E) return Keyboard.current.eKey.wasPressedThisFrame;
        if (key == KeyCode.Escape) return Keyboard.current.escapeKey.wasPressedThisFrame;
        return false;
#else
        return Input.GetKeyDown(key);
#endif
    }

    // ═════════════════════════════════════════════════════════════════════════════
    //  GOOGEL - built automatically inside the "Googel" app window.
    //  All content comes from the Googel Data asset in the "Googel" section.
    // ═════════════════════════════════════════════════════════════════════════════

    [Serializable]
    public class GoogelSettings
    {
        [Tooltip("The desktop app that opens Googel (must match the name in Apps).")]
        public string appName = "Googel";
        [Tooltip("Drag your Googel Data asset here (Create > Googel > Googel Data). Empty = built-in rocket parts.")]
        public GoogelData data;
        [Tooltip("The 'G' icon shown before 'oogel'. Empty = a big blue letter G.")]
        public Sprite logoIcon;
        public string searchPlaceholder = "Search Googel or type a rocket part";
        public string notFoundMessage = ":(( sorry we dont have info on that";
        [Tooltip("On = built-in dark theme. Off = uses the colors below.")]
        public bool darkMode = true;
        [Header("Light theme colors (only used when Dark Mode is off)")]
        public Color pageColor = Color.white;
        public Color tabBarColor = new Color(0.87f, 0.89f, 0.92f);
        public Color cardColor = new Color(0.96f, 0.97f, 0.98f);
        public Color textColor = new Color(0.12f, 0.13f, 0.15f);
        public Color subTextColor = new Color(0.36f, 0.38f, 0.42f);
        public Color linkColor = new Color(0.10f, 0.45f, 0.91f);
        [Tooltip("X = smallest card width (cards stretch to fill each row). Y = card height.")]
        public Vector2 cardSize = new Vector2(250f, 340f);
        public float scrollSensitivity = 40f;
    }

    bool IsGoogel(DesktopApp app) =>
        app != null && string.Equals((app.name ?? "").Trim(), (googel.appName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    class GoogelScreen
    {
        readonly RectTransform root;
        readonly GoogelData data;
        readonly Sprite logoIcon;
        readonly TMP_FontAsset font;
        readonly string searchPlaceholder, notFoundMessage;
        readonly Color pageColor, tabBarColor, cardColor, textColor, subTextColor, linkColor;
        readonly Color searchBg, searchBorder, frameColor, divider, hoverTint, chipColor, scrollTrack, scrollHandle, onLink, shadowColor;
        readonly Vector2 cardSize;
        readonly float scrollSensitivity;

        public GoogelScreen(GoogelSettings s, TMP_FontAsset font, RectTransform root)
        {
            this.root = root;
            this.font = font;
            data = s.data;
            logoIcon = s.logoIcon;
            searchPlaceholder = s.searchPlaceholder;
            notFoundMessage = s.notFoundMessage;
            if (s.darkMode)
            {
                pageColor    = C("#202124");
                tabBarColor  = C("#121314");
                cardColor    = C("#2A2B2F");
                textColor    = C("#E8EAED");
                subTextColor = C("#9AA0A6");
                linkColor    = C("#8AB4F8");
                searchBg     = C("#303134");
                searchBorder = C("#5F6368");
                frameColor   = C("#35363A");
                chipColor    = C("#303134");
                onLink       = C("#202124");
                divider      = new Color(1f, 1f, 1f, 0.08f);
                hoverTint    = new Color(1f, 1f, 1f, 0.08f);
                scrollTrack  = new Color(1f, 1f, 1f, 0.04f);
                scrollHandle = new Color(1f, 1f, 1f, 0.25f);
                shadowColor  = new Color(0f, 0f, 0f, 0.35f);
            }
            else
            {
                pageColor    = s.pageColor;
                tabBarColor  = s.tabBarColor;
                cardColor    = s.cardColor;
                textColor    = s.textColor;
                subTextColor = s.subTextColor;
                linkColor    = s.linkColor;
                searchBg     = Color.white;
                searchBorder = new Color(0.80f, 0.82f, 0.86f);
                frameColor   = new Color(0.91f, 0.92f, 0.94f);
                chipColor    = new Color(0.91f, 0.94f, 0.99f);
                onLink       = Color.white;
                divider      = new Color(0f, 0f, 0f, 0.08f);
                hoverTint    = new Color(0f, 0f, 0f, 0.06f);
                scrollTrack  = new Color(0f, 0f, 0f, 0.04f);
                scrollHandle = new Color(0f, 0f, 0f, 0.25f);
                shadowColor  = new Color(0f, 0f, 0f, 0.12f);
            }
            cardSize = s.cardSize;
            scrollSensitivity = s.scrollSensitivity;

            LoadData();
            BuildBrowser();
        }

        const float TabBarHeight = 40f;

        class Tab
        {
            public string key;
            public GameObject button, page;
            public Image bg;
            public TextMeshProUGUI label;
        }

        readonly List<Tab> tabs = new List<Tab>();
        Tab homeTab, activeTab;
        RectTransform tabStrip, pagesRoot, homeContent, resultsGrid;
        GameObject categoriesRoot, resultsRoot;
        TextMeshProUGUI resultsTitle, notFoundLabel;
        TMP_InputField searchField;

        // ───────────────────────────── Lifecycle ─────────────────────────────

        List<GoogelCategory> Categories;

        void LoadData()
        {
            var source = (data && data.categories != null && data.categories.Count > 0)
                ? data.categories
                : GoogelDefaults.Build();
            // Work on copies so play mode never edits your GoogelData asset
            Categories = new List<GoogelCategory>();
            foreach (var c in source)
                if (c != null) Categories.Add(new GoogelCategory { name = c.name, description = c.description, parts = new List<GoogelPart>(c.parts) });
            // Every AscendCAD part has a card (cards already in the asset, with your pictures, win)
            AscendGoogel.Merge(Categories, AscendGoogel.Build(AscendCADData.Resolve(null)), false);
            foreach (var c in Categories)
                foreach (var p in c.parts) p.category = c.name;
        }

        IEnumerable<GoogelPart> AllParts()
        {
            foreach (var c in Categories) foreach (var p in c.parts) yield return p;
        }

        void BuildBrowser()
        {
            var rootImg = root.gameObject.AddComponent<Image>();
            rootImg.color = tabBarColor;

            // Tab strip (top)
            tabStrip = CreateRect("Tabs", root);
            tabStrip.anchorMin = new Vector2(0f, 1f);
            tabStrip.anchorMax = Vector2.one;
            tabStrip.pivot = new Vector2(0.5f, 1f);
            tabStrip.sizeDelta = new Vector2(0f, TabBarHeight);
            tabStrip.anchoredPosition = Vector2.zero;
            HList(tabStrip, new RectOffset(8, 8, 6, 0), 3f, TextAnchor.LowerLeft);
            tabStrip.gameObject.AddComponent<RectMask2D>();

            // Pages (everything under the tabs)
            pagesRoot = CreateRect("Pages", root);
            Stretch(pagesRoot);
            pagesRoot.offsetMax = new Vector2(0f, -TabBarHeight);

            homeTab = AddTab("home", "Googel", BuildHomePage(), false);
            SwitchTo(homeTab);
        }

        // ───────────────────────────── Home page ─────────────────────────────

        GameObject BuildHomePage()
        {
            homeContent = CreateScrollPage("Home", out var page);
            VList(homeContent, new RectOffset(40, 40, 40, 48), 18f);

            BuildLogo(homeContent);
            BuildSearchBar(homeContent);
            Space(homeContent, 10f);

            // Search results (hidden until you search)
            var res = CreateRect("Results", homeContent);
            VList(res, new RectOffset(0, 0, 0, 0), 14f);
            resultsRoot = res.gameObject;

            var header = CreateRect("Header", res);
            HList(header, new RectOffset(0, 0, 0, 0), 12f, TextAnchor.MiddleLeft);
            resultsTitle = Text(header, "", 24f, textColor, FontStyles.Bold);
            resultsTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            TextButton(header, "Show all parts", 15f, linkColor, chipColor, ShowAllParts, 150f, 36f);

            notFoundLabel = Text(res, notFoundMessage, 34f, subTextColor);
            notFoundLabel.alignment = TextAlignmentOptions.Center;
            notFoundLabel.gameObject.AddComponent<LayoutElement>().minHeight = 180f;

            resultsGrid = CreateGrid(res);
            resultsRoot.SetActive(false);

            // Every category with its cards
            var cats = CreateRect("Categories", homeContent);
            VList(cats, new RectOffset(0, 0, 0, 0), 36f);
            categoriesRoot = cats.gameObject;

            int n = 1;
            foreach (var cat in Categories) BuildCategory(cat, n++, cats);

            return page;
        }

        void BuildLogo(Transform parent)
        {
            var row = CreateRect("Logo", parent);
            HList(row, new RectOffset(0, 0, 0, 0), 2f, TextAnchor.MiddleCenter);

            if (logoIcon)
            {
                var g = CreateRect("G", row);
                var le = g.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 84f;
                var img = g.gameObject.AddComponent<Image>();
                img.sprite = logoIcon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                var g = Text(row, "G", 88f, C("#4285F4"), FontStyles.Bold);
                g.textWrappingMode = TextWrappingModes.NoWrap;
            }

            var rest = Text(row,
                "<color=#EA4335>o</color><color=#FBBC05>o</color><color=#4285F4>g</color>" +
                "<color=#34A853>e</color><color=#EA4335>l</color>", 88f, textColor);
            rest.textWrappingMode = TextWrappingModes.NoWrap;
        }

        void BuildSearchBar(Transform parent)
        {
            var row = CreateRect("SearchRow", parent);
            HList(row, new RectOffset(0, 0, 0, 0), 0f, TextAnchor.MiddleCenter);

            // Grey 1px border + white pill inside
            var box = CreateRect("SearchBox", row);
            var le = box.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 660f;
            le.minWidth = 260f;
            le.preferredHeight = 52f;
            var border = box.gameObject.AddComponent<Image>();
            border.sprite = Rounded;
            border.type = Image.Type.Sliced;
            border.pixelsPerUnitMultiplier = 0.38f;
            border.color = searchBorder;

            var inner = CreateRect("Inner", box);
            Stretch(inner);
            inner.offsetMin = new Vector2(1.5f, 1.5f);
            inner.offsetMax = new Vector2(-1.5f, -1.5f);
            var innerImg = inner.gameObject.AddComponent<Image>();
            innerImg.sprite = Rounded;
            innerImg.type = Image.Type.Sliced;
            innerImg.pixelsPerUnitMultiplier = 0.4f;
            innerImg.color = searchBg;
            HList(inner, new RectOffset(20, 6, 6, 6), 10f, TextAnchor.MiddleLeft);

            var mag = CreateRect("Magnifier", inner);
            var mle = mag.gameObject.AddComponent<LayoutElement>();
            mle.preferredWidth = mle.preferredHeight = mle.minWidth = 20f;
            var magImg = mag.gameObject.AddComponent<Image>();
            magImg.sprite = Magnifier;
            magImg.color = subTextColor;
            magImg.raycastTarget = false;

            searchField = CreateInputField(inner);
            var fle = searchField.gameObject.AddComponent<LayoutElement>();
            fle.flexibleWidth = 1f;
            fle.minWidth = 60f;
            fle.preferredHeight = 40f;

            TextButton(inner, "Search", 16f, onLink, linkColor, RunSearch, 110f, 40f);
        }

        TMP_InputField CreateInputField(Transform parent)
        {
            var rt = CreateRect("SearchField", parent);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f); // invisible, but makes the field clickable

            var area = CreateRect("Text Area", rt);
            Stretch(area);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = Text(area, searchPlaceholder, 18f, new Color(subTextColor.r, subTextColor.g, subTextColor.b, 0.75f));
            var txt = Text(area, "", 18f, textColor);
            foreach (var t in new[] { ph, txt })
            {
                Stretch(t.rectTransform);
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.textWrappingMode = TextWrappingModes.NoWrap;
            }
            txt.richText = false;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = txt;
            field.placeholder = ph;
            if (font) field.fontAsset = font;
            field.pointSize = 18f;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.customCaretColor = true;
            field.caretColor = textColor;
            field.caretWidth = 2;
            field.selectionColor = new Color(linkColor.r, linkColor.g, linkColor.b, 0.3f);
            field.targetGraphic = hit;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.onSubmit.AddListener(_ => RunSearch()); // Enter key
            return field;
        }

        void BuildCategory(GoogelCategory cat, int number, Transform parent)
        {
            var sec = CreateRect(cat.name, parent);
            VList(sec, new RectOffset(0, 0, 0, 0), 6f);

            var line = CreateRect("Divider", sec);
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            var lineImg = line.gameObject.AddComponent<Image>();
            lineImg.color = divider;
            lineImg.raycastTarget = false;

            Space(sec, 6f);
            Text(sec, number + ". " + cat.name, 26f, textColor, FontStyles.Bold);
            Text(sec, cat.description, 15f, subTextColor);
            Space(sec, 8f);

            var grid = CreateGrid(sec);
            foreach (var p in cat.parts) BuildCard(p, grid);
        }

        RectTransform CreateGrid(Transform parent)
        {
            var rt = CreateRect("Cards", parent);
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cardSize;
            g.spacing = new Vector2(18f, 18f);
            g.padding = new RectOffset(2, 2, 2, 6); // room for the card shadow
            g.startCorner = GridLayoutGroup.Corner.UpperLeft;
            g.startAxis = GridLayoutGroup.Axis.Horizontal;
            g.childAlignment = TextAnchor.UpperLeft;
            g.constraint = GridLayoutGroup.Constraint.Flexible; // wraps to the window width
            rt.gameObject.AddComponent<GoogelGridFit>().minCellWidth = cardSize.x; // stretch to fill the row
            return rt;
        }

        // Title → short description ending in "Read more" → image
        void BuildCard(GoogelPart p, Transform grid)
        {
            var card = CreateRect(p.title, grid);
            var bg = card.gameObject.AddComponent<Image>();
            bg.sprite = Rounded;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 0.9f;
            bg.color = cardColor;
            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = shadowColor;
            shadow.effectDistance = new Vector2(0f, -2f);
            VList(card, new RectOffset(14, 14, 14, 14), 6f);

            Color hoverColor = Color.Lerp(cardColor, linkColor, 0.12f);
            var hover = card.gameObject.AddComponent<GoogelClick>();
            hover.onEnter = () => bg.color = hoverColor;
            hover.onExit = () => bg.color = cardColor;

            Text(card, p.title, 18f, textColor, FontStyles.Bold);

            var desc = Text(card, p.shortDescription + " <link=\"more\"><color=" + Hex(linkColor) + "><u>Read more</u></color></link>",
                14f, subTextColor);
            desc.raycastTarget = true;
            var click = desc.gameObject.AddComponent<GoogelClick>();
            click.onClick = e =>
            {
                if (TMP_TextUtilities.FindIntersectingLink(desc, e.position, e.pressEventCamera) != -1) OpenPart(p);
            };

            BuildImageBox(card, p, 140f, 80f, 11f, true);
        }

        void BuildImageBox(Transform parent, GoogelPart p, float prefHeight, float minHeight, float captionSize, bool flexible)
        {
            var frame = CreateRect("ImageFrame", parent);
            var le = frame.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = prefHeight;
            le.minHeight = minHeight;
            le.flexibleHeight = flexible ? 1f : 0f;

            var fimg = frame.gameObject.AddComponent<Image>();
            fimg.sprite = Rounded;
            fimg.type = Image.Type.Sliced;
            fimg.pixelsPerUnitMultiplier = 0.9f;
            fimg.color = frameColor;
            fimg.raycastTarget = false;

            var inner = CreateRect("Picture", frame);
            Stretch(inner);
            inner.offsetMin = new Vector2(6f, 6f);
            inner.offsetMax = new Vector2(-6f, -6f);

            var sprite = FindImage(p);
            if (sprite)
            {
                var img = inner.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                var t = Text(inner, "Add an image named\n<b>" + p.title + "</b>", captionSize, subTextColor);
                Stretch(t.rectTransform);
                t.alignment = TextAlignmentOptions.Center;
            }
        }

        // ───────────────────────────── Detail page ("Read more" tab) ─────────────────────────────

        GameObject BuildDetailPage(GoogelPart p)
        {
            var content = CreateScrollPage(p.title, out var page);
            VList(content, new RectOffset(56, 56, 28, 48), 10f);

            Text(content, "Googel  >  " + p.category, 14f, subTextColor);
            Text(content, p.title, 40f, textColor, FontStyles.Bold);
            Space(content, 4f);
            BuildImageBox(content, p, 340f, 200f, 16f, false);
            Space(content, 6f);
            var intro = Text(content, p.shortDescription, 19f, textColor);
            intro.lineSpacing = 6f;

            Section(content, "Purpose", p.purpose);
            Section(content, "Key Specifications", Bullets(p.specs));
            Section(content, "Cost", p.cost);
            Section(content, "History", p.history);

            Space(content, 16f);
            Text(content, "Figures are approximate and based on publicly available information.", 13f, subTextColor, FontStyles.Italic);
            return page;
        }

        void Section(Transform parent, string heading, string body)
        {
            Space(parent, 10f);
            Text(parent, heading, 22f, textColor, FontStyles.Bold);
            var b = Text(parent, body, 16.5f, textColor);
            b.lineSpacing = 8f;
        }

        static string Bullets(string specs)
        {
            var sb = new StringBuilder();
            foreach (var raw in specs.Split('\n', '|'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                if (colon > 0 && colon < 40) line = "<b>" + line.Substring(0, colon + 1) + "</b>" + line.Substring(colon + 1);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("•  ").Append(line);
            }
            return sb.ToString();
        }

        // ───────────────────────────── Tabs ─────────────────────────────

        public void OpenPart(GoogelPart p)
        {
            foreach (var t in tabs)
                if (t.key == p.Id) { SwitchTo(t); return; }

            SwitchTo(AddTab(p.Id, p.title, BuildDetailPage(p), true));
        }

        Tab AddTab(string key, string title, GameObject page, bool closable)
        {
            var t = new Tab { key = key, page = page };
            var rt = CreateRect("Tab " + title, tabStrip);
            t.button = rt.gameObject;

            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 220f;
            le.minWidth = 90f;
            le.preferredHeight = 34f;

            t.bg = rt.gameObject.AddComponent<Image>();
            t.bg.sprite = Rounded;
            t.bg.type = Image.Type.Sliced;
            t.bg.pixelsPerUnitMultiplier = 1.2f;
            HList(rt, new RectOffset(12, 6, 0, 0), 7f, TextAnchor.MiddleLeft);

            MiniLogo(rt);

            t.label = Text(rt, title, 13f, textColor);
            t.label.textWrappingMode = TextWrappingModes.NoWrap;
            t.label.overflowMode = TextOverflowModes.Truncate;
            var lle = t.label.gameObject.AddComponent<LayoutElement>();
            lle.flexibleWidth = 1f;
            lle.preferredWidth = 1f;
            lle.minWidth = 0f;

            if (closable)
            {
                var x = CreateRect("Close", rt);
                var xle = x.gameObject.AddComponent<LayoutElement>();
                xle.preferredWidth = xle.preferredHeight = xle.minWidth = 20f;
                var ximg = x.gameObject.AddComponent<Image>();
                ximg.sprite = Rounded;
                ximg.type = Image.Type.Sliced;
                var xb = x.gameObject.AddComponent<Button>();
                xb.targetGraphic = ximg;
                var cb = ColorBlock.defaultColorBlock;
                cb.normalColor = cb.selectedColor = cb.disabledColor = new Color(1f, 1f, 1f, 0f);
                cb.highlightedColor = new Color(hoverTint.r, hoverTint.g, hoverTint.b, hoverTint.a * 2f);
                cb.pressedColor = new Color(hoverTint.r, hoverTint.g, hoverTint.b, hoverTint.a * 3.5f);
                xb.colors = cb;
                xb.navigation = new Navigation { mode = Navigation.Mode.None };
                xb.onClick.AddListener(() => CloseTab(t));
                var xt = Text(x, "x", 14f, subTextColor);
                Stretch(xt.rectTransform);
                xt.alignment = TextAlignmentOptions.Center;
            }

            var click = rt.gameObject.AddComponent<GoogelClick>();
            click.onClick = _ => SwitchTo(t);
            click.onEnter = () => { if (t != activeTab) t.bg.color = hoverTint; };
            click.onExit = () => { if (t != activeTab) t.bg.color = new Color(1f, 1f, 1f, 0f); };

            tabs.Add(t);
            return t;
        }

        void SwitchTo(Tab t)
        {
            activeTab = t;
            foreach (var tab in tabs)
            {
                bool on = tab == t;
                tab.page.SetActive(on);
                tab.bg.color = on ? pageColor : new Color(1f, 1f, 1f, 0f);
                tab.label.color = on ? textColor : subTextColor;
            }
        }

        void CloseTab(Tab t)
        {
            int i = tabs.IndexOf(t);
            if (i <= 0) return; // the Googel home tab can't be closed
            tabs.RemoveAt(i);
            t.button.SetActive(false);
            UnityEngine.Object.Destroy(t.button);
            UnityEngine.Object.Destroy(t.page);
            if (activeTab == t) SwitchTo(tabs[Mathf.Min(i, tabs.Count - 1)]);
        }

        void MiniLogo(Transform parent)
        {
            if (logoIcon)
            {
                var r = CreateRect("Favicon", parent);
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = le.minWidth = 16f;
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = logoIcon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            else
            {
                var g = Text(parent, "G", 15f, C("#4285F4"), FontStyles.Bold);
                var le = g.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = 13f;
            }
        }

        // ───────────────────────────── Search ─────────────────────────────

        public void RunSearch()
        {
            if (activeTab != homeTab) SwitchTo(homeTab);

            string raw = searchField.text.Trim();
            string q = Norm(raw);
            if (q.Length == 0) { ShowAllParts(); return; }

            // 1) Exact title match
            var matches = new List<GoogelPart>();
            foreach (var p in AllParts())
                if (Norm(p.title) == q) matches.Add(p);

            // 2) Partial title match, or a whole category ("solid rocket boosters")
            if (matches.Count == 0)
            {
                foreach (var cat in Categories)
                {
                    bool catHit = Norm(cat.name).Contains(q);
                    foreach (var p in cat.parts)
                    {
                        string t = Norm(p.title);
                        if (catHit || t.Contains(q) || (t.Length >= 2 && q.Contains(t))) matches.Add(p);
                    }
                }
            }

            ShowResults(raw, matches);
            if (matches.Count == 1) OpenPart(matches[0]); // one hit → go straight to it
        }

        void ShowResults(string raw, List<GoogelPart> matches)
        {
            categoriesRoot.SetActive(false);
            resultsRoot.SetActive(true);

            for (int i = resultsGrid.childCount - 1; i >= 0; i--)
            {
                var old = resultsGrid.GetChild(i).gameObject;
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }

            bool none = matches.Count == 0;
            notFoundLabel.gameObject.SetActive(none);
            resultsGrid.gameObject.SetActive(!none);
            resultsTitle.text = none
                ? "No results for \"<noparse>" + raw + "</noparse>\""
                : matches.Count + (matches.Count == 1 ? " result" : " results") + " for \"<noparse>" + raw + "</noparse>\"";

            foreach (var p in matches) BuildCard(p, resultsGrid);
            homeContent.anchoredPosition = Vector2.zero; // back to the top
        }

        void ShowAllParts()
        {
            searchField.SetTextWithoutNotify("");
            resultsRoot.SetActive(false);
            categoriesRoot.SetActive(true);
            homeContent.anchoredPosition = Vector2.zero;
        }

        Sprite FindImage(GoogelPart p) => p.image;

        // ───────────────────────────── UI helpers ─────────────────────────────

        RectTransform CreateScrollPage(string pageName, out GameObject page)
        {
            var rt = CreateRect(pageName, pagesRoot);
            Stretch(rt);
            rt.gameObject.AddComponent<Image>().color = pageColor; // also catches the mouse wheel

            var scroll = rt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = scrollSensitivity;

            var viewport = CreateRect("Viewport", rt);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Slim scrollbar on the right
            var bar = CreateRect("Scrollbar", rt);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = Vector2.one;
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(10f, 0f);
            var barImg = bar.gameObject.AddComponent<Image>();
            barImg.sprite = Rounded;
            barImg.type = Image.Type.Sliced;
            barImg.color = scrollTrack;

            var area = CreateRect("Sliding Area", bar);
            Stretch(area);
            var handle = CreateRect("Handle", area);
            Stretch(handle);
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.sprite = Rounded;
            hImg.type = Image.Type.Sliced;
            hImg.color = scrollHandle;

            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle;
            sb.targetGraphic = hImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.navigation = new Navigation { mode = Navigation.Mode.None };

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 2f;

            page = rt.gameObject;
            return content;
        }

        void TextButton(Transform parent, string label, float size, Color txtColor, Color bgColor, Action onClick, float w, float h)
        {
            var rt = CreateRect(label, parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = w;
            le.preferredHeight = h;

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.6f;
            img.color = bgColor;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() => onClick());

            var t = Text(rt, label, size, txtColor, FontStyles.Bold);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }

        TextMeshProUGUI Text(Transform parent, string s, float size, Color c, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = s;
            t.fontSize = size;
            t.color = c;
            t.fontStyle = style;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        static void VList(RectTransform rt, RectOffset pad, float spacing)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = pad;
            v.spacing = spacing;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
        }

        static void HList(RectTransform rt, RectOffset pad, float spacing, TextAnchor align)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = pad;
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
        }

        static void Space(Transform parent, float height)
        {
            var r = CreateRect("Space", parent);
            r.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        }

        static RectTransform CreateRect(string goName, Transform parent)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // "RS-25", "rs 25" and "Rs25" all become "rs25"
        static string Norm(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        // ───────────────────────────── Generated sprites ─────────────────────────────

        static Sprite rounded, magnifier;

        static Sprite Rounded => rounded ? rounded : rounded = MakeSprite(32, (u, v) =>
        {
            const float r = 0.5f;
            float qx = Mathf.Max(Mathf.Abs(u) - (1f - r), 0f);
            float qy = Mathf.Max(Mathf.Abs(v) - (1f - r), 0f);
            return qx * qx + qy * qy <= r * r;
        }, 10f);

        static Sprite Magnifier => magnifier ? magnifier : magnifier = MakeSprite(64, (u, v) =>
        {
            float cx = u + 0.18f, cy = v - 0.18f;
            bool ring = Mathf.Abs(Mathf.Sqrt(cx * cx + cy * cy) - 0.48f) < 0.12f;
            float t = Mathf.Clamp01(((u - 0.2f) * 0.65f - (v + 0.2f) * 0.65f) / 0.845f);
            float px = 0.2f + 0.65f * t, py = -0.2f - 0.65f * t;
            bool handle = (u - px) * (u - px) + (v - py) * (v - py) < 0.14f * 0.14f;
            return ring || handle;
        });

        static Sprite MakeSprite(int size, Func<float, float, bool> inside, float border = 0f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            const int ss = 4;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    float u = (x + (sx + 0.5f) / ss) / size * 2f - 1f;
                    float v = (y + (sy + 0.5f) / ss) / size * 2f - 1f;
                    if (inside(u, v)) hits++;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / (ss * ss)));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}

// Click / hover hook that (unlike EventTrigger) doesn't swallow mouse-wheel scrolling
public class GoogelClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Action<PointerEventData> onClick;
    public Action onEnter, onExit;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) onClick?.Invoke(e);
    }
    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
    public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
}

// Makes grid cards stretch so each row fills the full width (recalculates when the window resizes)
public class GoogelGridFit : MonoBehaviour
{
    public float minCellWidth = 250f;
    GridLayoutGroup grid;
    RectTransform rt;
    float lastWidth = -1f;

    void LateUpdate()
    {
        if (!grid) { grid = GetComponent<GridLayoutGroup>(); rt = (RectTransform)transform; if (!grid) return; }

        float width = rt.rect.width;
        if (width <= 0f || Mathf.Approximately(width, lastWidth)) return;
        lastWidth = width;

        float avail = width - grid.padding.left - grid.padding.right;
        float gap = grid.spacing.x;
        int cols = Mathf.Max(1, Mathf.FloorToInt((avail + gap) / (minCellWidth + gap)));
        float cellWidth = Mathf.Floor((avail - gap * (cols - 1)) / cols);
        grid.cellSize = new Vector2(Mathf.Max(1f, cellWidth), grid.cellSize.y);
    }
}
