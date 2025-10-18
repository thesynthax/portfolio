using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Highlightable: highlights renderers on the object. If no renderers are found,
/// optionally create edge outlines from BoxColliders under this object (useful for empty GameObjects).
/// Edge outlines are implemented with LineRenderers for crisp edge-only visuals.
/// </summary>
[DisallowMultipleComponent]
public class Highlightable : MonoBehaviour
{
    [Header("Outline settings")]
    public Color outlineColor = Color.white;
    [Range(0f, 0.3f)] public float baseThickness = 0.02f; // smaller base for edge lines
    [Tooltip("How much the thickness pulses (added to base thickness)")]
    public float pulseAmount = 0.01f;
    [Tooltip("Pulse speed")]
    public float pulseSpeed = 2.0f;

    [Tooltip("Optional explicit renderers (leave empty to auto-collect from children)")]
    public Renderer[] explicitRenderers;

    [Tooltip("If true and no renderers are present, attempt to create outlines from BoxColliders under this object.")]
    public bool outlineCollidersIfNoRenderers = true;

    // runtime
    List<GameObject> outlineObjects = new List<GameObject>();
    Material lineMaterial;            // material for LineRenderers
    Material meshOutlineMaterial;     // material for mesh-based renderer outlines (kept for renderer case)
    bool isHighlighted = false;

    float pulseTime = 0f;

