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

    [Header("Text Settings")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private int fontSize = 32;
    [SerializeField] private Color textColor = Color.white;

    private Transform cameraTransform;

    // Shared between ALL doors
    private static TextMeshProUGUI interactionText;
    private static Canvas promptCanvas;
    private static DoorInteraction selectedDoor;
    private static int lastTeleportFrame = -1;
    private static float lastTeleportTime = -999f;

    // Resets static values when entering Play Mode
    // (needed if "Enter Play Mode Options" / no domain reload is on)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        interactionText = null;
        promptCanvas = null;
        selectedDoor = null;
        lastTeleportFrame = -1;
        lastTeleportTime = -999f;
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

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject textObject = new GameObject("InteractionPromptText");
        textObject.transform.SetParent(canvasObject.transform, false);

        interactionText = textObject.AddComponent<TextMeshProUGUI>();
        interactionText.text = "";
        interactionText.fontSize = fontSize;
        interactionText.color = textColor;
        interactionText.alignment = TextAlignmentOptions.Center;

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
    }

    void Update()
    {
        if (cameraTransform == null || interactionText == null)
            return;

        // If ANY door already teleported the player this frame,
        // do nothing. This is the main fix.
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
                ExecuteTeleport();
            }
        }
        else if (selectedDoor == this)
        {
            // Hide prompt when looking away from this door
            interactionText.gameObject.SetActive(false);
            selectedDoor = null;
        }
    }

    private void ExecuteTeleport()
    {
        CharacterController player =
            FindAnyObjectByType<CharacterController>();

        if (player == null)
        {
            Debug.LogError("No CharacterController found!");
            return;
        }

        // Mark the teleport FIRST so no other door can react this frame
        lastTeleportFrame = Time.frameCount;
        lastTeleportTime = Time.time;

        player.enabled = false;
        player.transform.position = teleportTarget;
        player.enabled = true;

        interactionText.gameObject.SetActive(false);
        selectedDoor = null;

        Debug.Log("Teleported through " + gameObject.name + " to " + teleportTarget);
    }
}
