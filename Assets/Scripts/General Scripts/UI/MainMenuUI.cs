using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Remembers which world was opened. In the Hangar scene read it with:
//   string world = WorldSession.CurrentWorld;
public static class WorldSession
{
    public static string CurrentWorld;
}

// ============================================================================
//  ASCEND - Main Menu
//  Put this on ONE empty GameObject in the Main Menu scene.
//  Drag in a font and a background image. Everything else is built by code.
// ============================================================================
public class MainMenuUI : MonoBehaviour
{
    [Header("Drag these in")]
    [Tooltip("A .ttf or .otf font file")]
    [SerializeField] private Font font;
    [Tooltip("Optional: a TextMeshPro Font Asset (used instead of the font file if set)")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private Texture2D backgroundImage;

    [Header("Scenes (drag the scene files in)")]
    [Tooltip("Drag the Hangar scene file here")]
    [SerializeField] private UnityEngine.Object hangarScene;
    [Tooltip("Drag the Tutorial scene file here (leave empty until you have one)")]
    [SerializeField] private UnityEngine.Object tutorialScene;

    // Filled in automatically from the scenes above. Builds use these.
    [SerializeField, HideInInspector] private string hangarScenePath = "";
    [SerializeField, HideInInspector] private string hangarSceneName = "Hangar";
    [SerializeField, HideInInspector] private string tutorialScenePath = "";
    [SerializeField, HideInInspector] private string tutorialSceneName = "";

    [Header("Look")]
    [SerializeField] private Color textColor = new Color(0.93f, 0.93f, 0.96f);
    [SerializeField] private Color hoverColor = new Color(0.55f, 0.8f, 1f);
    [Tooltip("Colour of the world names on the world bars")]
    [SerializeField] private Color worldNameColor = Color.black;
    [Tooltip("Extra black layer over the background on the main screen (0 = none)")]
    [Range(0f, 1f)] [SerializeField] private float backgroundDarkness = 0f;
    [Range(0f, 1f)] [SerializeField] private float playPanelDarkness = 0.65f;

    private const string SaveKey = "ASCEND_Worlds";
    private const float DoubleClickTime = 0.5f;

    private TMP_FontAsset tmpFont;
    private GameObject mainPanel;
    private GameObject playPanel;
    private RectTransform listContent;
    private ScrollRect scrollRect;

    private Sprite iconCheck, iconCross, iconPencil, iconTrash, iconPlus, iconBack, iconQuestion;
    private Sprite barSprite;

    private readonly List<WorldRow> rows = new List<WorldRow>();

    private class WorldRow
    {
        public string name;
        public bool isNew;
        public bool editing;
        public float lastClick = -1f;
        public GameObject root;
        public TMP_Text label;
        public TMP_InputField input;
        public GameObject confirm, cancel, edit, delete;
    }

    [Serializable]
    private class SaveData
    {
        public List<string> worlds = new List<string>();
    }

    // ------------------------------------------------------------------------
#if UNITY_EDITOR
    // Runs in the editor whenever you change something in the Inspector.
    // Turns the dragged scene files into a path + name the game can load.
    private void OnValidate()
    {
        ReadScene(ref hangarScene, ref hangarScenePath, ref hangarSceneName);
        ReadScene(ref tutorialScene, ref tutorialScenePath, ref tutorialSceneName);
    }

    private static void ReadScene(ref UnityEngine.Object scene, ref string path, ref string sceneName)
    {
        if (scene != null && !(scene is UnityEditor.SceneAsset))
        {
            Debug.LogWarning("MainMenuUI: that's not a scene file. Drag a scene (.unity) in.");
            scene = null;
        }

        if (scene == null)
        {
            path = "";
            return;
        }

        path = UnityEditor.AssetDatabase.GetAssetPath(scene);
        sceneName = Path.GetFileNameWithoutExtension(path);
    }
#endif

    private void Awake()
    {
        SetupFont();
        MakeSprites();
        EnsureEventSystem();
        BuildUI();
        LoadWorlds();
        ShowPanel(mainPanel);
    }

    // ======================= BUTTON ACTIONS =================================

    private void OnPlay() => ShowPanel(playPanel);
    private void OnBack() => ShowPanel(mainPanel);
    private void OnOptions() => Debug.Log("Options menu not built yet.");

    private void OnTutorial()
    {
        if (string.IsNullOrEmpty(tutorialScenePath))
        {
            Debug.Log("Tutorial not set up yet.");
            return;
        }
        OpenScene(tutorialScenePath, tutorialSceneName);
    }

    private void OpenScene(string path, string sceneName)
    {
#if UNITY_EDITOR
        // In the editor, load straight from the file. No Build Profiles list needed.
        if (!string.IsNullOrEmpty(path))
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        // In a built game the scene must be in File > Build Profiles > Scene List.
        SceneManager.LoadScene(sceneName);
    }

    private void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowPanel(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        playPanel.SetActive(panel == playPanel);
    }

    // ======================= WORLD LIST LOGIC ===============================

    private void CreateNewWorld()
    {
        // Only one unnamed world at a time
        foreach (var r in rows)
        {
            if (r.isNew) { BeginEdit(r); return; }
        }

        SpawnRow(null);
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f; // scroll to the new row
    }

    private void BeginEdit(WorldRow row)
    {
        row.editing = true;
        row.input.text = row.name ?? "";
        SetPlaceholder(row, "Enter world name...");
        SetEditing(row, true);
        row.input.Select();
        row.input.ActivateInputField();
    }

    private void ConfirmRow(WorldRow row)
    {
        if (!row.editing) return;

        string newName = row.input.text.Trim();

        if (string.IsNullOrEmpty(newName))
        {
            SetPlaceholder(row, "Name can't be empty");
            row.input.ActivateInputField();
            return;
        }

        foreach (var r in rows)
        {
            if (r != row && !r.isNew &&
                string.Equals(r.name, newName, StringComparison.OrdinalIgnoreCase))
            {
                row.input.text = "";
                SetPlaceholder(row, "That name is taken");
                row.input.ActivateInputField();
                return;
            }
        }

        row.name = newName;
        row.isNew = false;
        SaveWorlds();
        ShowNormal(row);
    }

    private void CancelRow(WorldRow row)
    {
        if (row.isNew) DeleteRow(row);   // brand-new world: X throws it away
        else ShowNormal(row);            // renaming: X keeps the old name
    }

    private void DeleteRow(WorldRow row)
    {
        rows.Remove(row);
        Destroy(row.root);
        SaveWorlds();
    }

    private void OnRowPressed(WorldRow row)
    {
        if (row.editing || row.isNew) return;

        float now = Time.unscaledTime;
        if (row.lastClick >= 0f && now - row.lastClick <= DoubleClickTime)
        {
            row.lastClick = -1f;
            WorldSession.CurrentWorld = row.name;
            Debug.Log("Opening world: " + row.name);
            OpenScene(hangarScenePath, hangarSceneName);
        }
        else
        {
            row.lastClick = now;
            Debug.Log("Clicked world: " + row.name + " (click again to open)");
        }
    }

    private void ShowNormal(WorldRow row)
    {
        row.editing = false;
        row.label.text = row.name;
        SetEditing(row, false);
    }

    private void SetEditing(WorldRow row, bool editing)
    {
        row.input.gameObject.SetActive(editing);
        row.confirm.SetActive(editing);
        row.cancel.SetActive(editing);

        row.label.gameObject.SetActive(!editing);
        row.edit.SetActive(!editing);
        row.delete.SetActive(!editing);
    }

    private void SetPlaceholder(WorldRow row, string message)
    {
        if (row.input.placeholder is TMP_Text ph) ph.text = message;
    }

    private void SaveWorlds()
    {
        var data = new SaveData();
        foreach (var r in rows)
        {
            if (!r.isNew) data.worlds.Add(r.name);
        }
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    private void LoadWorlds()
    {
        string json = PlayerPrefs.GetString(SaveKey, "");
        if (string.IsNullOrEmpty(json)) return;

        var data = JsonUtility.FromJson<SaveData>(json);
        foreach (string n in data.worlds) SpawnRow(n);
    }

    // ======================= BUILDING THE UI ================================

    private void BuildUI()
    {
        // ---------- Canvas ----------
        var canvasGO = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.layer = 5;
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)canvasGO.transform;

        // ---------- Background ----------
        var bgHolder = NewUI("Background", root);
        Stretch(bgHolder);
        if (backgroundImage != null)
        {
            var bgRT = NewUI("Image", bgHolder);
            var raw = bgRT.gameObject.AddComponent<RawImage>();
            raw.texture = backgroundImage;
            raw.raycastTarget = false;
            var fit = bgRT.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = (float)backgroundImage.width / backgroundImage.height;
        }
        else
        {
            var solid = bgHolder.gameObject.AddComponent<Image>();
            solid.color = new Color(0.03f, 0.05f, 0.1f);
            solid.raycastTarget = false;
        }

        // Black layer over the whole background
        if (backgroundDarkness > 0f)
        {
            var shadeRT = NewUI("Darken", bgHolder);
            Stretch(shadeRT);
            var shade = shadeRT.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, backgroundDarkness);
            shade.raycastTarget = false;
        }

        // ---------- Main panel ----------
        var main = NewUI("MainPanel", root);
        Stretch(main);
        mainPanel = main.gameObject;

        var titleRT = NewUI("Title", main);
        titleRT.anchorMin = titleRT.anchorMax = new Vector2(0.68f, 0.76f);
        titleRT.sizeDelta = new Vector2(1200, 240);
        AddText(titleRT, "ASCEND", 160, TextAlignmentOptions.Center, 22, false);

        MakeTextButton(main, "Play",    new Vector2(0.68f, 0.52f), OnPlay);
        MakeTextButton(main, "Options", new Vector2(0.68f, 0.40f), OnOptions);
        MakeTextButton(main, "Exit",    new Vector2(0.68f, 0.28f), OnExit);

        // ---------- Play panel ----------
        var play = NewUI("PlayPanel", root);
        Stretch(play);
        var dark = play.gameObject.AddComponent<Image>();
        dark.color = new Color(0f, 0f, 0f, playPanelDarkness);
        playPanel = play.gameObject;

        MakeIconButton(play, "TutorialButton", iconQuestion, "TUTORIAL", false,
            new Vector2(0, 1), new Vector2(140, -120), 700, OnTutorial);
        MakeIconButton(play, "CreateWorldButton", iconPlus, "CREATE NEW WORLD", false,
            new Vector2(0, 1), new Vector2(140, -220), 900, CreateNewWorld);
        MakeIconButton(play, "BackButton", iconBack, "Back", true,
            new Vector2(1, 1), new Vector2(-140, -120), 320, OnBack);

        // Existing worlds box
        var box = NewUI("ExistingWorlds", play);
        box.anchorMin = new Vector2(0.1f, 0.07f);
        box.anchorMax = new Vector2(0.9f, 0.66f);
        box.offsetMin = box.offsetMax = Vector2.zero;
        MakeFrame(box, 3, new Color(1, 1, 1, 0.9f));

        var headerRT = NewUI("Header", box);
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.anchoredPosition = new Vector2(0, -15);
        headerRT.sizeDelta = new Vector2(0, 70);
        AddText(headerRT, "Existing Worlds", 40, TextAlignmentOptions.Center, 30, true);

        // Scrollable list
        var viewport = NewUI("Viewport", box);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(20, 20);
        viewport.offsetMax = new Vector2(-20, -100);
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = Color.clear; // lets the mouse wheel scroll over empty space
        viewport.gameObject.AddComponent<RectMask2D>();

        listContent = NewUI("Content", viewport);
        listContent.anchorMin = new Vector2(0, 1);
        listContent.anchorMax = new Vector2(1, 1);
        listContent.pivot = new Vector2(0.5f, 1);
        listContent.offsetMin = listContent.offsetMax = Vector2.zero;

        var layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(160, 160, 10, 10);
        layout.spacing = 14;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = listContent;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30;
    }

