// MissionBriefing.cs  -  ASCEND mission briefing (uses PhMail from AscendCADData.cs)
//
// Flow:
//  1. Press E at the elevator DOOR box   -> fade, teleport inside the elevator, phone opens on PhMail
//  2. Tap a mission in the email          -> new PhMail email: "You have been assigned to ... FLOOR X"
//  3. Press P                             -> phone closes
//  4. Press E at the elevator BUTTON box  -> "GOING TO FLOOR X", fade to black, teleport to the meeting room
//  5. Briefing subtitles at the bottom (Space = continue, S = skip)
//  6. Phone opens with a summary email (P = exit). Then onBriefingFinished runs.
//
// All emails are real PhMail messages (AscendMail), so they also show up in the computer's PhMail app.
// The chosen mission is available to other scripts as MissionBriefing.SelectedMission.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

public class MissionBriefing : MonoBehaviour
{
    [Serializable]
    public class Mission
    {
        public string title = "New Mission";
        public string subtitle = "";
        [TextArea(2, 5)] public string[] briefingLines = new string[0];
        [Tooltip("PhMail rich text is allowed, like <b>bold</b>.")]
        [TextArea(6, 25)] public string summaryEmail = "";
    }

    enum State { ChoosingMission, GoToElevator, Briefing, Done }

    [Header("PhMail")]
    [Tooltip("Your AscendCAD Data asset (for the PhMail addresses). Leave empty to use the one on the computer.")]
    public AscendCADData ascendCADData;
    public string senderName = "Mission Control";

    [Header("Elevator boxes (each needs a Collider)")]
    [Tooltip("The box on the elevator door. Press E on it to go inside and open the phone.")]
    public Collider elevatorDoorBox;
    [Tooltip("The big box on the elevator button. Press E on it to ride to the briefing room.")]
    public Collider elevatorButtonBox;
    public float interactDistance = 4f;
    [Tooltip("Key to use the elevator door and button boxes.")]
    public KeyCode elevatorKey = KeyCode.E;

    [Header("Player & teleports")]
    [Tooltip("Your player object (the one that moves). Leave empty to use the object tagged 'Player'.")]
    public Transform player;
    [Tooltip("Teleport the player inside the elevator when they press E on the door.")]
    public bool teleportIntoElevator = true;
    [Tooltip("Optional: an empty object inside the elevator. If empty, 'Elevator Inside Position' is used.")]
    public Transform elevatorInsidePoint;
    public Vector3 elevatorInsidePosition = Vector3.zero;
    [Tooltip("Turn the player to face 'Elevator Facing Y' inside the elevator (ignored if an Elevator Inside Point is set; then the point's blue arrow is used).")]
    public bool setElevatorFacing = true;
    [Tooltip("Which way to face inside the elevator (Y rotation in degrees).")]
    public float elevatorFacingY = -90f;
    [Tooltip("Optional: an empty object in the meeting room. If empty, 'Meeting Room Position' is used.")]
    public Transform meetingRoomPoint;
    public Vector3 meetingRoomPosition = new Vector3(700f, 1f, 628f);
    [Tooltip("Turn the player to face 'Meeting Room Facing Y' when they arrive (ignored if a Meeting Room Point is set; then the point's blue arrow is used).")]
    public bool setMeetingRoomFacing = true;
    [Tooltip("Which way to face in the meeting room (Y rotation in degrees). Tip: in Play mode, turn the player the way you want, then copy the Player's Rotation Y here.")]
    public float meetingRoomFacingY = -90f;
    [Tooltip("Also make the camera look straight ahead (not up or down) after a teleport.")]
    public bool levelCameraOnTeleport = true;
    [Tooltip("Movement and mouse-look scripts to switch off while the phone or briefing is open.")]
    public Behaviour[] disableWhileBusy;
    [Tooltip("Things like the crosshair to hide while the phone or briefing is open.")]
    public GameObject[] hideWhileBusy;

    [Header("Keys")]
    [Tooltip("Key to close the phone.")]
    public KeyCode closePhoneKey = KeyCode.P;
    public KeyCode skipKey = KeyCode.S;
    public KeyCode continueKey = KeyCode.Space;

    [Header("Text & timing")]
    [Tooltip("Optional TextMesh Pro font. Leave empty to use the default TMP font.")]
    public TMP_FontAsset font;
    [Tooltip("Makes every text in the phone, briefing and floor screen bigger or smaller.")]
    [Range(0.6f, 1.6f)] public float textScale = 1f;
    public string speakerName = "Mission Director";
    [Tooltip("PhMail rich text is allowed, like <b>bold</b>.")]
    [TextArea(6, 20)] public string welcomeEmail = DefaultWelcome;
    public float lettersPerSecond = 45f;
    public float floorMessageSeconds = 2f;
    public float fadeSeconds = 0.6f;

    [Header("Missions (Easy, Medium, Hard)")]
    public Mission[] missions = DefaultMissions();

    [Header("Events")]
    [Tooltip("Runs when the player closes the phone after the briefing. Hook the next step up here.")]
    public UnityEvent onBriefingFinished;

    public static Mission SelectedMission { get; private set; }
    public static int SelectedMissionIndex { get; private set; } = -1;
    public static int BriefingFloor { get; private set; }

