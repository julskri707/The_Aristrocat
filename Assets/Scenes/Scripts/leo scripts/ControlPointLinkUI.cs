using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class ControlPointLinkUI : MonoBehaviour, IPointerDownHandler
{
    [Header("Binding")]
    public Camera cam;

    public IControlPointProvider provider;
    public MonoBehaviour providerBehaviour;
    public WallSelectionManager selectionManager;
    public int indexA;
    public int indexB;

    public bool useDirectWorldPoints = false;
    public Vector3 worldA;
    public Vector3 worldB;

    [Header("Look")]
    public float thickness = 10f;

    [Header("Click")]
    public bool autoCreateRaycastImage = true;

    private RectTransform _rect;
    private Canvas _rootCanvas;
    private RectTransform _canvasRect;
    private Camera _uiCamera;

    void Awake()
    {
        CacheCanvas();
        EnsureRaycastGraphic();
    }

    void OnEnable()
    {
        CacheCanvas();
        EnsureRaycastGraphic();
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

        _uiCamera = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;

        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
    }

    void EnsureRaycastGraphic()
    {
        Graphic g = GetComponent<Graphic>();
        if (g == null && autoCreateRaycastImage)
        {
            Image img = gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.001f);
            img.raycastTarget = true;
            g = img;
        }

        if (g != null)
            g.raycastTarget = true;
    }

    void LateUpdate()
    {
        if (_rect == null || _canvasRect == null)
            CacheCanvas();

        if (_canvasRect == null)
            return;

        Camera sceneCam = cam != null ? cam : Camera.main;
        if (sceneCam == null)
            return;

        if (!TryGetCurrentWorldSegment(out Vector3 wa, out Vector3 wb))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        Vector3 sa = sceneCam.WorldToScreenPoint(wa);
        Vector3 sb = sceneCam.WorldToScreenPoint(wb);

        if (sa.z <= 0f || sb.z <= 0f)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sa, _uiCamera, out Vector2 localA) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sb, _uiCamera, out Vector2 localB))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Vector2 dir = localB - localA;
        float length = dir.magnitude;
        if (length < 0.001f)
            length = 0.001f;

        Vector2 mid = (localA + localB) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        _rect.anchoredPosition = mid;
        _rect.sizeDelta = new Vector2(length, thickness);
        _rect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    public void SetDirectWorldPoints(Vector3 a, Vector3 b)
    {
        useDirectWorldPoints = true;
        worldA = a;
        worldB = b;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right)
            return;

        if (selectionManager == null)
            selectionManager = FindFirstObjectByType<WallSelectionManager>();

        if (selectionManager == null)
            return;

        if (TryGetPointOnDisplayedLink(eventData.position, out Vector3 worldPoint))
        {
            selectionManager.TryInsertPointAtWorldPosition(worldPoint, providerBehaviour);
            return;
        }

        selectionManager.TryInsertPointAtScreenPosition(eventData.position, providerBehaviour);
    }

    bool TryGetCurrentWorldSegment(out Vector3 a, out Vector3 b)
    {
        if (useDirectWorldPoints)
        {
            a = worldA;
            b = worldB;
            return true;
        }

        if (provider == null)
        {
            a = default;
            b = default;
            return false;
        }

        int count = provider.ControlPointCount;
        if (indexA < 0 || indexA >= count || indexB < 0 || indexB >= count)
        {
            a = default;
            b = default;
            return false;
        }

        if (!provider.IsControlPointEditable(indexA) || !provider.IsControlPointEditable(indexB))
        {
            a = default;
            b = default;
            return false;
        }

        a = provider.GetControlPointWorld(indexA);
        b = provider.GetControlPointWorld(indexB);
        return true;
    }

    bool TryGetPointOnDisplayedLink(Vector2 screenPos, out Vector3 worldPoint)
    {
        worldPoint = default;

        Camera sceneCam = cam != null ? cam : Camera.main;
        if (sceneCam == null)
            return false;

        if (!TryGetCurrentWorldSegment(out Vector3 a, out Vector3 b))
            return false;

        Ray ray = sceneCam.ScreenPointToRay(screenPos);

        if (TryClosestPointRayToSegment(ray, a, b, out Vector3 closestOnSegment))
        {
            worldPoint = closestOnSegment;
            return true;
        }

        if (TryProjectRayToSegmentPlane(ray, a, b, out Vector3 projected))
        {
            worldPoint = ClosestPointOnSegment(projected, a, b);
            return true;
        }

        worldPoint = (a + b) * 0.5f;
        return true;
    }

    static bool TryClosestPointRayToSegment(Ray ray, Vector3 segA, Vector3 segB, out Vector3 closestOnSegment)
    {
        closestOnSegment = default;

        Vector3 u = ray.direction;
        Vector3 v = segB - segA;
        Vector3 w0 = ray.origin - segA;

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w0);
        float e = Vector3.Dot(v, w0);

        float denom = a * c - b * b;
        float s;
        float t;

        if (denom < 0.000001f)
        {
            s = 0f;
            t = Mathf.Clamp01(e / Mathf.Max(0.000001f, c));
        }
        else
        {
            s = (b * e - c * d) / denom;
            t = (a * e - b * d) / denom;

            if (s < 0f)
            {
                s = 0f;
                t = Mathf.Clamp01(e / Mathf.Max(0.000001f, c));
            }
            else
            {
                t = Mathf.Clamp01(t);
            }
        }

        Vector3 pointOnRay = ray.origin + u * s;
        Vector3 pointOnSegment = segA + v * t;

        float separation = Vector3.Distance(pointOnRay, pointOnSegment);
        float allowed = Mathf.Max(0.1f, Vector3.Distance(segA, segB) * 0.15f);
        if (separation > allowed)
            return false;

        closestOnSegment = pointOnSegment;
        return true;
    }

    static bool TryProjectRayToSegmentPlane(Ray ray, Vector3 segA, Vector3 segB, out Vector3 point)
    {
        point = default;

        Vector3 segment = segB - segA;
        if (segment.sqrMagnitude < 0.000001f)
            return false;

        Vector3 planeNormal = Vector3.Cross(segment.normalized, Vector3.up);
        if (planeNormal.sqrMagnitude < 0.000001f)
            planeNormal = Vector3.Cross(segment.normalized, Vector3.forward);

        if (planeNormal.sqrMagnitude < 0.000001f)
            return false;

        Plane plane = new Plane(planeNormal.normalized, segA);
        if (!plane.Raycast(ray, out float enter))
            return false;

        point = ray.GetPoint(enter);
        return true;
    }

    static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.000001f)
            return a;

        float t = Vector3.Dot(point - a, ab) / len2;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }
}
