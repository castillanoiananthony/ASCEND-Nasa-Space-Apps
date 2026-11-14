using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DoorInteraction : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private string promptMessage = "Press E to Interact";
    [SerializeField] private float interactionDistance = 4f;

    [Header("Teleport Settings")]
    [SerializeField] private Vector3 teleportTarget = Vector3.zero;
    [SerializeField] private float teleportCooldown = 0.5f;

    [Header("Fade Settings")]
    [Tooltip("Seconds to fade to black (and the same to fade back in).")]
    [SerializeField] private float fadeDuration = 0.25f;
    [Tooltip("Seconds to stay black after the teleport, before fading back in.")]
    [SerializeField] private float blackHold = 0.1f;

    [Header("Text Settings")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private int fontSize = 32;
    [SerializeField] private Color textColor = Color.white;

    private Transform cameraTransform;

    // Shared between ALL doors
    private static TextMeshProUGUI interactionText;
    private static Canvas promptCanvas;
    private static CanvasGroup fadeGroup;
    private static DoorInteraction selectedDoor;
    private static int lastTeleportFrame = -1;
    private static float lastTeleportTime = -999f;
    private static bool isTeleporting;

    // Resets static values when entering Play Mode
    // (needed if "Enter Play Mode Options" / no domain reload is on)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        interactionText = null;
        promptCanvas = null;
        fadeGroup = null;
        selectedDoor = null;
        lastTeleportFrame = -1;
        lastTeleportTime = -999f;
        isTeleporting = false;
    }

    void Start()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        else
        {
            Camera cam = FindAnyObjectByType<Camera>();
            if (cam != null)
                cameraTransform = cam.transform;
        }

        if (cameraTransform == null)
            Debug.LogError("No player camera found!");

        CreateInteractionUI();
    }

    private void CreateInteractionUI()
    {
        if (interactionText != null)
            return;

        GameObject canvasObject = new GameObject("InteractionCanvas");

        promptCanvas = canvasObject.AddComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        promptCanvas.sortingOrder = 1000; // fade covers everything

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasObject.AddComponent<GraphicRaycaster>();

        // Prompt text
        GameObject textObject = new GameObject("InteractionPromptText");
        textObject.transform.SetParent(canvasObject.transform, false);

        interactionText = textObject.AddComponent<TextMeshProUGUI>();
        interactionText.text = "";
        interactionText.fontSize = fontSize;
        interactionText.color = textColor;
        interactionText.alignment = TextAlignmentOptions.Center;
        interactionText.raycastTarget = false;

        if (fontAsset != null)
            interactionText.font = fontAsset;
        else
            Debug.LogWarning("No TMP Font Asset assigned! Using TMP default font.");

        RectTransform rect = interactionText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.15f);
        rect.anchorMax = new Vector2(0.5f, 0.15f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(600f, 80f);

        textObject.SetActive(false);

        // Black fade screen (on top of the prompt)
        GameObject fadeObject = new GameObject("DoorFade");
        fadeObject.transform.SetParent(canvasObject.transform, false);

        Image fadeImage = fadeObject.AddComponent<Image>();
        fadeImage.color = Color.black;
        fadeImage.raycastTarget = false;

        RectTransform fadeRect = fadeImage.rectTransform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = Vector2.zero;
        fadeRect.offsetMax = Vector2.zero;

        fadeGroup = fadeObject.AddComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;
    }

    void Update()
    {
        if (cameraTransform == null || interactionText == null)
            return;

        // While a fade/teleport is running, no door reacts
        if (isTeleporting)
            return;

        // If ANY door already teleported the player this frame, do nothing
        if (Time.frameCount == lastTeleportFrame)
            return;

        bool lookingAtThisDoor = false;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance))
        {
            DoorInteraction hitDoor =
                hit.collider.GetComponentInParent<DoorInteraction>();

            lookingAtThisDoor = (hitDoor == this);
        }

        if (lookingAtThisDoor)
        {
            selectedDoor = this;
            interactionText.text = promptMessage;
            interactionText.gameObject.SetActive(true);

            bool cooldownOver =
                Time.time - lastTeleportTime >= teleportCooldown;

            if (Input.GetKeyDown(KeyCode.E) && cooldownOver)
            {
                StartCoroutine(FadeTeleport());
            }
        }
        else if (selectedDoor == this)
        {
            // Hide prompt when looking away from this door
            interactionText.gameObject.SetActive(false);
            selectedDoor = null;
        }
    }

    private IEnumerator FadeTeleport()
    {
        CharacterController player =
            FindAnyObjectByType<CharacterController>();

        if (player == null)
        {
            Debug.LogError("No CharacterController found!");
            yield break;
        }

        // Mark the teleport FIRST so no other door can react
        isTeleporting = true;
        lastTeleportFrame = Time.frameCount;

        interactionText.gameObject.SetActive(false);
        selectedDoor = null;

        // Fade out
        yield return Fade(0f, 1f);

        // Teleport while the screen is black
        player.enabled = false;
        player.transform.position = teleportTarget;
        Physics.SyncTransforms();
        player.enabled = true;
        Debug.Log("Teleported through " + gameObject.name + " to " + teleportTarget);

        yield return new WaitForSecondsRealtime(blackHold);

        // Fade in
        yield return Fade(1f, 0f);

        lastTeleportTime = Time.time;
        isTeleporting = false;
    }

    private IEnumerator Fade(float from, float to)
    {
        if (fadeGroup == null)
            yield break;

        float t = 0f;
        fadeGroup.alpha = from;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }

        fadeGroup.alpha = to;
    }

    // If this door gets turned off mid-fade, don't leave the screen black
    void OnDisable()
    {
        if (isTeleporting)
        {
            isTeleporting = false;
            if (fadeGroup != null)
                fadeGroup.alpha = 0f;
        }
    }
}
