using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class RuntimeAssetStoreUI : MonoBehaviour
{
    const string ResourceCatalogPath = "RuntimeAssetStore";
    const string BedResourcePath = ResourceCatalogPath + "/Bed";
    const string BettModelAssetPath = "Assets/Scenes/Scripts/Julien/World/Sites/tripo_convert_570f986f-911e-4c25-9075-19c7a6b628df.fbx";

    enum PlacementMode
    {
        Ground,
        WallDoor,
        WallWindow
    }

    sealed class CatalogItem
    {
        public string id;
        public string displayName;
        public string description;
        public PlacementMode placementMode;
        public GameObject prefab;
        public System.Func<GameObject> createProcedural;
    }

    struct PlacementPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool isValid;
        public WallObject wall;
        public int wallSegIndex;
        public float wallT;
        public float wallSegLength;
        public Vector3 wallNormal;
        public float floorYWorld;
        public Vector3 wallCenterlineWorld;
    }

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.I;
    public KeyCode rotateKey = KeyCode.R;
    public KeyCode cancelKey = KeyCode.Escape;

    [Header("Placement")]
    public Camera placementCamera;
    public LayerMask placementMask = ~0;
    public float rotateStepDegrees = 45f;
    public float fallbackPlaceDistance = 8f;
    public float previewYOffset = 0.02f;
    public float wallSurfaceOffset = 0.04f;
    public float windowCenterHeight = 1.35f;
    [Tooltip("Échelle des portes / fenêtres / pot procéduraux et des instances murales.")]
    public float decorScale = 1.65f;
    [Tooltip("Échelle appliquée aux prefabs catalogue au sol (hors lit).")]
    public float catalogPrefabScale = 1.55f;
    [Tooltip("Décalage vertical du lit Bett après placement (m).")]
    public float bedVerticalOffsetMeters = 1f;

    [Header("UI")]
    public bool openOnStart = true;
    public float panelWidth = 300f;
    public Color panelColor = new Color(0.08f, 0.08f, 0.09f, 0.92f);
    public Color buttonColor = new Color(0.20f, 0.20f, 0.23f, 0.96f);
    public Color selectedButtonColor = new Color(0.24f, 0.36f, 0.55f, 0.98f);

    readonly List<CatalogItem> _items = new List<CatalogItem>();
    readonly List<Button> _buttons = new List<Button>();
    readonly Dictionary<Button, Image> _buttonImages = new Dictionary<Button, Image>();

    Canvas _canvas;
    RectTransform _panel;
    Text _selectedText;
    CatalogItem _selectedItem;
    GameObject _preview;
    float _currentYaw;
    Font _font;
    WallDrawInput _drawInput;
    HierarchicalGridManager _gridManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<RuntimeAssetStoreUI>(FindObjectsInactive.Include) != null)
            return;

        GameObject root = new GameObject("Runtime Asset Store UI");
        root.AddComponent<RuntimeAssetStoreUI>();
    }

    void Awake()
    {
        ResolveSceneReferences();
        _font = ResolveFont();
        EnsureEventSystem();
        BuildCatalog();
        BuildUi();
        SetOpen(openOnStart);
    }

    void OnDestroy()
    {
        DestroyPreview();
    }

    void Update()
    {
        ResolveSceneReferences();

        if (Input.GetKeyDown(toggleKey))
            ToggleOpen();

        if (_selectedItem == null)
            return;

        if (Input.GetKeyDown(cancelKey))
        {
            ClearSelection();
            return;
        }

        if (Input.GetKeyDown(rotateKey) && _selectedItem.placementMode == PlacementMode.Ground)
            _currentYaw += rotateStepDegrees;

        UpdatePreviewTransform();

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUi() &&
            TryResolvePlacementPose(_selectedItem, out PlacementPose pose) && pose.isValid)
            PlaceSelected(pose);
    }

    void ResolveSceneReferences()
    {
        if (placementCamera == null)
            placementCamera = Camera.main;
        if (_drawInput == null)
            _drawInput = FindFirstObjectByType<WallDrawInput>();
        if (_gridManager == null)
            _gridManager = FindFirstObjectByType<HierarchicalGridManager>();
    }

    void BuildCatalog()
    {
        _items.Clear();

        _items.Add(new CatalogItem
        {
            id = "door",
            displayName = "Porte",
            description = "Lot maison : perce le mur, des deux côtés.",
            placementMode = PlacementMode.WallDoor,
            createProcedural = CreateDoor
        });

        _items.Add(new CatalogItem
        {
            id = "window",
            displayName = "Fenêtre",
            description = "Lot maison : perce le mur, vitre des deux côtés.",
            placementMode = PlacementMode.WallWindow,
            createProcedural = CreateWindow
        });

        _items.Add(new CatalogItem
        {
            id = "bed",
            displayName = "Bett",
            description = "Uniquement à l’intérieur d’un lot maison.",
            placementMode = PlacementMode.Ground,
            createProcedural = CreateBettBed
        });

        _items.Add(new CatalogItem
        {
            id = "flower_pot",
            displayName = "Pot de fleurs",
            description = "Uniquement à l’intérieur d’un lot maison.",
            placementMode = PlacementMode.Ground,
            createProcedural = CreateFlowerPot
        });

        GameObject[] prefabs = Resources.LoadAll<GameObject>(ResourceCatalogPath);
        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null || prefab.name == "Bed")
                continue;

            _items.Add(new CatalogItem
            {
                id = "prefab_" + prefab.name,
                displayName = ObjectNameToLabel(prefab.name),
                description = "Prefab importé depuis Resources/" + ResourceCatalogPath + ".",
                placementMode = PlacementMode.Ground,
                prefab = prefab
            });
        }
    }

    void BuildUi()
    {
        _canvas = CreateCanvas("AssetStoreCanvas");
        _panel = CreatePanel(_canvas.transform);

        VerticalLayoutGroup layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        Text title = CreateText("Title", _panel, "Asset Store", 22, FontStyle.Bold, TextAnchor.MiddleLeft);
        title.color = Color.white;
        AddLayout(title.gameObject, -1f, 34f);

        Text help = CreateText("Help", _panel, "Objets au sol: snap grille. Porte/fenêtre: clique directement sur un mur. R = rotation sol, Esc = annuler, I = ouvrir/fermer.", 12, FontStyle.Normal, TextAnchor.UpperLeft);
        help.color = new Color(0.84f, 0.84f, 0.86f, 1f);
        help.horizontalOverflow = HorizontalWrapMode.Wrap;
        AddLayout(help.gameObject, -1f, 72f);

        _selectedText = CreateText("Selected", _panel, string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleLeft);
        _selectedText.color = new Color(0.78f, 0.88f, 1f, 1f);
        AddLayout(_selectedText.gameObject, -1f, 24f);

        for (int i = 0; i < _items.Count; i++)
            CreateItemButton(_items[i]);

        RefreshSelectedText();
    }

    Canvas CreateCanvas(string objectName)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 7000;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    RectTransform CreatePanel(Transform parent)
    {
        GameObject go = new GameObject("AssetStorePanel");
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = panelColor;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(panelWidth, 0f);
        return rect;
    }

    void CreateItemButton(CatalogItem item)
    {
        GameObject go = new GameObject(item.id + "_Button");
        go.transform.SetParent(_panel, false);

        Image image = go.AddComponent<Image>();
        image.color = buttonColor;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => SelectItem(item));

        AddLayout(go, -1f, 66f);

        Text label = CreateText("Label", go.GetComponent<RectTransform>(), item.displayName + "\n" + item.description, 14, FontStyle.Normal, TextAnchor.MiddleLeft);
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;

        RectTransform textRect = label.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 6f);
        textRect.offsetMax = new Vector2(-12f, -6f);

        _buttons.Add(button);
        _buttonImages[button] = image;
    }

    Text CreateText(string objectName, Transform parent, string value, int size, FontStyle style, TextAnchor anchor)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = _font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.raycastTarget = false;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        return text;
    }

    static void AddLayout(GameObject go, float preferredWidth, float preferredHeight)
    {
        LayoutElement layout = go.AddComponent<LayoutElement>();
        if (preferredWidth > 0f)
            layout.preferredWidth = preferredWidth;
        if (preferredHeight > 0f)
            layout.preferredHeight = preferredHeight;
    }

    void SelectItem(CatalogItem item)
    {
        _selectedItem = item;
        _currentYaw = 0f;
        RebuildPreview();
        RefreshSelectedText();
        RefreshButtonColors();
    }

    void ClearSelection()
    {
        _selectedItem = null;
        DestroyPreview();
        RefreshSelectedText();
        RefreshButtonColors();
    }

    void RefreshSelectedText()
    {
        if (_selectedText == null)
            return;

        _selectedText.text = _selectedItem == null
            ? "Sélection : aucune"
            : "Sélection : " + _selectedItem.displayName;
    }

    void RefreshButtonColors()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            Button button = _buttons[i];
            if (button == null || !_buttonImages.TryGetValue(button, out Image image) || image == null)
                continue;

            image.color = i < _items.Count && _items[i] == _selectedItem ? selectedButtonColor : buttonColor;
        }
    }

    void RebuildPreview()
    {
        DestroyPreview();

        if (_selectedItem == null)
            return;

        _preview = CreateItemInstance(_selectedItem);
        if (_preview == null)
            return;

        _preview.name = _selectedItem.displayName + " Preview";
        if (_selectedItem.placementMode == PlacementMode.WallDoor || _selectedItem.placementMode == PlacementMode.WallWindow)
            _preview.transform.localScale *= decorScale;

        SetPreviewVisuals(_preview);
        UpdatePreviewTransform();
    }

    void DestroyPreview()
    {
        if (_preview != null)
            Destroy(_preview);

        _preview = null;
    }

    void UpdatePreviewTransform()
    {
        if (_preview == null || _selectedItem == null)
            return;

        if (TryResolvePlacementPose(_selectedItem, out PlacementPose pose) && pose.isValid)
        {
            _preview.SetActive(true);
            _preview.transform.position = pose.position + Vector3.up * previewYOffset;
            _preview.transform.rotation = pose.rotation;
        }
        else
        {
            _preview.SetActive(false);
        }
    }

    void PlaceSelected(PlacementPose pose)
    {
        if (_selectedItem == null)
            return;

        if (_selectedItem.placementMode == PlacementMode.WallDoor)
        {
            PlaceWallDecorWithOpening(pose, WallOpeningKind.Door, 0.9f * decorScale, 2.1f * decorScale);
            return;
        }

        if (_selectedItem.placementMode == PlacementMode.WallWindow)
        {
            float winH = 0.88f * decorScale;
            PlaceWallDecorWithOpening(pose, WallOpeningKind.Window, 1.15f * decorScale, winH);
            return;
        }

        if (!IsWorldPointInsideAnyDesignatedHouseLotXZ(pose.position))
            return;

        GameObject instance = CreateItemInstance(_selectedItem);
        if (instance == null)
            return;

        instance.name = _selectedItem.displayName;
        instance.transform.position = pose.position;
        instance.transform.rotation = pose.rotation;

        if (_selectedItem.id != "bed")
        {
            if (_selectedItem.prefab != null)
                instance.transform.localScale *= catalogPrefabScale;
            else
                instance.transform.localScale *= decorScale;
        }

        if (_selectedItem.id == "bed")
        {
            instance.transform.localScale *= 0.5f;
            instance.transform.position += Vector3.up * bedVerticalOffsetMeters;
        }
    }

    void PlaceWallDecorWithOpening(PlacementPose pose, WallOpeningKind kind, float widthMeters, float heightMeters)
    {
        WallObject wall = pose.wall;
        if (wall == null)
            return;

        WallOpeningRegistry registry = wall.GetComponent<WallOpeningRegistry>();
        if (registry == null)
            registry = wall.gameObject.AddComponent<WallOpeningRegistry>();

        float wallH = Mathf.Max(0.1f, wall.height);
        float h0 = 0.02f;
        float h1 = Mathf.Clamp01(heightMeters / wallH - 0.02f);
        if (kind == WallOpeningKind.Window)
        {
            float centerFrac = windowCenterHeight / wallH;
            float halfFrac = (heightMeters * 0.5f) / wallH;
            h0 = Mathf.Clamp01(centerFrac - halfFrac);
            h1 = Mathf.Clamp01(centerFrac + halfFrac);
        }

        if (h1 - h0 < 0.03f)
            return;

        registry.AddOpening(pose.wallSegIndex, pose.wallT, widthMeters, pose.wallSegLength, h0, h1, kind);
        wall.ForceRebuildMesh();

        Vector3 n = pose.wallNormal;
        float halfT = Mathf.Max(0.01f, wall.thickness) * 0.5f;
        float off = halfT + wallSurfaceOffset;
        Vector3 centerFloor = pose.wallCenterlineWorld;

        SpawnFaceDecorInstance(centerFloor, Quaternion.LookRotation(n, Vector3.up), n, off, kind, false);
        SpawnFaceDecorInstance(centerFloor, Quaternion.LookRotation(-n, Vector3.up), -n, off, kind, true);
    }

    void SpawnFaceDecorInstance(Vector3 centerFloor, Quaternion rotation, Vector3 outwardNormal, float surfaceOffset, WallOpeningKind kind, bool innerFace)
    {
        GameObject instance = CreateItemInstance(_selectedItem);
        if (instance == null)
            return;

        instance.name = _selectedItem.displayName + (innerFace ? " (intérieur)" : " (extérieur)");

        float y = centerFloor.y;
        if (kind == WallOpeningKind.Window)
            y = centerFloor.y + windowCenterHeight;

        Vector3 pos = new Vector3(centerFloor.x, y, centerFloor.z) + outwardNormal * surfaceOffset;
        instance.transform.position = pos;
        instance.transform.rotation = rotation;
        instance.transform.localScale *= decorScale;
    }

    GameObject CreateItemInstance(CatalogItem item)
    {
        if (item == null)
            return null;

        if (item.prefab != null)
            return Instantiate(item.prefab);

        return item.createProcedural != null ? item.createProcedural() : null;
    }

    bool TryResolvePlacementPose(CatalogItem item, out PlacementPose pose)
    {
        pose = default;
        if (item == null)
            return false;

        if (item.placementMode == PlacementMode.WallDoor || item.placementMode == PlacementMode.WallWindow)
            return TryResolveWallPlacement(item, out pose);

        return TryResolveGroundPlacement(out pose);
    }

    bool TryResolveGroundPlacement(out PlacementPose pose)
    {
        pose = default;
        if (!TryGetGridPlanePlacementPoint(out Vector3 point))
            return false;

        point = SnapWorldToGrid(point);
        if (!IsWorldPointInsideAnyDesignatedHouseLotXZ(point))
            return false;

        pose.position = point;
        pose.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        pose.isValid = true;
        return true;
    }

    bool TryResolveWallPlacement(CatalogItem item, out PlacementPose pose)
    {
        pose = default;

        if (placementCamera == null)
            return false;

        Ray ray = placementCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, placementMask, QueryTriggerInteraction.Ignore))
            return false;

        WallObject wall = hit.collider != null ? hit.collider.GetComponentInParent<WallObject>() : null;
        if (wall == null || wall.Points == null || wall.ControlPointCount < 2)
            return false;

        Vector3 snappedHit = SnapWallHitToGrid(hit.point);
        if (!TryFindClosestWallSegment(wall, snappedHit, out int segIndex, out float tAlong, out Vector3 centerlinePoint, out Vector3 tangent, out float segLen, out float floorY))
            return false;

        if (!CanPlaceWallInDesignatedHouse(wall, centerlinePoint, out _))
            return false;

        Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
        if (normal.sqrMagnitude < 0.0001f)
            return false;

        if (Vector3.Dot(normal, hit.point - centerlinePoint) < 0f)
            normal = -normal;

        float halfThickness = Mathf.Max(0.01f, wall.thickness) * 0.5f;
        bool isWindow = item.placementMode == PlacementMode.WallWindow;
        float anchorY = isWindow ? floorY + windowCenterHeight : floorY;
        pose.position = new Vector3(centerlinePoint.x, anchorY, centerlinePoint.z) + normal * (halfThickness + wallSurfaceOffset);
        pose.rotation = Quaternion.LookRotation(normal, Vector3.up);
        pose.wall = wall;
        pose.wallSegIndex = segIndex;
        pose.wallT = tAlong;
        pose.wallSegLength = segLen;
        pose.wallNormal = normal;
        pose.floorYWorld = floorY;
        pose.wallCenterlineWorld = new Vector3(centerlinePoint.x, floorY, centerlinePoint.z);
        pose.isValid = true;
        return true;
    }

    bool TryGetGridPlanePlacementPoint(out Vector3 point)
    {
        point = Vector3.zero;

        if (placementCamera == null)
            return false;

        float planeY = _gridManager != null && _gridManager.settings != null
            ? _gridManager.settings.gridPlaneY
            : 0f;

        Ray ray = placementCamera.ScreenPointToRay(Input.mousePosition);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        if (!ground.Raycast(ray, out float distance))
            return TryGetRawPlacementPoint(out point);

        point = ray.GetPoint(distance);
        return true;
    }

    bool TryGetRawPlacementPoint(out Vector3 point)
    {
        point = Vector3.zero;

        if (placementCamera == null)
            return false;

        Ray ray = placementCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, placementMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        Plane ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = placementCamera.transform.position + placementCamera.transform.forward * fallbackPlaceDistance;
        point.y = 0f;
        return true;
    }

    Vector3 SnapWorldToGrid(Vector3 point)
    {
        if (_drawInput != null && _drawInput.enableGridSnap && _drawInput.snapToHierarchicalVisualGrid)
            return _drawInput.SnapWorldToHierarchicalLeafCenter(point);

        if (_gridManager != null && _gridManager.TryGetCellAtWorld(point, out HierarchicalGridNode cell))
        {
            Vector3 c = _gridManager.GetCellCenterWorld(cell);
            return new Vector3(c.x, point.y, c.z);
        }

        return point;
    }

    Vector3 SnapWallHitToGrid(Vector3 hitPoint)
    {
        Vector3 snapped = SnapWorldToGrid(hitPoint);
        snapped.y = hitPoint.y;
        return snapped;
    }

    static bool IsWorldPointInsideAnyDesignatedHouseLotXZ(Vector3 world)
    {
        WallEditShape[] edits = FindObjectsByType<WallEditShape>(FindObjectsSortMode.None);
        for (int i = 0; i < edits.Length; i++)
        {
            WallEditShape edit = edits[i];
            if (edit == null || edit.wall == null)
                continue;

            HouseParquetFloor floor = edit.wall.GetComponent<HouseParquetFloor>();
            if (floor == null || !floor.IsDesignatedHouseLot)
                continue;

            if (edit.ContainsWorldPointInClosedLotFootprintXZ(world, 0f))
                return true;
        }

        return false;
    }

    static bool CanPlaceWallInDesignatedHouse(WallObject wall, Vector3 testPointWorld, out WallEditShape resolvedLotEdit)
    {
        resolvedLotEdit = null;
        WallEditShape edit = wall.GetComponent<WallEditShape>();
        if (edit == null)
            return false;

        HouseParquetFloor hf = wall.GetComponent<HouseParquetFloor>();
        if (hf != null && hf.IsDesignatedHouseLot && edit.ContainsWorldPointInClosedLotFootprintXZ(testPointWorld, 0f))
        {
            resolvedLotEdit = edit;
            return true;
        }

        WallEditShape parentLot = edit.interiorWallsStayInsideLot;
        if (parentLot != null && parentLot.ContainsWorldPointInClosedLotFootprintXZ(testPointWorld, 0f))
        {
            resolvedLotEdit = parentLot;
            return true;
        }

        return false;
    }

    static bool TryFindClosestWallSegment(
        WallObject wall,
        Vector3 point,
        out int segIndex,
        out float tAlong,
        out Vector3 closestPoint,
        out Vector3 tangent,
        out float segLen,
        out float floorY)
    {
        segIndex = 0;
        tAlong = 0f;
        closestPoint = Vector3.zero;
        tangent = Vector3.forward;
        segLen = 0.01f;
        floorY = point.y;

        int count = wall.ControlPointCount;
        if (count < 2)
            return false;

        Vector2 p = new Vector2(point.x, point.z);
        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (!wall.closedLoop && i == count - 1)
                break;

            Vector3 a3 = wall.GetControlPointWorld(i);
            Vector3 b3 = wall.GetControlPointWorld((i + 1) % count);
            Vector2 a = new Vector2(a3.x, a3.z);
            Vector2 b = new Vector2(b3.x, b3.z);
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f)
                continue;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 c = a + ab * t;
            float d = (p - c).sqrMagnitude;
            if (d >= best)
                continue;

            best = d;
            segIndex = i;
            tAlong = t;
            segLen = Mathf.Sqrt(lenSq);
            floorY = Mathf.Lerp(a3.y, b3.y, t);
            closestPoint = new Vector3(c.x, floorY, c.y);
            tangent = new Vector3(ab.x, 0f, ab.y).normalized;
            found = true;
        }

        return found;
    }

    bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    void ToggleOpen()
    {
        SetOpen(_panel == null || !_panel.gameObject.activeSelf);
    }

    void SetOpen(bool open)
    {
        if (_panel != null)
            _panel.gameObject.SetActive(open);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    static Font ResolveFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    static string ObjectNameToLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Objet";

        return value.Replace('_', ' ').Replace('-', ' ');
    }

    static void SetPreviewVisuals(GameObject root)
    {
        HomeSite[] homeSites = root.GetComponentsInChildren<HomeSite>(true);
        for (int i = 0; i < homeSites.Length; i++)
            homeSites[i].enabled = false;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] == null)
                    continue;

                mats[m].color = new Color(mats[m].color.r, mats[m].color.g, mats[m].color.b, 0.55f);
                mats[m].SetFloat("_Mode", 3f);
                mats[m].SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mats[m].SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mats[m].SetInt("_ZWrite", 0);
                mats[m].DisableKeyword("_ALPHATEST_ON");
                mats[m].EnableKeyword("_ALPHABLEND_ON");
                mats[m].DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mats[m].renderQueue = 3000;
            }
        }
    }

    static GameObject CreateDoor()
    {
        GameObject root = new GameObject("Door");
        CreateCube(root.transform, "Panel", new Vector3(0f, 1.05f, 0f), new Vector3(0.9f, 2.1f, 0.12f), new Color(0.45f, 0.25f, 0.12f));
        CreateCube(root.transform, "FrameTop", new Vector3(0f, 2.18f, 0f), new Vector3(1.1f, 0.12f, 0.18f), new Color(0.22f, 0.13f, 0.07f));
        CreateCube(root.transform, "FrameLeft", new Vector3(-0.55f, 1.05f, 0f), new Vector3(0.12f, 2.2f, 0.18f), new Color(0.22f, 0.13f, 0.07f));
        CreateCube(root.transform, "FrameRight", new Vector3(0.55f, 1.05f, 0f), new Vector3(0.12f, 2.2f, 0.18f), new Color(0.22f, 0.13f, 0.07f));
        CreateSphere(root.transform, "Handle", new Vector3(0.32f, 1.05f, -0.08f), new Vector3(0.08f, 0.08f, 0.08f), new Color(0.95f, 0.75f, 0.25f));
        return root;
    }

    static GameObject CreateWindow()
    {
        GameObject root = new GameObject("Window");
        GameObject glass = CreateCube(root.transform, "Glass", new Vector3(0f, 0f, 0f), new Vector3(1.15f, 0.8f, 0.06f), Color.white);
        ApplyRuntimeTransparentGlass(glass.GetComponent<Renderer>());
        CreateCube(root.transform, "FrameTop", new Vector3(0f, 0.46f, 0f), new Vector3(1.35f, 0.1f, 0.12f), new Color(0.18f, 0.12f, 0.07f));
        CreateCube(root.transform, "FrameBottom", new Vector3(0f, -0.46f, 0f), new Vector3(1.35f, 0.1f, 0.12f), new Color(0.18f, 0.12f, 0.07f));
        CreateCube(root.transform, "FrameLeft", new Vector3(-0.67f, 0f, 0f), new Vector3(0.1f, 0.9f, 0.12f), new Color(0.18f, 0.12f, 0.07f));
        CreateCube(root.transform, "FrameRight", new Vector3(0.67f, 0f, 0f), new Vector3(0.1f, 0.9f, 0.12f), new Color(0.18f, 0.12f, 0.07f));
        CreateCube(root.transform, "CrossVertical", new Vector3(0f, 0f, -0.01f), new Vector3(0.07f, 0.82f, 0.13f), new Color(0.18f, 0.12f, 0.07f));
        CreateCube(root.transform, "CrossHorizontal", new Vector3(0f, 0f, -0.01f), new Vector3(1.18f, 0.07f, 0.13f), new Color(0.18f, 0.12f, 0.07f));
        return root;
    }

    static GameObject CreateBettBed()
    {
        GameObject resourceBed = Resources.Load<GameObject>(BedResourcePath);
        if (resourceBed != null)
            return Instantiate(resourceBed);

#if UNITY_EDITOR
        GameObject bettModel = AssetDatabase.LoadAssetAtPath<GameObject>(BettModelAssetPath);
        if (bettModel != null)
            return CreateBettBedFromModel(bettModel);
#endif

        HomeSite template = FindBettTemplateInScene();
        if (template != null)
            return Instantiate(template.gameObject);

        return CreateFallbackBettBed();
    }

    static HomeSite FindBettTemplateInScene()
    {
        HomeSite[] sites = FindObjectsByType<HomeSite>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sites.Length; i++)
        {
            HomeSite site = sites[i];
            if (site == null)
                continue;

            string n = site.name.ToLowerInvariant();
            if (n.Contains("bett"))
                return site;
        }

        return null;
    }

    static GameObject CreateBettBedFromModel(GameObject bettModel)
    {
        GameObject root = new GameObject("Bett");
        GameObject model = Instantiate(bettModel, root.transform);
        model.name = "Bett_Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        model.transform.localScale = Vector3.one * 4.5072f;

        Transform bedPoint = CreateAnchor(root.transform, "Laying Point", new Vector3(0f, 0.8f, 0.05f));
        Transform standPoint = CreateAnchor(root.transform, "Drop Point", new Vector3(0f, 0f, -1.55f));
        HomeSite home = root.AddComponent<HomeSite>();
        AssignHomeSiteAnchors(home, bedPoint, standPoint);
        return root;
    }

    static GameObject CreateFallbackBettBed()
    {
        GameObject root = CreateBedVisual();
        root.name = "Bett";

        Transform bedPoint = CreateAnchor(root.transform, "Laying Point", new Vector3(0f, 0.8f, 0.05f));
        Transform standPoint = CreateAnchor(root.transform, "Drop Point", new Vector3(0f, 0f, -1.55f));
        HomeSite home = root.AddComponent<HomeSite>();
        AssignHomeSiteAnchors(home, bedPoint, standPoint);
        return root;
    }

    static GameObject CreateBedVisual()
    {
        GameObject root = new GameObject("Bed");
        CreateCube(root.transform, "Base", new Vector3(0f, 0.25f, 0f), new Vector3(1.7f, 0.35f, 2.45f), new Color(0.42f, 0.25f, 0.15f));
        CreateCube(root.transform, "Mattress", new Vector3(0f, 0.52f, 0.05f), new Vector3(1.58f, 0.26f, 2.18f), new Color(0.85f, 0.86f, 0.82f));
        CreateCube(root.transform, "Pillow", new Vector3(0f, 0.74f, 0.78f), new Vector3(1.25f, 0.18f, 0.48f), new Color(0.96f, 0.96f, 0.92f));
        CreateCube(root.transform, "Blanket", new Vector3(0f, 0.78f, -0.35f), new Vector3(1.5f, 0.12f, 1.2f), new Color(0.25f, 0.36f, 0.62f));
        return root;
    }

    static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    static void AssignHomeSiteAnchors(HomeSite home, Transform bedPoint, Transform standPoint)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(HomeSite).GetField("bedPoint", flags)?.SetValue(home, bedPoint);
        typeof(HomeSite).GetField("bedStandPoint", flags)?.SetValue(home, standPoint);
    }

    static GameObject CreateFlowerPot()
    {
        GameObject root = new GameObject("Flower Pot");
        CreateCylinder(root.transform, "Pot", new Vector3(0f, 0.28f, 0f), new Vector3(0.5f, 0.55f, 0.5f), new Color(0.65f, 0.28f, 0.14f));
        CreateSphere(root.transform, "Plant", new Vector3(0f, 0.76f, 0f), new Vector3(0.62f, 0.42f, 0.62f), new Color(0.18f, 0.55f, 0.20f));
        CreateSphere(root.transform, "FlowerA", new Vector3(0.18f, 0.96f, 0.05f), new Vector3(0.14f, 0.14f, 0.14f), new Color(0.95f, 0.35f, 0.55f));
        CreateSphere(root.transform, "FlowerB", new Vector3(-0.16f, 0.92f, -0.08f), new Vector3(0.12f, 0.12f, 0.12f), new Color(1f, 0.85f, 0.18f));
        return root;
    }

    static GameObject CreateCube(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SetupPrimitive(go, parent, name, localPosition, localScale, color);
        return go;
    }

    static GameObject CreateSphere(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        SetupPrimitive(go, parent, name, localPosition, localScale, color);
        return go;
    }

    static GameObject CreateCylinder(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        SetupPrimitive(go, parent, name, localPosition, localScale, color);
        return go;
    }

    static void SetupPrimitive(GameObject go, Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
    {
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return;

        Shader shader = ResolveDefaultShader();
        if (shader == null)
            return;

        Material mat = new Material(shader);
        mat.color = color;
        renderer.sharedMaterial = mat;
    }

    static Shader ResolveDefaultShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        return shader;
    }

    static void ApplyRuntimeTransparentGlass(Renderer renderer)
    {
        if (renderer == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return;

        Material mat = new Material(shader);
        mat.color = new Color(0.55f, 0.78f, 0.92f, 0.32f);

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        }

        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
        }

        mat.renderQueue = 3000;
        renderer.sharedMaterial = mat;
    }
}
