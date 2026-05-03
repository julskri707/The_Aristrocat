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
    [Tooltip("Clic droit : insertion seulement avec Ctrl (sinon menu contextuel). Clic gauche + Ctrl : insertion sur l’arête.")]
    public bool requireCtrlForInsert = true;

    private RectTransform _rect;
    private Canvas _rootCanvas;
    private RectTransform _canvasRect;
    private Camera _uiCamera;

    Vector3 _lastWaForLayout;
    Vector3 _lastWbForLayout;
    Vector3 _lastCamPosForLayout;
    Quaternion _lastCamRotForLayout;
    bool _hasLayoutCache;

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
                _hasLayoutCache = false;
                return;
            }

            if (!provider.IsControlPointEditable(indexA) || !provider.IsControlPointEditable(indexB))
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                _hasLayoutCache = false;
                return;
            }

            wa = provider.GetControlPointWorld(indexA);
            wb = provider.GetControlPointWorld(indexB);
        }

        if (_hasLayoutCache
            && (wa - _lastWaForLayout).sqrMagnitude < 1e-10f
            && (wb - _lastWbForLayout).sqrMagnitude < 1e-10f
            && (sceneCam.transform.position - _lastCamPosForLayout).sqrMagnitude < 1e-10f
            && Quaternion.Angle(sceneCam.transform.rotation, _lastCamRotForLayout) < 0.01f)
        {
            return;
        }

        _lastWaForLayout = wa;
        _lastWbForLayout = wb;
        _lastCamPosForLayout = sceneCam.transform.position;
        _lastCamRotForLayout = sceneCam.transform.rotation;
        _hasLayoutCache = true;

        Vector3 sa = sceneCam.WorldToScreenPoint(wa);
        Vector3 sb = sceneCam.WorldToScreenPoint(wb);

        if (sa.z <= 0f || sb.z <= 0f)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sa, _uiCamera, out Vector2 localA) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sb, _uiCamera, out Vector2 localB))
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            _hasLayoutCache = false;
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
        _hasLayoutCache = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (selectionManager == null)
            selectionManager = FindFirstObjectByType<WallSelectionManager>();

        if (selectionManager == null)
            return;

        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // Ctrl + clic gauche sur l’arête : insertion explicite (nouveau point).
        if (eventData.button == PointerEventData.InputButton.Left && ctrlHeld)
        {
#region agent log
            DebugSessionAgentLog.Write("H4", "ControlPointLinkUI.OnPointerDown", "insert_ctrl_left", "{}");
#endregion
            selectionManager.TryInsertPointAtScreenPosition(eventData.position, providerBehaviour);
            eventData.Use();
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Right)
            return;

        if (!requireCtrlForInsert || ctrlHeld)
        {
#region agent log
            DebugSessionAgentLog.Write("H4", "ControlPointLinkUI.OnPointerDown", "insert_right_try", "{}");
#endregion
            selectionManager.TryInsertPointAtScreenPosition(eventData.position, providerBehaviour);
        }
        else
            selectionManager.TryOpenContextMenuAtScreenPosition(eventData.position, providerBehaviour);

        eventData.Use();
    }
}
