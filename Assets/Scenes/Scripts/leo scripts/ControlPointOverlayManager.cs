using System.Collections.Generic;
using UnityEngine;

public class ControlPointOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public Camera cam;
    public RectTransform handlesRoot;
    public GameObject handlePrefab;

    [Header("Links UI")]
    public RectTransform linksRoot;
    public GameObject linkPrefab;
    public bool showLinks = true;
    public bool usePreviewPathForLinks = true;

    [Header("Selection")]
    public WallSelectionManager selectionManager;

    [Header("Target Provider Behaviour")]
    public MonoBehaviour targetProviderBehaviour;

    [Header("Sorting")]
    public bool useSeparateSortingCanvases = true;
    public int baseSortingOrder = 5000;
    public int linksSortingOrderOffset = 0;
    public int handlesSortingOrderOffset = 1;

    private IControlPointProvider _provider;
    private IControlPointPathProvider _pathProvider;

    private readonly List<ControlPointHandleUI> _handles = new List<ControlPointHandleUI>();
    private readonly List<ControlPointLinkUI> _links = new List<ControlPointLinkUI>();

    private bool _linksUsePreviewPath = false;

    void Awake()
    {
        if (cam == null)
            cam = Camera.main;

        if (selectionManager == null)
            selectionManager = FindFirstObjectByType<WallSelectionManager>();

        RefreshProvider();
        EnsureRootCanvasSorting();
        ForceRootOrder();
    }

    void Start()
    {
        RebuildOverlay();
    }

    void LateUpdate()
    {
        EnsureRootCanvasSorting();
        ForceRootOrder();
        RefreshDynamicLinks();

        if (targetProviderBehaviour == null && (_provider != null || _pathProvider != null))
            ClearTarget();
    }

    public void SetTarget(MonoBehaviour providerBehaviour)
    {
        targetProviderBehaviour = providerBehaviour;
        RefreshProvider();
        RebuildOverlay();
    }

    public void ClearTarget()
    {
        targetProviderBehaviour = null;
        RefreshProvider();
        ClearHandles();
        ClearLinks();
    }

    public void RebuildOverlay()
    {
        RebuildHandles();
        RebuildLinks();
    }

    void RefreshProvider()
    {
        _provider = targetProviderBehaviour as IControlPointProvider;
        _pathProvider = targetProviderBehaviour as IControlPointPathProvider;
    }

    void EnsureRootCanvasSorting()
    {
        if (!useSeparateSortingCanvases)
            return;

        SetupSubCanvas(linksRoot, baseSortingOrder + linksSortingOrderOffset);
        SetupSubCanvas(handlesRoot, baseSortingOrder + handlesSortingOrderOffset);
    }

    void SetupSubCanvas(RectTransform root, int sortingOrder)
    {
        if (root == null)
            return;

        Canvas subCanvas = root.GetComponent<Canvas>();
        if (subCanvas == null)
            subCanvas = root.gameObject.AddComponent<Canvas>();

        subCanvas.overrideSorting = true;
        subCanvas.sortingOrder = sortingOrder;
        subCanvas.pixelPerfect = false;

        if (root.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
    }

    void ForceRootOrder()
    {
        if (linksRoot != null)
            linksRoot.SetAsFirstSibling();

        if (handlesRoot != null)
            handlesRoot.SetAsLastSibling();
    }

    public void RebuildHandles()
    {
        ClearHandles();

        if (_provider == null || handlePrefab == null || handlesRoot == null || cam == null)
            return;

        int count = _provider.ControlPointCount;
        if (count <= 0)
            return;

        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(handlePrefab, handlesRoot);
            ControlPointHandleUI handle = go.GetComponent<ControlPointHandleUI>();

            if (handle == null)
            {
                Destroy(go);
                continue;
            }

            handle.cam = cam;
            handle.provider = _provider;
            handle.index = i;

            go.transform.SetAsLastSibling();
            _handles.Add(handle);
        }
    }

    public void RebuildLinks()
    {
        ClearLinks();
        _linksUsePreviewPath = false;

        if (!showLinks || _provider == null || linkPrefab == null || linksRoot == null || cam == null)
            return;

        if (usePreviewPathForLinks && _pathProvider != null)
        {
            List<Vector3> path = _pathProvider.GetPreviewPathWorld();
            if (path != null && path.Count >= 2)
            {
                _linksUsePreviewPath = true;

                for (int i = 0; i < path.Count - 1; i++)
                    CreateDirectLink(path[i], path[i + 1]);

                return;
            }
        }

        int count = _provider.ControlPointCount;
        if (count < 2)
            return;

        for (int i = 0; i < count - 1; i++)
        {
            GameObject go = Instantiate(linkPrefab, linksRoot);
            ControlPointLinkUI link = go.GetComponent<ControlPointLinkUI>();

            if (link == null)
            {
                Destroy(go);
                continue;
            }

            link.cam = cam;
            link.provider = _provider;
            link.providerBehaviour = targetProviderBehaviour;
            link.selectionManager = selectionManager;
            link.indexA = i;
            link.indexB = i + 1;

            go.transform.SetAsLastSibling();
            _links.Add(link);
        }
    }

    void RefreshDynamicLinks()
    {
        if (!_linksUsePreviewPath)
            return;

        if (_pathProvider == null)
            return;

        List<Vector3> path = _pathProvider.GetPreviewPathWorld();
        if (path == null || path.Count < 2)
        {
            ClearLinks();
            return;
        }

        int desiredLinkCount = path.Count - 1;
        if (desiredLinkCount != _links.Count)
        {
            RebuildLinks();
            return;
        }

        for (int i = 0; i < _links.Count; i++)
        {
            if (_links[i] == null)
                continue;

            _links[i].cam = cam;
            _links[i].provider = _provider;
            _links[i].providerBehaviour = targetProviderBehaviour;
            _links[i].selectionManager = selectionManager;
            _links[i].SetDirectWorldPoints(path[i], path[i + 1]);
        }
    }

    void CreateDirectLink(Vector3 a, Vector3 b)
    {
        GameObject go = Instantiate(linkPrefab, linksRoot);
        ControlPointLinkUI link = go.GetComponent<ControlPointLinkUI>();

        if (link == null)
        {
            Destroy(go);
            return;
        }

        link.cam = cam;
        link.provider = _provider;
        link.providerBehaviour = targetProviderBehaviour;
        link.selectionManager = selectionManager;
        link.SetDirectWorldPoints(a, b);

        go.transform.SetAsLastSibling();
        _links.Add(link);
    }

    void ClearHandles()
    {
        for (int i = 0; i < _handles.Count; i++)
        {
            if (_handles[i] != null)
                Destroy(_handles[i].gameObject);
        }

        _handles.Clear();
    }

    void ClearLinks()
    {
        for (int i = 0; i < _links.Count; i++)
        {
            if (_links[i] != null)
                Destroy(_links[i].gameObject);
        }

        _links.Clear();
    }
}
