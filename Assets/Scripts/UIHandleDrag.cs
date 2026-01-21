using UnityEngine;
using UnityEngine.EventSystems;

public class UIHandleDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Camera cam;
    public System.Action<float> onDeltaY; // callback (delta hauteur)
    public float sensitivity = 0.01f;

    Vector2 startPos;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    public void SetScreenPosition(Vector2 screenPos)
    {
        ((RectTransform)transform).position = screenPos;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        startPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        float dy = eventData.position.y - startPos.y;
        startPos = eventData.position;

        onDeltaY?.Invoke(dy * sensitivity);
    }

    public void OnEndDrag(PointerEventData eventData) { }
}
