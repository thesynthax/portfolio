using UnityEngine;
using System;

[DisallowMultipleComponent]
public class CameraMoverNoCoroutines_WithMouseLookIntegration : MonoBehaviour
{
    public enum PathMode { Straight, Curved }

    [Header("Camera points (ordered)")]
    [Tooltip("Ordered list of camera target transforms. Movement is only allowed between neighboring indices.")]
    public Transform[] cameraPoints;

    [Header("Click targets (per-source)")]
    [Tooltip("Collider to click when at index i to move FORWARD to index i+1. Length should be cameraPoints.Length (last element unused).")]
    public Collider[] forwardColliders;
    [Tooltip("Collider to click when at index i to move BACK to index i-1. Length should be cameraPoints.Length (first element unused).")]
    public Collider[] backColliders;

    [Header("Profiles")]
    [Tooltip("Per-point CameraPositionDrivenMicroLook.Profile. Length must match cameraPoints.")]
    public CameraPositionDrivenMicroLook.Profile[] profiles;

    [Header("Transition")]
    public float duration = 1.0f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public PathMode pathMode = PathMode.Curved;
    public float arcHeight = 0.7f;
    public bool slerpRotation = true;

    [Header("Raycast")]
    public LayerMask raycastMask = ~0;

    [Header("Mouse Look Integration")]
    [Tooltip("Reference to the CameraPositionDrivenMicroLook script")]
    public CameraPositionDrivenMicroLook mouseLook;
    [Tooltip("Blend time (seconds) for the mouse look influence when fading in/out")]
    public float mouseLookBlendTime = 0.35f;

    // Events
    public event Action<int> OnTransitionStart;    // passes destination index
    public event Action<int> OnTransitionComplete; // passes final index

    // internal
    [SerializeField]
    int currentIndex = 0;           // current active camera index
    int destinationIndex = -1;      // target index while transitioning
    bool isTransitioning = false;
    float t = 0f;
    Vector3 startPos;
    Quaternion startRot;
    Vector3 p0, p1, p2;
    float currentDuration;

    void Reset()
    {
        duration = 1.0f;
        ease = AnimationCurve.EaseInOut(0,0,1,1);
        pathMode = PathMode.Curved;
        arcHeight = 0.7f;
        slerpRotation = true;
    }

