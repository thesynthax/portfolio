using UnityEngine;
using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ContentPopout - improved:
/// - computes popout pose so object faces camera and fills ~targetScreenHeightFraction of screen height
/// - locks camera while popped by disabling mouseLook and cameraMover (safe toggles)
/// - clicking anywhere (left mouse) while popped closes the popout
/// - temporarily disables global HoverHighlighter while popped to avoid highlight conflicts
/// - uses frame-driven eased lerps (no coroutines)
/// 
/// Wire mouseLook, cameraMover, hoverHighlighter in inspector if you have them; otherwise the script will try to FindObjectOfType at runtime.
/// </summary>
[DisallowMultipleComponent]
public class ContentPopout : MonoBehaviour
{
    [Header("References (optional, will try to auto-find if null)")]
    [Tooltip("Scene darkener to fade the background. Optional.")]
    public SceneDarkener sceneDarkener;

    [Tooltip("Camera used to compute popout pose (defaults to Camera.main).")]
    public Camera sceneCamera;

    [Tooltip("Optional: micro mouse-look component to lock while popped.")]
    public CameraPositionDrivenMicroLook mouseLook;

    [Tooltip("Optional: camera mover to disable while popped (prevents transitions).")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover;

    [Tooltip("Optional: global hover highlighter to disable while popped (prevents highlighting noise).")]
    public HoverHighlighter hoverHighlighter;

    [Header("Popout parameters")]
    [Tooltip("Local scale multiplier while popped.")]
    public float popoutScale = 1.05f;

    [Tooltip("How long the popout/open animation takes (seconds).")]
    public float openDuration = 0.65f;
    [Tooltip("How long the close animation takes (seconds).")]
    public float closeDuration = 0.45f;

    [Header("Visual tuning")]
    [Range(0.1f, 1f)]
    [Tooltip("Target fraction of screen height the object's bounding box should fill when popped.")]
    public float targetScreenHeightFraction = 0.90f;

    [Header("Easing")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Interaction")]
    [Tooltip("If true, input while animating is ignored to avoid double actions.")]
    public bool blockInputWhileAnimating = true;

    [Header("Click handling")]
    [Tooltip("Buttons that should NOT cause the popout to close when clicked (e.g. social icons).")]
    public Button[] ignoreCloseButtons;

    // internal animation state
    public bool isPopped = false;
    bool isAnimating = false;
    float animT = 0f;
    float animDuration = 0.5f;

    Vector3 startPos, startScale;
    Quaternion startRot;
    Vector3 targetPos, targetScale;
    Quaternion targetRot;

    // store original world pose to restore on close
    Vector3 originalWorldPos;
    Quaternion originalWorldRot;
    Vector3 originalLocalScale;
    bool originalRecorded = false;

    // keep track of toggles we changed so we restore exactly as we found them
    bool mouseLookWasEnabled = false;
    bool cameraMoverWasEnabled = false;
    bool hoverHighlighterWasEnabled = false;

    void Reset()
    {
        sceneCamera = Camera.main;
        popoutScale = 1.05f;
        openDuration = 0.65f;
        closeDuration = 0.45f;
        targetScreenHeightFraction = 0.9f;
        ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    void Awake()
    {
        if (sceneCamera == null) sceneCamera = Camera.main;
        // try auto-find for optional refs
        if (mouseLook == null) mouseLook = FindObjectOfType<CameraPositionDrivenMicroLook>();
        if (cameraMover == null) cameraMover = FindObjectOfType<CameraMoverNoCoroutines_WithMouseLookIntegration>();
        if (hoverHighlighter == null) hoverHighlighter = FindObjectOfType<HoverHighlighter>();
    }

    void OnEnable()
    {
        // record original pose so we can restore it exactly on close
        originalWorldPos = transform.position;
        originalWorldRot = transform.rotation;
        originalLocalScale = transform.localScale;
        originalRecorded = true;
    }

    void Update()
    {
        // If not popped, clicking the object opens it (raycast on mouse down)
        if (!isPopped && !isAnimating && Input.GetMouseButtonDown(0))
        {
            if (blockInputWhileAnimating && isAnimating) { /* skip */ }
            else
            {
                Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
                if (cam != null)
                {
                    Ray r = cam.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(r, out RaycastHit hit, 200f))
                    {
                        if (hit.collider != null && (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform))
                        {
                            BeginOpen();
                        }
                    }
                }
            }
        }

        // If popped, any click should close (except if animating)
        if (isPopped && !isAnimating && Input.GetMouseButtonDown(0))
        {
            // If click hit one of the special UI buttons, don't close — allow the UI button's OnClick to run.
            if (ClickHitIgnoredUIButton())
            {
                // optional: debug
                // Debug.Log("Click hit ignored UI button — not closing popout.");
            }
            else
            {
                // safe to close
                BeginClose();
            }
        }

        // Animate if needed
        if (isAnimating)
        {
            animT += Time.deltaTime / Mathf.Max(0.0001f, animDuration);
            float u = Mathf.Clamp01(animT);
            float e = ease.Evaluate(u);

            transform.position = Vector3.Lerp(startPos, targetPos, e);
            transform.rotation = Quaternion.Slerp(startRot, targetRot, e);
            transform.localScale = Vector3.Lerp(startScale, targetScale, e);

            if (u >= 1f - 1e-6f)
            {
                isAnimating = false;
                // finish state has already been set in BeginOpen/BeginClose
                if (!isPopped)
                {
                    // fully closed — restore the things we disabled earlier
                    RestoreLockedSystems();
                }
                // if opened, we keep systems locked until close
            }
        }
    }

    bool ClickHitIgnoredUIButton()
    {
        if (ignoreCloseButtons == null || ignoreCloseButtons.Length == 0)
            return false;

        // 1. Try standard EventSystem raycast first
        if (EventSystem.current != null)
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            if (results != null && results.Count > 0)
            {
                foreach (var r in results)
                {
                    if (r.gameObject == null) continue;
                    foreach (var b in ignoreCloseButtons)
                    {
                        if (b == null) continue;
                        if (r.gameObject == b.gameObject || r.gameObject.transform.IsChildOf(b.transform))
                            return true;
                    }
                }
            }
        }

        // 2. Fallback: Manual physics raycast (for world-space canvases)
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            foreach (var b in ignoreCloseButtons)
            {
                if (b == null) continue;
                // check if the hit collider is on same object or under button root hierarchy
                if (hit.collider != null && hit.collider.transform.IsChildOf(b.transform))
                    return true;
            }
        }

        return false;
    }

