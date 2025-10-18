using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class MinimapNavigator : MonoBehaviour
{
    [Header("References")]
    public CameraMoverNoCoroutines_WithMouseLookIntegration cameraMover;
    public RectTransform buttonContainer; // UI panel where buttons will be instantiated
    public Button buttonPrefab;
    public Color idleColor = Color.white;
    public Color activeColor = Color.green;
    public Color visitedColor = new Color(0.8f, 0.8f, 0.8f);

    [Header("Step timing")]
    public float stepDelay = 0.12f;

    List<Button> buttons = new List<Button>();
    int currentIndex = 0;
    Coroutine stepRoutine = null;

    void Start()
    {
        if (cameraMover == null || buttonContainer == null || buttonPrefab == null) return;
        PopulateButtons();
        currentIndex = 0;
        UpdateButtonStates();
        cameraMover.OnTransitionComplete += OnMoverComplete;
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
        for (int i = 0; i < n; i++)
        {
            Button b = Instantiate(buttonPrefab, buttonContainer);
            int idx = i;
            b.onClick.AddListener(() => OnButtonClicked(idx));
            var txt = b.GetComponentInChildren<Text>();
            if (txt != null) txt.text = (idx).ToString();
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
        if (stepRoutine != null) StopCoroutine(stepRoutine);
        stepRoutine = StartCoroutine(StepToTargetRoutine(target));
    }

    IEnumerator StepToTargetRoutine(int target)
    {
        // If same index, do nothing (maybe snap)
        if (target == currentIndex) yield break;

        // Determine direction
        int dir = (target > currentIndex) ? 1 : -1;
        while (currentIndex != target)
        {
            int next = currentIndex + dir;
            // call StartTransitionToIndex and wait until mover completes
            cameraMover.StartTransitionToIndex(next);
            // wait until mover finished (we listen to OnMoverComplete to update currentIndex)
            bool completed = false;
            System.Action<int> onComplete = (idx) => { completed = true; };
            cameraMover.OnTransitionComplete += onComplete;
            // safety timeout
            float timer = 0f;
            while (!completed && timer < 5f)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            cameraMover.OnTransitionComplete -= onComplete;

            // currentIndex will be updated by OnMoverComplete
            // but in case it wasn't, force set
            if (!completed) currentIndex = Mathf.Clamp(next, 0, cameraMover.cameraPoints.Length - 1);
            UpdateButtonStates();
            yield return new WaitForSeconds(stepDelay);
        }
        stepRoutine = null;
    }

    void OnMoverComplete(int idx)
    {
        currentIndex = idx;
        UpdateButtonStates();
    }

    void UpdateButtonStates()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            var img = buttons[i].GetComponent<Image>();
            if (img == null) continue;
            if (i == currentIndex) img.color = activeColor;
            else if (i < currentIndex) img.color = visitedColor;
            else img.color = idleColor;
        }
    }
}

