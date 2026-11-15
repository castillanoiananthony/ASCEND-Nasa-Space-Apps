// ═════════════════════════════════════════════════════════════════════════════
//  MissionBriefing.cs  -  ASCEND opening flow: office, phone, mission choice, briefing.
//
//  Flow:
//   1. (From the Main Menu story) fade in: you sit in the office chair at your computer.
//   2. Notification (top left): new PhMail email.  TIP: press P to open your phone.
//   3. Phone home screen: PhMail (red badge) and ASCEND BANK.  TIP: click PhMail.
//   4. Open the email and choose a mission (Easy / Medium / Hard). You can change it
//      any time until you enter the meeting room.
//   5. Close the phone (P).  TIP: go to the Meeting Room. The door glows + has a sign.
//   6. Press E at the door: fade, you are in the meeting room. Briefing (SPACE / S).
//   7. The mission fund is deposited (ASCEND BANK), a detailed summary + fund statement
//      arrives in PhMail.  TIP: leave the meeting room (the exit door glows).
//
//  The chosen mission is available to other scripts as MissionBriefing.SelectedMission.
// ═════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
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
        [Tooltip("PhMail rich text is allowed, like <b>bold</b>. The fund statement is added automatically.")]
        [TextArea(6, 25)] public string summaryEmail = "";
    }

    public enum Stage { Intro, ChooseMission, GoToMeeting, Briefing, LeaveMeeting, Free }

    // A person in the meeting room: model + animations (Idle, Interact = talking with hands, Wave)
    [Serializable]
    public class Actor
    {
        public GameObject model;
        public AnimationClip idle, talk, wave;
        public float height = 1.72f;
    }

    [Header("PhMail")]
    [Tooltip("Your AscendCAD Data asset (for the PhMail addresses and the mission fund). Leave empty to use the one on the computer.")]
    public AscendCADData ascendCADData;
    public string senderName = "ASCEND Mission Control";

    [Header("Office start (sitting at your desk)")]
    [Tooltip("Optional: an empty object on the floor under the office chair, blue arrow toward the computer.")]
    public Transform officeChairSeat;
    [Tooltip("Floor point under the office chair (used if Office Chair Seat is empty).")]
    public Vector3 chairFeetPosition = new Vector3(802.12f, 1.03f, 711.0f);
    [Tooltip("What you look at while sitting (the monitor).")]
    public Vector3 chairLookAt = new Vector3(802.35f, 2.45f, 709.58f);
    [Tooltip("Play the office intro when you press Play in the Hangar scene directly (for testing).")]
    public bool officeIntroWhenStartedDirectly = true;

    [Header("Meeting room door (needs a Collider)")]
    [Tooltip("The door in the office that leads to the meeting room. Press E on it.")]
    [FormerlySerializedAs("elevatorDoorBox")] public Collider meetingRoomDoor;
    [Tooltip("The door that leads out of the meeting room. Leave empty to find the door named 'Meeting Room'.")]
    public Collider meetingRoomExitDoor;
    public float interactDistance = 4f;
    [FormerlySerializedAs("elevatorKey")] public KeyCode interactKey = KeyCode.E;
    [Tooltip("Sign above the meeting room door (position, it faces into the office).")]
    public Vector3 meetingSignPosition = new Vector3(808.04f, 3.45f, 710.2f);
    public Vector3 meetingSignFacing = new Vector3(-1f, 0f, 0f);
    [Tooltip("Sign on the meeting room's exit door (position, it faces into the meeting room).")]
    public Vector3 exitSignPosition = new Vector3(807.97f, 2.75f, 725.03f);
    public Vector3 exitSignFacing = new Vector3(0f, 0f, -1f);
    public Color glowColor = new Color(0.35f, 0.85f, 1f);

    [Header("Player & teleports")]
    [Tooltip("Your player object (the one that moves). Leave empty to find it automatically.")]
    public Transform player;
    [Tooltip("Optional: an empty object in the meeting room. If empty, 'Meeting Room Position' is used.")]
    public Transform meetingRoomPoint;
    public Vector3 meetingRoomPosition = new Vector3(811f, 1.08f, 721.2f);
    public bool setMeetingRoomFacing = true;
    public float meetingRoomFacingY = -90f;
    public bool levelCameraOnTeleport = true;
    [Tooltip("Movement and mouse-look scripts to switch off while the phone or briefing is open.")]
    public Behaviour[] disableWhileBusy;
    [Tooltip("Things like the crosshair to hide while the phone or briefing is open.")]
    public GameObject[] hideWhileBusy;

    [Header("Keys")]
    [FormerlySerializedAs("closePhoneKey")] public KeyCode phoneKey = KeyCode.P;
    public KeyCode skipKey = KeyCode.S;
    public KeyCode continueKey = KeyCode.Space;

    [Header("Text & timing")]
    [Tooltip("Optional TextMesh Pro font. Leave empty to use the default TMP font.")]
    public TMP_FontAsset font;
    [Range(0.6f, 1.6f)] public float textScale = 1f;
    public string speakerName = "Director Isabel Reyes";
    [TextArea(6, 20)] public string welcomeEmail = DefaultWelcome;
    public float lettersPerSecond = 45f;
    public float fadeSeconds = 0.6f;

    [Header("Meeting room people")]
    [Tooltip("The Mission Director who briefs you (wears the black ASCEND uniform).")]
    public Actor director = new Actor { height = 1.68f };
    [Tooltip("Team members standing around the table.")]
    public Actor[] team = new Actor[0];
    [Range(0, 14)] public int teamSize = 10;
    [Tooltip("How far in front of you the director stands, and how far to the side.")]
    public float directorDistance = 3.0f, directorSide = 0.25f;

    [Header("Missions (Easy, Medium, Hard)")]
    public Mission[] missions = DefaultMissions();

    [Header("Events")]
    [Tooltip("Runs when the briefing ends.")]
    public UnityEvent onBriefingFinished;

    // ---------- shared with other scripts (kept while you go to other scenes and back) ----------
    public static Mission SelectedMission { get; private set; }
    public static int SelectedMissionIndex { get; private set; } = -1;
    public static Stage Progress { get; private set; } = Stage.Intro;
    static AscendMail.Message sChoiceMsg;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { SelectedMission = null; SelectedMissionIndex = -1; Progress = Stage.Intro; sChoiceMsg = null; }

    // Other scripts (e.g. the orbit mission) can show a tip on the left
    static MissionBriefing inst;
    public static void Hint(string text, float seconds = 10f) { if (inst) inst.ShowTip(text, seconds); }
    // Hide the office UI (tips, prompts, phone) while flying or in orbit
    public static void SetUiVisible(bool on) { if (inst && inst.uiCanvas) inst.uiCanvas.SetActive(on); }
    // The tip shown right now ("" if none)
    public static string CurrentTip => inst && inst.tipRoot && inst.tipRoot.activeSelf && inst.tipText ? inst.tipText.text : "";
    GameObject uiCanvas;

    // DEV: skip the office + briefing (mission picked, fund paid, guide set to "free")
    public static void DevSkipBriefing(int missionIndex)
    {
        if (!inst) return;
        inst.StopAllCoroutines();
        inst.transitioning = false;
        if (inst.fade) inst.fade.alpha = 0f;
        IntroStory.Remove();
        if (Progress == Stage.Intro) Progress = Stage.ChooseMission;
        if (Progress <= Stage.GoToMeeting) inst.ChooseMission(missionIndex);
        if (Progress < Stage.LeaveMeeting) inst.EndBriefing();
        Progress = Stage.Free;
        if (inst.phoneOpen) inst.ClosePhone();
        inst.ShowTip("");
        inst.RefreshObjective();
        if (inst.cast) Destroy(inst.cast.gameObject);
    }

    // Called by the Main Menu when a world is opened: start the story from the beginning
    public static void ResetForNewGame() => ResetStatics();

    // ---------- phone colors ----------
    static readonly Color Bezel = Hex("#0B0B0E");
    static readonly Color Bg = Hex("#18191C");
    static readonly Color Panel = Hex("#212226");
    static readonly Color Panel2 = Hex("#2E2F35");
    static readonly Color Txt = Hex("#E8EAED");
    static readonly Color Sub = Hex("#9AA0A6");
    static readonly Color Accent = Hex("#0B57D0");
    static readonly Color Chosen = Hex("#1E8E3E");
    static readonly Color Gold = Hex("#FFC850");
    static readonly Color RowBg = new Color(1f, 1f, 1f, 0.04f);

    // ---------- runtime ----------
    bool phoneOpen, isBusy, transitioning, isTyping, listDirty, phoneTipShown, appTipShown;
    int lineIndex;
    string[] lines;
    CursorLockMode savedLock; bool savedVisible;
    AscendCADData data;
    [NonSerialized] AscendMail.Message summaryMsg, current;
    PlayerController pc;

    GameObject phoneRoot, homeView, mailView, bankView, listView, readerView, briefingRoot;
    RectTransform listContent, readerContent, notifyRoot;
    ScrollRect listScroll, readerScroll;
    AscendBankScreen bankScreen;
    GameObject seatHint;
    TextMeshProUGUI hintText, tipText, inboxCount, speakerText, subtitleText, progressText, clockText, dateText, statusTime, badgeText;
    GameObject tipRoot, badge;
    CanvasGroup fade;
    Coroutine typing, flash;
    readonly Queue<(Sprite icon, string app, string title, string body)> notes = new Queue<(Sprite, string, string, string)>();
    bool notifying;
    DoorGlow meetGlow, exitGlow;
    MeetingCast cast;

    // ================= Unity =================

    void Awake()
    {
        AscendMail.SkipWelcome();                                   // this flow sends its own emails
        if (Progress < Stage.LeaveMeeting) AscendEconomy.HoldFund(); // the fund arrives after the briefing
    }

    void OnEnable()
    {
        AscendMail.Changed += OnMailChanged;
        AscendMail.Received += OnMailReceived;
        AscendEconomy.FundDeposited += OnFundDeposited;
    }

    void OnDisable()
    {
        AscendMail.Changed -= OnMailChanged;
        AscendMail.Received -= OnMailReceived;
        AscendEconomy.FundDeposited -= OnFundDeposited;
    }

    void OnMailChanged() { listDirty = true; UpdateBadge(); }

    void Start()
    {
        if (GameFlow.PlayTrailer) { enabled = false; return; }   // the trailer takes over the scene
        inst = this;
        if (!font && ascendCADData) font = ascendCADData.labelFont;
        pc = PlayerController.Instance ? PlayerController.Instance : FindAnyObjectByType<PlayerController>();
        if (!player) player = pc ? pc.transform : (GameObject.FindWithTag("Player") ? GameObject.FindWithTag("Player").transform : null);
        if (!meetingRoomExitDoor)
            foreach (var d in FindObjectsByType<DoorInteraction>(FindObjectsInactive.Exclude))
                if (d.name.Trim() == "Meeting Room") { meetingRoomExitDoor = d.GetComponent<Collider>(); break; }
        if (!meetingRoomDoor) Debug.LogWarning("MissionBriefing: drag the office door that leads to the meeting room into 'Meeting Room Door'.", this);
        if (missions == null || missions.Length == 0) missions = DefaultMissions();
        if (SelectedMissionIndex >= 0 && SelectedMissionIndex < missions.Length) SelectedMission = missions[SelectedMissionIndex];

        try { AscendEconomy.Init(Data); } catch (Exception e) { Debug.LogException(e); }

        EnsureEventSystem();
        BuildUI();
        phoneRoot.SetActive(false);
        briefingRoot.SetActive(false);
        BuildDoorSigns();
        gameObject.AddComponent<AscendGuide>().Init(this, font);   // guidance after the briefing

        if (Progress == Stage.Intro)
        {
            bool intro = GameFlow.PlayOfficeIntro || (Application.isEditor && officeIntroWhenStartedDirectly);
            GameFlow.PlayOfficeIntro = false;
            StartCoroutine(OfficeIntro(intro));
        }
        else
        {
            IntroStory.Remove();
            RefreshObjective();
        }
    }

    void Update()
    {
        if (transitioning) return;

        if (Progress == Stage.Briefing) { BriefingInput(); return; }

        if (phoneOpen)
        {
            if (listDirty && listView.activeInHierarchy) RebuildList();
            if (clockText && homeView.activeSelf) UpdateClock();
            if (KeyDown(phoneKey) && !AUI.Typing()) ClosePhone();
            return;
        }

        // "SPACE  Stand up" while sitting in the chair
        if (seatHint) seatHint.SetActive(pc && pc.Seated && pc.enabled && Cursor.lockState == CursorLockMode.Locked);

        // Open the phone (only when walking around: no computer / console open)
        if (Progress != Stage.Intro && KeyDown(phoneKey) && Cursor.lockState == CursorLockMode.Locked && !AUI.Typing())
        {
            OpenPhone();
            return;
        }

        // Meeting room door
        bool lookDoor = LookingAt(meetingRoomDoor);
        if (flash == null)
        {
            if (lookDoor)
                hintText.text = Progress == Stage.GoToMeeting ? KeyName(interactKey) + " to enter the Meeting Room"
                              : Progress == Stage.ChooseMission ? "Meeting Room  -  choose your mission first (" + KeyName(phoneKey) + ")"
                              : Progress >= Stage.LeaveMeeting ? "Meeting Room  -  briefing complete" : "";
            else hintText.text = "";
        }
        if (lookDoor && KeyDown(interactKey))
        {
            if (Progress == Stage.GoToMeeting) StartCoroutine(EnterMeeting());
            else if (Progress == Stage.ChooseMission) Flash("Pick a mission first. Press " + phoneKey + " to open your phone.");
        }

        // Left the meeting room?
        if (Progress == Stage.LeaveMeeting && PlayerBody() && HorizontalDistance(PlayerBody().position, MeetingPos) > 7f)
        {
            Progress = Stage.Free;
            RefreshObjective();
            if (cast) Destroy(cast.gameObject);   // the team goes back to work; AscendGuide shows the next tip
        }
    }

    Vector3 MeetingPos => meetingRoomPoint ? meetingRoomPoint.position : meetingRoomPosition;
    static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // ================= Office intro =================

    IEnumerator OfficeIntro(bool playIntro)
    {
        transitioning = true;
        fade.alpha = 1f;
        IntroStory.Remove();
        // let the player controller settle (it snaps to the floor on start)
        for (int i = 0; i < 3; i++) yield return null;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        SitInChair();
        if (playIntro)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Fade(1f, 0f, 1.8f);
            yield return new WaitForSecondsRealtime(1.2f);
        }
        else fade.alpha = 0f;
        transitioning = false;

        Progress = Stage.ChooseMission;
        EnsureChoiceEmail();          // arrives as a notification
        yield return new WaitForSecondsRealtime(1.0f);
        if (!phoneOpen) { ShowTip("Press <b>" + phoneKey + "</b> to open your phone."); phoneTipShown = true; }
    }

    void SitInChair()
    {
        if (!pc) return;
        Vector3 feet = officeChairSeat ? officeChairSeat.position : chairFeetPosition;
        Vector3 look = officeChairSeat ? officeChairSeat.position + officeChairSeat.forward * 1.5f + Vector3.up * 1.35f : chairLookAt;
        pc.SitAt(feet, look, FindStandPoint(feet, look));
    }

    // A free spot next to the chair to stand up into
    Vector3 FindStandPoint(Vector3 feet, Vector3 look)
    {
        var cc = pc.GetComponent<CharacterController>();
        float r = cc ? cc.radius : 0.3f, h = cc ? cc.height : 1.7f;
        Vector3 fwd = look - feet; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3[] tries = { -right * 1.0f, right * 1.0f, -fwd * 0.9f, -right * 1.0f - fwd * 0.6f, right * 1.0f - fwd * 0.6f, -fwd * 1.4f };
        foreach (var o in tries)
        {
            Vector3 p = feet + o;
            Vector3 a = p + Vector3.up * (r + 0.05f), b = p + Vector3.up * (h - r);
            if (!Physics.CheckCapsule(a, b, r, ~0, QueryTriggerInteraction.Ignore)) return p;
        }
        return feet - fwd * 0.9f;
    }

    // ================= Mail & notifications =================

    AscendCADData Data
    {
        get
        {
            if (data) return data;
            data = ascendCADData ? AscendCADData.Resolve(ascendCADData) : (AscendCADData.Active ? AscendCADData.Active : AscendCADData.Resolve(null));
            return data;
        }
    }

    AscendMail.Message SendMail(string subject, string body) => AscendMail.Deliver(Data.agencyEmail, Data.playerEmail, subject, body);

    void EnsureChoiceEmail()
    {
        if (sChoiceMsg != null && AscendMail.All.Contains(sChoiceMsg)) return;
        sChoiceMsg = SendMail("Pick your first mission", welcomeEmail);
    }

    void OnMailReceived(AscendMail.Message m)
    {
        if (m == null || m.outgoing) return;
        if (phoneOpen && mailView && mailView.activeSelf) return;
        string from = Data != null && m.from == Data.agencyEmail ? senderName : m.from;
        Notify(PhoneArt.MailIcon, "PhMail", "New email from " + from, m.subject);
    }

    void OnFundDeposited(double amount)
    {
        Notify(PhoneArt.BankIcon, "ASCEND BANK", "Deposit received: " + AscendReport.MoneyFull(amount),
               "Government of the Philippines - mission fund. Available now in your ASCEND BANK account.");
    }

    void Notify(Sprite icon, string app, string title, string body)
    {
        notes.Enqueue((icon, app, title, body));
        if (!notifying) StartCoroutine(NotifyRoutine());
    }

    IEnumerator NotifyRoutine()
    {
        notifying = true;
        while (notes.Count > 0)
        {
            var n = notes.Dequeue();
            var card = NewImage("Notification", notifyRoot, new Color(0.13f, 0.13f, 0.15f, 0.97f), 26f);
            var rt = card.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(480, 128);
            var edge = NewImage("Edge", rt, new Color(1f, 1f, 1f, 0.08f), 26f); Stretch(edge.rectTransform, -1, -1, -1, -1); edge.transform.SetAsFirstSibling();
            var ic = NewImage("Icon", rt, Color.white, 0f); ic.sprite = n.icon; ic.preserveAspect = true;
            var irt = ic.rectTransform; irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0f, 0.5f); irt.sizeDelta = new Vector2(76, 76); irt.anchoredPosition = new Vector2(22, 0);
            var head = T(rt, n.app.ToUpperInvariant() + "   <color=#9AA0A6>now</color>", 17, Sub, FontStyles.Bold);
            Stretch(head.rectTransform, 116, 0, 20, 16); head.characterSpacing = 3f;
            var title = T(rt, n.title, 22, Txt, FontStyles.Bold);
            Stretch(title.rectTransform, 116, 0, 20, 42); title.textWrappingMode = TextWrappingModes.NoWrap; title.overflowMode = TextOverflowModes.Ellipsis;
            var body = T(rt, n.body, 18, Sub);
            Stretch(body.rectTransform, 116, 8, 20, 74); body.overflowMode = TextOverflowModes.Ellipsis;

            float t = 0f;
            while (t < 0.35f) { t += Time.unscaledDeltaTime; rt.anchoredPosition = new Vector2(Mathf.Lerp(-520f, 28f, Ease(t / 0.35f)), -28f); yield return null; }
            yield return new WaitForSecondsRealtime(6f);
            t = 0f;
            while (t < 0.3f) { t += Time.unscaledDeltaTime; rt.anchoredPosition = new Vector2(Mathf.Lerp(28f, -520f, Ease(t / 0.3f)), -28f); yield return null; }
            Destroy(card.gameObject);
            yield return new WaitForSecondsRealtime(0.2f);
        }
        notifying = false;
    }

    static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }

    // ================= Tips =================

    void ShowTip(string text, float seconds = 0f)
    {
        tipText.text = text;
        tipRoot.SetActive(!string.IsNullOrEmpty(text));
        if (seconds > 0f) StartCoroutine(HideTipLater(text, seconds));
    }

    IEnumerator HideTipLater(string text, float s)
    {
        yield return new WaitForSecondsRealtime(s);
        if (tipText.text == text) ShowTip("");
    }

    // Door glows / tips that match the current stage
    void RefreshObjective()
    {
        if (meetGlow) meetGlow.On = Progress == Stage.GoToMeeting;
        if (exitGlow) exitGlow.On = Progress == Stage.LeaveMeeting;
    }

    // ================= Phone =================

    void OpenPhone()
    {
        phoneOpen = true;
        hintText.text = "";
        phoneRoot.SetActive(true);
        SetBusy(true, true);
        ShowHome();
        if (Progress == Stage.ChooseMission) { ShowTip("Click <b>PhMail</b> to read your email."); appTipShown = true; }
    }

    void ClosePhone()
    {
        phoneOpen = false;
        phoneRoot.SetActive(false);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        SetBusy(false, false);

        if (Progress == Stage.GoToMeeting)
        {
            ShowTip("Go to the glowing <b>Meeting Room</b> door.");
            RefreshObjective();
        }
        else if (Progress == Stage.ChooseMission) ShowTip("Press <b>" + phoneKey + "</b> and pick a mission in PhMail.");
        else if (Progress == Stage.LeaveMeeting) ShowTip("Leave through the glowing door.");
    }

    void ShowHome()
    {
        homeView.SetActive(true); mailView.SetActive(false); bankView.SetActive(false);
        UpdateBadge(); UpdateClock();
    }

    void OpenMail()
    {
        homeView.SetActive(false); bankView.SetActive(false); mailView.SetActive(true);
        if (Progress == Stage.ChooseMission && sChoiceMsg != null && !sChoiceMsg.read) { ShowMessage(sChoiceMsg); ShowTip("Pick a mission."); }
        else ShowList();
    }

    void OpenBank()
    {
        homeView.SetActive(false); mailView.SetActive(false); bankView.SetActive(true);
        bankScreen.Rebuild();
    }

    void UpdateBadge()
    {
        if (!badge) return;
        int n = AscendMail.Unread;
        badge.SetActive(n > 0);
        badgeText.text = n > 9 ? "9+" : n.ToString();
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        if (clockText) clockText.text = now.ToString("h:mm");
        if (dateText) dateText.text = now.ToString("dddd, MMMM d");
        if (statusTime) statusTime.text = now.ToString("h:mm");
    }

    // ---------- mission choice ----------

    void ChooseMission(int index)
    {
        if (Progress != Stage.ChooseMission && Progress != Stage.GoToMeeting) return;
        bool change = SelectedMissionIndex >= 0 && SelectedMissionIndex != index;
        if (SelectedMissionIndex == index) return;
        SelectedMissionIndex = index;
        SelectedMission = missions[index];
        AscendMissionGuide.Mission = (AscendSimMission)Mathf.Clamp(index, 0, 2);   // AscendCAD guide + Mission Simulator follow
        Progress = Stage.GoToMeeting;

        var m = SendMail((change ? "Mission changed: " : "Mission assigned: ") + SelectedMission.title,
            "You are leading <b>" + SelectedMission.title + "</b>!\n\n" +
            "Your briefing is in the <b>Meeting Room</b>, the door next to your desk.\n\n" +
            "<color=#9AA0A6>Changed your mind? Pick again in the first email.</color>\n\n" +
            "- " + senderName);
        AscendMail.MarkRead(m);
        if (current == sChoiceMsg) ShowMessage(sChoiceMsg);   // refresh the buttons
        ShowTip("Mission picked! Press <b>" + phoneKey + "</b> to close your phone.");
    }

    // ================= Meeting =================

    IEnumerator EnterMeeting()
    {
        transitioning = true;
        hintText.text = ""; ShowTip("");
        SetBusy(true, false);
        if (pc && pc.Seated) pc.StandUp();
        yield return Fade(0f, 1f, fadeSeconds);
        yield return TeleportPlayer(meetingRoomPoint, meetingRoomPosition, setMeetingRoomFacing ? meetingRoomFacingY : (float?)null);
        if (meetGlow) meetGlow.On = false;
        if (cast) Destroy(cast.gameObject);
        cast = MeetingCast.Spawn(this, MeetingPos, meetingRoomPoint ? meetingRoomPoint.eulerAngles.y : meetingRoomFacingY, font);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Fade(1f, 0f, fadeSeconds);
        transitioning = false;
        StartBriefing();
    }

    void StartBriefing()
    {
        Progress = Stage.Briefing;
        lines = BuildBriefingLines(SelectedMissionIndex);
        lineIndex = 0;
        speakerText.text = speakerName.ToUpperInvariant() + "   <color=#9AA0A6>//   MISSION DIRECTOR   //   " + SelectedMission.title.ToUpperInvariant() + "</color>";
        briefingRoot.SetActive(true);
        ShowLine();
    }

    void BriefingInput()
    {
        if (KeyDown(skipKey)) { EndBriefing(); return; }
        if (!KeyDown(continueKey)) return;
        if (isTyping) { StopTyping(); return; }
        lineIndex++;
        if (lineIndex >= lines.Length) EndBriefing();
        else ShowLine();
    }

    void ShowLine()
    {
        progressText.text = (lineIndex + 1).ToString("00") + " / " + lines.Length.ToString("00");
        StopTyping();
        if (cast) cast.OnLine(lineIndex);
        typing = StartCoroutine(TypeLine(lines[lineIndex]));
    }

    IEnumerator TypeLine(string line)
    {
        isTyping = true;
        if (cast) cast.Speaking(true);
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
        if (cast) cast.Speaking(false);
    }

    void StopTyping()
    {
        if (typing != null) StopCoroutine(typing);
        typing = null;
        isTyping = false;
        if (subtitleText) subtitleText.maxVisibleCharacters = 99999;
        if (cast) cast.Speaking(false);
    }

    void EndBriefing()
    {
        StopTyping();
        briefingRoot.SetActive(false);
        SetBusy(false, false);
        Progress = Stage.LeaveMeeting;
        if (cast) cast.OnLine(0);   // the director waves you off

        AscendEconomy.ReleaseFund();                         // -> ASCEND BANK notification
        summaryMsg = SendMail("Your mission notes: " + SelectedMission.title, BuildSummary(SelectedMissionIndex));
        RefreshObjective();
        ShowTip("Leave through the glowing door.\nYour mission notes are in PhMail.");
        onBriefingFinished?.Invoke();
    }

    // ================= Briefing text =================

    double Fund => AscendEconomy.FundReleased ? AscendEconomy.Balance : Math.Max(AscendEconomy.PendingFund, Data != null ? Data.governmentFundUSD : 30000000d);
    string Pct(float r) => Mathf.RoundToInt(r * 100f) + "%";

    string[] BuildBriefingLines(int i)
    {
        var l = new List<string> { "Welcome to ASCEND! I'm " + speakerName + ", the Mission Director. Please, have a seat." };
        if (SelectedMission != null && SelectedMission.briefingLines != null) l.AddRange(SelectedMission.briefingLines);
        l.Add("Now, the money. You get <b>" + AscendReport.MoneyFull(Fund) + "</b>. It goes into your ASCEND BANK app when we finish here.");
        l.Add("Every rocket part costs money. Building and testing adds about " + Pct(BuildRate) + " more.");
        l.Add(EstimateLine(i));
        l.Add("Save some money in case something goes wrong. Good results bring more money next time.");
        l.Add("Everything you need is in your email. Good luck, Mission Lead!");
        return l.ToArray();
    }

    float BuildRate => Data != null ? Data.assemblyIntegrationRate + Data.testingQARate : 0.15f;

    static string EstimateLine(int i)
    {
        switch (i)
        {
            case 0: return "A CanSat is cheap to build. You will have lots of money left over.";
            case 1: return "A small rocket for this costs about $7 to $11 million. The biggest rockets cost more than you have!";
            default: return "The cheapest Moon rocket costs about $10 to $16 million. The biggest rockets cost more than you have!";
        }
    }

    static readonly string[] CostRows =
    {
        "CanSat + small rocket: under $100,000",
        "Small rocket: about $7M - $11M\nMedium rocket: about $13M - $23M\n<color=#F28B82>Biggest rockets: too expensive!</color>",
        "Small rocket: about $10M - $16M\nMedium rocket: about $16M - $29M\n<color=#F28B82>Biggest rockets: too expensive!</color>",
    };

    string BuildSummary(int i)
    {
        string H(string s) => "<b><color=#8AB4F8>" + s + "</color></b>\n";
        var sb = new System.Text.StringBuilder();
        if (SelectedMission != null) sb.Append(SelectedMission.summaryEmail).Append("\n\n");
        sb.Append(H("YOUR MONEY"));
        sb.Append("Mission fund: <b>" + AscendReport.MoneyFull(Fund) + "</b>\n");
        sb.Append("Building + testing: +" + Pct(BuildRate) + " on parts\n");
        sb.Append("Keep for emergencies: " + AscendReport.Money(Fund * 0.15) + "\n");
        sb.Append(CostRows[Mathf.Clamp(i, 0, 2)]).Append("\n\n");
        sb.Append(H("NEXT STEPS"));
        sb.Append("1. Go to your computer\n2. Build your rocket in <b>AscendCAD</b>\n3. Plan the flight in the <b>Mission Simulator</b>\n4. Launch!\n\n");
        sb.Append("Good luck!\n" + speakerName);
        return sb.ToString();
    }

    // ================= Teleport =================

    Transform PlayerBody()
    {
        Transform p = player;
        if (p && !p.gameObject.scene.IsValid()) p = null;
        if (!p && pc) p = pc.transform;
        if (!p) { var tagged = GameObject.FindWithTag("Player"); if (tagged) p = tagged.transform; }
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
        if (!body) { Debug.LogWarning("MissionBriefing: no player found, so I can't teleport.", this); yield break; }

        Vector3 pos = point ? point.position : fallback;
        Quaternion rot = point ? Quaternion.Euler(0f, point.eulerAngles.y, 0f)
                       : facingY.HasValue ? Quaternion.Euler(0f, facingY.Value, 0f) : body.rotation;
        bool turned = point || facingY.HasValue;

        var cc = body.GetComponent<CharacterController>();
        bool ccWasOn = cc && cc.enabled;
        if (cc) cc.enabled = false;
        var rb = body.GetComponent<Rigidbody>();
        var interp = RigidbodyInterpolation.None;
        if (rb) { interp = rb.interpolation; rb.interpolation = RigidbodyInterpolation.None; }

        for (int i = 0; i < 4; i++)
        {
            body.SetPositionAndRotation(pos, rot);
            if (rb) { rb.position = pos; rb.rotation = rot; if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
            Physics.SyncTransforms();
            yield return null;
        }
        if (rb) rb.interpolation = interp;
        if (cc && ccWasOn) cc.enabled = true;
        if (turned && levelCameraOnTeleport)
        {
            Camera cam = Camera.main;
            if (cam && cam.transform != body && cam.transform.IsChildOf(body))
                cam.transform.localRotation = Quaternion.Euler(0f, cam.transform.localEulerAngles.y, cam.transform.localEulerAngles.z);
        }
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        fade.alpha = from;
        while (t < seconds) { t += Time.unscaledDeltaTime; fade.alpha = Mathf.Lerp(from, to, t / seconds); yield return null; }
        fade.alpha = to;
    }

    // ================= Interaction helpers =================

    bool LookingAt(Collider target)
    {
        if (!target) return false;
        Camera cam = Camera.main;
        if (!cam) return false;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide)) return false;
        return hit.collider == target || hit.transform.IsChildOf(target.transform);
    }

    void SetBusy(bool busy, bool showCursor)
    {
        if (busy && !isBusy) { savedLock = Cursor.lockState; savedVisible = Cursor.visible; }
        if (busy) { Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = showCursor; }
        else if (isBusy) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        isBusy = busy;
        if (pc) pc.enabled = !busy;
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

    static string KeyName(KeyCode k) => k == KeyCode.Mouse0 ? "Click" : "Press " + k;

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
        return Enum.TryParse(n, out Key key) && key != Key.None && kb[key].wasPressedThisFrame;
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

    static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

    static void ScrollTop(RectTransform content, ScrollRect scroll)
    {
        if (!content.gameObject.activeInHierarchy) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.verticalNormalizedPosition = 1f;
    }

    static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) { var c = t.GetChild(i).gameObject; c.SetActive(false); Destroy(c); }
    }

    // ================= Doors: sign + glow =================

    void BuildDoorSigns()
    {
        if (meetingRoomDoor)
            meetGlow = DoorGlow.Make(meetingRoomDoor, meetingSignPosition, meetingSignFacing, "MEETING ROOM", "BRIEFING  //  CONFERENCE", glowColor, font);
        if (meetingRoomExitDoor)
            exitGlow = DoorGlow.Make(meetingRoomExitDoor, exitSignPosition, exitSignFacing, "EXIT", "OFFICE  //  MISSION LEAD", glowColor, font);
        RefreshObjective();
    }

    // ================= PhMail views =================

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
            string from = Data != null && m.from == Data.agencyEmail ? senderName : m.from;
            t.text = (m.read ? "" : "<color=#F28B82>• </color>") + "<noparse>" + from + "</noparse>\n" +
                     "<size=105%><noparse>" + m.subject + "</noparse></size>\n" +
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

        T(readerContent, "<noparse>" + m.subject + "</noparse>", 32, Txt, FontStyles.Bold);
        string from = Data != null && m.from == Data.agencyEmail ? senderName + " <" + m.from + ">" : m.from;
        T(readerContent, "From: <noparse>" + from + "</noparse>\nTo: " + m.to + "\n" + m.time, 19, Sub);
        Le(NewImage("Line", readerContent, Panel2), -1, 2);

        var body = T(readerContent, m.body, 22, Txt);
        body.lineSpacing = 14f; body.paragraphSpacing = 6f;

        if (m == sChoiceMsg)
            for (int i = 0; i < missions.Length; i++) AddMissionButton(i);

        AscendMail.MarkRead(m);
        UpdateInboxCount();
        ScrollTop(readerContent, readerScroll);
    }

    void AddMissionButton(int index)
    {
        Mission m = missions[index];
        bool chosen = index == SelectedMissionIndex;
        bool locked = Progress != Stage.ChooseMission && Progress != Stage.GoToMeeting;

        var img = NewImage("Mission " + (index + 1), readerContent, chosen ? Chosen : Accent, 20f);
        img.raycastTarget = true;
        if (locked && !chosen) img.color = new Color(0.35f, 0.35f, 0.4f, 0.6f);
        Le(img, -1, 120);
        Vgroup(img.rectTransform, 22, 22, 16, 16, 4);
        T(img.transform, m.title + (chosen ? "   (selected)" : ""), 27, Color.white, FontStyles.Bold);
        if (!string.IsNullOrEmpty(m.subtitle)) T(img.transform, m.subtitle, 19, new Color(1f, 1f, 1f, 0.85f));

        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.interactable = !locked;
        var cb = b.colors; cb.disabledColor = Color.white; b.colors = cb;
        b.onClick.AddListener(() => ChooseMission(index));
    }

    void UpdateInboxCount()
    {
        int unread = AscendMail.Unread;
        inboxCount.text = unread > 0 ? "Inbox (" + unread + ")" : "Inbox";
        UpdateBadge();
    }

    // ================= ASCEND BANK view =================

    // ================= UI building =================

    void BuildUI()
    {
        var canvasGO = new GameObject("MissionBriefing UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        uiCanvas = canvasGO;
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)canvasGO.transform;

        // Interact hint (bottom centre)
        hintText = T(root, "", 24, Color.white, FontStyles.Bold);
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.outlineWidth = 0.2f; hintText.outlineColor = new Color32(0, 0, 0, 220);
        var hrt = hintText.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0f);
        hrt.sizeDelta = new Vector2(1400, 44); hrt.anchoredPosition = new Vector2(0, 50);

        BuildPhone(root);
        BuildBriefing(root);

        // Tip box (bottom centre, above the hint) - mission control style
        var tip = MissionUI.Box(root, "Tip", MissionUI.Line, new Color(0f, 0f, 0f, 0.93f));
        MissionUI.Place(tip, new Vector2(0f, 1f), new Vector2(28, -176), new Vector2(480, 96));
        tipRoot = tip.gameObject;
        var tl = MissionUI.Lbl(tip, "TIP", 14, font); tl.color = MissionUI.Accent;
        MissionUI.Place(tl.rectTransform, new Vector2(0f, 0.5f), new Vector2(24, 0), new Vector2(60, 40)); tl.alignment = TextAlignmentOptions.MidlineLeft;
        var div = MissionUI.Solid(tip, MissionUI.LineDim, false, "Divider").rectTransform;
        MissionUI.Place(div, new Vector2(0f, 0.5f), new Vector2(90, 0), new Vector2(1.5f, 44));
        tipText = MissionUI.Txt(tip, "", 19, MissionUI.Text, FontStyles.Normal, font);
        tipText.lineSpacing = 18f;
        tipText.alignment = TextAlignmentOptions.MidlineLeft;
        var ttr = tipText.rectTransform; ttr.anchorMin = Vector2.zero; ttr.anchorMax = Vector2.one; ttr.offsetMin = new Vector2(110, 0); ttr.offsetMax = new Vector2(-24, 0);
        tipRoot.SetActive(false);

        // While sitting: key hint, bottom left
        var sh = MissionUI.Box(root, "Seat Hint", MissionUI.Line, new Color(0f, 0f, 0f, 0.93f));
        MissionUI.Place(sh, new Vector2(0f, 0f), new Vector2(28, 28), new Vector2(300, 56));
        var sht = MissionUI.Txt(sh, "<color=#FFFFFF>SPACE</color>   STAND UP", 18, MissionUI.Label, FontStyles.Bold, font);
        sht.characterSpacing = 5f; MissionUI.Stretch(sht.rectTransform, 22, 0, 22, 0); sht.alignment = TextAlignmentOptions.MidlineLeft;
        seatHint = sh.gameObject; seatHint.SetActive(false);

        // Notifications (top left)
        notifyRoot = NewRect("Notifications", root);
        Stretch(notifyRoot);

        // Fade to black (last, so it covers everything)
        var fadeImg = NewImage("Fade", root, Color.black);
        Stretch(fadeImg.rectTransform);
        fade = fadeImg.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f; fade.blocksRaycasts = false; fade.interactable = false;
    }

    void BuildPhone(RectTransform root)
    {
        var layer = NewRect("Phone Layer", root);
        Stretch(layer);
        phoneRoot = layer.gameObject;

        var exitBox = MissionUI.Box(layer, "Exit Hint", MissionUI.Line, new Color(0f, 0f, 0f, 0.93f));
        MissionUI.Place(exitBox, new Vector2(1f, 1f), new Vector2(-30, -30), new Vector2(380, 64));
        var exit = MissionUI.Txt(exitBox, "<color=#FFFFFF>" + phoneKey + "</color>   CLOSE PHONE", 20, MissionUI.Label, FontStyles.Bold, font);
        exit.characterSpacing = 4f; MissionUI.Stretch(exit.rectTransform, 22, 0, 22, 0); exit.alignment = TextAlignmentOptions.MidlineLeft;

        var phone = NewImage("Phone", layer, Bezel, 70f);
        var prt = phone.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(600, 1010);
        prt.localScale = Vector3.one * 0.86f;

        var screen = NewImage("Screen", prt, Bg, 54f);
        Stretch(screen.rectTransform, 16, 16, 16, 16);
        screen.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var srt = screen.rectTransform;

        // Status bar
        statusTime = T(srt, "", 18, Txt, FontStyles.Bold);
        Stretch(statusTime.rectTransform, 40, 0, 40, 14); statusTime.alignment = TextAlignmentOptions.TopLeft;
        var sig = T(srt, "5G   100%", 16, Txt, FontStyles.Bold);
        Stretch(sig.rectTransform, 40, 0, 40, 15); sig.alignment = TextAlignmentOptions.TopRight;

        var content = NewRect("Content", srt);
        Stretch(content, 0, 46, 0, 48);

        BuildHome(content);
        BuildMail(content);
        BuildBank(content);

        // Home bar (click to go home)
        var homeHit = NewImage("Home Bar", srt, new Color(0, 0, 0, 0));
        homeHit.raycastTarget = true;
        var hb = homeHit.rectTransform; hb.anchorMin = hb.anchorMax = hb.pivot = new Vector2(0.5f, 0f); hb.sizeDelta = new Vector2(320, 46); hb.anchoredPosition = Vector2.zero;
        var pill = NewImage("Pill", hb, new Color(1f, 1f, 1f, 0.85f), 4f);
        var pr = pill.rectTransform; pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f); pr.sizeDelta = new Vector2(170, 8);
        var hbb = homeHit.gameObject.AddComponent<Button>(); hbb.targetGraphic = pill; hbb.navigation = new Navigation { mode = Navigation.Mode.None };
        hbb.onClick.AddListener(ShowHome);
    }

    void BuildHome(RectTransform parent)
    {
        var hv = NewRect("Home", parent); Stretch(hv);
        homeView = hv.gameObject;
        var wall = NewImage("Wallpaper", hv, Color.white); wall.sprite = PhoneArt.Wallpaper; Stretch(wall.rectTransform, 0, -46, 0, -48);
        wall.transform.SetAsFirstSibling();

        clockText = T(hv, "9:41", 96, Color.white, FontStyles.Bold);
        Stretch(clockText.rectTransform, 0, 0, 0, 40); clockText.alignment = TextAlignmentOptions.Top;
        dateText = T(hv, "", 22, new Color(1f, 1f, 1f, 0.8f));
        Stretch(dateText.rectTransform, 0, 0, 0, 160); dateText.alignment = TextAlignmentOptions.Top;

        AppIcon(hv, PhoneArt.MailIcon, "PhMail", new Vector2(-95, -300), OpenMail, true);
        AppIcon(hv, PhoneArt.BankIcon, "ASCEND BANK", new Vector2(95, -300), OpenBank, false);
    }

    void AppIcon(RectTransform parent, Sprite icon, string label, Vector2 pos, Action onClick, bool withBadge)
    {
        var cell = NewImage(label, parent, new Color(0, 0, 0, 0));
        cell.raycastTarget = true;
        var rt = cell.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(170, 190); rt.anchoredPosition = pos;
        var img = NewImage("Icon", rt, Color.white); img.sprite = icon; img.preserveAspect = true;
        var irt = img.rectTransform; irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f); irt.sizeDelta = new Vector2(124, 124); irt.anchoredPosition = new Vector2(0, -6);
        var t = T(rt, label, 19, Color.white, FontStyles.Bold);
        Stretch(t.rectTransform, 0, 0, 0, 140); t.alignment = TextAlignmentOptions.Top; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.outlineWidth = 0.15f; t.outlineColor = new Color32(0, 0, 0, 160);
        var b = cell.gameObject.AddComponent<Button>();
        b.targetGraphic = img; b.navigation = new Navigation { mode = Navigation.Mode.None };
        var cb = b.colors; cb.highlightedColor = new Color(0.85f, 0.85f, 0.85f); cb.pressedColor = new Color(0.6f, 0.6f, 0.6f); b.colors = cb;
        b.onClick.AddListener(() => onClick());
        if (withBadge)
        {
            var bd = NewImage("Badge", irt, Hex("#E53935"), 22f);
            var brt = bd.rectTransform; brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f); brt.sizeDelta = new Vector2(44, 44); brt.anchoredPosition = new Vector2(12, 12);
            badgeText = T(brt, "1", 22, Color.white, FontStyles.Bold);
            Stretch(badgeText.rectTransform); badgeText.alignment = TextAlignmentOptions.Center;
            badge = bd.gameObject;
        }
    }

    // App header: back to home + title
    RectTransform AppHeader(RectTransform parent, string title, Sprite icon)
    {
        var header = NewImage("Header", parent, Panel);
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = Vector2.one; hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 92); hrt.anchoredPosition = Vector2.zero;
        var back = NewImage("Home", hrt, new Color(1f, 1f, 1f, 0f), 18f); back.raycastTarget = true;
        var brt = back.rectTransform; brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 0.5f); brt.sizeDelta = new Vector2(64, 64); brt.anchoredPosition = new Vector2(8, 0);
        var bb = back.gameObject.AddComponent<Button>(); bb.targetGraphic = back; bb.navigation = new Navigation { mode = Navigation.Mode.None };
        var bcb = bb.colors; bcb.normalColor = new Color(1f, 1f, 1f, 0f); bcb.highlightedColor = new Color(1f, 1f, 1f, 0.1f); bcb.pressedColor = new Color(1f, 1f, 1f, 0.2f); bcb.selectedColor = bcb.normalColor; bb.colors = bcb;
        bb.onClick.AddListener(ShowHome);
        Chevron(brt, new Vector2(24, 0));
        var ic = NewImage("Logo", hrt, Color.white); ic.sprite = icon; ic.preserveAspect = true;
        var icr = ic.rectTransform; icr.anchorMin = icr.anchorMax = icr.pivot = new Vector2(0, 0.5f); icr.sizeDelta = new Vector2(52, 52); icr.anchoredPosition = new Vector2(76, 0);
        var t = T(hrt, title, 34, Txt, FontStyles.Bold);
        Stretch(t.rectTransform, 142, 0, 20, 0); t.alignment = TextAlignmentOptions.MidlineLeft;
        return hrt;
    }

    void Chevron(RectTransform parent, Vector2 pos)
    {
        Color link = Hex("#8AB4F8");
        for (int i = 0; i < 2; i++)
        {
            var stroke = NewImage("Chevron", parent, link, 2f);
            var crt = stroke.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0, 0.5f); crt.pivot = new Vector2(0f, 0.5f);
            crt.sizeDelta = new Vector2(20, 4); crt.anchoredPosition = pos;
            crt.localRotation = Quaternion.Euler(0, 0, i == 0 ? 45f : -45f);
        }
    }

    void BuildMail(RectTransform parent)
    {
        var mv = NewImage("PhMail", parent, Bg); Stretch(mv.rectTransform);
        mailView = mv.gameObject;
        var hrt = AppHeader(mv.rectTransform, "<color=#8AB4F8>Ph</color>Mail", PhoneArt.MailIcon);
        inboxCount = T(hrt, "Inbox", 20, Sub, FontStyles.Bold);
        Stretch(inboxCount.rectTransform, 26, 0, 26, 0);
        inboxCount.alignment = TextAlignmentOptions.MidlineRight;

        var body = NewRect("Body", mv.rectTransform);
        Stretch(body, 0, 0, 0, 92);

        var lv = NewRect("Inbox View", body); Stretch(lv);
        listView = lv.gameObject;
        listContent = MakeScroll(lv, 0, out listScroll, 14, 14, 14, 14, 10);

        var rv = NewRect("Reader View", body); Stretch(rv);
        readerView = rv.gameObject;
        var bar = NewImage("Back Bar", rv, Bg);
        var barRt = bar.rectTransform;
        barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = Vector2.one; barRt.pivot = new Vector2(0.5f, 1);
        barRt.sizeDelta = new Vector2(0, 70); barRt.anchoredPosition = Vector2.zero;
        var divider = NewImage("Divider", barRt, Panel2);
        var drt = divider.rectTransform; drt.anchorMin = Vector2.zero; drt.anchorMax = new Vector2(1, 0); drt.pivot = new Vector2(0.5f, 0); drt.sizeDelta = new Vector2(0, 2);
        var backImg = NewImage("Back", barRt, new Color(1f, 1f, 1f, 0f), 18f);
        backImg.raycastTarget = true;
        var brt = backImg.rectTransform; brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 0.5f); brt.sizeDelta = new Vector2(170, 54); brt.anchoredPosition = new Vector2(10, 0);
        var back = backImg.gameObject.AddComponent<Button>();
        back.targetGraphic = backImg; back.navigation = new Navigation { mode = Navigation.Mode.None };
        var bcb = back.colors; bcb.normalColor = new Color(1f, 1f, 1f, 0f); bcb.highlightedColor = new Color(1f, 1f, 1f, 0.08f); bcb.pressedColor = new Color(1f, 1f, 1f, 0.16f); bcb.selectedColor = bcb.normalColor; back.colors = bcb;
        back.onClick.AddListener(ShowList);
        Chevron(brt, new Vector2(22, 0));
        var backText = T(brt, "Inbox", 26, Hex("#8AB4F8"), FontStyles.Bold);
        Stretch(backText.rectTransform, 52, 0, 0, 0); backText.alignment = TextAlignmentOptions.MidlineLeft;
        readerContent = MakeScroll(rv, 70, out readerScroll, 26, 26, 10, 26, 16);
    }

    void BuildBank(RectTransform parent)
    {
        var bv = NewImage("ASCEND BANK", parent, Hex("#0A1022")); Stretch(bv.rectTransform);
        bankView = bv.gameObject;
        AppHeader(bv.rectTransform, "ASCEND <color=#FFC850>BANK</color>", PhoneArt.BankIcon);
        var body = NewRect("Body", bv.rectTransform);
        Stretch(body, 0, 0, 0, 92);
        bankScreen = new AscendBankScreen(body, font, textScale, 0f, false);
    }

    RectTransform MakeScroll(RectTransform parent, float topInset, out ScrollRect scroll, int padL, int padR, int padT, int padB, float spacing)
    {
        var sroot = NewImage("Scroll", parent, new Color(0, 0, 0, 0));
        sroot.raycastTarget = true;
        var rt = sroot.rectTransform;
        Stretch(rt, 0, 0, 0, topInset);
        scroll = rt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40f;
        var viewport = NewRect("Viewport", rt);
        Stretch(viewport, 0, 0, 16, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        Vgroup(content, padL, padR, padT, padB, spacing);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;

        var bar = NewImage("Scrollbar", rt, Hex("#141518"), 6f);
        bar.raycastTarget = true;
        var brt = bar.rectTransform; brt.anchorMin = new Vector2(1, 0); brt.anchorMax = Vector2.one; brt.pivot = new Vector2(1, 0.5f);
        brt.sizeDelta = new Vector2(10, -8); brt.anchoredPosition = new Vector2(-2, 0);
        var sliding = NewRect("Sliding Area", brt); Stretch(sliding);
        var handle = NewImage("Handle", sliding, Hex("#6E7180"), 5f); handle.raycastTarget = true; Stretch(handle.rectTransform);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        return content;
    }

    void BuildBriefing(RectTransform root)
    {
        var briefing = NewRect("Briefing", root); Stretch(briefing);
        briefingRoot = briefing.gameObject;

        // Subtitle panel - mission control style
        var box = MissionUI.Box(briefing, "Subtitle Box", MissionUI.Line, new Color(0f, 0f, 0f, 0.94f), true);
        box.anchorMin = new Vector2(0.1f, 0); box.anchorMax = new Vector2(0.9f, 0); box.pivot = new Vector2(0.5f, 0);
        box.sizeDelta = new Vector2(0, 270); box.anchoredPosition = new Vector2(0, 40);
        speakerText = MissionUI.Txt(box, "", 18, MissionUI.Text, FontStyles.Bold, font);
        speakerText.characterSpacing = 4f;
        Stretch(speakerText.rectTransform, 32, 210, 32, 20);
        MissionUI.HLine(box, 60f, MissionUI.LineDim, 20f);
        subtitleText = MissionUI.Txt(box, "", 28, Color.white, FontStyles.Normal, font);
        subtitleText.lineSpacing = 22f;
        Stretch(subtitleText.rectTransform, 32, 52, 32, 78);
        progressText = MissionUI.Lbl(box, "", 14, font);
        Stretch(progressText.rectTransform, 32, 16, 32, 16);
        progressText.alignment = TextAlignmentOptions.BottomRight;

        var keys = MissionUI.Txt(box, "<color=#FFFFFF>" + continueKey.ToString().ToUpperInvariant() + "</color>   CONTINUE        <color=#FFFFFF>" + skipKey + "</color>   SKIP BRIEFING", 15, MissionUI.Label, FontStyles.Bold, font);
        keys.characterSpacing = 5f;
        Stretch(keys.rectTransform, 32, 16, 32, 16); keys.alignment = TextAlignmentOptions.BottomLeft;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image NewImage(string name, Transform parent, Color color, float radius = 0f)
    {
        var img = NewRect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        if (radius > 0f) { img.sprite = PhoneArt.Round; img.type = Image.Type.Sliced; img.fillCenter = true; img.pixelsPerUnitMultiplier = PhoneArt.RoundRadius / radius; }
        return img;
    }

    TextMeshProUGUI T(Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var t = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = text; t.fontSize = size * textScale; t.color = color; t.fontStyle = style;
        t.richText = true; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal; t.alignment = TextAlignmentOptions.TopLeft;
        return t;
    }

    static VerticalLayoutGroup Vgroup(RectTransform rt, int l, int r, int t, int b, float spacing)
    {
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(l, r, t, b); v.spacing = spacing; v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
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
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
    }

    // ================= Default text =================

    [ContextMenu("Reset missions and welcome email to defaults")]
    void ResetTexts()
    {
        missions = DefaultMissions();
        welcomeEmail = DefaultWelcome;
        speakerName = "Director Isabel Reyes";
        senderName = "ASCEND Mission Control";
    }

    const string DefaultWelcome =
        "Welcome to <b>ASCEND</b>, Mission Lead!\n\n" +
        "We have three missions ready. Pick the one you want to lead.\n\n" +
        "You can change your pick until you walk into the Meeting Room.\n\n" +
        "- ASCEND Mission Control";

    static Mission[] DefaultMissions()
    {
        return new[]
        {
            new Mission
            {
                title = "Sky Sensor",
                subtitle = "EASY  |  A tiny probe that measures the sky",
                briefingLines = new[]
                {
                    "Your mission: <b>Sky Sensor</b>.",
                    "You will build a CanSat: a tiny satellite the size of a soda can.",
                    "A small rocket carries it about 1 kilometre up. Then it pops out and floats down on a parachute.",
                    "On the way down, it measures the air: how warm it is, and how hard it presses.",
                    "Fun fact: the air gets about 6.5 degrees colder for every kilometre you go up!",
                    "Choose the parachute carefully. Too small and it crashes. Too big and it drifts away.",
                },
                summaryEmail =
                    "<b><color=#8AB4F8>YOUR MISSION</color></b>\nSky Sensor\nA tiny probe that measures the sky\n\n" +
                    "<b><color=#8AB4F8>THE PLAN</color></b>\n- Fly about 1 km up\n- Pop out and open the parachute\n- Measure the air on the way down\n- Land in one piece\n\n" +
                    "<b><color=#8AB4F8>WHAT TO BUILD</color></b>\nRocket: a small one\nCanSat: sensors, battery, radio, parachute",
            },
            new Mission
            {
                title = "Earth Watcher",
                subtitle = "MEDIUM  |  A satellite that takes photos of Earth",
                briefingLines = new[]
                {
                    "Your mission: <b>Earth Watcher</b>.",
                    "You will put a satellite into orbit, about 700 kilometres above the Earth.",
                    "To stay up there it must fly about 27,000 km per hour. That is one lap around Earth every 100 minutes!",
                    "Its camera will watch our farms, forests and coasts, and help us after typhoons.",
                    "It needs solar panels for power, a radio to send photos home, and a way to point its camera.",
                    "Remember: every extra kilogram needs a bigger rocket. Keep it light!",
                },
                summaryEmail =
                    "<b><color=#8AB4F8>YOUR MISSION</color></b>\nEarth Watcher\nA satellite that takes photos of Earth\n\n" +
                    "<b><color=#8AB4F8>THE PLAN</color></b>\n- Launch into orbit, 700 km up\n- Open the solar panels\n- Take photos of Earth\n- Send them home\n\n" +
                    "<b><color=#8AB4F8>WHAT TO BUILD</color></b>\nRocket: small or medium\nSatellite: camera, solar panels, battery, radio",
            },
            new Mission
            {
                title = "Moon Mapper",
                subtitle = "HARD  |  A spacecraft that maps the Moon",
                briefingLines = new[]
                {
                    "Your mission: <b>Moon Mapper</b>. Our hardest one yet!",
                    "Your spacecraft will travel 384,000 kilometres to the Moon. The trip takes about 3 to 5 days.",
                    "When it arrives, it must fire its engine to slow down. Too fast, and it flies right past the Moon!",
                    "Then it circles just 50 kilometres above the surface and maps every crater and mountain.",
                    "Fuel is everything here. You need a big rocket, but you have a small budget.",
                },
                summaryEmail =
                    "<b><color=#8AB4F8>YOUR MISSION</color></b>\nMoon Mapper\nA spacecraft that maps the Moon\n\n" +
                    "<b><color=#8AB4F8>THE PLAN</color></b>\n- Launch into orbit around Earth\n- Fly to the Moon (3-5 days)\n- Slow down and circle the Moon\n- Map the surface and send the maps home\n\n" +
                    "<b><color=#8AB4F8>WHAT TO BUILD</color></b>\nRocket: a powerful one\nSpacecraft: camera, laser, engine, fuel, solar panels, radio",
            },
        };
    }
}


// ═════════════════════════════════════════════════════════════════════════════
//  Glowing door + sign (cheap: one unlit quad, one small point light, one text sign)
// ═════════════════════════════════════════════════════════════════════════════
public class DoorGlow : MonoBehaviour
{
    public bool On { get => on; set { on = value; if (glow) glow.SetActive(value); if (glowLight) glowLight.enabled = value; } }
    bool on;
    GameObject glow;
    Light glowLight;
    Material mat;
    TextMeshPro title;
    Color color;

    public static DoorGlow Make(Collider door, Vector3 signPos, Vector3 signFacing, string signTitle, string signSub, Color c, TMP_FontAsset font)
    {
        var root = new GameObject("Door Glow - " + signTitle);
        var g = root.AddComponent<DoorGlow>();
        g.color = c;
        Vector3 n = signFacing.sqrMagnitude > 0.001f ? signFacing.normalized : Vector3.back;

        // Glow quad on the door face
        var b = door.bounds;
        bool thinX = Mathf.Abs(n.x) > Mathf.Abs(n.z);
        float halfDepth = thinX ? b.extents.x : b.extents.z;
        float w = thinX ? b.size.z : b.size.x;
        float floorY = b.min.y, topY = b.max.y - 0.1f;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Glow";
        quad.transform.SetParent(root.transform, false);
        Vector3 faceCenter = new Vector3(b.center.x, (floorY + topY) * 0.5f, b.center.z) + n * (halfDepth + 0.02f);
        quad.transform.position = faceCenter;
        quad.transform.rotation = Quaternion.LookRotation(-n);
        quad.transform.localScale = new Vector3(w * 0.96f, topY - floorY, 1f);
        g.mat = Transparent(c);
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = g.mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        g.glow = quad;

        // Small light so the floor and walls around the door glow too
        var lgo = new GameObject("Glow Light");
        lgo.transform.SetParent(root.transform, false);
        lgo.transform.position = faceCenter + n * 0.8f;
        g.glowLight = lgo.AddComponent<Light>();
        g.glowLight.type = LightType.Point; g.glowLight.color = c; g.glowLight.range = 4f; g.glowLight.intensity = 1.5f; g.glowLight.shadows = LightShadows.None;

        // Sign (always visible)
        var sign = new GameObject("Sign");
        sign.transform.SetParent(root.transform, false);
        sign.transform.position = signPos + n * 0.01f;
        sign.transform.rotation = Quaternion.LookRotation(-n);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(panel.GetComponent<Collider>());
        panel.name = "Panel";
        panel.transform.SetParent(sign.transform, false);
        panel.transform.localPosition = new Vector3(0f, 0f, 0.005f);
        panel.transform.localScale = new Vector3(1.7f, 0.42f, 1f);
        panel.GetComponent<MeshRenderer>().sharedMaterial = Opaque(new Color(0.02f, 0.03f, 0.05f));
        var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(frame.GetComponent<Collider>());
        frame.name = "Frame";
        frame.transform.SetParent(sign.transform, false);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.008f);
        frame.transform.localScale = new Vector3(1.74f, 0.46f, 1f);
        frame.GetComponent<MeshRenderer>().sharedMaterial = Opaque(c);

        g.title = Text(sign.transform, signTitle, 1.6f, Color.white, new Vector3(0f, 0.045f, 0f), font);
        g.title.characterSpacing = 12f;
        var sub = Text(sign.transform, signSub, 0.55f, new Color(c.r, c.g, c.b, 1f), new Vector3(0f, -0.12f, 0f), font);
        sub.characterSpacing = 8f;
        g.On = false;
        return g;
    }

    static TextMeshPro Text(Transform parent, string s, float size, Color col, Vector3 pos, TMP_FontAsset font)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var t = go.AddComponent<TextMeshPro>();
        if (font) t.font = font;
        t.text = s; t.fontSize = size; t.color = col; t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(1.6f, 0.3f);
        return t;
    }

    static Material Opaque(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { name = "Door Sign" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }

    static Material Transparent(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh) { name = "Door Glow" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        return m;
    }

    void Update()
    {
        float p = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
        if (on)
        {
            if (mat) { var c = color; c.a = Mathf.Lerp(0.05f, 0.2f, p); if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); if (mat.HasProperty("_Color")) mat.SetColor("_Color", c); }
            if (glowLight) glowLight.intensity = Mathf.Lerp(0.4f, 1.4f, p);
            if (title) title.color = Color.Lerp(Color.white, color, p * 0.7f);
        }
        else if (title) title.color = Color.white;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  Phone art drawn in code: app logos, wallpaper, rounded corners
// ═════════════════════════════════════════════════════════════════════════════
public static class PhoneArt
{
    public const float RoundRadius = 64f;
    static Sprite round, mail, bank, wall;

    static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

    public static Sprite Round
    {
        get
        {
            if (round) return round;
            const int size = 160; float r = RoundRadius;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dx = Mathf.Max(r - fx, 0f, fx - (size - r)), dy = Mathf.Max(r - fy, 0f, fy - (size - r));
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            tex.SetPixels32(px); tex.Apply();
            return round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }
    }

    // signed distance helpers (pixels in 0..1 space)
    static float Box(Vector2 p, Vector2 c, Vector2 half) { Vector2 d = new Vector2(Mathf.Abs(p.x - c.x) - half.x, Mathf.Abs(p.y - c.y) - half.y); return new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f); }
    static float RoundBox(Vector2 p, Vector2 c, Vector2 half, float r) => Box(p, c, half - Vector2.one * r) - r;
    static float Segment(Vector2 p, Vector2 a, Vector2 b) { Vector2 pa = p - a, ba = b - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude); return (pa - ba * h).magnitude; }
    static float Cover(float d, float px) => Mathf.Clamp01(0.5f - d / px);

    static Sprite Draw(int size, Func<Vector2, float, Color> shade)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var px = new Color[size * size];
        float pix = 1f / size;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = shade(new Vector2((x + 0.5f) / size, (y + 0.5f) / size), pix);
        tex.SetPixels(px); tex.Apply(true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static Color Over(Color under, Color over, float a) { a *= over.a; return new Color(Mathf.Lerp(under.r, over.r, a), Mathf.Lerp(under.g, over.g, a), Mathf.Lerp(under.b, over.b, a), Mathf.Max(under.a, a)); }

    // PhMail: blue rounded square with a white envelope
    public static Sprite MailIcon => mail ? mail : mail = Draw(256, (p, px) =>
    {
        Vector2 c = new Vector2(0.5f, 0.5f);
        float bg = Cover(RoundBox(p, c, new Vector2(0.48f, 0.48f), 0.13f), px);
        Color top = Hex("#4C8DF6"), bot = Hex("#0B57D0");
        Color col = Color.Lerp(bot, top, p.y); col.a = bg;
        float env = Cover(RoundBox(p, new Vector2(0.5f, 0.47f), new Vector2(0.29f, 0.2f), 0.035f), px);
        col = Over(col, Color.white, env * bg);
        float flapD = Mathf.Min(Segment(p, new Vector2(0.23f, 0.65f), new Vector2(0.5f, 0.45f)), Segment(p, new Vector2(0.77f, 0.65f), new Vector2(0.5f, 0.45f))) - 0.022f;
        col = Over(col, Hex("#0B57D0"), Cover(flapD, px) * env);
        return col;
    });

    // ASCEND BANK: navy rounded square, gold rocket-chevron over three bank columns
    public static Sprite BankIcon => bank ? bank : bank = Draw(256, (p, px) =>
    {
        Vector2 c = new Vector2(0.5f, 0.5f);
        float bg = Cover(RoundBox(p, c, new Vector2(0.48f, 0.48f), 0.13f), px);
        Color col = Color.Lerp(Hex("#061230"), Hex("#123C7A"), p.y); col.a = bg;
        float ring = Mathf.Abs(RoundBox(p, c, new Vector2(0.43f, 0.43f), 0.1f)) - 0.008f;
        Color gold = Hex("#FFC850");
        col = Over(col, new Color(gold.r, gold.g, gold.b, 0.55f), Cover(ring, px) * bg);
        // chevron (an upward "A" like a rocket)
        float chev = Mathf.Min(Segment(p, new Vector2(0.3f, 0.5f), new Vector2(0.5f, 0.78f)), Segment(p, new Vector2(0.7f, 0.5f), new Vector2(0.5f, 0.78f))) - 0.045f;
        col = Over(col, gold, Cover(chev, px) * bg);
        // three columns + base
        float cols = Mathf.Min(Box(p, new Vector2(0.37f, 0.36f), new Vector2(0.035f, 0.09f)),
                     Mathf.Min(Box(p, new Vector2(0.5f, 0.36f), new Vector2(0.035f, 0.09f)), Box(p, new Vector2(0.63f, 0.36f), new Vector2(0.035f, 0.09f))));
        cols = Mathf.Min(cols, Box(p, new Vector2(0.5f, 0.24f), new Vector2(0.2f, 0.022f)));
        col = Over(col, Color.white, Cover(cols, px) * bg);
        return col;
    });

    public static Sprite Wallpaper => wall ? wall : wall = Draw(128, (p, px) =>
    {
        Color col = Color.Lerp(Hex("#02040A"), Hex("#0D2A57"), Mathf.Pow(p.y, 1.6f));
        float glow = Mathf.Exp(-((p - new Vector2(0.75f, 0.85f)).sqrMagnitude) * 9f) * 0.35f;
        col += new Color(0.25f, 0.45f, 0.8f) * glow;
        col.a = 1f;
        return col;
    });
}