    void Awake()
    {
        // create a simple unlit color material for lines
        Shader s = Shader.Find("Unlit/Color");
        if (s == null)
        {
            Debug.LogWarning("[Highlightable] Unlit/Color shader not found; falling back to default shader.");
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        else
        {
            lineMaterial = new Material(s);
        }
        lineMaterial.hideFlags = HideFlags.DontSave;
        lineMaterial.SetColor("_Color", outlineColor);

        // create a mesh-based outline material for renderer outlines using existing shader if available
        var outlineShader = Shader.Find("Custom/OutlineUnlit");
        if (outlineShader != null)
        {
            meshOutlineMaterial = new Material(outlineShader) { hideFlags = HideFlags.DontSave };
            meshOutlineMaterial.SetColor("_Color", outlineColor);
            meshOutlineMaterial.SetFloat("_Thickness", baseThickness * 2f); // scale for mesh
        }
        else
        {
            meshOutlineMaterial = null; // renderer-based outlines will not work without shader
        }
    }

    void OnDestroy()
    {
        ClearOutlines();
        if (lineMaterial != null) DestroyImmediate(lineMaterial);
        if (meshOutlineMaterial != null) DestroyImmediate(meshOutlineMaterial);
    }

    public void SetHighlighted(bool highlight)
    {
        if (highlight == isHighlighted) return;
        isHighlighted = highlight;
        if (isHighlighted) CreateOutlines();
        else ClearOutlines();
    }

    void Update()
    {
        if (!isHighlighted) return;

        // pulse animation: increase/decrease thickness
        pulseTime += Time.deltaTime * pulseSpeed;
        float p = (Mathf.Sin(pulseTime) * 0.5f + 0.5f); // 0..1
        float thickness = baseThickness + p * pulseAmount;

        // update line renderers thickness
        foreach (var go in outlineObjects)
        {
            if (go == null) continue;
            var lr = go.GetComponent<LineRenderer>();
            if (lr != null)
            {
                lr.widthMultiplier = thickness;
            }
            else
            {
                // if it's a mesh-based outline (renderer case), animate its material thickness if possible
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Thickness"))
                {
                    mr.sharedMaterial.SetFloat("_Thickness", thickness * 4f);
                }
            }
        }
    }

    void CreateOutlines()
    {
        if (outlineObjects.Count > 0) return;

        // First try explicit renderers or all child renderers
        Renderer[] renderers = (explicitRenderers != null && explicitRenderers.Length > 0)
            ? explicitRenderers
            : GetComponentsInChildren<Renderer>(true);

        bool createdAny = false;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (r is ParticleSystemRenderer) continue;

            Mesh copyMesh = null;

            // MeshRenderer + MeshFilter
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                copyMesh = mf.sharedMesh;
            }
            // Skinned
            var sk = r as SkinnedMeshRenderer;
            if (sk != null)
            {
                copyMesh = new Mesh();
                sk.BakeMesh(copyMesh);
            }

            if (copyMesh == null) continue;

            // create a mesh outline child as before (keeps previous behavior)
            GameObject go = new GameObject(r.gameObject.name + "_outline");
            go.transform.SetParent(r.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var newMF = go.AddComponent<MeshFilter>();
            newMF.mesh = copyMesh;

            var newMR = go.AddComponent<MeshRenderer>();
            if (meshOutlineMaterial != null) newMR.sharedMaterial = meshOutlineMaterial;
            else
            {
                // fallback: use simple unlit color material on mesh to give silhouette (less pretty)
                var fallback = new Material(Shader.Find("Unlit/Texture")) { hideFlags = HideFlags.DontSave };
                fallback.SetColor("_Color", outlineColor);
                newMR.sharedMaterial = fallback;
            }

            outlineObjects.Add(go);
            createdAny = true;
        }

        // If no renderers yielded outlines and the fallback flag is set, generate edge outlines from BoxColliders
        if (!createdAny && outlineCollidersIfNoRenderers)
        {
            var colliders = GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                if (col == null) continue;
                var b = col as BoxCollider;
                if (b != null)
                {
                    CreateEdgeOutlineForBoxCollider(b);
                    createdAny = true;
                }
            }
        }
    }

    void CreateEdgeOutlineForBoxCollider(BoxCollider box)
    {
        // parent to collider transform and offset to center
        GameObject parent = new GameObject(box.gameObject.name + "_edge_outline");
        parent.transform.SetParent(box.transform, false);
        parent.transform.localPosition = box.center;
        parent.transform.localRotation = Quaternion.identity;
        parent.transform.localScale = Vector3.one;

        // prepare material (double-sided) - we create per-Highlightable and reuse for all edges
        if (lineMaterial == null)
        {
            Shader ds = Shader.Find("Custom/UnlitColor_DoubleSided");
            if (ds != null) lineMaterial = new Material(ds) { hideFlags = HideFlags.DontSave };
            else
            {
                // fallback
                lineMaterial = new Material(Shader.Find("Unlit/Color")) { hideFlags = HideFlags.DontSave };
            }
            lineMaterial.SetColor("_Color", outlineColor);
        }

        Vector3 half = box.size * 0.5f;
        Vector3[] corners = new Vector3[8]
        {
            new Vector3(-half.x, -half.y, -half.z),
            new Vector3( half.x, -half.y, -half.z),
            new Vector3( half.x,  half.y, -half.z),
            new Vector3(-half.x,  half.y, -half.z),

            new Vector3(-half.x, -half.y,  half.z),
            new Vector3( half.x, -half.y,  half.z),
            new Vector3( half.x,  half.y,  half.z),
            new Vector3(-half.x,  half.y,  half.z)
        };

        // Create horizontal loop for front/back rectangle + connect loops into one line (8 points around perimeter)
        // We'll draw two loops (back rectangle and front rectangle) and then vertical connectors separately.
        // First: back rectangle (0-1-2-3) then front rect (4-5-6-7)
        // We'll create one LineRenderer that draws the back loop then the front loop in sequence, but to avoid weird diagonal lines,
        // we create 2 LineRenderers: one for each rectangle, and one for vertical connectors.

        // Rectangle back (0-1-2-3)
        var rectBack = new GameObject("rect_back"); rectBack.transform.SetParent(parent.transform, false);
        var lrBack = rectBack.AddComponent<LineRenderer>();
        lrBack.useWorldSpace = false;
        lrBack.material = lineMaterial;
        lrBack.positionCount = 5;
        lrBack.loop = false;
        lrBack.numCapVertices = 4;
        lrBack.numCornerVertices = 4;
        lrBack.alignment = LineAlignment.View;
        lrBack.textureMode = LineTextureMode.Stretch;
        lrBack.widthMultiplier = baseThickness;
        lrBack.SetPosition(0, corners[0]);
        lrBack.SetPosition(1, corners[1]);
        lrBack.SetPosition(2, corners[2]);
        lrBack.SetPosition(3, corners[3]);
        lrBack.SetPosition(4, corners[0]); // close

        outlineObjects.Add(rectBack);

        // Rectangle front (4-5-6-7)
        var rectFront = new GameObject("rect_front"); rectFront.transform.SetParent(parent.transform, false);
        var lrFront = rectFront.AddComponent<LineRenderer>();
        lrFront.useWorldSpace = false;
        lrFront.material = lineMaterial;
        lrFront.positionCount = 5;
        lrFront.loop = false;
        lrFront.numCapVertices = 4;
        lrFront.numCornerVertices = 4;
        lrFront.alignment = LineAlignment.View;
        lrFront.textureMode = LineTextureMode.Stretch;
        lrFront.widthMultiplier = baseThickness;
        lrFront.SetPosition(0, corners[4]);
        lrFront.SetPosition(1, corners[5]);
        lrFront.SetPosition(2, corners[6]);
        lrFront.SetPosition(3, corners[7]);
        lrFront.SetPosition(4, corners[4]); // close

        outlineObjects.Add(rectFront);

        // Vertical connectors (0-4,1-5,2-6,3-7) — we pack them into a single LineRenderer as pairs
        var verts = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            verts.Add(corners[i]);
            verts.Add(corners[i + 4]);
        }

        var vertsGO = new GameObject("verticals"); vertsGO.transform.SetParent(parent.transform, false);
        var lrVert = vertsGO.AddComponent<LineRenderer>();
        lrVert.useWorldSpace = false;
        lrVert.material = lineMaterial;
        lrVert.positionCount = verts.Count;
        lrVert.loop = false;
        lrVert.numCapVertices = 4;
        lrVert.numCornerVertices = 4;
        lrVert.alignment = LineAlignment.View;
        lrVert.textureMode = LineTextureMode.Stretch;
        lrVert.widthMultiplier = baseThickness;
        for (int i = 0; i < verts.Count; i++) lrVert.SetPosition(i, verts[i]);

        outlineObjects.Add(vertsGO);
    }

    void ClearOutlines()
    {
        if (outlineObjects == null) return;
        foreach (var go in outlineObjects)
        {
            if (go == null) continue;
            // cleanup any generated assets if needed
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        outlineObjects.Clear();
    }
}