    private void SpawnRow(string worldName)
    {
        var row = new WorldRow { name = worldName, isNew = string.IsNullOrEmpty(worldName) };

        var rt = NewUI("World", listContent);
        row.root = rt.gameObject;

        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = barSprite;
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
        MakeFrame(rt, 3, new Color(1, 1, 1, 0.85f));

        // Name label (black so it reads on the light side of the bar)
        row.label = AddText(rt, "", 36, TextAlignmentOptions.MidlineLeft, 10, true);
        row.label.color = worldNameColor;
        var labelRT = (RectTransform)row.label.transform;
        labelRT.offsetMin = new Vector2(24, 0);
        labelRT.offsetMax = new Vector2(-160, 0);

        // Name input
        row.input = MakeInput(rt);

        // Buttons: pencil + trash normally, ✓ + X while editing (same spots)
        row.edit    = MakeRowIcon(rt, "EditButton",    iconPencil, -86, () => BeginEdit(row));
        row.delete  = MakeRowIcon(rt, "DeleteButton",  iconTrash,  -20, () => DeleteRow(row));
        row.confirm = MakeRowIcon(rt, "ConfirmButton", iconCheck,  -86, () => ConfirmRow(row));
        row.cancel  = MakeRowIcon(rt, "CancelButton",  iconCross,  -20, () => CancelRow(row));

        row.input.onSubmit.AddListener(_ => ConfirmRow(row)); // Enter = ✓

        // The whole bar is a Button. Two clicks close together = open the world.
        var rowButton = rt.gameObject.AddComponent<Button>();
        rowButton.transition = Selectable.Transition.None;
        rowButton.targetGraphic = bg;
        var nav = rowButton.navigation;
        nav.mode = Navigation.Mode.None;
        rowButton.navigation = nav;
        rowButton.onClick.AddListener(() => OnRowPressed(row));

        rows.Add(row);

        if (row.isNew) BeginEdit(row);
        else ShowNormal(row);
    }

