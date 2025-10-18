using UnityEngine;

[DisallowMultipleComponent]
public class CameraPositionDrivenMicroLook : MonoBehaviour
{
    public enum RotationMode
    {
        Legacy,   // original math (may produce roll in extreme pitches)
        WorldUp   // yaw around world up, pitch around the right after yaw, final LookRotation with up vector (no roll)
    }

    [System.Serializable]
    public struct Profile
    {
        public float maxYaw;
        public float maxPitch;
        public float positionSensitivity;
        public float centerDeadzone;
        public float maxOffsetX;
        public float maxOffsetY;
        public float maxOffsetZ;
        public float rotationSmoothTime;
        public float positionSmoothTime;
        public bool invertY;
    }

    [Header("Default Profile (used unless overridden)")]
    public Profile profile = new Profile
    {
        maxYaw = 8f,
        maxPitch = 6f,
        positionSensitivity = 1f,
        centerDeadzone = 0.06f,
        maxOffsetX = 0.04f,
        maxOffsetY = 0.02f,
        maxOffsetZ = -0.015f,
        rotationSmoothTime = 0.08f,
        positionSmoothTime = 0.08f,
        invertY = false
    };

    [Header("Runtime")]
    [Range(0f, 1f)] public float influence = 1f; // 0 = no effect, 1 = full effect
    [Tooltip("When blending influence, how long (seconds) it takes.")]
    public float influenceBlendTime = 0.35f;

    [Header("Rotation behavior")]
    public RotationMode rotationMode = RotationMode.WorldUp;
    [Tooltip("If using WorldUp mode, this is the up vector used for yaw/pitch composition.")]
    public Vector3 upVector = Vector3.up;

    // internals
    Vector3 initialLocalPos;
    Quaternion initialLocalRot;

    // target and current angles (relative to initialLocalRot)
    float targetYaw = 0f;   // degrees (positive = turn right)
    float targetPitch = 0f; // degrees (positive = look up)
    float currentYaw = 0f;
    float currentPitch = 0f;

    // velocities for SmoothDamp
    float yawVel = 0f;
    float pitchVel = 0f;
    Vector3 posVel = Vector3.zero;

    // internal influence blend state
    float influenceVel = 0f;
    float targetInfluence = 1f;

    // active profile (can be updated at runtime)
    Profile activeProfile;

    void Awake()
    {
        initialLocalPos = transform.localPosition;
        initialLocalRot = transform.localRotation;
        activeProfile = profile;
    }

    void OnEnable()
    {
        // When enabled ensure current angles match initial rotation (avoid snap)
        // NOTE: with WorldUp mode it's safer to treat the current transform as neutral
        SyncToCameraRotationInstant();
    }

    void Update()
    {
        // Blend influence toward targetInfluence smoothly
        influence = Mathf.SmoothDamp(influence, targetInfluence, ref influenceVel, Mathf.Max(0.001f, influenceBlendTime), Mathf.Infinity, Time.deltaTime);

        // If influence is extremely small, we can skip mouse processing (saves work and avoids tiny noise)
        if (influence > 0.001f)
        {
            // Read mouse position (screen-based mouse look)
            Vector2 mouse = Input.mousePosition;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            float nx = Mathf.Clamp((mouse.x - cx) / cx, -1f, 1f);
            float ny = Mathf.Clamp((mouse.y - cy) / cy, -1f, 1f);
            if (activeProfile.invertY) ny = -ny;

            float mag = new Vector2(nx, ny).magnitude;
            if (mag < Mathf.Clamp01(activeProfile.centerDeadzone))
            {
                nx = 0f; ny = 0f;
            }
            else if (activeProfile.centerDeadzone > 0f)
            {
                float scaled = (mag - activeProfile.centerDeadzone) / (1f - activeProfile.centerDeadzone);
                Vector2 dir = new Vector2(nx, ny).normalized;
                nx = dir.x * scaled;
                ny = dir.y * scaled;
            }

            // Apply sensitivity and set target angles (these are the desired full-effect angles when influence==1)
            targetYaw = nx * activeProfile.maxYaw * Mathf.Clamp01(activeProfile.positionSensitivity);
            targetPitch = -ny * activeProfile.maxPitch * Mathf.Clamp01(activeProfile.positionSensitivity);

            // Smooth current toward target using profile.rotationSmoothTime
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVel, activeProfile.rotationSmoothTime, Mathf.Infinity, Time.deltaTime);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, targetPitch, ref pitchVel, activeProfile.rotationSmoothTime, Mathf.Infinity, Time.deltaTime);

