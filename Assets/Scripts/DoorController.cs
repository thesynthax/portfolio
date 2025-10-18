using System.Collections;
using UnityEngine;

/// <summary>
/// DoorController: simple hard-coded door animation that reacts to CameraMover transitions
/// Implements the exact behavior described:
/// - 0 -> 1 : open during transition, stay open at 1
/// - 1 -> 2 : close during transition, stay closed at 2
/// - 2 -> 1 : remain closed
/// - 1 -> 0 : open during transition, close after reaching 0
/// 
/// Requires:
/// - a reference to CameraMoverNoCoroutines_WithMouseLookIntegration (mover)
/// - a reference to the rotating door Transform (doorTransform), whose local Y controls opening.
/// </summary>
[DisallowMultipleComponent]
public class DoorController : MonoBehaviour
{
    [Header("References")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration mover;
    [Tooltip("The transform of the visual door that rotates about local Y.")]
    public Transform doorTransform;

    [Header("Angles (local Y)")]
    public float closedY = 0f;    // closed angle (degrees)
    public float openY = -90f;    // open angle (degrees) — your door opens inside at -90

    [Header("Timing")]
    [Tooltip("Multiplier applied to mover.duration when animating door during transitions. 1 = same duration.")]
    public float openDurationMultiplier = 1f;
    [Tooltip("Duration to close door after arriving at index 0 (when coming back to outside).")]
    public float closeAfterArrivalDuration = 0.35f;

    // internal state
    int knownCurrentIndex = 0;
    Coroutine animRoutine = null;

    void Start()
    {
        if (mover == null)
        {
            Debug.LogError("[DoorController] mover reference not assigned.");
            enabled = false;
            return;
        }
        if (doorTransform == null)
        {
            Debug.LogError("[DoorController] doorTransform not assigned.");
            enabled = false;
            return;
        }

        // Determine initial known index by comparing camera position to mover.cameraPoints (safe fallback)
        knownCurrentIndex = ResolveCurrentIndexFromCamera();
        // Subscribe to mover events
        mover.OnTransitionStart += HandleTransitionStart;
        mover.OnTransitionComplete += HandleTransitionComplete;
    }

    void OnDestroy()
    {
        if (mover != null)
        {
            mover.OnTransitionStart -= HandleTransitionStart;
            mover.OnTransitionComplete -= HandleTransitionComplete;
        }
    }

    int ResolveCurrentIndexFromCamera()
    {
        if (mover == null || mover.cameraPoints == null || mover.cameraPoints.Length == 0)
            return 0;

        Vector3 camPos = Camera.main != null ? Camera.main.transform.position : transform.position;
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < mover.cameraPoints.Length; i++)
        {
            var t = mover.cameraPoints[i];
            if (t == null) continue;
            float d = Vector3.SqrMagnitude(t.position - camPos);
            if (d < bestDist) { best = i; bestDist = d; }
        }
        return best;
    }

    // Called when a transition starts. destinationIndex is what the mover passed along.
    void HandleTransitionStart(int destinationIndex)
    {
        int from = knownCurrentIndex;
        int to = Mathf.Clamp(destinationIndex, 0, (mover.cameraPoints != null ? mover.cameraPoints.Length - 1 : 0));

        // Hard-coded rules:
        // 0 -> 1 : open during transition, stay open at 1 (don't close on arrival)
        if (from == 0 && to == 1)
        {
            StartDoorAnimation(openY, mover.duration * openDurationMultiplier, stayOpen: true, closeAfterArrival: false);
        }
        // 1 -> 2 : close during transition, stay closed at 2
        else if (from == 1 && to == 2)
        {
            StartDoorAnimation(closedY, mover.duration * openDurationMultiplier, stayOpen: false, closeAfterArrival: false);
        }
        // 2 -> 1 : remain closed (ensure closed instantly or smoothly)
        else if (from == 2 && to == 1)
        {
            // ensure closed — we animate quickly to closed if not already closed
            StartDoorAnimation(closedY, Mathf.Min(0.15f, mover.duration * 0.25f), stayOpen: false, closeAfterArrival: false);
        }
        // 1 -> 0 : open during transition, then close after reaching 0
        else if (from == 1 && to == 0)
        {
            // open during transition
            StartDoorAnimation(openY, mover.duration * openDurationMultiplier, stayOpen: true, closeAfterArrival: true);
        }
        else
        {
            // For any other transitions we do nothing.
        }
    }

