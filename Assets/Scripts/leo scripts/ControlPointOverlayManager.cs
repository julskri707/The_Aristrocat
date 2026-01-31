using System.Collections.Generic;
using UnityEngine;

public class ControlPointOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public Camera cam;

    [Header("UI Roots")]
    public RectTransform handlesRoot;
    public RectTransform linksRoot;

    [Header("Prefabs")]
    public GameObject handlePrefab;
    public GameObject linkPrefab;

    [Header("Target Provider Behaviour")]
    public MonoBehaviour targetProviderBehaviour;

    [Header("Links Options")]
    public bool connectLoop = true;
    public float linkThickness = 4f;

    private IControlPointProvider _provider;
    private readonly List<ControlPointHandleUI> _handles = new();
    private readonly List<ControlPointLinkUI> _links = new();

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Start()
    {
        RebuildHandles();
    }

    // ✅ NOUVEAU : appelé quand tu cliques un mur
    public void SetTarget(MonoBehaviour providerBehaviour)
    {
        targetProviderBehaviour = providerBehaviour;
        RebuildHandles();
    }

    public void RebuildHandles()
    {
        // Destroy old handles
        for (int i = 0; i < _handles.Count; i++)
            if (_handles[i] != null) Destroy(_handles[i].gameObject);
        _handles.Clear();

        // Destroy old links
        for (int i = 0; i < _links.Count; i++)
            if (_links[i] != null) Destroy(_links[i].gameObject);
        _links.Clear();

        if (cam == null) cam = Camera.main;
        if (handlesRoot == null || handlePrefab == null || cam == null)
        {
            Debug.LogWarning("[ControlPointOverlayManager] Missing refs: cam/handlesRoot/handlePrefab.");
            return;
        }

        _provider = targetProviderBehaviour as IControlPointProvider;
        if (targetProviderBehaviour != null && _provider == null)
        {
            Debug.LogError($"[ControlPointOverlayManager] '{targetProviderBehaviour.name}' ne fait pas IControlPointProvider.");
            return;
        }
        if (_provider == null) return;

        int count = _provider.ControlPointCount;
        if (count <= 0) return;

        // IMPORTANT : lignes derrière
        if (linksRoot != null && linksRoot.parent == handlesRoot)
            linksRoot.SetAsFirstSibling();

        // 1) Create links (behind)
        if (linkPrefab != null)
        {
            if (linksRoot == null)
                linksRoot = handlesRoot;

            for (int i = 0; i < count - 1; i++)
                CreateLink(i, i + 1);

            if (connectLoop && count >= 3)
                CreateLink(count - 1, 0);
        }

        // 2) Create handles (on top)
        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(handlePrefab, handlesRoot);
            go.transform.SetAsLastSibling();

            var handle = go.GetComponent<ControlPointHandleUI>();
            if (handle == null)
            {
                Debug.LogError("[ControlPointOverlayManager] handlePrefab doit contenir ControlPointHandleUI.");
                Destroy(go);
                continue;
            }

            handle.cam = cam;
            handle.provider = _provider;
            handle.index = i;

            _handles.Add(handle);
        }
    }

    private void CreateLink(int a, int b)
    {
        GameObject go = Instantiate(linkPrefab, linksRoot);

        var link = go.GetComponent<ControlPointLinkUI>();
        if (link == null)
        {
            Debug.LogError("[ControlPointOverlayManager] linkPrefab doit contenir ControlPointLinkUI.");
            Destroy(go);
            return;
        }

        link.cam = cam;
        link.provider = _provider;
        link.indexA = a;
        link.indexB = b;
        link.thickness = linkThickness;

        _links.Add(link);
    }
}