    private TMP_InputField MakeInput(RectTransform parent)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = "NameInput";
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(14, 10);
        rt.offsetMax = new Vector2(-160, -10);

        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var input = go.GetComponent<TMP_InputField>();
        input.fontAsset = tmpFont;
        input.pointSize = 32;
        input.characterLimit = 24;
        input.customCaretColor = true;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.4f, 0.7f, 1f, 0.5f);

        input.textComponent.color = textColor;
        input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;

        if (input.placeholder is TMP_Text ph)
        {
            ph.color = new Color(1f, 1f, 1f, 0.45f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.fontStyle = FontStyles.Italic;
        }

        return input;
    }

    // ======================= UI HELPERS =====================================

    private static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private TextMeshProUGUI AddText(Transform parent, string text, float size,
        TextAlignmentOptions align, float spacing, bool smallCaps)
    {
        var rt = NewUI("Text", parent);
        Stretch(rt);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = tmpFont;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = textColor;
        t.characterSpacing = spacing;
        t.fontStyle = smallCaps ? FontStyles.SmallCaps : FontStyles.Normal;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private void MakeTextButton(Transform parent, string label, Vector2 anchor, UnityAction onClick)
    {
        var rt = NewUI(label + "Button", parent);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.sizeDelta = new Vector2(500, 90);

        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = Color.clear;

        var txt = AddText(rt, label, 64, TextAlignmentOptions.Center, 12, true);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = txt;
        SetHoverColors(btn);
        btn.onClick.AddListener(onClick);
    }

    private void MakeIconButton(Transform parent, string name, Sprite icon, string label, bool smallCaps,
        Vector2 corner, Vector2 position, float width, UnityAction onClick)
    {
        var rt = NewUI(name, parent);
        rt.anchorMin = rt.anchorMax = corner;
        rt.pivot = corner;
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(width, 80);

        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = Color.clear;

        var iconRT = NewUI("Icon", rt);
        iconRT.anchorMin = iconRT.anchorMax = new Vector2(0, 0.5f);
        iconRT.pivot = new Vector2(0, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = new Vector2(64, 64);
        var iconImg = iconRT.gameObject.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.raycastTarget = false;

        var txt = AddText(rt, label, 52, TextAlignmentOptions.MidlineLeft, 14, smallCaps);
        var txtRT = (RectTransform)txt.transform;
        txtRT.offsetMin = new Vector2(92, 0);
        txtRT.offsetMax = Vector2.zero;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = txt;
        SetHoverColors(btn);
        btn.onClick.AddListener(onClick);
    }

    private GameObject MakeRowIcon(RectTransform parent, string name, Sprite icon, float x, UnityAction onClick)
    {
        var rt = NewUI(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0);
        rt.sizeDelta = new Vector2(54, 54);

        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = icon;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var c = btn.colors;
        c.highlightedColor = hoverColor;
        c.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        btn.colors = c;
        btn.onClick.AddListener(onClick);

        return rt.gameObject;
    }

    private void SetHoverColors(Button btn)
    {
        var c = btn.colors;
        c.normalColor = Color.white;
        c.highlightedColor = hoverColor;
        c.selectedColor = Color.white;
        c.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        c.fadeDuration = 0.08f;
        btn.colors = c;
    }

    private static void MakeFrame(RectTransform target, float t, Color color)
    {
        // top, bottom, left, right
        AddEdge(target, "FrameTop",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, t), color);
        AddEdge(target, "FrameBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, t), color);
        AddEdge(target, "FrameLeft",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(t, 0), color);
        AddEdge(target, "FrameRight",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(t, 0), color);
    }

    private static void AddEdge(RectTransform parent, string name, Vector2 min, Vector2 max,
        Vector2 pivot, Vector2 size, Color color)
    {
        var rt = NewUI(name, parent);
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    // ======================= SETUP ==========================================

    private void SetupFont()
    {
        if (fontAsset != null) tmpFont = fontAsset;
        else if (font != null) tmpFont = TMP_FontAsset.CreateFontAsset(font);

        if (tmpFont == null)
        {
            tmpFont = TMP_Settings.defaultFontAsset;
            Debug.LogWarning("MainMenuUI: no font assigned, using the TextMeshPro default font.");
        }
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    // ======================= ICONS (drawn by code) ==========================

    private void MakeSprites()
    {
        iconCheck  = MakeIcon(0.075f, new Vector4(0.28f, 0.52f, 0.43f, 0.36f), new Vector4(0.43f, 0.36f, 0.72f, 0.67f));
        iconCross  = MakeIcon(0.075f, new Vector4(0.33f, 0.33f, 0.67f, 0.67f), new Vector4(0.33f, 0.67f, 0.67f, 0.33f));
        iconPlus   = MakeIcon(0.08f,  new Vector4(0.5f, 0.27f, 0.5f, 0.73f),   new Vector4(0.27f, 0.5f, 0.73f, 0.5f));
        iconBack   = MakeIcon(0.08f,  new Vector4(0.58f, 0.70f, 0.40f, 0.50f), new Vector4(0.40f, 0.50f, 0.58f, 0.30f));
        iconPencil = MakeIcon(0.10f,  new Vector4(0.29f, 0.29f, 0.32f, 0.32f), new Vector4(0.38f, 0.38f, 0.68f, 0.68f));
        iconTrash  = MakeIcon(0.05f,
            new Vector4(0.29f, 0.66f, 0.71f, 0.66f),   // lid
            new Vector4(0.43f, 0.73f, 0.57f, 0.73f),   // handle
            new Vector4(0.35f, 0.60f, 0.39f, 0.27f),   // left side
            new Vector4(0.65f, 0.60f, 0.61f, 0.27f),   // right side
            new Vector4(0.39f, 0.27f, 0.61f, 0.27f),   // bottom
            new Vector4(0.47f, 0.54f, 0.47f, 0.35f),   // inner line
            new Vector4(0.53f, 0.54f, 0.53f, 0.35f));  // inner line

        // Question mark: an arc, a stem and a dot
        var q = new List<Vector4>();
        Vector2 c = new Vector2(0.5f, 0.6f);
        float r = 0.14f;
        Vector2 prev = c + new Vector2(Mathf.Cos(160 * Mathf.Deg2Rad), Mathf.Sin(160 * Mathf.Deg2Rad)) * r;
        for (int a = 140; a >= -70; a -= 15)
        {
            Vector2 p = c + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * r;
            q.Add(new Vector4(prev.x, prev.y, p.x, p.y));
            prev = p;
        }
        q.Add(new Vector4(prev.x, prev.y, 0.5f, 0.41f));
        q.Add(new Vector4(0.5f, 0.27f, 0.5f, 0.27f));
        iconQuestion = MakeIcon(0.075f, q.ToArray());

        barSprite = MakeBarGradient();
    }

    // White circle with dark line segments. Each Vector4 = (startX, startY, endX, endY) in 0..1
    private static Sprite MakeIcon(float thickness, params Vector4[] segments)
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[S * S];
        Color dark = new Color(0.1f, 0.1f, 0.12f, 1f);
        float aa = 1.5f / S;
        float half = thickness * 0.5f;
        Vector2 centre = new Vector2(0.5f, 0.5f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / S, (y + 0.5f) / S);
                float circle = Mathf.Clamp01((0.48f - Vector2.Distance(p, centre)) / aa);

                float d = float.MaxValue;
                foreach (var s in segments)
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(s.x, s.y), new Vector2(s.z, s.w)));

                float line = Mathf.Clamp01((half - d) / aa);
                Color col = Color.Lerp(Color.white, dark, line);
                col.a = circle;
                pixels[y * S + x] = col;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100);
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-6f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return Vector2.Distance(p, a + ab * t);
    }

    // Light-grey-to-dark gradient for the world bars
    private static Sprite MakeBarGradient()
    {
        const int W = 256;
        var tex = new Texture2D(W, 4, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color left = new Color(0.78f, 0.78f, 0.80f, 0.95f);
        Color right = new Color(0.12f, 0.12f, 0.14f, 0.85f);

        for (int x = 0; x < W; x++)
        {
            float t = Mathf.SmoothStep(0f, 1f, x / (float)(W - 1));
            Color col = Color.Lerp(left, right, t);
            for (int y = 0; y < 4; y++) tex.SetPixel(x, y, col);
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, 4), new Vector2(0.5f, 0.5f), 100);
    }
}
