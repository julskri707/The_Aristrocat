using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class ControlPointHandleUI : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // ✅ IMPORTANT: utilisé par WallDrawInputGuard
    public static bool IsDraggingAnyHandle { get; private set; }

    [Header("Binding (assigné par le Manager)")]
    public Camera cam;
    public IControlPointProvider provider;
    public int index;

    [Header("Drag")]
    public float groundY = 0f;
    public bool dragOnGroundPlane = true;

    private RectTransform _rect;
    private bool _dragging;
    private Plane _dragPlane;
    private Vector3 _offsetWorld;

    void Awake()
    {
        _rect = (RectTransform)transform;
    }

    void LateUpdate()
    {
        if (cam == null || provider == null) return;
        if (!provider.IsControlPointEditable(index)) return;

        Vector3 world = provider.GetControlPointWorld(index);
        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);
        _rect.position = screen;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // ✅ Empêche le clic de traverser vers le monde
        eventData.Use();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (cam == null || provider == null) return;
        if (!provider.IsControlPointEditable(index)) return;

        _dragging = true;
        IsDraggingAnyHandle = true;   // ✅ pour WallDrawInputGuard
        eventData.Use();

        Vector3 startWorld = provider.GetControlPointWorld(index);

        if (dragOnGroundPlane)
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        else
            _dragPlane = new Plane(-cam.transform.forward, startWorld);

        if (TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            _offsetWorld = startWorld - hit;
        else
            _offsetWorld = Vector3.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || cam == null || provider == null) return;

        eventData.Use();

        if (!TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            return;

        Vector3 newWorld = hit + _offsetWorld;
        provider.SetControlPointWorld(index, newWorld);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = false;
        IsDraggingAnyHandle = false;  // ✅ pour WallDrawInputGuard
        eventData.Use();
    }

    private bool TryScreenToPlaneWorld(Vector2 screenPos, Plane plane, out Vector3 world)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (plane.Raycast(ray, out float enter))
        {
            world = ray.GetPoint(enter);
            return true;
        }

        world = default;
        return false;
    }
}
