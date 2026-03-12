// FieldPlacer_NoLayers.cs
// Unity 2022+
//
// Build Mode field drawing WITHOUT LayerMasks.
// - Toggle BuildMode: B
// - Add points: LMB click (GroundMarker via RaycastAll - selects nearest GroundMarker hit even if field colliders are hit first)
// - Auto-add points while dragging: optional (autoAddPointsWhileDragging + autoPointSpacing)
// - Preview: smoothPreview subdivision (previewMaxSegmentLength) + optional terrain conform (conformPreviewToTerrain)
// - Finish: Enter/Return (always works). Optional autoCloseOnFinish.
// - Cancel: Escape
// - Undo: Backspace
// - Clear: Delete
//
// Validations:
// - minPoints, maxPoints
// - minDistanceBetweenPoints (also for auto-add)
// - rejectSelfIntersection (tolerant intersection check: ignores shared endpoints, ignores colinear/touch cases)
// - minArea
//
// NO Physics LayerMasks. Ground is detected by GroundMarker component.
// Logs precise warnings when clicks invalid / refs missing / inputs ignored.
// Instantiates fieldPrefab (FieldArea) and calls SetPoints(pointsCopy).
//
// IMPORTANT: This script does NOT assume any UI framework; optionally ignores clicks over UI using EventSystem.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class FieldPlacer_NoLayers : MonoBehaviour
{
    [Header("Build Mode")]
    public bool buildMode = false;
    public KeyCode toggleBuildModeKey = KeyCode.B;

    [Header("Finish / Cancel")]
    public KeyCode finishKey = KeyCode.Return;      // Return
    public KeyCode finishKeyAlt = KeyCode.KeypadEnter;
    public KeyCode cancelKey = KeyCode.Escape;
    public KeyCode undoKey = KeyCode.Backspace;
    public KeyCode clearKey = KeyCode.Delete;

    [Tooltip("If true, BuildMode will automatically turn off after a field is finished.")]
    public bool autoCloseOnFinish = true;

    [Header("Placement")]
    [Tooltip("Prefab with FieldArea component.")]
    public FieldArea fieldPrefab;

    [Tooltip("Parent transform for instantiated fields (optional).")]
    public Transform fieldParent;

    [Tooltip("Extra Y offset for preview line (helps avoid z-fighting).")]
    public float previewHeightOffset = 0.02f;

    [Header("UI")]
    [Tooltip("If true, ignore clicks when pointer is over UI (EventSystem required).")]
    public bool ignoreClicksOverUI = true;

    [Header("Point Constraints")]
    public int minPoints = 3;
    public int maxPoints = 128;

    [Tooltip("Minimum distance between consecutive points in WORLD XZ.")]
    public float minDistanceBetweenPoints = 0.25f;

    [Tooltip("Reject polygons that self-intersect.")]
    public bool rejectSelfIntersection = true;

    [Tooltip("Minimum area in WORLD XZ required to finish.")]
    public float minArea = 0.5f;

    [Header("Auto Points While Dragging")]
    public bool autoAddPointsWhileDragging = true;

    [Tooltip("World meters: distance from last point before automatically adding a new point while holding LMB.")]
    public float autoPointSpacing = 0.5f;

    [Header("Preview Smoothing")]
    public bool smoothPreview = true;

    [Tooltip("Max segment length for preview line subdivision in WORLD XZ.")]
    public float previewMaxSegmentLength = 0.5f;

    [Header("Preview Terrain Conform")]
    public bool conformPreviewToTerrain = true;

    [Tooltip("Ray origin height above the preview point for downward raycast sampling.")]
    public float previewConformRayHeight = 50f;

    [Header("Ground Sampling (clicks)")]
    [Tooltip("Ray origin height above camera ray for robust sampling.")]
    public float clickRayMaxDistance = 500f;

    [Header("Preview Renderer")]
    public LineRenderer previewOutline;

    [Tooltip("If previewOutline is null, it will be created automatically at runtime.")]
    public bool autoCreatePreviewLineRenderer = true;

    [Header("Debug")]
    public bool debugLogs = false;

    // Internal state
    private readonly List<Vector3> _points = new List<Vector3>(256);

    private bool _lmbDown = false;
    private Vector3 _lastAutoPoint = Vector3.positiveInfinity;

    // Double click support (optional)
    [Header("Optional Double Click Finish")]
    public bool enableDoubleClickFinish = false;
    public float doubleClickMaxTime = 0.25f;
    public float doubleClickMaxDistance = 0.25f;

    private float _lastClickTime = -999f;
    private Vector3 _lastClickPos = Vector3.positiveInfinity;

    private void Awake()
    {
        if (autoCreatePreviewLineRenderer && previewOutline == null)
        {
            CreatePreviewLineRenderer();
        }

        ValidateSettings();
        UpdatePreview();
    }

    private void OnValidate()
    {
        ValidateSettings();
        if (previewOutline != null)
        {
            previewOutline.useWorldSpace = true;
            previewOutline.loop = false;
        }
    }

    private void ValidateSettings()
    {
        minPoints = Mathf.Max(3, minPoints);
        maxPoints = Mathf.Max(minPoints, maxPoints);

        minDistanceBetweenPoints = Mathf.Max(0.001f, minDistanceBetweenPoints);
        autoPointSpacing = Mathf.Max(0.001f, autoPointSpacing);
        previewMaxSegmentLength = Mathf.Max(0.001f, previewMaxSegmentLength);
        previewConformRayHeight = Mathf.Max(1f, previewConformRayHeight);
        clickRayMaxDistance = Mathf.Max(1f, clickRayMaxDistance);

        if (fieldPrefab == null && debugLogs)
            Debug.LogWarning("[FieldPlacer_NoLayers] fieldPrefab is not assigned. Finish() will fail.");
    }

    private void Update()
    {
        // Toggle Build Mode
        if (Input.GetKeyDown(toggleBuildModeKey))
        {
            buildMode = !buildMode;
            if (debugLogs)
                Debug.Log($"[FieldPlacer_NoLayers] BuildMode toggled: {buildMode}");

            if (!buildMode)
            {
                // optional: keep points? usually clear when leaving
                // We'll keep points so user can toggle back. But you can choose to clear.
                // ClearPoints();
                UpdatePreview();
            }
        }

        if (!buildMode)
            return;

        HandleKeyboard();
        HandleMouseInput();
        UpdatePreview();
    }

    private void HandleKeyboard()
    {
        // Finish always works when in build mode
        if (Input.GetKeyDown(finishKey) || Input.GetKeyDown(finishKeyAlt))
        {
            if (debugLogs)
                Debug.Log($"[FieldPlacer_NoLayers] Finish key pressed. points={_points.Count} buildMode={buildMode}");
            Finish();
        }

        if (Input.GetKeyDown(cancelKey))
        {
            if (debugLogs)
                Debug.Log("[FieldPlacer_NoLayers] Cancel (Esc) pressed.");
            Cancel();
        }

        if (Input.GetKeyDown(undoKey))
        {
            UndoLastPoint();
        }

        if (Input.GetKeyDown(clearKey))
        {
            ClearPoints();
        }
    }

    private void HandleMouseInput()
    {
        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (Input.GetMouseButtonDown(0) && debugLogs)
                Debug.LogWarning("[FieldPlacer_NoLayers] Click ignored: pointer is over UI.");
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            _lmbDown = true;

            if (TryGetGroundPoint(out Vector3 hitPoint))
            {
                // Double click finish optional
                if (enableDoubleClickFinish && _points.Count >= minPoints)
                {
                    float t = Time.time;
                    float dt = t - _lastClickTime;
                    float dist = (new Vector2(hitPoint.x, hitPoint.z) - new Vector2(_lastClickPos.x, _lastClickPos.z)).magnitude;

                    if (dt <= doubleClickMaxTime && dist <= doubleClickMaxDistance)
                    {
                        if (debugLogs)
                            Debug.Log("[FieldPlacer_NoLayers] Double click detected -> Finish()");
                        Finish();
                        _lastClickTime = -999f;
                        _lastClickPos = Vector3.positiveInfinity;
                        _lmbDown = false;
                        return;
                    }

                    _lastClickTime = t;
                    _lastClickPos = hitPoint;
                }

                TryAddPoint(hitPoint, isAutoPoint: false);
                _lastAutoPoint = hitPoint;
            }
            else
            {
                Debug.LogWarning("[FieldPlacer_NoLayers] Click rejected: no GroundMarker hit found (RaycastAll).");
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            _lmbDown = false;
        }

        // Auto-add while dragging / holding mouse
        if (autoAddPointsWhileDragging && _lmbDown)
        {
            if (_points.Count == 0)
                return;

            if (!TryGetGroundPoint(out Vector3 hitPoint))
                return;

            float dist = DistanceXZ(_points[_points.Count - 1], hitPoint);
            if (dist < autoPointSpacing)
                return;

            // Also enforce minDistanceBetweenPoints to avoid spam
            if (dist < minDistanceBetweenPoints)
                return;

            // Optional backtracking guard (very common cause of self-intersection during dragging)
            if (IsBacktracking(_points, hitPoint))
            {
                if (debugLogs)
                    Debug.LogWarning("[FieldPlacer_NoLayers] AutoPoint skipped: backtracking would likely self-intersect.");
                return;
            }

            TryAddPoint(hitPoint, isAutoPoint: true);
            _lastAutoPoint = hitPoint;
        }
    }

    private bool TryAddPoint(Vector3 worldPoint, bool isAutoPoint)
    {
        if (_points.Count >= maxPoints)
        {
            Debug.LogWarning($"[FieldPlacer_NoLayers] Point rejected: maxPoints reached ({maxPoints}).");
            return false;
        }

        // If we already have points, ensure minimum distance
        if (_points.Count > 0)
        {
            float d = DistanceXZ(_points[_points.Count - 1], worldPoint);
            if (d < minDistanceBetweenPoints)
            {
                if (debugLogs)
                    Debug.LogWarning($"[FieldPlacer_NoLayers] Point rejected: too close to last point (dXZ={d:0.###} < minDistance={minDistanceBetweenPoints:0.###}).");
                return false;
            }
        }

        // Self-intersection check (tolerant)
        if (rejectSelfIntersection && WouldSelfIntersect(worldPoint))
        {
            // This is the warning you saw
            Debug.LogWarning($"[FieldPlacer_NoLayers] Point rejected: would create self-intersection with segment [{_lastIntersectSegA}->{_lastIntersectSegB}].");
            return false;
        }

        _points.Add(worldPoint);

        if (debugLogs)
        {
            string kind = isAutoPoint ? "AutoPoint" : "ClickPoint";
            Debug.Log($"[FieldPlacer_NoLayers] {kind} added. count={_points.Count} point=({worldPoint.x:0.###},{worldPoint.y:0.###},{worldPoint.z:0.###})");
        }

        return true;
    }

    private void Finish()
    {
        if (_points.Count < minPoints)
        {
            Debug.LogWarning($"[FieldPlacer_NoLayers] Finish rejected: not enough points ({_points.Count}/{minPoints}).");
            return;
        }

        float area = ComputeAreaXZ(_points);
        if (area < minArea)
        {
            Debug.LogWarning($"[FieldPlacer_NoLayers] Finish rejected: area too small ({area:0.###} < minArea={minArea:0.###}).");
            return;
        }

        if (fieldPrefab == null)
        {
            Debug.LogWarning("[FieldPlacer_NoLayers] Finish failed: fieldPrefab is null.");
            return;
        }

        // Instantiate field
        FieldArea field = null;
        try
        {
            FieldArea inst = Instantiate(fieldPrefab, Vector3.zero, Quaternion.identity, fieldParent);
            inst.name = $"{fieldPrefab.name}_{DateTime.Now:HHmmss}";
            field = inst;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FieldPlacer_NoLayers] Finish failed: could not instantiate fieldPrefab. {e.Message}");
            return;
        }

        // Send points copy (keep exact click Y)
        var pointsCopy = new List<Vector3>(_points);
        field.SetPoints(pointsCopy);

        if (debugLogs)
            Debug.Log($"[FieldPlacer_NoLayers] Field created '{field.name}' with {pointsCopy.Count} points. BuildMode={buildMode}");

        // Reset
        ClearPoints();

        if (autoCloseOnFinish)
        {
            buildMode = false;
            if (debugLogs)
                Debug.Log("[FieldPlacer_NoLayers] AutoCloseOnFinish -> BuildMode OFF");
        }
    }

    private void Cancel()
    {
        ClearPoints();
        // Keep build mode ON (user can continue) – you can switch to OFF if preferred
        if (debugLogs)
            Debug.Log("[FieldPlacer_NoLayers] Cancel: points cleared.");
    }

    private void UndoLastPoint()
    {
        if (_points.Count == 0)
        {
            if (debugLogs)
                Debug.LogWarning("[FieldPlacer_NoLayers] Undo ignored: no points.");
            return;
        }

        _points.RemoveAt(_points.Count - 1);

        if (debugLogs)
            Debug.Log($"[FieldPlacer_NoLayers] Undo: removed last point. count={_points.Count}");
    }

    private void ClearPoints()
    {
        _points.Clear();
        _lastAutoPoint = Vector3.positiveInfinity;

        if (previewOutline != null)
            previewOutline.positionCount = 0;
    }

    // -----------------------------
    // Preview rendering
    // -----------------------------

    private void UpdatePreview()
    {
        if (previewOutline == null)
            return;

        if (!buildMode || _points.Count == 0)
        {
            previewOutline.positionCount = 0;
            return;
        }

        // Build preview points: raw + optional closing segment to current mouse point
        var previewPts = new List<Vector3>(_points.Count + 16);
        previewPts.AddRange(_points);

        // Add current mouse point as last preview point (ghost) if possible
        if (TryGetGroundPoint(out Vector3 mousePt))
        {
            // only if far enough to avoid jitter
            if (_points.Count == 0 || DistanceXZ(_points[_points.Count - 1], mousePt) > 0.01f)
                previewPts.Add(mousePt);
        }

        // Smooth preview (subdivide segments) in XZ
        if (smoothPreview && previewPts.Count >= 2)
        {
            previewPts = SubdividePolylineXZ(previewPts, previewMaxSegmentLength);
        }

        // Conform preview to terrain (Y) per point
        if (conformPreviewToTerrain)
        {
            for (int i = 0; i < previewPts.Count; i++)
            {
                Vector3 p = previewPts[i];
                if (TrySampleGroundY(p.x, p.z, p.y + previewConformRayHeight, previewConformRayHeight * 2f, out float y))
                {
                    p.y = y + previewHeightOffset;
                }
                else
                {
                    if (debugLogs)
                        Debug.LogWarning("[FieldPlacer_NoLayers] Preview conform: no GroundMarker found under a preview point.");
                    p.y += previewHeightOffset;
                }
                previewPts[i] = p;
            }
        }
        else
        {
            for (int i = 0; i < previewPts.Count; i++)
            {
                var p = previewPts[i];
                p.y += previewHeightOffset;
                previewPts[i] = p;
            }
        }

        // Draw
        previewOutline.useWorldSpace = true;
        previewOutline.loop = false;

        previewOutline.positionCount = previewPts.Count;
        for (int i = 0; i < previewPts.Count; i++)
            previewOutline.SetPosition(i, previewPts[i]);
    }

    private List<Vector3> SubdividePolylineXZ(List<Vector3> pts, float maxSegLen)
    {
        if (pts == null || pts.Count < 2)
            return pts;

        float maxLen = Mathf.Max(0.001f, maxSegLen);
        var outPts = new List<Vector3>(pts.Count * 2);

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];
            outPts.Add(a);

            float dist = DistanceXZ(a, b);
            if (dist <= maxLen)
                continue;

            int segments = Mathf.CeilToInt(dist / maxLen);
            segments = Mathf.Clamp(segments, 1, 256);

            for (int s = 1; s < segments; s++)
            {
                float t = (float)s / segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                outPts.Add(p);
            }
        }

        outPts.Add(pts[pts.Count - 1]);
        return outPts;
    }

    private void CreatePreviewLineRenderer()
    {
        var go = new GameObject("FieldPlacer_PreviewLine");
        go.transform.SetParent(transform, false);

        previewOutline = go.AddComponent<LineRenderer>();
        previewOutline.useWorldSpace = true;
        previewOutline.loop = false;
        previewOutline.positionCount = 0;

        // Minimal defaults (user can override in inspector)
        previewOutline.widthMultiplier = 0.05f;
        previewOutline.numCornerVertices = 4;
        previewOutline.numCapVertices = 4;
        previewOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewOutline.receiveShadows = false;
    }

    // -----------------------------
    // Ground detection (NO LayerMasks)
    // -----------------------------

    private bool TryGetGroundPoint(out Vector3 point)
    {
        point = Vector3.zero;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[FieldPlacer_NoLayers] No Camera.main found. Cannot raycast for ground point.");
            return false;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, clickRayMaxDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        // Sort by distance and choose nearest hit that has GroundMarker in parent
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null)
                continue;

            if (h.collider.GetComponentInParent<GroundMarker>() == null)
                continue;

            point = h.point;
            return true;
        }

        return false;
    }

    private bool TrySampleGroundY(float x, float z, float originY, float dist, out float y)
    {
        y = originY;

        Vector3 origin = new Vector3(x, originY, z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            if (h.collider.GetComponentInParent<GroundMarker>() == null) continue;

            y = h.point.y;
            return true;
        }

        return false;
    }

    // -----------------------------
    // Self-intersection detection (tolerant)
    // -----------------------------

    private int _lastIntersectSegA = -1;
    private int _lastIntersectSegB = -1;

    private bool WouldSelfIntersect(Vector3 newPoint)
    {
        _lastIntersectSegA = -1;
        _lastIntersectSegB = -1;

        if (_points.Count < 2)
            return false;

        Vector3 newA = _points[_points.Count - 1];
        Vector3 newB = newPoint;

        // Check against existing segments [i -> i+1], excluding the segment that shares endpoints with new segment.
        for (int i = 0; i < _points.Count - 2; i++)
        {
            Vector3 sA = _points[i];
            Vector3 sB = _points[i + 1];

            if (SegmentsIntersectStrictXZ(newA, newB, sA, sB, 0.01f))
            {
                _lastIntersectSegA = i;
                _lastIntersectSegB = i + 1;
                return true;
            }
        }

        return false;
    }

    private bool SegmentsIntersectStrictXZ(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2, float epsilon)
    {
        Vector2 A1 = new Vector2(a1.x, a1.z);
        Vector2 A2 = new Vector2(a2.x, a2.z);
        Vector2 B1 = new Vector2(b1.x, b1.z);
        Vector2 B2 = new Vector2(b2.x, b2.z);

        float epsSqr = epsilon * epsilon;

        // Shared endpoints => ignore
        if ((A1 - B1).sqrMagnitude <= epsSqr) return false;
        if ((A1 - B2).sqrMagnitude <= epsSqr) return false;
        if ((A2 - B1).sqrMagnitude <= epsSqr) return false;
        if ((A2 - B2).sqrMagnitude <= epsSqr) return false;

        return LinesIntersect2D(A1, A2, B1, B2, epsilon);
    }

    private bool LinesIntersect2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float eps)
    {
        float o1 = Orientation(p1, p2, p3);
        float o2 = Orientation(p1, p2, p4);
        float o3 = Orientation(p3, p4, p1);
        float o4 = Orientation(p3, p4, p2);

        bool proper =
            ((o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps)) &&
            ((o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps));

        if (proper)
            return true;

        // Colinear/touching cases treated as NOT intersecting (tolerant)
        return false;
    }

    private float Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private bool IsBacktracking(List<Vector3> pts, Vector3 candidate, float minDot = -0.3f)
    {
        if (pts == null || pts.Count < 2) return false;

        Vector3 a = pts[pts.Count - 2];
        Vector3 b = pts[pts.Count - 1];

        Vector2 dir1 = new Vector2(b.x - a.x, b.z - a.z);
        Vector2 dir2 = new Vector2(candidate.x - b.x, candidate.z - b.z);

        if (dir1.sqrMagnitude < 1e-6f || dir2.sqrMagnitude < 1e-6f)
            return false;

        dir1.Normalize();
        dir2.Normalize();

        float dot = Vector2.Dot(dir1, dir2);
        return dot < minDot;
    }

    // -----------------------------
    // Geometry helpers
    // -----------------------------

    private float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private float ComputeAreaXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 3)
            return 0f;

        double sum = 0.0;
        int n = pts.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = pts[i].x;
            double zi = pts[i].z;
            double xj = pts[j].x;
            double zj = pts[j].z;
            sum += (xj * zi) - (xi * zj);
        }

        return (float)(Math.Abs(sum) * 0.5);
    }
}