            // Compose rotation according to chosen mode
            Quaternion desiredRot;
            if (rotationMode == RotationMode.Legacy)
            {
                // legacy behavior: rotate relative to initialLocalRot using simple euler (keeps previous semantics)
                desiredRot = initialLocalRot * Quaternion.Euler(currentPitch, currentYaw, 0f);
            }
            else // WorldUp mode
            {
                // 1) start from the initial forward in world space
                Vector3 initialForwardWorld = transform.parent != null
                    ? transform.parent.TransformDirection(initialLocalRot * Vector3.forward)
                    : (initialLocalRot * Vector3.forward);

                // But simpler: compute world-space initial forward/up based on initialLocalRot using parent's transform
                Vector3 initForward = (transform.parent != null) ? transform.parent.rotation * (initialLocalRot * Vector3.forward) : (initialLocalRot * Vector3.forward);
                // Apply yaw around the global upVector
                Vector3 yawedForward = Quaternion.AngleAxis(currentYaw, upVector.normalized) * initForward;

                // Compute right axis after yaw (world-space)
                Vector3 rightAxis = Vector3.Cross(upVector.normalized, yawedForward).normalized;
                if (rightAxis.sqrMagnitude < 1e-6f)
                {
                    // edge case: forward aligns with up (very steep), fallback to parent's right or world right
                    rightAxis = transform.parent != null ? transform.parent.right : Vector3.right;
                }

                // Apply pitch around the right axis
                Vector3 pitchedForward = Quaternion.AngleAxis(currentPitch, rightAxis) * yawedForward;

                // Build rotation such that forward is pitchedForward and up is aligned to upVector (removes roll)
                desiredRot = Quaternion.LookRotation(pitchedForward.normalized, upVector.normalized);

                // Convert desiredRot into local-space relative to parent to respect initialLocalRot's parent
                if (transform.parent != null)
                {
                    // desiredRot is world-space, convert to local
                    desiredRot = Quaternion.Inverse(transform.parent.rotation) * desiredRot;
                }
            }

            // Lerp/slerp from current local rotation toward desired by influence
            transform.localRotation = Quaternion.Slerp(transform.localRotation, desiredRot, influence);

            // Positional parallax based on current angles, then blended by influence
            float xNorm = (Mathf.Abs(activeProfile.maxYaw) > 0.0001f) ? Mathf.Clamp(currentYaw / activeProfile.maxYaw, -1f, 1f) : 0f;
            float yNorm = (Mathf.Abs(activeProfile.maxPitch) > 0.0001f) ? Mathf.Clamp(-currentPitch / activeProfile.maxPitch, -1f, 1f) : 0f;

            Vector3 targetOffset = new Vector3(xNorm * activeProfile.maxOffsetX, yNorm * activeProfile.maxOffsetY, yNorm * activeProfile.maxOffsetZ);
            Vector3 desiredLocalPos = initialLocalPos + targetOffset;

            // We blend position by influence: we lerp the transform.localPosition toward desiredLocalPos by influence factor (smoothed)
            Vector3 blendedTargetPos = Vector3.Lerp(initialLocalPos, desiredLocalPos, influence);
            transform.localPosition = Vector3.SmoothDamp(transform.localPosition, blendedTargetPos, ref posVel, activeProfile.positionSmoothTime, Mathf.Infinity, Time.deltaTime);
        }
        else
        {
            // Influence nearly zero: still slowly relax velocities so when we resume it won't snap
            yawVel = Mathf.MoveTowards(yawVel, 0f, Time.deltaTime * 10f);
            pitchVel = Mathf.MoveTowards(pitchVel, 0f, Time.deltaTime * 10f);
            posVel = Vector3.MoveTowards(posVel, Vector3.zero, Time.deltaTime * 5f);
        }
    }

    // PUBLIC API ----------------------------------------------------------

    // Immediately set influence (instant)
    public void SetInfluenceInstant(float v)
    {
        influence = Mathf.Clamp01(v);
        targetInfluence = influence;
        influenceVel = 0f;
    }

    // Blend influence over configured influenceBlendTime toward 0 (out) or 1 (in)
    public void BlendOut(float blendTime = -1f)
    {
        if (blendTime > 0f) influenceBlendTime = blendTime;
        targetInfluence = 0f;
    }

    public void BlendIn(float blendTime = -1f)
    {
        if (blendTime > 0f) influenceBlendTime = blendTime;
        targetInfluence = 1f;
    }

    // Sync: for the new world-up style it's safer to treat current transform as neutral.
    // This avoids complex angle extraction that can be error prone.
    public void SyncToCameraRotationInstant()
    {
        // Make the mouse-look internally think it's at rest relative to its current rotation:
        // i.e., zero the internal angular offsets so blending won't jump.
        currentYaw = 0f;
        currentPitch = 0f;
        targetYaw = 0f;
        targetPitch = 0f;
        yawVel = pitchVel = 0f;
        influenceVel = 0f;
    }

    // Set the neutral local pose to current transform local pose
    public void SetInitialToCurrent()
    {
        initialLocalPos = transform.localPosition;
        initialLocalRot = transform.localRotation;

        // After changing the neutral, sync internal angle state to avoid jumps:
        SyncToCameraRotationInstant();
    }

    public void SetInitialFromTransform(Transform t)
    {
        if (t == null) return;

        // Set camera transform to the target's world pose and set it as neutral.
        transform.position = t.position;
        transform.rotation = t.rotation;
        SetInitialToCurrent();
    }

    // Convenience: set profile at runtime
    public void ApplyProfile(Profile p)
    {
        activeProfile = p;
        // clamp current angles to new caps to avoid sudden overshoot
        currentYaw = Mathf.Clamp(currentYaw, -Mathf.Abs(activeProfile.maxYaw), Mathf.Abs(activeProfile.maxYaw));
        currentPitch = Mathf.Clamp(currentPitch, -Mathf.Abs(activeProfile.maxPitch), Mathf.Abs(activeProfile.maxPitch));
    }

    // Helper
    static float NormalizeAngle(float a)
    {
        // Map 0..360 to -180..180
        if (a > 180f) a -= 360f;
        return a;
    }
}

