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
    public float groundY = 0f;
    public bool dragOnGroundPlane = true;

    [Header("UI")]
    public bool keepOnTop = true;

    public static bool IsDraggingAnyHandle { get; private set; }

    private RectTransform _rect;
    private bool _dragging;
    private Plane _dragPlane;
    private Vector3 _offsetWorld;

    private Canvas _rootCanvas;
    private RectTransform _canvasRect;
    private Camera _uiCamera;

    void Awake()
    {
        CacheCanvas();
    }

    void OnEnable()
    {
        CacheCanvas();
    }

    void OnDisable()
    {
        if (_dragging)
        {
            _dragging = false;
            IsDraggingAnyHandle = false;
        }
    }

    void CacheCanvas()
    {
        _rect = (RectTransform)transform;

        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            _rootCanvas = null;
            _canvasRect = null;
            _uiCamera = null;
            return;
        }

        _rootCanvas = parentCanvas.rootCanvas;
        _canvasRect = _rootCanvas.transform as RectTransform;

        if (_rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            _uiCamera = null;
        else
            _uiCamera = _rootCanvas.worldCamera;

        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
    }

    void LateUpdate()
    {
        if (cam == null || provider == null)
            return;

        if (_rect == null || _canvasRect == null)
            CacheCanvas();

        if (_canvasRect == null)
            return;

        int count = provider.ControlPointCount;
        if (index < 0 || index >= count)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (!provider.IsControlPointEditable(index))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        Vector3 world = provider.GetControlPointWorld(index);
        Vector3 screen = cam.WorldToScreenPoint(world);

        if (screen.z <= 0f)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, _uiCamera, out localPoint))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        _rect.anchoredPosition = localPoint;

        if (keepOnTop)
            _rect.SetAsLastSibling();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (cam == null || provider == null)
            return;

        int count = provider.ControlPointCount;
        if (index < 0 || index >= count)
            return;

        WallUndoManager undo = FindFirstObjectByType<WallUndoManager>();
        if (undo != null)
            undo.RecordSnapshot("Move Handle");

        _dragging = true;
        IsDraggingAnyHandle = true;

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
        if (!_dragging || cam == null || provider == null)
            return;

        int count = provider.ControlPointCount;
        if (index < 0 || index >= count)
            return;

        if (!TryScreenToPlaneWorld(eventData.position, _dragPlane, out var hit))
            return;

        Vector3 newWorld = hit + _offsetWorld;
        provider.SetControlPointWorld(index, newWorld);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _dragging = false;
        IsDraggingAnyHandle = false;
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
