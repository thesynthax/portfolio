using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class MainChoiceUI : MonoBehaviour
{
    [Header("UI")]
    public RectTransform leftArea;   // full-left parent (RectTransform anchored to left half)
    public RectTransform rightArea;  // full-right parent (RectTransform anchored to right half)
    public Button leftButton;
    public Button rightButton;
    public CanvasGroup canvasGroup; // optional quick fade

    [Header("Preview controllers")]
    public PreviewBlur leftPreview;
    public PreviewBlur rightPreview;

    [Header("References")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover; // your mover

    [Header("Transition tuning")]
    public float uiFadeDuration = 0.45f;
    public AnimationCurve uiEase = AnimationCurve.EaseInOut(0,0,1,1);

    void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        leftButton.onClick.AddListener(() => OnChoiceClicked(1, true)); // snap to index 1 (house entry)
        rightButton.onClick.AddListener(() => OnChoiceClicked(0, true)); // transition to index 0 (landscape) — keep transition
    }

    void OnChoiceClicked(int targetIndex, bool snapInsteadOfTransition)
    {
        // ensure UI can't be spammed
        leftButton.interactable = false;
        rightButton.interactable = false;
        StartCoroutine(HideUIAndGoTo(targetIndex, snapInsteadOfTransition));
    }

    IEnumerator HideUIAndGoTo(int targetIndex, bool snapInsteadOfTransition)
    {
        // Move camera either by snapping or by smooth transition
        if (cameraMover != null)
        {
            if (snapInsteadOfTransition)
            {
                cameraMover.SnapToIndex(targetIndex);
            }
            else
            {
                cameraMover.StartTransitionToIndex(targetIndex);
            }
        }
        // fade-out UI
        float elapsed = 0f;
        float start = canvasGroup.alpha;
        while (elapsed < uiFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = uiEase.Evaluate(Mathf.Clamp01(elapsed / uiFadeDuration));
            canvasGroup.alpha = Mathf.Lerp(start, 0f, t);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        // render final preview frame if previews are in "Once" mode and not already rendered
        if (leftPreview != null) leftPreview.EnsureRenderNow();
        if (rightPreview != null) rightPreview.EnsureRenderNow();

        // disable preview cameras to avoid interference and GPU cost
        if (leftPreview != null && leftPreview.sourceCamera != null) leftPreview.sourceCamera.enabled = false;
        if (rightPreview != null && rightPreview.sourceCamera != null) rightPreview.sourceCamera.enabled = false;

        // optional: fully deactivate UI root
        gameObject.SetActive(false);

    }

    // Call to show the UI (fade-in)
    public void ShowUI()
    {
        StopAllCoroutines();
        gameObject.SetActive(true);
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        leftButton.interactable = true;
        rightButton.interactable = true;
        StartCoroutine(FadeIn());
    }

    IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < uiFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = uiEase.Evaluate(Mathf.Clamp01(elapsed / uiFadeDuration));
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }
}