    public void ButtonClicked()
    {
        Debug.Log("button clicked!");
    }

    void BeginOpen()
    {
        Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
        if (cam == null) return;

        // Save original pose (in case object moved after enable)
        originalWorldPos = transform.position;
        originalWorldRot = transform.rotation;
        originalLocalScale = transform.localScale;
        originalRecorded = true;

        // Record current enabled state and then disable camera interaction systems
        if (mouseLook != null)
        {
            mouseLookWasEnabled = mouseLook.enabled;
            // soften mouse look influence so it doesn't snap: request a blend out
            mouseLook.BlendOut(0.12f);
            // also disable component so user can't meddle; we will re-enable on close
            mouseLook.enabled = false;
        }
        if (cameraMover != null)
        {
            cameraMoverWasEnabled = cameraMover.enabled;
            cameraMover.enabled = false;
        }
        if (hoverHighlighter != null)
        {
            hoverHighlighterWasEnabled = hoverHighlighter.enabled;
            hoverHighlighter.enabled = false;
            // also clear any lingering highlights
            // public ResetCurrent() not exposed; we can call ResetCurrent via reflection but simpler: force all Highlightable to unhighlight
            var all = FindObjectsOfType<Highlightable>();
            foreach (var h in all) h.SetHighlighted(false);
        }

        // compute target pose and start anim
        startPos = transform.position;
        startRot = transform.rotation;
        startScale = transform.localScale;

        ComputePopoutPose(cam, out targetPos, out targetRot, out targetScale);

        animDuration = Mathf.Max(0.01f, openDuration);
        animT = 0f;
        isAnimating = true;
        isPopped = true;

        // show darkener
        if (sceneDarkener != null) sceneDarkener.Show(animDuration);
    }

    void BeginClose()
    {
        // start closing: animate from current world pose to original recorded pose
        startPos = transform.position;
        startRot = transform.rotation;
        startScale = transform.localScale;

        if (originalRecorded)
        {
            targetPos = originalWorldPos;
            targetRot = originalWorldRot;
            targetScale = originalLocalScale;
        }
        else
        {
            // fallback
            Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
            targetPos = transform.position + (cam != null ? -cam.transform.forward * 0.5f : Vector3.back * 0.5f);
            targetRot = transform.rotation;
            targetScale = transform.localScale;
        }

        animDuration = Mathf.Max(0.01f, closeDuration);
        animT = 0f;
        isAnimating = true;
        isPopped = false;

        // hide darkener
        if (sceneDarkener != null) sceneDarkener.Hide(animDuration);
    }



