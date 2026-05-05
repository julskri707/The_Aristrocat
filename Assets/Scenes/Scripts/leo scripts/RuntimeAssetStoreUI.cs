using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
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
        WallWindow,
        Stair
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
        /// <summary>Hauteur monde du centre de la fenêtre le long du mur (portes : ignoré pour le mesh).</summary>
        public float windowCenterYWorld;
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
    [Tooltip("Hauteur par défaut du centre de fenêtre si le rayon est trop parallèle au mur (m au-dessus du bas du mur).")]
    public float windowCenterHeight = 1.35f;
    [Tooltip("Distance minimale du bas de l’ouverture fenêtre au sol du mur (m).")]
    [Min(0.05f)] public float windowMinClearanceFromFloorMeters = 1f;
    [Tooltip("Distance minimale du haut de l’ouverture fenêtre au plafond du mur (m).")]
    [Min(0.05f)] public float windowMinClearanceFromCeilingMeters = 0.5f;
    [Tooltip("Largeur min / max de la fenêtre au placement (m, avant décor).")]
    public float windowMinWidthMeters = 0.55f;
    public float windowMaxWidthMeters = 2.4f;
    [Tooltip("Hauteur min / max de la fenêtre au placement (m).")]
    public float windowMinHeightMeters = 0.45f;
    public float windowMaxHeightMeters = 2f;
    [Tooltip("Saillie supplémentaire des panneaux porte au-delà de la surface du mur (chaque côté).")]
    [Min(0f)] public float doorDecorProtrusionExtraMeters = 0.14f;
    [Tooltip("Saillie supplémentaire des cadres fenêtre au-delà de la surface du mur.")]
    [Min(0f)] public float windowDecorProtrusionExtraMeters = 0.04f;
    [Tooltip("Demi-largeur XZ de la trémie parquet pour un escalier placé (m).")]
    [Min(0.2f)] public float stairCutoutHalfWidthMeters = 1.4f;
    [Tooltip("Demi-profondeur XZ de la trémie parquet pour un escalier placé (m).")]
    [Min(0.2f)] public float stairCutoutHalfDepthMeters = 2.5f;
    [Tooltip("Échelle des portes / fenêtres / pot procéduraux et des instances murales.")]
    public float decorScale = 1.65f;
    [Tooltip("Échelle appliquée aux prefabs catalogue au sol (hors lit).")]
    public float catalogPrefabScale = 1.55f;
    [Tooltip("Décalage vertical du lit Bett après placement (m).")]
    public float bedVerticalOffsetMeters = 1f;

    [Header("UI")]
    public bool openOnStart = true;
    public float panelWidth = 300f;
    public Color panelColor = new Color(0.11f, 0.098f, 0.088f, 0.97f);
    public Color headerBarColor = new Color(0.15f, 0.125f, 0.10f, 1f);
    public Color headerAccentColor = new Color(0.86f, 0.72f, 0.42f, 1f);
    public Color buttonColor = new Color(0.20f, 0.175f, 0.155f, 0.99f);
    public Color buttonColorAlt = new Color(0.17f, 0.15f, 0.135f, 0.99f);
    public Color selectedButtonColor = new Color(0.35f, 0.42f, 0.28f, 1f);

    readonly List<CatalogItem> _items = new List<CatalogItem>();
    readonly List<Button> _buttons = new List<Button>();
    readonly Dictionary<Button, Image> _buttonImages = new Dictionary<Button, Image>();

    Canvas _canvas;
    RectTransform _panel;
    GameObject _selectionLabelGo;
    CatalogItem _selectedItem;
    GameObject _preview;
    float _currentYaw;
    float _windowPlaceWidthMeters;
    float _windowPlaceHeightMeters;
    Font _font;
    TMP_FontAsset _catalogTmpFont;
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
        ResetWindowPlacementSize();
        BuildCatalog();
    }

    void ResetWindowPlacementSize()
    {
        _windowPlaceWidthMeters = Mathf.Clamp(1.15f * decorScale, windowMinWidthMeters, windowMaxWidthMeters);
        _windowPlaceHeightMeters = Mathf.Clamp(0.88f * decorScale, windowMinHeightMeters, windowMaxHeightMeters);
    }

    void Start()
    {
        _catalogTmpFont = LoadTmpFontForCatalogUi();
        BuildUi();
        RefreshSelectedText();
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

        if (Input.GetKeyDown(rotateKey) &&
            (_selectedItem.placementMode == PlacementMode.Ground || _selectedItem.placementMode == PlacementMode.Stair))
            _currentYaw += rotateStepDegrees;

        if (_selectedItem.placementMode == PlacementMode.WallWindow && !IsPointerOverUi())
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 1e-3f)
            {
                float step = GetInteriorFineLatticeStepMeters();
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    _windowPlaceWidthMeters = Mathf.Clamp(
                        _windowPlaceWidthMeters + Mathf.Sign(scroll) * step,
                        windowMinWidthMeters,
                        windowMaxWidthMeters);
                }
                else
                {
                    _windowPlaceHeightMeters = Mathf.Clamp(
                        _windowPlaceHeightMeters + Mathf.Sign(scroll) * step,
                        windowMinHeightMeters,
                        windowMaxHeightMeters);
                }

                RebuildPreview();
            }
        }

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
            description = "Mur maison uniquement : ouverture tunnel + cadres saillants des deux côtés.",
            placementMode = PlacementMode.WallDoor,
            createProcedural = CreateDoor
        });

        _items.Add(new CatalogItem
        {
            id = "window",
            displayName = "Fenêtre",
            description = "Mur maison uniquement : grille sur le mur (plus fine que la grille monde), marges sol/plafond, molette / Shift+molette pour la taille.",
            placementMode = PlacementMode.WallWindow,
            createProcedural = CreateWindow
        });

        _items.Add(new CatalogItem
        {
            id = "stairs",
            displayName = "Escalier",
            description = "Lot maison : clic sur l’escalier → 4 poignées overlay 2D (coins au sol), sans traits entre points. Largeur / profondeur ; ~18–22 marches.",
            placementMode = PlacementMode.Stair,
            createProcedural = () =>
            {
                GameObject g = new GameObject("Stairs");
                float rise = 2.5f;
                float run = Mathf.Clamp(rise * 1.75f, 2.4f, 8f);
                int steps = StairFlightMeshBuilder.ComputeStepCount(run, rise, 0.27f, 18, 22);
                StairFlightMeshBuilder.Rebuild(g.transform, rise, run, 1.1f, steps);
                return g;
            }
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
        ApplyPanelChrome(_panel);

        VerticalLayoutGroup layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 22);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;

        CreateHeaderRow(_panel);

        const string helpTxt =
            "Clic catalogue : sélection / désélection. Sol : snap grille (plus fin dans le lot maison). Poignée centrale verte : déplacer ; molette sur cette poignée = rotation 90° (objets au sol / escalier) ou échange largeur↔hauteur sur mur (porte / fenêtre). Porte & fenêtre posées : rectangle sur le mur (coins + centre vert) ; coins + molette = taille ; fenêtre au placement : molette = hauteur, Shift+molette = largeur ; contraintes sol / plafond. Escalier : tout dans le lot ; R avant pose. Esc = annuler. I = panneau.";
        if (_catalogTmpFont != null)
        {
            TMP_Text help = CreateTmpText(
                "Help",
                _panel,
                helpTxt,
                13f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                _catalogTmpFont);
            help.color = new Color(0.68f, 0.64f, 0.58f, 1f);
            AddLayout(help.gameObject, -1f, 100f);
        }
        else
        {
            Text help = CreateText("Help", _panel, helpTxt, 12, FontStyle.Normal, TextAnchor.UpperLeft);
            help.color = new Color(0.68f, 0.64f, 0.58f, 1f);
            help.horizontalOverflow = HorizontalWrapMode.Wrap;
            AddLayout(help.gameObject, -1f, 100f);
        }

        GameObject sep = new GameObject("Divider");
        sep.transform.SetParent(_panel, false);
        LayoutElement sepLayout = sep.AddComponent<LayoutElement>();
        sepLayout.preferredHeight = 1f;
        sepLayout.flexibleWidth = 1f;
        Image sepImg = sep.AddComponent<Image>();
        sepImg.color = new Color(1f, 1f, 1f, 0.05f);

        if (_catalogTmpFont != null)
        {
            TMP_Text sel = CreateTmpText(
                "Selected",
                _panel,
                string.Empty,
                14f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                _catalogTmpFont);
            sel.color = new Color(0.92f, 0.86f, 0.72f, 1f);
            AddLayout(sel.gameObject, -1f, 26f);
            _selectionLabelGo = sel.gameObject;

            TMP_Text catalogHeading = CreateTmpText(
                "CatalogHeading",
                _panel,
                "Catalogue",
                12f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                _catalogTmpFont);
            catalogHeading.color = new Color(0.58f, 0.52f, 0.46f, 1f);
            AddLayout(catalogHeading.gameObject, -1f, 20f);
        }
        else
        {
            Text sel = CreateText("Selected", _panel, string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleLeft);
            sel.color = new Color(0.92f, 0.86f, 0.72f, 1f);
            AddLayout(sel.gameObject, -1f, 26f);
            _selectionLabelGo = sel.gameObject;

            Text catalogHeading = CreateText("CatalogHeading", _panel, "Catalogue", 11, FontStyle.Bold, TextAnchor.MiddleLeft);
            catalogHeading.color = new Color(0.58f, 0.52f, 0.46f, 1f);
            AddLayout(catalogHeading.gameObject, -1f, 20f);
        }

        for (int i = 0; i < _items.Count; i++)
            CreateItemButton(_items[i], i);
    }

    /// <summary>
    /// Ajoute un <see cref="BoxCollider"/> à la racine si aucun collider dans la hiérarchie (sélection au clic).
    /// </summary>
    public static void EnsureCatalogObjectPickCollider(GameObject root)
    {
        if (root == null)
            return;
        if (root.GetComponentInChildren<Collider>(true) != null)
            return;

        Renderer[] rends = root.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            BoxCollider b = root.AddComponent<BoxCollider>();
            b.center = Vector3.up * 0.5f;
            b.size = Vector3.one;
            return;
        }

        Bounds w = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            w.Encapsulate(rends[i].bounds);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(w.center);
        Vector3 scale = root.transform.lossyScale;
        box.size = new Vector3(
            w.size.x / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
            w.size.y / Mathf.Max(0.001f, Mathf.Abs(scale.y)),
            w.size.z / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
    }

    void CreateHeaderRow(RectTransform panel)
    {
        GameObject headerGo = new GameObject("Header");
        headerGo.transform.SetParent(panel, false);
        Image headerBg = headerGo.AddComponent<Image>();
        headerBg.color = headerBarColor;
        AddLayout(headerGo, -1f, 56f);

        GameObject accent = new GameObject("AccentStripe");
        accent.transform.SetParent(headerGo.transform, false);
        Image accentImg = accent.AddComponent<Image>();
        accentImg.color = headerAccentColor;
        accentImg.raycastTarget = false;
        RectTransform accentRt = accent.GetComponent<RectTransform>();
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(0f, 1f);
        accentRt.pivot = new Vector2(0f, 0.5f);
        accentRt.sizeDelta = new Vector2(6f, 0f);
        accentRt.anchoredPosition = Vector2.zero;

        const string titleRich = "Asset Store\n<size=11><color=#C9B896>Objets et placement</color></size>";
        if (_catalogTmpFont != null)
        {
            TMP_Text title = CreateTmpText(
                "Title",
                headerGo.transform,
                titleRich,
                23f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                _catalogTmpFont);
            title.color = new Color(0.96f, 0.94f, 0.90f, 1f);
            RectTransform titleRt = title.rectTransform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = new Vector2(20f, 2f);
            titleRt.offsetMax = new Vector2(-14f, -2f);
        }
        else
        {
            Text title = CreateText("Title", headerGo.transform, titleRich, 22, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.color = new Color(0.96f, 0.94f, 0.90f, 1f);
            title.supportRichText = true;
            RectTransform titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = new Vector2(20f, 2f);
            titleRt.offsetMax = new Vector2(-14f, -2f);
        }
    }

    void ApplyPanelChrome(RectTransform panelRt)
    {
        Shadow shadow = panelRt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.32f);
        shadow.effectDistance = new Vector2(4f, -4f);

        Outline outline = panelRt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.72f, 0.62f, 0.42f, 0.35f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    static Color CatalogAccentStripeColor(string itemId)
    {
        if (itemId == "door")
            return new Color(0.82f, 0.52f, 0.28f, 1f);
        if (itemId == "window")
            return new Color(0.38f, 0.68f, 0.86f, 1f);
        if (itemId == "bed")
            return new Color(0.72f, 0.48f, 0.82f, 1f);
        if (itemId == "flower_pot")
            return new Color(0.42f, 0.76f, 0.48f, 1f);
        if (itemId == "stairs")
            return new Color(0.55f, 0.48f, 0.72f, 1f);
        return new Color(0.58f, 0.56f, 0.52f, 1f);
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
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

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

    void CreateItemButton(CatalogItem item, int rowIndex)
    {
        GameObject go = new GameObject(item.id + "_Button");
        go.transform.SetParent(_panel, false);

        Image image = go.AddComponent<Image>();
        Color rowBase = (rowIndex & 1) == 0 ? buttonColor : buttonColorAlt;
        image.color = rowBase;

        GameObject stripGo = new GameObject("AccentStripe");
        stripGo.transform.SetParent(go.transform, false);
        stripGo.transform.SetAsFirstSibling();
        Image stripImg = stripGo.AddComponent<Image>();
        stripImg.color = CatalogAccentStripeColor(item.id);
        stripImg.raycastTarget = false;
        RectTransform stripRt = stripGo.GetComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(0f, 1f);
        stripRt.pivot = new Vector2(0f, 0.5f);
        stripRt.sizeDelta = new Vector2(5f, 0f);
        stripRt.anchoredPosition = Vector2.zero;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.06f, 1.02f, 0.98f, 1f);
        colors.pressedColor = new Color(0.88f, 0.84f, 0.80f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.07f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => ToggleSelectCatalogItem(item));

        AddLayout(go, -1f, 70f);

        string labelTxt = item.displayName + "\n" + item.description;
        if (_catalogTmpFont != null)
        {
            TMP_Text label = CreateTmpText(
                "Label",
                go.GetComponent<RectTransform>(),
                labelTxt,
                14f,
                FontStyles.Normal,
                TextAlignmentOptions.Left,
                _catalogTmpFont);
            label.color = new Color(0.94f, 0.90f, 0.84f, 1f);
            RectTransform textRect = label.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 9f);
            textRect.offsetMax = new Vector2(-14f, -9f);
        }
        else
        {
            Text label = CreateText("Label", go.GetComponent<RectTransform>(), labelTxt, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            label.color = new Color(0.94f, 0.90f, 0.84f, 1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform textRect = label.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 9f);
            textRect.offsetMax = new Vector2(-14f, -9f);
        }

        _buttons.Add(button);
        _buttonImages[button] = image;
    }

    static TMP_FontAsset LoadTmpFontForCatalogUi()
    {
        Resources.Load<TMP_Settings>("TMP Settings");

        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/LiberationSans SDF.asset");
#else
        return null;
#endif
    }

    static TMP_Text CreateTmpText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions alignment,
        TMP_FontAsset fontAsset)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);

        TMP_Text tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = fontAsset;
        tmp.text = value;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;
        tmp.enableAutoSizing = false;
        tmp.extraPadding = true;

        RectTransform rect = tmp.rectTransform;
        rect.localScale = Vector3.one;
        return tmp;
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
        text.horizontalOverflow = HorizontalWrapMode.Wrap;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        return text;
    }

    static Font ResolveFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    static void AddLayout(GameObject go, float preferredWidth, float preferredHeight)
    {
        LayoutElement layout = go.AddComponent<LayoutElement>();
        if (preferredWidth > 0f)
            layout.preferredWidth = preferredWidth;
        if (preferredHeight > 0f)
            layout.preferredHeight = preferredHeight;
    }

    void ToggleSelectCatalogItem(CatalogItem item)
    {
        if (_selectedItem == item)
            ClearSelection();
        else
            SelectItem(item);
    }

    void SelectItem(CatalogItem item)
    {
        _selectedItem = item;
        _currentYaw = 0f;
        if (item != null && item.placementMode == PlacementMode.WallWindow)
            ResetWindowPlacementSize();
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
        if (_selectionLabelGo == null)
            return;

        string label = _selectedItem == null
            ? "Sélection : aucune"
            : "Sélection : " + _selectedItem.displayName;

        TMP_Text tmp = _selectionLabelGo.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            tmp.text = label;
            return;
        }

        Text legacy = _selectionLabelGo.GetComponent<Text>();
        if (legacy != null)
            legacy.text = label;
    }

    void RefreshButtonColors()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            Button button = _buttons[i];
            if (button == null || !_buttonImages.TryGetValue(button, out Image image) || image == null)
                continue;

            if (i < _items.Count && _items[i] == _selectedItem)
                image.color = selectedButtonColor;
            else
                image.color = (i & 1) == 0 ? buttonColor : buttonColorAlt;
        }
    }

    void RebuildPreview()
    {
        DestroyPreview();

        if (_selectedItem == null)
            return;

        if (_selectedItem.placementMode == PlacementMode.Stair)
        {
            float rise = ResolveStairTotalRiseMetersForPreview();
            _preview = new GameObject(_selectedItem.displayName + " Preview");
            float run = Mathf.Clamp(rise * 1.75f, 2.4f, 8f);
            int steps = StairFlightMeshBuilder.ComputeStepCount(run, rise, 0.27f, 18, 22);
            StairFlightMeshBuilder.Rebuild(_preview.transform, rise, run, 1.1f, steps);
        }
        else
            _preview = CreateItemInstance(_selectedItem);

        if (_preview == null)
            return;

        _preview.name = _selectedItem.displayName + " Preview";
        if (_selectedItem.placementMode == PlacementMode.WallDoor ||
            _selectedItem.placementMode == PlacementMode.WallWindow)
        {
            _preview.transform.localScale *= decorScale;
            if (_selectedItem.placementMode == PlacementMode.WallWindow)
                ApplyWindowDecorWorldScale(_preview.transform, _windowPlaceWidthMeters, _windowPlaceHeightMeters);
        }

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

        if (_selectedItem.placementMode == PlacementMode.Stair)
        {
            PlaceStairWell(pose);
            return;
        }

        if (_selectedItem.placementMode == PlacementMode.WallDoor)
        {
            PlaceWallDecorWithOpening(pose, WallOpeningKind.Door, 0.9f * decorScale, 2.1f * decorScale);
            return;
        }

        if (_selectedItem.placementMode == PlacementMode.WallWindow)
        {
            PlaceWallDecorWithOpening(pose, WallOpeningKind.Window, _windowPlaceWidthMeters, _windowPlaceHeightMeters);
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

        if (instance.GetComponent<PlacedStairManipulator>() == null)
        {
            if (instance.GetComponent<CatalogPlacedObjectDraggable>() == null)
                instance.AddComponent<CatalogPlacedObjectDraggable>();
            EnsureCatalogObjectPickCollider(instance);
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
        float floorBaseY = pose.floorYWorld;
        float h0 = 0.02f;
        float h1 = Mathf.Clamp01(heightMeters / wallH - 0.02f);
        if (kind == WallOpeningKind.Window)
        {
            float cy = pose.windowCenterYWorld;
            float halfWin = heightMeters * 0.5f;
            float minCy = floorBaseY + windowMinClearanceFromFloorMeters + halfWin;
            float maxCy = floorBaseY + wallH - windowMinClearanceFromCeilingMeters - halfWin;
            cy = Mathf.Clamp(cy, minCy, maxCy);
            float centerFrac = (cy - floorBaseY) / wallH;
            float halfFrac = halfWin / wallH;
            h0 = Mathf.Clamp01(centerFrac - halfFrac);
            h1 = Mathf.Clamp01(centerFrac + halfFrac);
        }

        if (h1 - h0 < 0.03f)
            return;

        int entryIdx = registry.AddOpening(pose.wallSegIndex, pose.wallT, widthMeters, pose.wallSegLength, h0, h1, kind);
        if (entryIdx < 0)
            return;

        wall.ForceRebuildMesh();

        float winW = kind == WallOpeningKind.Window ? widthMeters : 0f;
        float winH = kind == WallOpeningKind.Window ? heightMeters : 0f;

        GameObject root = new GameObject(_selectedItem.displayName);
        PlacedWallOpeningManipulator manip = root.AddComponent<PlacedWallOpeningManipulator>();

        GameObject outerGo = CreateItemInstance(_selectedItem);
        if (outerGo == null)
            return;
        outerGo.name = _selectedItem.displayName + " (extérieur)";
        outerGo.transform.SetParent(root.transform, false);
        outerGo.transform.localPosition = Vector3.zero;
        outerGo.transform.localRotation = Quaternion.identity;
        outerGo.transform.localScale = Vector3.one * decorScale;
        if (kind == WallOpeningKind.Window)
            ApplyWindowDecorWorldScale(outerGo.transform, winW, winH);

        GameObject innerGo = CreateItemInstance(_selectedItem);
        if (innerGo == null)
        {
            Destroy(root);
            return;
        }

        innerGo.name = _selectedItem.displayName + " (intérieur)";
        innerGo.transform.SetParent(root.transform, false);
        innerGo.transform.localPosition = Vector3.zero;
        innerGo.transform.localRotation = Quaternion.identity;
        innerGo.transform.localScale = Vector3.one * decorScale;
        if (kind == WallOpeningKind.Window)
            ApplyWindowDecorWorldScale(innerGo.transform, winW, winH);

        manip.Initialize(
            wall,
            registry,
            entryIdx,
            kind,
            outerGo.transform,
            innerGo.transform,
            decorScale,
            wallSurfaceOffset,
            doorDecorProtrusionExtraMeters,
            windowDecorProtrusionExtraMeters,
            floorBaseY,
            windowMinClearanceFromFloorMeters,
            windowMinClearanceFromCeilingMeters,
            pose.wallNormal,
            0.9f,
            2.1f);
    }

    void PlaceStairWell(PlacementPose pose)
    {
        if (!TryResolveHouseLotEditAtWorldXZ(pose.position, out _, out WallObject lotWall))
            return;

        float rise = ResolveStairTotalRiseMeters(pose.position);
        GameObject instance = new GameObject(_selectedItem.displayName);
        instance.transform.position = pose.position;
        instance.transform.rotation = pose.rotation;

        PlacedStairManipulator manip = instance.AddComponent<PlacedStairManipulator>();
        manip.ConfigureNewPlacement(rise, pose.position.y);

        WallBuildController build = FindFirstObjectByType<WallBuildController>();
        float storyDefault = build != null ? build.AddFloorHeightMeters : 2.5f;

        HouseParquetFloor pf = lotWall.GetComponent<HouseParquetFloor>();
        if (pf == null)
            pf = lotWall.gameObject.AddComponent<HouseParquetFloor>();

        // Aligner avec le contrôleur de construction : même valeur que le menu « maison ».
        if (build != null)
            pf.storeyHeightMeters = build.AddFloorHeightMeters;

        float story = Mathf.Max(0.1f, pf.storeyHeightMeters > 0.01f ? pf.storeyHeightMeters : storyDefault);
        int floorSlabs = Mathf.Max(1, Mathf.RoundToInt(lotWall.height / story));
        if (floorSlabs < 2)
            return;

        // Boîte trémie suivant l’empreinte réelle de l’escalier (rotation comprise).
        const float cutPadding = 0.12f;
        manip.ComputeFootprintAabbXZ(cutPadding, out Vector2 cutCenter, out Vector2 halfExtents);
        halfExtents.x = Mathf.Max(halfExtents.x, stairCutoutHalfWidthMeters);
        halfExtents.y = Mathf.Max(halfExtents.y, stairCutoutHalfDepthMeters);

        // Autant de dalles traversées que de « paliers » franchis par la volée (souvent 1 = premier étage).
        int decksToPierce = Mathf.Clamp(Mathf.CeilToInt(rise / story), 1, floorSlabs - 1);
        for (int slabIdx = 1; slabIdx <= decksToPierce; slabIdx++)
            pf.AddSlabHorizontalCutout(slabIdx, cutCenter, halfExtents);
    }

    public static bool TryResolveHouseLotEditAtWorldXZ(Vector3 world, out WallEditShape edit, out WallObject wall)
    {
        edit = null;
        wall = null;

        WallEditShape[] edits = FindObjectsByType<WallEditShape>(FindObjectsSortMode.None);
        for (int i = 0; i < edits.Length; i++)
        {
            WallEditShape e = edits[i];
            if (e == null || e.wall == null)
                continue;

            HouseParquetFloor floor = e.wall.GetComponent<HouseParquetFloor>();
            if (floor == null || !floor.IsDesignatedHouseLot)
                continue;

            if (!e.ContainsWorldPointInClosedLotFootprintXZ(world, 0f))
                continue;

            edit = e;
            wall = e.wall;
            return true;
        }

        return false;
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

        WallEditShape resolvedLotEdit = null;
        if (TryResolveHouseLotEditAtWorldXZ(point, out WallEditShape edit, out WallObject lotWall))
        {
            resolvedLotEdit = edit;
            HouseParquetFloor pf = lotWall.GetComponent<HouseParquetFloor>();
            float yOff = pf != null ? pf.yOffsetAboveBase : 0f;
            point.y = edit.shapeY + yOff;
        }

        Quaternion yawRot = Quaternion.Euler(0f, _currentYaw, 0f);
        if (_selectedItem != null && _selectedItem.placementMode == PlacementMode.Stair)
        {
            if (resolvedLotEdit == null)
                return false;

            float rise = ResolveStairTotalRiseMeters(point);
            float run = Mathf.Clamp(rise * 1.75f, 1.4f, 14f);
            float hw = Mathf.Clamp(0.55f, 0.32f, 1.35f);
            if (!AreStairFootprintCornersInsideLot(point, yawRot, hw, run, resolvedLotEdit))
                return false;
        }

        pose.position = point;
        pose.rotation = yawRot;
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
        float wallH = Mathf.Max(0.1f, wall.height);
        bool isWindow = item.placementMode == PlacementMode.WallWindow;

        float wallGridStep = GetInteriorFineLatticeStepMeters();
        float distAlong = tAlong * segLen;
        distAlong = Mathf.Round(distAlong / wallGridStep) * wallGridStep;
        distAlong = Mathf.Clamp(distAlong, 0f, segLen);
        float snappedT = segLen > 1e-5f ? distAlong / segLen : 0f;

        int count = wall.ControlPointCount;
        Vector3 a3 = wall.GetControlPointWorld(segIndex);
        Vector3 b3 = wall.GetControlPointWorld((segIndex + 1) % count);
        centerlinePoint = Vector3.Lerp(a3, b3, snappedT);
        floorY = Mathf.Lerp(a3.y, b3.y, snappedT);

        float winHalfH = _windowPlaceHeightMeters * 0.5f;
        float minWinCy = floorY + windowMinClearanceFromFloorMeters + winHalfH;
        float maxWinCy = floorY + wallH - windowMinClearanceFromCeilingMeters - winHalfH;

        float windowCy = floorY + windowCenterHeight;
        if (isWindow)
        {
            if (maxWinCy < minWinCy + 0.05f)
                return false;

            float denom = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denom) > 1e-4f)
            {
                Vector3 planePt = new Vector3(centerlinePoint.x, floorY, centerlinePoint.z);
                float tr = Vector3.Dot(planePt - ray.origin, normal) / denom;
                tr = Mathf.Clamp(tr, -800f, 800f);
                Vector3 wallPt = ray.origin + ray.direction * tr;
                windowCy = wallPt.y;
            }

            float relY = windowCy - floorY;
            relY = Mathf.Round(relY / wallGridStep) * wallGridStep;
            windowCy = floorY + relY;
            windowCy = Mathf.Clamp(windowCy, minWinCy, maxWinCy);
        }

        float anchorY = isWindow ? windowCy : floorY;
        pose.windowCenterYWorld = windowCy;
        pose.position = new Vector3(centerlinePoint.x, anchorY, centerlinePoint.z) + normal * (halfThickness + wallSurfaceOffset);
        pose.rotation = Quaternion.LookRotation(normal, Vector3.up);
        pose.wall = wall;
        pose.wallSegIndex = segIndex;
        pose.wallT = snappedT;
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
        if (IsWorldPointInsideAnyDesignatedHouseLotXZ(point) &&
            _drawInput != null &&
            _drawInput.TryGetMainGridLatticeStepXZ(out float mainStep, out Vector2 origin))
        {
            float fine = mainStep / ResolveInteriorGridFinenessMul();
            float x = Mathf.Round((point.x - origin.x) / fine) * fine + origin.x;
            float z = Mathf.Round((point.z - origin.y) / fine) * fine + origin.y;
            return new Vector3(x, point.y, z);
        }

        if (_drawInput != null && _drawInput.enableGridSnap && _drawInput.snapToHierarchicalVisualGrid)
            return _drawInput.SnapWorldToHierarchicalLeafCenter(point);

        if (_gridManager != null && _gridManager.TryGetCellAtWorld(point, out HierarchicalGridNode cell))
        {
            Vector3 c = _gridManager.GetCellCenterWorld(cell);
            return new Vector3(c.x, point.y, c.z);
        }

        return point;
    }

    float GetInteriorFineLatticeStepMeters()
    {
        if (_drawInput != null && _drawInput.TryGetMainGridLatticeStepXZ(out float step, out _))
            return step / ResolveInteriorGridFinenessMul();
        return 0.2f;
    }

    float ResolveInteriorGridFinenessMul()
    {
        if (_drawInput != null)
            return Mathf.Max(1.1f, _drawInput.interiorFineGridFinenessMul);
        return 2f;
    }

    void ApplyWindowDecorWorldScale(Transform t, float openingWidthMeters, float openingHeightMeters)
    {
        const float refW = 1.15f;
        const float refH = 0.88f;
        t.localScale = Vector3.Scale(t.localScale, new Vector3(
            openingWidthMeters / (refW * decorScale),
            openingHeightMeters / (refH * decorScale),
            1f));
    }

    float ResolveStairTotalRiseMetersForPreview()
    {
        if (!TryGetGridPlanePlacementPoint(out Vector3 p))
            return 2.5f;
        return ResolveStairTotalRiseMeters(p);
    }

    static float ResolveStairTotalRiseMeters(Vector3 worldXZ)
    {
        Vector3 test = worldXZ;
        if (!TryResolveHouseLotEditAtWorldXZ(test, out _, out WallObject lotWall))
            return 2.5f;

        HouseParquetFloor pf = lotWall.GetComponent<HouseParquetFloor>();
        return Mathf.Max(0.1f, pf != null ? pf.storeyHeightMeters : 2.5f);
    }

    Vector3 SnapWallHitToGrid(Vector3 hitPoint)
    {
        Vector3 snapped = SnapWorldToGrid(hitPoint);
        snapped.y = hitPoint.y;
        return snapped;
    }

    public static bool IsWorldPointInsideAnyDesignatedHouseLotXZ(Vector3 world)
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

    /// <summary>
    /// Les 4 coins au sol de l’empreinte escalier (+Z local = profondeur de volée) doivent tous être dans le polygone du lot.
    /// </summary>
    public static bool AreStairFootprintCornersInsideLot(
        Vector3 stairRootWorld,
        Quaternion rotation,
        float halfWidthMeters,
        float runLengthMeters,
        WallEditShape lotEdit)
    {
        if (lotEdit == null)
            return false;

        Quaternion q = rotation;
        Vector3 r = stairRootWorld;
        Vector3 c0 = r + q * new Vector3(-halfWidthMeters, 0f, 0f);
        Vector3 c1 = r + q * new Vector3(halfWidthMeters, 0f, 0f);
        Vector3 c2 = r + q * new Vector3(-halfWidthMeters, 0f, runLengthMeters);
        Vector3 c3 = r + q * new Vector3(halfWidthMeters, 0f, runLengthMeters);

        return lotEdit.ContainsWorldPointInClosedLotFootprintXZ(c0, 0f)
               && lotEdit.ContainsWorldPointInClosedLotFootprintXZ(c1, 0f)
               && lotEdit.ContainsWorldPointInClosedLotFootprintXZ(c2, 0f)
               && lotEdit.ContainsWorldPointInClosedLotFootprintXZ(c3, 0f);
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
