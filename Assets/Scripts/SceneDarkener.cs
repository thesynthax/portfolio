using UnityEngine;

/// <summary>
/// SceneDarkener – darkens only the background (world-space, not UI).
/// Creates a transparent black quad in front of the camera that fades in/out,
/// without affecting nearby objects like popouts.
/// </summary>
[DisallowMultipleComponent]
public class SceneDarkener : MonoBehaviour
{
    [Tooltip("Camera to attach the darkener to. If null, uses Camera.main.")]
    public Camera sceneCamera;

    [Tooltip("Color of darkness (alpha is controlled at runtime).")]
    public Color darkColor = new Color(0f, 0f, 0f, 0.7f);

    [Tooltip("Distance from camera near plane.")]
    public float distanceFromCamera = 0.05f;

    [Tooltip("Fade animation curve.")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("Default fade duration.")]
    public float defaultDuration = 0.4f;

    MeshRenderer quadRenderer;
    public Material darkMat;

    float currentAlpha = 0f;
    float targetAlpha = 0f;
    float t = 1f;
    float duration = 0.4f;
    bool animating = false;

    void Awake()
    {
        if (sceneCamera == null) sceneCamera = Camera.main;
        CreateQuad();
    }

    void CreateQuad()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "SceneDarkener_Quad";
        quad.transform.SetParent(sceneCamera.transform, false);
        quad.transform.localPosition = new Vector3(0, 0, distanceFromCamera);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(2f, 2f, 1f); // covers view; adjusted in update

        /*Shader shader = Shader.Find("Unlit/Color");
        darkMat = new Material(shader);*/
        darkMat.color = new Color(darkColor.r, darkColor.g, darkColor.b, 0f);
        quadRenderer = quad.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = darkMat;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
    }

    void Update()
    {
        if (!sceneCamera) return;

        // ensure the quad matches the view
        float height = 2f * Mathf.Tan(sceneCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * (distanceFromCamera + 0.1f);
        float width = height * sceneCamera.aspect;
        quadRenderer.transform.localScale = new Vector3(width, height, 1f);

        if (animating)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, duration);
            float u = Mathf.Clamp01(t);
            float e = ease.Evaluate(u);
            currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, e);
            darkMat.color = new Color(darkColor.r, darkColor.g, darkColor.b, currentAlpha);

            if (u >= 1f - 1e-6f)
                animating = false;
        }
    }

    public void Show(float dur = -1f)
    {
        duration = dur > 0f ? dur : defaultDuration;
        t = 0f;
        targetAlpha = darkColor.a;
        animating = true;
    }

    public void Hide(float dur = -1f)
    {
        duration = dur > 0f ? dur : defaultDuration;
        t = 0f;
        targetAlpha = 0f;
        animating = true;
    }
}