    // ---------- PhMail colors ----------
    static readonly Color Bezel   = Hex("#0B0B0E");
    static readonly Color Bg      = Hex("#18191C");
    static readonly Color Panel   = Hex("#212226");
    static readonly Color Panel2  = Hex("#2E2F35");
    static readonly Color Txt     = Hex("#E8EAED");
    static readonly Color Sub     = Hex("#9AA0A6");
    static readonly Color Accent  = Hex("#0B57D0");
    static readonly Color Chosen  = Hex("#1E8E3E");
    static readonly Color Gold    = Hex("#FFC850");
    static readonly Color RowBg   = new Color(1f, 1f, 1f, 0.04f);

    // ---------- runtime ----------
    State state = State.ChoosingMission;
    int floor;
    bool phoneOpen, isBusy, transitioning, finishOnPhoneClose, isTyping, listDirty;
    int lineIndex;
    CursorLockMode savedLock;
    bool savedVisible;
    AscendCADData data;
    // NonSerialized: otherwise Unity can fill these with blank emails (empty From/To/subject)
    [NonSerialized] AscendMail.Message choiceMsg, assignedMsg, summaryMsg, focusMsg, current;

    GameObject phoneRoot, listView, readerView, briefingRoot, floorRoot;
    RectTransform listContent, readerContent;
    ScrollRect listScroll, readerScroll;
    TextMeshProUGUI hintText, inboxCount, speakerText, subtitleText, progressText, floorNumberText;
    CanvasGroup fade;
    Coroutine typing, flash;

    // ================= Unity =================

    void Awake()
    {
        SelectedMission = null;
        SelectedMissionIndex = -1;
        BriefingFloor = 0;
    }

    void OnEnable() { AscendMail.Changed += OnMailChanged; }
    void OnDisable() { AscendMail.Changed -= OnMailChanged; }
    void OnMailChanged() { listDirty = true; }

