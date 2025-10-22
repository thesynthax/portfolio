using UnityEngine;

/// <summary>
/// Attach to any object that should be highlightable for a specific camera point index.
/// Example: add to the board/canvas GameObject and set index = 3 so it only highlights when cameraMover.currentIndex == 3.
/// </summary>
[DisallowMultipleComponent]
public class HighlightPointIndex : MonoBehaviour
{
    [Tooltip("The camera point index for which this object should be considered highlightable.")]
    public int index = 0;
}

