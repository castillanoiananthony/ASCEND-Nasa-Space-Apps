// ═════════════════════════════════════════════════════════════════════════════
//  PauseMenu.cs  -  Esc during the game: pauses and shows Settings
//  (character, mouse sensitivity), RESUME and MAIN MENU. Esc again continues.
//  Added automatically to any scene that has a PlayerController.
//  It only opens while you're walking around (not while a computer, phone or
//  console is open - Esc closes those).
// ═════════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PauseMenu : MonoBehaviour
{
    public static bool IsPaused { get; private set; }
    public string mainMenuScene = "Main Menu";

    GameObject canvasGo, panel;
    float savedScale = 1f;
    CursorLockMode savedLock; bool savedVisible;
    PlayerController pc; bool pcWasEnabled;
    int openedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Add();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => Add();

    static void Add()
    {
        if (FindAnyObjectByType<PauseMenu>() || !FindAnyObjectByType<PlayerController>()) return;
        new GameObject("Pause Menu").AddComponent<PauseMenu>();
    }

    void OnDestroy() { if (IsPaused) { Time.timeScale = 1f; AudioListener.pause = false; IsPaused = false; } }

    void Update()
    {
        if (!EscDown()) return;
        if (IsPaused) { if (Time.frameCount != openedFrame) Resume(); return; }
        // only while walking around (cursor locked) and not during the trailer
        if (GameFlow.PlayTrailer || Cursor.lockState != CursorLockMode.Locked) return;
        Open();
    }

    static bool EscDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    void Open()
    {
        IsPaused = true; openedFrame = Time.frameCount;
        savedScale = Time.timeScale; Time.timeScale = 0f; AudioListener.pause = true;
        savedLock = Cursor.lockState; savedVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        pc = PlayerController.Instance;
        if (pc) { pcWasEnabled = pc.enabled; pc.enabled = false; }
        if (!canvasGo) Build();
        canvasGo.SetActive(true);
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        Time.timeScale = savedScale <= 0f ? 1f : savedScale; AudioListener.pause = false;
        Cursor.lockState = savedLock; Cursor.visible = savedVisible;
        if (pc && pcWasEnabled) pc.enabled = true;
        if (canvasGo) canvasGo.SetActive(false);
        if (UnityEngine.EventSystems.EventSystem.current) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }

    void ToMainMenu()
    {
        Resume();
        Time.timeScale = 1f;
        if (Application.CanStreamedLevelBeLoaded(mainMenuScene)) SceneManager.LoadScene(mainMenuScene);
        else Debug.LogWarning("PauseMenu: add '" + mainMenuScene + "' to File > Build Profiles (Scene List).");
    }

    void Build()
    {
        canvasGo = new GameObject("Pause Menu UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var c = canvasGo.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 2500;
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        if (!FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }
        panel = SettingsPanel.Build(canvasGo.transform, null, null,
            ("RESUME", (System.Action)Resume, true),
            ("MAIN MENU", (System.Action)ToMainMenu, false));
        var hint = MissionUI.Lbl(panel.transform, "PAUSED   //   ESC  RESUME", 14);
        MissionUI.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(800, 30)); hint.alignment = TMPro.TextAlignmentOptions.Center;
    }
}
