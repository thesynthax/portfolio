using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Minimap navigator: spawns buttons horizontally near the top of the container,
/// and requests a DIRECT move to the target index when a button is clicked.
/// </summary>
public class MinimapNavigator : MonoBehaviour
{
    [Header("References")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover;
    public RectTransform buttonContainer; // UI panel where buttons will be instantiated
    public Button buttonPrefab;
    public Color idleColor = Color.gray;
    public Color activeColor = Color.white;
    public Color visitedColor = new Color(0.8f, 0.8f, 0.8f);

    [Header("Layout")]
    [Tooltip("Pixels from the top edge of the container")]
    public float paddingTop = 12f;
    [Tooltip("Spacing between buttons in pixels")]
    public float spacing = 8f;
    [Tooltip("Optional maximum horizontal width to allow wrapping/clamping. 0 = no clamp")]
    public float maxAllowedWidth = 0f;

    [Header("Step timing (unused for direct jumps)")]
    public float stepDelay = 0.12f;

    List<Button> buttons = new List<Button>();
    int currentIndex = 0;
    bool hasReachedLastIdx = false;
    Vector3 buttonInitPos, buttonFinalPos;
    public float buttonBringSpeed = 10f;
    public float buttonInitY = 80f;

    void Start()
    {
        if (cameraMover == null || buttonContainer == null || buttonPrefab == null) return;
        buttonContainer.gameObject.SetActive(false);
        PopulateButtons();
        currentIndex = 0;
        UpdateButtonStates();
        cameraMover.OnTransitionComplete += OnMoverComplete;
        buttonFinalPos = buttonContainer.position;
        buttonContainer.position += buttonInitY * Vector3.up;
        buttonInitPos = buttonContainer.position;
    }

    void Update()
    {
        if (currentIndex == 5) hasReachedLastIdx = true;

        if (hasReachedLastIdx) BringButtons();

        foreach (Button b in buttons) {
            b.transform.GetChild(1).gameObject.SetActive(b.GetComponent<HoverCheck>().isHovering);
        }

    }

    void BringButtons() {
        buttonContainer.gameObject.SetActive(true);
        if ((buttonContainer.position - buttonFinalPos).sqrMagnitude > 0.05f)
            buttonContainer.position = Vector3.Lerp(buttonContainer.position, buttonFinalPos, buttonBringSpeed * Time.deltaTime);
    }

    void OnDestroy()
    {
        if (cameraMover != null) cameraMover.OnTransitionComplete -= OnMoverComplete;
    }

    void PopulateButtons()
    {
        ClearButtons();
        if (cameraMover.cameraPoints == null) return;

        int n = cameraMover.cameraPoints.Length;
        if (n == 0) return;

        // Custom labels for each camera index
        string[] labels = new string[]
        {
            "Creative",
            "Technical Intro",
            "Skills",
            "Experience",
            "Projects",
            "Fun Facts"
        };

        // read prefab size (local) via RectTransform
        var prefabRT = buttonPrefab.GetComponent<RectTransform>();
        Vector2 btnSize = (prefabRT != null) ? prefabRT.sizeDelta : new Vector2(80f, 32f);

        float totalWidth = n * btnSize.x + Mathf.Max(0, n - 1) * spacing;

        if (maxAllowedWidth > 0f && totalWidth > maxAllowedWidth)
        {
            float avail = Mathf.Max(1f, maxAllowedWidth - n * btnSize.x);
            spacing = avail / Mathf.Max(1, n - 1);
            totalWidth = n * btnSize.x + Mathf.Max(0, n - 1) * spacing;
        }

        Rect containerRect = buttonContainer.rect;
        float startX = -totalWidth * 0.5f + btnSize.x * 0.5f;

        for (int i = 0; i < n; i++)
        {
            Button b = Instantiate(buttonPrefab, buttonContainer);
            int idx = i;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => OnButtonClicked(idx));

            // Set button label
            var txt = b.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null)
            {
                if (i < labels.Length)
                    txt.text = labels[i];
                else
                    txt.text = $"Point {i}";
                txt.textWrappingMode = TextWrappingModes.Normal;
            }

            // Position the button horizontally near top
            var rt = b.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);

                float x = startX + i * (btnSize.x + spacing);
                float y = -paddingTop;
                rt.anchoredPosition = new Vector2(x, y);
                rt.sizeDelta = btnSize;
            }

            buttons.Add(b);
        }
    }

    void ClearButtons()
    {
        for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            Destroy(buttonContainer.GetChild(i).gameObject);
        buttons.Clear();
    }

    void OnButtonClicked(int target)
    {
        if (cameraMover == null) return;
        // Directly request move to target index
        cameraMover.StartTransitionToIndex(target, true);
        // UI will be updated when OnMoverComplete fires
    }

    void OnMoverComplete(int idx)
    {
        currentIndex = Mathf.Clamp(idx, 0, (cameraMover.cameraPoints != null ? cameraMover.cameraPoints.Length - 1 : 0));
        UpdateButtonStates();
    }

    void UpdateButtonStates()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            var img = buttons[i].GetComponent<Image>();
            var txt = img.GetComponentInChildren<TextMeshProUGUI>();
            if (img == null) continue;
            if (i == currentIndex) { img.color = activeColor; txt.color = activeColor; }
            //else if (i < currentIndex) { img.color = visitedColor; txt.color = visitedColor; }
            else { img.color = idleColor; txt.color = idleColor; }
        }
    }
}

