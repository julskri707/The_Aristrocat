using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class WallUndoManager : MonoBehaviour
{
    const int MaxUndoSnapshotsHardCap = 5;

    [Header("References")]
    public WallBuildController buildController;
    public ControlPointOverlayManager overlay;
    public WallContextMenuUI contextMenu;

    [Header("Input")]
    public bool enableUndo = true;
    public bool requireCtrl = true;
    public KeyCode undoKey = KeyCode.Z;

    [Header("Stack")]
    [Range(1, MaxUndoSnapshotsHardCap)] public int maxSnapshots = MaxUndoSnapshotsHardCap;
    public bool logDebug = false;
    [Tooltip("Réduit l'empreinte mémoire de l'undo en limitant le nombre de points stockés par path (0 = illimité). " +
             "Les lots fusionnés avec arcs denses nécessitent 0 pour éviter une géométrie fausse après Ctrl+Z.")]
    [Range(0, 512)] public int maxStoredPathPointsPerWall = 0;
    [Tooltip("Au disable/destroy (ex: fermeture/changement de scène), vide la pile undo pour libérer la mémoire.")]
    public bool clearUndoStackOnDisable = true;

    [Header("Stability")]
    public bool suspendCladdingRebuildDuringUndo = true;
    public bool rebuildCladdingAfterUndo = true;
    [Min(1)] public int claddingRebuildBudgetPerFrame = 2;

    private readonly Stack<SceneUndoSnapshot> _undoStack = new Stack<SceneUndoSnapshot>();
    private bool _isRestoring;
    private Coroutine _rebuildCoroutine;

    public bool IsRestoring => _isRestoring;
    public int UndoCount => _undoStack.Count;

    void Awake()
    {
        maxSnapshots = Mathf.Clamp(maxSnapshots, 1, MaxUndoSnapshotsHardCap);

        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();

        if (contextMenu == null)
            contextMenu = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);
    }

    void OnDisable()
    {
        if (!clearUndoStackOnDisable)
            return;

        if (_rebuildCoroutine != null)
        {
            StopCoroutine(_rebuildCoroutine);
            _rebuildCoroutine = null;
        }

        _undoStack.Clear();
    }

    void Update()
    {
        if (!enableUndo || _isRestoring)
            return;

        if (!Input.GetKeyDown(undoKey))
            return;

        if (requireCtrl && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            return;

        Undo();
    }

    public void RecordSnapshot(string reason)
    {
        if (!enableUndo || _isRestoring)
            return;

        SceneUndoSnapshot snapshot = CaptureSceneSnapshot(reason);
        if (snapshot == null)
            return;

        _undoStack.Push(snapshot);

        int maxAllowed = Mathf.Clamp(maxSnapshots, 1, MaxUndoSnapshotsHardCap);
        while (_undoStack.Count > maxAllowed)
            TrimBottom();

        if (logDebug)
            Debug.Log($"[WallUndoManager] Snapshot recorded: {reason} (count={_undoStack.Count})");
    }

    public bool Undo()
    {
        if (_isRestoring || _undoStack.Count == 0)
            return false;

        SceneUndoSnapshot snapshot = _undoStack.Pop();
        RestoreSceneSnapshot(snapshot);

        if (logDebug)
            Debug.Log($"[WallUndoManager] Undo restored: {snapshot.reason}");

        return true;
    }

    void TrimBottom()
    {
        int maxAllowed = Mathf.Clamp(maxSnapshots, 1, MaxUndoSnapshotsHardCap);
        if (_undoStack.Count <= maxAllowed)
            return;

        SceneUndoSnapshot[] arr = _undoStack.ToArray();
        _undoStack.Clear();

        for (int i = arr.Length - 2; i >= 0; i--)
            _undoStack.Push(arr[i]);
    }

    SceneUndoSnapshot CaptureSceneSnapshot(string reason)
    {
        List<WallObject> walls = CollectWallsForUndoSnapshot();
        var wallToSnapshotIndex = new Dictionary<WallObject, int>(walls.Count);
        for (int i = 0; i < walls.Count; i++)
        {
            if (walls[i] != null)
                wallToSnapshotIndex[walls[i]] = i;
        }

        SceneUndoSnapshot snapshot = new SceneUndoSnapshot();
        snapshot.reason = reason;
        snapshot.selectedIndex = -1;

        WallObject selected = buildController != null ? buildController.SelectedWall : null;

        for (int i = 0; i < walls.Count; i++)
        {
            WallObject wall = walls[i];
            if (wall == null)
                continue;

            if (selected == wall)
                snapshot.selectedIndex = snapshot.walls.Count;

            snapshot.walls.Add(CaptureWallSnapshot(wall, wallToSnapshotIndex));
        }

        return snapshot;
    }

    /// <summary>
    /// Murs gérés par le build + tout autre WallObject dans la scène (sinon Ctrl+Z laisse des orphelins qui se superposent au restore).
    /// </summary>
    List<WallObject> CollectWallsForUndoSnapshot()
    {
        var seen = new HashSet<WallObject>();
        var result = new List<WallObject>();

        List<WallObject> managed = GetOrderedWalls();
        for (int i = 0; i < managed.Count; i++)
        {
            WallObject w = managed[i];
            if (w == null || !seen.Add(w))
                continue;
            result.Add(w);
        }

        WallObject[] inScene = FindObjectsByType<WallObject>(FindObjectsSortMode.None);
        var extras = new List<WallObject>();
        for (int i = 0; i < inScene.Length; i++)
        {
            WallObject w = inScene[i];
            if (w == null || !seen.Add(w))
                continue;
            extras.Add(w);
        }

        extras.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        result.AddRange(extras);
        return result;
    }

    WallUndoWallSnapshot CaptureWallSnapshot(WallObject wall, Dictionary<WallObject, int> wallToSnapshotIndex)
    {
        WallUndoWallSnapshot snap = new WallUndoWallSnapshot();

        snap.height = wall.height;
        snap.thickness = wall.thickness;
        snap.closedLoop = wall.closedLoop;
        snap.addCaps = wall.addCaps;
        snap.addBottom = wall.addBottom;
        snap.doubleSided = wall.doubleSided;
        snap.wallMaterial = wall.wallMaterial;
        snap.uvMetersPerU = wall.uvMetersPerU;
        snap.uvMetersPerV = wall.uvMetersPerV;
        if (!snap.hasEditShape)
            snap.path = LimitPathPointCount(wall.Points, wall.closedLoop);

        WallStyleInstance styleInstance = wall.GetComponent<WallStyleInstance>();
        if (styleInstance != null)
            snap.currentStyle = styleInstance.currentStyle;

        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit != null)
        {
            snap.hasEditShape = true;
            snap.editState = CaptureEditShape(edit);
            snap.path = null;

            snap.interiorWallsStayInsideLotSnapshotIndex = -1;
            if (edit.interiorWallsStayInsideLot != null && edit.interiorWallsStayInsideLot.wall != null)
            {
                if (wallToSnapshotIndex != null &&
                    wallToSnapshotIndex.TryGetValue(edit.interiorWallsStayInsideLot.wall, out int lotIdx))
                    snap.interiorWallsStayInsideLotSnapshotIndex = lotIdx;
            }
        }

        HouseExteriorEnvelopeSources envelope = wall.GetComponent<HouseExteriorEnvelopeSources>();
        if (envelope != null && envelope.SourceLotObjects != null && envelope.SourceLotObjects.Count > 0 &&
            wallToSnapshotIndex != null)
        {
            snap.hasEnvelopeSources = true;
            snap.envelopeUseIndependentHandles = envelope.UseIndependentSourceHandlesForHouseEnvelope;
            snap.envelopeSourceWallIndices = new List<int>();
            IReadOnlyList<GameObject> srcGos = envelope.SourceLotObjects;
            for (int si = 0; si < srcGos.Count; si++)
            {
                GameObject go = srcGos[si];
                if (go == null)
                    continue;
                WallObject sw = go.GetComponent<WallObject>();
                if (sw != null && wallToSnapshotIndex.TryGetValue(sw, out int ix))
                    snap.envelopeSourceWallIndices.Add(ix);
            }
        }

        HouseEnvelopeBundledSourceTag bundleTag = wall.GetComponent<HouseEnvelopeBundledSourceTag>();
        if (bundleTag != null && bundleTag.envelopeWall != null && wallToSnapshotIndex != null &&
            wallToSnapshotIndex.TryGetValue(bundleTag.envelopeWall, out int envIx))
            snap.bundledEnvelopeSnapshotIndex = envIx;

        HouseParquetFloor floor = wall.GetComponent<HouseParquetFloor>();
        if (floor != null)
        {
            snap.hasParquetFloor = true;
            snap.parquetMaterial = floor.parquetMaterial;
            snap.parquetUvMetersPerTile = floor.uvMetersPerTile;
            snap.parquetYOffsetAboveBase = floor.yOffsetAboveBase;
        }

        return snap;
    }

    WallEditShapeUndoState CaptureEditShape(WallEditShape edit)
    {
        WallEditShapeUndoState state = new WallEditShapeUndoState();

        state.shapeKind = edit.shapeKind;
        state.minX = edit.minX;
        state.maxX = edit.maxX;
        state.minZ = edit.minZ;
        state.maxZ = edit.maxZ;
        state.shapeY = edit.shapeY;

        state.rectangleOriginXZ = edit.rectangleOriginXZ;
        state.rectangleAxisX = edit.rectangleAxisX;
        state.rectangleAxisY = edit.rectangleAxisY;
        state.rectangleMinX = edit.rectangleMinX;
        state.rectangleMaxX = edit.rectangleMaxX;
        state.rectangleMinY = edit.rectangleMinY;
        state.rectangleMaxY = edit.rectangleMaxY;

        state.ellipseWallResolution = edit.ellipseWallResolution;
        state.ellipseRotationRad = edit.ellipseRotationRad;
        state.centerScrollRotationDegrees = edit.centerScrollRotationDegrees;
        state.triangleControlPoints = edit.triangleControlPoints != null
            ? LimitPathPointCount(edit.triangleControlPoints, closed: true)
            : new List<Vector3>();

        state.arcCenterXZ = edit.arcCenterXZ;
        state.arcRadius = edit.arcRadius;
        state.arcStartRad = edit.arcStartRad;
        state.arcEndRad = edit.arcEndRad;
        state.arcCounterClockwise = edit.arcCounterClockwise;
        state.openArcWallResolution = edit.openArcWallResolution;

        state.closedLoopPrivate = ReflectionGet<bool>(edit, "_closedLoop", false);
        state.freePathWasEdited = ReflectionGet<bool>(edit, "_freePathWasEdited", false);
        state.mergeFootprintUseExactPolyline = ReflectionGet<bool>(edit, "_mergeFootprintUseExactPolyline", false);
        state.closedFreeOrthogonalPolylineMode = ReflectionGet<bool>(edit, "_closedFreeOrthogonalPolylineMode", false);

        state.freeControlPoints = LimitPathPointCount(edit.freeControlPoints, state.closedLoopPrivate);

        List<Vector3> rawPath = ReflectionGet<List<Vector3>>(edit, "_freeRawPath", null);
        state.freeRawPath = rawPath != null ? LimitPathPointCount(rawPath, state.closedLoopPrivate) : new List<Vector3>();

        state.allowVerticalScrollElevation = edit.allowVerticalScrollElevation;
        state.verticalScrollElevationMetersPerWheelUnit = edit.verticalScrollElevationMetersPerWheelUnit;

        return state;
    }

    void RestoreSceneSnapshot(SceneUndoSnapshot snapshot)
    {
        _isRestoring = true;
        bool usedGlobalSuspend = suspendCladdingRebuildDuringUndo;
        if (usedGlobalSuspend)
            WallCladdingGenerator.SetGlobalRebuildSuspended(true);

        List<WallCladdingGenerator> claddingToRebuild = new List<WallCladdingGenerator>(snapshot.walls.Count);

        try
        {
            if (contextMenu != null)
                contextMenu.Close();

            DestroyAllWallObjectsInActiveScenes();

            if (buildController != null)
                buildController.ClearManagedWalls();

            WallObject selectedRestoredWall = null;

            var restoredByIndex = new WallObject[snapshot.walls.Count];

            for (int i = 0; i < snapshot.walls.Count; i++)
            {
                WallUndoWallSnapshot wallSnap = snapshot.walls[i];
                WallObject wall = RestoreWallSnapshot(wallSnap);
                restoredByIndex[i] = wall;
                if (wall == null)
                    continue;

                if (buildController != null)
                    buildController.RegisterExistingWall(wall);

                if (i == snapshot.selectedIndex)
                    selectedRestoredWall = wall;

                WallCladdingGenerator cladding = wall.GetComponent<WallCladdingGenerator>();
                if (cladding != null)
                {
                    cladding.MarkDirty();
                    claddingToRebuild.Add(cladding);
                }
            }

            for (int i = 0; i < snapshot.walls.Count; i++)
            {
                WallUndoWallSnapshot wallSnap = snapshot.walls[i];
                WallObject wall = restoredByIndex[i];
                if (wallSnap == null || wall == null)
                    continue;

                if (wallSnap.interiorWallsStayInsideLotSnapshotIndex < 0 ||
                    wallSnap.interiorWallsStayInsideLotSnapshotIndex >= restoredByIndex.Length)
                    continue;

                WallObject lotWall = restoredByIndex[wallSnap.interiorWallsStayInsideLotSnapshotIndex];
                WallEditShape childEdit = wall.GetComponent<WallEditShape>();
                WallEditShape lotEdit = lotWall != null ? lotWall.GetComponent<WallEditShape>() : null;
                if (childEdit != null && lotEdit != null)
                    childEdit.interiorWallsStayInsideLot = lotEdit;
            }

            for (int i = 0; i < snapshot.walls.Count; i++)
            {
                WallUndoWallSnapshot ws = snapshot.walls[i];
                WallObject wall = restoredByIndex[i];
                if (ws == null || wall == null)
                    continue;

                if (ws.hasEnvelopeSources && ws.envelopeSourceWallIndices != null && ws.envelopeSourceWallIndices.Count > 0)
                {
                    HouseExteriorEnvelopeSources envComp = wall.GetComponent<HouseExteriorEnvelopeSources>();
                    if (envComp == null)
                        envComp = wall.gameObject.AddComponent<HouseExteriorEnvelopeSources>();

                    var srcWalls = new List<WallObject>(ws.envelopeSourceWallIndices.Count);
                    for (int k = 0; k < ws.envelopeSourceWallIndices.Count; k++)
                    {
                        int ix = ws.envelopeSourceWallIndices[k];
                        if (ix >= 0 && ix < restoredByIndex.Length && restoredByIndex[ix] != null)
                            srcWalls.Add(restoredByIndex[ix]);
                    }

                    envComp.RestoreUndoState(ws.envelopeUseIndependentHandles, srcWalls);
                }
            }

            for (int i = 0; i < snapshot.walls.Count; i++)
            {
                WallUndoWallSnapshot ws = snapshot.walls[i];
                WallObject sourceWall = restoredByIndex[i];
                if (ws == null || sourceWall == null || ws.bundledEnvelopeSnapshotIndex < 0)
                    continue;
                int ei = ws.bundledEnvelopeSnapshotIndex;
                if (ei >= restoredByIndex.Length || restoredByIndex[ei] == null)
                    continue;

                WallObject envelopeWall = restoredByIndex[ei];
                HouseEnvelopeBundledSourceTag tag = sourceWall.GetComponent<HouseEnvelopeBundledSourceTag>();
                if (tag == null)
                    tag = sourceWall.gameObject.AddComponent<HouseEnvelopeBundledSourceTag>();
                tag.envelopeWall = envelopeWall;
                HouseEnvelopeBundledSourceVisuals.SetBundledSourceVisualsHidden(sourceWall, true);
            }

            if (buildController != null)
                buildController.ForceSelectWall(selectedRestoredWall);
            else if (overlay != null)
                overlay.ClearTarget();

            if (rebuildCladdingAfterUndo && claddingToRebuild.Count > 0)
                StartDeferredCladdingRebuild(claddingToRebuild);
        }
        finally
        {
            if (usedGlobalSuspend)
                WallCladdingGenerator.SetGlobalRebuildSuspended(false);
            _isRestoring = false;
            ControlPointHandleUI.ClearOverlayPointerBlockAfterUndoOrRestore();
            EnvelopeOverlayHandleFocus.ClearAllFocus();
        }
    }

    void StartDeferredCladdingRebuild(List<WallCladdingGenerator> claddingToRebuild)
    {
        if (_rebuildCoroutine != null)
            StopCoroutine(_rebuildCoroutine);
        _rebuildCoroutine = StartCoroutine(RebuildCladdingDeferred(claddingToRebuild));
    }

    IEnumerator RebuildCladdingDeferred(List<WallCladdingGenerator> claddingToRebuild)
    {
        int budget = Mathf.Max(1, claddingRebuildBudgetPerFrame);
        int done = 0;

        for (int i = 0; i < claddingToRebuild.Count; i++)
        {
            WallCladdingGenerator cladding = claddingToRebuild[i];
            if (cladding == null || !cladding.isActiveAndEnabled)
                continue;

            // Avoid forcing many heavy mesh rebuilds in the same frame after undo.
            // Let each generator rebuild progressively via its own throttled LateUpdate.
            cladding.MarkDirty();
            done++;

            if (done >= budget)
            {
                done = 0;
                yield return null;
            }
        }

        _rebuildCoroutine = null;
    }

    WallObject RestoreWallSnapshot(WallUndoWallSnapshot snap)
    {
        if (buildController == null || buildController.wallPrefab == null)
        {
            Debug.LogError("[WallUndoManager] Impossible de restaurer: WallBuildController ou wallPrefab manquant.");
            return null;
        }

        WallObject wall = Instantiate(buildController.wallPrefab);
        wall.transform.position = Vector3.zero;
        wall.transform.rotation = Quaternion.identity;
        wall.transform.localScale = Vector3.one;

        wall.height = snap.height;
        wall.thickness = snap.thickness;
        wall.closedLoop = snap.closedLoop;
        wall.addCaps = snap.addCaps;
        wall.addBottom = snap.addBottom;
        wall.doubleSided = snap.doubleSided;
        wall.wallMaterial = snap.wallMaterial;
        wall.uvMetersPerU = snap.uvMetersPerU;
        wall.uvMetersPerV = snap.uvMetersPerV;

        if (snap.hasEditShape && snap.editState != null)
        {
            WallEditShape edit = wall.GetComponent<WallEditShape>();
            if (edit == null)
                edit = wall.gameObject.AddComponent<WallEditShape>();

            edit.wall = wall;
            RestoreEditShape(edit, snap.editState);

            WallSelectable selectable = wall.GetComponent<WallSelectable>();
            if (selectable == null)
                selectable = wall.gameObject.AddComponent<WallSelectable>();
            selectable.providerBehaviour = edit;
        }
        else
        {
            wall.SetPath(snap.path != null ? new List<Vector3>(snap.path) : new List<Vector3>());

            WallSelectable selectable = wall.GetComponent<WallSelectable>();
            if (selectable == null)
                selectable = wall.gameObject.AddComponent<WallSelectable>();
            selectable.AutoFindProvider();
        }

        if (snap.hasParquetFloor)
        {
            HouseParquetFloor floor = wall.GetComponent<HouseParquetFloor>();
            if (floor == null)
                floor = wall.gameObject.AddComponent<HouseParquetFloor>();

            floor.parquetMaterial = snap.parquetMaterial;
            floor.uvMetersPerTile = snap.parquetUvMetersPerTile;
            floor.yOffsetAboveBase = snap.parquetYOffsetAboveBase;

            WallEditShape editForFloor = wall.GetComponent<WallEditShape>();
            if (editForFloor != null && editForFloor.IsClosedLoopPath)
            {
                if (editForFloor.shapeKind == WallEditShape.ShapeKind.Rectangle)
                    floor.ApplyOrRefresh(wall, editForFloor);
                else if (editForFloor.shapeKind == WallEditShape.ShapeKind.Free)
                    floor.ApplyOrRefreshClosedFreeLoop(wall, editForFloor);
                else if (editForFloor.shapeKind == WallEditShape.ShapeKind.Ellipse ||
                         editForFloor.shapeKind == WallEditShape.ShapeKind.Triangle)
                    floor.ApplyOrRefreshFromClosedPreviewPath(wall, editForFloor);
                else
                    floor.ClearFloor();
            }
            else
                floor.ClearFloor();
        }

        if (snap.currentStyle != null)
        {
            WallStyleInstance instance = wall.GetComponent<WallStyleInstance>();
            if (instance == null)
                instance = wall.gameObject.AddComponent<WallStyleInstance>();
            instance.SetCurrentStyle(snap.currentStyle);
        }

        return wall;
    }

    void RestoreEditShape(WallEditShape edit, WallEditShapeUndoState state)
    {
        edit.shapeKind = state.shapeKind;
        edit.minX = state.minX;
        edit.maxX = state.maxX;
        edit.minZ = state.minZ;
        edit.maxZ = state.maxZ;
        edit.shapeY = state.shapeY;

        edit.rectangleOriginXZ = state.rectangleOriginXZ;
        edit.rectangleAxisX = state.rectangleAxisX;
        edit.rectangleAxisY = state.rectangleAxisY;
        edit.rectangleMinX = state.rectangleMinX;
        edit.rectangleMaxX = state.rectangleMaxX;
        edit.rectangleMinY = state.rectangleMinY;
        edit.rectangleMaxY = state.rectangleMaxY;

        edit.ellipseWallResolution = state.ellipseWallResolution;
        edit.ellipseRotationRad = state.ellipseRotationRad;
        edit.centerScrollRotationDegrees = state.centerScrollRotationDegrees;
        edit.triangleControlPoints = state.triangleControlPoints != null
            ? new List<Vector3>(state.triangleControlPoints)
            : new List<Vector3>();

        edit.arcCenterXZ = state.arcCenterXZ;
        edit.arcRadius = state.arcRadius;
        edit.arcStartRad = state.arcStartRad;
        edit.arcEndRad = state.arcEndRad;
        edit.arcCounterClockwise = state.arcCounterClockwise;
        edit.openArcWallResolution = state.openArcWallResolution;

        edit.freeControlPoints = state.freeControlPoints != null
            ? new List<Vector3>(state.freeControlPoints)
            : new List<Vector3>();

        ReflectionSet(edit, "_closedLoop", state.closedLoopPrivate);
        ReflectionSet(edit, "_freePathWasEdited", state.freePathWasEdited);
        ReflectionSet(edit, "_mergeFootprintUseExactPolyline", state.mergeFootprintUseExactPolyline);
        ReflectionSet(edit, "_closedFreeOrthogonalPolylineMode", state.closedFreeOrthogonalPolylineMode);
        ReflectionSet(edit, "_freeRawPath", state.freeRawPath != null ? new List<Vector3>(state.freeRawPath) : new List<Vector3>());

        edit.allowVerticalScrollElevation = state.allowVerticalScrollElevation;
        edit.verticalScrollElevationMetersPerWheelUnit = state.verticalScrollElevationMetersPerWheelUnit > 0.001f
            ? state.verticalScrollElevationMetersPerWheelUnit
            : 5f;

        edit.InvalidateStraightClosedPreviewCache();
        edit.ApplyToWall();
    }

    List<WallObject> GetOrderedWalls()
    {
        List<WallObject> res = new List<WallObject>();

        if (buildController != null && buildController.Walls != null && buildController.Walls.Count > 0)
        {
            for (int i = 0; i < buildController.Walls.Count; i++)
            {
                WallObject wall = buildController.Walls[i];
                if (wall != null)
                    res.Add(wall);
            }

            return res;
        }

        WallObject[] all = FindObjectsByType<WallObject>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null)
                res.Add(all[i]);

        return res;
    }

    static T ReflectionGet<T>(object target, string fieldName, T fallback)
    {
        if (target == null)
            return fallback;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
            return fallback;

        object value = field.GetValue(target);
        if (value is T typed)
            return typed;

        return fallback;
    }

    static void ReflectionSet(object target, string fieldName, object value)
    {
        if (target == null)
            return;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null)
            return;

        if (field.FieldType == typeof(List<Vector3>) && value is List<Vector3> list)
        {
            List<Vector3> targetList = field.GetValue(target) as List<Vector3>;
            if (targetList != null)
            {
                targetList.Clear();
                targetList.AddRange(list);
                return;
            }
        }

        field.SetValue(target, value);
    }

    List<Vector3> LimitPathPointCount(IReadOnlyList<Vector3> source, bool closed)
    {
        if (source == null)
            return new List<Vector3>();

        int count = source.Count;
        if (count == 0)
            return new List<Vector3>();

        int maxPts = Mathf.Clamp(maxStoredPathPointsPerWall, 0, 512);
        if (maxPts == 0 || count <= maxPts)
            return new List<Vector3>(source);

        if (closed)
        {
            List<Vector3> ring = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
                ring.Add(source[i]);

            if (ring.Count > 1 && Vector3.Distance(ring[0], ring[ring.Count - 1]) < 0.001f)
                ring.RemoveAt(ring.Count - 1);

            int target = Mathf.Clamp(maxPts, 8, 256);
            ring = WallObject.ResampleClosedLoopEvenly(ring, target);
            if (ring.Count > 0)
                ring.Add(ring[0]);
            return ring;
        }

        var reduced = new List<Vector3>(maxPts);
        float last = count - 1;
        for (int i = 0; i < maxPts; i++)
        {
            int idx = Mathf.RoundToInt((i / Mathf.Max(1f, maxPts - 1f)) * last);
            idx = Mathf.Clamp(idx, 0, count - 1);
            reduced.Add(source[idx]);
        }

        return reduced;
    }

    void DestroyAllWallObjectsInActiveScenes()
    {
        WallObject[] all = FindObjectsByType<WallObject>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            WallObject w = all[i];
            if (w == null)
                continue;
            if (Application.isPlaying)
                Destroy(w.gameObject);
            else
                DestroyImmediate(w.gameObject);
        }
    }

    [System.Serializable]
    class SceneUndoSnapshot
    {
        public string reason;
        public int selectedIndex = -1;
        public List<WallUndoWallSnapshot> walls = new List<WallUndoWallSnapshot>();
    }

    [System.Serializable]
    class WallUndoWallSnapshot
    {
        public float height;
        public float thickness;
        public bool closedLoop;
        public bool addCaps;
        public bool addBottom;
        public bool doubleSided;
        public Material wallMaterial;
        public float uvMetersPerU;
        public float uvMetersPerV;
        public List<Vector3> path = new List<Vector3>();
        public bool hasEditShape;
        public WallEditShapeUndoState editState;
        public int interiorWallsStayInsideLotSnapshotIndex = -1;
        public WallStyleDefinition currentStyle;
        public bool hasParquetFloor;
        public Material parquetMaterial;
        public float parquetUvMetersPerTile = 0.45f;
        public float parquetYOffsetAboveBase = 0.003f;

        public bool hasEnvelopeSources;
        public bool envelopeUseIndependentHandles;
        public List<int> envelopeSourceWallIndices;
        public int bundledEnvelopeSnapshotIndex = -1;
    }

    [System.Serializable]
    class WallEditShapeUndoState
    {
        public WallEditShape.ShapeKind shapeKind;
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
        public float shapeY;

        public Vector2 rectangleOriginXZ;
        public Vector2 rectangleAxisX;
        public Vector2 rectangleAxisY;
        public float rectangleMinX;
        public float rectangleMaxX;
        public float rectangleMinY;
        public float rectangleMaxY;

        public int ellipseWallResolution;
        public float ellipseRotationRad;
        public float centerScrollRotationDegrees;
        public List<Vector3> triangleControlPoints = new List<Vector3>();

        public Vector2 arcCenterXZ;
        public float arcRadius;
        public float arcStartRad;
        public float arcEndRad;
        public bool arcCounterClockwise;
        public int openArcWallResolution;

        public List<Vector3> freeControlPoints = new List<Vector3>();

        public bool closedLoopPrivate;
        public List<Vector3> freeRawPath = new List<Vector3>();
        public bool freePathWasEdited;
        public bool mergeFootprintUseExactPolyline;
        public bool closedFreeOrthogonalPolylineMode;

        public bool allowVerticalScrollElevation;
        public float verticalScrollElevationMetersPerWheelUnit = 5f;
    }
}
