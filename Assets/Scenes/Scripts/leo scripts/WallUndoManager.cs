using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class WallUndoManager : MonoBehaviour
{
    [Header("References")]
    public WallBuildController buildController;
    public ControlPointOverlayManager overlay;
    public WallContextMenuUI contextMenu;

    [Header("Input")]
    public bool enableUndo = true;
    public bool requireCtrl = true;
    public KeyCode undoKey = KeyCode.Z;

    [Header("Stack")]
    [Min(1)] public int maxSnapshots = 40;
    public bool logDebug = false;

    private readonly Stack<SceneUndoSnapshot> _undoStack = new Stack<SceneUndoSnapshot>();
    private bool _isRestoring;

    public bool IsRestoring => _isRestoring;
    public int UndoCount => _undoStack.Count;

    void Awake()
    {
        if (buildController == null)
            buildController = FindFirstObjectByType<WallBuildController>();

        if (overlay == null)
            overlay = FindFirstObjectByType<ControlPointOverlayManager>();

        if (contextMenu == null)
            contextMenu = FindFirstObjectByType<WallContextMenuUI>(FindObjectsInactive.Include);
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

        while (_undoStack.Count > maxSnapshots)
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
        if (_undoStack.Count <= maxSnapshots)
            return;

        SceneUndoSnapshot[] arr = _undoStack.ToArray();
        _undoStack.Clear();

        for (int i = arr.Length - 2; i >= 0; i--)
            _undoStack.Push(arr[i]);
    }

    SceneUndoSnapshot CaptureSceneSnapshot(string reason)
    {
        List<WallObject> walls = GetOrderedWalls();
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

            snapshot.walls.Add(CaptureWallSnapshot(wall));
        }

        return snapshot;
    }

    WallUndoWallSnapshot CaptureWallSnapshot(WallObject wall)
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
        snap.path = new List<Vector3>(wall.Points);

        WallStyleInstance styleInstance = wall.GetComponent<WallStyleInstance>();
        if (styleInstance != null)
            snap.currentStyle = styleInstance.currentStyle;

        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit != null)
        {
            snap.hasEditShape = true;
            snap.editState = CaptureEditShape(edit);
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
        state.freeControlPoints = new List<Vector3>(edit.freeControlPoints);

        state.closedLoopPrivate = ReflectionGet<bool>(edit, "_closedLoop", false);
        state.freePathWasEdited = ReflectionGet<bool>(edit, "_freePathWasEdited", false);

        List<Vector3> rawPath = ReflectionGet<List<Vector3>>(edit, "_freeRawPath", null);
        state.freeRawPath = rawPath != null ? new List<Vector3>(rawPath) : new List<Vector3>();

        return state;
    }

    void RestoreSceneSnapshot(SceneUndoSnapshot snapshot)
    {
        _isRestoring = true;

        try
        {
            if (contextMenu != null)
                contextMenu.Close();

            List<WallObject> existingWalls = GetOrderedWalls();
            for (int i = 0; i < existingWalls.Count; i++)
            {
                if (existingWalls[i] == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(existingWalls[i].gameObject);
                else
                    DestroyImmediate(existingWalls[i].gameObject);
            }

            if (buildController != null)
                buildController.ClearManagedWalls();

            WallObject selectedRestoredWall = null;

            for (int i = 0; i < snapshot.walls.Count; i++)
            {
                WallUndoWallSnapshot wallSnap = snapshot.walls[i];
                WallObject wall = RestoreWallSnapshot(wallSnap);

                if (buildController != null)
                    buildController.RegisterExistingWall(wall);

                if (i == snapshot.selectedIndex)
                    selectedRestoredWall = wall;
            }

            if (buildController != null)
                buildController.ForceSelectWall(selectedRestoredWall);
            else if (overlay != null)
                overlay.ClearTarget();
        }
        finally
        {
            _isRestoring = false;
        }
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
        edit.freeControlPoints = state.freeControlPoints != null
            ? new List<Vector3>(state.freeControlPoints)
            : new List<Vector3>();

        ReflectionSet(edit, "_closedLoop", state.closedLoopPrivate);
        ReflectionSet(edit, "_freePathWasEdited", state.freePathWasEdited);
        ReflectionSet(edit, "_freeRawPath", state.freeRawPath != null ? new List<Vector3>(state.freeRawPath) : new List<Vector3>());

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
        public WallStyleDefinition currentStyle;
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
        public List<Vector3> freeControlPoints = new List<Vector3>();

        public bool closedLoopPrivate;
        public List<Vector3> freeRawPath = new List<Vector3>();
        public bool freePathWasEdited;
    }
}
