using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class RocketPartsDropdown : MonoBehaviour
{
    [Header("Parent Dropdown Reference")]
    [SerializeField] private TMP_Dropdown parentDropdown;

    [Header("Child Sub-Dropdown Objects (Matches Parent Options Order)")]
    [SerializeField] private List<GameObject> childDropdownObjects;

    [Header("Animation Settings")]
    [SerializeField] private float transitionDuration = 0.2f;

    private GameObject currentActiveChild;
    private Coroutine currentAnimation;

    private void Start()
    {
        if (parentDropdown == null) return;

        // Hide all sub-dropdowns initially
        foreach (GameObject child in childDropdownObjects)
        {
            if (child != null)
            {
                GetOrAddCanvasGroup(child).alpha = 0f;
                child.transform.localScale = Vector3.zero;
                child.SetActive(false);
            }
        }

        // Add event listener
        parentDropdown.onValueChanged.AddListener(OnParentOptionChanged);

        // Display current active child on start
        OnParentOptionChanged(parentDropdown.value);
    }

    private void OnParentOptionChanged(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= childDropdownObjects.Count) return;

        GameObject targetChild = childDropdownObjects[selectedIndex];

        if (targetChild == currentActiveChild) return;

        if (currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
        }

        currentAnimation = StartCoroutine(AnimateChildTransition(currentActiveChild, targetChild));
        currentActiveChild = targetChild;
    }

    private IEnumerator AnimateChildTransition(GameObject closingChild, GameObject openingChild)
    {
        // Close previous sub-dropdown
        if (closingChild != null && closingChild.activeSelf)
        {
            yield return AnimateScaleAndFade(closingChild, Vector3.one, Vector3.zero, 1f, 0f);
            closingChild.SetActive(false);
        }

        // Open newly selected sub-dropdown
        if (openingChild != null)
        {
            openingChild.SetActive(true);
            yield return AnimateScaleAndFade(openingChild, Vector3.zero, Vector3.one, 0f, 1f);
        }
    }

    private IEnumerator AnimateScaleAndFade(GameObject target, Vector3 startScale, Vector3 endScale, float startAlpha, float endAlpha)
    {
        CanvasGroup group = GetOrAddCanvasGroup(target);
        float timer = 0f;

        while (timer < transitionDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / transitionDuration;

            target.transform.localScale = Vector3.Lerp(startScale, endScale, progress);
            group.alpha = Mathf.Lerp(startAlpha, endAlpha, progress);

            yield return null;
        }

        target.transform.localScale = endScale;
        group.alpha = endAlpha;
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject obj)
    {
        CanvasGroup group = obj.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = obj.AddComponent<CanvasGroup>();
        }
        return group;
    }

    private void OnDestroy()
    {
        if (parentDropdown != null)
        {
            parentDropdown.onValueChanged.RemoveListener(OnParentOptionChanged);
        }
    }
}
