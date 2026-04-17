using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(1f, 0.55f, 0.12f, 1f);

    public static bool IsDraggingAnyHandle { get; private set; }
    public static IControlPointProvider SelectedProvider { get; private set; }
    public static int SelectedIndex { get; private set; } = -1;
    public static bool SelectAllOnProvider { get; private set; }
    private static int s_LastDeleteFrame = -1;
    private static int s_LastSelectAllFrame = -1;

    private RectTransform _rect;
    private Graphic _graphic;
    private Graphic[] _graphics;
    private SpriteRenderer _spriteRenderer;
    private SpriteRenderer[] _spriteRenderers;
    private bool _dragging;
    private Plane _dragPlane;
    private Vector3 _offsetWorld;
    private Vector3 _dragStartWorld;
    private WallDrawInput _drawInput;

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
        if (_graphic == null)
            _graphic = GetComponent<Graphic>();
        if (_graphics == null || _graphics.Length == 0)
            _graphics = GetComponentsInChildren<Graphic>(true);
        if (_spriteRenderer == null)
            _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderers == null || _spriteRenderers.Length == 0)
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);

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
        ApplySelectionColor();
        HandleDeleteInput();

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

        SelectedProvider = provider;
        SelectedIndex = index;
        SelectAllOnProvider = false;

        _dragging = true;
        IsDraggingAnyHandle = true;

        Vector3 startWorld = provider.GetControlPointWorld(index);
        _dragStartWorld = startWorld;
        _drawInput = FindFirstObjectByType<WallDrawInput>();

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
        newWorld = SnapDraggedPointIfNeeded(newWorld);
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

    private Vector3 SnapDraggedPointIfNeeded(Vector3 world)
    {
        if (_drawInput == null || !_drawInput.enableGridSnap)
            return world;

        float step = Mathf.Max(0.05f, _drawInput.gridSize);
        Vector3 origin = _drawInput.gridOrigin;

        // Rectangle center handle must move by full grid steps, otherwise
        // a half-cell offset can place walls in the middle of cells.
        if (provider is WallEditShape editShape &&
            editShape.shapeKind == WallEditShape.ShapeKind.Rectangle &&
            index == 8)
        {
            float dx = world.x - _dragStartWorld.x;
            float dz = world.z - _dragStartWorld.z;
            dx = Mathf.Round(dx / step) * step;
            dz = Mathf.Round(dz / step) * step;
            world.x = _dragStartWorld.x + dx;
            world.z = _dragStartWorld.z + dz;
        }
        else
        {
            world.x = origin.x + Mathf.Round((world.x - origin.x) / step) * step;
            world.z = origin.z + Mathf.Round((world.z - origin.z) / step) * step;
        }

        if (_drawInput.flattenYToZero)
            world.y = 0f;

        return world;
    }

    private void ApplySelectionColor()
    {
        bool selected = provider != null &&
                        provider == SelectedProvider &&
                        (SelectAllOnProvider || index == SelectedIndex);
        Color c = selected ? selectedColor : normalColor;

        if (_graphic != null)
            _graphic.color = c;
        if (_graphics != null)
        {
            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] != null)
                    _graphics[i].color = c;
            }
        }

        if (_spriteRenderer != null)
            _spriteRenderer.color = c;
        if (_spriteRenderers != null)
        {
            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                if (_spriteRenderers[i] != null)
                    _spriteRenderers[i].color = c;
            }
        }
    }

    private void HandleDeleteInput()
    {
        if (provider == null || provider != SelectedProvider)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SelectAllOnProvider = false;
            return;
        }

        bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool selectAllPressed = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Q);
        if (ctrlDown && selectAllPressed)
        {
            if (s_LastSelectAllFrame != Time.frameCount)
            {
                SelectAllOnProvider = true;
                s_LastSelectAllFrame = Time.frameCount;
            }
            return;
        }

        bool isCurrentPoint = index == SelectedIndex;
        if (!SelectAllOnProvider && !isCurrentPoint)
            return;

        if (!Input.GetKeyDown(KeyCode.Delete) && !Input.GetKeyDown(KeyCode.Backspace))
            return;

        if (s_LastDeleteFrame == Time.frameCount)
            return;
        s_LastDeleteFrame = Time.frameCount;

        bool removed = TryDeleteSelectedPoint();
        if (!removed)
            return;

        ControlPointOverlayManager overlay = FindFirstObjectByType<ControlPointOverlayManager>();
        if (overlay != null)
            overlay.RebuildOverlay();
    }

    private bool TryDeleteSelectedPoint()
    {
        WallUndoManager undo = FindFirstObjectByType<WallUndoManager>();
        if (undo != null)
            undo.RecordSnapshot(SelectAllOnProvider ? "Delete Wall (All Points)" : "Delete Handle");

        if (SelectAllOnProvider)
            return TryDeleteWholeWall();

        if (provider is WallEditShape editShape)
        {
            if (editShape.shapeKind == WallEditShape.ShapeKind.Rectangle && index == 8)
                return TryDeleteWholeWall();

            return editShape.RemoveControlPointAt(index);
        }

        return false;
    }

    private bool TryDeleteWholeWall()
    {
        Component providerComponent = provider as Component;
        if (providerComponent == null)
            return false;

        WallObject wall = providerComponent.GetComponent<WallObject>();
        if (wall == null)
            return false;

        WallBuildController build = FindFirstObjectByType<WallBuildController>();
        if (build != null)
            build.UnregisterWall(wall);

        ControlPointOverlayManager overlay = FindFirstObjectByType<ControlPointOverlayManager>();
        if (overlay != null)
            overlay.ClearTarget();

        SelectedProvider = null;
        SelectedIndex = -1;
        SelectAllOnProvider = false;

        if (Application.isPlaying)
            Destroy(wall.gameObject);
        else
            DestroyImmediate(wall.gameObject);

        return true;
    }
}
