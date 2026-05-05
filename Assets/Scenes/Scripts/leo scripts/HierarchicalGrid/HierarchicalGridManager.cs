using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HierarchicalGridManager : MonoBehaviour
{
    [Header("References")]
    public HierarchicalGridSettings settings;
    public HierarchicalGridRenderer gridRenderer;
    public Camera focusCamera;
    public Transform focusTarget;

    [Tooltip("Optional. Used only for hover highlight behavior if extended later.")]
    public WallDrawInput drawInput;

    [Tooltip("When true, mesh grid is not rendered (WallDrawInput draws the LineRenderer overlay instead).")]
    public bool suppressMeshRendering;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] Vector3 currentFocus;
    [SerializeField] int currentLeafCount;
    [SerializeField] int currentRenderNodeCount;
    [SerializeField] int currentDepthUsed;

    readonly List<HierarchicalGridNode> _leafNodes = new List<HierarchicalGridNode>(4096);
    readonly List<HierarchicalGridNode> _renderNodes = new List<HierarchicalGridNode>(12000);
    HierarchicalGridNode _root;
    HierarchicalGridNode _hoverCell;
    Vector2 _rootCenter;
    Vector3 _lastFocus;
    float _lastFocusHeight;
    float _lastBuildTime;
    bool _initialized;
    bool _treeBuiltOnce;
    bool _hasRenderedOnce;
    int _lastRenderedNodeCount;
    float _nextHoverSampleTime;

    public IReadOnlyList<HierarchicalGridNode> LeafNodes => _leafNodes;
    public HierarchicalGridNode HoverCell => _hoverCell;

    /// <summary>
    /// Même facteur que <see cref="WallBuildController.GetEffectiveBuildingScale"/> : agrandit le rendu maillage de la grille
    /// autour de <see cref="HierarchicalGridSettings.gridWorldCenterXZ"/> pour l’aligner visuellement sur le bâtiment mis à l’échelle.
    /// </summary>
    public float GetGridVisualScaleXZ()
    {
        WallBuildController wbc = drawInput != null ? drawInput.wallBuild : null;
        if (wbc == null)
            wbc = FindFirstObjectByType<WallBuildController>(FindObjectsInactive.Include);
        return wbc != null ? Mathf.Max(0.01f, wbc.GetEffectiveBuildingScale()) : 1f;
    }

    void Awake()
    {
        if (gridRenderer == null)
            gridRenderer = GetComponent<HierarchicalGridRenderer>();
        if (gridRenderer == null)
            gridRenderer = gameObject.AddComponent<HierarchicalGridRenderer>();

        if (focusCamera == null)
            focusCamera = Camera.main;

        if (drawInput == null)
            drawInput = FindFirstObjectByType<WallDrawInput>();
    }

    void OnEnable()
    {
        _initialized = false;
        _treeBuiltOnce = false;
        _hasRenderedOnce = false;
        _lastRenderedNodeCount = -1;
    }

    void LateUpdate()
    {
        if (settings == null || gridRenderer == null)
            return;

        if (suppressMeshRendering)
        {
            gridRenderer.ClearAll();
            return;
        }

        Vector3 focus = ResolveFocusWorld();
        currentFocus = focus;
        float focusHeight = Mathf.Abs(focus.y - settings.gridPlaneY);

        bool recentered = EnsureRootCenter(focus);
        bool shouldRebuild = ShouldRebuild(focus, focusHeight, recentered);

        bool didRebuild = false;
        if (shouldRebuild)
        {
            RebuildTree();
            _lastFocus = focus;
            _lastFocusHeight = focusHeight;
            _lastBuildTime = Time.unscaledTime;
            _initialized = true;
            _treeBuiltOnce = true;
            didRebuild = true;
        }

        bool hoverChanged = UpdateHoverCell();
        bool nodeCountChanged = _lastRenderedNodeCount != _renderNodes.Count;
        bool shouldRender = !_hasRenderedOnce || didRebuild || hoverChanged || nodeCountChanged;
        if (shouldRender)
        {
            HierarchicalGridNode hoverForRender = _hoverCell;
            if (drawInput != null && !drawInput.enableGridSnap)
                hoverForRender = null;

            gridRenderer.Render(_renderNodes, hoverForRender, settings, GetGridVisualScaleXZ());
            _hasRenderedOnce = true;
            _lastRenderedNodeCount = _renderNodes.Count;
        }
    }

    public bool TryGetCellAtWorld(Vector3 worldPoint, out HierarchicalGridNode cell)
    {
        Vector2 p = new Vector2(worldPoint.x, worldPoint.z);
        for (int i = 0; i < _leafNodes.Count; i++)
        {
            HierarchicalGridNode c = _leafNodes[i];
            if (c != null && c.ContainsXZ(p))
            {
                cell = c;
                return true;
            }
        }

        cell = null;
        return false;
    }

    public Vector3 GetCellCenterWorld(HierarchicalGridNode cell)
    {
        if (cell == null)
            return new Vector3(0f, settings != null ? settings.gridPlaneY : 0f, 0f);
        return new Vector3(cell.center.x, settings != null ? settings.gridPlaneY : 0f, cell.center.y);
    }

    Vector3 ResolveFocusWorld()
    {
        if (settings == null)
            return Vector3.zero;

        Vector3 focus;
        if (settings.focusMode == HierarchicalGridSettings.FocusMode.ManualPoint)
            focus = settings.manualFocusPoint;
        else if (settings.focusMode == HierarchicalGridSettings.FocusMode.TargetTransform && focusTarget != null)
            focus = focusTarget.position;
        else
        {
            if (focusCamera == null)
                focusCamera = Camera.main;
            focus = focusCamera != null ? focusCamera.transform.position : settings.manualFocusPoint;
        }

        focus.x += settings.gridFocusOffsetXZ.x;
        focus.z += settings.gridFocusOffsetXZ.y;
        return focus;
    }

    bool EnsureRootCenter(Vector3 focusWorld)
    {
        float size = Mathf.Max(8f, settings.rootCellSize);
        Vector2 p = new Vector2(focusWorld.x, focusWorld.z);

        if (!_initialized)
        {
            _rootCenter = ComputeRootCenterXZ(p, size);
            return true;
        }

        if (!settings.recenterRootOnFocus)
            return false;

        if (_root == null || !_root.ContainsXZ(p))
        {
            _rootCenter = ComputeRootCenterXZ(p, size);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Root cell center: anchored on <see cref="HierarchicalGridSettings.gridWorldCenterXZ"/> so the grid
    /// is symmetric around the map center (unlike Floor-based snap which offsets by half a cell at origin).
    /// When <see cref="HierarchicalGridSettings.recenterRootOnFocus"/> is on, steps by root cell size from that anchor.
    /// </summary>
    Vector2 ComputeRootCenterXZ(Vector2 focusXZ, float size)
    {
        Vector2 c = settings.gridWorldCenterXZ;
        if (!settings.recenterRootOnFocus)
            return c;

        return new Vector2(
            c.x + Mathf.Round((focusXZ.x - c.x) / size) * size,
            c.y + Mathf.Round((focusXZ.y - c.y) / size) * size);
    }

    bool ShouldRebuild(Vector3 focus, float focusHeight, bool recentered)
    {
        // Mode demandé: tout rendre en même temps, sans LOD au zoom.
        // On construit l'arbre complet une seule fois (sauf recenter explicite).
        if (!_treeBuiltOnce)
            return true;

        if (!settings.recenterRootOnFocus)
            return false;

        if (!_initialized || _root == null)
            return true;
        if (recentered)
            return true;

        float now = Time.unscaledTime;
        if (now - _lastBuildTime < settings.minRebuildInterval)
            return false;

        if (Vector2.Distance(new Vector2(focus.x, focus.z), new Vector2(_lastFocus.x, _lastFocus.z)) >= settings.rebuildMoveThreshold)
            return true;

        if (Mathf.Abs(focusHeight - _lastFocusHeight) >= settings.rebuildHeightThreshold)
            return true;

        return false;
    }

    void RebuildTree()
    {
        _leafNodes.Clear();
        _renderNodes.Clear();
        currentDepthUsed = 0;

        float rootSize = Mathf.Max(8f, settings.rootCellSize);
        _root = new HierarchicalGridNode(_rootCenter, rootSize, 0);

        BuildRecursive(_root);

        currentLeafCount = _leafNodes.Count;
        currentRenderNodeCount = _renderNodes.Count;

        if (settings.enableDebugLogs)
            Debug.Log($"[HierarchicalGrid] rebuild leaves={currentLeafCount} depth={currentDepthUsed}");
    }

    void BuildRecursive(HierarchicalGridNode node)
    {
        _renderNodes.Add(node);

        if (_leafNodes.Count >= settings.maxLeafNodes)
        {
            _leafNodes.Add(node);
            return;
        }

        bool canSubdivide =
            node.depth < settings.maxDepth &&
            node.size / 3f >= settings.minCellSize;

        if (!canSubdivide)
        {
            _leafNodes.Add(node);
            if (node.depth > currentDepthUsed)
                currentDepthUsed = node.depth;
            return;
        }

        node.Subdivide();
        for (int i = 0; i < node.children.Length; i++)
            BuildRecursive(node.children[i]);
    }

    bool UpdateHoverCell()
    {
        HierarchicalGridNode before = _hoverCell;
        if (settings == null || !settings.highlightCellUnderMouse)
        {
            _hoverCell = null;
            return before != _hoverCell;
        }

        float now = Time.unscaledTime;
        if (now < _nextHoverSampleTime)
            return false;
        _nextHoverSampleTime = now + 0.05f;

        _hoverCell = null;

        if (focusCamera == null)
            focusCamera = Camera.main;
        if (focusCamera == null)
            return before != _hoverCell;

        Ray ray = focusCamera.ScreenPointToRay(Input.mousePosition);
        Plane gridPlane = new Plane(Vector3.up, new Vector3(0f, settings.gridPlaneY, 0f));
        if (!gridPlane.Raycast(ray, out float enter))
            return before != _hoverCell;

        Vector3 p = ray.GetPoint(enter);
        // Même référentiel que le rendu (ScaleOutlineXZ) et que WallDrawInput.UnscaleGridCornerXZAboutOrigin :
        // le quadtree est en coordonnées « logiques », le mesh est étiré par GetGridVisualScaleXZ autour de gridWorldCenterXZ.
        p = MouseHitWorldToLogicalGridXZ(p);
        TryGetCellAtWorld(p, out _hoverCell);
        return before != _hoverCell;
    }

    /// <summary>
    /// Inverse de l’échelle visuelle autour de <see cref="HierarchicalGridSettings.gridWorldCenterXZ"/> (voir <see cref="HierarchicalGridRenderer"/>).
    /// </summary>
    Vector3 MouseHitWorldToLogicalGridXZ(Vector3 worldOnPlane)
    {
        Vector2 O = settings.gridWorldCenterXZ;
        float s = GetGridVisualScaleXZ();
        if (Mathf.Approximately(s, 1f))
            return worldOnPlane;
        return new Vector3(
            O.x + (worldOnPlane.x - O.x) / s,
            worldOnPlane.y,
            O.y + (worldOnPlane.z - O.y) / s);
    }

}

