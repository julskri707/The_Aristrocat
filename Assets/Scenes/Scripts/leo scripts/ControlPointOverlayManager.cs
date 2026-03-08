using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ControlPointOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public Camera cam;
    public RectTransform handlesRoot;
    public GameObject handlePrefab;

    [Header("Target Provider Behaviour")]
    public MonoBehaviour targetProviderBehaviour;

    private IControlPointProvider _provider;
    private IControlPointPathProvider _pathProvider;

    [Header("Preview Line (UI Overlay)")]
    public bool showPreviewLine = true;
    public float lineThickness = 3f;
    public Color lineColor = Color.yellow;

    private readonly List<ControlPointHandleUI> _handles = new();
    private readonly List<Image> _lineSegments = new();

    private RectTransform _linesRoot;
    private Canvas _canvas;

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        ResolveProvider();
        EnsureRoots();
    }

    void Start()
    {
        RebuildHandles();
        UpdatePreviewLineUI();
    }

    void LateUpdate()
    {
        UpdatePreviewLineUI();
    }

    void ResolveProvider()
    {
        _provider = targetProviderBehaviour as IControlPointProvider;
        _pathProvider = targetProviderBehaviour as IControlPointPathProvider;
    }

    public void SetTarget(MonoBehaviour provider)
    {
        targetProviderBehaviour = provider;
        ResolveProvider();
        RebuildHandles();
        UpdatePreviewLineUI();
    }

    void EnsureRoots()
    {
        if (handlesRoot == null)
            return;

        _canvas = handlesRoot.GetComponentInParent<Canvas>();

        Transform existing = handlesRoot.parent.Find("PreviewLinesRoot");
        if (existing != null)
        {
            _linesRoot = existing as RectTransform;
            return;
        }

        GameObject go = new GameObject("PreviewLinesRoot", typeof(RectTransform));
        _linesRoot = go.GetComponent<RectTransform>();
        _linesRoot.SetParent(handlesRoot.parent, false);

        _linesRoot.anchorMin = Vector2.zero;
        _linesRoot.anchorMax = Vector2.one;
        _linesRoot.offsetMin = Vector2.zero;
        _linesRoot.offsetMax = Vector2.zero;
        _linesRoot.localScale = Vector3.one;

        // lignes derrière les handles
        _linesRoot.SetSiblingIndex(handlesRoot.GetSiblingIndex());
        handlesRoot.SetAsLastSibling();
    }

    // -------------------------------------------------------
    // HANDLES
    // -------------------------------------------------------
    public void RebuildHandles()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            if (_handles[i] != null)
                Destroy(_handles[i].gameObject);
        }
        _handles.Clear();

        if (_provider == null || handlePrefab == null || handlesRoot == null || cam == null)
            return;

        int count = _provider.ControlPointCount;

        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(handlePrefab, handlesRoot);
            var handle = go.GetComponent<ControlPointHandleUI>();

            if (handle == null)
            {
                Destroy(go);
                continue;
            }

            handle.cam = cam;
            handle.provider = _provider;
            handle.index = i;

            _handles.Add(handle);
        }
    }

    // -------------------------------------------------------
    // PREVIEW LINE AS UI
    // -------------------------------------------------------
    void UpdatePreviewLineUI()
    {
        if (!showPreviewLine || _provider == null || handlesRoot == null || _linesRoot == null)
        {
            ClearLineSegments();
            return;
        }

        List<Vector3> pathWorld = null;

        // 1) Priorité au path provider (courbe propre)
        if (_pathProvider != null)
            pathWorld = _pathProvider.GetPreviewPathWorld();

        // 2) Fallback : relier les control points dans l’ordre
        if (pathWorld == null || pathWorld.Count < 2)
        {
            int count = _provider.ControlPointCount;
            if (count < 2)
            {
                ClearLineSegments();
                return;
            }

            pathWorld = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
                pathWorld.Add(_provider.GetControlPointWorld(i));
        }

        // Convertit les points monde -> points UI locaux
        List<Vector2> localPoints = new List<Vector2>();
        for (int i = 0; i < pathWorld.Count; i++)
        {
            Vector3 screen = cam.WorldToScreenPoint(pathWorld[i]);

            // derrière la caméra = on skip
            if (screen.z <= 0f)
                continue;

            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _linesRoot,
                screen,
                GetCanvasCamera(),
                out local))
            {
                localPoints.Add(local);
            }
        }

        if (localPoints.Count < 2)
        {
            ClearLineSegments();
            return;
        }

        int neededSegments = localPoints.Count - 1;
        EnsureLineSegmentCount(neededSegments);

        for (int i = 0; i < neededSegments; i++)
        {
            DrawSegment(_lineSegments[i].rectTransform, localPoints[i], localPoints[i + 1]);
            _lineSegments[i].color = lineColor;
            _lineSegments[i].gameObject.SetActive(true);
        }

        for (int i = neededSegments; i < _lineSegments.Count; i++)
            _lineSegments[i].gameObject.SetActive(false);
    }

    void EnsureLineSegmentCount(int count)
    {
        while (_lineSegments.Count < count)
        {
            GameObject go = new GameObject("LineSegment", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(_linesRoot, false);

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = lineColor;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            _lineSegments.Add(img);
        }
    }

    void DrawSegment(RectTransform rt, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float length = delta.magnitude;

        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(length, lineThickness);

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    void ClearLineSegments()
    {
        for (int i = 0; i < _lineSegments.Count; i++)
        {
            if (_lineSegments[i] != null)
                _lineSegments[i].gameObject.SetActive(false);
        }
    }

    Camera GetCanvasCamera()
    {
        if (_canvas == null)
            return null;

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return _canvas.worldCamera != null ? _canvas.worldCamera : cam;
    }
}