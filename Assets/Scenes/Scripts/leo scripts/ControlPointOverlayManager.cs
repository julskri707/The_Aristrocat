using System.Collections.Generic;
using UnityEngine;

public class ControlPointOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public Camera cam;
    public RectTransform handlesRoot;
    public GameObject handlePrefab;

    [Tooltip("Pivot global (MergedLotShapePivotHandleUI) : lot fusionné orthogonal, rectangle, triangle, ellipse, arc. Laisser vide pour le masquer.")]
    public GameObject mergedLotShapePivotPrefab;

    [Tooltip("Poignées roses (HouseEnvelopeSourceHandleUI) : un plan par lot source, enveloppe maison multi-plans. Vide = clone de mergedLotShapePivotPrefab sans le script pivot.")]
    public GameObject houseEnvelopeSourceHandlePrefab;

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
    private GameObject _mergedLotPivotInstance;
    private readonly List<GameObject> _envelopeSourceHandleInstances = new List<GameObject>(8);

    /// <summary>
    /// Enveloppe multi-plans + poignées par lot : indice du plan source dont on affiche les poignées (-1 = aucun jusqu’à clic sur le mur).
    /// </summary>
    int _independentHouseEnvelopeFocusedSourceLot = -1;

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
        ControlPointHandleUI.TickClearOverlayPointerBlockWhenPrimaryReleased();

        // Do not call EnsureRootCanvasSorting / ForceRootOrder here: sibling reorder on Canvas roots
        // forces a full Graphic rebuild every frame (very expensive with many handles/links).
        // Those run once from Awake/SetTarget/RebuildOverlay.
        RefreshDynamicLinks();

        if (targetProviderBehaviour == null && (_provider != null || _pathProvider != null))
            ClearTarget();
    }

    public void SetTarget(MonoBehaviour providerBehaviour, int independentHouseEnvelopeFocusedSourceLot = -1)
    {
        WallObject previousWall = null;
        if (targetProviderBehaviour is WallEditShape prevEdit && prevEdit.wall != null)
            previousWall = prevEdit.wall;

        targetProviderBehaviour = providerBehaviour;
        _independentHouseEnvelopeFocusedSourceLot = independentHouseEnvelopeFocusedSourceLot;

        WallObject nextWall = null;
        if (providerBehaviour is WallEditShape nextEdit && nextEdit.wall != null)
            nextWall = nextEdit.wall;

        if (previousWall != null && nextWall != null && previousWall != nextWall)
            EnvelopeOverlayHandleFocus.ClearAllFocus();

        RefreshProvider();
        RebuildOverlay();
    }

    public void ClearTarget()
    {
        EnvelopeOverlayHandleFocus.ClearAllFocus();
        targetProviderBehaviour = null;
        _independentHouseEnvelopeFocusedSourceLot = -1;
        RefreshProvider();
        ClearHandles();
        ClearLinks();
    }

    public void RebuildOverlay()
    {
        // Ne pas ClearFocusForWall ici : chaque rebuild réinitialisait rose/violet ; changement de mur géré par SetTarget / ClearTarget.

        ControlPointHandleUI.NotifyOverlayRebuildWhilePrimaryButtonMayStillBeHeld();
        RebuildHandles();
        RebuildLinks();
        EnsureRootCanvasSorting();
        ForceRootOrder();
    }

    void RefreshProvider()
    {
        _provider = targetProviderBehaviour as IControlPointProvider;
        _pathProvider = targetProviderBehaviour as IControlPointPathProvider;
    }

    /// <summary>
    /// Même contour que le plancher maison : <see cref="WallEditShape.GetOverlayPathWorld"/> évite le fil gris en « carré / L orthogonal »
    /// quand le mesh du mur suit encore l’ovale.
    /// </summary>
    List<Vector3> GetOverlayPathForCurrentTarget()
    {
        if (targetProviderBehaviour is WallEditShape wes)
            return wes.GetOverlayPathWorld();
        return _pathProvider != null ? _pathProvider.GetPreviewPathWorld() : null;
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

        if (handlePrefab == null || handlesRoot == null || cam == null)
            return;

        if (TryRebuildHandlesForIndependentHouseEnvelope())
            return;

        if (_provider == null)
            return;

        int count = _provider.ControlPointCount;
        if (count <= 0)
            return;

        // Toujours exclure l’indice « centre » (centroïde) de la liste des handles classiques :
        // sinon un second point blanc apparaît au milieu si mergedLotShapePivotPrefab n’est pas assigné.
        int skipHandleIndex = -1;
        if (targetProviderBehaviour is WallEditShape wSkip &&
            wSkip.TryGetShapeBulkMovePivotInfo(out int sk, out _) &&
            sk >= 0)
            skipHandleIndex = sk;

        for (int i = 0; i < count; i++)
        {
            if (i == skipHandleIndex)
                continue;

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

        // Pivot instancié après les roses, puis on remet chaque rose devant le violet (raycast : rose prioritaire).
        TrySpawnHouseEnvelopeSourceHandles();

        TrySpawnShapeBulkMovePivotHandle();

        BringHouseEnvelopeSourceHandlesToFront();
    }

    /// <summary>
    /// Enveloppe maison multi-plans : une poignée par point de contrôle de <b>chaque</b> lot source (formes d’origine),
    /// au lieu du contour fusionné unique sur le mur enveloppe.
    /// </summary>
    bool TryRebuildHandlesForIndependentHouseEnvelope()
    {
        if (targetProviderBehaviour is not WallEditShape envelopeWes || envelopeWes.wall == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelopeWes.wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || !meta.HasMultipleSourceLots || !meta.UseIndependentSourceHandlesForHouseEnvelope)
            return false;

        // Aucun lot encore ciblé par clic sur le mur : pivot violet de l’enveloppe seulement (même maison).
        if (_independentHouseEnvelopeFocusedSourceLot < 0)
        {
            TrySpawnShapeBulkMovePivotHandle();
            return true;
        }

        IReadOnlyList<GameObject> srcGos = meta.SourceLotObjects;
        if (srcGos == null)
            return false;

        // Évite deux poignées au même XZ (murs communs entre deux rectangles) : premier lot / indice gagne.
        const float handlePositionQuant = 200f;
        var usedHandlePositions = new HashSet<(int qx, int qz)>();

        int spawned = 0;
        int focus = _independentHouseEnvelopeFocusedSourceLot;
        for (int s = 0; s < srcGos.Count; s++)
        {
            if (s != focus)
                continue;

            GameObject lotGo = srcGos[s];
            if (lotGo == null)
                continue;

            WallEditShape srcEdit = lotGo.GetComponent<WallEditShape>();
            if (srcEdit == null)
                continue;

            IControlPointProvider srcProvider = srcEdit;
            int count = srcProvider.ControlPointCount;
            if (count <= 0)
                continue;

            for (int i = 0; i < count; i++)
            {
                Vector3 w = srcProvider.GetControlPointWorld(i);
                int qx = Mathf.RoundToInt(w.x * handlePositionQuant);
                int qz = Mathf.RoundToInt(w.z * handlePositionQuant);
                var key = (qx, qz);
                if (!usedHandlePositions.Add(key))
                    continue;

                GameObject go = Instantiate(handlePrefab, handlesRoot);
                ControlPointHandleUI handle = go.GetComponent<ControlPointHandleUI>();

                if (handle == null)
                {
                    Destroy(go);
                    continue;
                }

                handle.cam = cam;
                handle.provider = srcProvider;
                handle.index = i;

                go.transform.SetAsLastSibling();
                _handles.Add(handle);
                spawned++;
            }
        }

        if (spawned == 0)
            return false;

        TrySpawnHouseEnvelopeSourceHandles();
        TrySpawnShapeBulkMovePivotHandle();
        BringHouseEnvelopeSourceHandlesToFront();
        return true;
    }

    /// <summary>
    /// Dernier enfant = dessus pour GraphicRaycaster : les poignées roses (plans sources) doivent passer avant le pivot violet.
    /// </summary>
    void BringHouseEnvelopeSourceHandlesToFront()
    {
        for (int i = 0; i < _envelopeSourceHandleInstances.Count; i++)
        {
            if (_envelopeSourceHandleInstances[i] != null)
                _envelopeSourceHandleInstances[i].transform.SetAsLastSibling();
        }
    }

    void TrySpawnShapeBulkMovePivotHandle()
    {
        if (handlesRoot == null || cam == null || mergedLotShapePivotPrefab == null)
            return;

        if (targetProviderBehaviour is not WallEditShape wes)
            return;

        if (!wes.TryGetShapeBulkMovePivotInfo(out _, out _))
            return;

        if (_mergedLotPivotInstance != null)
        {
            MergedLotShapePivotHandleUI existing = _mergedLotPivotInstance.GetComponent<MergedLotShapePivotHandleUI>();
            if (existing != null)
            {
                existing.cam = cam;
                existing.edit = wes;
                return;
            }

            Destroy(_mergedLotPivotInstance);
            _mergedLotPivotInstance = null;
        }

        _mergedLotPivotInstance = Instantiate(mergedLotShapePivotPrefab, handlesRoot);
        MergedLotShapePivotHandleUI pivotUi = _mergedLotPivotInstance.GetComponent<MergedLotShapePivotHandleUI>();
        if (pivotUi == null)
        {
            Destroy(_mergedLotPivotInstance);
            _mergedLotPivotInstance = null;
            return;
        }

        pivotUi.cam = cam;
        pivotUi.edit = wes;
    }

    void TrySpawnHouseEnvelopeSourceHandles()
    {
        GameObject prefab = houseEnvelopeSourceHandlePrefab;
        if (prefab == null && mergedLotShapePivotPrefab != null)
            prefab = mergedLotShapePivotPrefab;

        if (handlesRoot == null || cam == null || prefab == null)
            return;

        if (targetProviderBehaviour is not WallEditShape wes || wes.wall == null)
            return;

        HouseExteriorEnvelopeSources env = wes.wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (env == null || !env.HasMultipleSourceLots)
            return;

        bool filterOnePink =
            env.UseIndependentSourceHandlesForHouseEnvelope &&
            _independentHouseEnvelopeFocusedSourceLot >= 0;

        for (int i = 0; i < env.SourceLotObjects.Count; i++)
        {
            if (filterOnePink && i != _independentHouseEnvelopeFocusedSourceLot)
                continue;

            GameObject srcGo = env.SourceLotObjects[i];
            if (srcGo == null)
                continue;
            if (srcGo.GetComponent<WallObject>() == null)
                continue;

            GameObject go = Instantiate(prefab, handlesRoot);
            MergedLotShapePivotHandleUI oldPivot = go.GetComponent<MergedLotShapePivotHandleUI>();
            if (oldPivot != null)
                DestroyImmediate(oldPivot);

            HouseEnvelopeSourceHandleUI h = go.GetComponent<HouseEnvelopeSourceHandleUI>();
            if (h == null)
                h = go.AddComponent<HouseEnvelopeSourceHandleUI>();

            h.cam = cam;
            h.envelopeEdit = wes;
            h.sourceLotIndex = i;
            go.transform.SetAsLastSibling();
            _envelopeSourceHandleInstances.Add(go);
        }
    }

    public void RebuildLinks()
    {
        ClearLinks();
        _linksUsePreviewPath = false;

        if (!showLinks || linkPrefab == null || linksRoot == null || cam == null)
            return;

        if (TryRebuildLinksForIndependentHouseEnvelope())
            return;

        if (_provider == null)
            return;

        if (usePreviewPathForLinks && _pathProvider != null)
        {
            List<Vector3> path = GetOverlayPathForCurrentTarget();
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

        int skipLinkEndAt = -1;
        if (targetProviderBehaviour is WallEditShape wLink &&
            mergedLotShapePivotPrefab != null &&
            wLink.TryGetShapeBulkMovePivotInfo(out int skL, out bool mergedC) &&
            skL >= 0 && !mergedC)
            skipLinkEndAt = skL;

        for (int i = 0; i < count - 1; i++)
        {
            if (skipLinkEndAt >= 0 && i + 1 == skipLinkEndAt)
                continue;

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
        if (TryRefreshIndependentHouseEnvelopeLinks())
            return;

        if (!_linksUsePreviewPath)
            return;

        // GetOverlayPathForCurrentTarget() peut utiliser WallEditShape.GetOverlayPathWorld sans passer par IControlPointPathProvider.
        if (_pathProvider == null && targetProviderBehaviour is not WallEditShape)
            return;

        List<Vector3> path = GetOverlayPathForCurrentTarget();
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

    bool TryRefreshIndependentHouseEnvelopeLinks()
    {
        if (targetProviderBehaviour is not WallEditShape envelopeWes || envelopeWes.wall == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelopeWes.wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || !meta.HasMultipleSourceLots || !meta.UseIndependentSourceHandlesForHouseEnvelope)
            return false;

        if (!_linksUsePreviewPath)
            return false;

        if (_links.Count == 0)
            return true;

        List<Vector3> path = envelopeWes.GetOverlayPathWorld();
        if (path == null || path.Count < 2)
        {
            ClearLinks();
            return true;
        }

        int desiredLinkCount = path.Count - 1;
        if (desiredLinkCount != _links.Count)
        {
            RebuildLinks();
            return true;
        }

        for (int i = 0; i < _links.Count; i++)
        {
            if (_links[i] == null)
                continue;

            _links[i].cam = cam;
            _links[i].provider = envelopeWes;
            _links[i].providerBehaviour = envelopeWes;
            _links[i].selectionManager = selectionManager;
            _links[i].SetDirectWorldPoints(path[i], path[i + 1]);
        }

        return true;
    }

    void CreateDirectLink(Vector3 a, Vector3 b)
    {
        CreateDirectLink(a, b, _provider, targetProviderBehaviour);
    }

    void CreateDirectLink(Vector3 a, Vector3 b, IControlPointProvider forProvider, MonoBehaviour forBehaviour)
    {
        GameObject go = Instantiate(linkPrefab, linksRoot);
        ControlPointLinkUI link = go.GetComponent<ControlPointLinkUI>();

        if (link == null)
        {
            Destroy(go);
            return;
        }

        link.cam = cam;
        link.provider = forProvider;
        link.providerBehaviour = forBehaviour;
        link.selectionManager = selectionManager;
        link.SetDirectWorldPoints(a, b);

        go.transform.SetAsLastSibling();
        _links.Add(link);
    }

    bool TryRebuildLinksForIndependentHouseEnvelope()
    {
        if (targetProviderBehaviour is not WallEditShape envelopeWes || envelopeWes.wall == null)
            return false;

        HouseExteriorEnvelopeSources meta = envelopeWes.wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (meta == null || !meta.HasMultipleSourceLots || !meta.UseIndependentSourceHandlesForHouseEnvelope)
            return false;

        // Un seul contour (enveloppe) : évite de dessiner deux fois les arêtes communes entre lots sources.
        List<Vector3> path = envelopeWes.GetOverlayPathWorld();
        if (path == null || path.Count < 2)
            return false;

        for (int i = 0; i < path.Count - 1; i++)
            CreateDirectLink(path[i], path[i + 1], envelopeWes, envelopeWes);

        _linksUsePreviewPath = true;
        return true;
    }

    void ClearHandles()
    {
        for (int i = 0; i < _envelopeSourceHandleInstances.Count; i++)
        {
            if (_envelopeSourceHandleInstances[i] != null)
                Destroy(_envelopeSourceHandleInstances[i]);
        }

        _envelopeSourceHandleInstances.Clear();

        if (_mergedLotPivotInstance != null)
        {
            if (!MergedLotShapePivotHandleUI.ShouldPreserveMergedPivotThroughOverlayClear)
            {
                Destroy(_mergedLotPivotInstance);
                _mergedLotPivotInstance = null;
            }
        }

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
