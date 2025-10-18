using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HoverHighlighter that ONLY highlights the forward/back colliders allowed by CameraMover.currentIndex.
/// - If cameraMover is assigned and forward/back arrays are valid, only those colliders are eligible.
/// - Allowed collider is considered hit if the raycast hit collider is either the allowed collider itself or a child of it.
/// - The Highlightable used is the one returned from allowedCollider.GetComponentInParent<Highlightable>().
/// - Falls back to legacy behavior when cameraMover or arrays are not present/mismatched.
/// </summary>
[DisallowMultipleComponent]
public class HoverHighlighter : MonoBehaviour
{
    [Tooltip("Mask used for detecting clickable objects. Should match CameraMover's raycastMask.")]
    public LayerMask raycastMask = ~0;
    [Tooltip("Max distance for hover raycast")]
    public float maxDistance = 200f;

    [Tooltip("UI tooltip (optional). A Text or TMP element to show hints.")]
    public CanvasGroup tooltipGroup;
    public Text tooltipText; // or TMPro.TextMeshProUGUI

    [Tooltip("Time to wait before showing tooltip (prevents flicker)")]
    public float tooltipDelay = 0.12f;

    [Tooltip("Reference to camera used for screen rays. If null, Camera.main will be used.")]
    public Camera raycastCamera;

    [Tooltip("Optional CameraMover to restrict highlightable colliders to only forward/back of current index.")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover;

    // runtime
    Highlightable current = null;
    Highlightable last = null;
    float hoverTimer = 0f;

    void Start()
    {
        if (tooltipGroup != null)
        {
            tooltipGroup.alpha = 0f;
            tooltipGroup.blocksRaycasts = false;
            tooltipGroup.interactable = false;
        }
    }

    void Update()
    {
        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, raycastMask))
        {
            // Determine the single allowed Highlightable for the current index (if any)
            Highlightable allowedHighlightable = null;
            Collider allowedCollider = null;
            bool usingRestriction = false;

            if (cameraMover != null && cameraMover.cameraPoints != null && cameraMover.cameraPoints.Length > 0)
            {
                int idx = Mathf.Clamp(cameraMover.currentIndex, 0, cameraMover.cameraPoints.Length - 1);

                bool hasForward = cameraMover.forwardColliders != null && cameraMover.forwardColliders.Length == cameraMover.cameraPoints.Length;
                bool hasBack = cameraMover.backColliders != null && cameraMover.backColliders.Length == cameraMover.cameraPoints.Length;

                if (hasForward || hasBack)
                {
                    usingRestriction = true;

                    // prefer forward then back (both can be allowed — we'll allow either)
                    if (hasForward && cameraMover.forwardColliders[idx] != null)
                    {
                        allowedCollider = cameraMover.forwardColliders[idx];
                        allowedHighlightable = allowedCollider.GetComponentInParent<Highlightable>();
                    }

                    if (hasBack && cameraMover.backColliders[idx] != null)
                    {
                        // if forward and back are both present and different, we still want to allow either.
                        // we'll check both below; to simplify, null out allowedHighlightable here and handle both.
                        // So store them separately by overriding allowedCollider to null to indicate both must be checked.
                        // Instead of complex state, we'll handle checks explicitly below.
                        // For now we just continue to next step.
                    }
                }
            }

            // Decide if we should highlight an allowed highlightable (restricted mode)
            Highlightable hitHighlight = null;
            if (cameraMover != null && cameraMover.cameraPoints != null && cameraMover.cameraPoints.Length > 0
                && (cameraMover.forwardColliders != null && cameraMover.forwardColliders.Length == cameraMover.cameraPoints.Length
                    || cameraMover.backColliders != null && cameraMover.backColliders.Length == cameraMover.cameraPoints.Length))
            {
                // restricted mode — check forward and back colliders explicitly for match
                int idx = Mathf.Clamp(cameraMover.currentIndex, 0, cameraMover.cameraPoints.Length - 1);

                // check forward collider
                if (cameraMover.forwardColliders != null && cameraMover.forwardColliders.Length == cameraMover.cameraPoints.Length)
                {
                    var f = cameraMover.forwardColliders[idx];
                    if (IsHitUnderCollider(hit.collider, f))
                    {
                        hitHighlight = f != null ? f.GetComponentInParent<Highlightable>() : null;
                    }
                }

                // check back collider (if not already matched)
                if (hitHighlight == null && cameraMover.backColliders != null && cameraMover.backColliders.Length == cameraMover.cameraPoints.Length)
                {
                    var b = cameraMover.backColliders[idx];
                    if (IsHitUnderCollider(hit.collider, b))
                    {
                        hitHighlight = b != null ? b.GetComponentInParent<Highlightable>() : null;
                    }
                }

                // Only allow highlight if we found a matching allowed highlightable and it exists
                if (hitHighlight != null)
                {
                    current = hitHighlight;
                    hoverTimer += Time.deltaTime;
                }
                else
                {
                    // no allowed collider was hit -> clear
                    ResetCurrent();
                }
            }
            else
            {
                // legacy/fallback mode: highlight whatever Highlightable is under the cursor
                var found = hit.collider.GetComponentInParent<Highlightable>();
                if (found != null)
                {
                    if (found != current)
                    {
                        if (current != null) current.SetHighlighted(false);
                        current = found;
                        hoverTimer = 0f;
                    }
                    else
                    {
                        hoverTimer += Time.deltaTime;
                    }
                }
                else
                {
                    ResetCurrent();
                }
            }

            // show tooltip after delay if applicable
            if (current != null && tooltipGroup != null && tooltipText != null && hoverTimer >= tooltipDelay)
            {
                string hint = GetHintText(current);
                tooltipText.text = hint;
                tooltipGroup.alpha = 1f;

                Vector2 pos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    tooltipGroup.transform.parent as RectTransform,
                    Input.mousePosition, cam, out pos);
                (tooltipGroup.transform as RectTransform).anchoredPosition = pos + new Vector2(16f, -24f);
            }
        }
        else
        {
            ResetCurrent();
        }

        // apply highlight toggles
        if (last != current)
        {
            if (last != null) last.SetHighlighted(false);
            if (current != null) current.SetHighlighted(true);
            last = current;
        }
    }

    // Returns true if the hit collider is exactly the allowed collider or is a child of it.
    static bool IsHitUnderCollider(Collider hit, Collider allowed)
    {
        if (allowed == null || hit == null) return false;
        if (hit == allowed) return true;
        return hit.transform.IsChildOf(allowed.transform);
    }

    void ResetCurrent()
    {
        if (current != null) current.SetHighlighted(false);
        current = null;
        hoverTimer = 0f;
        if (tooltipGroup != null)
            tooltipGroup.alpha = 0f;
    }

    string GetHintText(Highlightable h)
    {
        var hintField = h.GetType().GetField("hintText");
        if (hintField != null)
        {
            object v = hintField.GetValue(h);
            if (v is string s && !string.IsNullOrEmpty(s)) return s;
        }
        return "Click to interact";
    }
}

