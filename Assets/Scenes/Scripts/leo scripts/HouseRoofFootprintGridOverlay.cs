using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Optionnel : LineRenderer de debug pour la base du toit (désactivé par défaut). Le snap des poignées utilise
/// les méthodes statiques de cette classe ; aucun composant n’est requis sur la maison pour l’aimantation.
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofFootprintGridOverlay : MonoBehaviour
{
    const string GridChildName = "__RoofFootprintGrid";
    const string AnchorChildName = "__RoofFootprintGridAnchor";

    static Material s_SharedLineMaterial;

    [Tooltip("Affiche la boucle grise et le segment centre→ancrage (second sommet). Désactivé par défaut.")]
    [SerializeField] bool showGrid = false;
    [SerializeField] Color gridColor = new Color(0.65f, 0.68f, 0.72f, 0.92f);
    [SerializeField] Color anchorGuideColor = new Color(1f, 0.72f, 0.38f, 0.88f);
    [SerializeField] float lineWidth = 0.045f;
    [SerializeField] float anchorLineWidth = 0.032f;
    [Tooltip("Au-dessus du plan base du toit (même référence que HouseRoofSystem).")]
    [SerializeField] float yOffsetAboveRoofBase = 0.04f;
    [SerializeField] int sortingOrder = 5200;

    HouseRoofSystem _roof;
    LineRenderer _lrGrid;
    LineRenderer _lrAnchor;
    bool _materialInstancedGrid;
    bool _materialInstancedAnchor;
    int _lastHash;

    void Awake() => _roof = GetComponent<HouseRoofSystem>();

    /// <summary>
    /// Aimantation XZ du second sommet vers le centre + 8 points du pourtour (même géométrie que la grille).
    /// </summary>
    public static bool TrySnapWorldXZToFootprintGrid(
        WallEditShape edit,
        WallObject wall,
        HouseRoofSystem roof,
        Vector2 worldXZ,
        float snapRadiusMeters,
        out Vector2 snappedXZ)
    {
        snappedXZ = worldXZ;
        if (edit == null || wall == null || roof == null || snapRadiusMeters <= 1e-6f)
            return false;

        float baseY = edit.shapeY + wall.height + roof.yOffsetAboveWallTop + HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        float y = baseY + 0.04f;
        if (!edit.TryGetFootprintGridNinePointsAtY(y, out Vector3[] pts))
            return false;

        float bestSq = float.MaxValue;
        Vector2 best = worldXZ;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 p = new Vector2(pts[i].x, pts[i].z);
            float d = (p - worldXZ).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                best = p;
            }
        }

        float limSq = snapRadiusMeters * snapRadiusMeters;
        if (bestSq > limSq)
            return false;

        snappedXZ = best;
        return true;
    }

    /// <summary>
    /// Second sommet « grille seulement » : parmi les nœuds du même guide que le maillage, celui qui respecte
    /// la limite de déport et qui est le plus proche du curseur (pas de position intermédiaire libre).
    /// </summary>
    public static bool TryResolveHorizontalApexOnFootprintGrid(
        WallEditShape edit,
        WallObject wall,
        HouseRoofSystem roof,
        Vector2 dragXZWorld,
        Vector2 footprintCentroidXZ,
        float maxAnchorOffsetRadius,
        out Vector2 meshAnchorOffsetXZ)
    {
        meshAnchorOffsetXZ = Vector2.zero;
        if (edit == null || wall == null || roof == null)
            return false;

        float baseY = edit.shapeY + wall.height + roof.yOffsetAboveWallTop + HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        float y = baseY + 0.04f;
        if (!roof.TryGetFootprintSnapPointsWorld(y, out Vector3[] pts))
            return false;

        float limSq = maxAnchorOffsetRadius * maxAnchorOffsetRadius;
        float bestSq = float.MaxValue;
        Vector2 bestOff = Vector2.zero;
        bool any = false;

        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 p = new Vector2(pts[i].x, pts[i].z);
            Vector2 off = p - footprintCentroidXZ;
            if (off.sqrMagnitude > limSq + 1e-6f)
                continue;

            float d = (p - dragXZWorld).sqrMagnitude;
            if (!any || d < bestSq)
            {
                any = true;
                bestSq = d;
                bestOff = off;
            }
        }

        if (!any)
        {
            meshAnchorOffsetXZ = Vector2.zero;
            return true;
        }

        meshAnchorOffsetXZ = bestOff;
        return true;
    }

    void LateUpdate()
    {
        if (_roof == null)
            _roof = GetComponent<HouseRoofSystem>();

        if (!showGrid || _roof == null)
        {
            SetAllEnabled(false);
            return;
        }

        WallObject wall = GetComponent<WallObject>();
        WallEditShape edit = GetComponent<WallEditShape>();
        if (wall == null || edit == null || !edit.IsClosedLoopPath)
        {
            SetAllEnabled(false);
            return;
        }

        float baseY = edit.shapeY + wall.height + _roof.yOffsetAboveWallTop + HouseRoofSystem.RoofBuiltInVerticalLiftMeters;
        float y = baseY + yOffsetAboveRoofBase;

        if (!_roof.TryGetFootprintGuideLoopWorld(y, out Vector3[] guideLoop))
        {
            SetAllEnabled(false);
            return;
        }

        HouseRoofControlPointProvider roofUi = _roof.GetComponentInChildren<HouseRoofControlPointProvider>(true);
        bool dualHandle = roofUi != null && roofUi.IsHorizontalApexHandleEnabled;
        Vector2 anchorOff = (_roof != null && _roof.lateralApexHandleEnabled) ? _roof.lateralApexOffsetXZ : Vector2.zero;

        int h = HashGridState(guideLoop, y, dualHandle, anchorOff);
        if (h == _lastHash && _lrGrid != null && _lrGrid.enabled)
            return;

        _lastHash = h;
        EnsureGridLineRenderer();
        BuildFootprintGuideLoop(guideLoop);

        if (dualHandle && anchorOff.sqrMagnitude > 1e-10f)
        {
            EnsureAnchorLineRenderer();
            Vector3 cen = Vector3.zero;
            if (_roof.TryGetFootprintSnapPointsWorld(y, out Vector3[] snap) && snap != null && snap.Length > 0)
                cen = snap[snap.Length - 1];
            Vector3 proj = new Vector3(cen.x + anchorOff.x, y, cen.z + anchorOff.y);
            BuildAnchorGuide(cen, proj, y);
            _lrAnchor.enabled = true;
        }
        else if (_lrAnchor != null)
            _lrAnchor.enabled = false;

        SetAllEnabled(true);
    }

    static int HashGridState(Vector3[] guideLoop, float y, bool dualHandle, Vector2 anchorOff)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt(y * 1000f);
            if (guideLoop != null)
            {
                for (int i = 0; i < guideLoop.Length; i++)
                {
                    h = h * 31 + Mathf.RoundToInt(guideLoop[i].x * 100f);
                    h = h * 31 + Mathf.RoundToInt(guideLoop[i].z * 100f);
                }
            }

            h = h * 31 + (dualHandle ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(anchorOff.x * 1000f);
            h = h * 31 + Mathf.RoundToInt(anchorOff.y * 1000f);
            return h;
        }
    }

    static Material GetOrCreateSharedLineMaterial()
    {
        if (s_SharedLineMaterial != null)
            return s_SharedLineMaterial;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
            sh = Shader.Find("Sprites/Default");
        if (sh == null)
            sh = Shader.Find("Unlit/Color");
        if (sh == null)
            return null;

        s_SharedLineMaterial = new Material(sh)
        {
            name = "HouseRoofFootprintGrid_Shared",
            hideFlags = HideFlags.DontSave
        };
        return s_SharedLineMaterial;
    }

    void EnsureGridLineRenderer()
    {
        if (_lrGrid != null)
            return;

        Transform existing = transform.Find(GridChildName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(GridChildName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = gameObject.layer;

        _lrGrid = go.GetComponent<LineRenderer>();
        if (_lrGrid == null)
            _lrGrid = go.AddComponent<LineRenderer>();

        _lrGrid.loop = true;
        _lrGrid.useWorldSpace = true;
        _lrGrid.textureMode = LineTextureMode.Stretch;
        _lrGrid.alignment = LineAlignment.View;
        _lrGrid.numCornerVertices = 2;
        _lrGrid.numCapVertices = 2;
        _lrGrid.shadowCastingMode = ShadowCastingMode.Off;
        _lrGrid.receiveShadows = false;
        _lrGrid.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _lrGrid.sortingOrder = sortingOrder;
        _lrGrid.startWidth = lineWidth;
        _lrGrid.endWidth = lineWidth;

        Material template = GetOrCreateSharedLineMaterial();
        if (template != null && !_materialInstancedGrid)
        {
            _lrGrid.material = new Material(template) { name = "HouseRoofFootprintGrid_Instance" };
            _materialInstancedGrid = true;
        }

        ApplyVisualGrid();
    }

    void EnsureAnchorLineRenderer()
    {
        if (_lrAnchor != null)
            return;

        Transform existing = transform.Find(AnchorChildName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(AnchorChildName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = gameObject.layer;

        _lrAnchor = go.GetComponent<LineRenderer>();
        if (_lrAnchor == null)
            _lrAnchor = go.AddComponent<LineRenderer>();

        _lrAnchor.loop = false;
        _lrAnchor.useWorldSpace = true;
        _lrAnchor.textureMode = LineTextureMode.Stretch;
        _lrAnchor.alignment = LineAlignment.View;
        _lrAnchor.numCornerVertices = 2;
        _lrAnchor.numCapVertices = 2;
        _lrAnchor.shadowCastingMode = ShadowCastingMode.Off;
        _lrAnchor.receiveShadows = false;
        _lrAnchor.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _lrAnchor.sortingOrder = sortingOrder + 1;
        _lrAnchor.startWidth = anchorLineWidth;
        _lrAnchor.endWidth = anchorLineWidth;

        Material template = GetOrCreateSharedLineMaterial();
        if (template != null && !_materialInstancedAnchor)
        {
            _lrAnchor.material = new Material(template) { name = "HouseRoofFootprintGridAnchor_Instance" };
            _materialInstancedAnchor = true;
        }

        ApplyVisualAnchor();
    }

    void ApplyVisualGrid()
    {
        if (_lrGrid == null)
            return;

        _lrGrid.sortingOrder = sortingOrder;
        _lrGrid.startWidth = lineWidth;
        _lrGrid.endWidth = lineWidth;
        _lrGrid.startColor = gridColor;
        _lrGrid.endColor = gridColor;

        Material mat = _lrGrid.material != null ? _lrGrid.material : GetOrCreateSharedLineMaterial();
        if (mat == null)
            return;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", gridColor);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", gridColor);
        else
            mat.color = gridColor;

        mat.renderQueue = 3200;
        if (mat.HasProperty("_ZWrite"))
            mat.SetInt("_ZWrite", 0);
    }

    void ApplyVisualAnchor()
    {
        if (_lrAnchor == null)
            return;

        _lrAnchor.sortingOrder = sortingOrder + 1;
        _lrAnchor.startWidth = anchorLineWidth;
        _lrAnchor.endWidth = anchorLineWidth;
        _lrAnchor.startColor = anchorGuideColor;
        _lrAnchor.endColor = anchorGuideColor;

        Material mat = _lrAnchor.material != null ? _lrAnchor.material : GetOrCreateSharedLineMaterial();
        if (mat == null)
            return;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", anchorGuideColor);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", anchorGuideColor);
        else
            mat.color = anchorGuideColor;

        mat.renderQueue = 3200;
        if (mat.HasProperty("_ZWrite"))
            mat.SetInt("_ZWrite", 0);
    }

    void OnValidate()
    {
        _lastHash = 0;
        if (_lrGrid != null)
            ApplyVisualGrid();
        if (_lrAnchor != null)
            ApplyVisualAnchor();
    }

    /// <summary>Même pourtour que la semelle du maillage : coins du débord + milieux (2N points fermés).</summary>
    void BuildFootprintGuideLoop(Vector3[] pts)
    {
        if (_lrGrid == null || pts == null || pts.Length < 3)
            return;
        _lrGrid.positionCount = pts.Length;
        _lrGrid.SetPositions(pts);
    }

    void BuildAnchorGuide(Vector3 footprintCenter, Vector3 anchorProjected, float yGrid)
    {
        var pts = new Vector3[2];
        pts[0] = new Vector3(footprintCenter.x, yGrid, footprintCenter.z);
        pts[1] = new Vector3(anchorProjected.x, yGrid, anchorProjected.z);
        _lrAnchor.positionCount = 2;
        _lrAnchor.SetPositions(pts);
        _lrAnchor.loop = false;
    }

    void SetAllEnabled(bool on)
    {
        if (!on)
        {
            if (_lrGrid != null)
                _lrGrid.enabled = false;
            if (_lrAnchor != null)
                _lrAnchor.enabled = false;
            return;
        }

        EnsureGridLineRenderer();
        ApplyVisualGrid();
        if (_lrGrid != null)
            _lrGrid.enabled = true;
    }
}
