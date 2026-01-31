using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class ControlPointHandleUI : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Binding (assigné par le Manager)")]
    public Camera cam;
    public IControlPointProvider provider;
    public int index;

    [Header("Drag")]
    public float groundY = 0f;         // sol = Y=0 (change si besoin)
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

        // Si pas éditable: cacher
        if (!provider.IsControlPointEditable(index))
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        // Position écran du point world
        Vector3 world = provider.GetControlPointWorld(index);
        Vector3 screen = cam.WorldToScreenPoint(world);

        // Si derrière la caméra: cacher
        if (screen.z <= 0f)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        // Overlay: rect.position = screen
        _rect.position = screen;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (cam == null || provider == null) return;

        _dragging = true;

        Vector3 startWorld = provider.GetControlPointWorld(index);

        // Plan de drag
        if (dragOnGroundPlane)
        {
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f)); // Y = groundY
        }
        else
        {
            // Plan face caméra qui passe par le point
            _dragPlane = new Plane(-cam.transform.forward, startWorld);
        }

        // Offset anti-snap (pour ne pas "sauter" sous la souris)
        if (TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            _offsetWorld = startWorld - hit;
        else
            _offsetWorld = Vector3.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || cam == null || provider == null) return;

        if (!TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            return;

        Vector3 newWorld = hit + _offsetWorld;
        provider.SetControlPointWorld(index, newWorld);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _dragging = false;
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
