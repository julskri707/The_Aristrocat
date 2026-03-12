using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class ControlPointLinkUI : MonoBehaviour
{
    [Header("Binding")]
    public Camera cam;

    // Mode provider + indices
    public IControlPointProvider provider;
    public int indexA;
    public int indexB;

    // Mode points directs
    public bool useDirectWorldPoints = false;
    public Vector3 worldA;
    public Vector3 worldB;

    [Header("Look")]
    public float thickness = 4f;

    private RectTransform _rect;
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
        if (_rect == null || _canvasRect == null)
            CacheCanvas();

        if (_canvasRect == null)
            return;

        Camera sceneCam = cam != null ? cam : Camera.main;
        if (sceneCam == null)
            return;

        Vector3 wa;
        Vector3 wb;

        if (useDirectWorldPoints)
        {
            wa = worldA;
            wb = worldB;
        }
        else
        {
            if (provider == null)
                return;

            int count = provider.ControlPointCount;
            if (indexA < 0 || indexA >= count || indexB < 0 || indexB >= count)
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (!provider.IsControlPointEditable(indexA) || !provider.IsControlPointEditable(indexB))
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            wa = provider.GetControlPointWorld(indexA);
            wb = provider.GetControlPointWorld(indexB);
        }

        Vector3 sa = sceneCam.WorldToScreenPoint(wa);
        Vector3 sb = sceneCam.WorldToScreenPoint(wb);

        if (sa.z <= 0f || sb.z <= 0f)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        Vector2 localA;
        Vector2 localB;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sa, _uiCamera, out localA) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sb, _uiCamera, out localB))
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
}