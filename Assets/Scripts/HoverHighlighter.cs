using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HoverHighlighter
/// - Primary rule: if a CameraMover is assigned and its forward/back arrays are valid, only the forward/back colliders
///   for cameraMover.currentIndex are eligible to produce highlights (child colliders are accepted).
/// - Secondary: 'extraHighlightables' are always eligible (boards/TVs/PCs). Assign them in inspector.
/// - Fallback: if CameraMover or its arrays are not configured, behaves like legacy: highlight any Highlightable under cursor.
/// - Tooltip support with small hover delay.
/// </summary>
[DisallowMultipleComponent]
public class HoverHighlighter : MonoBehaviour
{
    [Header("Raycast")]
    [Tooltip("Mask used for detecting clickable objects. Should match CameraMover's raycastMask.")]
    public LayerMask raycastMask = ~0;
    [Tooltip("Max distance for hover raycast")]
    public float maxDistance = 200f;

    [Header("Tooltip (optional)")]
    [Tooltip("CanvasGroup for tooltip (set up a small UI Text).")]
    public CanvasGroup tooltipGroup;
    public Text tooltipText; // swap to TMP if you use TextMeshPro
    [Tooltip("Delay before showing tooltip (seconds)")]
    public float tooltipDelay = 0.12f;

    [Header("References")]
    [Tooltip("Camera to use for screen rays. If null, Camera.main will be used.")]
    public Camera raycastCamera;

    [Tooltip("Optional CameraMover to restrict highlightable colliders to only forward/back of current index.")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover;

    [Header("Extra highlightables (non-navigation content)")]
    [Tooltip("Content objects (boards, TVs, etc.) that should be highlightable regardless of camera index.")]
    public Highlightable[] extraHighlightables;

    [Tooltip("If true, extraHighlightables are accepted even when cameraMover is present. If false, extraHighlightables are ignored when mover is restricting.")]
    public bool allowExtraRegardlessOfMover = true;

    bool transitionActive = false;            // true while camera mover is transitioning
    bool autoSubscribeToMover = true;         // if true, subscribe on Start/OnEnable
    // runtime state
    Highlightable current = null;
    Highlightable last = null;
    float hoverTimer = 0f;

    void OnEnable()
    {
        if (autoSubscribeToMover && cameraMover != null)
        {
            cameraMover.OnTransitionStart += OnMoverTransitionStarted;
            cameraMover.OnTransitionComplete += OnMoverTransitionComplete;
        }
    }

    void OnDisable()
    {
        if (cameraMover != null)
        {
            cameraMover.OnTransitionStart -= OnMoverTransitionStarted;
            cameraMover.OnTransitionComplete -= OnMoverTransitionComplete;
        }
    }

    void OnMoverTransitionStarted(int destIndex)
    {
        // Immediately stop highlighting and mark transition active
        transitionActive = true;
        ResetCurrent();   // clears highlight + tooltip instantly
    }

    void OnMoverTransitionComplete(int finalIndex)
    {
        // Re-enable highlighting (next Update will resume normal behavior)
        transitionActive = false;
    }


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
        if (transitionActive)
        {
            // optionally keep tooltip cleared
            if (tooltipGroup != null) tooltipGroup.alpha = 0f;
            return;
        }

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null) return;