    // Called when transition completes; update knownCurrentIndex and optionally close after arrival in some cases
    void HandleTransitionComplete(int arrivedIndex)
    {
        // Update current index
        knownCurrentIndex = Mathf.Clamp(arrivedIndex, 0, (mover.cameraPoints != null ? mover.cameraPoints.Length - 1 : 0));
        // When arriving at index 0 after a 1->0 transition we should close if a close-after flag was requested.
        // The close-after behavior is handled in StartDoorAnimation by scheduling the close on OnTransitionComplete,
        // but because we don't have per-animation state here we detect the specific scenario: if we just arrived at 0
        // and camera came from 1, we close the door quickly.
        // However StartDoorAnimation already queued a closeAfterArrival flag; we manage that by storing a short-lived token.
        // Simpler: do nothing here because StartDoorAnimation initiated the close coroutine on completion for 1->0.
    }

    // Starts animating the door to targetLocalY over duration seconds.
    // If stayOpen==true we leave the door at target angle (no automatic close).
    // If closeAfterArrival==true we will schedule a close back to closedY after the mover completes (we approximate by waiting for mover.duration and then closing).
    void StartDoorAnimation(float targetLocalY, float duration, bool stayOpen, bool closeAfterArrival)
    {
        // stop existing
        if (animRoutine != null) StopCoroutine(animRoutine);

        // capture current local Y as start
        float currentY = doorTransform.localEulerAngles.y;
        // Unity returns 0..360; convert to -180..180 for smooth interpolation
        currentY = NormalizeAngle(currentY);

        float target = NormalizeAngle(targetLocalY);

        // If target equals current (within small epsilon) and no further action required, skip
        if (Mathf.Abs(Mathf.DeltaAngle(currentY, target)) < 0.5f && !closeAfterArrival)
        {
            // already at desired orientation
            return;
        }

        animRoutine = StartCoroutine(DoDoorAnimationCoroutine(currentY, target, duration, stayOpen, closeAfterArrival));
    }

    IEnumerator DoDoorAnimationCoroutine(float startY, float targetY, float duration, bool stayOpen, bool closeAfterArrival)
    {
        float elapsed = 0f;
        // Smooth step easing
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
            float s = Mathf.SmoothStep(0f, 1f, t);
            float angle = Mathf.LerpAngle(startY, targetY, s);
            SetDoorLocalY(angle);
            yield return null;
        }

        // ensure exact final
        SetDoorLocalY(targetY);

        // if we need to close after arrival (i.e., for 1->0), then wait for mover to finish (approx duration already used),
        // but the mover's duration may be different; best approach is to wait until mover.OnTransitionComplete occurs.
        // We will implement a simple watch: if closeAfterArrival is true, wait until the mover reports that it is at index 0
        // or until a small timeout. We'll attach a one-shot listener.

        if (closeAfterArrival)
        {
            bool closedNow = false;
            void onComplete(int arrivedIndex)
            {
                // we want to close when arrived at 0
                if (arrivedIndex == 0)
                {
                    // start close animation (short)
                    if (animRoutine != null) StopCoroutine(animRoutine);
                    animRoutine = StartCoroutine(CloseNowCoroutine(closeAfterArrivalDuration));
                    closedNow = true;
                }
            }

            mover.OnTransitionComplete += onComplete;

            // timeout fallback (safety) — if OnTransitionComplete didn't fire soon (shouldn't happen), close anyway after 2s
            float timeout = Mathf.Max(1f, duration + 1f);
            float timer = 0f;
            while (!closedNow && timer < timeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            mover.OnTransitionComplete -= onComplete;

            if (!closedNow)
            {
                // fallback close
                if (animRoutine != null) StopCoroutine(animRoutine);
                animRoutine = StartCoroutine(CloseNowCoroutine(closeAfterArrivalDuration));
            }
        }

        // finish
        animRoutine = null;
    }

    IEnumerator CloseNowCoroutine(float closeDur)
    {
        float startY = NormalizeAngle(doorTransform.localEulerAngles.y);
        float target = NormalizeAngle(closedY);
        float elapsed = 0f;
        while (elapsed < closeDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, closeDur));
            float s = Mathf.SmoothStep(0f, 1f, t);
            float angle = Mathf.LerpAngle(startY, target, s);
            SetDoorLocalY(angle);
            yield return null;
        }
        SetDoorLocalY(target);
        animRoutine = null;
    }

    void SetDoorLocalY(float yDegrees)
    {
        Vector3 e = doorTransform.localEulerAngles;
        e.y = yDegrees;
        doorTransform.localEulerAngles = e;
    }

    static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }
}

