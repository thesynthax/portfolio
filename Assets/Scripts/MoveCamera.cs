using UnityEngine;
using System;
using System.Collections.Generic;

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
    [Tooltip("Vertical arc height (used for fallback arc or combined with horizontal curve).")]
    public float arcHeight = 0.7f;
    public bool slerpRotation = true;

    [Header("Raycast")]
    public LayerMask raycastMask = ~0;
    [Tooltip("Camera used for ScreenPointToRay for clicks. If null, Camera.main will be used.")]
    public Camera raycastCamera;

    [Header("Mouse Look Integration")]
    [Tooltip("Reference to the CameraPositionDrivenMicroLook script")]
    public CameraPositionDrivenMicroLook mouseLook;
    [Tooltip("Blend time (seconds) for the mouse look influence when fading in/out")]
    public float mouseLookBlendTime = 0.35f;

    // --- Path definitions (new) --------------------------------------
    [System.Serializable]
    public class PathDefinition
    {
        [Tooltip("index of source camera point")]
        public int fromIndex;
        [Tooltip("index of destination camera point")]
        public int toIndex;
        [Tooltip("Optional control points (in world space) that the camera should pass through. Leave empty for direct curve.")]
        public Transform[] controlPoints;
        [Tooltip("If true, the curve will be computed in XZ plane (horizontal) and arcHeight applied vertically on top.")]
        public bool treatAsHorizontalOnly = true;
        [Tooltip("Multiplier applied to lateral offsets of control points (useful if you want to exaggerate)")]
        public float lateralMultiplier = 1f;
        [Tooltip("Catmull-Rom tension parameter (0..1). Lower = smoother, higher = tighter.")]
        [Range(0f, 1f)]
        public float tension = 0.5f;
        [Tooltip("Color used to draw the gizmo for this path")]
        public Color gizmoColor = Color.cyan;
        public float duration = 1f;
    }

    [Tooltip("List of custom path definitions between camera points (fromIndex -> toIndex).")]
    public PathDefinition[] customPaths;

    // Events
    public event Action<int> OnTransitionStart;    // passes destination index
    public event Action<int> OnTransitionComplete; // passes final index

    // internal
    public int currentIndex { get; private set; }          // current active camera index
    int destinationIndex = -1;      // target index while transitioning
    bool isTransitioning = false;
    float t = 0f;
    Vector3 startPos;
    Quaternion startRot;
    float currentDuration;

    // Path sampling cache
    Vector3[] activePathPoints; // points used by current path (including start & end)
    int activeSampleResolution = 60; // number of samples (used only for OnDrawGizmos preview)

    void Reset()
    {
        duration = 1.0f;
        ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        pathMode = PathMode.Curved;
        arcHeight = 0.7f;
        slerpRotation = true;
    }

    void Start()
    {
        currentIndex = 0;
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

        if (forwardColliders == null || forwardColliders.Length != cameraPoints.Length)
        {
            forwardColliders = new Collider[cameraPoints.Length];
            Debug.LogWarning("[CameraMover] forwardColliders was missing or wrong length. Created empty array.");
        }
        if (backColliders == null || backColliders.Length != cameraPoints.Length)
        {
            backColliders = new Collider[cameraPoints.Length];
            Debug.LogWarning("[CameraMover] backColliders was missing or wrong length. Created empty array.");
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

            // position
            if (pathMode == PathMode.Straight || activePathPoints == null || activePathPoints.Length == 0)
            {
                // fallback to old behavior: straight or simple vertical arc
                if (pathMode == PathMode.Straight)
                {
                    Transform targetTransform = GetTargetTransform(destinationIndex);
                    transform.position = Vector3.LerpUnclamped(startPos, GetTargetPosition(targetTransform), eval);
                }
                else
                {
                    // vertical arc fallback (old behavior)
                    Transform targetTransform = GetTargetTransform(destinationIndex);
                    Vector3 end = GetTargetPosition(targetTransform);
                    Vector3 pos = Vector3.LerpUnclamped(startPos, end, eval);
                    // add vertical arc
                    float arc = Mathf.Sin(Mathf.Clamp01(eval) * Mathf.PI) * arcHeight;
                    pos.y += arc;
                    transform.position = pos;
                }
            }
            else
            {
                // sample along the custom path using Catmull-Rom
                float sampleT = Mathf.Clamp01(eval);
                Vector3 pos = SamplePathPosition(activePathPoints, sampleT);
                transform.position = pos;
            }

            // rotation
            Transform targetRotT = GetTargetTransform(destinationIndex);
            Quaternion targetRot = GetTargetRotation(targetRotT);
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
            Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, raycastMask))
            {
                // priority: forward/back colliders for current index
                if (HasCameraPoints())
                {
                    if (currentIndex >= 0 && currentIndex < forwardColliders.Length)
                    {
                        var f = forwardColliders[currentIndex];
                        if (f != null && hit.collider == f)
                        {
                            TryMoveToIndex(currentIndex + 1);
                            return;
                        }
                    }
                    if (currentIndex >= 0 && currentIndex < backColliders.Length)
                    {
                        var b = backColliders[currentIndex];
                        if (b != null && hit.collider == b)
                        {
                            TryMoveToIndex(currentIndex - 1);
                            return;
                        }
                    }

                    // defensive neighbor checks (if you've assigned on neighbor)
                    if (currentIndex - 1 >= 0)
                    {
                        var fp = forwardColliders[currentIndex - 1];
                        if (fp != null && hit.collider == fp)
                        {
                            TryMoveToIndex(currentIndex); // redundant
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
            }
        }

        backColliders[3].enabled = currentIndex != 2; 
    }

    void TryMoveToIndex(int targetIndex)
    {
        if (!HasCameraPoints())
        {
            Debug.LogWarning("[CameraMover] TryMoveToIndex called but cameraPoints not configured.");
            return;
        }
        targetIndex = Mathf.Clamp(targetIndex, 0, cameraPoints.Length - 1);
        if (targetIndex == currentIndex) return;
        if (Mathf.Abs(targetIndex - currentIndex) == 1)
        {
            StartTransitionToIndex(targetIndex, false);
        }
    }

    public void StartTransitionToIndex(int targetIndex, bool allowNonAdjacent = false)
    {
        if (!HasCameraPoints())
        {
            Debug.LogWarning("[CameraMover] StartTransitionToIndex called but no cameraPoints configured.");
            return;
        }
        targetIndex = Mathf.Clamp(targetIndex, 0, cameraPoints.Length - 1);
        if (isTransitioning) return;
        if (targetIndex == currentIndex) return;
        if (!allowNonAdjacent && Mathf.Abs(targetIndex - currentIndex) != 1)
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
        float thisDuration = duration;
        foreach (PathDefinition path in customPaths) {
            if ((currentIndex == path.fromIndex && destinationIndex == path.toIndex)
                    || (currentIndex == path.toIndex && destinationIndex == path.fromIndex)) {
                thisDuration = path.duration;
            }
        }
        currentDuration = Mathf.Max(0.01f, thisDuration);
        t = 0f;

        // build activePathPoints by checking customPaths
        activePathPoints = BuildActivePathPoints(currentIndex, destinationIndex);

        // if using Curved fallback and no custom path, compute p0/p1/p2 via arc: handled in Update

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

        currentIndex = Mathf.Clamp(destinationIndex, 0, cameraPoints.Length - 1);
        destinationIndex = -1;

        // clear active path
        activePathPoints = null;

        if (mouseLook != null)
        {
            CameraPositionDrivenMicroLook.Profile applyProfile;
            if (profiles != null && profiles.Length == cameraPoints.Length)
            {
                applyProfile = profiles[currentIndex];
            }
            else
            {
                applyProfile = mouseLook.profile;
            }

            mouseLook.ApplyProfile(applyProfile);
            mouseLook.SetInitialToCurrent();
            mouseLook.SyncToCameraRotationInstant();
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

    // --- Path building & sampling -------------------------------------

    // Return path points (world positions) for transition current->dest, or null if none
    Vector3[] BuildActivePathPoints(int fromIdx, int toIdx)
    {
        if (customPaths == null || customPaths.Length == 0) return null;

        // find a matching path def (either direction)
        PathDefinition def = null;
        foreach (var p in customPaths)
        {
            if (p == null) continue;
            if ((p.fromIndex == fromIdx && p.toIndex == toIdx) || (p.fromIndex == toIdx && p.toIndex == fromIdx))
            {
                def = p;
                break;
            }
        }
        if (def == null) return null;

        // Build points list: start, controlPoints..., end
        List<Vector3> pts = new List<Vector3>();
        Vector3 start = GetTargetPosition(cameraPoints[fromIdx]);
        Vector3 end = GetTargetPosition(cameraPoints[toIdx]);
        pts.Add(start);

        if (def.controlPoints != null && def.controlPoints.Length > 0)
        {
            // append control points world positions; if connection reversed, reverse order
            if (def.fromIndex == fromIdx)
            {
                foreach (var ct in def.controlPoints)
                {
                    if (ct == null) continue;
                    Vector3 p = ct.position;
                    if (def.treatAsHorizontalOnly)
                    {
                        // if horizontal-only, apply lateral multiplier in local XZ plane relative to segment
                        // but we assume control points are placed in world where you want them so just use them
                        // multiply lateral offset around mid to exaggerate if needed:
                        Vector3 mid = (start + end) * 0.5f;
                        Vector3 lateral = p - mid;
                        lateral.y = 0f;
                        p = mid + lateral * def.lateralMultiplier + Vector3.up * p.y;
                    }
                    pts.Add(p);
                }
            }
            else
            {
                // path def is reversed relative to current direction: add control points in reverse order
                for (int i = def.controlPoints.Length - 1; i >= 0; i--)
                {
                    var ct = def.controlPoints[i];
                    if (ct == null) continue;
                    Vector3 p = ct.position;
                    if (def.treatAsHorizontalOnly)
                    {
                        Vector3 mid = (start + end) * 0.5f;
                        Vector3 lateral = p - mid;
                        lateral.y = 0f;
                        p = mid + lateral * def.lateralMultiplier + Vector3.up * p.y;
                    }
                    pts.Add(p);
                }
            }
        }

        pts.Add(end);

        // convert to array
        return pts.ToArray();
    }

    // Sample a position along a poly-curve defined by pts[] using Catmull-Rom.
    // t in [0,1] across entire path. Preserves vertex Y by default but supports horizontal-only flag
    Vector3 SamplePathPosition(Vector3[] pts, float t01)
    {
        if (pts == null || pts.Length == 0) return transform.position;
        if (pts.Length == 1) return pts[0];
        if (pts.Length == 2) return Vector3.Lerp(pts[0], pts[1], t01);

        // Map t01 to segment index
        int segments = pts.Length - 1;
        float scaled = t01 * segments;
        int seg = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, segments - 1);
        float localT = Mathf.Clamp01(scaled - seg);

        // For Catmull-Rom we need p0,p1,p2,p3 where p1..p2 is the segment
        Vector3 p0 = (seg - 1 >= 0) ? pts[seg - 1] : pts[seg];
        Vector3 p1 = pts[seg];
        Vector3 p2 = pts[seg + 1];
        Vector3 p3 = (seg + 2 < pts.Length) ? pts[seg + 2] : pts[seg + 1];

        // Use standard Catmull-Rom with tension 0.5 (centripetal-ish). We can expose tension per PathDefinition if needed.
        // Implement a centripetal Catmull-Rom parameterized by alpha (use 0.5 default if not provided)
        float alpha = 0.5f;

        // Compute point
        return CatmullRom(p0, p1, p2, p3, localT, alpha);
    }

    // Catmull-Rom (centripetal) interpolation
    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t, float alpha = 0.5f)
    {
        // Based on parametric Catmull-Rom with chordal parameterization
        float GetK(Vector3 a, Vector3 b) => Mathf.Pow((b - a).magnitude, alpha);

        float k0 = 0f;
        float k1 = k0 + GetK(p0, p1);
        float k2 = k1 + GetK(p1, p2);
        float k3 = k2 + GetK(p2, p3);

        // Avoid degenerate
        if (Mathf.Approximately(k1, k0)) k1 = k0 + 1e-4f;
        if (Mathf.Approximately(k2, k1)) k2 = k1 + 1e-4f;
        if (Mathf.Approximately(k3, k2)) k3 = k2 + 1e-4f;

        float t0 = Mathf.Lerp(k1, k2, t);
        // Interpolate
        Vector3 A1 = ((k1 - t0) / (k1 - k0)) * p0 + ((t0 - k0) / (k1 - k0)) * p1;
        Vector3 A2 = ((k2 - t0) / (k2 - k1)) * p1 + ((t0 - k1) / (k2 - k1)) * p2;
        Vector3 A3 = ((k3 - t0) / (k3 - k2)) * p2 + ((t0 - k2) / (k3 - k2)) * p3;

        float t1 = Mathf.Lerp(k1, k2, t);
        Vector3 B1 = ((k2 - t1) / (k2 - k0)) * A1 + ((t1 - k0) / (k2 - k0)) * A2;
        Vector3 B2 = ((k3 - t1) / (k3 - k1)) * A2 + ((t1 - k1) / (k3 - k1)) * A3;

        float t2 = Mathf.Lerp(k1, k2, t);
        Vector3 C = ((k2 - t2) / (k2 - k1)) * B1 + ((t2 - k1) / (k2 - k1)) * B2;
        return C;
    }

    // --- Utilities ----------------------------------------------------

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
            if (profiles != null && profiles.Length == cameraPoints.Length)
                mouseLook.ApplyProfile(profiles[currentIndex]);
            else
                mouseLook.ApplyProfile(mouseLook.profile);

            mouseLook.SetInitialToCurrent();
            mouseLook.SyncToCameraRotationInstant();
            mouseLook.SetInfluenceInstant(1f);
        }
    }

    // Draw gizmos for custom paths and active path
    void OnDrawGizmosSelected()
    {
        // draw configured custom paths
        if (customPaths != null)
        {
            for (int i = 0; i < customPaths.Length; i++)
            {
                var pd = customPaths[i];
                if (pd == null) continue;
                // try to fetch start/end positions
                Vector3 start = (HasCameraPoints() && pd.fromIndex >= 0 && pd.fromIndex < cameraPoints.Length && cameraPoints[pd.fromIndex] != null) ? cameraPoints[pd.fromIndex].position : Vector3.zero;
                Vector3 end = (HasCameraPoints() && pd.toIndex >= 0 && pd.toIndex < cameraPoints.Length && cameraPoints[pd.toIndex] != null) ? cameraPoints[pd.toIndex].position : Vector3.zero;
                if (start == Vector3.zero && end == Vector3.zero) continue;

                // gather points
                List<Vector3> pts = new List<Vector3>();
                pts.Add(start);
                if (pd.controlPoints != null && pd.controlPoints.Length > 0)
                {
                    foreach (var ct in pd.controlPoints) if (ct != null) pts.Add(ct.position);
                }
                pts.Add(end);

                // sample curve and draw
                Gizmos.color = pd.gizmoColor;
                Vector3 prev = pts[0];
                int steps = Mathf.Max(8, activeSampleResolution);
                for (int s = 1; s <= steps; s++)
                {
                    float u = s / (float)steps;
                    Vector3 p = SamplePathPosition(pts.ToArray(), u);
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
                // draw control point spheres
                Gizmos.color = Color.yellow;
                for (int k = 0; k < pts.Count; k++)
                {
                    Gizmos.DrawWireSphere(pts[k], 0.08f);
                }
            }
        }

        // draw active path if transitioning and has activePathPoints
        if (isTransitioning && activePathPoints != null && activePathPoints.Length > 1)
        {
            Gizmos.color = Color.cyan;
            Vector3 prev = activePathPoints[0];
            int steps = Mathf.Max(16, activeSampleResolution);
            for (int s = 1; s <= steps; s++)
            {
                float u = s / (float)steps;
                Vector3 p = SamplePathPosition(activePathPoints, u);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}