    void Start()
    {
        if (HasCameraPoints())
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, cameraPoints.Length - 1);
            SnapToIndex(currentIndex);
        }
        else
        {
            Debug.LogWarning("[CameraMover] No cameraPoints assigned. Assign cameraPoints in inspector.");
        }

        ValidateArrays();
    }

    void ValidateArrays()
    {
        if (!HasCameraPoints()) return;

        // Ensure forward/back arrays exist and are the expected length (defensive)
        if (forwardColliders == null || forwardColliders.Length != cameraPoints.Length)
        {
            forwardColliders = new Collider[cameraPoints.Length];
            // leaving entries null — inspector should populate
            Debug.LogWarning("[CameraMover] forwardColliders was missing or wrong length. Created empty array. Populate in inspector: forwardColliders[i] is clicked while AT i to go to i+1.");
        }
        if (backColliders == null || backColliders.Length != cameraPoints.Length)
        {
            backColliders = new Collider[cameraPoints.Length];
            Debug.LogWarning("[CameraMover] backColliders was missing or wrong length. Created empty array. Populate in inspector: backColliders[i] is clicked while AT i to go to i-1.");
        }

        if (profiles != null && profiles.Length != 0 && profiles.Length != cameraPoints.Length)
            Debug.LogWarning("[CameraMover] profiles length does not match cameraPoints length. Missing profiles will use mouseLook.profile as fallback.");
    }

    bool HasCameraPoints() => cameraPoints != null && cameraPoints.Length > 0;

    void Update()
    {
        HandleClickInput();

        if (isTransitioning)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, currentDuration);
            float eval = ease.Evaluate(Mathf.Clamp01(t));

            Transform targetTransform = GetTargetTransform(destinationIndex);

            // position
            if (pathMode == PathMode.Straight)
            {
                transform.position = Vector3.LerpUnclamped(startPos, GetTargetPosition(targetTransform), eval);
            }
            else
            {
                transform.position = BezierQuadratic(p0, p1, p2, eval);
            }

            // rotation
            Quaternion targetRot = GetTargetRotation(targetTransform);
            if (slerpRotation)
                transform.rotation = Quaternion.Slerp(startRot, targetRot, eval);
            else
            {
                Vector3 startEuler = startRot.eulerAngles;
                Vector3 targetEuler = targetRot.eulerAngles;
                float yaw = Mathf.LerpAngle(startEuler.y, targetEuler.y, eval);
                float pitch = Mathf.LerpAngle(startEuler.x, targetEuler.x, eval);
                transform.rotation = Quaternion.Euler(pitch, yaw, startEuler.z);
            }

            if (t >= 1f)
            {
                EndTransition();
            }
        }
    }

    void HandleClickInput()
    {
        if (Input.GetMouseButtonDown(0) && !isTransitioning)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, raycastMask))
            {
                // First check forward/back arrays for currentIndex
                if (HasCameraPoints())
                {
                    // check forward collider at currentIndex -> move to currentIndex+1
                    if (currentIndex >= 0 && currentIndex < forwardColliders.Length)
                    {
                        var f = forwardColliders[currentIndex];
                        if (f != null && hit.collider == f)
                        {
                            TryMoveToIndex(currentIndex + 1);
                            return;
                        }
                    }

                    // check back collider at currentIndex -> move to currentIndex-1
                    if (currentIndex >= 0 && currentIndex < backColliders.Length)
                    {
                        var b = backColliders[currentIndex];
                        if (b != null && hit.collider == b)
                        {
                            TryMoveToIndex(currentIndex - 1);
                            return;
                        }
                    }

                    // As a convenience/compat: if user also filled forward/back for adjacent slot (e.g. clicking a door that sits at the destination)
                    // check forward collider of the previous index (that might be the same physical collider)
                    // This makes it tolerant if you assigned collider to the destination's forward/back instead of the source.
                    // Check neighbor forward/back colliders too (defensive).
                    // previous index forward (maps to currentIndex if clicked from neighbor)
                    if (currentIndex - 1 >= 0)
                    {
                        var fp = forwardColliders[currentIndex - 1];
                        if (fp != null && hit.collider == fp)
                        {
                            TryMoveToIndex(currentIndex); // same as moving to currentIndex (should be handled above normally)
                            return;
                        }
                    }
                    if (currentIndex + 1 < forwardColliders.Length)
                    {
                        var fn = forwardColliders[currentIndex + 1];
                        if (fn != null && hit.collider == fn)
                        {
                            TryMoveToIndex(currentIndex + 1);
                            return;
                        }
                    }
                }

                // If none matched, ignore the click
            }
        }
    }

    // Attempt a move to targetIndex with adjacency check (only neighbor moves allowed)
    void TryMoveToIndex(int targetIndex)
    {
        if (!HasCameraPoints())
        {
            Debug.LogWarning("[CameraMover] TryMoveToIndex called but cameraPoints not configured.");
            return;
        }

        // clamp
        targetIndex = Mathf.Clamp(targetIndex, 0, cameraPoints.Length - 1);

        if (targetIndex == currentIndex)
            return; // already here

        if (Mathf.Abs(targetIndex - currentIndex) == 1)
        {
            StartTransitionToIndex(targetIndex);
        }
        else
        {
            // Not adjacent — ignore
        }
    }

    // Public API: start transition to a specific index (obeys adjacency)
    public void StartTransitionToIndex(int targetIndex)
    {
        if (!HasCameraPoints())
        {
            Debug.LogWarning("[CameraMover] StartTransitionToIndex called but no cameraPoints configured.");
            return;
        }
        targetIndex = Mathf.Clamp(targetIndex, 0, cameraPoints.Length - 1);
        if (isTransitioning) return;
        if (targetIndex == currentIndex) return;
        if (Mathf.Abs(targetIndex - currentIndex) != 1)
        {
            Debug.LogWarning($"[CameraMover] StartTransitionToIndex: target {targetIndex} is not adjacent to current {currentIndex}. Ignored.");
            return;
        }

        BeginTransitionToIndex(targetIndex);
    }

    void BeginTransitionToIndex(int targetIndex)
    {
        Transform target = cameraPoints[targetIndex];
        if (target == null)
        {
            Debug.LogWarning($"[CameraMover] cameraPoints[{targetIndex}] is null. Aborting transition.");
            return;
        }

        isTransitioning = true;
        destinationIndex = targetIndex;
        startPos = transform.position;
        startRot = transform.rotation;
        currentDuration = Mathf.Max(0.01f, duration);
        t = 0f;

        if (pathMode == PathMode.Curved)
        {
            p0 = startPos;
            p2 = GetTargetPosition(target);
            Vector3 mid = (p0 + p2) * 0.5f;
            Vector3 forwardDir = (p2 - p0).normalized;
            float forwardPush = Mathf.Clamp01(Vector3.Distance(p0, p2) * 0.2f);
            mid += forwardDir * forwardPush;
            float distance = Vector3.Distance(p0, p2);
            float scaledArc = arcHeight * Mathf.Clamp01(distance / 3f);
            mid += Vector3.up * scaledArc;
            p1 = mid;
        }

        if (mouseLook != null)
        {
            mouseLook.BlendOut(mouseLookBlendTime);
        }

        OnTransitionStart?.Invoke(destinationIndex);
    }

    void EndTransition()
    {
        Transform target = GetTargetTransform(destinationIndex);
        Vector3 finalPos = GetTargetPosition(target);
        Quaternion finalRot = GetTargetRotation(target);
        transform.position = finalPos;
        transform.rotation = finalRot;

        isTransitioning = false;
        t = 0f;

        // update current index
        currentIndex = Mathf.Clamp(destinationIndex, 0, cameraPoints.Length - 1);
        destinationIndex = -1;

        // Apply mouseLook profile for this point (if any), and restore mouseLook smoothly
        if (mouseLook != null)
        {
            CameraPositionDrivenMicroLook.Profile applyProfile;

            if (profiles != null && profiles.Length == cameraPoints.Length)
            {
                applyProfile = profiles[currentIndex];
            }
            else
            {
                // fallback to the mouseLook's inspector default
                applyProfile = mouseLook.profile;
            }

            mouseLook.ApplyProfile(applyProfile);

            // set neutral pose to the camera final pose so offsets are computed relative to this position
            mouseLook.SetInitialToCurrent();

            // sync yaw/pitch internal values
            mouseLook.SyncToCameraRotationInstant();

            // blend influence back in
            mouseLook.BlendIn(mouseLookBlendTime);
        }

        OnTransitionComplete?.Invoke(currentIndex);
    }

    Transform GetTargetTransform(int idx)
    {
        if (idx >= 0 && idx < cameraPoints.Length)
            return cameraPoints[idx];
        return cameraPoints[Mathf.Clamp(currentIndex, 0, cameraPoints.Length - 1)];
    }

    Vector3 GetTargetPosition(Transform target)
    {
        return target != null ? target.position : transform.position;
    }

    Quaternion GetTargetRotation(Transform target)
    {
        return target != null ? target.rotation : transform.rotation;
    }

    static Vector3 BezierQuadratic(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    // Utility: instant snap to an index (useful on load)
    public void SnapToIndex(int idx)
    {
        if (!HasCameraPoints())
        {
            Debug.LogWarning("[CameraMover] SnapToIndex called but cameraPoints not configured.");
            return;
        }
        idx = Mathf.Clamp(idx, 0, cameraPoints.Length - 1);
        Transform target = cameraPoints[idx];
        if (target == null) return;
        transform.position = target.position;
        transform.rotation = target.rotation;
        currentIndex = idx;
        isTransitioning = false;
        destinationIndex = -1;

        if (mouseLook != null)
        {
            // apply profile if exists
            if (profiles != null && profiles.Length == cameraPoints.Length)
                mouseLook.ApplyProfile(profiles[currentIndex]);
            else
                mouseLook.ApplyProfile(mouseLook.profile);

            mouseLook.SetInitialToCurrent();
            mouseLook.SyncToCameraRotationInstant();
            mouseLook.SetInfluenceInstant(1f);
        }
    }

    // Draw bezier preview for consecutive points
    void OnDrawGizmosSelected()
    {
        // If currently transitioning and p0/p1/p2 are valid, draw active curve
        if (isTransitioning && p0 != Vector3.zero && p2 != Vector3.zero && pathMode == PathMode.Curved)
        {
            Gizmos.color = Color.cyan;
            DrawQuadratic(p0, p1, p2, 24);
        }

        // If cameraPoints are assigned, draw preview for each adjacent pair
        if (cameraPoints != null && cameraPoints.Length > 1 && pathMode == PathMode.Curved)
        {
            for (int i = 0; i < cameraPoints.Length - 1; i++)
            {
                Transform aT = cameraPoints[i];
                Transform cT = cameraPoints[i + 1];
                if (aT == null || cT == null) continue;

                Vector3 a = aT.position;
                Vector3 c = cT.position;
                Vector3 mid = (a + c) * 0.5f;
                Vector3 forwardDir = (c - a).normalized;
                float forwardPush = Mathf.Clamp01(Vector3.Distance(a, c) * 0.2f);
                mid += forwardDir * forwardPush;
                float distance = Vector3.Distance(a, c);
                float scaledArc = arcHeight * Mathf.Clamp01(distance / 3f);
                mid += Vector3.up * scaledArc;
                Vector3 b = mid;

                // color alternation to make it readable
                Gizmos.color = (i % 2 == 0) ? Color.yellow : Color.magenta;
                DrawQuadratic(a, b, c, 32);
            }
        }
    }

    void DrawQuadratic(Vector3 a, Vector3 b, Vector3 c, int steps)
    {
        Vector3 prev = a;
        for (int i = 1; i <= steps; i++)
        {
            float tt = i / (float)steps;
            Vector3 p = BezierQuadratic(a, b, c, tt);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}

