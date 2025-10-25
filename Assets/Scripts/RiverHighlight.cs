using UnityEngine;
using System;

[DisallowMultipleComponent]
public class RiverHighlight : MonoBehaviour
{
    [Header("Materials")]
    public Material UnlitWhite;
    public Material original;

    [Header("River Raycast")]
    public LayerMask Water;
    public float maxRayDistance = 150f;

    [Header("Panel (creative)")]
    [Tooltip("The GameObject (Transform) that is a child of the main camera and holds the world-space canvas/panel.\nIt should be at localPosition (0,0,0) initially.")]
    public Transform creativeRoot;

    [Tooltip("Distance in local camera-space to place the creativeRoot when opened.")]
    public float openDistance = 9.5f;

    [Tooltip("Time (seconds) to animate open/close.")]
    public float duration = 0.7f;

    [Tooltip("Easing curve for the open/close animation.")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Colliders which, when clicked while panel is open, will NOT trigger BeginClose().\nUsually these are the clickable images/buttons' colliders.")]
    public Collider[] ignoreCloseColliders;

    [Header("Integration")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover;

    [Header("Camera Look Control")]
    [Tooltip("Reference to the CameraPositionDrivenMicroLook script to disable while the panel is open.")]
    public CameraPositionDrivenMicroLook mouseLook;

    // runtime
    Material current;
    bool isOpen = false;
    bool isAnimating = false;
    float animT = 0f;
    Vector3 animStartLocalPos;
    Vector3 animTargetLocalPos;
    float animDuration = 0.7f;

    void Start()
    {
        current = original;
        // sanity defaults
        if (creativeRoot == null)
        {
            Debug.LogWarning("[RiverHighlight] creativeRoot not assigned. Panel open/close will be skipped.");
        }
        animDuration = Mathf.Max(0.01f, duration);

        // Ensure creativeRoot starts at zero local position (user said it's at 0,0,0)
        if (creativeRoot != null)
        {
            creativeRoot.localPosition = Vector3.zero;
        }
    }

    void Update()
    {
        // keep the river highlight material behaviour
        if (cameraMover != null && cameraMover.currentIndex == 0)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            bool hitWater = Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, Water);
            current = hitWater ? UnlitWhite : original;
            var rend = GetComponent<Renderer>();
            if (rend != null) rend.material = current;
        } else {
            GetComponent<Renderer>().material = original;
        }

        // animate creativeRoot if requested
        if (isAnimating && creativeRoot != null)
        {
            animT += Time.deltaTime / Mathf.Max(0.0001f, animDuration);
            float u = Mathf.Clamp01(animT);
            float e = ease != null ? ease.Evaluate(u) : u;
            creativeRoot.localPosition = Vector3.Lerp(animStartLocalPos, animTargetLocalPos, e);

            if (u >= 1f - 1e-6f)
            {
                isAnimating = false;
                // ensure final exact position
                creativeRoot.localPosition = animTargetLocalPos;
            }
        }

        // Input handling:
        // 1) If not open, clicking river toggles open.
        // 2) If open, clicking anywhere closes unless click hits an ignored collider.
        HandleClickInput();
    }

    void HandleClickInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // If not open: if click hits the river layer, open panel
        if (!isOpen && creativeRoot != null)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, Water))
            {
                // Only react when cameraMover index is 0 (as your original behavior implied)
                if (cameraMover == null || cameraMover.currentIndex == 0)
                {
                    BeginOpen();
                }
            }
            return;
        }

        // If open: determine if click hit an ignore-collider. If yes, do nothing (let UI handle it).
        if (isOpen)
        {
            // Raycast against the scene (use a reasonable max distance)
            if (Physics.Raycast(ray, out RaycastHit worldHit, 200f))
            {
                if (IsHitUnderAnyCollider(worldHit.collider, ignoreCloseColliders))
                {
                    // Click is on an interactive image/collider — do not close
                    return;
                }
            }

            // If ray didn't hit any ignore colliders, then close
            BeginClose();
        }
    }

    // PUBLIC API
    public void BeginOpen()
    {
        if (creativeRoot == null) return;
        if (isAnimating) return;

        isOpen = true;
        isAnimating = true;
        animT = 0f;
        animDuration = Mathf.Max(0.01f, duration);

        animStartLocalPos = creativeRoot.localPosition;
        animTargetLocalPos = Vector3.forward * openDistance; // camera-local forward

        // 🧠 Disable camera look + movement
        if (mouseLook != null)
            mouseLook.BlendOut(0.25f);
        if (cameraMover != null)
            cameraMover.enabled = false; // disables all highlighting & click transitions
    }

    public void BeginClose()
    {
        if (creativeRoot == null) return;
        if (isAnimating) return;

        isOpen = false;
        isAnimating = true;
        animT = 0f;
        animDuration = Mathf.Max(0.01f, duration);

        animStartLocalPos = creativeRoot.localPosition;
        animTargetLocalPos = Vector3.zero;

        // 🧠 Re-enable camera look + movement
        if (mouseLook != null)
            mouseLook.BlendIn(0.25f);
        if (cameraMover != null)
            cameraMover.enabled = true; // re-enable camera transitions
    }

    // Helper: returns true if 'hit' is the collider itself or a child of any collider in the list
    static bool IsHitUnderAnyCollider(Collider hit, Collider[] allowed)
    {
        if (hit == null || allowed == null || allowed.Length == 0) return false;
        foreach (var a in allowed)
        {
            if (a == null) continue;
            if (hit == a) return true;
            if (hit.transform.IsChildOf(a.transform)) return true;
        }
        return false;
    }
}

