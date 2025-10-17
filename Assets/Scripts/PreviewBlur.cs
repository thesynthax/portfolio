using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PreviewBlur : MonoBehaviour
{
    public enum UpdateMode { Continuous, Interval, Once }
    public enum CropAnchor { Center, Left, Right }

    [Header("Source camera that renders the scene from a preview point")]
    public Camera sourceCamera;

    [Header("UI RawImage to show the blurred preview")]
    public RawImage targetRawImage;

    [Header("Downsample (power of two). 1 = half, 2 = quarter, 3 = 1/8")]
    [Range(1,4)]
    public int downsample = 2;

    [Header("Blur")]
    [Tooltip("Number of separable blur iterations (each does horizontal+vertical).")]
    public int iterations = 2;
    [Tooltip("Radius parameter passed to shader")]
    public float radius = 3f;

    [Header("Update mode")]
    public UpdateMode updateMode = UpdateMode.Once;
    [Tooltip("Used when updateMode == Interval; seconds between updates.")]
    public float updateInterval = 0.25f;

    [Header("Crop preview (0..1) - fraction of horizontal portion to show, e.g. 0.5 = half")]
    [Range(0.1f, 1f)]
    public float cropFraction = 0.5f;

    [Header("Crop anchor")]
    public CropAnchor cropAnchor = CropAnchor.Center;

    // internal
    Material blurMat;
    RenderTexture rtSource;
    RenderTexture rtTempA;
    RenderTexture rtTempB;

    float intervalTimer = 0f;
    bool hasRenderedOnce = false;
    bool resourcesCreated = false;

    void OnEnable()
    {
        hasRenderedOnce = false;
        intervalTimer = 0f;
        if (Application.isPlaying && sourceCamera != null && targetRawImage != null)
        {
            CreateResourcesIfNeeded();
            if (updateMode == UpdateMode.Once)
                RenderOnceAndStop();
        }
    }

    void OnDisable()
    {
        ReleaseRTs();
        ReleaseMaterial();
    }

    void Update()
    {
        if (!Application.isPlaying) return; // do nothing in edit mode

        if (sourceCamera == null || targetRawImage == null) return;

        CreateResourcesIfNeeded();

        if (updateMode == UpdateMode.Continuous)
        {
            RenderAndBlur();
        }
        else if (updateMode == UpdateMode.Interval)
        {
            intervalTimer += Time.deltaTime;
            if (intervalTimer >= updateInterval)
            {
                intervalTimer = 0f;
                RenderAndBlur();
            }
        }
        // Once mode: nothing here
    }

    // Public: force a render now (safe to call from editor or play)
    public void EnsureRenderNow()
    {
        if (sourceCamera == null || targetRawImage == null) return;

#if UNITY_EDITOR
        bool editorTemporary = !Application.isPlaying;
#else
        bool editorTemporary = false;
#endif

        if (editorTemporary)
        {
#if UNITY_EDITOR
            EnsureMaterialEditorSafe();
            SetupRenderTexturesEditorSafe();
            RenderAndBlurEditorSafe();
            ReleaseRTsEditorSafe();
            //ReleaseMaterialEditorSafe();
#endif
            return;
        }

        CreateResourcesIfNeeded();
        if (updateMode == UpdateMode.Once && hasRenderedOnce) return;
        RenderAndBlur();
        if (updateMode == UpdateMode.Once)
        {
            hasRenderedOnce = true;
            if (sourceCamera != null) sourceCamera.enabled = false;
        }
    }

    void RenderOnceAndStop()
    {
        CreateResourcesIfNeeded();
        RenderAndBlur();
        hasRenderedOnce = true;
        if (sourceCamera != null) sourceCamera.enabled = false;
    }

    void RenderAndBlur()
    {
        if (blurMat == null || rtSource == null || rtTempA == null || rtTempB == null) return;

        // Render camera to source RT
        sourceCamera.targetTexture = rtSource;
        sourceCamera.Render();
        sourceCamera.targetTexture = null;

        // Downsample
        Graphics.Blit(rtSource, rtTempA);

        // Separable blur iterations
        for (int i = 0; i < iterations; i++)
        {
            // horizontal pass
            blurMat.SetVector("_TexelSize", new Vector4(1.0f / rtTempA.width, 0f, 0f, 0f));
            blurMat.SetFloat("_Radius", radius);
            Graphics.Blit(rtTempA, rtTempB, blurMat);

            // vertical pass
            blurMat.SetVector("_TexelSize", new Vector4(0f, 1.0f / rtTempB.height, 0f, 0f));
            blurMat.SetFloat("_Radius", radius);
            Graphics.Blit(rtTempB, rtTempA, blurMat);
        }

        // assign to RawImage
        targetRawImage.texture = rtTempA;

        // compute uvRect according to cropFraction and cropAnchor
        float crop = Mathf.Clamp01(cropFraction);
        float uWidth = Mathf.Clamp(crop, 0.01f, 1f);
        float uX = 0.5f - (uWidth * 0.5f); // default center

        if (cropAnchor == CropAnchor.Left)
        {
            // show left portion: start at 0
            uX = 0f;
        }
        else if (cropAnchor == CropAnchor.Right)
        {
            // show right portion: align to right
            uX = 1f - uWidth;
        }
        // else center uses computed uX

        Rect uv = new Rect(uX, 0f, uWidth, 1f);
        targetRawImage.uvRect = uv;
    }

    void CreateResourcesIfNeeded()
    {
        if (resourcesCreated) return;
        EnsureMaterial();
        SetupRenderTextures();
        resourcesCreated = true;
    }

    void EnsureMaterial()
    {
        if (blurMat != null) return;
        var shader = Shader.Find("UI/SeparableBlur");
        if (shader == null)
        {
            Debug.LogError("[PreviewBlur] Shader UI/SeparableBlur not found.");
            return;
        }
        blurMat = new Material(shader) { hideFlags = HideFlags.DontSave };
    }

    void SetupRenderTextures()
    {
        if (sourceCamera == null) return;
        ReleaseRTs();

        int sw = Mathf.Max(16, sourceCamera.pixelWidth);
        int sh = Mathf.Max(16, sourceCamera.pixelHeight);
        rtSource = new RenderTexture(sw, sh, 16, RenderTextureFormat.ARGB32) { name = "PreviewSource_RT", hideFlags = HideFlags.DontSave };
        int w = Mathf.Max(16, sw >> downsample);
        int h = Mathf.Max(16, sh >> downsample);
        rtTempA = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "PreviewBlurA_RT", hideFlags = HideFlags.DontSave };
        rtTempB = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "PreviewBlurB_RT", hideFlags = HideFlags.DontSave };

        rtSource.Create();
        rtTempA.Create();
        rtTempB.Create();
    }

    void ReleaseRTs()
    {
        if (rtSource != null) { rtSource.Release(); DestroyImmediate(rtSource); rtSource = null; }
        if (rtTempA != null) { rtTempA.Release(); DestroyImmediate(rtTempA); rtTempA = null; }
        if (rtTempB != null) { rtTempB.Release(); DestroyImmediate(rtTempB); rtTempB = null; }
        resourcesCreated = false;
    }

    void ReleaseMaterial()
    {
        if (blurMat != null) { DestroyImmediate(blurMat); blurMat = null; }
    }

