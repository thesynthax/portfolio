// Assets/Editor/CapturePreviewImages.cs
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class CapturePreviewImages : EditorWindow
{
    public Camera captureCamera; // camera to use (if null we'll create a temporary camera)
    public CameraMoverNoCoroutines_WithMouseLookIntegration mover; // assign your mover
    public int captureWidth = 1920;
    public int captureHeight = 1080;
    public int downsample = 2; // 1=half,2=quarter
    public int blurIterations = 2;
    public float blurRadius = 3f;
    public string outputFolder = "Assets/PreviewCaptures";

    [MenuItem("Tools/Preview Capture")]
    static void OpenWindow() => GetWindow<CapturePreviewImages>("Preview Capture");

    void OnGUI()
    {
        GUILayout.Label("Capture & Blur Previews", EditorStyles.boldLabel);
        mover = (CameraMoverNoCoroutines_WithMouseLookIntegration)EditorGUILayout.ObjectField("Camera Mover", mover, typeof(CameraMoverNoCoroutines_WithMouseLookIntegration), true);
        captureCamera = (Camera)EditorGUILayout.ObjectField("Capture Camera (optional)", captureCamera, typeof(Camera), true);
        captureWidth = EditorGUILayout.IntField("Width", captureWidth);
        captureHeight = EditorGUILayout.IntField("Height", captureHeight);
        downsample = EditorGUILayout.IntSlider("Downsample (power 2)", downsample, 1, 4);
        blurIterations = EditorGUILayout.IntSlider("Blur Iterations", blurIterations, 0, 4);
        blurRadius = EditorGUILayout.Slider("Blur Radius", blurRadius, 0f, 8f);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        if (GUILayout.Button("Capture Previews"))
        {
            if (mover == null || mover.cameraPoints == null || mover.cameraPoints.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Assign a CameraMover with cameraPoints populated.", "OK");
                return;
            }
            CaptureAllPreviews();
        }
    }

    void CaptureAllPreviews()
    {
        Directory.CreateDirectory(outputFolder);
        Shader blurShader = Shader.Find("UI/SeparableBlur");
        Material blurMat = null;
        if (blurIterations > 0)
        {
            if (blurShader == null)
            {
                Debug.LogError("Separable blur shader not found (UI/SeparableBlur). Either add it or set blurIterations=0.");
            }
            else
            {
                blurMat = new Material(blurShader);
            }
        }

        Camera cam = captureCamera;
        bool createdTempCam = false;
        if (cam == null)
        {
            // create a temporary camera
            GameObject go = new GameObject("PreviewCaptureCamera");
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            createdTempCam = true;
        }

        int count = mover.cameraPoints.Length;
        for (int i = 0; i < count; i++)
        {
            Transform pt = mover.cameraPoints[i];
            if (pt == null) continue;

            // position camera
            cam.transform.position = pt.position;
            cam.transform.rotation = pt.rotation;
            cam.orthographic = false;
            cam.fieldOfView = Camera.main ? Camera.main.fieldOfView : 60f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.cullingMask = Camera.main ? Camera.main.cullingMask : -1;

            // render to RT
            RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            // downsample to smaller RT to blur cheaply
            int w = Mathf.Max(16, captureWidth >> downsample);
            int h = Mathf.Max(16, captureHeight >> downsample);
            RenderTexture tmpA = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture tmpB = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, tmpA);

            // blur passes if shader available
            if (blurMat != null && blurIterations > 0)
            {
                for (int it = 0; it < blurIterations; it++)
                {
                    blurMat.SetFloat("_Radius", blurRadius);
                    blurMat.SetVector("_TexelSize", new Vector4(1.0f / tmpA.width, 0f, 0f, 0f));
                    Graphics.Blit(tmpA, tmpB, blurMat);
                    blurMat.SetVector("_TexelSize", new Vector4(0f, 1.0f / tmpB.height, 0f, 0f));
                    Graphics.Blit(tmpB, tmpA, blurMat);
                }
            }

            // read pixels from tmpA into Texture2D
            RenderTexture.active = tmpA;
            Texture2D tex = new Texture2D(tmpA.width, tmpA.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, tmpA.width, tmpA.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // encode PNG and save
            byte[] bytes = tex.EncodeToPNG();
            string path = Path.Combine(outputFolder, $"preview_{i}.png");
            File.WriteAllBytes(path, bytes);
            Debug.Log($"Saved preview {i} to {path}");

            // cleanup
            DestroyImmediate(tex);
            tmpA.Release(); DestroyImmediate(tmpA);
            tmpB.Release(); DestroyImmediate(tmpB);
            rt.Release(); DestroyImmediate(rt);
        }

        if (createdTempCam && cam != null) DestroyImmediate(cam.gameObject);
        if (blurMat != null) DestroyImmediate(blurMat);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Done", $"Captured {count} previews to {outputFolder}", "OK");
    }
}
#endif

