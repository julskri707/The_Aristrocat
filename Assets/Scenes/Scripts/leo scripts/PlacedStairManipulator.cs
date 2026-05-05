using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Escalier catalogue : 5 poignées overlay — 4 coins + <b>centre</b> (déplacement de tout l’objet au sol).
/// Molette : agrandit ou réduit l’empreinte (largeur + profondeur) selon le mode choisi dans le panneau.
/// </summary>
[DisallowMultipleComponent]
public class PlacedStairManipulator : MonoBehaviour, IControlPointProvider, IControlPointDragPlaneProvider, IControlPointWallShapeBinding
{
    public bool ControlPointsBelongToWallShape => false;

    const string LegacyCornerHandlesRootName = "__StairCornerHandles";

    [Header("Hauteur (fixée au placement)")]
    [SerializeField] float totalRiseMeters = 2.5f;

    [Header("Empreinte (+Z local = direction de montée)")]
    [SerializeField] float runLengthMeters = 5f;
    [SerializeField] float halfWidthMeters = 0.55f;

    [Header("Molette — pas relatif (coins sélectionnés ; centre = rotation 90° sur la poignée)")]
    [SerializeField] float footprintWheelScaleStep = 1.035f;

    [Header("Marches procédurales")]
    [SerializeField] float idealTreadDepthMeters = 0.27f;
    [SerializeField] int minStepCount = 18;
    [SerializeField] int maxStepCount = 22;

    [Header("Limites des poignées")]
    [SerializeField] float minHalfWidthMeters = 0.32f;
    [SerializeField] float maxHalfWidthMeters = 1.35f;
    [SerializeField] float minRunLengthMeters = 1.4f;
    [SerializeField] float maxRunLengthMeters = 14f;

    BoxCollider _box;
    ControlPointOverlayManager _cachedOverlay;
    readonly Vector3[] _footprintCornersScratch = new Vector3[4];
    readonly List<Vector2> _lotRingScratch = new List<Vector2>(64);

    public int ControlPointCount => 5;

    public float TotalRiseMeters => totalRiseMeters;

    public float HalfWidthMeters => halfWidthMeters;

    public float RunLengthMeters => runLengthMeters;

    /// <summary>Boîte englobante XZ de l’empreinte au sol (pour trémie parquet), avec marge.</summary>
    public void ComputeFootprintAabbXZ(float paddingMeters, out Vector2 centerXZ, out Vector2 halfExtentsXZ)
    {
        Quaternion q = transform.rotation;
        Vector3 r = transform.position;
        float hw = halfWidthMeters;
        float run = runLengthMeters;
        Vector3 c0 = r + q * new Vector3(-hw, 0f, 0f);
        Vector3 c1 = r + q * new Vector3(hw, 0f, 0f);
        Vector3 c2 = r + q * new Vector3(-hw, 0f, run);
        Vector3 c3 = r + q * new Vector3(hw, 0f, run);
        float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
        float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
        float minZ = Mathf.Min(Mathf.Min(c0.z, c1.z), Mathf.Min(c2.z, c3.z));
        float maxZ = Mathf.Max(Mathf.Max(c0.z, c1.z), Mathf.Max(c2.z, c3.z));
        centerXZ = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        halfExtentsXZ = new Vector2(
            (maxX - minX) * 0.5f + paddingMeters,
            (maxZ - minZ) * 0.5f + paddingMeters);
        halfExtentsXZ.x = Mathf.Max(halfExtentsXZ.x, 0.25f);
        halfExtentsXZ.y = Mathf.Max(halfExtentsXZ.y, 0.25f);
    }

    /// <summary>Après pose ou rotation : garde l’empreinte dans le lot désigné.</summary>
    public void ClampFootprintFullyInsideDesignatedLot()
    {
        if (!TryResolveContainingLotEdit(out WallEditShape edit))
            return;
        ClampFootprintTransformTowardLot(edit);
        RebuildGeometry();
    }

    /// <summary>Appelé après rotation à la molette sur la poignée centrale.</summary>
    public void NotifyRotatedWithFootprintClamp()
    {
        ClampFootprintFullyInsideDesignatedLot();
    }

    void OnEnable()
    {
        DestroyLegacyWorldHandleObjects();

        if (transform.Find(StairFlightMeshBuilder.GeometryChildName) != null)
            return;

        for (int i = 0; i < transform.childCount; i++)
        {
            string n = transform.GetChild(i).name;
            if (n.StartsWith("Step_", System.StringComparison.Ordinal))
            {
                RebuildGeometry();
                break;
            }
        }
    }

    public void ConfigureNewPlacement(float riseMeters, float floorWorldY, float? initialRunLength = null, float? initialHalfWidth = null)
    {
        totalRiseMeters = Mathf.Max(0.12f, riseMeters);
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, floorWorldY, p.z);