#if UNITY_EDITOR
    // Editor-only helpers that do not persist resources (safer)
    void EnsureMaterialEditorSafe()
    {
        if (blurMat != null) return;
        var shader = Shader.Find("UI/SeparableBlur");
        if (shader == null)
        {
            Debug.LogError("[PreviewBlur] Shader UI/SeparableBlur not found.");
            return;
        }
        blurMat = new Material(shader);
    }

    void SetupRenderTexturesEditorSafe()
    {
        if (sourceCamera == null) return;
        int sw = Mathf.Max(16, sourceCamera.pixelWidth);
        int sh = Mathf.Max(16, sourceCamera.pixelHeight);
        rtSource = new RenderTexture(sw, sh, 16, RenderTextureFormat.ARGB32);
        int w = Mathf.Max(16, sw >> downsample);
        int h = Mathf.Max(16, sh >> downsample);
        rtTempA = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        rtTempB = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
    }

    void RenderAndBlurEditorSafe()
    {
        if (blurMat == null || rtSource == null || rtTempA == null || rtTempB == null) return;
        sourceCamera.targetTexture = rtSource;
        sourceCamera.Render();
        sourceCamera.targetTexture = null;

        Graphics.Blit(rtSource, rtTempA);

        for (int i = 0; i < iterations; i++)
        {
            blurMat.SetVector("_TexelSize", new Vector4(1.0f / rtTempA.width, 0f, 0f, 0f));
            blurMat.SetFloat("_Radius", radius);
            Graphics.Blit(rtTempA, rtTempB, blurMat);

            blurMat.SetVector("_TexelSize", new Vector4(0f, 1.0f / rtTempB.height, 0f, 0f));
            blurMat.SetFloat("_Radius", radius);
            Graphics.Blit(rtTempB, rtTempA, blurMat);
        }

        targetRawImage.texture = rtTempA;

        // compute uvRect for editor preview
        float crop = Mathf.Clamp01(cropFraction);
        float uWidth = Mathf.Clamp(crop, 0.01f, 1f);
        float uX = 0.5f - (uWidth * 0.5f);
        if (cropAnchor == CropAnchor.Left) uX = 0f;
        else if (cropAnchor == CropAnchor.Right) uX = 1f - uWidth;
        Rect uv = new Rect(uX, 0f, uWidth, 1f);
        targetRawImage.uvRect = uv;
    }

    void ReleaseRTsEditorSafe()
    {
        if (rtSource != null) { rtSource.Release(); DestroyImmediate(rtSource); rtSource = null; }
        if (rtTempA != null) { rtTempA.Release(); DestroyImmediate(rtTempA); rtTempA = null; }
        if (rtTempB != null) { rtTempB.Release(); DestroyImmediate(rtTempB); rtTempB = null; }
        if (blurMat != null) { DestroyImmediate(blurMat); blurMat = null; }
    }
#endif
}