    void Start()
    {
        if (!font && ascendCADData) font = ascendCADData.labelFont;
        if (!player)
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged) player = tagged.transform;
        }
        if (!elevatorDoorBox) Debug.LogWarning("MissionBriefing: drag the elevator door box into 'Elevator Door Box'.", this);
        if (!elevatorButtonBox) Debug.LogWarning("MissionBriefing: drag the elevator button box into 'Elevator Button Box'.", this);
        if (teleportIntoElevator && !elevatorInsidePoint && elevatorInsidePosition == Vector3.zero)
            Debug.LogWarning("MissionBriefing: set 'Elevator Inside Point' or 'Elevator Inside Position' (or turn off 'Teleport Into Elevator').", this);
        if (missions == null || missions.Length == 0) missions = DefaultMissions();

        EnsureEventSystem();
        BuildUI();
        phoneRoot.SetActive(false);
        briefingRoot.SetActive(false);
        floorRoot.SetActive(false);
    }

    void Update()
    {
        if (transitioning) return;

        if (phoneOpen)
        {
            if (listDirty && listView.activeSelf) RebuildList();
            if (KeyDown(closePhoneKey)) ClosePhone();
            return;
        }
        if (state == State.Briefing) { BriefingInput(); return; }

        Collider target = LookedAt();
        if (flash == null)
        {
            if (target != null && target == elevatorDoorBox) hintText.text = KeyName(elevatorKey) + " to enter the elevator";
            else if (target != null && target == elevatorButtonBox) hintText.text = KeyName(elevatorKey) + " to press the elevator button";
            else hintText.text = "";
        }

        if (target != null && KeyDown(elevatorKey))
        {
            if (target == elevatorDoorBox) StartCoroutine(EnterElevator());
            else PressElevatorButton();
        }
    }

    // ================= PhMail =================

    AscendCADData Data
    {
        get
        {
            if (data) return data;
            data = ascendCADData ? AscendCADData.Resolve(ascendCADData) : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
            return data;
        }
    }

    AscendMail.Message SendMail(string subject, string body)
    {
        return AscendMail.Deliver(Data.agencyEmail, Data.playerEmail, subject, body);
    }

    void EnsureChoiceEmail()
    {
        if (choiceMsg != null && AscendMail.All.Contains(choiceMsg)) return;
        choiceMsg = SendMail("Choose your mission",
            welcomeEmail + "\n\n<color=#9AA0A6><i>Open this email on your phone to choose a mission.</i></color>");
        focusMsg = choiceMsg;
    }

    // ================= Interaction =================

    Collider LookedAt()
    {
        Camera cam = Camera.main;
        if (!cam) return null;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide)) return null;
        if (Matches(hit.collider, elevatorDoorBox)) return elevatorDoorBox;
        if (Matches(hit.collider, elevatorButtonBox)) return elevatorButtonBox;
        return null;
    }

    static bool Matches(Collider hit, Collider target)
    {
        return target && (hit == target || hit.transform.IsChildOf(target.transform));
    }

    IEnumerator EnterElevator()
    {
        transitioning = true;
        hintText.text = "";
        SetBusy(true, false);

        if (teleportIntoElevator && (elevatorInsidePoint || elevatorInsidePosition != Vector3.zero))
        {
            yield return Fade(0f, 1f);
            yield return TeleportPlayer(elevatorInsidePoint, elevatorInsidePosition, setElevatorFacing ? elevatorFacingY : (float?)null);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Fade(1f, 0f);
        }

        transitioning = false;
        OpenPhone();
    }

    void OpenPhone()
    {
        EnsureChoiceEmail();
        phoneOpen = true;
        hintText.text = "";
        phoneRoot.SetActive(true);
        SetBusy(true, true);
        ShowMessage(focusMsg ?? choiceMsg);
    }

    void ClosePhone()
    {
        phoneOpen = false;
        phoneRoot.SetActive(false);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        SetBusy(false, false);

        if (finishOnPhoneClose)
        {
            finishOnPhoneClose = false;
            onBriefingFinished?.Invoke();
        }
    }

    void ChooseMission(int index)
    {
        if (state != State.ChoosingMission) return;

        SelectedMissionIndex = index;
        SelectedMission = missions[index];
        AscendMissionGuide.Mission = (AscendSimMission)Mathf.Clamp(index, 0, 2);   // AscendCAD guide + Mission Simulator follow the briefing
        floor = UnityEngine.Random.Range(1, 6); // 1 to 5
        BriefingFloor = floor;
        state = State.GoToElevator;

        assignedMsg = SendMail("Mission assigned: " + SelectedMission.title,
            "Hello Astronaut,\n\n" +
            "You have been assigned to mission <b>" + SelectedMission.title + "</b>.\n\n" +
            "Please go to the <b>Briefing Room</b> on <b><color=#FFC850>FLOOR " + floor + "</color></b>.\n\n" +
            "Press <b>" + elevatorKey + "</b> on the elevator button to go there.\n\n" +
            "Good luck!\n" + senderName);
        focusMsg = assignedMsg;
        ShowMessage(assignedMsg);
    }

    void PressElevatorButton()
    {
        if (state == State.ChoosingMission) { OpenPhone(); return; }
        if (state == State.Done) { Flash("Your briefing is done. Time to build your rocket!"); return; }
        if (state == State.GoToElevator) StartCoroutine(RideElevator());
    }

    IEnumerator RideElevator()
    {
        transitioning = true;
        hintText.text = "";
        SetBusy(true, false);

        floorNumberText.text = "FLOOR " + floor;
        floorRoot.SetActive(true);
        yield return new WaitForSecondsRealtime(floorMessageSeconds);

        yield return Fade(0f, 1f);
        floorRoot.SetActive(false);
        yield return TeleportPlayer(meetingRoomPoint, meetingRoomPosition, setMeetingRoomFacing ? meetingRoomFacingY : (float?)null);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Fade(1f, 0f);

        transitioning = false;
        StartBriefing();
    }

    // Finds the object that really moves the player (the one with the CharacterController or Rigidbody)
    Transform PlayerBody()
    {
        Transform p = player;
        if (p && !p.gameObject.scene.IsValid())
        {
            Debug.LogWarning("MissionBriefing: 'Player' is a prefab from the Project window. Drag the Player from the Hierarchy instead. Using the object tagged 'Player' for now.", this);
            p = null;
        }
        if (!p)
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged) p = tagged.transform;
        }
        if (!p) return null;

        var cc = p.GetComponentInParent<CharacterController>();
        if (cc) return cc.transform;
        var rb = p.GetComponentInParent<Rigidbody>();
        if (rb) return rb.transform;
        return p;
    }

    IEnumerator TeleportPlayer(Transform point, Vector3 fallback, float? facingY)
    {
        Transform body = PlayerBody();
        if (!body)
        {
            Debug.LogWarning("MissionBriefing: no player found (drag it into 'Player' or tag it 'Player'), so I can't teleport.", this);
            yield break;
        }

        Vector3 pos = point ? point.position : fallback;
        Quaternion rot = point ? Quaternion.Euler(0f, point.eulerAngles.y, 0f)
                       : facingY.HasValue ? Quaternion.Euler(0f, facingY.Value, 0f)
                       : body.rotation;
        bool turned = point || facingY.HasValue;

        var cc = body.GetComponent<CharacterController>();
        bool ccWasOn = cc && cc.enabled;
        if (cc) cc.enabled = false; // a CharacterController overrides position changes otherwise

        var rb = body.GetComponent<Rigidbody>();
        var interp = RigidbodyInterpolation.None;
        if (rb) { interp = rb.interpolation; rb.interpolation = RigidbodyInterpolation.None; }

        // Place the player a few frames in a row (while the screen is black),
        // so no other script or physics step can pull them back.
        for (int i = 0; i < 4; i++)
        {
            body.SetPositionAndRotation(pos, rot);
            if (rb)
            {
                rb.position = pos;
                rb.rotation = rot;
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            Physics.SyncTransforms();
            yield return null;
        }

        if (rb) rb.interpolation = interp;
        if (cc && ccWasOn) cc.enabled = true;
        // Level the camera so the player looks straight ahead (only if the camera is part of the player)
        if (turned && levelCameraOnTeleport)
        {
            Camera cam = Camera.main;
            if (cam && cam.transform != body && cam.transform.IsChildOf(body))
                cam.transform.localRotation = Quaternion.Euler(0f, cam.transform.localEulerAngles.y, cam.transform.localEulerAngles.z);
        }

        Debug.Log("MissionBriefing: teleported '" + body.name + "' to " + pos + (turned ? ", facing Y " + rot.eulerAngles.y.ToString("0") : ""), body);
    }

    IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        fade.alpha = from;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            fade.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
            yield return null;
        }
        fade.alpha = to;
    }

    // ================= Briefing =================

    void StartBriefing()
    {
        state = State.Briefing;
        lineIndex = 0;
        speakerText.text = speakerName + "  |  " + SelectedMission.title;
        briefingRoot.SetActive(true);

        if (SelectedMission.briefingLines == null || SelectedMission.briefingLines.Length == 0) { EndBriefing(); return; }
        ShowLine();
    }

    void BriefingInput()
    {
        if (KeyDown(skipKey)) { EndBriefing(); return; }
        if (!KeyDown(continueKey)) return;

        string[] lines = SelectedMission.briefingLines;
        if (isTyping)
        {
            // First press finishes the current line, second press goes to the next one
            StopTyping();
            subtitleText.text = lines[lineIndex];
            return;
        }
        lineIndex++;
        if (lineIndex >= lines.Length) EndBriefing();
        else ShowLine();
    }

    void ShowLine()
    {
        string[] lines = SelectedMission.briefingLines;
        progressText.text = (lineIndex + 1) + " / " + lines.Length;
        StopTyping();
        typing = StartCoroutine(TypeLine(lines[lineIndex]));
    }

    IEnumerator TypeLine(string line)
    {
        isTyping = true;
        subtitleText.text = line;
        subtitleText.maxVisibleCharacters = 0;
        subtitleText.ForceMeshUpdate();
        int total = subtitleText.textInfo.characterCount;
        float shown = 0f;
        while (shown < total)
        {
            shown += Time.unscaledDeltaTime * lettersPerSecond;
            subtitleText.maxVisibleCharacters = Mathf.Min(total, (int)shown);
            yield return null;
        }
        subtitleText.maxVisibleCharacters = 99999;
        isTyping = false;
        typing = null;
    }

    void StopTyping()
    {
        if (typing != null) StopCoroutine(typing);
        typing = null;
        isTyping = false;
        if (subtitleText) subtitleText.maxVisibleCharacters = 99999;
    }

    void EndBriefing()
    {
        StopTyping();
        briefingRoot.SetActive(false);
        state = State.Done;

        summaryMsg = SendMail("Mission summary: " + SelectedMission.title, SelectedMission.summaryEmail);
        focusMsg = summaryMsg;
        finishOnPhoneClose = true;
        OpenPhone();
    }

    // ================= Phone views =================

    void ShowList()
    {
        current = null;
        readerView.SetActive(false);
        listView.SetActive(true);
        RebuildList();
    }

    void RebuildList()
    {
        listDirty = false;
        UpdateInboxCount();
        Clear(listContent);

        var msgs = AscendMail.All.Where(m => !m.outgoing).Reverse().ToList();
        if (msgs.Count == 0) T(listContent, "No messages.", 26, Sub);

        foreach (var m in msgs)
        {
            var msg = m;
            var row = NewImage("Row", listContent, RowBg, 18f);
            row.raycastTarget = true;
            Le(row, -1, 130);
            var b = row.gameObject.AddComponent<Button>();
            b.targetGraphic = row;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => ShowMessage(msg));

            var t = T(row.transform, "", 22, Txt);
            Stretch(t.rectTransform, 18, 10, 18, 10);
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.fontStyle = m.read ? FontStyles.Normal : FontStyles.Bold;
            t.text = (m.read ? "" : "<color=#8AB4F8>• </color>") + "<noparse>" + m.from + "</noparse>\n" +
                     "<size=115%><noparse>" + m.subject + "</noparse></size>\n" +
                     "<size=85%><color=#9AA0A6>" + m.time + "</color></size>";
        }
        ScrollTop(listContent, listScroll);
    }

    void ShowMessage(AscendMail.Message m)
    {
        if (m == null || !AscendMail.All.Contains(m)) { ShowList(); return; }
        current = m;
        listView.SetActive(false);
        readerView.SetActive(true);
        Clear(readerContent);

        T(readerContent, "<noparse>" + m.subject + "</noparse>", 38, Txt, FontStyles.Bold);
        T(readerContent, "From: " + m.from + "\nTo: " + m.to + "\n" + m.time, 21, Sub);
        Le(NewImage("Line", readerContent, Panel2), -1, 2);

        if (m == assignedMsg) AddFloorBadge();

        var body = T(readerContent, m.body, 27, Txt);
        body.lineSpacing = 8f;

        if (m == choiceMsg)
            for (int i = 0; i < missions.Length; i++) AddMissionButton(i);

        AscendMail.MarkRead(m);
        UpdateInboxCount();
        ScrollTop(readerContent, readerScroll);
    }

    void AddFloorBadge()
    {
        var badge = NewImage("Floor Badge", readerContent, Panel2, 20f);
        var v = Vgroup(badge.rectTransform, 10, 10, 18, 18, 0);
        v.childAlignment = TextAnchor.MiddleCenter;
        T(badge.transform, "BRIEFING ROOM", 26, Sub, FontStyles.Bold).alignment = TextAlignmentOptions.Center;
        T(badge.transform, "FLOOR " + floor, 96, Gold, FontStyles.Bold).alignment = TextAlignmentOptions.Center;
    }

    void AddMissionButton(int index)
    {
        Mission m = missions[index];
        bool chosen = index == SelectedMissionIndex;
        bool locked = state != State.ChoosingMission;

        var img = NewImage("Mission " + (index + 1), readerContent, chosen ? Chosen : Accent, 20f);
        img.raycastTarget = true;
        if (locked && !chosen) img.color = new Color(0.35f, 0.35f, 0.4f, 0.6f);
        Le(img, -1, 120);
        Vgroup(img.rectTransform, 22, 22, 16, 16, 4);

        T(img.transform, m.title + (chosen ? "   (chosen)" : ""), 31, Color.white, FontStyles.Bold);
        if (!string.IsNullOrEmpty(m.subtitle)) T(img.transform, m.subtitle, 22, new Color(1f, 1f, 1f, 0.85f));

        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.interactable = !locked;
        var cb = b.colors;
        cb.disabledColor = Color.white; // keep our own colors when locked
        b.colors = cb;
        b.onClick.AddListener(() => ChooseMission(index));
    }

    void UpdateInboxCount()
    {
        int unread = AscendMail.Unread;
        inboxCount.text = unread > 0 ? "Inbox (" + unread + ")" : "Inbox";
    }

    // ================= Helpers =================

    void SetBusy(bool busy, bool showCursor)
    {
        if (busy && !isBusy) { savedLock = Cursor.lockState; savedVisible = Cursor.visible; }

        if (busy)
        {
            Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = showCursor;
        }
        else if (isBusy)
        {
            Cursor.lockState = savedLock;
            Cursor.visible = savedVisible;
        }
        isBusy = busy;

        if (disableWhileBusy != null) foreach (var b in disableWhileBusy) if (b) b.enabled = !busy;
        if (hideWhileBusy != null) foreach (var g in hideWhileBusy) if (g) g.SetActive(!busy);
        hintText.gameObject.SetActive(!busy);
    }

    void Flash(string message)
    {
        if (flash != null) StopCoroutine(flash);
        flash = StartCoroutine(FlashRoutine(message));
    }

    IEnumerator FlashRoutine(string message)
    {
        hintText.text = message;
        yield return new WaitForSecondsRealtime(2.5f);
        hintText.text = "";
        flash = null;
    }

    static void ScrollTop(RectTransform content, ScrollRect scroll)
    {
        if (!content.gameObject.activeInHierarchy) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.verticalNormalizedPosition = 1f;
    }

    static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var c = t.GetChild(i).gameObject;
            c.SetActive(false);
            Destroy(c);
        }
    }

    static string KeyName(KeyCode k)
    {
        if (k == KeyCode.Mouse0) return "Click";
        return "Press " + k;
    }

    static bool KeyDown(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        if (k == KeyCode.Mouse0) return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (k == KeyCode.Mouse1) return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        var kb = Keyboard.current;
        if (kb == null) return false;
        string n = k.ToString();
        if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
        else if (n == "Return") n = "Enter";
        if (Enum.TryParse(n, out Key key) && key != Key.None) return kb[key].wasPressedThisFrame;
        return false;
#else
        return Input.GetKeyDown(k);
#endif
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
#if ENABLE_INPUT_SYSTEM
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
    }

    static Color Hex(string h) { return ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta; }

    // ================= UI building =================

    void BuildUI()
    {
        var canvasGO = new GameObject("MissionBriefing UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)canvasGO.transform;

        // Interact hint, small, at the bottom of the screen
        hintText = T(root, "", 24, Color.white, FontStyles.Bold);
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.outlineWidth = 0.2f;
        hintText.outlineColor = new Color32(0, 0, 0, 220);
        var hrt = hintText.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0f);
        hrt.sizeDelta = new Vector2(1200, 44);
        hrt.anchoredPosition = new Vector2(0, 50);

        BuildPhone(root);
        BuildBriefing(root);
        BuildFloorScreen(root);

        // Fade to black (last, so it covers everything)
        var fadeImg = NewImage("Fade", root, Color.black);
        Stretch(fadeImg.rectTransform);
        fade = fadeImg.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.blocksRaycasts = false;
        fade.interactable = false;
    }

    void BuildPhone(RectTransform root)
    {
        var layer = NewRect("Phone Layer", root);
        Stretch(layer);
        phoneRoot = layer.gameObject;

        // "Press E to exit phone", top left of the screen
        var exitBox = NewImage("Exit Hint", layer, new Color(0, 0, 0, 0.92f), 20f);
        var ert = exitBox.rectTransform;
        ert.anchorMin = ert.anchorMax = ert.pivot = new Vector2(0f, 1f);
        ert.sizeDelta = new Vector2(420, 76); ert.anchoredPosition = new Vector2(30, -30);
        var exit = T(ert, KeyName(closePhoneKey) + " to exit phone", 32, Color.white, FontStyles.Bold);
        Stretch(exit.rectTransform, 24, 0, 24, 0);
        exit.alignment = TextAlignmentOptions.MidlineLeft;

        var phone = NewImage("Phone", layer, Bezel, 70f);
        var prt = phone.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(640, 1030);

        var screen = NewImage("Screen", prt, Bg, 54f);
        Stretch(screen.rectTransform, 16, 16, 16, 16);
        screen.gameObject.AddComponent<Mask>().showMaskGraphic = true; // clip the header to the rounded corners
        var srt = screen.rectTransform;

        // Header: PhMail logo + inbox count
        var header = NewImage("Header", srt, Panel);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = Vector2.one; hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 96); hrt.anchoredPosition = Vector2.zero;
        var logo = T(hrt, "<color=#4285F4>Ph</color>Mail", 44, Txt, FontStyles.Bold);
        Stretch(logo.rectTransform, 26, 0, 26, 0);
        logo.alignment = TextAlignmentOptions.MidlineLeft;
        inboxCount = T(hrt, "Inbox", 24, Sub, FontStyles.Bold);
        Stretch(inboxCount.rectTransform, 26, 0, 26, 0);
        inboxCount.alignment = TextAlignmentOptions.MidlineRight;

        // Body under the header
        var body = NewRect("Body", srt);
        Stretch(body, 0, 24, 0, 96);

        // Inbox list view
        var lv = NewRect("Inbox View", body);
        Stretch(lv);
        listView = lv.gameObject;
        listContent = MakeScroll(lv, 0, out listScroll, 14, 14, 14, 14, 10);

        // Reader view: back bar + scrollable email
        var rv = NewRect("Reader View", body);
        Stretch(rv);
        readerView = rv.gameObject;

        var bar = NewImage("Back Bar", rv, Bg);
        var barRt = bar.rectTransform;
        barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = Vector2.one; barRt.pivot = new Vector2(0.5f, 1);
        barRt.sizeDelta = new Vector2(0, 76); barRt.anchoredPosition = Vector2.zero;
        var divider = NewImage("Divider", barRt, Panel2);
        var drt = divider.rectTransform;
        drt.anchorMin = Vector2.zero; drt.anchorMax = new Vector2(1, 0); drt.pivot = new Vector2(0.5f, 0);
        drt.sizeDelta = new Vector2(0, 2); drt.anchoredPosition = Vector2.zero;

        // Flat "< Inbox" link: a drawn chevron + blue text, with a soft highlight on hover
        var backImg = NewImage("Back", barRt, new Color(1f, 1f, 1f, 0f), 18f);
        backImg.raycastTarget = true;
        var brt = backImg.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 0.5f);
        brt.sizeDelta = new Vector2(170, 56); brt.anchoredPosition = new Vector2(10, 0);
        var back = backImg.gameObject.AddComponent<Button>();
        back.targetGraphic = backImg;
        back.navigation = new Navigation { mode = Navigation.Mode.None };
        var bcb = back.colors;
        bcb.normalColor = new Color(1f, 1f, 1f, 0f);
        bcb.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
        bcb.pressedColor = new Color(1f, 1f, 1f, 0.16f);
        bcb.selectedColor = new Color(1f, 1f, 1f, 0f);
        bcb.colorMultiplier = 1f;
        back.colors = bcb;
        back.onClick.AddListener(ShowList);

        Color link = Hex("#8AB4F8");
        for (int i = 0; i < 2; i++)
        {
            var stroke = NewImage("Chevron", brt, link, 2f);
            var crt = stroke.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0, 0.5f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.sizeDelta = new Vector2(20, 4);
            crt.anchoredPosition = new Vector2(22, 0);
            crt.localRotation = Quaternion.Euler(0, 0, i == 0 ? 45f : -45f);
        }
        var backText = T(brt, "Inbox", 28, link, FontStyles.Bold);
        Stretch(backText.rectTransform, 52, 0, 0, 0);
        backText.alignment = TextAlignmentOptions.MidlineLeft;

        readerContent = MakeScroll(rv, 76, out readerScroll, 26, 26, 10, 26, 16);
    }

    // Scroll view with a permanent scrollbar on the right. Returns the content to fill.
    RectTransform MakeScroll(RectTransform parent, float topInset, out ScrollRect scroll, int padL, int padR, int padT, int padB, float spacing)
    {
        var sroot = NewImage("Scroll", parent, new Color(0, 0, 0, 0));
        sroot.raycastTarget = true;
        var rt = sroot.rectTransform;
        Stretch(rt, 0, 0, 0, topInset);

        scroll = rt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        var viewport = NewRect("Viewport", rt);
        Stretch(viewport, 0, 0, 16, 0);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        Vgroup(content, padL, padR, padT, padB, spacing);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;

        var bar = NewImage("Scrollbar", rt, Hex("#141518"), 6f);
        bar.raycastTarget = true;
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(1, 0); brt.anchorMax = Vector2.one; brt.pivot = new Vector2(1, 0.5f);
        brt.sizeDelta = new Vector2(12, -8); brt.anchoredPosition = new Vector2(-2, 0);
        var sliding = NewRect("Sliding Area", brt);
        Stretch(sliding);
        var handle = NewImage("Handle", sliding, Hex("#6E7180"), 6f);
        handle.raycastTarget = true;
        Stretch(handle.rectTransform);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        return content;
    }

    void BuildBriefing(RectTransform root)
    {
        var briefing = NewRect("Briefing", root);
        Stretch(briefing);
        briefingRoot = briefing.gameObject;

        // Subtitle box at the bottom
        var box = NewImage("Subtitle Box", briefing, new Color(0, 0, 0, 0.92f), 24f);
        var brt = box.rectTransform;
        brt.anchorMin = new Vector2(0.08f, 0); brt.anchorMax = new Vector2(0.92f, 0); brt.pivot = new Vector2(0.5f, 0);
        brt.sizeDelta = new Vector2(0, 320); brt.anchoredPosition = new Vector2(0, 36);

        speakerText = T(brt, "", 34, Gold, FontStyles.Bold);
        Stretch(speakerText.rectTransform, 36, 250, 36, 22);
        subtitleText = T(brt, "", 42, Color.white);
        Stretch(subtitleText.rectTransform, 36, 50, 36, 78);
        progressText = T(brt, "", 24, Sub);
        Stretch(progressText.rectTransform, 36, 14, 36, 14);
        progressText.alignment = TextAlignmentOptions.BottomRight;

        // Key hints, top right
        var keys = NewImage("Key Hints", briefing, new Color(0, 0, 0, 0.92f), 20f);
        var krt = keys.rectTransform;
        krt.anchorMin = krt.anchorMax = krt.pivot = Vector2.one;
        krt.sizeDelta = new Vector2(560, 140); krt.anchoredPosition = new Vector2(-30, -30);
        var skip = T(krt, "Press " + skipKey + " to skip Briefing", 32, Color.white, FontStyles.Bold);
        Stretch(skip.rectTransform, 24, 72, 24, 12);
        skip.alignment = TextAlignmentOptions.MidlineRight;
        var cont = T(krt, "Press " + continueKey + " to continue", 32, Color.white, FontStyles.Bold);
        Stretch(cont.rectTransform, 24, 12, 24, 72);
        cont.alignment = TextAlignmentOptions.MidlineRight;
    }

    void BuildFloorScreen(RectTransform root)
    {
        var dim = NewImage("Floor Screen", root, new Color(0, 0, 0, 0.92f));
        Stretch(dim.rectTransform);
        floorRoot = dim.gameObject;
        var v = Vgroup(dim.rectTransform, 0, 0, 0, 0, 0);
        v.childAlignment = TextAnchor.MiddleCenter;

        var going = T(dim.transform, "GOING TO", 72, Color.white, FontStyles.Bold);
        going.alignment = TextAlignmentOptions.Center;
        going.outlineWidth = 0.15f;
        going.outlineColor = new Color32(0, 0, 0, 230);

        floorNumberText = T(dim.transform, "FLOOR 1", 220, Gold, FontStyles.Bold);
        floorNumberText.alignment = TextAlignmentOptions.Center;
        floorNumberText.outlineWidth = 0.15f;
        floorNumberText.outlineColor = new Color32(0, 0, 0, 230);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // radius = corner radius in pixels (0 = square corners)
    static Image NewImage(string name, Transform parent, Color color, float radius = 0f)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        if (radius > 0f)
        {
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            img.pixelsPerUnitMultiplier = SpriteRadius / radius;
        }
        return img;
    }

    const float SpriteRadius = 64f;
    static Sprite roundSprite;
    static Sprite RoundSprite
    {
        get
        {
            if (roundSprite) return roundSprite;
            const int size = 160;
            float r = SpriteRadius;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, 0f, fx - (size - r));
                float dy = Mathf.Max(r - fy, 0f, fy - (size - r));
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            tex.SetPixels32(px);
            tex.Apply();
            roundSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return roundSprite;
        }
    }

    TextMeshProUGUI T(Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var t = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = text;
        t.fontSize = size * textScale;
        t.color = color;
        t.fontStyle = style;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.alignment = TextAlignmentOptions.TopLeft;
        return t;
    }

    static VerticalLayoutGroup Vgroup(RectTransform rt, int l, int r, int t, int b, float spacing)
    {
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(l, r, t, b);
        v.spacing = spacing;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return v;
    }

    static void Le(Component c, float w, float h)
    {
        var le = c.gameObject.AddComponent<LayoutElement>();
        if (w >= 0) le.minWidth = w;
        if (h >= 0) le.minHeight = h;
    }

    static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    // ================= Default text =================

    [ContextMenu("Reset missions and welcome email to defaults")]
    void ResetTexts()
    {
        missions = DefaultMissions();
        welcomeEmail = DefaultWelcome;
    }

    const string DefaultWelcome =
        "Dear Astronaut,\n\n" +
        "Welcome to <b>Ascend Philippines</b>! We are very happy to have you on our team.\n\n" +
        "Our scientists have three missions that need a brave astronaut like you. " +
        "Each one will teach us something new about our planet and our Moon.\n\n" +
        "Please read the missions below and tap the one you would like to lead.\n\n" +
        "Kind regards,\n" +
        "Mission Control\n" +
        "Ascend Philippines";

    static Mission[] DefaultMissions()
    {
        return new[]
        {
            new Mission
            {
                title = "Sky Sensor (CanSat)",
                subtitle = "Easy  |  Measure the air while falling from the sky",
                briefingLines = new[]
                {
                    "Welcome to the Briefing Room, astronaut! Today you will learn about your mission: Sky Sensor.",
                    "Your job is to build a CanSat. A CanSat is a tiny satellite, about the size of a soda can!",
                    "A small rocket will carry your CanSat high into the sky, about one kilometre up. That is higher than the tallest building in the world!",
                    "At the top, your CanSat pops out of the rocket and opens its parachute.",
                    "While it floats down, its sensors measure the air: how warm it is, and how hard the air is pushing. We call that air pressure.",
                    "Fun fact: the higher you go, the colder the air gets and the thinner it becomes.",
                    "Your CanSat uses a little radio to send its measurements to our ground station, so scientists can read them right away.",
                    "Be careful when you build it! If the parachute is too small, your CanSat will fall too fast and might break when it lands.",
                    "Real scientists send up balloons and small probes like this every day to help forecast the weather.",
                    "First, you will design your CanSat and choose its parts. Good luck, astronaut!"
                },
                summaryEmail =
                    "<b>Mission:</b> Sky Sensor (CanSat)\n<b>Difficulty:</b> Easy\n\n" +
                    "<b>YOUR GOALS</b>\n" +
                    "- Launch your CanSat about 1 km high\n" +
                    "- Open the parachute\n" +
                    "- Measure air temperature and air pressure while falling\n" +
                    "- Send your data to the ground station by radio\n" +
                    "- Land softly without breaking\n\n" +
                    "<b>WHAT YOU WILL BUILD</b>\n" +
                    "<b>Rocket:</b> a small rocket that can lift a can-sized payload\n" +
                    "<b>Payload:</b> your CanSat (about the size of a soda can)\n" +
                    "<b>Science instruments:</b> temperature sensor, air pressure sensor\n" +
                    "<b>Other parts:</b> parachute, battery, small radio\n\n" +
                    "<b>TIPS</b>\n" +
                    "- A bigger parachute means a softer landing, but it adds weight.\n" +
                    "- Keep your CanSat light so the rocket can lift it easily.\n\n" +
                    "<b>Next step:</b> go to the computer and open AscendCAD to build. Good luck!\n" +
                    "Mission Control"
            },
            new Mission
            {
                title = "Earth Watcher",
                subtitle = "Medium  |  Take pictures of Earth from orbit",
                briefingLines = new[]
                {
                    "Welcome, astronaut! Your mission is called Earth Watcher.",
                    "You will send a satellite into orbit around Earth. Orbit means it keeps falling around the planet without ever hitting the ground!",
                    "Your satellite will fly about 700 kilometres above Earth. That is way higher than any airplane can go.",
                    "To stay in orbit it must move super fast: about 27,000 kilometres per hour!",
                    "Your satellite carries a special camera. It can see colours our eyes can't see, which helps scientists check if forests and farms are healthy.",
                    "Its solar panels turn sunlight into electricity, and its antenna sends the pictures back to Earth.",
                    "You will need a bigger rocket this time, because getting to orbit takes a lot of energy.",
                    "NASA has a real mission just like this called Landsat. Landsat satellites have been taking pictures of Earth since 1972!",
                    "Remember: every part adds weight, and a heavier satellite needs a bigger, more expensive rocket.",
                    "Now let's go design your satellite. Good luck, astronaut!"
                },
                summaryEmail =
                    "<b>Mission:</b> Earth Watcher\n<b>Difficulty:</b> Medium\n\n" +
                    "<b>YOUR GOALS</b>\n" +
                    "- Launch your satellite into orbit, about 700 km above Earth\n" +
                    "- Open the solar panels to get power\n" +
                    "- Take pictures of Earth's land, forests and farms\n" +
                    "- Send the pictures to the ground station\n\n" +
                    "<b>WHAT YOU WILL BUILD</b>\n" +
                    "<b>Rocket:</b> a medium rocket that can reach orbit\n" +
                    "<b>Payload:</b> an Earth-watching satellite\n" +
                    "<b>Science instruments:</b> Earth camera (sees normal and infrared light)\n" +
                    "<b>Other parts:</b> solar panels, battery, antenna, small thrusters\n\n" +
                    "<b>TIPS</b>\n" +
                    "- Bigger solar panels give more power, but they add weight.\n" +
                    "- Real mission like yours: NASA's Landsat.\n\n" +
                    "<b>Next step:</b> go to the computer and open AscendCAD to build. Good luck!\n" +
                    "Mission Control"
            },
            new Mission
            {
                title = "Moon Mapper",
                subtitle = "Hard  |  Fly to the Moon and map its surface",
                briefingLines = new[]
                {
                    "Welcome, astronaut! This is our hardest mission: Moon Mapper.",
                    "You will send a spacecraft all the way to the Moon. The Moon is about 384,000 kilometres away!",
                    "The trip takes about four to five days, so your spacecraft needs lots of fuel.",
                    "When it arrives, it must fire its engine to slow down. If it is too fast, it will fly right past the Moon!",
                    "Then it will circle the Moon only about 50 kilometres above the ground, and make a map of the whole surface.",
                    "Your spacecraft carries a camera to take pictures, and a laser that measures how tall the mountains are and how deep the craters go.",
                    "Radio messages take more than one second to travel between the Moon and Earth, so your spacecraft must be smart enough to do some work on its own.",
                    "NASA's Lunar Reconnaissance Orbiter, called LRO, has been mapping the Moon like this since 2009. Its maps help choose safe landing spots for astronauts.",
                    "This mission needs the biggest rocket and the most careful planning. Every kilogram counts!",
                    "Now let's go design your Moon mission. Good luck, astronaut!"
                },
                summaryEmail =
                    "<b>Mission:</b> Moon Mapper\n<b>Difficulty:</b> Hard\n\n" +
                    "<b>YOUR GOALS</b>\n" +
                    "- Launch on a big rocket and travel about 384,000 km to the Moon\n" +
                    "- Fire your engine to slow down and go into orbit around the Moon\n" +
                    "- Circle the Moon about 50 km above its surface\n" +
                    "- Map the surface with your camera and laser\n" +
                    "- Send your maps back to Earth\n\n" +
                    "<b>WHAT YOU WILL BUILD</b>\n" +
                    "<b>Rocket:</b> a heavy rocket that can reach the Moon\n" +
                    "<b>Payload:</b> a Moon-orbiting spacecraft\n" +
                    "<b>Science instruments:</b> mapping camera, laser altimeter (measures height)\n" +
                    "<b>Other parts:</b> big fuel tank, main engine, solar panels, battery, large antenna\n\n" +
                    "<b>TIPS</b>\n" +
                    "- More fuel lets you slow down safely, but it makes the rocket heavier.\n" +
                    "- Real mission like yours: NASA's Lunar Reconnaissance Orbiter (LRO).\n\n" +
                    "<b>Next step:</b> go to the computer and open AscendCAD to build. Good luck!\n" +
                    "Mission Control"
            }
        };
    }
}