    void RestoreLockedSystems()
    {
        // Re-enable systems only after animation finished
        if (mouseLook != null)
        {
            // re-enable component if it was enabled before
            mouseLook.enabled = mouseLookWasEnabled;
            // blend back in smoothly
            mouseLook.BlendIn(0.12f);
        }
        if (cameraMover != null)
        {
            cameraMover.enabled = cameraMoverWasEnabled;
        }
        if (hoverHighlighter != null)
        {
            hoverHighlighter.enabled = hoverHighlighterWasEnabled;
        }
    }

    /// <summary>
    /// Computes a popout pose that places the object's bounding-center in front of the camera at a distance
    /// such that the object's world bounding height occupies ~targetScreenHeightFraction of the vertical frustum.
    /// Also orients the object to face the camera (object +Z opposite camera.forward).
    /// Returns world-space transform for the object's transform.position / rotation / scale.
    /// </summary>
    void ComputePopoutPose(Camera cam, out Vector3 outPos, out Quaternion outRot, out Vector3 outScale)
    {
        // Determine bounding volume
        Bounds? bOpt = ComputeWorldBounds();
        Bounds bounds;
        if (bOpt.HasValue) bounds = bOpt.Value;
        else
        {
            // fallback to small box
            bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        }

        // projected height of bounds along camera.up
        float objWorldHeight = ComputeProjectedHeightOnCameraUp(bounds, cam);

        if (cam.orthographic)
        {
            float visibleWorldHeight = 2f * cam.orthographicSize;
            float desiredWorldHeight = visibleWorldHeight * Mathf.Clamp01(targetScreenHeightFraction);
            float scaleMultiplier = 1f;
            if (objWorldHeight > 1e-6f) scaleMultiplier = desiredWorldHeight / objWorldHeight;
            outScale = transform.localScale * scaleMultiplier * popoutScale;

            // place center at some distance in front of camera
            float z = Mathf.Max(0.5f, cam.nearClipPlane + 0.5f);
            Vector3 desiredCenter = cam.transform.position + cam.transform.forward * z;
            Vector3 posOffset = transform.position - bounds.center;
            outPos = desiredCenter + posOffset;
            outRot = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
            return;
        }
        else
        {
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            float tanHalf = Mathf.Tan(fovRad * 0.5f);
            float frac = Mathf.Clamp01(targetScreenHeightFraction);

            if (objWorldHeight <= 1e-6f || tanHalf <= 1e-6f)
            {
                float fallbackDist = Mathf.Max(1.0f, cam.nearClipPlane + 1.0f);
                outScale = transform.localScale * popoutScale;
                outPos = cam.transform.position + cam.transform.forward * fallbackDist;
                outRot = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
                return;
            }

            float z = objWorldHeight / (2f * frac * tanHalf);
            z = Mathf.Max(z, cam.nearClipPlane + 0.05f);

            Vector3 desiredCenter = cam.transform.position + cam.transform.forward * z;

            // Keep object's local scale (we rely on distance to size). Multiply by popoutScale small tweak.
            outScale = transform.localScale * popoutScale;

            // Compute transform.position so that bounds.center becomes desiredCenter
            Vector3 posOffset = transform.position - bounds.center;
            outPos = desiredCenter + posOffset;

            outRot = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
            return;
        }
    }

    // compute world-space bounds using renderers or colliders (fallback)
    Bounds? ComputeWorldBounds()
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        if (rends != null && rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        if (cols != null && cols.Length > 0)
        {
            Bounds b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return b;
        }

        return null;
    }

    // compute the object's height in world units along the camera.up axis by projecting all 8 bounding-corners onto cam.up
    static float ComputeProjectedHeightOnCameraUp(Bounds bounds, Camera cam)
    {
        Vector3 up = cam.transform.up.normalized;
        Vector3[] corners = new Vector3[8];
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;

        corners[0] = c + new Vector3(-e.x, -e.y, -e.z);
        corners[1] = c + new Vector3(e.x, -e.y, -e.z);
        corners[2] = c + new Vector3(e.x, e.y, -e.z);
        corners[3] = c + new Vector3(-e.x, e.y, -e.z);
        corners[4] = c + new Vector3(-e.x, -e.y, e.z);
        corners[5] = c + new Vector3(e.x, -e.y, e.z);
        corners[6] = c + new Vector3(e.x, e.y, e.z);
        corners[7] = c + new Vector3(-e.x, e.y, e.z);

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            float v = Vector3.Dot(corners[i], up);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return Mathf.Max(0f, max - min);
    }
}

