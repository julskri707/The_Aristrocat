using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Porte / fenêtre catalogue : ouverture dans <see cref="WallOpeningRegistry"/> + 5 poignées (rectangle sur le mur : 4 coins + centre vert).
/// Molette sur le centre = échange largeur / hauteur (équivalent rotation 90° du rectangle sur le mur). Molette sur un coin = zoom uniforme de l’ouverture.
/// </summary>
[DisallowMultipleComponent]
public class PlacedWallOpeningManipulator : MonoBehaviour, IControlPointProvider, IControlPointDragPlaneProvider,
    IControlPointWallShapeBinding
{
    public bool ControlPointsBelongToWallShape => false;

    const float WheelResizeStep = 1.035f;
    const float MinTSpan = 0.03f;
    const float MinHSpan = 0.03f;
    const float MinOpeningAlongMeters = 0.28f;
    const float MinOpeningHeightMeters = 0.35f;

    [SerializeField] WallObject wall;
    [SerializeField] WallOpeningRegistry registry;
    [SerializeField] int entryIndex = -1;
    [SerializeField] WallOpeningKind kind;

    [SerializeField] Transform decorOuter;
    [SerializeField] Transform decorInner;

    [SerializeField] float decorScale = 1.65f;
    [SerializeField] float wallSurfaceOffset = 0.04f;
    [SerializeField] float doorDecorProtrusionExtraMeters = 0.14f;
    [SerializeField] float windowDecorProtrusionExtraMeters = 0.04f;

    [SerializeField] float floorYWorld;
    [SerializeField] float windowMinClearanceFromFloorMeters = 1f;
    [SerializeField] float windowMinClearanceFromCeilingMeters = 0.5f;

    [SerializeField] float doorRefWidthMeters = 0.9f;
    [SerializeField] float doorRefHeightMeters = 2.1f;

    Vector3 _decorOuterInitialScale = Vector3.one;
    Vector3 _decorInnerInitialScale = Vector3.one;

    ControlPointOverlayManager _cachedOverlay;

    public int ControlPointCount => 5;

    public void Initialize(
        WallObject wallObj,
        WallOpeningRegistry reg,
        int idx,
        WallOpeningKind k,
        Transform outer,
        Transform inner,
        float decorScaleValue,
        float wallSurfOff,
        float doorExtra,
        float windowExtra,
        float floorYW,
        float winFloorClr,
        float winCeilClr,
        Vector3 wallNormalAtPlacement,
        float doorRefWidthUnscaled,
        float doorRefHeightUnscaled)
    {
        wall = wallObj;
        registry = reg;
        entryIndex = idx;
        kind = k;
        decorOuter = outer;
        decorInner = inner;
        decorScale = decorScaleValue;
        wallSurfaceOffset = wallSurfOff;
        doorDecorProtrusionExtraMeters = doorExtra;
        windowDecorProtrusionExtraMeters = windowExtra;
        floorYWorld = floorYW;
        windowMinClearanceFromFloorMeters = winFloorClr;
        windowMinClearanceFromCeilingMeters = winCeilClr;
        doorRefWidthMeters = doorRefWidthUnscaled;
        doorRefHeightMeters = doorRefHeightUnscaled;

        if (decorOuter != null)
            _decorOuterInitialScale = decorOuter.localScale;
        if (decorInner != null)
            _decorInnerInitialScale = decorInner.localScale;

        if (!TryReadEntry(out WallOpeningEntry e))
            return;

        TryComputeOutwardSign(e.segmentIndex, wallNormalAtPlacement);
        RefreshVisualsAndWall();
    }

    float _outwardSign = 1f;

    void TryComputeOutwardSign(int segmentIndex, Vector3 wallNormalHint)
    {
        if (!TrySegmentTangent(segmentIndex, out Vector3 tang))
        {
            _outwardSign = 1f;
            return;
        }

        Vector3 cross = Vector3.Cross(Vector3.up, tang).normalized;
        _outwardSign = Vector3.Dot(cross, wallNormalHint) >= 0f ? 1f : -1f;
    }

    bool IsOverlayTargetThis()
    {
        if (_cachedOverlay == null)
            _cachedOverlay = FindFirstObjectByType<ControlPointOverlayManager>();
        return _cachedOverlay != null && _cachedOverlay.targetProviderBehaviour == this;
    }

    void Update()
    {
        if (!IsOverlayTargetThis())
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;
        if (ControlPointHandleUI.SelectedProvider == (IControlPointProvider)this &&
            ControlPointHandleUI.SelectedIndex == 4)
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-6f)
            return;

        float factor = scroll > 0f ? WheelResizeStep : 1f / WheelResizeStep;
        ApplyUniformOpeningScale(factor);
    }

    public void ApplyCenterWheelQuarterTurn()
    {
        if (!TryReadEntry(out WallOpeningEntry e))
            return;
        if (!TrySegmentLength(e.segmentIndex, out float segLen))
            return;

        float wallH = Mathf.Max(0.1f, wall.height);
        float wAlong = (e.t1 - e.t0) * segLen;
        float hWorld = (e.h1 - e.h0) * wallH;
        float tMid = (e.t0 + e.t1) * 0.5f;
        float hMid = (e.h0 + e.h1) * 0.5f;
        float newWAlong = hWorld;
        float newHWorld = wAlong;
        float halfTnorm = (newWAlong * 0.5f) / Mathf.Max(segLen, 0.01f);
        float halfHnorm = (newHWorld * 0.5f) / Mathf.Max(wallH, 0.01f);
        e.t0 = tMid - halfTnorm;
        e.t1 = tMid + halfTnorm;
        e.h0 = hMid - halfHnorm;
        e.h1 = hMid + halfHnorm;
        SanitizeAndClampEntry(ref e);
        CommitEntry(e);
    }

    void ApplyUniformOpeningScale(float factor)
    {
        if (!TryReadEntry(out WallOpeningEntry e))
            return;
        float tMid = (e.t0 + e.t1) * 0.5f;
        float hMid = (e.h0 + e.h1) * 0.5f;
        float halfT = (e.t1 - e.t0) * 0.5f * factor;
        float halfH = (e.h1 - e.h0) * 0.5f * factor;
        e.t0 = tMid - halfT;
        e.t1 = tMid + halfT;
        e.h0 = hMid - halfH;
        e.h1 = hMid + halfH;
        SanitizeAndClampEntry(ref e);
        CommitEntry(e);
    }

    bool TryReadEntry(out WallOpeningEntry e)
    {
        e = default;
        if (wall == null || registry == null || entryIndex < 0)
            return false;
        return registry.TryGetEntry(entryIndex, out e);
    }

    void CommitEntry(WallOpeningEntry e)
    {
        if (registry == null || entryIndex < 0)
            return;
        registry.SetEntry(entryIndex, e);
        wall.ForceRebuildMesh();
        RefreshVisualsAndWall();
    }

    void SanitizeAndClampEntry(ref WallOpeningEntry e)
    {
        float wallH = Mathf.Max(0.1f, wall.height);
        if (!TrySegmentLength(e.segmentIndex, out float segLen))
            return;

        float tLo = Mathf.Clamp01(Mathf.Min(e.t0, e.t1));
        float tHi = Mathf.Clamp01(Mathf.Max(e.t0, e.t1));
        float hLo = Mathf.Clamp01(Mathf.Min(e.h0, e.h1));
        float hHi = Mathf.Clamp01(Mathf.Max(e.h0, e.h1));

        float minNormAlong = MinOpeningAlongMeters / Mathf.Max(segLen, 0.01f);
        float minNormH = MinOpeningHeightMeters / Mathf.Max(wallH, 0.01f);
        minNormAlong = Mathf.Clamp(minNormAlong, MinTSpan, 0.95f);
        minNormH = Mathf.Clamp(minNormH, MinHSpan, 0.95f);

        if (tHi - tLo < minNormAlong)
        {
            float mid = (tLo + tHi) * 0.5f;
            float half = minNormAlong * 0.5f;
            tLo = Mathf.Clamp01(mid - half);
            tHi = Mathf.Clamp01(mid + half);
            if (tHi - tLo < minNormAlong)
            {
                tHi = Mathf.Clamp01(tLo + minNormAlong);
            }
        }

        if (hHi - hLo < minNormH)
        {
            float mid = (hLo + hHi) * 0.5f;
            float half = minNormH * 0.5f;
            hLo = Mathf.Clamp01(mid - half);
            hHi = Mathf.Clamp01(mid + half);
            if (hHi - hLo < minNormH)
                hHi = Mathf.Clamp01(hLo + minNormH);
        }

        if (kind == WallOpeningKind.Window)
            ClampWindowVerticalFractions(ref hLo, ref hHi, wallH);

        e.t0 = tLo;
        e.t1 = tHi;
        e.h0 = hLo;
        e.h1 = hHi;
    }

    void ClampWindowVerticalFractions(ref float hLo, ref float hHi, float wallH)
    {
        float halfWinM = (hHi - hLo) * wallH * 0.5f;
        float minCy = floorYWorld + windowMinClearanceFromFloorMeters + halfWinM;
        float maxCy = floorYWorld + wallH - windowMinClearanceFromCeilingMeters - halfWinM;
        if (maxCy < minCy + 0.05f)
            return;

        float cy = (hLo + hHi) * 0.5f * wallH + floorYWorld;
        cy = Mathf.Clamp(cy, minCy, maxCy);
        float centerFrac = (cy - floorYWorld) / wallH;
        float halfFrac = halfWinM / wallH;
        hLo = Mathf.Clamp01(centerFrac - halfFrac);
        hHi = Mathf.Clamp01(centerFrac + halfFrac);
    }

    void ClampOpeningAlongSegment(ref WallOpeningEntry e)
    {
        if (!TrySegmentLength(e.segmentIndex, out float sl))
            return;
        float minNormAlong = Mathf.Clamp(MinOpeningAlongMeters / Mathf.Max(sl, 0.01f), MinTSpan, 0.95f);
        float tLo = Mathf.Clamp01(Mathf.Min(e.t0, e.t1));
        float tHi = Mathf.Clamp01(Mathf.Max(e.t0, e.t1));
        if (tLo < minNormAlong * 0.5f)
        {
            float span = tHi - tLo;
            tLo = 0f;
            tHi = Mathf.Clamp01(tLo + Mathf.Max(span, minNormAlong));
        }

        if (tHi > 1f - minNormAlong * 0.5f)
        {
            float span = Mathf.Max(tHi - tLo, minNormAlong);
            tHi = 1f;
            tLo = Mathf.Clamp01(tHi - span);
        }

        e.t0 = tLo;
        e.t1 = tHi;
    }

    public Vector3 GetControlPointWorld(int index)
    {
        if (!TryReadEntry(out WallOpeningEntry e))
            return transform.position;
        if (!TryOpeningBasis(e, out OpeningBasis b))
            return transform.position;

        float t0 = e.t0;
        float t1 = e.t1;
        float h0 = e.h0;
        float h1 = e.h1;
        switch (index)
        {
            case 0: return CornerWorld(b, t0, h0);
            case 1: return CornerWorld(b, t1, h0);
            case 2: return CornerWorld(b, t1, h1);
            case 3: return CornerWorld(b, t0, h1);
            case 4: return CornerWorld(b, (t0 + t1) * 0.5f, (h0 + h1) * 0.5f);
            default: return transform.position;
        }
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        if (!TryReadEntry(out WallOpeningEntry e))
            return;
        if (!TryOpeningBasis(e, out OpeningBasis b))
            return;

        if (!WorldToNormalizedOnOpening(worldPos, b, out float f, out float hv))
            return;

        float t0 = e.t0;
        float t1 = e.t1;
        float h0 = e.h0;
        float h1 = e.h1;

        if (index == 4)
        {
            float tMid = (t0 + t1) * 0.5f;
            float hMid = (h0 + h1) * 0.5f;
            float dt = f - tMid;
            float dh = hv - hMid;
            t0 = Mathf.Clamp01(t0 + dt);
            t1 = Mathf.Clamp01(t1 + dt);
            h0 = Mathf.Clamp01(h0 + dh);
            h1 = Mathf.Clamp01(h1 + dh);
        }
        else
        {
            switch (index)
            {
                case 0:
                    t0 = Mathf.Min(f, t1 - MinTSpan);
                    h0 = Mathf.Min(hv, h1 - MinHSpan);
                    break;
                case 1:
                    t1 = Mathf.Max(f, t0 + MinTSpan);
                    h0 = Mathf.Min(hv, h1 - MinHSpan);
                    break;
                case 2:
                    t1 = Mathf.Max(f, t0 + MinTSpan);
                    h1 = Mathf.Max(hv, h0 + MinHSpan);
                    break;
                case 3:
                    t0 = Mathf.Min(f, t1 - MinTSpan);
                    h1 = Mathf.Max(hv, h0 + MinHSpan);
                    break;
            }
        }

        e.t0 = Mathf.Clamp01(Mathf.Min(t0, t1));
        e.t1 = Mathf.Clamp01(Mathf.Max(t0, t1));
        e.h0 = Mathf.Clamp01(Mathf.Min(h0, h1));
        e.h1 = Mathf.Clamp01(Mathf.Max(h0, h1));
        SanitizeAndClampEntry(ref e);
        ClampOpeningAlongSegment(ref e);
        CommitEntry(e);
    }

    public bool IsControlPointEditable(int index) => index >= 0 && index < 5;

    public bool TryGetDragPlane(int index, Camera cam, Vector3 startWorld, out Plane plane)
    {
        plane = default;
        if (!TryReadEntry(out WallOpeningEntry e) || !TryOpeningBasis(e, out OpeningBasis b))
            return false;

        Vector3 n = b.outward;
        plane = new Plane(n, startWorld);
        return true;
    }

    struct OpeningBasis
    {
        public Vector3 ob0;
        public Vector3 ob1;
        public Vector3 ot0;
        public Vector3 ot1;
        public Vector3 outward;
        public float segLen;
    }

    bool TryOpeningBasis(WallOpeningEntry e, out OpeningBasis b)
    {
        b = default;
        if (!TryOuterQuad(e.segmentIndex, out b.ob0, out b.ob1, out b.ot0, out b.ot1, out b.outward, out b.segLen))
            return false;
        return true;
    }

    static Vector3 CornerWorld(in OpeningBasis b, float f, float h)
    {
        Vector3 bot = Vector3.Lerp(b.ob0, b.ob1, f);
        Vector3 top = Vector3.Lerp(b.ot0, b.ot1, f);
        return Vector3.Lerp(bot, top, h);
    }

    bool WorldToNormalizedOnOpening(Vector3 world, in OpeningBasis b, out float f, out float h)
    {
        Vector3 n = b.outward;
        Vector3 p = world - n * Vector3.Dot(world - b.ob0, n);
        Vector3 tang = (b.ob1 - b.ob0);
        float sl = tang.magnitude;
        if (sl < 1e-5f)
        {
            f = h = 0f;
            return false;
        }

        tang /= sl;
        Vector3 bot0 = b.ob0;
        float fGuess = Mathf.Clamp01(Vector3.Dot(p - bot0, tang) / Mathf.Max(sl, 1e-5f));
        Vector3 bot = Vector3.Lerp(b.ob0, b.ob1, fGuess);
        Vector3 top = Vector3.Lerp(b.ot0, b.ot1, fGuess);
        float denom = Mathf.Max(top.y - bot.y, 1e-4f);
        h = Mathf.Clamp01((p.y - bot.y) / denom);
        Vector3 bot2 = Vector3.Lerp(b.ob0, b.ob1, fGuess);
        f = fGuess;
        for (int iter = 0; iter < 3; iter++)
        {
            bot = Vector3.Lerp(b.ob0, b.ob1, f);
            top = Vector3.Lerp(b.ot0, b.ot1, f);
            denom = Mathf.Max(top.y - bot.y, 1e-4f);
            h = Mathf.Clamp01((p.y - bot.y) / denom);
            Vector3 q = bot + (top - bot) * h;
            Vector3 residual = p - q;
            float df = Vector3.Dot(residual, tang) / Mathf.Max(sl, 1e-5f);
            f = Mathf.Clamp01(f + df);
        }

        return true;
    }

    bool TrySegmentTangent(int segmentIndex, out Vector3 tangent)
    {
        tangent = Vector3.forward;
        if (wall == null)
            return false;
        int count = wall.ControlPointCount;
        if (count < 2)
            return false;
        int n = wall.closedLoop ? (segmentIndex + 1) % count : segmentIndex + 1;
        if (!wall.closedLoop && segmentIndex >= count - 1)
            return false;
        Vector3 d = wall.GetControlPointWorld(n) - wall.GetControlPointWorld(segmentIndex);
        d.y = 0f;
        if (d.sqrMagnitude < 1e-8f)
            return false;
        tangent = d.normalized;
        return true;
    }

    bool TrySegmentLength(int segmentIndex, out float segLen)
    {
        segLen = 0.01f;
        if (wall == null)
            return false;
        int count = wall.ControlPointCount;
        int n = wall.closedLoop ? (segmentIndex + 1) % count : segmentIndex + 1;
        if (!wall.closedLoop && segmentIndex >= count - 1)
            return false;
        Vector3 p0 = wall.GetControlPointWorld(segmentIndex);
        Vector3 p1 = wall.GetControlPointWorld(n);
        Vector3 d = p1 - p0;
        d.y = 0f;
        segLen = Mathf.Max(d.magnitude, 0.01f);
        return true;
    }

    bool TryOuterQuad(int segmentIndex, out Vector3 ob0, out Vector3 ob1, out Vector3 ot0, out Vector3 ot1,
        out Vector3 outward, out float segLen)
    {
        ob0 = ob1 = ot0 = ot1 = Vector3.zero;
        outward = Vector3.forward;
        segLen = 0.01f;
        if (wall == null)
            return false;

        int count = wall.ControlPointCount;
        if (count < 2)
            return false;

        int n = wall.closedLoop ? (segmentIndex + 1) % count : segmentIndex + 1;
        if (!wall.closedLoop && segmentIndex >= count - 1)
            return false;

        Vector3 p0 = wall.GetControlPointWorld(segmentIndex);
        Vector3 p1 = wall.GetControlPointWorld(n);
        Vector3 d = p1 - p0;
        d.y = 0f;
        segLen = d.magnitude;
        if (segLen < 1e-5f)
            return false;

        Vector3 tang = d / segLen;
        Vector3 cross = Vector3.Cross(Vector3.up, tang).normalized;
        outward = cross * _outwardSign;
        float halfT = Mathf.Max(0.01f, wall.thickness) * 0.5f;
        ob0 = p0 + outward * halfT;
        ob1 = p1 + outward * halfT;
        float wallH = wall.height;
        ot0 = ob0 + Vector3.up * wallH;
        ot1 = ob1 + Vector3.up * wallH;
        return true;
    }

    void RefreshVisualsAndWall()
    {
        if (!TryReadEntry(out WallOpeningEntry e))
            return;
        if (!TryOpeningBasis(e, out OpeningBasis b))
            return;

        float fMid = (e.t0 + e.t1) * 0.5f;
        float hMid = (e.h0 + e.h1) * 0.5f;
        Vector3 openingCenter = CornerWorld(b, fMid, hMid);

        float halfT = Mathf.Max(0.01f, wall.thickness) * 0.5f;
        float extra = kind == WallOpeningKind.Door ? doorDecorProtrusionExtraMeters : windowDecorProtrusionExtraMeters;
        float off = halfT + wallSurfaceOffset + extra;

        transform.SetPositionAndRotation(openingCenter, Quaternion.LookRotation(b.outward, Vector3.up));

        float segLen = Mathf.Max(b.segLen, 0.01f);
        float wallH = Mathf.Max(0.1f, wall.height);
        float openingW = (e.t1 - e.t0) * segLen;
        float openingH = (e.h1 - e.h0) * wallH;

        if (decorOuter != null)
        {
            decorOuter.localRotation = Quaternion.identity;
            decorOuter.localPosition = Vector3.forward * off;
            ApplyDecorScale(decorOuter, _decorOuterInitialScale, openingW, openingH);
        }

        if (decorInner != null)
        {
            decorInner.localRotation = Quaternion.Euler(0f, 180f, 0f);
            decorInner.localPosition = -Vector3.forward * off;
            ApplyDecorScale(decorInner, _decorInnerInitialScale, openingW, openingH);
        }

        FitPickCollider(openingW, openingH, off);
    }

    void ApplyDecorScale(Transform t, Vector3 initialScaleAfterSpawn, float openingW, float openingH)
    {
        if (t == null)
            return;

        if (kind == WallOpeningKind.Window)
        {
            const float refW = 1.15f;
            const float refH = 0.88f;
            Vector3 mul = new Vector3(
                openingW / (refW * decorScale),
                openingH / (refH * decorScale),
                1f);
            t.localScale = Vector3.Scale(initialScaleAfterSpawn, mul);
        }
        else
        {
            float bw = doorRefWidthMeters * decorScale;
            float bh = doorRefHeightMeters * decorScale;
            Vector3 mul = new Vector3(
                openingW / Mathf.Max(bw, 1e-4f),
                openingH / Mathf.Max(bh, 1e-4f),
                1f);
            t.localScale = Vector3.Scale(initialScaleAfterSpawn, mul);
        }
    }

    void FitPickCollider(float openingW, float openingH, float decorOff)
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
            box = gameObject.AddComponent<BoxCollider>();

        float depth = decorOff * 2f + Mathf.Max(0.05f, wall.thickness);
        box.center = Vector3.zero;
        box.size = new Vector3(Mathf.Max(openingW, 0.15f), Mathf.Max(openingH, 0.15f), depth);
        box.isTrigger = false;
    }
}