        // Raycast from mouse
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, raycastMask))
        {
            // What Highlightable is under the cursor (if any)
            Highlightable found = hit.collider.GetComponentInParent<Highlightable>();

            // Determine allowed highlightable using rules:
            // 1) if cameraMover is valid and arrays are present, ONLY forward/back colliders for currentIndex are eligible
            // 2) else fallback to legacy: any found Highlightable is allowed
            // 3) extraHighlightables are also considered (subject to allowExtraRegardlessOfMover)
            Highlightable allowed = null;

            bool moverHasValidArrays = (cameraMover != null
                && cameraMover.cameraPoints != null
                && cameraMover.cameraPoints.Length > 0
                && ((cameraMover.forwardColliders != null && cameraMover.forwardColliders.Length == cameraMover.cameraPoints.Length)
                    || (cameraMover.backColliders != null && cameraMover.backColliders.Length == cameraMover.cameraPoints.Length)));

            if (moverHasValidArrays)
            {
                int idx = Mathf.Clamp(cameraMover.currentIndex, 0, cameraMover.cameraPoints.Length - 1);

                // check forward collider for current index
                if (cameraMover.forwardColliders != null && cameraMover.forwardColliders.Length == cameraMover.cameraPoints.Length)
                {
                    Collider f = cameraMover.forwardColliders[idx];
                    if (IsHitUnderCollider(hit.collider, f))
                    {
                        allowed = (f != null) ? f.GetComponentInParent<Highlightable>() : null;
                    }
                }

                // check back collider if not matched yet
                if (allowed == null && cameraMover.backColliders != null && cameraMover.backColliders.Length == cameraMover.cameraPoints.Length)
                {
                    Collider b = cameraMover.backColliders[idx];
                    if (IsHitUnderCollider(hit.collider, b))
                    {
                        allowed = (b != null) ? b.GetComponentInParent<Highlightable>() : null;
                    }
                }

                // Optionally allow extraHighlightables even when mover has valid arrays
                if (allowed == null && allowExtraRegardlessOfMover && extraHighlightables != null)
                {
                    allowed = FindMatchingExtra(hit.collider);
                }
            }
            else
            {
                // legacy mode: allow any Highlightable under cursor
                allowed = found;

                // also consider extra highlightables if set (helps cases where content uses separate roots)
                if (allowed == null && extraHighlightables != null)
                {
                    allowed = FindMatchingExtra(hit.collider);
                }
            }

            // If cameraMover is present and arrays valid but we didn't find an allowed match,
            // we must not highlight anything -> clear
            if (allowed != null)
            {
                // if the allowed result was found via collider's parent Highlightable but it's null, treat as none
                if (allowed == null)
                {
                    ResetCurrent();
                }
                else
                {
                    // update current/hover timer
                    if (allowed != current)
                    {
                        if (current != null) current.SetHighlighted(false);
                        current = allowed;
                        hoverTimer = 0f;
                    }
                    else
                    {
                        hoverTimer += Time.deltaTime;
                    }

                    // handle tooltip display
                    if (tooltipGroup != null && tooltipText != null && hoverTimer >= tooltipDelay)
                    {
                        tooltipText.text = GetHintText(current);
                        tooltipGroup.alpha = 1f;

                        Vector2 pos;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            tooltipGroup.transform.parent as RectTransform,
                            Input.mousePosition, cam, out pos);
                        (tooltipGroup.transform as RectTransform).anchoredPosition = pos + new Vector2(16f, -24f);
                    }
                }
            }
            else
            {
                ResetCurrent();
            }
        }
        else
        {
            ResetCurrent();
        }

        // apply highlight toggles (only when current changes)
        if (last != current)
        {
            if (last != null) last.SetHighlighted(false);
            if (current != null) current.SetHighlighted(true);
            last = current;
        }
    }

    // Find an Extra Highlightable that owns or is parent of the hit collider
    Highlightable FindMatchingExtra(Collider hitCollider)
    {
        if (extraHighlightables == null) return null;
        foreach (var h in extraHighlightables)
        {
            if (h == null) continue;
            if (hitCollider == null) continue;
            if (hitCollider.transform == h.transform || hitCollider.transform.IsChildOf(h.transform))
                return h;
        }
        return null;
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
        if (h == null) return "Click to interact";
        var hintField = h.GetType().GetField("hintText");
        if (hintField != null)
        {
            object v = hintField.GetValue(h);
            if (v is string s && !string.IsNullOrEmpty(s)) return s;
        }
        return "Click to interact";
    }
}

