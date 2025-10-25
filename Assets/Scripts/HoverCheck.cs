using UnityEngine;
using UnityEngine.EventSystems;

public class HoverCheck : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public bool isHovering { get; private set; } = false;

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
    }
}