        float defaultRun = Mathf.Clamp(totalRiseMeters * 1.75f, minRunLengthMeters, maxRunLengthMeters);
        runLengthMeters = Mathf.Clamp(initialRunLength ?? defaultRun, minRunLengthMeters, maxRunLengthMeters);
        halfWidthMeters = Mathf.Clamp(initialHalfWidth ?? 0.55f, minHalfWidthMeters, maxHalfWidthMeters);

        EnsureCollider();
        RebuildGeometry();
        ClampFootprintTransformTowardLotAfterMutation();
    }

    void Update()
    {
        if (!IsOverlayTargetThis())
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Poignée centrale sélectionnée : la molette sert à la rotation (voir ControlPointHandleUI).
        if (ControlPointHandleUI.SelectedProvider == (IControlPointProvider)this &&
            ControlPointHandleUI.SelectedIndex == 4)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-6f)
            return;

        bool wheelUp = scroll > 0f;
        float factor = wheelUp ? footprintWheelScaleStep : 1f / footprintWheelScaleStep;
        ApplyUniformFootprintScale(factor);
    }

    bool IsOverlayTargetThis()
    {
        if (_cachedOverlay == null)
            _cachedOverlay = FindFirstObjectByType<ControlPointOverlayManager>();
        return _cachedOverlay != null && _cachedOverlay.targetProviderBehaviour == this;
    }

    void ApplyUniformFootprintScale(float factor)
    {
        Quaternion r = transform.rotation;
        Vector3 centerWorld = transform.position + r * new Vector3(0f, 0f, runLengthMeters * 0.5f);

        float hw = Mathf.Clamp(halfWidthMeters * factor, minHalfWidthMeters, maxHalfWidthMeters);
        float run = Mathf.Clamp(runLengthMeters * factor, minRunLengthMeters, maxRunLengthMeters);

        if (Mathf.Approximately(hw, halfWidthMeters) && Mathf.Approximately(run, runLengthMeters))
            return;

        halfWidthMeters = hw;
        runLengthMeters = run;

        transform.position = centerWorld - r * new Vector3(0f, 0f, runLengthMeters * 0.5f);
        RebuildGeometry();
        ClampFootprintTransformTowardLotAfterMutation();
    }

    void EnsureCollider()
    {
        if (_box == null)
            _box = GetComponent<BoxCollider>();
        if (_box == null)
            _box = gameObject.AddComponent<BoxCollider>();
    }

    int ComputeStepCountForRebuild()
    {
        return StairFlightMeshBuilder.ComputeStepCount(
            runLengthMeters,
            totalRiseMeters,
            idealTreadDepthMeters,
            minStepCount,
            maxStepCount);
    }

    public void RebuildGeometry()
    {
        DestroyLegacyWorldHandleObjects();
        StripLegacyRootGeometryChildren();

        runLengthMeters = Mathf.Clamp(runLengthMeters, minRunLengthMeters, maxRunLengthMeters);
        halfWidthMeters = Mathf.Clamp(halfWidthMeters, minHalfWidthMeters, maxHalfWidthMeters);

        int steps = ComputeStepCountForRebuild();
        float width = halfWidthMeters * 2f;
        StairFlightMeshBuilder.Rebuild(transform, totalRiseMeters, runLengthMeters, width, steps);

        EnsureCollider();
        float rise = totalRiseMeters;
        float run = runLengthMeters;
        _box.center = new Vector3(0f, rise * 0.5f, run * 0.5f);
        _box.size = new Vector3(width + 0.18f, rise + 0.06f, run + 0.1f);
        _box.isTrigger = false;
    }

    void DestroyLegacyWorldHandleObjects()
    {
        Transform legacy = transform.Find(LegacyCornerHandlesRootName);
        if (legacy != null)
            Destroy(legacy.gameObject);
    }

    void StripLegacyRootGeometryChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c == null)
                continue;
            string n = c.name;
            if (n == LegacyCornerHandlesRootName || n == StairFlightMeshBuilder.GeometryChildName)
                continue;
            if (n.StartsWith("Step_", System.StringComparison.Ordinal) ||
                n.StartsWith("Stringer", System.StringComparison.Ordinal) ||
                n == "BottomTrim")
                Destroy(c.gameObject);
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        if (index == 4)
            return transform.TransformPoint(FootprintCenterLocal());

        return transform.TransformPoint(LocalCorner(index));
    }

    Vector3 FootprintCenterLocal()
    {
        return new Vector3(0f, 0f, runLengthMeters * 0.5f);
    }

    Vector3 LocalCorner(int index)
    {
        float hw = halfWidthMeters;
        float r = runLengthMeters;
        switch (index)
        {
            case 0: return new Vector3(-hw, 0f, 0f);
            case 1: return new Vector3(hw, 0f, 0f);
            case 2: return new Vector3(-hw, 0f, r);
            case 3: return new Vector3(hw, 0f, r);
            default: return Vector3.zero;
        }
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        if (index == 4)
        {
            Vector3 current = GetControlPointWorld(4);
            Vector3 delta = worldPos - current;
            delta.y = 0f;
            transform.position += delta;
            ClampFootprintTransformTowardLotAfterMutation();
            return;
        }

        worldPos.y = transform.position.y;
        worldPos = SnapWorldXZIfInHouseLot(worldPos);

        Vector3 lp = transform.InverseTransformPoint(worldPos);
        lp.y = 0f;

        halfWidthMeters = Mathf.Clamp(Mathf.Abs(lp.x), minHalfWidthMeters, maxHalfWidthMeters);
        if (index == 2 || index == 3)
            runLengthMeters = Mathf.Clamp(lp.z, minRunLengthMeters, maxRunLengthMeters);

        RebuildGeometry();
        ClampFootprintTransformTowardLotAfterMutation();
    }

    public bool IsControlPointEditable(int index) => index >= 0 && index < 5;

    public bool TryGetDragPlane(int index, Camera cam, Vector3 startWorld, out Plane plane)
    {
        float y = transform.position.y;
        plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
        return true;
    }

    Vector3 SnapWorldXZIfInHouseLot(Vector3 world)
    {
        if (!RuntimeAssetStoreUI.IsWorldPointInsideAnyDesignatedHouseLotXZ(world))
            return world;

        WallDrawInput di = FindFirstObjectByType<WallDrawInput>();
        if (di == null || !di.TryGetMainGridLatticeStepXZ(out float st, out Vector2 o))
            return world;

        float fine = st / Mathf.Max(1.1f, di.interiorFineGridFinenessMul);
        world.x = Mathf.Round((world.x - o.x) / fine) * fine + o.x;
        world.z = Mathf.Round((world.z - o.y) / fine) * fine + o.y;
        return world;
    }

    bool TryResolveContainingLotEdit(out WallEditShape edit)
    {
        Vector3 mid = transform.TransformPoint(FootprintCenterLocal());
        mid.y = transform.position.y;
        if (RuntimeAssetStoreUI.TryResolveHouseLotEditAtWorldXZ(mid, out edit, out _))
            return true;
        return RuntimeAssetStoreUI.TryResolveHouseLotEditAtWorldXZ(transform.position, out edit, out _);
    }

    void FillFootprintCornersWorld(Vector3[] corners4)
    {
        Quaternion q = transform.rotation;
        Vector3 r = transform.position;
        float hw = halfWidthMeters;
        float run = runLengthMeters;
        corners4[0] = r + q * new Vector3(-hw, 0f, 0f);
        corners4[1] = r + q * new Vector3(hw, 0f, 0f);
        corners4[2] = r + q * new Vector3(-hw, 0f, run);
        corners4[3] = r + q * new Vector3(hw, 0f, run);
    }

    bool IsFootprintFullyInsideLot(WallEditShape edit)
    {
        if (edit == null)
            return false;
        FillFootprintCornersWorld(_footprintCornersScratch);
        for (int i = 0; i < 4; i++)
        {
            if (!edit.ContainsWorldPointInClosedLotFootprintXZ(_footprintCornersScratch[i], 0f))
                return false;
        }

        return true;
    }

    void ClampFootprintTransformTowardLot(WallEditShape edit)
    {
        if (edit == null)
            return;
        _lotRingScratch.Clear();
        if (!edit.TryGetClosedLotFootprintRingXZ(_lotRingScratch) || _lotRingScratch.Count < 3)
            return;

        float sx = 0f, sz = 0f;
        for (int i = 0; i < _lotRingScratch.Count; i++)
        {
            sx += _lotRingScratch[i].x;
            sz += _lotRingScratch[i].y;
        }

        float inv = 1f / _lotRingScratch.Count;
        Vector2 lotC = new Vector2(sx * inv, sz * inv);

        for (int iter = 0; iter < 96; iter++)
        {
            if (IsFootprintFullyInsideLot(edit))
                return;

            Vector3 push = Vector3.zero;
            FillFootprintCornersWorld(_footprintCornersScratch);
            for (int i = 0; i < 4; i++)
            {
                if (edit.ContainsWorldPointInClosedLotFootprintXZ(_footprintCornersScratch[i], 0f))
                    continue;
                Vector2 c = new Vector2(_footprintCornersScratch[i].x, _footprintCornersScratch[i].z);
                Vector2 d = lotC - c;
                if (d.sqrMagnitude < 1e-12f)
                    continue;
                d.Normalize();
                push.x += d.x;
                push.z += d.y;
            }

            if (push.sqrMagnitude < 1e-14f)
                return;

            push.Normalize();
            transform.position += new Vector3(push.x, 0f, push.z) * 0.04f;
        }
    }

    void ClampFootprintTransformTowardLotAfterMutation()
    {
        if (!TryResolveContainingLotEdit(out WallEditShape edit))
            return;
        ClampFootprintTransformTowardLot(edit);
        RebuildGeometry();
    }
}
