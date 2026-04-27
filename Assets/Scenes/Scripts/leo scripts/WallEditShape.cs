using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public partial class WallEditShape : MonoBehaviour, IControlPointProvider, IControlPointPathProvider
{
    public enum ShapeKind
    {
        Free,
        Rectangle,
        Ellipse,
        Triangle,
        /// <summary>Open arc (semi-circle, quarter arc, etc.) — not a closed loop.</summary>
        OpenArc
    }

    [Header("References")]
    public WallObject wall;

    [Header("Detected Shape")]
    public ShapeKind shapeKind = ShapeKind.Free;

    [Header("Bounds Shape Data")]
    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;
    public float shapeY = 0f;

    [Header("Rectangle Shape Data")]
    public Vector2 rectangleOriginXZ;
    public Vector2 rectangleAxisX = Vector2.right;
    public Vector2 rectangleAxisY = Vector2.up;
    public float rectangleMinX = -0.5f;
    public float rectangleMaxX = 0.5f;
    public float rectangleMinY = -0.5f;
    public float rectangleMaxY = 0.5f;

    [Header("Ellipse")]
    public int ellipseWallResolution = 64;
    [Tooltip("Rotation du cercle / ovale dans le plan XZ (radians), autour du centre de la boîte.")]
    public float ellipseRotationRad;

    [Header("Center handle — molette")]
    [Tooltip("Degrés de rotation par unité Unity Mouse ScrollWheel lorsque le point central est sélectionné.")]
    [Range(1f, 36f)] public float centerScrollRotationDegrees = 8f;

    [Header("Élévation (mur depuis menu lot maison)")]
    [Tooltip("Si vrai : la molette déplace tout le tracé sur Y (haut/bas) tant que ce mur est sélectionné — sans poignée centre rotation.")]
    public bool allowVerticalScrollElevation;
    [Tooltip("Élévation (m) par unité de molette : Shift + molette sur la poignée centrale d’un mur intérieur ouvert (la molette seule fait tourner comme les autres formes).")]
    [Min(0.01f)] public float verticalScrollElevationMetersPerWheelUnit = 5f;

    [Header("Murs intérieurs (limite au lot)")]
    [Tooltip("Lot fermé : ce mur ouvert ne peut pas sortir du contour de ce lot (plan XZ). Rempli automatiquement par « Ajouter un mur ».")]
    public WallEditShape interiorWallsStayInsideLot;

    [Header("Free Shape")]
    public List<Vector3> freeControlPoints = new List<Vector3>();

    [Header("Triangle Shape Data")]
    public List<Vector3> triangleControlPoints = new List<Vector3>();

    [Header("Open Arc Shape Data")]
    public Vector2 arcCenterXZ;
    public float arcRadius = 1f;
    public float arcStartRad;
    public float arcEndRad;
    public bool arcCounterClockwise = true;
    [Range(24, 196)] public int openArcWallResolution = 40;

    [Header("Free Shape Settings")]
    public float freeHandleSpacing = 2.5f;
    public int minFreeHandles = 4;
    public int maxFreeHandles = 12;
    public int freeWallResolution = 64;

    [Header("Preserve Drawn Freeform")]
    public bool preserveInitialFreeDrawnPath = true;
    [Range(0.02f, 1.0f)] public float rawFreeMinPointSpacing = 0.08f;

    [Header("Closed Free Shapes")]
    [Range(0.2f, 1.0f)] public float closedFreeHandleSpacingMultiplier = 0.5f;
    [Range(6, 48)] public int closedFreeMinHandles = 10;
    [Range(8, 64)] public int closedFreeMaxHandles = 24;
    [Range(32, 256)] public int closedFreeWallResolution = 128;
    [Range(0, 4)] public int closedFreeSmoothIterations = 0;

    [Header("Closed Free Shape Safety")]
    [Tooltip("Distance minimale entre deux points consécutifs pour éviter les micro-segments")]
    [Range(0.02f, 1.0f)] public float minClosedSegmentLength = 0.18f;

    [Tooltip("Lot orthogonal fusionné : si le sommet déplacé est à moins de cette distance (XZ) d’un autre sommet, le rejet par auto-croisement est ignoré (fusion rentrant/saillant). Complété par une tolérance le long des arêtes horizontales/verticales — sinon ~0,65 m plafonne le déplacement sur un long mur.")]
    [SerializeField, Min(0.05f)] float orthogonalEditMergeProximitySkipIntersectionM = 0.65f;

    [Tooltip("Distance minimale (XZ) entre coin interne et coin externe (paire rentrant/saillant) : jamais superposés ; appliqué au drag et après finalisation. 0 = désactivé.")]
    [SerializeField, Min(0f)] float orthogonalVertexAvoidanceRadiusXZ = 0.04f;

    [Tooltip("En déplaçant un coin A de la paire interne/externe, A ne peut pas s’approcher à moins de cette distance (XZ) de l’autre coin B. Combiné avec la distance minimale générale (le max des deux s’applique à la paire). 0 = seulement la valeur générale ci-dessus.")]
    [SerializeField, Min(0f)] float orthogonalStackedPartnerMinDistanceXZ = 0.001f;

    [Tooltip("Paire interne/externe : le déplacement couplé (A et B bougent ensemble) ne s’active que lorsque la distance XZ entre le coin saisi et le coin partenaire B est ≤ cette valeur (mètres). Ex. 0,002 = 2 mm (≈0,2 cm). 0 = actif dès qu’une paire rentrant/saillant existe (comportement précédent).")]
    [SerializeField, Min(0f)] float orthogonalStackedPairActivationDistanceXZ = 0.002f;

    [Tooltip("Lorsque le déplacement couplé est actif (A près de B), ne conserver que la composante « haut/bas » à l’écran (verticale de la caméra projetée sur le sol XZ) — pas gauche/droite. Si faux, déplacement libre dans le plan XZ.")]
    [SerializeField] bool orthogonalStackedPairNearPartnerOnlyAllowScreenVerticalDrag = true;

    [Tooltip("Lot orthogonal — « diamètre » de détection pour fusionner plusieurs sommets en un : toute chaîne d’arêtes consécutives (XZ) plus courtes que ce seuil est fusionnée en un seul point au relâchement. Augmenter = regrouper des points plus éloignés. Ne fusionne pas la paire rentrant/saillant volontaire. 0 = désactivé.")]
    [SerializeField, Min(0f)]
    [Range(0f, 1f)]
    float orthogonalConsecutiveVertexMergeDistanceXZ = 0.22f;

    [Tooltip("Lot orthogonal : sur chaque mur (arête entre deux coins), exactement une poignée au milieu si le mur est assez long (voir distance minimale de segment) ; sinon aucune. S’il en manque une, elle est ajoutée ; s’il y en a plusieurs, les surplus sont retirés.")]
    [SerializeField] bool orthogonalEnforceExactlyOneMidpointPerWallFace = true;

    [Tooltip("Poignée milieu de mur : le segment ne se déplace que perpendiculairement au mur (épaisseur du lot, avant/arrière), pas le long du mur (gauche/droite).")]
    [SerializeField] bool orthogonalMidHandleDragPerpendicularToWallOnly = true;
    [Tooltip("Poignée milieu de mur : si vrai, tout le mur (tronçon colinéaire) glisse comme une bande rigide — comme les milieux d’arête d’un rectangle. Si faux (ancien), seul le point milieu se déplace (contour déformé, peu intuitif).")]
    [SerializeField] bool orthogonalMidHandleDragsWholeWallRun = true;

    [Tooltip("Lot orthogonal : au relâchement d’une poignée coin, recaler chaque coin concerné (y compris les coins superposés au même XZ) sur la grille feuille (WallDrawInput). Les poignées milieu de mur ne sont jamais recalées.")]
    [SerializeField] bool snapOrthogonalVerticesToGridLeafCenterOnRelease = true;

    [Tooltip("Avec le snap au relâchement : si vrai, grille 3×3 par carré feuille (4 coins, 4 milieux d’arête, 1 centre) — voir SnapWorldToVisibleLeafNinePointLattice. Si faux, uniquement le centre de la cellule feuille.")]
    [SerializeField] bool snapOrthogonalVerticesToLeafNinePointLatticeOnRelease = true;

    [Tooltip("Si une boucle fermée brute est invalide, on retombe sur un contour sûr")]
    public bool useSafeClosedFallback = true;

    [Header("Open Free Shapes")]
    [Range(1.0f, 1.5f)] public float mostlyStraightArcRatioThreshold = 1.06f;
    [Range(0f, 30f)] public float mostlyStraightAverageTurnThreshold = 8f;
    [Range(0, 4)] public int openFreeSmoothIterations = 1;

    private bool _closedLoop = true;
    private readonly List<Vector3> _freeRawPath = new List<Vector3>();
    readonly List<Vector2> _scratchFootprintRing = new List<Vector2>();
    readonly List<WallEditShape> _scratchPeerInteriorWalls = new List<WallEditShape>(8);
    private bool _freePathWasEdited = false;
    bool _hasLastValidOpenInteriorTwoPointSegment;
    Vector3 _lastValidOpenInteriorTwoPointA;
    Vector3 _lastValidOpenInteriorTwoPointB;
    /// <summary>Si vrai : boucle libre = polyline droite exacte (fusion L/U), sans validation/Catmull/convexe.</summary>
    bool _mergeFootprintUseExactPolyline;

    /// <summary>
    /// Lot fusionné édité en polyline droite : le mur suit les segments entre poignées (pas Catmull),
    /// milieux de segment se déplacent comme les arêtes d’un rectangle (perpendiculaire au mur).
    /// </summary>
    bool _closedFreeOrthogonalPolylineMode;

    /// <summary>
    /// Contour fermé droit (lots L/U) : réutilisé pour éviter des milliers d’allocations List par seconde
    /// (<see cref="ControlPointOverlayManager"/> utilise <see cref="GetOverlayPathWorld"/> pour les liens ; cache interne pour le preview droit).
    /// </summary>
    readonly List<Vector3> _straightClosedPreviewCache = new List<Vector3>(64);

    bool _straightClosedPreviewDirty = true;

    readonly List<int> _ringEdgeMidScratch = new List<int>(32);

    /// <summary>Coins (pas milieux) proches d’un même XZ pour le snap grille au relâchement.</summary>
    readonly List<int> _gridSnapReleaseScratch = new List<int>(16);

    /// <summary>Copie de secours du contour orthogonal avant une édition ; rollback si auto-croisement.</summary>
    readonly List<Vector3> _orthogonalRingEditBackup = new List<Vector3>(64);

    /// <summary>Classification du drag orthogonal au PointerDown : 3 cas seulement (coin, mur rigide, paire superposée).</summary>
    enum OrthoDragKind
    {
        None,
        ClassicCorner,
        RigidWallRun,
        StackedPair,
    }

    OrthoDragKind _orthoDragKind;

    /// <summary>Indice de la poignée saisie (ou -1 pour pivot / pas de stroke).</summary>
    int _orthoDragStrokeVertexIndex = -1;

    /// <summary>CAS 3 : autre coin au même XZ ; -1 si absent.</summary>
    int _orthoStackPartnerIndex = -1;

    /// <summary>Repère mur figé au PointerDown : u = le long du mur, v = profondeur (extérieur = +v).</summary>
    Vector2 _orthoWallFrameU;

    Vector2 _orthoWallFrameV;

    /// <summary>
    /// Appeler au début du drag d'une poignée de contour (PointerDown).
    /// <paramref name="vertexIndex"/> : sommet déplacé, ou -1 (déplacement global du lot par le pivot).
    /// </summary>
    public void NotifyOrthogonalVertexDragStrokeStarted(int vertexIndex = -1)
    {
        _orthoDragKind = OrthoDragKind.None;
        _orthoDragStrokeVertexIndex = vertexIndex;
        _orthoStackPartnerIndex = -1;
        _orthoWallFrameU = Vector2.right;
        _orthoWallFrameV = Vector2.up;

        if (vertexIndex < 0 || freeControlPoints == null || vertexIndex >= freeControlPoints.Count)
            return;

        if (!_closedFreeOrthogonalPolylineMode || !UsesMergedLotOrthogonalHandles)
        {
            _orthoDragKind = OrthoDragKind.ClassicCorner;
            return;
        }

        // CAS 2 > CAS 3 > CAS 1
        if (IsRingVertexStraightMidXZ(vertexIndex))
        {
            _orthoDragKind = OrthoDragKind.RigidWallRun;
            BuildWallFrameForOrthoDrag(vertexIndex, OrthoDragKind.RigidWallRun, out _orthoWallFrameU, out _orthoWallFrameV);
            return;
        }

        if (TryGetStackedCornerPartner(vertexIndex, out int partner))
        {
            _orthoStackPartnerIndex = partner;
            bool closeEnough = IsOrthogonalStackedPairWithinActivationDistance(vertexIndex, partner);
            _orthoDragKind = closeEnough ? OrthoDragKind.StackedPair : OrthoDragKind.ClassicCorner;
            BuildWallFrameForOrthoDrag(vertexIndex, _orthoDragKind, out _orthoWallFrameU, out _orthoWallFrameV);
            return;
        }

        _orthoDragKind = OrthoDragKind.ClassicCorner;
        BuildWallFrameForOrthoDrag(vertexIndex, OrthoDragKind.ClassicCorner, out _orthoWallFrameU, out _orthoWallFrameV);
    }

    /// <summary>
    /// Appeler à la fin du drag (poignée ou pivot du lot). Align + fusion des sommets consécutifs trop proches.
    /// À exécuter <b>avant</b> le recalage des coins sur la grille (<see cref="SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(WallDrawInput, Vector3, float)"/>).
    /// <paramref name="lastDraggedVertexIndex"/> : sommet déplacé, ou -1 après déplacement global du pivot.
    /// </summary>
    public void NotifyOrthogonalVertexDragStrokeEnded(int lastDraggedVertexIndex = -1)
    {
        _orthoDragKind = OrthoDragKind.None;
        _orthoDragStrokeVertexIndex = -1;
        _orthoStackPartnerIndex = -1;

        // Fusion / alignement des sommets proches : uniquement sur le mur périmètre du lot (pas les murs intérieurs).
        if (_closedLoop &&
            _closedFreeOrthogonalPolylineMode &&
            interiorWallsStayInsideLot == null &&
            freeControlPoints != null &&
            freeControlPoints.Count > 3)
        {
            bool changed = TryAlignSuperposedOrthogonalRingVerticesAfterHandleDrag();
            if (orthogonalConsecutiveVertexMergeDistanceXZ > 1e-6f)
                changed |= TryCollapseConsecutiveNearVerticesOrthogonalRing(orthogonalConsecutiveVertexMergeDistanceXZ);

            if (changed)
            {
                FinalizeOrthogonalFreeRingAfterControlEdit();
                ApplyToWall();
            }
        }
    }

    /// <summary>
    /// Regroupe les sommets consécutifs trop proches (align + suppression d’arête courte), puis finalise le contour.
    /// À appeler hors drag — sinon utiliser <see cref="ShouldDeferNearbyControlPointMerge"/>.
    /// </summary>
    public bool TryMergeNearbyColocatedControlPoints()
    {
        if (ShouldDeferNearbyControlPointMerge())
            return false;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode || interiorWallsStayInsideLot != null ||
            freeControlPoints == null || freeControlPoints.Count <= 3)
            return false;

        bool changed = TryAlignSuperposedOrthogonalRingVerticesAfterHandleDrag();
        if (orthogonalConsecutiveVertexMergeDistanceXZ > 1e-6f)
            changed |= TryCollapseConsecutiveNearVerticesOrthogonalRing(orthogonalConsecutiveVertexMergeDistanceXZ);

        if (!changed)
            return false;

        FinalizeOrthogonalFreeRingAfterControlEdit();
        ApplyToWall();
        return true;
    }

    /// <summary>
    /// Vrai tant qu’une poignée de sommet est déplacée (PointerDown → PointerUp). Faux pour le déplacement du pivot lot entier.
    /// À utiliser pour ne pas fusionner des points proches pendant le drag.
    /// </summary>
    public static bool IsVertexControlPointDragActive => ControlPointHandleUI.IsVertexHandleDragActive;

    /// <summary>
    /// Indique si la fusion / regroupement de sommets proches doit être reportée (drag de poignée en cours).
    /// </summary>
    public static bool ShouldDeferNearbyControlPointMerge() => ControlPointHandleUI.IsVertexHandleDragActive;

    /// <summary>
    /// Au relâchement : recale chaque coin du groupe sur la grille feuille (centre ou 9 points selon
    /// <see cref="snapOrthogonalVerticesToLeafNinePointLatticeOnRelease"/>). Ne fait rien pour une poignée « milieu de mur ».
    /// </summary>
    public bool SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(int releasedVertexIndex, WallDrawInput drawInput)
    {
        int n = freeControlPoints != null ? freeControlPoints.Count : 0;
        if (n < 3 || releasedVertexIndex < 0 || releasedVertexIndex >= n)
            return false;
        if (IsRingVertexStraightMidXZ(releasedVertexIndex))
            return false;

        Vector3 anchor = GetControlPointWorld(releasedVertexIndex);
        return SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(drawInput, anchor, 0.012f);
    }

    /// <summary>
    /// Même logique que la surcharge à indice, avec une ancre monde explicite (ex. position du sommet <b>avant</b>
    /// fusion auto des points, car les indices peuvent changer après <see cref="NotifyOrthogonalVertexDragStrokeEnded"/>).
    /// <paramref name="coincidentCornerEpsXZ"/> : rayon (m) pour regrouper les coins à recaler (plus large après fusion).
    /// </summary>
    public bool SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(
        WallDrawInput drawInput,
        Vector3 anchorWorldBeforeMerge,
        float coincidentCornerEpsXZ = 0.22f)
    {
        if (!snapOrthogonalVerticesToGridLeafCenterOnRelease || drawInput == null || freeControlPoints == null)
            return false;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode || shapeKind != ShapeKind.Free)
            return false;
        if (interiorWallsStayInsideLot != null)
            return false;
        if (!drawInput.enableGridSnap)
            return false;

        int n = freeControlPoints.Count;
        if (n < 3)
            return false;

        GatherCoincidentCornerIndicesNearWorldXZ(anchorWorldBeforeMerge, _gridSnapReleaseScratch, coincidentCornerEpsXZ);
        if (_gridSnapReleaseScratch.Count == 0)
            return false;

        bool changed = false;
        for (int s = 0; s < _gridSnapReleaseScratch.Count; s++)
        {
            int i = _gridSnapReleaseScratch[s];
            Vector3 p = freeControlPoints[i];
            Vector3 snapped = snapOrthogonalVerticesToLeafNinePointLatticeOnRelease
                ? drawInput.SnapWorldToVisibleLeafNinePointLattice(p)
                : drawInput.SnapWorldToHierarchicalLeafCenter(p);
            snapped.y = shapeY;
            float dx = snapped.x - p.x;
            float dz = snapped.z - p.z;
            if (dx * dx + dz * dz < 1e-12f)
                continue;

            freeControlPoints[i] = snapped;
            changed = true;
        }

        if (!changed)
            return false;

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        FinalizeOrthogonalFreeRingAfterControlEdit();
        ComputeBounds(BuildFreePreviewPath() ?? freeControlPoints);
        ApplyToWall();
        return true;
    }

    void GatherCoincidentCornerIndicesNearWorldXZ(Vector3 anchorWorld, List<int> sink, float coincidentEps = 0.012f)
    {
        sink.Clear();
        if (freeControlPoints == null)
            return;

        int n = freeControlPoints.Count;
        float epsSq = coincidentEps * coincidentEps;

        for (int i = 0; i < n; i++)
        {
            if (IsRingVertexStraightMidXZ(i))
                continue;

            Vector3 q = GetControlPointWorld(i);
            float dx = q.x - anchorWorld.x;
            float dz = q.z - anchorWorld.z;
            if (dx * dx + dz * dz <= epsSq)
                sink.Add(i);
        }
    }

    /// <summary>
    /// Poignée « milieu de mur » (pas un coin) sur le contour orthogonal fusionné.
    /// </summary>
    public bool IsOrthogonalWallMidHandleIndex(int index)
    {
        return freeControlPoints != null &&
               index >= 0 &&
               index < freeControlPoints.Count &&
               IsRingVertexStraightMidXZ(index);
    }

    /// <summary>
    /// Poignée centrale dédiée d’un mur libre ouvert (segment / polyline ouverte) pour déplacement global + élévation.
    /// </summary>
    public bool IsOpenFreeVerticalCenterHandleIndex(int index)
    {
        if (shapeKind != ShapeKind.Free || _closedLoop || !allowVerticalScrollElevation || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        return n >= 2 && index == n;
    }

    /// <summary>
    /// Compat : même logique que <see cref="SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease"/> ;
    /// met à jour <paramref name="worldPos"/> pour l’indice donné après snap.
    /// À utiliser après une fusion au relâchement si l’indice peut avoir changé : préférer
    /// <see cref="SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(WallDrawInput, Vector3, float)"/>.
    /// </summary>
    public bool TrySnapReleasedOrthogonalVertexToGridLeafCenter(int index, WallDrawInput drawInput, ref Vector3 worldPos)
    {
        if (!SnapReleasedOrthogonalCornerHandlesToGridLeafCentersOnRelease(index, drawInput))
            return false;
        if (freeControlPoints != null && index >= 0 && index < freeControlPoints.Count)
            worldPos = GetControlPointWorld(index);
        return true;
    }

    /// <summary>
    /// Autre coin de la paire interne/externe (rentrant+saillant) la plus proche en XZ — CAS 3, même si les deux ne sont plus superposés.
    /// </summary>
    bool TryGetStackedCornerPartner(int index, out int partner)
    {
        partner = -1;
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;
        int n = freeControlPoints.Count;
        if (n < 3 || index < 0 || index >= n)
            return false;
        if (IsRingVertexStraightMidXZ(index))
            return false;

        float bestSq = float.MaxValue;
        Vector3 p = GetControlPointWorld(index);
        Vector2 pi = new Vector2(p.x, p.z);

        for (int j = 0; j < n; j++)
        {
            if (j == index)
                continue;
            if (IsRingVertexStraightMidXZ(j))
                continue;
            if (!IsReflexSalientStackableCornerPair(index, j))
                continue;

            Vector3 q = GetControlPointWorld(j);
            float dx = q.x - p.x;
            float dz = q.z - p.z;
            float s = dx * dx + dz * dz;
            if (s < bestSq)
            {
                bestSq = s;
                partner = j;
            }
        }

        return partner >= 0;
    }

    /// <summary>
    /// Vrai si le déplacement couplé paire interne/externe est autorisé : seuil 0 = toujours ; sinon distance XZ ≤ <see cref="orthogonalStackedPairActivationDistanceXZ"/>.
    /// </summary>
    bool IsOrthogonalStackedPairWithinActivationDistance(int indexA, int indexB)
    {
        if (freeControlPoints == null)
            return false;
        if (orthogonalStackedPairActivationDistanceXZ <= 0f)
            return true;

        Vector3 pa = GetControlPointWorld(indexA);
        Vector3 pb = GetControlPointWorld(indexB);
        float dx = pb.x - pa.x;
        float dz = pb.z - pa.z;
        float maxSq = orthogonalStackedPairActivationDistanceXZ * orthogonalStackedPairActivationDistanceXZ;
        return dx * dx + dz * dz <= maxSq;
    }

    /// <summary>
    /// Sur le sol XZ, ne garde que l’axe « haut/bas » écran : projection du vecteur <paramref name="deltaXZ"/> sur la direction (up.x, up.z) de <see cref="Camera.main"/>.
    /// </summary>
    static Vector2 ProjectOrthogonalStackedPairDeltaToScreenVerticalOnly(Vector2 deltaXZ)
    {
        Camera cam = Camera.main;
        Vector2 axis;
        if (cam != null)
        {
            Vector3 up = cam.transform.up;
            axis = new Vector2(up.x, up.z);
            if (axis.sqrMagnitude < 1e-10f)
                axis = Vector2.up;
            else
                axis.Normalize();
        }
        else
            axis = Vector2.up;

        return axis * Vector2.Dot(deltaXZ, axis);
    }

    /// <summary>
    /// u = tangente le long du mur (arête incidente dominante) ; v = normale sortante (profondeur), perpendiculaire à u.
    /// </summary>
    void BuildWallFrameForOrthoDrag(int index, OrthoDragKind kind, out Vector2 u, out Vector2 v)
    {
        u = Vector2.right;
        v = Vector2.up;
        if (freeControlPoints == null)
            return;

        int n = freeControlPoints.Count;
        if (n < 3 || index < 0 || index >= n)
            return;

        int iPrev = (index - 1 + n) % n;
        int iNext = (index + 1) % n;
        Vector3 prev = freeControlPoints[iPrev];
        Vector3 curr = freeControlPoints[index];
        Vector3 next = freeControlPoints[iNext];
        Vector2 eIn = new Vector2(curr.x - prev.x, curr.z - prev.z);
        Vector2 eOut = new Vector2(next.x - curr.x, next.z - curr.z);

        Vector2 tang;
        if (kind == OrthoDragKind.RigidWallRun)
        {
            tang = eOut.sqrMagnitude >= eIn.sqrMagnitude ? eOut : eIn;
            if (tang.sqrMagnitude < 1e-10f)
                tang = eIn.sqrMagnitude > eOut.sqrMagnitude ? eIn : eOut;
        }
        else
        {
            tang = eIn.sqrMagnitude >= eOut.sqrMagnitude ? eIn : eOut;
            if (tang.sqrMagnitude < 1e-10f)
                tang = eOut.sqrMagnitude > eIn.sqrMagnitude ? eOut : eIn;
        }

        if (tang.sqrMagnitude < 1e-12f)
            return;

        u = tang.normalized;
        Vector2 perpA = new Vector2(-u.y, u.x);
        Vector2 perpB = new Vector2(u.y, -u.x);
        Vector3 c = GetClosedFreeLotCentroidWorld();
        Vector2 toCentroid = new Vector2(c.x - curr.x, c.z - curr.z);
        float da = Vector2.Dot(perpA, toCentroid);
        float db = Vector2.Dot(perpB, toCentroid);
        if (Mathf.Abs(da) >= Mathf.Abs(db))
            v = da >= 0f ? -perpA : perpA;
        else
            v = db >= 0f ? -perpB : perpB;
    }

    /// <summary>
    /// Superpose les poignées quasi confondues, puis supprime les doublons non volontaires (voir <see cref="TryRemoveOrthogonalRingDuplicateVerticesNonStackableSameXZ"/>).
    /// </summary>
    bool TryAlignSuperposedOrthogonalRingVerticesAfterHandleDrag()
    {
        if (interiorWallsStayInsideLot != null)
            return false;
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return false;

        bool changed = AlignOrthogonalRingVerticesToExactSuperposition(0.02f);
        changed |= TryRemoveOrthogonalRingDuplicateVerticesNonStackableSameXZ(0.02f);
        if (changed)
        {
            _freePathWasEdited = true;
            InvalidateStraightClosedPreviewCache();
        }

        return changed;
    }

    /// <summary>
    /// Pour chaque paire (i,j) avec i&lt;j, si les points sont à moins de <paramref name="alignEps"/> m en XZ,
    /// recopie la position du plus petit indice sur l’autre. La suppression des doublons se fait ensuite par
    /// <see cref="TryRemoveOrthogonalRingDuplicateVerticesNonStackableSameXZ"/> (sauf paires rentrant/saillant).
    /// </summary>
    bool AlignOrthogonalRingVerticesToExactSuperposition(float alignEps)
    {
        if (interiorWallsStayInsideLot != null)
            return false;
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return false;

        float alignEpsSq = alignEps * alignEps;
        bool changed = false;
        int n = freeControlPoints.Count;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < j; i++)
            {
                if (IsReflexSalientStackableCornerPair(i, j))
                    continue;

                Vector3 d = freeControlPoints[j] - freeControlPoints[i];
                float s = d.x * d.x + d.z * d.z;
                if (s > alignEpsSq || s < 1e-20f)
                    continue;

                Vector3 anchor = new Vector3(freeControlPoints[i].x, shapeY, freeControlPoints[i].z);
                freeControlPoints[j] = anchor;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Supprime les sommets en trop au même XZ que le premier d’une paire (après align), sauf les vraies paires
    /// rentrant/saillant à garder en double. Évite l’empilement de poignées au même endroit (ex. sur le centroïde).
    /// </summary>
    bool TryRemoveOrthogonalRingDuplicateVerticesNonStackableSameXZ(float eps)
    {
        if (interiorWallsStayInsideLot != null)
            return false;
        if (freeControlPoints == null || freeControlPoints.Count <= 3)
            return false;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode)
            return false;

        float epsSq = eps * eps;
        bool changed = false;
        const int maxPasses = 64;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            int n = freeControlPoints.Count;
            if (n <= 3)
                break;

            int removeAt = -1;
            for (int j = n - 1; j >= 0 && removeAt < 0; j--)
            {
                for (int i = 0; i < j; i++)
                {
                    if (IsReflexSalientStackableCornerPair(i, j))
                        continue;
                    float dx = freeControlPoints[j].x - freeControlPoints[i].x;
                    float dz = freeControlPoints[j].z - freeControlPoints[i].z;
                    if (dx * dx + dz * dz <= epsSq)
                    {
                        removeAt = j;
                        break;
                    }
                }
            }

            if (removeAt < 0)
                break;

            freeControlPoints.RemoveAt(removeAt);
            changed = true;
        }

        if (changed)
        {
            _freePathWasEdited = true;
            InvalidateStraightClosedPreviewCache();
        }

        return changed;
    }

    static float DistSqXZPoints(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Regroupe en un seul sommet toute chaîne consécutive où chaque arête (XZ) est plus courte que le seuil
    /// (y compris paire coin rentrant/saillant quasi superposée). Traite aussi la fermeture de l’anneau.
    /// </summary>
    bool TryCollapseConsecutiveNearVerticesOrthogonalRing(float mergeEpsXZ)
    {
        if (interiorWallsStayInsideLot != null)
            return false;
        if (freeControlPoints == null || freeControlPoints.Count <= 3)
            return false;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode)
            return false;
        if (mergeEpsXZ <= 1e-8f)
            return false;

        float mergeSq = mergeEpsXZ * mergeEpsXZ;
        bool anyPassChanged = false;
        int outer = 0;
        while (outer++ < 8 && freeControlPoints.Count > 3)
        {
            int nOrig = freeControlPoints.Count;
            var orig = new List<Vector3>(nOrig);
            for (int t = 0; t < nOrig; t++)
                orig.Add(freeControlPoints[t]);

            var neu = new List<Vector3>(nOrig);
            int i = 0;
            while (i < nOrig)
            {
                int j = i;
                while (j + 1 < nOrig && DistSqXZPoints(orig[j], orig[j + 1]) <= mergeSq)
                    j++;

                if (j > i)
                {
                    // Garder le premier sommet de la chaîne (pas la moyenne : sinon tout converge vers un « point central »
                    // et empile les poignées au centroïde du segment).
                    Vector3 keep = orig[i];
                    neu.Add(new Vector3(keep.x, shapeY, keep.z));
                    i = j + 1;
                }
                else
                {
                    neu.Add(new Vector3(orig[i].x, shapeY, orig[i].z));
                    i++;
                }
            }

            if (neu.Count < 3)
                break;

            bool shortClosingEdge = DistSqXZPoints(orig[nOrig - 1], orig[0]) <= mergeSq;
            if (neu.Count >= 3 && shortClosingEdge &&
                DistSqXZPoints(neu[0], neu[neu.Count - 1]) <= mergeSq)
            {
                neu.RemoveAt(neu.Count - 1);
            }

            if (neu.Count < 3)
                break;

            bool passChanged = neu.Count != nOrig;
            if (!passChanged)
            {
                for (int t = 0; t < neu.Count; t++)
                {
                    if (DistSqXZPoints(neu[t], freeControlPoints[t]) > 1e-12f)
                    {
                        passChanged = true;
                        break;
                    }
                }
            }

            if (!passChanged)
                break;

            freeControlPoints.Clear();
            for (int t = 0; t < neu.Count; t++)
                freeControlPoints.Add(neu[t]);

            anyPassChanged = true;
        }

        if (anyPassChanged)
        {
            _freePathWasEdited = true;
            InvalidateStraightClosedPreviewCache();
        }

        return anyPassChanged;
    }

    /// <summary>
    /// Garantit une distance minimale XZ entre chaque paire coin rentrant / coin saillant (indices déplacés : j &gt; i).
    /// </summary>
    void EnforceReflexSalientStackablePairsMinimumSeparationXZ(float minD)
    {
        if (minD <= 0.0001f || freeControlPoints == null)
            return;

        int n = freeControlPoints.Count;
        if (n < 2)
            return;

        float minSq = minD * minD;

        for (int sweep = 0; sweep < 8; sweep++)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (!IsReflexSalientStackableCornerPair(i, j))
                        continue;
                    if (IsRingVertexStraightMidXZ(i) || IsRingVertexStraightMidXZ(j))
                        continue;

                    Vector2 pi = new Vector2(freeControlPoints[i].x, freeControlPoints[i].z);
                    Vector2 pj = new Vector2(freeControlPoints[j].x, freeControlPoints[j].z);
                    Vector2 d = pj - pi;
                    float dsq = d.sqrMagnitude;
                    if (dsq >= minSq - 1e-8f)
                        continue;

                    any = true;
                    Vector2 newPj;
                    if (dsq < 1e-14f)
                        newPj = pi + new Vector2(minD, 0f);
                    else
                        newPj = pi + d.normalized * minD;

                    freeControlPoints[j] = new Vector3(newPj.x, shapeY, newPj.y);
                }
            }

            if (!any)
                break;
        }
    }

    static bool RingHasDuplicateSuperposedVerticesXZ(List<Vector3> ring, float epsSq)
    {
        if (ring == null)
            return false;
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                float dx = ring[i].x - ring[j].x;
                float dz = ring[i].z - ring[j].z;
                if (dx * dx + dz * dz <= epsSq)
                    return true;
            }
        }

        return false;
    }

    /// <summary>True if this wall was created as a closed path (loop). Open polylines use two endpoints as handles.</summary>
    public bool IsClosedLoopPath => _closedLoop;

    /// <summary>True for merged L/U lots: straight segments + edge/corner handle behaviour + optional center handle.</summary>
    public bool UsesMergedLotOrthogonalHandles => _closedFreeOrthogonalPolylineMode;

    /// <summary>
    /// Collage / duplication d’étage : appliquer <see cref="InitFromMergedLotOutline"/> au lieu de
    /// <see cref="InitFromDetectedPath"/> pour retrouver le même pipeline que la fusion de lots / l’enveloppe maison
    /// (sinon spline, ellipse ou snap recréent un contour cassé).
    /// </summary>
    public bool ShouldPasteClosedFreeAsMergedLotOutline =>
        IsClosedLoopPath && shapeKind == ShapeKind.Free && UsesMergedLotOrthogonalHandles;

    /// <summary>Invalide le cache du contour droit (appeler après restauration undo / changements externes).</summary>
    public void InvalidateStraightClosedPreviewCache()
    {
        _straightClosedPreviewDirty = true;
    }

    void Awake()
    {
        if (wall == null)
            wall = GetComponent<WallObject>();
    }

    public void InitFromPath(List<Vector3> points)
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (points == null || points.Count < 2)
            return;

        List<Vector3> src = new List<Vector3>(points);

        _closedLoop = IsClosed(src);

        if (_closedLoop && src.Count > 1 && Vector3.Distance(src[0], src[src.Count - 1]) < 0.001f)
            src.RemoveAt(src.Count - 1);

        _freeRawPath.Clear();
        _freePathWasEdited = false;
        _mergeFootprintUseExactPolyline = false;
        _closedFreeOrthogonalPolylineMode = false;
        InvalidateStraightClosedPreviewCache();

        if (TrySetupEllipse(src))
        {
            shapeKind = ShapeKind.Ellipse;
            ApplyToWall();
            return;
        }

        if (TrySetupRectangle(src))
        {
            shapeKind = ShapeKind.Rectangle;
            ApplyToWall();
            return;
        }

        if (TrySetupTriangle(src))
        {
            shapeKind = ShapeKind.Triangle;
            ApplyToWall();
            return;
        }

        if (!_closedLoop && TrySetupOpenArc(src, requireRadialFit: true))
        {
            shapeKind = ShapeKind.OpenArc;
            ApplyToWall();
            return;
        }

        shapeKind = ShapeKind.Free;
        InitFreeFromPathChoosingOrthogonalOrResampled(src);
        ApplyToWall();
    }

    /// <summary>
    /// Après fusion de deux lots : applique le contour extérieur calculé (L, U, …) tel quel.
    /// Évite <see cref="InitFromPath"/> qui peut retomber sur du lissage ou une enveloppe convexe (grand rectangle).
    /// </summary>
    /// <param name="drawInputForSnap">Si non null, <paramref name="snapCommittedOutlineToMainGrid"/> et contour <b>uniquement H/V (Manhattan)</b> : aligne le contour sur la grille (lots L/U). Les enveloppes avec arcs (cercle, union triangle/ellipse) ne sont pas accrochées à la grille pour éviter l’escalier à angles droits.</param>
    public void InitFromMergedLotOutline(
        List<Vector3> mergedClosedWorldPath,
        WallDrawInput drawInputForSnap = null,
        bool snapCommittedOutlineToMainGrid = false)
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (mergedClosedWorldPath == null || mergedClosedWorldPath.Count < 4)
            return;

        var src = new List<Vector3>(mergedClosedWorldPath);
        _closedLoop = true;

        if (src.Count >= 2 && Vector3.Distance(src[0], src[src.Count - 1]) < 0.001f)
            src.RemoveAt(src.Count - 1);

        if (src.Count < 3)
            return;

        // Accrochage grille : seulement contours 100% horizontaux / verticaux (L, U, rect). Sinon
        // (cercle échantillonné, union tri/ellipse) → chaque sommet sur la grille = escalier 90°.
        bool outlineIsOrthogonalAxis = WallObject.IsClosedLoopOrthogonalAxisAlignedXZ(src);
        if (drawInputForSnap != null &&
            snapCommittedOutlineToMainGrid &&
            drawInputForSnap.enableGridSnap &&
            drawInputForSnap.snapToHierarchicalVisualGrid &&
            outlineIsOrthogonalAxis)
        {
            drawInputForSnap.SnapCommittedPathToMainGridInPlace(src, closed: true);
        }

        shapeY = src[0].y;
        _freePathWasEdited = false;
        _mergeFootprintUseExactPolyline = true;
        _closedFreeOrthogonalPolylineMode = outlineIsOrthogonalAxis;
        orthogonalMidHandleDragsWholeWallRun = outlineIsOrthogonalAxis;
        InvalidateStraightClosedPreviewCache();

        freeControlPoints.Clear();
        for (int i = 0; i < src.Count; i++)
            freeControlPoints.Add(new Vector3(src[i].x, shapeY, src[i].z));

        _freeRawPath.Clear();
        for (int i = 0; i < src.Count; i++)
            _freeRawPath.Add(new Vector3(src[i].x, shapeY, src[i].z));

        EnsureClosedFreeRingCounterClockwiseXZ();

        if (outlineIsOrthogonalAxis)
        {
            InsertMidpointsOnCoinToCoinEdgesOrthogonalRing();
            // Même pipeline qu’après édition : sinon segments diagonaux / cordes [coin,coin] ne sont jamais corrigés
            // tant que l’utilisateur n'a pas déplacé une poignée.
            FinalizeOrthogonalFreeRingAfterControlEdit();
        }

        shapeKind = ShapeKind.Free;
        ApplyToWall();

        List<Vector3> prev = GetPreviewPathWorld();
        if (prev != null && prev.Count >= 2)
            ComputeBounds(prev);

        CommitBulkPivotToFootprintAreaCentroid();
    }

    public void InitFromDetectedPath(List<Vector3> points, WallDrawInput.DetectedShapeKind detectedKind)
    {
        if (wall == null)
            wall = GetComponent<WallObject>();

        if (points == null || points.Count < 2)
            return;

        List<Vector3> src = new List<Vector3>(points);

        _closedLoop = IsClosed(src);

        if (_closedLoop && src.Count > 1 && Vector3.Distance(src[0], src[src.Count - 1]) < 0.001f)
            src.RemoveAt(src.Count - 1);

        _freeRawPath.Clear();
        _freePathWasEdited = false;
        _mergeFootprintUseExactPolyline = false;
        _closedFreeOrthogonalPolylineMode = false;
        InvalidateStraightClosedPreviewCache();

        bool wantsRectangle =
            detectedKind == WallDrawInput.DetectedShapeKind.Rectangle ||
            detectedKind == WallDrawInput.DetectedShapeKind.Square;
        bool wantsTriangle = detectedKind == WallDrawInput.DetectedShapeKind.Triangle;
        bool wantsOpenArc = detectedKind == WallDrawInput.DetectedShapeKind.OpenArc;

        if (wantsRectangle)
        {
            if (TrySetupRectangleForcedFromDetected(src))
            {
                shapeKind = ShapeKind.Rectangle;
                ApplyToWall();
                return;
            }

            if (TrySetupRectangle(src))
            {
                shapeKind = ShapeKind.Rectangle;
                ApplyToWall();
                return;
            }

            shapeKind = ShapeKind.Free;
            InitFreeFromPathChoosingOrthogonalOrResampled(src);
            ApplyToWall();
            return;
        }

        if (wantsTriangle)
        {
            if (TrySetupTriangle(src))
            {
                shapeKind = ShapeKind.Triangle;
                ApplyToWall();
                return;
            }

            shapeKind = ShapeKind.Free;
            InitFreeFromPathChoosingOrthogonalOrResampled(src);
            ApplyToWall();
            return;
        }

        if (wantsOpenArc && !_closedLoop && TrySetupOpenArc(src, requireRadialFit: false))
        {
            shapeKind = ShapeKind.OpenArc;
            ApplyToWall();
            return;
        }

        // Important:
        // - on n'autorise plus de 2e passe rectangle ici
        // - mais on réautorise la 2e passe ellipse pour retrouver
        //   les vrais cercles / ovales avec 4 handles.
        // Donc:
        //   cercle détecté -> ellipse
        //   ovale dessiné mais classé Free -> ellipse si le fit passe
        //   triangle / free non-elliptique -> restent Free
        if (_closedLoop && TrySetupEllipse(src))
        {
            shapeKind = ShapeKind.Ellipse;
            ApplyToWall();
            return;
        }

        shapeKind = ShapeKind.Free;
        InitFreeFromPathChoosingOrthogonalOrResampled(src);
        ApplyToWall();
    }

    /// <summary>
    /// Contours uniquement H/V (grille / escalier Manhattan) : même édition que les lots fusionnés
    /// (<see cref="_closedFreeOrthogonalPolylineMode"/> + poignées milieu/coins, angles droits au drag).
    /// Sinon sous-échantillonnage classique via <see cref="SetupFree"/>.
    /// </summary>
    void InitFreeFromPathChoosingOrthogonalOrResampled(List<Vector3> srcOpenRing)
    {
        if (srcOpenRing != null && srcOpenRing.Count > 0)
            shapeY = srcOpenRing[0].y;

        CacheInitialFreeRawPath(srcOpenRing);

        if (_closedLoop &&
            srcOpenRing != null &&
            srcOpenRing.Count >= 3 &&
            WallObject.IsClosedLoopOrthogonalAxisAlignedXZ(srcOpenRing))
        {
            _closedFreeOrthogonalPolylineMode = true;
            orthogonalMidHandleDragsWholeWallRun = true;
            freeControlPoints.Clear();
            for (int i = 0; i < srcOpenRing.Count; i++)
                freeControlPoints.Add(new Vector3(srcOpenRing[i].x, shapeY, srcOpenRing[i].z));

            if (freeControlPoints.Count > 1 &&
                Vector3.Distance(freeControlPoints[0], freeControlPoints[freeControlPoints.Count - 1]) < 0.0001f)
                freeControlPoints.RemoveAt(freeControlPoints.Count - 1);

            EnsureClosedFreeRingCounterClockwiseXZ();
            InsertMidpointsOnCoinToCoinEdgesOrthogonalRing();
            InvalidateStraightClosedPreviewCache();
            return;
        }

        _closedFreeOrthogonalPolylineMode = false;
        SetupFree(srcOpenRing);
    }

    public int ControlPointCount
    {
        get
        {
            switch (shapeKind)
            {
                case ShapeKind.Ellipse:
                    return 5;

                case ShapeKind.Rectangle:
                    return 9;

                case ShapeKind.Triangle:
                    return triangleControlPoints != null && triangleControlPoints.Count >= 3 ? 4 : 0;

                case ShapeKind.OpenArc:
                    return 4;

                case ShapeKind.Free:
                {
                    int n = freeControlPoints != null ? freeControlPoints.Count : 0;
                    if (TryGetClosedFreeBulkCentroidVirtualIndex(out _))
                        return n + 1;
                    if (!_closedLoop && allowVerticalScrollElevation && n >= 2)
                        return n + 1;
                    return n;
                }

                default:
                    return freeControlPoints != null ? freeControlPoints.Count : 0;
            }
        }
    }

    public Vector3 GetControlPointWorld(int index)
    {
        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                return GetEllipseControlPoint(index);

            case ShapeKind.Rectangle:
                return GetRectangleControlPoint(index);

            case ShapeKind.Triangle:
                if (triangleControlPoints == null || triangleControlPoints.Count < 3)
                    return Vector3.zero;
                if (index == 3)
                {
                    Vector3 s = triangleControlPoints[0] + triangleControlPoints[1] + triangleControlPoints[2];
                    return new Vector3(s.x / 3f, shapeY, s.z / 3f);
                }

                if (index >= 0 && index < 3)
                    return triangleControlPoints[index];
                return Vector3.zero;

            case ShapeKind.OpenArc:
                return GetOpenArcControlPoint(index);

            case ShapeKind.Free:
            {
                if (freeControlPoints == null)
                    return Vector3.zero;
                int fn = freeControlPoints.Count;
                if (TryGetClosedFreeBulkCentroidVirtualIndex(out int vi) && index == vi)
                    return GetClosedFreeLotCentroidWorld();
                if (!_closedLoop && allowVerticalScrollElevation && fn >= 2 && index == fn)
                    return GetOpenFreeCenterWorld();
                if (index < 0 || index >= fn)
                    return Vector3.zero;
                return freeControlPoints[index];
            }

            default:
                if (freeControlPoints == null || index < 0 || index >= freeControlPoints.Count)
                    return Vector3.zero;

                return freeControlPoints[index];
        }
    }

    /// <summary>
    /// Indice du « centre » utilisé pour le déplacement global (handle séparé) et drapeau pour le lot orthogonal fusionné
    /// dont le centre est virtuel (centroïde), pas un point de la liste.
    /// </summary>
    public bool TryGetShapeBulkMovePivotInfo(out int standardCenterIndex, out bool useMergedCentroid)
    {
        standardCenterIndex = -1;
        useMergedCentroid = false;

        switch (shapeKind)
        {
            case ShapeKind.Rectangle:
                standardCenterIndex = 8;
                return true;
            case ShapeKind.Triangle:
                if (triangleControlPoints == null || triangleControlPoints.Count < 3)
                    return false;
                standardCenterIndex = 3;
                return true;
            case ShapeKind.Ellipse:
                standardCenterIndex = 4;
                return true;
            case ShapeKind.OpenArc:
                standardCenterIndex = 2;
                return true;
            case ShapeKind.Free:
                if (TryGetClosedFreeBulkCentroidVirtualIndex(out int virt))
                {
                    standardCenterIndex = virt;
                    useMergedCentroid = true;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    public Vector3 GetMergedOrthogonalShapeCentroidWorld() => GetClosedFreeLotCentroidWorld();

    bool TryGetClosedFreeBulkCentroidVirtualIndex(out int virtualIndex)
    {
        virtualIndex = -1;
        if (shapeKind != ShapeKind.Free || !_closedLoop || freeControlPoints == null || freeControlPoints.Count < 3)
            return false;

        // Tout contour fermé libre (carré détecté « Free », L léger hors grille, etc.) : un pivot global au centroïde,
        // comme pour Rectangle / Ellipse — sinon pas de point bleu et sensation de forme « bloquée ».
        virtualIndex = freeControlPoints.Count;
        return true;
    }

    bool IsClosedFreeDesignatedHouseLotForPivot()
    {
        if (shapeKind != ShapeKind.Free || !_closedLoop || wall == null)
            return false;
        HouseParquetFloor hf = wall.GetComponent<HouseParquetFloor>();
        return hf != null && hf.IsDesignatedHouseLot;
    }

    /// <summary>
    /// Centre XZ pour placer un mur ouvert ajouté depuis le menu du pivot violet (lot maison).
    /// </summary>
    public bool TryGetHouseLotSpawnCenterWorld(out Vector3 world)
    {
        world = default;
        if (UsesMergedLotOrthogonalHandles && IsClosedLoopPath)
        {
            world = GetMergedOrthogonalShapeCentroidWorld();
            return true;
        }

        List<Vector3> prev = GetPreviewPathWorld();
        if (prev == null || prev.Count < 2)
            return false;

        Vector3 s = Vector3.zero;
        for (int i = 0; i < prev.Count; i++)
            s += prev[i];
        world = s / prev.Count;
        return true;
    }

    /// <summary>
    /// Contour fermé du lot projeté en XZ (sans point dupliqué final), pour tests « dedans / dehors ».
    /// </summary>
    public bool TryGetClosedLotFootprintRingXZ(List<Vector2> ringOut)
    {
        if (ringOut == null)
            return false;
        ringOut.Clear();
        if (!IsClosedLoopPath)
            return false;

        List<Vector3> path = ResolveClosedLotDisplayRingWorld(wall, this);
        if (path == null || path.Count < 3)
            path = GetPreviewPathWorld();
        if (path == null || path.Count < 3)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;

        if (n < 3)
            return false;

        for (int i = 0; i < n; i++)
            ringOut.Add(new Vector2(path[i].x, path[i].z));

        return ringOut.Count >= 3;
    }

    /// <summary>
    /// Déplace la forme entière sur l’axe Y (ex. molette avec <see cref="allowVerticalScrollElevation"/>).
    /// </summary>
    public void OffsetShapeWorldY(float deltaY)
    {
        if (Mathf.Abs(deltaY) < 1e-8f)
            return;

        shapeY += deltaY;

        switch (shapeKind)
        {
            case ShapeKind.Free:
                if (freeControlPoints != null)
                {
                    for (int i = 0; i < freeControlPoints.Count; i++)
                    {
                        Vector3 p = freeControlPoints[i];
                        freeControlPoints[i] = new Vector3(p.x, shapeY, p.z);
                    }
                }

                for (int i = 0; i < _freeRawPath.Count; i++)
                {
                    Vector3 p = _freeRawPath[i];
                    _freeRawPath[i] = new Vector3(p.x, shapeY, p.z);
                }
                break;

            case ShapeKind.Triangle:
                if (triangleControlPoints != null)
                {
                    for (int i = 0; i < triangleControlPoints.Count; i++)
                    {
                        Vector3 p = triangleControlPoints[i];
                        triangleControlPoints[i] = new Vector3(p.x, shapeY, p.z);
                    }
                }
                break;

            case ShapeKind.Rectangle:
            case ShapeKind.Ellipse:
            case ShapeKind.OpenArc:
                break;
        }

        ApplyToWall();
    }

    public void TrySetMergedOrthogonalShapeCentroidWorld(Vector3 targetWorld)
    {
        if (shapeKind != ShapeKind.Free || !_closedLoop)
            return;
        if (!_closedFreeOrthogonalPolylineMode && !IsClosedFreeDesignatedHouseLotForPivot())
            return;
        if (freeControlPoints == null || freeControlPoints.Count == 0)
            return;

        Vector3 cBefore = GetClosedFreeLotCentroidWorld();
        Vector3 delta = new Vector3(targetWorld.x - cBefore.x, 0f, targetWorld.z - cBefore.z);

        // Lot geometry first: interior walls clamp against <see cref="TryGetClosedLotFootprintRingXZ"/>; if they
        // move before the lot updates, the ring is stale and clipping pulls endpoints off the new shell (gap bug).
        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            freeControlPoints[i] = new Vector3(p.x + delta.x, shapeY, p.z + delta.z);
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();

        if (_mergeFootprintUseExactPolyline || _closedFreeOrthogonalPolylineMode)
        {
            _freeRawPath.Clear();
            for (int i = 0; i < freeControlPoints.Count; i++)
            {
                Vector3 p = freeControlPoints[i];
                _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
            }
        }

        MoveAttachedInteriorOpenWallsByDeltaXZ(delta);

        ComputeBounds(BuildFreePreviewPath() ?? freeControlPoints);
        ApplyToWall();
    }

    /// <summary>
    /// Après construction du contour fusionné (L, U, …) : aligne le pivot de déplacement global sur le centroïde
    /// surfacique du périmètre affiché, pour éviter qu’il coïncide avec un ancien point / un clic résiduel.
    /// </summary>
    public void CommitBulkPivotToFootprintAreaCentroid()
    {
        if (shapeKind != ShapeKind.Free || !_closedLoop)
            return;
        if (!TryGetShapeBulkMovePivotInfo(out _, out bool useMerged) || !useMerged)
            return;

        List<Vector3> path = GetPreviewPathWorld();
        if (path == null || path.Count < 3)
            return;

        if (!TryComputeClosedRingCentroidXZ(path, out Vector2 xz))
            return;

        Vector3 target = new Vector3(xz.x, shapeY, xz.y);
        TrySetMergedOrthogonalShapeCentroidWorld(target);
    }

    /// <summary>
    /// Translation rigide XZ d’un lot fermé (lots sources masqués sous l’enveloppe maison, etc.).
    /// </summary>
    public void TranslateClosedLotGeometryXZ(Vector3 deltaXZ)
    {
        deltaXZ.y = 0f;
        if (deltaXZ.sqrMagnitude < 1e-18f)
            return;

        switch (shapeKind)
        {
            case ShapeKind.Rectangle:
            {
                Vector3 c = GetRectangleCenterWorld();
                SetControlPointWorld(8, new Vector3(c.x + deltaXZ.x, shapeY, c.z + deltaXZ.z));
                return;
            }

            case ShapeKind.Ellipse:
            {
                Vector3 c = GetBoundsCenter();
                SetControlPointWorld(4, new Vector3(c.x + deltaXZ.x, shapeY, c.z + deltaXZ.z));
                return;
            }

            case ShapeKind.Triangle:
            {
                if (triangleControlPoints == null || triangleControlPoints.Count < 3)
                    return;
                Vector3 c = (triangleControlPoints[0] + triangleControlPoints[1] + triangleControlPoints[2]) / 3f;
                SetControlPointWorld(3, new Vector3(c.x + deltaXZ.x, shapeY, c.z + deltaXZ.z));
                return;
            }

            case ShapeKind.Free:
            {
                if (!_closedLoop || freeControlPoints == null || freeControlPoints.Count == 0)
                    return;

                for (int i = 0; i < freeControlPoints.Count; i++)
                {
                    Vector3 p = freeControlPoints[i];
                    freeControlPoints[i] = new Vector3(p.x + deltaXZ.x, shapeY, p.z + deltaXZ.z);
                }

                _freePathWasEdited = true;
                InvalidateStraightClosedPreviewCache();

                if (_mergeFootprintUseExactPolyline || _closedFreeOrthogonalPolylineMode)
                {
                    _freeRawPath.Clear();
                    for (int i = 0; i < freeControlPoints.Count; i++)
                    {
                        Vector3 p = freeControlPoints[i];
                        _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
                    }
                }

                MoveAttachedInteriorOpenWallsByDeltaXZ(deltaXZ);
                ComputeBounds(BuildFreePreviewPath() ?? freeControlPoints);
                ApplyToWall();
                return;
            }

            default:
                return;
        }
    }

    static WallBuildController s_cachedBuildControllerForInteriorMove;

    void MoveAttachedInteriorOpenWallsByDeltaXZ(Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-16f)
            return;

        if (s_cachedBuildControllerForInteriorMove == null)
            s_cachedBuildControllerForInteriorMove = FindFirstObjectByType<WallBuildController>();

        if (s_cachedBuildControllerForInteriorMove != null)
            s_cachedBuildControllerForInteriorMove.MoveInteriorWallsAttachedToLotXZ(this, delta);
    }

    /// <summary>
    /// Translation XZ des poignées (murs intérieurs suivant le lot).
    /// </summary>
    public void TranslateOpenFreeInteriorWallXZ(Vector3 deltaXZ)
    {
        deltaXZ.y = 0f;
        if (shapeKind != ShapeKind.Free || _closedLoop)
            return;
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return;

        float dx = deltaXZ.x;
        float dz = deltaXZ.z;

        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            freeControlPoints[i] = new Vector3(p.x + dx, shapeY, p.z + dz);
        }

        for (int i = 0; i < _freeRawPath.Count; i++)
        {
            Vector3 p = _freeRawPath[i];
            _freeRawPath[i] = new Vector3(p.x + dx, shapeY, p.z + dz);
        }

        _mergeFootprintUseExactPolyline = false;
        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        // Rigid translation with the lot: keep endpoints flush to the lot ring; peer-shrink is for avoiding
        // interior–interior overlap and would re-open a gap along the exterior shell after every move.
        ClampOpenFreeVerticesToInteriorLotConstraint(applyPeerSeparation: false);
        ApplyToWall();
    }

    public void SetControlPointWorld(int index, Vector3 worldPos)
    {
        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                SetEllipseControlPoint(index, worldPos);
                break;

            case ShapeKind.Rectangle:
                SetRectangleControlPoint(index, worldPos);
                break;

            case ShapeKind.Triangle:
                if (triangleControlPoints == null || triangleControlPoints.Count < 3)
                    return;
                if (index == 3)
                {
                    Vector3 cBefore = (triangleControlPoints[0] + triangleControlPoints[1] + triangleControlPoints[2]) /
                        3f;
                    Vector3 delta = new Vector3(worldPos.x - cBefore.x, 0f, worldPos.z - cBefore.z);
                    for (int i = 0; i < 3; i++)
                    {
                        triangleControlPoints[i] = new Vector3(
                            triangleControlPoints[i].x + delta.x,
                            shapeY,
                            triangleControlPoints[i].z + delta.z);
                    }

                    EnsureCounterClockwiseXZ(triangleControlPoints);
                    ComputeBounds(BuildTrianglePath());
                    MoveAttachedInteriorOpenWallsByDeltaXZ(delta);
                    break;
                }

                if (index < 0 || index >= 3)
                    return;
                triangleControlPoints[index] = new Vector3(worldPos.x, shapeY, worldPos.z);
                EnsureCounterClockwiseXZ(triangleControlPoints);
                ComputeBounds(BuildTrianglePath());
                break;

            case ShapeKind.OpenArc:
                SetOpenArcControlPoint(index, worldPos);
                break;

            case ShapeKind.Free:
                if (!TryApplyFreeShapeControlPointWorld(index, worldPos))
                    return;
                break;

            default:
                return;
        }

        ApplyToWall();
    }

    /// <summary>
    /// Après un déplacement rigide (handle centre), aligne tous les points de contrôle sur la grille hiérarchique
    /// comme pour le dessin (<see cref="WallDrawInput.SnapWorldPointForEditing"/>).
    /// </summary>
    /// <param name="applyToWallAtEnd">Si faux, ne met pas à jour le mesh (ex. snap après rotation molette, avant un second traitement).</param>
    public void SnapAllControlPointsToHierarchicalGrid(WallDrawInput drawInput, bool applyToWallAtEnd = true)
    {
        if (drawInput == null || !drawInput.enableGridSnap || !drawInput.snapToHierarchicalVisualGrid)
            return;

        switch (shapeKind)
        {
            case ShapeKind.Free:
                if (freeControlPoints == null || freeControlPoints.Count == 0)
                    return;

                for (int i = 0; i < freeControlPoints.Count; i++)
                {
                    Vector3 p = freeControlPoints[i];
                    p = drawInput.SnapWorldPointForEditing(p);
                    freeControlPoints[i] = new Vector3(p.x, shapeY, p.z);
                }

                if (_mergeFootprintUseExactPolyline || _closedFreeOrthogonalPolylineMode)
                {
                    _freeRawPath.Clear();
                    for (int i = 0; i < freeControlPoints.Count; i++)
                    {
                        Vector3 p = freeControlPoints[i];
                        _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
                    }
                }
                else
                    _freePathWasEdited = true;

                InvalidateStraightClosedPreviewCache();
                ComputeBounds(BuildFreePreviewPath() ?? freeControlPoints);
                break;

            case ShapeKind.Triangle:
                if (triangleControlPoints == null || triangleControlPoints.Count < 3)
                    return;

                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = triangleControlPoints[i];
                    p = drawInput.SnapWorldPointForEditing(p);
                    triangleControlPoints[i] = new Vector3(p.x, shapeY, p.z);
                }

                EnsureCounterClockwiseXZ(triangleControlPoints);
                ComputeBounds(BuildTrianglePath());
                break;

            case ShapeKind.Rectangle:
            {
                Vector3 c0 = drawInput.SnapWorldPointForEditing(GetRectangleControlPoint(0));
                Vector3 c2 = drawInput.SnapWorldPointForEditing(GetRectangleControlPoint(2));
                Vector3 c4 = drawInput.SnapWorldPointForEditing(GetRectangleControlPoint(4));
                Vector3 c6 = drawInput.SnapWorldPointForEditing(GetRectangleControlPoint(6));

                Vector2 a0 = RectangleWorldToLocal(c0);
                Vector2 a2 = RectangleWorldToLocal(c2);
                Vector2 a4 = RectangleWorldToLocal(c4);
                Vector2 a6 = RectangleWorldToLocal(c6);

                rectangleMinX = Mathf.Min(a0.x, a2.x, a4.x, a6.x);
                rectangleMaxX = Mathf.Max(a0.x, a2.x, a4.x, a6.x);
                rectangleMinY = Mathf.Min(a0.y, a2.y, a4.y, a6.y);
                rectangleMaxY = Mathf.Max(a0.y, a2.y, a4.y, a6.y);

                const float minEdge = 0.1f;
                if (rectangleMaxX - rectangleMinX < minEdge)
                {
                    float mid = (rectangleMinX + rectangleMaxX) * 0.5f;
                    rectangleMinX = mid - minEdge * 0.5f;
                    rectangleMaxX = mid + minEdge * 0.5f;
                }

                if (rectangleMaxY - rectangleMinY < minEdge)
                {
                    float mid = (rectangleMinY + rectangleMaxY) * 0.5f;
                    rectangleMinY = mid - minEdge * 0.5f;
                    rectangleMaxY = mid + minEdge * 0.5f;
                }

                ComputeBounds(BuildRectanglePath());
                break;
            }

            case ShapeKind.Ellipse:
                if (Mathf.Abs(ellipseRotationRad) > 1e-5f)
                    FlattenEllipseRotationIntoBounds();

                Vector3 e0 = drawInput.SnapWorldPointForEditing(GetEllipseControlPoint(0));
                Vector3 e1 = drawInput.SnapWorldPointForEditing(GetEllipseControlPoint(1));
                Vector3 e2 = drawInput.SnapWorldPointForEditing(GetEllipseControlPoint(2));
                Vector3 e3 = drawInput.SnapWorldPointForEditing(GetEllipseControlPoint(3));

                SetEllipseControlPoint(0, e0);
                SetEllipseControlPoint(1, e1);
                SetEllipseControlPoint(2, e2);
                SetEllipseControlPoint(3, e3);
                ComputeBounds(BuildEllipsePath(Mathf.Max(48, ellipseWallResolution)));
                break;

            case ShapeKind.OpenArc:
            {
                Vector3 o2 = drawInput.SnapWorldPointForEditing(GetOpenArcControlPoint(2));
                Vector3 o0 = drawInput.SnapWorldPointForEditing(GetOpenArcControlPoint(0));
                Vector3 o1 = drawInput.SnapWorldPointForEditing(GetOpenArcControlPoint(1));

                SetOpenArcControlPoint(2, o2);
                SetOpenArcControlPoint(0, o0);
                SetOpenArcControlPoint(1, o1);
                break;
            }

            default:
                return;
        }

        if (applyToWallAtEnd)
            ApplyToWall();
    }

    public bool IsControlPointEditable(int index)
    {
        return index >= 0 && index < ControlPointCount;
    }

    /// <summary>
    /// Contour monde aligné sur le <see cref="WallObject"/> (centreline) pour plancher, fil d’overlay et tests « dedans »
    /// quand le preview analytique diverge (ex. Rectangle imposé sur un mesh encore ovale ; free orthogonal vs mesh courbe).
    /// </summary>
    public static List<Vector3> ResolveClosedLotDisplayRingWorld(WallObject wall, WallEditShape editShape)
    {
        if (editShape == null)
            return null;

        if (wall != null &&
            wall.closedLoop &&
            wall.Points != null &&
            wall.Points.Count >= 3)
        {
            ShapeKind sk = editShape.shapeKind;
            if (sk == ShapeKind.Ellipse || sk == ShapeKind.Triangle)
            {
                float y = editShape.shapeY;
                var ring = new List<Vector3>(wall.Points.Count);
                for (int i = 0; i < wall.Points.Count; i++)
                {
                    Vector3 p = wall.Points[i];
                    ring.Add(new Vector3(p.x, y, p.z));
                }

                return ring;
            }

            if (sk == ShapeKind.Free && wall.Points.Count >= 12)
            {
                List<Vector3> previewFree = editShape.GetPreviewPathWorld();
                if (previewFree != null && previewFree.Count >= 4)
                {
                    var wp = new List<Vector3>(wall.Points.Count);
                    for (int i = 0; i < wall.Points.Count; i++)
                        wp.Add(wall.Points[i]);

                    if (WallObject.IsClosedLoopOrthogonalAxisAlignedXZ(previewFree) &&
                        !WallObject.IsClosedLoopOrthogonalAxisAlignedXZ(wp))
                    {
                        float yf = editShape.shapeY;
                        for (int i = 0; i < wp.Count; i++)
                        {
                            Vector3 p = wp[i];
                            wp[i] = new Vector3(p.x, yf, p.z);
                        }

                        return wp;
                    }
                }
            }
        }

        List<Vector3> preview = editShape.GetPreviewPathWorld();
        if (wall == null)
            return preview;

        if (!wall.closedLoop ||
            editShape.shapeKind != ShapeKind.Rectangle ||
            wall.Points == null ||
            wall.Points.Count < 12)
            return preview;

        var wpRect = new List<Vector3>(wall.Points.Count);
        for (int i = 0; i < wall.Points.Count; i++)
            wpRect.Add(wall.Points[i]);

        bool useMesh =
            TryFitCircleLikeClosedPathXZForDisplay(wpRect, out _) ||
            (wall.Points.Count >= 12 && !WallObject.IsClosedLoopOrthogonalAxisAlignedXZ(wpRect));

        if (!useMesh)
            return preview;

        float yRect = editShape.shapeY;
        for (int i = 0; i < wpRect.Count; i++)
        {
            Vector3 p = wpRect[i];
            wpRect[i] = new Vector3(p.x, yRect, p.z);
        }

        return wpRect;
    }

    static bool TryFitCircleLikeClosedPathXZForDisplay(List<Vector3> path, out float radiusOut)
    {
        radiusOut = 0f;
        if (path == null || path.Count < 6)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;

        if (n < 6)
            return false;

        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++)
            c += new Vector2(path[i].x, path[i].z);
        c /= n;

        float rAcc = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            rAcc += Vector2.Distance(p, c);
        }

        float radius = rAcc / n;
        if (radius < 0.12f)
            return false;

        float maxErr = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = new Vector2(path[i].x, path[i].z);
            maxErr = Mathf.Max(maxErr, Mathf.Abs(Vector2.Distance(p, c) - radius));
        }

        if (maxErr > Mathf.Max(0.07f, radius * 0.22f))
            return false;

        radiusOut = radius;
        return true;
    }

    /// <summary>
    /// Ligne d’overlay / liens : suit le mesh du mur quand le preview seul trace encore un quad ou un L orthogonal
    /// alors que la géométrie réelle est ovale (voir <see cref="ResolveClosedLotDisplayRingWorld"/>).
    /// </summary>
    public List<Vector3> GetOverlayPathWorld()
    {
        if (!IsClosedLoopPath)
            return GetPreviewPathWorld();

        List<Vector3> resolved = ResolveClosedLotDisplayRingWorld(wall, this);
        return resolved != null && resolved.Count >= 2 ? resolved : GetPreviewPathWorld();
    }

    public List<Vector3> GetPreviewPathWorld()
    {
        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                return BuildEllipsePath(Mathf.Max(48, ellipseWallResolution));

            case ShapeKind.Rectangle:
                return BuildRectanglePath();

            case ShapeKind.Triangle:
                return BuildTrianglePath();

            case ShapeKind.OpenArc:
                return BuildOpenArcPath(Mathf.Max(24, openArcWallResolution));

            default:
                return BuildFreePreviewPath();
        }
    }

    /// <summary>
    /// Chemin pour Ctrl+C / Ctrl+V : pour un mur libre, copie les poignées (segment / polyline / anneau fermé),
    /// pas le chemin résolu dense (<see cref="GetPreviewPathWorld"/>), sinon collage / « ajouter un étage »
    /// recrée une forme incorrecte (lots fusionnés L/U, murs intérieurs, etc.).
    /// </summary>
    public List<Vector3> GetClipboardDuplicatePathWorld()
    {
        if (shapeKind == ShapeKind.Free && freeControlPoints != null)
        {
            int n = freeControlPoints.Count;
            bool enough = _closedLoop ? n >= 3 : n >= 2;
            if (enough)
            {
                var list = new List<Vector3>(n);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = freeControlPoints[i];
                    list.Add(new Vector3(p.x, shapeY, p.z));
                }

                return list;
            }
        }

        return GetPreviewPathWorld();
    }

    /// <summary>
    /// Kind à passer à <see cref="InitFromDetectedPath"/> lors d’un collage (Ctrl+V) pour retrouver le même type de forme.
    /// </summary>
    public WallDrawInput.DetectedShapeKind GetClipboardDetectedKind()
    {
        switch (shapeKind)
        {
            case ShapeKind.Rectangle:
            {
                float w = rectangleMaxX - rectangleMinX;
                float h = rectangleMaxY - rectangleMinY;
                float m = Mathf.Max(w, h);
                if (m > 0.001f && Mathf.Abs(w - h) / m <= 0.1f)
                    return WallDrawInput.DetectedShapeKind.Square;
                return WallDrawInput.DetectedShapeKind.Rectangle;
            }

            case ShapeKind.Ellipse:
                return WallDrawInput.DetectedShapeKind.Circle;

            case ShapeKind.Triangle:
                return WallDrawInput.DetectedShapeKind.Triangle;

            case ShapeKind.OpenArc:
                return WallDrawInput.DetectedShapeKind.OpenArc;

            default:
                return WallDrawInput.DetectedShapeKind.Free;
        }
    }



    public bool InsertFreeControlPointAtWorld(Vector3 worldPos)
    {
        if (shapeKind != ShapeKind.Free)
            return false;

        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return false;

        Vector3 insertPos = new Vector3(worldPos.x, shapeY, worldPos.z);

        int bestInsertIndex = FindBestInsertIndexForFreePoint(insertPos);
        if (bestInsertIndex < 0)
            return false;

        if (_closedLoop && _closedFreeOrthogonalPolylineMode)
        {
            int n = freeControlPoints.Count;
            int iSeg = bestInsertIndex - 1;
            if (iSeg < 0)
                iSeg += n;
            int next = (iSeg + 1) % n;
            Vector3 a = freeControlPoints[iSeg];
            Vector3 b = freeControlPoints[next];
            if (!TryProjectAndClampOnSegmentXZ(insertPos, a, b, minClosedSegmentLength, out insertPos))
                return false;
        }

        if (_closedLoop && _closedFreeOrthogonalPolylineMode)
            CopyOrthogonalFreeRingToBackup();

        freeControlPoints.Insert(bestInsertIndex, insertPos);
        _mergeFootprintUseExactPolyline = false;
        _freePathWasEdited = true;

        if (_closedLoop && _closedFreeOrthogonalPolylineMode)
        {
            FinalizeOrthogonalFreeRingAfterControlEdit();
            if (CurrentOrthogonalFreeRingHasSelfIntersectionXZ(editingVertexIndex: bestInsertIndex))
            {
                RestoreOrthogonalFreeRingFromBackup();
                return false;
            }
        }
        else
            InvalidateStraightClosedPreviewCache();

        if (!_closedLoop)
            ClampOpenFreeVerticesToInteriorLotConstraint();

        ApplyToWall();
        return true;
    }

    public bool RemoveFreeControlPointAt(int index)
    {
        if (shapeKind != ShapeKind.Free)
            return false;

        if (freeControlPoints == null)
            return false;

        if (index < 0 || index >= freeControlPoints.Count)
            return false;

        int minCount = _closedLoop ? 3 : 2;
        if (freeControlPoints.Count <= minCount)
            return false;

        freeControlPoints.RemoveAt(index);
        _mergeFootprintUseExactPolyline = false;
        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        ApplyToWall();
        return true;
    }

    public bool RemoveControlPointAt(int index)
    {
        if (shapeKind == ShapeKind.Free)
        {
            if (_closedLoop && _closedFreeOrthogonalPolylineMode && freeControlPoints != null &&
                index == freeControlPoints.Count)
                return false;
            return RemoveFreeControlPointAt(index);
        }

        if (index < 0 || index >= ControlPointCount)
            return false;

        List<Vector3> freePts;
        int removeIndex;
        bool isClosed = _closedLoop;

        if (shapeKind == ShapeKind.Rectangle)
        {
            // Keep the 8 rectangle handles as independent points.
            freePts = new List<Vector3>(8);
            for (int i = 0; i < 8; i++)
                freePts.Add(GetRectangleControlPoint(i));

            if (index == 8)
            {
                // Center handle is not an edge point: remove nearest perimeter point.
                Vector3 selectedWorld = GetControlPointWorld(index);
                removeIndex = FindClosestPointIndexXZ(freePts, selectedWorld);
            }
            else
            {
                removeIndex = Mathf.Clamp(index, 0, freePts.Count - 1);
            }
        }
        else
        {
            Vector3 selectedWorld = GetControlPointWorld(index);
            List<Vector3> currentPath = GetPreviewPathWorld();
            if (currentPath == null || currentPath.Count < 3)
                return false;

            freePts = BuildUniquePathWithoutClosure(currentPath);
            if (freePts == null || freePts.Count < 3)
                return false;

            removeIndex = FindClosestPointIndexXZ(freePts, selectedWorld);
        }

        if (removeIndex < 0 || removeIndex >= freePts.Count)
            return false;

        int minCount = isClosed ? 3 : 2;
        if (freePts.Count <= minCount)
            return false;

        freePts.RemoveAt(removeIndex);
        if (freePts.Count < minCount)
            return false;

        shapeKind = ShapeKind.Free;
        freeControlPoints = freePts;
        _mergeFootprintUseExactPolyline = false;
        _closedFreeOrthogonalPolylineMode = false;
        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        ComputeBounds(BuildFreePreviewPath() ?? freeControlPoints);
        ApplyToWall();
        return true;
    }

    static List<Vector3> BuildUniquePathWithoutClosure(List<Vector3> path)
    {
        List<Vector3> result = new List<Vector3>();
        if (path == null || path.Count == 0)
            return result;

        const float epsSqr = 0.0001f * 0.0001f;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            if (result.Count == 0 || (p - result[result.Count - 1]).sqrMagnitude > epsSqr)
                result.Add(p);
        }

        if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude <= epsSqr)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    static int FindClosestPointIndexXZ(List<Vector3> points, Vector3 target)
    {
        if (points == null || points.Count == 0)
            return -1;

        int bestIndex = -1;
        float bestSqr = float.MaxValue;
        Vector2 t = new Vector2(target.x, target.z);
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = new Vector2(points[i].x, points[i].z);
            float sqr = (p - t).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    int FindBestInsertIndexForFreePoint(Vector3 worldPos)
    {
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return -1;

        float bestDist = float.MaxValue;
        int bestInsertIndex = -1;

        if (_closedLoop)
        {
            int count = freeControlPoints.Count;

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                float dist = DistancePointToSegmentXZ(worldPos, freeControlPoints[i], freeControlPoints[next]);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestInsertIndex = i + 1;
                }
            }

            return bestInsertIndex;
        }

        for (int i = 0; i < freeControlPoints.Count - 1; i++)
        {
            float dist = DistancePointToSegmentXZ(worldPos, freeControlPoints[i], freeControlPoints[i + 1]);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestInsertIndex = i + 1;
            }
        }

        return bestInsertIndex;
    }

    static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 pp = new Vector2(p.x, p.z);
        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);

        Vector2 ab = bb - aa;
        float len2 = ab.sqrMagnitude;

        if (len2 < 0.000001f)
            return Vector2.Distance(pp, aa);

        float t = Vector2.Dot(pp - aa, ab) / len2;
        t = Mathf.Clamp01(t);

        Vector2 proj = aa + ab * t;
        return Vector2.Distance(pp, proj);
    }

    /// <summary>
    /// Projection sur le segment XZ ; clamp le paramètre pour rester à au moins <paramref name="minDistFromEnds"/>
    /// des extrémités (évite arêtes nulles). Retourne false si le segment est trop court pour couper.
    /// </summary>
    static bool TryProjectAndClampOnSegmentXZ(Vector3 p, Vector3 a, Vector3 b, float minDistFromEnds, out Vector3 onSegment)
    {
        onSegment = p;
        Vector2 pp = new Vector2(p.x, p.z);
        Vector2 aa = new Vector2(a.x, a.z);
        Vector2 bb = new Vector2(b.x, b.z);
        Vector2 ab = bb - aa;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-12f)
        {
            onSegment = new Vector3(a.x, p.y, a.z);
            return false;
        }

        float segLen = Mathf.Sqrt(len2);
        if (segLen < minDistFromEnds * 2.0001f)
            return false;

        float t = Vector2.Dot(pp - aa, ab) / len2;
        float margin = Mathf.Clamp(minDistFromEnds / segLen, 0.04f, 0.48f);
        t = Mathf.Clamp(t, margin, 1f - margin);

        Vector2 xz = aa + ab * t;
        onSegment = new Vector3(xz.x, p.y, xz.y);
        return true;
    }

    bool TrySetupEllipse(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points == null || points.Count < 10) return false;

        List<Vector2> hull = ComputeConvexHullXZ(points);
        if (hull.Count < 5)
            return false;

        shapeY = points[0].y;
        ComputeBounds(points);

        float rx = (maxX - minX) * 0.5f;
        float rz = (maxZ - minZ) * 0.5f;
        Vector3 center = GetBoundsCenter();

        if (rx < 0.1f || rz < 0.1f)
            return false;

        float error = 0f;
        int count = 0;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float nx = (p.x - center.x) / rx;
            float nz = (p.z - center.z) / rz;
            float v = nx * nx + nz * nz;
            error += Mathf.Abs(v - 1f);
            count++;
        }

        float avgError = error / Mathf.Max(1, count);
        if (avgError > 0.20f)
            return false;

        ellipseRotationRad = 0f;
        return true;
    }

    Vector3 GetEllipseControlPoint(int index)
    {
        if (Mathf.Abs(ellipseRotationRad) > 1e-5f)
        {
            List<Vector3> samples = BuildEllipsePath(64);
            if (samples == null || samples.Count < 2)
                return GetBoundsCenter();

            float minx = float.MaxValue, maxx = float.MinValue, minz = float.MaxValue, maxz = float.MinValue;
            int last = samples.Count > 1 &&
                       Vector3.Distance(samples[0], samples[samples.Count - 1]) < 0.001f
                ? samples.Count - 1
                : samples.Count;
            for (int i = 0; i < last; i++)
            {
                Vector3 p = samples[i];
                minx = Mathf.Min(minx, p.x);
                maxx = Mathf.Max(maxx, p.x);
                minz = Mathf.Min(minz, p.z);
                maxz = Mathf.Max(maxz, p.z);
            }

            Vector3 c = new Vector3((minx + maxx) * 0.5f, shapeY, (minz + maxz) * 0.5f);
            switch (index)
            {
                case 0: return new Vector3(maxx, shapeY, c.z);
                case 1: return new Vector3(c.x, shapeY, maxz);
                case 2: return new Vector3(minx, shapeY, c.z);
                case 3: return new Vector3(c.x, shapeY, minz);
                case 4: return c;
                default: return c;
            }
        }

        Vector3 c0 = GetBoundsCenter();

        switch (index)
        {
            case 0: return new Vector3(maxX, shapeY, c0.z);
            case 1: return new Vector3(c0.x, shapeY, maxZ);
            case 2: return new Vector3(minX, shapeY, c0.z);
            case 3: return new Vector3(c0.x, shapeY, minZ);
            case 4: return c0;
            default: return c0;
        }
    }

    void SetEllipseControlPoint(int index, Vector3 worldPos)
    {
        if (index != 4 && Mathf.Abs(ellipseRotationRad) > 1e-5f)
            FlattenEllipseRotationIntoBounds();

        switch (index)
        {
            case 0:
                maxX = Mathf.Max(worldPos.x, minX + 0.1f);
                break;

            case 1:
                maxZ = Mathf.Max(worldPos.z, minZ + 0.1f);
                break;

            case 2:
                minX = Mathf.Min(worldPos.x, maxX - 0.1f);
                break;

            case 3:
                minZ = Mathf.Min(worldPos.z, maxZ - 0.1f);
                break;

            case 4:
            {
                Vector3 cBefore = GetBoundsCenter();
                Vector3 delta = new Vector3(worldPos.x - cBefore.x, 0f, worldPos.z - cBefore.z);
                minX += delta.x;
                maxX += delta.x;
                minZ += delta.z;
                maxZ += delta.z;
                MoveAttachedInteriorOpenWallsByDeltaXZ(delta);
                break;
            }
        }
    }

    List<Vector3> BuildEllipsePath(int resolution)
    {
        List<Vector3> pts = new List<Vector3>();
        resolution = Mathf.Max(16, resolution);

        Vector3 c = GetBoundsCenter();
        float rx = Mathf.Max(0.1f, (maxX - minX) * 0.5f);
        float rz = Mathf.Max(0.1f, (maxZ - minZ) * 0.5f);

        float cosR = Mathf.Cos(ellipseRotationRad);
        float sinR = Mathf.Sin(ellipseRotationRad);

        for (int i = 0; i < resolution; i++)
        {
            float t = (i / (float)resolution) * Mathf.PI * 2f;
            float lx = Mathf.Cos(t) * rx;
            float lz = Mathf.Sin(t) * rz;
            float x = lx * cosR - lz * sinR;
            float z = lx * sinR + lz * cosR;
            pts.Add(new Vector3(c.x + x, shapeY, c.z + z));
        }

        pts.Add(pts[0]);
        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    void FlattenEllipseRotationIntoBounds()
    {
        if (Mathf.Abs(ellipseRotationRad) < 1e-6f)
            return;

        List<Vector3> path = BuildEllipsePath(Mathf.Max(64, ellipseWallResolution));
        if (path == null || path.Count < 2)
            return;

        if (path.Count > 1 && Vector3.Distance(path[0], path[path.Count - 1]) < 0.001f)
            path.RemoveAt(path.Count - 1);

        ComputeBounds(path);
        ellipseRotationRad = 0f;
    }

    /// <summary>
    /// Rotation dans le plan XZ autour du point central (handle molette lorsque le centre est sélectionné).
    /// </summary>
    public void ApplyCenterScrollRotation(float scrollAxis)
    {
        if (Mathf.Abs(scrollAxis) < 1e-7f)
            return;

        float deltaRad = scrollAxis * Mathf.Deg2Rad * centerScrollRotationDegrees;

        switch (shapeKind)
        {
            case ShapeKind.Rectangle:
                RotateRectangleAroundOriginAxes(deltaRad, snapToGridAfterRotation: false);
                break;

            case ShapeKind.Triangle:
                RotateTriangleAroundCentroid(deltaRad);
                break;

            case ShapeKind.Ellipse:
                ellipseRotationRad += deltaRad;
                while (ellipseRotationRad > Mathf.PI) ellipseRotationRad -= Mathf.PI * 2f;
                while (ellipseRotationRad < -Mathf.PI) ellipseRotationRad += Mathf.PI * 2f;
                break;

            case ShapeKind.OpenArc:
                arcStartRad += deltaRad;
                arcEndRad += deltaRad;
                break;

            case ShapeKind.Free:
                if (_closedLoop && (_closedFreeOrthogonalPolylineMode || IsClosedFreeDesignatedHouseLotForPivot()))
                    RotateFreePolygonAroundCentroid(deltaRad);
                else if (!_closedLoop && freeControlPoints != null && freeControlPoints.Count >= 2)
                    RotateOpenFreePolylineAroundCentroid(deltaRad);
                else
                    return;
                break;

            default:
                return;
        }

        ApplyToWall();
    }

    /// <summary>
    /// Rotation quantifiée dans le plan XZ autour du point central.
    /// Ex: positionsPerTurn = 16 => pas de 22.5°.
    /// </summary>
    public void ApplyCenterScrollRotationQuantized(int steps, int positionsPerTurn)
    {
        if (steps == 0)
            return;

        positionsPerTurn = Mathf.Max(1, positionsPerTurn);
        float deltaRad = (Mathf.PI * 2f / positionsPerTurn) * steps;

        switch (shapeKind)
        {
            case ShapeKind.Rectangle:
                RotateRectangleAroundOriginAxes(deltaRad, snapToGridAfterRotation: true);
                break;

            case ShapeKind.Triangle:
                RotateTriangleAroundCentroid(deltaRad);
                break;

            case ShapeKind.Ellipse:
                ellipseRotationRad += deltaRad;
                while (ellipseRotationRad > Mathf.PI) ellipseRotationRad -= Mathf.PI * 2f;
                while (ellipseRotationRad < -Mathf.PI) ellipseRotationRad += Mathf.PI * 2f;
                break;

            case ShapeKind.OpenArc:
                arcStartRad += deltaRad;
                arcEndRad += deltaRad;
                break;

            case ShapeKind.Free:
                if (_closedLoop && (_closedFreeOrthogonalPolylineMode || IsClosedFreeDesignatedHouseLotForPivot()))
                    RotateFreePolygonAroundCentroid(deltaRad);
                else if (!_closedLoop && freeControlPoints != null && freeControlPoints.Count >= 2)
                    RotateOpenFreePolylineAroundCentroid(deltaRad);
                else
                    return;
                break;

            default:
                return;
        }

        ApplyToWall();
    }

    void RotateOpenFreePolylineAroundCentroid(float deltaRad)
    {
        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return;

        Vector3 c = GetOpenFreeCenterWorld();
        float cos = Mathf.Cos(deltaRad);
        float sin = Mathf.Sin(deltaRad);

        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            float lx = p.x - c.x;
            float lz = p.z - c.z;
            float rx = lx * cos - lz * sin;
            float rz = lx * sin + lz * cos;
            freeControlPoints[i] = new Vector3(c.x + rx, shapeY, c.z + rz);
        }

        for (int i = 0; i < _freeRawPath.Count; i++)
        {
            Vector3 p = _freeRawPath[i];
            float lx = p.x - c.x;
            float lz = p.z - c.z;
            float rx = lx * cos - lz * sin;
            float rz = lx * sin + lz * cos;
            _freeRawPath[i] = new Vector3(c.x + rx, shapeY, c.z + rz);
        }

        _mergeFootprintUseExactPolyline = false;
        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        ClampOpenFreeVerticesToInteriorLotConstraint();

        WallDrawInput di = FindFirstObjectByType<WallDrawInput>(FindObjectsInactive.Include);
        if (di != null && di.enableGridSnap && di.snapToHierarchicalVisualGrid)
        {
            SnapAllControlPointsToHierarchicalGrid(di, applyToWallAtEnd: false);
            ClampOpenFreeVerticesToInteriorLotConstraint();
        }
    }

    void RotateRectangleAroundOriginAxes(float deltaRad, bool snapToGridAfterRotation)
    {
        float c = Mathf.Cos(deltaRad);
        float s = Mathf.Sin(deltaRad);
        Vector2 ax = rectangleAxisX;
        Vector2 ay = rectangleAxisY;

        // Centre géométrique monde (pas seulement rectangleOriginXZ : après snap / collage les locaux
        // peuvent ne plus être centrés sur 0, et tourner les axes sans recaler l'origine faisait pivoter
        // autour de la carte / origine locale au lieu du centre du lot).
        float midLX = (rectangleMinX + rectangleMaxX) * 0.5f;
        float midLY = (rectangleMinY + rectangleMaxY) * 0.5f;
        Vector2 centerWorldXZ = rectangleOriginXZ + ax * midLX + ay * midLY;

        Vector2 newAx = new Vector2(c * ax.x - s * ax.y, s * ax.x + c * ax.y);
        Vector2 newAy = new Vector2(c * ay.x - s * ay.y, s * ay.x + c * ay.y);
        if (newAx.sqrMagnitude > 1e-8f) newAx.Normalize();
        if (newAy.sqrMagnitude > 1e-8f) newAy.Normalize();

        rectangleAxisX = newAx;
        rectangleAxisY = newAy;
        rectangleOriginXZ = centerWorldXZ - newAx * midLX - newAy * midLY;

        ComputeBounds(BuildRectanglePath());

        if (snapToGridAfterRotation)
            TrySnapRectangleToHierarchicalGridAfterRotation();
    }

    /// <summary>
    /// Après rotation molette quantifiée : recale le rectangle sur la même maille que le dessin / les handles
    /// (snap des 4 coins + recentrage du centre monde sur un point de la grille).
    /// </summary>
    void TrySnapRectangleToHierarchicalGridAfterRotation()
    {
        WallDrawInput di = FindFirstObjectByType<WallDrawInput>(FindObjectsInactive.Include);
        if (di == null || !di.enableGridSnap || !di.snapToHierarchicalVisualGrid)
            return;

        SnapAllControlPointsToHierarchicalGrid(di);
        SnapRectangleCenterWorldToGrid(di);
    }

    void SnapRectangleCenterWorldToGrid(WallDrawInput drawInput)
    {
        if (drawInput == null)
            return;

        Vector3 c = GetRectangleCenterWorld();
        Vector3 s = drawInput.SnapWorldPointForEditing(c);
        rectangleOriginXZ += new Vector2(s.x - c.x, s.z - c.z);
        ComputeBounds(BuildRectanglePath());
    }

    void RotateTriangleAroundCentroid(float deltaRad)
    {
        if (triangleControlPoints == null || triangleControlPoints.Count < 3)
            return;

        Vector3 cen = (triangleControlPoints[0] + triangleControlPoints[1] + triangleControlPoints[2]) / 3f;
        cen = new Vector3(cen.x, shapeY, cen.z);
        float cos = Mathf.Cos(deltaRad);
        float sin = Mathf.Sin(deltaRad);

        for (int i = 0; i < 3; i++)
        {
            float dx = triangleControlPoints[i].x - cen.x;
            float dz = triangleControlPoints[i].z - cen.z;
            float nx = dx * cos - dz * sin;
            float nz = dx * sin + dz * cos;
            triangleControlPoints[i] = new Vector3(cen.x + nx, shapeY, cen.z + nz);
        }

        EnsureCounterClockwiseXZ(triangleControlPoints);
        ComputeBounds(BuildTrianglePath());
    }

    bool TrySetupRectangle(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points == null || points.Count < 4) return false;

        List<Vector3> work = new List<Vector3>(points);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 4)
            return false;

        List<Vector2> hull = ComputeConvexHullXZ(work);
        if (hull == null || hull.Count < 4 || hull.Count > 10)
            return false;

        if (!TryBuildOrientedRectangleFromPath(
                work,
                out Vector2 origin,
                out Vector2 axisX,
                out Vector2 axisY,
                out float localMinX,
                out float localMaxX,
                out float localMinY,
                out float localMaxY))
            return false;

        shapeY = work[0].y;
        rectangleOriginXZ = origin;
        rectangleAxisX = axisX;
        rectangleAxisY = axisY;
        rectangleMinX = localMinX;
        rectangleMaxX = localMaxX;
        rectangleMinY = localMinY;
        rectangleMaxY = localMaxY;

        ComputeBounds(BuildRectanglePath());
        return true;
    }

    bool TrySetupRectangleForcedFromDetected(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points == null || points.Count < 4) return false;

        List<Vector3> work = new List<Vector3>(points);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 4)
            return false;

        if (!TryBuildOrientedRectangleFromPathRelaxed(
                work,
                out Vector2 origin,
                out Vector2 axisX,
                out Vector2 axisY,
                out float localMinX,
                out float localMaxX,
                out float localMinY,
                out float localMaxY))
            return false;

        shapeY = work[0].y;
        rectangleOriginXZ = origin;
        rectangleAxisX = axisX;
        rectangleAxisY = axisY;
        rectangleMinX = localMinX;
        rectangleMaxX = localMaxX;
        rectangleMinY = localMinY;
        rectangleMaxY = localMaxY;

        ComputeBounds(BuildRectanglePath());
        return true;
    }

    bool TryBuildOrientedRectangleFromPath(
        List<Vector3> work,
        out Vector2 origin,
        out Vector2 axisX,
        out Vector2 axisY,
        out float localMinX,
        out float localMaxX,
        out float localMinY,
        out float localMaxY)
    {
        return TryBuildOrientedRectangleFromPathInternal(
            work,
            0.68f,
            out origin,
            out axisX,
            out axisY,
            out localMinX,
            out localMaxX,
            out localMinY,
            out localMaxY);
    }

    bool TryBuildOrientedRectangleFromPathRelaxed(
        List<Vector3> work,
        out Vector2 origin,
        out Vector2 axisX,
        out Vector2 axisY,
        out float localMinX,
        out float localMaxX,
        out float localMinY,
        out float localMaxY)
    {
        return TryBuildOrientedRectangleFromPathInternal(
            work,
            0.20f,
            out origin,
            out axisX,
            out axisY,
            out localMinX,
            out localMaxX,
            out localMinY,
            out localMaxY);
    }

    bool TryBuildOrientedRectangleFromPathInternal(
        List<Vector3> work,
        float minScore,
        out Vector2 origin,
        out Vector2 axisX,
        out Vector2 axisY,
        out float localMinX,
        out float localMaxX,
        out float localMinY,
        out float localMaxY)
    {
        origin = Vector2.zero;
        axisX = Vector2.right;
        axisY = Vector2.up;
        localMinX = localMaxX = localMinY = localMaxY = 0f;

        if (work == null || work.Count < 4)
            return false;

        List<Vector2> hull = ComputeConvexHullXZ(work);
        if (hull == null || hull.Count < 4)
            return false;

        Vector2 hullCenter = Vector2.zero;
        for (int i = 0; i < hull.Count; i++)
            hullCenter += hull[i];
        hullCenter /= hull.Count;

        float bestScore = -1f;
        bool found = false;

        Vector2 bestOrigin = Vector2.zero;
        Vector2 bestAxisX = Vector2.right;
        Vector2 bestAxisY = Vector2.up;
        float bestMinX = 0f;
        float bestMaxX = 0f;
        float bestMinY = 0f;
        float bestMaxY = 0f;

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % hull.Count];
            Vector2 edge = b - a;

            if (edge.sqrMagnitude < 0.0001f)
                continue;

            Vector2 candidateAxisX = edge.normalized;
            Vector2 candidateAxisY = new Vector2(-candidateAxisX.y, candidateAxisX.x);

            List<Vector2> localPts = new List<Vector2>(work.Count);

            float minPX = float.PositiveInfinity;
            float maxPX = float.NegativeInfinity;
            float minPY = float.PositiveInfinity;
            float maxPY = float.NegativeInfinity;

            for (int p = 0; p < work.Count; p++)
            {
                Vector2 wp = new Vector2(work[p].x, work[p].z);
                Vector2 v = wp - hullCenter;

                float px = Vector2.Dot(v, candidateAxisX);
                float py = Vector2.Dot(v, candidateAxisY);

                localPts.Add(new Vector2(px, py));

                if (px < minPX) minPX = px;
                if (px > maxPX) maxPX = px;
                if (py < minPY) minPY = py;
                if (py > maxPY) maxPY = py;
            }

            float width = maxPX - minPX;
            float height = maxPY - minPY;

            if (width < 0.1f || height < 0.1f)
                continue;

            float edgeTol = Mathf.Max(width, height) * 0.10f;
            edgeTol = Mathf.Max(edgeTol, 0.08f);

            float cornerTol = edgeTol * 1.75f;

            int nearEdgeCount = 0;
            bool hitLeft = false;
            bool hitRight = false;
            bool hitBottom = false;
            bool hitTop = false;

            for (int p = 0; p < localPts.Count; p++)
            {
                Vector2 lp = localPts[p];

                float dLeft = Mathf.Abs(lp.x - minPX);
                float dRight = Mathf.Abs(lp.x - maxPX);
                float dBottom = Mathf.Abs(lp.y - minPY);
                float dTop = Mathf.Abs(lp.y - maxPY);

                float minEdge = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
                if (minEdge <= edgeTol)
                    nearEdgeCount++;

                if (dLeft <= edgeTol) hitLeft = true;
                if (dRight <= edgeTol) hitRight = true;
                if (dBottom <= edgeTol) hitBottom = true;
                if (dTop <= edgeTol) hitTop = true;
            }

            float edgeRatio = nearEdgeCount / (float)Mathf.Max(1, localPts.Count);

            int edgesHit = 0;
            if (hitLeft) edgesHit++;
            if (hitRight) edgesHit++;
            if (hitBottom) edgesHit++;
            if (hitTop) edgesHit++;

            float edgeCoverage = edgesHit / 4f;

            int cornersHit = 0;
            if (HasPointNearLocalCorner(localPts, minPX, maxPY, cornerTol)) cornersHit++;
            if (HasPointNearLocalCorner(localPts, maxPX, maxPY, cornerTol)) cornersHit++;
            if (HasPointNearLocalCorner(localPts, maxPX, minPY, cornerTol)) cornersHit++;
            if (HasPointNearLocalCorner(localPts, minPX, minPY, cornerTol)) cornersHit++;

            float cornerCoverage = cornersHit / 4f;

            float boxArea = width * height;
            float polyArea = ComputeAbsoluteSignedAreaXZ(work);
            float fillRatio = 0f;
            if (boxArea > 0.0001f)
                fillRatio = Mathf.Clamp01(polyArea / boxArea);

            float score =
                edgeRatio * 0.45f +
                edgeCoverage * 0.20f +
                cornerCoverage * 0.20f +
                fillRatio * 0.15f;

            if (edgesHit < 4)
                score -= 0.20f;

            if (cornerCoverage < 0.50f)
                score -= 0.15f;

            if (edgeRatio < 0.70f)
                score -= 0.15f;

            if (score > bestScore)
            {
                float midX = (minPX + maxPX) * 0.5f;
                float midY = (minPY + maxPY) * 0.5f;

                bestScore = score;
                bestAxisX = candidateAxisX;
                bestAxisY = candidateAxisY;
                bestOrigin = hullCenter + candidateAxisX * midX + candidateAxisY * midY;

                bestMinX = minPX - midX;
                bestMaxX = maxPX - midX;
                bestMinY = minPY - midY;
                bestMaxY = maxPY - midY;

                found = true;
            }
        }

        if (!found)
            return false;

        if (bestScore < minScore)
            return false;

        origin = bestOrigin;
        axisX = bestAxisX;
        axisY = bestAxisY;
        localMinX = bestMinX;
        localMaxX = bestMaxX;
        localMinY = bestMinY;
        localMaxY = bestMaxY;

        return true;
    }

    bool HasPointNearLocalCorner(List<Vector2> localPts, float x, float y, float tolerance)
    {
        float sqrTol = tolerance * tolerance;
        Vector2 corner = new Vector2(x, y);

        for (int i = 0; i < localPts.Count; i++)
        {
            if ((localPts[i] - corner).sqrMagnitude <= sqrTol)
                return true;
        }

        return false;
    }

    Vector3 GetRectangleControlPoint(int index)
    {
        Vector3 topLeft = RectangleLocalToWorld(rectangleMinX, rectangleMaxY);
        Vector3 topRight = RectangleLocalToWorld(rectangleMaxX, rectangleMaxY);
        Vector3 bottomRight = RectangleLocalToWorld(rectangleMaxX, rectangleMinY);
        Vector3 bottomLeft = RectangleLocalToWorld(rectangleMinX, rectangleMinY);

        switch (index)
        {
            case 0: return topLeft;
            case 1: return (topLeft + topRight) * 0.5f;
            case 2: return topRight;
            case 3: return (topRight + bottomRight) * 0.5f;
            case 4: return bottomRight;
            case 5: return (bottomRight + bottomLeft) * 0.5f;
            case 6: return bottomLeft;
            case 7: return (bottomLeft + topLeft) * 0.5f;
            default: return GetRectangleCenterWorld();
        }
    }

    /// <summary>
    /// Appends the 8 axis-aligned rectangle perimeter handles (corners + edge mids), indices 0–7.
    /// Used when merging lots so the combined Free shape can preserve the same wall-edge handles.
    /// </summary>
    public void AppendRectanglePerimeterHandlesTo(List<Vector3> dst)
    {
        if (shapeKind != ShapeKind.Rectangle)
            return;
        for (int i = 0; i < 8; i++)
            dst.Add(GetRectangleControlPoint(i));
    }

    /// <summary>
    /// Dense samples on the ellipse/circle wall ring (same idea as <see cref="AppendRectanglePerimeterHandlesTo"/>).
    /// Required for merged-lot handle preservation when one source is a maison cercle.
    /// </summary>
    public void AppendEllipsePerimeterHandlesTo(List<Vector3> dst)
    {
        if (shapeKind != ShapeKind.Ellipse)
            return;

        List<Vector3> ring = BuildEllipsePath(Mathf.Clamp(Mathf.Max(ellipseWallResolution * 2, 72), 72, 160));
        if (ring == null || ring.Count < 2)
            return;

        int last = ring.Count;
        if (last >= 2 && Vector3.Distance(ring[0], ring[last - 1]) < 0.001f)
            last--;

        for (int i = 0; i < last; i++)
            dst.Add(ring[i]);
    }

    /// <summary>
    /// Centre XZ et rayon (demi-grand axe max) pour adoucir le bossage carré englobant → arc lors d’une fusion de lots.
    /// </summary>
    public bool TryGetEllipseCircleApproxXZForLotMerge(out Vector2 centerXZ, out float radius)
    {
        centerXZ = default;
        radius = 0f;
        if (shapeKind != ShapeKind.Ellipse)
            return false;

        Vector3 c = GetBoundsCenter();
        float rx = (maxX - minX) * 0.5f;
        float rz = (maxZ - minZ) * 0.5f;
        if (rx < 0.06f || rz < 0.06f)
            return false;

        centerXZ = new Vector2(c.x, c.z);
        radius = Mathf.Max(rx, rz);
        return radius >= 0.12f;
    }

    /// <summary>
    /// Vertices and edge midpoints of the triangle wall ring for merge handle preservation.
    /// </summary>
    public void AppendTrianglePerimeterHandlesTo(List<Vector3> dst)
    {
        if (shapeKind != ShapeKind.Triangle || triangleControlPoints == null || triangleControlPoints.Count < 3)
            return;

        Vector3 a = new Vector3(triangleControlPoints[0].x, shapeY, triangleControlPoints[0].z);
        Vector3 b = new Vector3(triangleControlPoints[1].x, shapeY, triangleControlPoints[1].z);
        Vector3 c = new Vector3(triangleControlPoints[2].x, shapeY, triangleControlPoints[2].z);
        dst.Add(a);
        dst.Add((a + b) * 0.5f);
        dst.Add(b);
        dst.Add((b + c) * 0.5f);
        dst.Add(c);
        dst.Add((c + a) * 0.5f);
    }

    void SetRectangleControlPoint(int index, Vector3 worldPos)
    {
        if (index == 8)
        {
            Vector3 centerBefore = GetRectangleCenterWorld();
            Vector3 delta = new Vector3(worldPos.x - centerBefore.x, 0f, worldPos.z - centerBefore.z);
            rectangleOriginXZ += new Vector2(delta.x, delta.z);
            ComputeBounds(BuildRectanglePath());
            MoveAttachedInteriorOpenWallsByDeltaXZ(delta);
            return;
        }

        Vector2 local = RectangleWorldToLocal(worldPos);

        switch (index)
        {
            case 0:
                rectangleMinX = Mathf.Min(local.x, rectangleMaxX - 0.1f);
                rectangleMaxY = Mathf.Max(local.y, rectangleMinY + 0.1f);
                break;

            case 2:
                rectangleMaxX = Mathf.Max(local.x, rectangleMinX + 0.1f);
                rectangleMaxY = Mathf.Max(local.y, rectangleMinY + 0.1f);
                break;

            case 4:
                rectangleMaxX = Mathf.Max(local.x, rectangleMinX + 0.1f);
                rectangleMinY = Mathf.Min(local.y, rectangleMaxY - 0.1f);
                break;

            case 6:
                rectangleMinX = Mathf.Min(local.x, rectangleMaxX - 0.1f);
                rectangleMinY = Mathf.Min(local.y, rectangleMaxY - 0.1f);
                break;

            case 1:
                rectangleMaxY = Mathf.Max(local.y, rectangleMinY + 0.1f);
                break;

            case 3:
                rectangleMaxX = Mathf.Max(local.x, rectangleMinX + 0.1f);
                break;

            case 5:
                rectangleMinY = Mathf.Min(local.y, rectangleMaxY - 0.1f);
                break;

            case 7:
                rectangleMinX = Mathf.Min(local.x, rectangleMaxX - 0.1f);
                break;
        }

        ComputeBounds(BuildRectanglePath());
    }

    List<Vector3> BuildRectanglePath()
    {
        Vector3 topLeft = RectangleLocalToWorld(rectangleMinX, rectangleMaxY);
        Vector3 bottomLeft = RectangleLocalToWorld(rectangleMinX, rectangleMinY);
        Vector3 bottomRight = RectangleLocalToWorld(rectangleMaxX, rectangleMinY);
        Vector3 topRight = RectangleLocalToWorld(rectangleMaxX, rectangleMaxY);

        List<Vector3> pts = new List<Vector3>
        {
            topLeft,
            bottomLeft,
            bottomRight,
            topRight,
            topLeft
        };

        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    Vector3 RectangleLocalToWorld(float x, float y)
    {
        Vector2 p = rectangleOriginXZ + rectangleAxisX * x + rectangleAxisY * y;
        return new Vector3(p.x, shapeY, p.y);
    }

    Vector2 RectangleWorldToLocal(Vector3 worldPos)
    {
        Vector2 v = new Vector2(worldPos.x, worldPos.z) - rectangleOriginXZ;
        return new Vector2(Vector2.Dot(v, rectangleAxisX), Vector2.Dot(v, rectangleAxisY));
    }

    Vector3 GetRectangleCenterWorld()
    {
        float cx = (rectangleMinX + rectangleMaxX) * 0.5f;
        float cy = (rectangleMinY + rectangleMaxY) * 0.5f;
        return RectangleLocalToWorld(cx, cy);
    }

    /// <summary>
    /// Axis-aligned footprint area in local rectangle space (axis X/Y are unit vectors).
    /// </summary>
    public float GetRectangleFootprintArea()
    {
        if (shapeKind != ShapeKind.Rectangle)
            return 0f;
        float w = rectangleMaxX - rectangleMinX;
        float h = rectangleMaxY - rectangleMinY;
        return Mathf.Max(0f, w) * Mathf.Max(0f, h);
    }

    /// <summary>
    /// True when the XZ projection of <paramref name="worldPos"/> lies inside the rectangle interior,
    /// with an inset from edges so selection matches a click toward the center of the lot (not on the walls).
    /// </summary>
    public bool ContainsWorldPointInRectangleFootprintXZ(Vector3 worldPos, float edgeInsetLocal = 0.06f)
    {
        if (shapeKind != ShapeKind.Rectangle || !IsClosedLoopPath)
            return false;

        Vector2 local = RectangleWorldToLocal(worldPos);
        float w = rectangleMaxX - rectangleMinX;
        float h = rectangleMaxY - rectangleMinY;
        if (w <= 0.0001f || h <= 0.0001f)
            return false;

        float inset = Mathf.Max(edgeInsetLocal, Mathf.Min(w, h) * 0.06f);
        if (inset * 2f >= Mathf.Min(w, h))
            inset = Mathf.Min(w, h) * 0.1f;

        return local.x >= rectangleMinX + inset && local.x <= rectangleMaxX - inset
            && local.y >= rectangleMinY + inset && local.y <= rectangleMaxY - inset;
    }

    bool TrySetupTriangle(List<Vector3> points)
    {
        if (!_closedLoop) return false;
        if (points == null || points.Count < 3) return false;

        List<Vector3> work = new List<Vector3>(points);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.001f)
            work.RemoveAt(work.Count - 1);
        if (work.Count < 3) return false;

        List<Vector2> hull = ComputeConvexHullXZ(work);
        if (hull == null || hull.Count < 3) return false;

        float bestArea = 0f;
        int ia = -1, ib = -1, ic = -1;
        for (int i = 0; i < hull.Count - 2; i++)
        {
            for (int j = i + 1; j < hull.Count - 1; j++)
            {
                for (int k = j + 1; k < hull.Count; k++)
                {
                    float area = Mathf.Abs(Cross(hull[j] - hull[i], hull[k] - hull[i]));
                    if (area > bestArea)
                    {
                        bestArea = area;
                        ia = i; ib = j; ic = k;
                    }
                }
            }
        }

        if (ia < 0 || ib < 0 || ic < 0 || bestArea < 0.01f)
            return false;

        shapeY = work[0].y;
        triangleControlPoints = new List<Vector3>(3)
        {
            new Vector3(hull[ia].x, shapeY, hull[ia].y),
            new Vector3(hull[ib].x, shapeY, hull[ib].y),
            new Vector3(hull[ic].x, shapeY, hull[ic].y)
        };
        EnsureCounterClockwiseXZ(triangleControlPoints);
        ComputeBounds(BuildTrianglePath());
        return true;
    }

    List<Vector3> BuildTrianglePath()
    {
        if (triangleControlPoints == null || triangleControlPoints.Count < 3)
            return new List<Vector3>();

        List<Vector3> pts = new List<Vector3>(4)
        {
            new Vector3(triangleControlPoints[0].x, shapeY, triangleControlPoints[0].z),
            new Vector3(triangleControlPoints[1].x, shapeY, triangleControlPoints[1].z),
            new Vector3(triangleControlPoints[2].x, shapeY, triangleControlPoints[2].z)
        };
        EnsureCounterClockwiseXZ(pts);
        pts.Add(pts[0]);
        return pts;
    }

    bool TrySetupOpenArc(List<Vector3> points, bool requireRadialFit = true)
    {
        if (points == null || points.Count < 3)
            return false;

        Vector2 a = new Vector2(points[0].x, points[0].z);
        Vector2 b = new Vector2(points[points.Count - 1].x, points[points.Count - 1].z);
        if (Vector2.Distance(a, b) < 0.08f)
            return false;

        Vector2 mid = points.Count == 3
            ? new Vector2(points[1].x, points[1].z)
            : GetPathMidpointXZ(points);

        if (!TryCircleFromThreePointsXZ(a, mid, b, out arcCenterXZ, out float r))
            return false;

        if (r < 0.08f || float.IsNaN(r) || float.IsInfinity(r))
            return false;

        arcRadius = r;
        shapeY = points[0].y;
        arcStartRad = Mathf.Atan2(a.y - arcCenterXZ.y, a.x - arcCenterXZ.x);
        arcEndRad = Mathf.Atan2(b.y - arcCenterXZ.y, b.x - arcCenterXZ.x);
        float midAng = Mathf.Atan2(mid.y - arcCenterXZ.y, mid.x - arcCenterXZ.x);
        arcCounterClockwise = IsAngleOnCcwArc(arcStartRad, arcEndRad, midAng);

        float sweepDeg = arcCounterClockwise
            ? PositiveDeltaAngle(arcStartRad, arcEndRad) * Mathf.Rad2Deg
            : PositiveDeltaAngle(arcEndRad, arcStartRad) * Mathf.Rad2Deg;
        if (sweepDeg < 15f || sweepDeg > 350f)
            return false;

        if (requireRadialFit)
        {
            float sumErr = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 pv = new Vector2(points[i].x, points[i].z);
                sumErr += Mathf.Abs(Vector2.Distance(pv, arcCenterXZ) - arcRadius);
            }

            if (sumErr / points.Count > 0.42f)
                return false;
        }

        _closedLoop = false;
        return true;
    }

    Vector3 GetOpenArcControlPoint(int index)
    {
        Vector2 p0 = arcCenterXZ + new Vector2(Mathf.Cos(arcStartRad), Mathf.Sin(arcStartRad)) * arcRadius;
        Vector2 p1 = arcCenterXZ + new Vector2(Mathf.Cos(arcEndRad), Mathf.Sin(arcEndRad)) * arcRadius;
        switch (index)
        {
            case 0: return new Vector3(p0.x, shapeY, p0.y);
            case 1: return new Vector3(p1.x, shapeY, p1.y);
            case 2: return new Vector3(arcCenterXZ.x, shapeY, arcCenterXZ.y);
            case 3:
            {
                float sweep = arcCounterClockwise
                    ? PositiveDeltaAngle(arcStartRad, arcEndRad)
                    : PositiveDeltaAngle(arcEndRad, arcStartRad);
                float midAng = arcCounterClockwise
                    ? arcStartRad + sweep * 0.5f
                    : arcStartRad - sweep * 0.5f;
                Vector2 pm = arcCenterXZ + new Vector2(Mathf.Cos(midAng), Mathf.Sin(midAng)) * arcRadius;
                return new Vector3(pm.x, shapeY, pm.y);
            }
            default: return Vector3.zero;
        }
    }

    void SetOpenArcControlPoint(int index, Vector3 worldPos)
    {
        Vector2 xz = new Vector2(worldPos.x, worldPos.z);
        switch (index)
        {
            case 0:
            {
                Vector2 d = xz - arcCenterXZ;
                if (d.sqrMagnitude < 1e-8f)
                    return;
                d = d.normalized * arcRadius;
                arcStartRad = Mathf.Atan2(d.y, d.x);
                break;
            }
            case 1:
            {
                Vector2 d = xz - arcCenterXZ;
                if (d.sqrMagnitude < 1e-8f)
                    return;
                d = d.normalized * arcRadius;
                arcEndRad = Mathf.Atan2(d.y, d.x);
                break;
            }
            case 2:
            {
                Vector2 before = arcCenterXZ;
                Vector3 delta = new Vector3(xz.x - before.x, 0f, xz.y - before.y);
                arcCenterXZ = xz;
                MoveAttachedInteriorOpenWallsByDeltaXZ(delta);
                break;
            }

            // Apex on the arc: refit circle through fixed endpoints A,B and new point M (sagitta / "height").
            case 3:
            {
                Vector2 a = arcCenterXZ + new Vector2(Mathf.Cos(arcStartRad), Mathf.Sin(arcStartRad)) * arcRadius;
                Vector2 b = arcCenterXZ + new Vector2(Mathf.Cos(arcEndRad), Mathf.Sin(arcEndRad)) * arcRadius;
                Vector2 m = xz;
                if (!TryCircleFromThreePointsXZ(a, m, b, out Vector2 newCenter, out float newR))
                    return;

                if (newR < 0.05f || float.IsNaN(newR) || float.IsInfinity(newR))
                    return;

                arcCenterXZ = newCenter;
                arcRadius = newR;
                arcStartRad = Mathf.Atan2(a.y - newCenter.y, a.x - newCenter.x);
                arcEndRad = Mathf.Atan2(b.y - newCenter.y, b.x - newCenter.x);
                float midAng = Mathf.Atan2(m.y - newCenter.y, m.x - newCenter.x);
                arcCounterClockwise = IsAngleOnCcwArc(arcStartRad, arcEndRad, midAng);
                break;
            }

            default:
                return;
        }

        ComputeBounds(BuildOpenArcPath(Mathf.Max(24, openArcWallResolution)));
    }

    List<Vector3> BuildOpenArcPath(int resolution)
    {
        float sweep = arcCounterClockwise
            ? PositiveDeltaAngle(arcStartRad, arcEndRad)
            : PositiveDeltaAngle(arcEndRad, arcStartRad);

        float arcLen = arcRadius * sweep;
        int target = Mathf.Clamp(Mathf.RoundToInt(arcLen / 0.08f), 10, Mathf.Max(10, resolution));
        List<Vector3> pts = new List<Vector3>(target + 1);

        for (int i = 0; i <= target; i++)
        {
            float t = target <= 0 ? 0f : i / (float)target;
            float ang = arcCounterClockwise
                ? arcStartRad + sweep * t
                : arcStartRad - sweep * t;
            float wx = arcCenterXZ.x + Mathf.Cos(ang) * arcRadius;
            float wz = arcCenterXZ.y + Mathf.Sin(ang) * arcRadius;
            pts.Add(new Vector3(wx, shapeY, wz));
        }

        return pts;
    }

    static bool TryCircleFromThreePointsXZ(Vector2 a, Vector2 b, Vector2 c, out Vector2 center, out float radius)
    {
        center = Vector2.zero;
        radius = 0f;

        float d = 2f * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
        if (Mathf.Abs(d) < 0.0001f)
            return false;

        float a2 = a.sqrMagnitude;
        float b2 = b.sqrMagnitude;
        float c2 = c.sqrMagnitude;

        float ux = (a2 * (b.y - c.y) + b2 * (c.y - a.y) + c2 * (a.y - b.y)) / d;
        float uy = (a2 * (c.x - b.x) + b2 * (a.x - c.x) + c2 * (b.x - a.x)) / d;
        center = new Vector2(ux, uy);
        radius = Vector2.Distance(center, a);
        return !float.IsNaN(radius) && !float.IsInfinity(radius) && radius > 0.0001f;
    }

    static float PositiveDeltaAngle(float from, float to)
    {
        float d = to - from;
        while (d < 0f) d += Mathf.PI * 2f;
        while (d >= Mathf.PI * 2f) d -= Mathf.PI * 2f;
        return d;
    }

    static bool IsAngleOnCcwArc(float from, float to, float angle)
    {
        float total = PositiveDeltaAngle(from, to);
        float part = PositiveDeltaAngle(from, angle);
        return part <= total + 0.0001f;
    }

    static Vector2 GetPathMidpointXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0)
            return Vector2.zero;
        if (pts.Count == 1)
            return new Vector2(pts[0].x, pts[0].z);

        List<Vector2> pts2 = new List<Vector2>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
            pts2.Add(new Vector2(pts[i].x, pts[i].z));

        float total = 0f;
        for (int i = 1; i < pts2.Count; i++)
            total += Vector2.Distance(pts2[i - 1], pts2[i]);
        if (total < 0.0001f)
            return pts2[pts2.Count / 2];

        float half = total * 0.5f;
        float acc = 0f;
        for (int i = 1; i < pts2.Count; i++)
        {
            float seg = Vector2.Distance(pts2[i - 1], pts2[i]);
            if (acc + seg >= half)
            {
                float t = Mathf.InverseLerp(acc, acc + seg, half);
                return Vector2.Lerp(pts2[i - 1], pts2[i], t);
            }

            acc += seg;
        }

        return pts2[pts2.Count / 2];
    }

    void SetupFree(List<Vector3> points)
    {
        freeControlPoints.Clear();

        float localSpacing = freeHandleSpacing;
        int localMin = minFreeHandles;
        int localMax = maxFreeHandles;

        if (_closedLoop)
        {
            localSpacing *= closedFreeHandleSpacingMultiplier;
            localMin = Mathf.Max(localMin, closedFreeMinHandles);
            localMax = Mathf.Max(localMax, closedFreeMaxHandles);
        }

        float perimeter = ComputePerimeter(points, _closedLoop);
        int wantedHandles = Mathf.RoundToInt(perimeter / Mathf.Max(0.1f, localSpacing));
        wantedHandles = Mathf.Clamp(wantedHandles, localMin, localMax);

        if (points.Count <= wantedHandles)
        {
            freeControlPoints.AddRange(points);
        }
        else
        {
            for (int i = 0; i < wantedHandles; i++)
            {
                float t = _closedLoop
                    ? i / (float)wantedHandles
                    : i / (float)(wantedHandles - 1);

                int idx = Mathf.RoundToInt(t * (points.Count - 1));
                idx = Mathf.Clamp(idx, 0, points.Count - 1);
                freeControlPoints.Add(points[idx]);
            }
        }

        if (_closedLoop && freeControlPoints.Count > 1)
        {
            if (Vector3.Distance(freeControlPoints[0], freeControlPoints[freeControlPoints.Count - 1]) < 0.0001f)
                freeControlPoints.RemoveAt(freeControlPoints.Count - 1);
        }

        if (freeControlPoints.Count > 0)
            shapeY = freeControlPoints[0].y;
    }

    void CacheInitialFreeRawPath(List<Vector3> points)
    {
        _freeRawPath.Clear();
        _freePathWasEdited = false;

        if (points == null || points.Count < 2)
            return;

        for (int i = 0; i < points.Count; i++)
            _freeRawPath.Add(new Vector3(points[i].x, shapeY, points[i].z));
    }

    Vector3 GetClosedFreeLotCentroidWorld()
    {
        if (freeControlPoints == null || freeControlPoints.Count == 0)
            return new Vector3(0f, shapeY, 0f);

        if (IsClosedFreeDesignatedHouseLotForPivot())
        {
            if (TryComputeClosedRingCentroidXZ(GetPreviewPathWorld(), out Vector2 previewXZ))
                return new Vector3(previewXZ.x, shapeY, previewXZ.y);
        }

        if (_closedLoop && _closedFreeOrthogonalPolylineMode && freeControlPoints.Count >= 3)
        {
            if (TryComputePolygonAreaCentroidXZ(freeControlPoints, out Vector2 xz))
                return new Vector3(xz.x, shapeY, xz.y);
        }

        float sx = 0f, sz = 0f;
        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            sx += freeControlPoints[i].x;
            sz += freeControlPoints[i].z;
        }

        float inv = 1f / freeControlPoints.Count;
        return new Vector3(sx * inv, shapeY, sz * inv);
    }

    Vector3 GetOpenFreeCenterWorld()
    {
        if (freeControlPoints == null || freeControlPoints.Count == 0)
            return new Vector3(0f, shapeY, 0f);

        float sx = 0f;
        float sz = 0f;
        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            sx += freeControlPoints[i].x;
            sz += freeControlPoints[i].z;
        }

        float inv = 1f / freeControlPoints.Count;
        return new Vector3(sx * inv, shapeY, sz * inv);
    }

    /// <summary>
    /// Centroïde géométrique du polygone (plan XZ), pas la moyenne des sommets — plus stable quand un coin est tiré fort.
    /// </summary>
    static bool TryComputePolygonAreaCentroidXZ(IReadOnlyList<Vector3> ring, out Vector2 centroidXZ)
    {
        centroidXZ = default;
        int n = ring.Count;
        if (n < 3)
            return false;

        double a = 0.0;
        double cx = 0.0;
        double cz = 0.0;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double xi = ring[i].x;
            double zi = ring[i].z;
            double xj = ring[j].x;
            double zj = ring[j].z;
            double cross = xi * zj - xj * zi;
            a += cross;
            cx += (xi + xj) * cross;
            cz += (zi + zj) * cross;
        }

        a *= 0.5;
        if (System.Math.Abs(a) < 1e-14)
            return false;

        cx /= 6.0 * a;
        cz /= 6.0 * a;
        centroidXZ = new Vector2((float)cx, (float)cz);
        return true;
    }

    static bool TryComputeClosedRingCentroidXZ(List<Vector3> path, out Vector2 centroidXZ)
    {
        centroidXZ = default;
        if (path == null || path.Count < 3)
            return false;

        int n = path.Count;
        if (n >= 2 && Vector3.Distance(path[0], path[n - 1]) < 0.001f)
            n--;
        if (n < 3)
            return false;

        List<Vector3> ring = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
            ring.Add(path[i]);

        return TryComputePolygonAreaCentroidXZ(ring, out centroidXZ);
    }

    /// <summary>
    /// Repousse la cible du drag si elle entre dans le rayon autour d’un autre sommet (sauf voisins d’anneau, avec exception partenaire).
    /// Les paires interne/externe déjà au même XZ ne sont pas ignorées : on applique bien le rayon minimal.
    /// </summary>
    Vector3 ClampOrthogonalDragWorldPosAwayFromOtherVertices(int draggedIndex, Vector3 worldPos)
    {
        if (freeControlPoints == null)
            return worldPos;

        bool anyAvoidance =
            orthogonalVertexAvoidanceRadiusXZ > 0.0001f ||
            orthogonalStackedPartnerMinDistanceXZ > 0.0001f;
        if (!anyAvoidance)
            return worldPos;

        int n = freeControlPoints.Count;
        if (n < 2 || draggedIndex < 0 || draggedIndex >= n)
            return worldPos;

        const float stackedEpsSq = 1e-8f;
        int iPrev = (draggedIndex - 1 + n) % n;
        int iNext = (draggedIndex + 1) % n;

        Vector2 p = new Vector2(worldPos.x, worldPos.z);
        Vector2 dragFrom = new Vector2(freeControlPoints[draggedIndex].x, freeControlPoints[draggedIndex].z);

        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            for (int j = 0; j < n; j++)
            {
                if (j == draggedIndex)
                    continue;
                bool ringNeighbor = j == iPrev || j == iNext;
                if (ringNeighbor && (j != _orthoStackPartnerIndex || _orthoStackPartnerIndex < 0))
                    continue;

                Vector2 q = new Vector2(freeControlPoints[j].x, freeControlPoints[j].z);
                Vector2 dragPt = new Vector2(freeControlPoints[draggedIndex].x, freeControlPoints[draggedIndex].z);
                if ((q - dragPt).sqrMagnitude < stackedEpsSq &&
                    !IsReflexSalientStackableCornerPair(draggedIndex, j))
                    continue;

                Vector2 d = p - q;
                float dsq = d.sqrMagnitude;
                float r = orthogonalVertexAvoidanceRadiusXZ;
                if (IsReflexSalientStackableCornerPair(draggedIndex, j))
                    r = Mathf.Max(r, orthogonalStackedPartnerMinDistanceXZ);
                if (r <= 0.0001f)
                    continue;

                float rSq = r * r;
                if (dsq >= rSq)
                    continue;

                moved = true;
                if (dsq > 1e-12f)
                    p = q + d.normalized * r;
                else
                {
                    Vector2 away = p - dragFrom;
                    if (away.sqrMagnitude < 1e-12f)
                        away = new Vector2(r, 0f);
                    else
                        away = away.normalized * r;
                    p = q + away;
                }
            }
            if (!moved)
                break;
        }

        return new Vector3(p.x, shapeY, p.y);
    }

    bool TryApplyFreeShapeControlPointWorld(int index, Vector3 worldPos)
    {
        if (freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;

        if (_closedLoop && _closedFreeOrthogonalPolylineMode && n > 0 && index == n)
        {
            CopyOrthogonalFreeRingToBackup();

            Vector3 cBefore = GetClosedFreeLotCentroidWorld();
            Vector3 delta = new Vector3(worldPos.x - cBefore.x, 0f, worldPos.z - cBefore.z);

            for (int i = 0; i < n; i++)
            {
                Vector3 p = freeControlPoints[i];
                freeControlPoints[i] = new Vector3(p.x + delta.x, shapeY, p.z + delta.z);
            }

            FinalizeOrthogonalFreeRingAfterControlEdit();
            if (CurrentOrthogonalFreeRingHasSelfIntersectionXZ(editingVertexIndex: -1))
            {
                RestoreOrthogonalFreeRingFromBackup();
                return false;
            }

            InvalidateStraightClosedPreviewCache();
            // Same ordering as <see cref="TrySetMergedOrthogonalShapeCentroidWorld"/>: lot ring must be current
            // before attached interior walls translate + clamp.
            MoveAttachedInteriorOpenWallsByDeltaXZ(delta);
            return true;
        }

        if (!_closedLoop && allowVerticalScrollElevation && n >= 2 && index == n)
        {
            Vector3 cBefore = GetOpenFreeCenterWorld();
            Vector3 delta = new Vector3(worldPos.x - cBefore.x, 0f, worldPos.z - cBefore.z);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = freeControlPoints[i];
                freeControlPoints[i] = new Vector3(p.x + delta.x, shapeY, p.z + delta.z);
            }

            for (int i = 0; i < _freeRawPath.Count; i++)
            {
                Vector3 p = _freeRawPath[i];
                _freeRawPath[i] = new Vector3(p.x + delta.x, shapeY, p.z + delta.z);
            }

            _mergeFootprintUseExactPolyline = false;
            _freePathWasEdited = true;
            InvalidateStraightClosedPreviewCache();
            ClampOpenFreeVerticesToInteriorLotConstraint();
            return true;
        }

        if (index < 0 || index >= n)
            return false;

        if (_closedLoop && _closedFreeOrthogonalPolylineMode)
        {
            CopyOrthogonalFreeRingToBackup();
            worldPos = ClampOrthogonalDragWorldPosAwayFromOtherVertices(index, worldPos);
            ApplyOrthogonalClosedFreeVertexDrag(index, worldPos);
            if (CurrentOrthogonalFreeRingHasSelfIntersectionXZ(editingVertexIndex: index))
            {
                RestoreOrthogonalFreeRingFromBackup();
                return false;
            }
        }
        else
        {
            freeControlPoints[index] = new Vector3(worldPos.x, shapeY, worldPos.z);
            // Ne pas effacer le mode « contour fusionné = polyline exacte » : c’est ce qui basculait le mesh en
            // <see cref="BuildClosedCatmullRomThroughControls"/> (murs arrondis). Synchroniser le raw sur les poignées.
            if (_closedLoop && _mergeFootprintUseExactPolyline)
            {
                _freeRawPath.Clear();
                for (int i = 0; i < freeControlPoints.Count; i++)
                {
                    Vector3 p = freeControlPoints[i];
                    _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
                }
            }
            else
            {
                _mergeFootprintUseExactPolyline = false;
            }

            _freePathWasEdited = true;
            ClampOpenFreeVerticesToInteriorLotConstraint();
        }

        InvalidateStraightClosedPreviewCache();
        return true;
    }

    void FinalizeOrthogonalFreeRingAfterControlEdit()
    {
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode)
            return;

        // Contour issu d'une fusion de lots : ne pas réduire à « 1 milieu par arête » ni projeter sur des cordes droites,
        // sinon un bossage circulaire (points denses sur l'arc) redevient un carré à 4 points.
        if (_mergeFootprintUseExactPolyline)
        {
            EnsureClosedFreeRingCounterClockwiseXZ();
            _freeRawPath.Clear();
            for (int i = 0; i < freeControlPoints.Count; i++)
            {
                Vector3 p = freeControlPoints[i];
                _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
            }

            _freePathWasEdited = true;
            InvalidateStraightClosedPreviewCache();
            return;
        }

        // Avant toute chose : coller les poignées quasi confondues, puis retirer les sommets en trop (sauf paire rentrant/saillant).
        // Uniquement sur le périmètre maison — pas sur un mur intérieur rattaché au lot (sinon fusion de poignées trop proches).
        const float alignStackEps = 0.02f;
        if (interiorWallsStayInsideLot == null)
        {
            AlignOrthogonalRingVerticesToExactSuperposition(alignStackEps);
            TryRemoveOrthogonalRingDuplicateVerticesNonStackableSameXZ(alignStackEps);
        }

        float reflexPairMinSep = Mathf.Max(
            orthogonalVertexAvoidanceRadiusXZ,
            orthogonalStackedPartnerMinDistanceXZ);
        EnforceReflexSalientStackablePairsMinimumSeparationXZ(reflexPairMinSep);

        // Avant toute redistribution : casser les arêtes diagonales — sinon Redistribute projette les milieux sur la
        // corde [coin,coin] et fige un mur « en biais ».
        SplitDiagonalEdgesToAxisManhattanXZ(alignStackEps);

        // Même ordre de grandeur que l’alignement : sinon deux coins quasi superposés peuvent passer Enforce et
        // « décombiner » A/B en murs droits recanonicalisés.
        float superposedEpsSq = alignStackEps * alignStackEps;

        // Poignées superposées volontairement : ne pas appeler Enforce (reconstruction coin→milieu qui fusionnerait mal les doublons).
        // En revanche il faut quand même RedistributeAllMids : sinon après un drag sur un coin interne+externe, une seule
        // des deux arêtes incidentes voit ses milieux recalés — l’autre « mur » reste avec d’anciennes positions.
        if (freeControlPoints != null && RingHasDuplicateSuperposedVerticesXZ(freeControlPoints, superposedEpsSq))
        {
            RedistributeAllMidsOnOrthogonalRing();
            EnsureClosedFreeRingCounterClockwiseXZ();

            _freeRawPath.Clear();
            for (int i = 0; i < freeControlPoints.Count; i++)
            {
                Vector3 p = freeControlPoints[i];
                _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
            }

            _freePathWasEdited = true;
            InvalidateStraightClosedPreviewCache();
            return;
        }

        // Redistribute : recolle les milieux existants ; Enforce recanonicalise coin + milieu par arête (toujours,
        // pour éviter trous de poignées milieu après faux « coins » sur alignements presque droits).
        RedistributeAllMidsOnOrthogonalRing();
        if (orthogonalEnforceExactlyOneMidpointPerWallFace)
            EnforceOrthogonalRingSingleMidpointPerEdge();

        // Après recollage / éventuelle reconstruction coin→milieu : projette chaque arête sur la corde [coin,coin].
        // Sinon petits écarts (drag, float) « bombent » le polygone → façade lisse / arrondie au lieu d’orthogonal net.
        ProjectOrthogonalMidsOntoCornerSegmentChordsXZ();

        EnsureClosedFreeRingCounterClockwiseXZ();

        _freeRawPath.Clear();
        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        if (_mergeFootprintUseExactPolyline)
        {
            _freeRawPath.Clear();
            for (int i = 0; i < freeControlPoints.Count; i++)
            {
                Vector3 p = freeControlPoints[i];
                _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
            }
        }
    }

    void CopyOrthogonalFreeRingToBackup()
    {
        _orthogonalRingEditBackup.Clear();
        if (freeControlPoints == null)
            return;
        for (int i = 0; i < freeControlPoints.Count; i++)
            _orthogonalRingEditBackup.Add(freeControlPoints[i]);
    }

    void RestoreOrthogonalFreeRingFromBackup()
    {
        if (freeControlPoints == null)
            return;
        freeControlPoints.Clear();
        for (int i = 0; i < _orthogonalRingEditBackup.Count; i++)
            freeControlPoints.Add(_orthogonalRingEditBackup[i]);

        _freeRawPath.Clear();
        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            _freeRawPath.Add(new Vector3(p.x, shapeY, p.z));
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
    }

    /// <param name="editingVertexIndex">Sommet qu’on vient de modifier ; si ≥ 0 et proche d’un autre sommet (fusion), pas de rejet auto-croisement.</param>
    bool CurrentOrthogonalFreeRingHasSelfIntersectionXZ(int editingVertexIndex = -1)
    {
        if (freeControlPoints == null || freeControlPoints.Count < 4)
            return false;
        var work = new List<Vector3>(freeControlPoints.Count);
        for (int i = 0; i < freeControlPoints.Count; i++)
            work.Add(new Vector3(freeControlPoints[i].x, shapeY, freeControlPoints[i].z));

        if (editingVertexIndex >= 0 && editingVertexIndex < work.Count)
        {
            const float axisEps = 0.0025f;
            int n = work.Count;
            Vector3 p = work[editingVertexIndex];

            // Lot orthogonal : arête valide voisine = même X (mur vertical) ou même Z (horizontal) avec séparation non nulle.
            // Sans ce cas, le test isotrope ci‑dessous ne s’applique qu’à < ~0,65 m du voisin : au-delà, le rejet
            // par auto-croisement reprend et donne l’impression d’un « plafond » en tirant le long d’un long mur (marche, sortie latérale).
            if (_closedFreeOrthogonalPolylineMode)
            {
                for (int s = 0; s < 2; s++)
                {
                    int j = s == 0 ? (editingVertexIndex - 1 + n) % n : (editingVertexIndex + 1) % n;
                    Vector3 q = work[j];
                    float adx = Mathf.Abs(p.x - q.x);
                    float adz = Mathf.Abs(p.z - q.z);
                    bool orthoNeighborEdge = (adx <= axisEps && adz > axisEps) || (adz <= axisEps && adx > axisEps);
                    if (orthoNeighborEdge)
                        return false;
                }
            }

            if (orthogonalEditMergeProximitySkipIntersectionM > 0.001f)
            {
                float rSq = orthogonalEditMergeProximitySkipIntersectionM * orthogonalEditMergeProximitySkipIntersectionM;
                for (int j = 0; j < work.Count; j++)
                {
                    if (j == editingVertexIndex)
                        continue;
                    float dx = work[j].x - p.x;
                    float dz = work[j].z - p.z;
                    if (dx * dx + dz * dz <= rSq)
                        return false;
                }
            }
        }

        return HasSelfIntersectionXZ(work);
    }

    static bool SegmentParallelToEdgeDirectionXZ(Vector2 segmentXZ, Vector2 edgeDirUnit)
    {
        float ls = segmentXZ.sqrMagnitude;
        if (ls < 1e-12f)
            return false;
        segmentXZ /= Mathf.Sqrt(ls);
        return Mathf.Abs(Vector2.Dot(segmentXZ, edgeDirUnit)) >= 0.98f;
    }

    /// <summary>Directions normalisées XZ : mur horizontal ou vertical (lots fusionnés orthogonaux).</summary>
    static bool IsAxisAlignedUnitDirectionXZ(Vector2 v)
    {
        return Mathf.Abs(v.x) >= 0.94f && Mathf.Abs(v.y) <= 0.12f
            || Mathf.Abs(v.y) >= 0.94f && Mathf.Abs(v.x) <= 0.12f;
    }

    static bool IsRingVertexStraightMidXZ(IReadOnlyList<Vector3> ring, int i, bool relaxedOrthogonalStraightMid)
    {
        int nv = ring.Count;
        if (nv < 3)
            return false;

        int ip = (i - 1 + nv) % nv;
        int inx = (i + 1) % nv;
        Vector3 p = ring[i];
        Vector3 pr = ring[ip];
        Vector3 nx = ring[inx];
        Vector2 dIn = new Vector2(p.x - pr.x, p.z - pr.z);
        Vector2 dOut = new Vector2(nx.x - p.x, nx.z - p.z);
        if (dIn.sqrMagnitude < 1e-8f || dOut.sqrMagnitude < 1e-8f)
            return false;
        dIn.Normalize();
        dOut.Normalize();
        float dot = Vector2.Dot(dIn, dOut);
        if (relaxedOrthogonalStraightMid)
        {
            if (dot > 0.88f)
                return true;
            if (dot > 0.78f && IsAxisAlignedUnitDirectionXZ(dIn) && IsAxisAlignedUnitDirectionXZ(dOut))
                return true;
            return false;
        }

        return dot > 0.92f;
    }

    bool IsRingVertexStraightMidXZ(int i) =>
        freeControlPoints != null &&
        IsRingVertexStraightMidXZ(freeControlPoints, i, _closedLoop && _closedFreeOrthogonalPolylineMode);

    /// <summary>
    /// Coin rentrant du contour orthogonal CCW (~270°, « ongle intérieur » d’un L/U). Les coins saillants (~90°) renvoient false.
    /// Utilisé pour ne pas lancer la fusion de lots après le drag de cette poignée seule.
    /// </summary>
    public bool IsOrthogonalReflexInteriorCornerAtIndex(int index)
    {
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        if (n < 3 || index < 0 || index >= n)
            return false;

        if (IsRingVertexStraightMidXZ(index))
            return false;

        int ip = (index - 1 + n) % n;
        int inx = (index + 1) % n;
        Vector3 prev = freeControlPoints[ip];
        Vector3 curr = freeControlPoints[index];
        Vector3 next = freeControlPoints[inx];

        Vector2 dIn = new Vector2(curr.x - prev.x, curr.z - prev.z);
        Vector2 dOut = new Vector2(next.x - curr.x, next.z - curr.z);
        if (dIn.sqrMagnitude < 1e-10f || dOut.sqrMagnitude < 1e-10f)
            return false;

        float cross = dIn.x * dOut.y - dIn.y * dOut.x;
        // Contour CCW : au coin rentrant le trajet tourne à droite (cross < 0).
        return cross < -1e-7f;
    }

    /// <summary>
    /// Coin saillant (~90° convexe) sur contour CCW orthogonal, hors milieu d’arête.
    /// </summary>
    public bool IsOrthogonalSalientCornerAtIndex(int index)
    {
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        if (n < 3 || index < 0 || index >= n)
            return false;

        if (IsRingVertexStraightMidXZ(index))
            return false;

        if (IsOrthogonalReflexInteriorCornerAtIndex(index))
            return false;

        int ip = (index - 1 + n) % n;
        int inx = (index + 1) % n;
        Vector3 prev = freeControlPoints[ip];
        Vector3 curr = freeControlPoints[index];
        Vector3 next = freeControlPoints[inx];

        Vector2 dIn = new Vector2(curr.x - prev.x, curr.z - prev.z);
        Vector2 dOut = new Vector2(next.x - curr.x, next.z - curr.z);
        if (dIn.sqrMagnitude < 1e-10f || dOut.sqrMagnitude < 1e-10f)
            return false;

        float cross = dIn.x * dOut.y - dIn.y * dOut.x;
        return cross > 1e-7f;
    }

    /// <summary>
    /// Coin rentrant superposé au coin saillant (même XZ) : pas d’aimantation vers d’autres murs ni fusion de lots (évite les enchaînements).
    /// Inclut le cas où la géométrie est dégénérée (tests saillant/rentrant impossibles).
    /// </summary>
    public bool ShouldSuppressInterWallSnapAndLotMergeAtIndex(int index)
    {
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        if (n < 3 || index < 0 || index >= n)
            return false;

        const float pairEps = 0.0012f;
        float pairEpsSq = pairEps * pairEps;
        Vector3 p = GetControlPointWorld(index);

        for (int j = 0; j < n; j++)
        {
            if (j == index)
                continue;

            Vector3 q = GetControlPointWorld(j);
            float dx = q.x - p.x;
            float dz = q.z - p.z;
            if (dx * dx + dz * dz > pairEpsSq)
                continue;

            if (IsRingVertexStraightMidXZ(index) || IsRingVertexStraightMidXZ(j))
                continue;

            bool refIdx = IsOrthogonalReflexInteriorCornerAtIndex(index);
            bool salIdx = IsOrthogonalSalientCornerAtIndex(index);
            bool refJ = IsOrthogonalReflexInteriorCornerAtIndex(j);
            bool salJ = IsOrthogonalSalientCornerAtIndex(j);

            if ((refIdx && salJ) || (salIdx && refJ))
                return true;

            bool unkIdx = !refIdx && !salIdx;
            bool unkJ = !refJ && !salJ;

            // Empilé : un saillant net + l’autre coin non classé (souvent rentrant dégénéré)
            if ((salJ && unkIdx) || (salIdx && unkJ))
                return true;

            // Empilé : un rentrant net + l’autre coin non classé
            if ((refJ && unkIdx) || (refIdx && unkJ))
                return true;

            // Les deux coins non classés au même XZ (géométrie dégénérée une fois collés)
            if (unkIdx && unkJ)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Autre sommet au même XZ formant une paire coin rentrant / coin saillant (même critère que la suppression snap inter-murs).
    /// </summary>
    bool TryFindReflexSalientStackPartnerIndex(int index, out int partner)
    {
        partner = -1;
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        if (n < 3 || index < 0 || index >= n)
            return false;

        const float pairEps = 0.0012f;
        float pairEpsSq = pairEps * pairEps;
        Vector3 p = GetControlPointWorld(index);

        for (int j = 0; j < n; j++)
        {
            if (j == index)
                continue;

            Vector3 q = GetControlPointWorld(j);
            float dx = q.x - p.x;
            float dz = q.z - p.z;
            if (dx * dx + dz * dz > pairEpsSq)
                continue;

            if (IsRingVertexStraightMidXZ(index) || IsRingVertexStraightMidXZ(j))
                continue;

            bool refIdx = IsOrthogonalReflexInteriorCornerAtIndex(index);
            bool salIdx = IsOrthogonalSalientCornerAtIndex(index);
            bool refJ = IsOrthogonalReflexInteriorCornerAtIndex(j);
            bool salJ = IsOrthogonalSalientCornerAtIndex(j);

            if ((refIdx && salJ) || (salIdx && refJ))
            {
                partner = j;
                return true;
            }

            bool unkIdx = !refIdx && !salIdx;
            bool unkJ = !refJ && !salJ;

            if ((salJ && unkIdx) || (salIdx && unkJ))
            {
                partner = j;
                return true;
            }

            if ((refJ && unkIdx) || (refIdx && unkJ))
            {
                partner = j;
                return true;
            }

            if (unkIdx && unkJ)
            {
                partner = j;
                return true;
            }
        }

        return false;
    }

    bool IsReflexSalientStackableCornerPair(int i, int j)
    {
        if (i == j)
            return false;

        bool refI = IsOrthogonalReflexInteriorCornerAtIndex(i);
        bool salI = IsOrthogonalSalientCornerAtIndex(i);
        bool refJ = IsOrthogonalReflexInteriorCornerAtIndex(j);
        bool salJ = IsOrthogonalSalientCornerAtIndex(j);

        if ((refI && salJ) || (salI && refJ))
            return true;

        bool unkI = !refI && !salI;
        bool unkJ = !refJ && !salJ;

        if ((salJ && unkI) || (salI && unkJ))
            return true;
        if ((refJ && unkI) || (refI && unkJ))
            return true;
        return unkI && unkJ;
    }

    /// <summary>
    /// **Exactement deux** coins (pas milieu de mur) quasi superposés entre eux — pas un « amas » de poignées
    /// dans un rayon large (ça fusionnait des points le long d’un mur et supprimait un segment vertical).
    /// Tolérance serrée coin–coin ; préfère une paire rentrant/saillant classée, sinon repli si exactement deux candidats.
    /// </summary>
    bool TryFindStrictStackedCornerPairNearWorldXZ(Vector3 refWorldXZ, out int cornerA, out int cornerB)
    {
        cornerA = -1;
        cornerB = -1;
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        if (n < 3)
            return false;

        const float gatherEps = 0.008f;
        float gatherEpsSq = gatherEps * gatherEps;
        const float stackTightEps = 0.004f;
        float stackTightEpsSq = stackTightEps * stackTightEps;

        var candidates = new List<int>(8);
        for (int i = 0; i < n; i++)
        {
            if (IsRingVertexStraightMidXZ(i))
                continue;

            Vector3 q = GetControlPointWorld(i);
            float dx = q.x - refWorldXZ.x;
            float dz = q.z - refWorldXZ.z;
            if (dx * dx + dz * dz <= gatherEpsSq)
                candidates.Add(i);
        }

        if (candidates.Count < 2)
            return false;

        bool PairwiseTight(int ia, int ib)
        {
            Vector3 pa = GetControlPointWorld(ia);
            Vector3 pb = GetControlPointWorld(ib);
            float sdx = pa.x - pb.x;
            float sdz = pa.z - pb.z;
            return sdx * sdx + sdz * sdz <= stackTightEpsSq;
        }

        for (int a = 0; a < candidates.Count; a++)
        {
            for (int b = a + 1; b < candidates.Count; b++)
            {
                int ia = candidates[a];
                int ib = candidates[b];
                if (!PairwiseTight(ia, ib))
                    continue;
                if (IsReflexSalientStackableCornerPair(ia, ib))
                {
                    cornerA = ia;
                    cornerB = ib;
                    return true;
                }
            }
        }

        if (candidates.Count == 2 && PairwiseTight(candidates[0], candidates[1]))
        {
            cornerA = candidates[0];
            cornerB = candidates[1];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Autre coin (pas milieu de mur) au même XZ que <paramref name="index"/> — repli si
    /// <see cref="TryFindStrictStackedCornerPairNearWorldXZ"/> ne trouve pas la paire (classifications rentrant/saillant).
    /// </summary>
    bool TryFindDuplicateCornerPartnerAtIndex(int index, Vector3 refWorldXZ, out int partner)
    {
        partner = -1;
        if (!UsesMergedLotOrthogonalHandles || freeControlPoints == null)
            return false;

        int n = freeControlPoints.Count;
        if (index < 0 || index >= n)
            return false;
        if (IsRingVertexStraightMidXZ(index))
            return false;

        const float tightEps = 0.004f;
        float tightSq = tightEps * tightEps;

        for (int j = 0; j < n; j++)
        {
            if (j == index)
                continue;
            if (IsRingVertexStraightMidXZ(j))
                continue;

            Vector3 q = GetControlPointWorld(j);
            float dx = q.x - refWorldXZ.x;
            float dz = q.z - refWorldXZ.z;
            if (dx * dx + dz * dz > tightSq)
                continue;

            partner = j;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reconstruit l’anneau à partir des coins : chaque face de mur (coin → coin) a exactement une poignée au milieu,
    /// ou aucune si l’arête est plus courte que <see cref="minClosedSegmentLength"/>.
    /// Supprime les milieux en trop et en insère une si elle manque.
    /// </summary>
    void EnforceOrthogonalRingSingleMidpointPerEdge()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode)
            return;
        if (_mergeFootprintUseExactPolyline)
            return;

        var old = new List<Vector3>(freeControlPoints.Count);
        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            old.Add(new Vector3(p.x, shapeY, p.z));
        }

        int n = old.Count;
        var cornerPos = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            if (!IsRingVertexStraightMidXZ(old, i, true))
                cornerPos.Add(old[i]);
        }

        if (cornerPos.Count < 3)
            return;

        float minLenSq = minClosedSegmentLength * minClosedSegmentLength;
        int nc = cornerPos.Count;
        var neu = new List<Vector3>(nc * 2);
        for (int k = 0; k < nc; k++)
        {
            Vector3 a = cornerPos[k];
            Vector3 b = cornerPos[(k + 1) % nc];
            neu.Add(a);
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            if (dx * dx + dz * dz >= minLenSq)
                neu.Add(new Vector3((a.x + b.x) * 0.5f, shapeY, (a.z + b.z) * 0.5f));
        }

        if (neu.Count < 3)
            return;

        freeControlPoints.Clear();
        for (int i = 0; i < neu.Count; i++)
            freeControlPoints.Add(neu[i]);

        _freeRawPath.Clear();
        for (int i = 0; i < neu.Count; i++)
            _freeRawPath.Add(freeControlPoints[i]);

        InvalidateStraightClosedPreviewCache();
    }

    /// <summary>
    /// Contour fusionné : garantit une poignée milieu unique par mur (entre deux coins).
    /// </summary>
    void InsertMidpointsOnCoinToCoinEdgesOrthogonalRing()
    {
        if (!orthogonalEnforceExactlyOneMidpointPerWallFace)
            return;
        EnforceOrthogonalRingSingleMidpointPerEdge();
    }

    int FindNextPolygonCornerForward(int cornerIdx)
    {
        int nv = freeControlPoints.Count;
        int start = (cornerIdx + 1) % nv;
        int k = start;
        for (int s = 0; s < nv; s++)
        {
            if (!IsRingVertexStraightMidXZ(k))
                return k;
            k = (k + 1) % nv;
            if (k == start)
                return start;
        }
        return start;
    }

    int FindPrevPolygonCornerBackward(int cornerIdx)
    {
        int nv = freeControlPoints.Count;
        int start = (cornerIdx - 1 + nv) % nv;
        int k = start;
        for (int s = 0; s < nv; s++)
        {
            if (!IsRingVertexStraightMidXZ(k))
                return k;
            k = (k - 1 + nv) % nv;
            if (k == start)
                return start;
        }
        return start;
    }

    /// <summary>
    /// Recolle chaque poignée « milieu de mur » sur la droite entre deux coins consécutifs (t = 1/(k+1), …).
    /// Nécessaire après translation d’un mur entier : les arêtes perpendiculaires ont un coin déplacé mais pas l’autre.
    /// </summary>
    void RedistributeAllMidsOnOrthogonalRing()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return;

        int nv = freeControlPoints.Count;
        int firstCorner = -1;
        for (int i = 0; i < nv; i++)
        {
            if (!IsRingVertexStraightMidXZ(i))
            {
                firstCorner = i;
                break;
            }
        }

        if (firstCorner < 0)
            return;

        int c = firstCorner;
        int guard = 0;
        do
        {
            if (++guard > nv + 2)
                break;

            int cn = FindNextPolygonCornerForward(c);
            RedistributeMidsOnRingEdgeForward(c, cn);
            c = cn;
        } while (c != firstCorner);
    }

    void RedistributeMidsOnRingEdgeForward(int cornerA, int cornerB)
    {
        if (cornerA == cornerB)
            return;

        int nv = freeControlPoints.Count;
        List<int> mids = _ringEdgeMidScratch;
        mids.Clear();

        int k = (cornerA + 1) % nv;
        int guard = 0;
        while (k != cornerB && guard < nv)
        {
            mids.Add(k);
            k = (k + 1) % nv;
            guard++;
        }

        if (mids.Count == 0)
            return;

        Vector3 a = freeControlPoints[cornerA];
        Vector3 b = freeControlPoints[cornerB];
        float inv = 1f / (mids.Count + 1);
        for (int j = 0; j < mids.Count; j++)
        {
            float t = (j + 1) * inv;
            int idx = mids[j];
            freeControlPoints[idx] = new Vector3(
                Mathf.Lerp(a.x, b.x, t),
                shapeY,
                Mathf.Lerp(a.z, b.z, t));
        }
    }

    static Vector2 ClosestPointOnSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sql = ab.sqrMagnitude;
        if (sql < 1e-12f)
            return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / sql);
        return a + ab * t;
    }

    /// <summary>
    /// Insère un sommet intermédiaire sur chaque arête XZ où dx et dz sont tous deux significatifs,
    /// pour obtenir uniquement des segments horizontaux ou verticaux (contour Manhattan).
    /// </summary>
    void SplitDiagonalEdgesToAxisManhattanXZ(float eps)
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode)
            return;

        float epsSqr = eps * eps;
        for (int iter = 0; iter < 32; iter++)
        {
            int n = freeControlPoints.Count;
            var neu = new List<Vector3>(n + 8);
            bool added = false;

            for (int i = 0; i < n; i++)
            {
                Vector3 a = freeControlPoints[i];
                neu.Add(new Vector3(a.x, shapeY, a.z));

                int j = (i + 1) % n;
                Vector3 b = freeControlPoints[j];
                float adx = Mathf.Abs(b.x - a.x);
                float adz = Mathf.Abs(b.z - a.z);
                if (adx <= eps || adz <= eps)
                    continue;

                Vector3 hFirst = new Vector3(b.x, shapeY, a.z);
                Vector3 vFirst = new Vector3(a.x, shapeY, b.z);
                float dhA = (hFirst.x - a.x) * (hFirst.x - a.x) + (hFirst.z - a.z) * (hFirst.z - a.z);
                float dhB = (b.x - hFirst.x) * (b.x - hFirst.x) + (b.z - hFirst.z) * (b.z - hFirst.z);
                float dvA = (vFirst.x - a.x) * (vFirst.x - a.x) + (vFirst.z - a.z) * (vFirst.z - a.z);
                float dvB = (b.x - vFirst.x) * (b.x - vFirst.x) + (b.z - vFirst.z) * (b.z - vFirst.z);
                bool hOk = dhA > epsSqr && dhB > epsSqr;
                bool vOk = dvA > epsSqr && dvB > epsSqr;

                Vector3 split;
                if (hOk && vOk)
                    split = (dhA + dhB) <= (dvA + dvB) ? hFirst : vFirst;
                else if (hOk)
                    split = hFirst;
                else if (vOk)
                    split = vFirst;
                else
                    continue;

                neu.Add(split);
                added = true;
            }

            if (!added)
                break;

            freeControlPoints.Clear();
            freeControlPoints.AddRange(neu);
        }
    }

    /// <summary>
    /// Après <see cref="RedistributeAllMidsOnOrthogonalRing"/> : projette chaque sommet « entre deux coins » sur le
    /// segment [coin,coin] en XZ. Évite les arêtes légèrement incurvées (bombement) qui donnent un rendu arrondi.
    /// </summary>
    void ProjectOrthogonalMidsOntoCornerSegmentChordsXZ()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return;
        if (!_closedLoop || !_closedFreeOrthogonalPolylineMode)
            return;
        if (_mergeFootprintUseExactPolyline)
            return;

        int nv = freeControlPoints.Count;
        int firstCorner = -1;
        for (int i = 0; i < nv; i++)
        {
            if (!IsRingVertexStraightMidXZ(i))
            {
                firstCorner = i;
                break;
            }
        }

        if (firstCorner < 0)
            return;

        int c = firstCorner;
        int guard = 0;
        do
        {
            if (++guard > nv + 4)
                break;

            int cn = FindNextPolygonCornerForward(c);
            Vector2 a = new Vector2(freeControlPoints[c].x, freeControlPoints[c].z);
            Vector2 b = new Vector2(freeControlPoints[cn].x, freeControlPoints[cn].z);

            int k = (c + 1) % nv;
            int g2 = 0;
            while (k != cn && g2 < nv)
            {
                Vector2 p = new Vector2(freeControlPoints[k].x, freeControlPoints[k].z);
                Vector2 q = ClosestPointOnSegmentXZ(p, a, b);
                freeControlPoints[k] = new Vector3(q.x, shapeY, q.y);
                k = (k + 1) % nv;
                g2++;
            }

            c = cn;
        } while (c != firstCorner);
    }

    /// <summary>
    /// Marche le contour dans le sens des indices croissants (mod n), de <paramref name="from"/> à <paramref name="to"/> inclus.
    /// </summary>
    static void WalkRingForwardInclusive(int from, int to, int n, System.Action<int> visit)
    {
        int k = from;
        for (int g = 0; g < n; g++)
        {
            visit(k);
            if (k == to)
                break;
            k = (k + 1) % n;
        }
    }

    /// <summary>
    /// Translate tout le tronçon colinéaire contenant <paramref name="index"/> par le delta XZ jusqu'à <paramref name="worldPos"/>.
    /// Utilisé par le latch « mur droit » et par un stroke commencé sur une poignée milieu d'arête (évite le drag coin vif).
    /// </summary>
    void ApplyOrthogonalRigidCollinearChainDrag(int index, Vector3 worldPos)
    {
        int n = freeControlPoints.Count;
        if (n < 3)
            return;

        int iPrev = (index - 1 + n) % n;
        int iNext = (index + 1) % n;
        Vector3 curr = freeControlPoints[index];
        Vector2 deltaFull = new Vector2(worldPos.x - curr.x, worldPos.z - curr.z);
        Vector2 delta = deltaFull;
        if (orthogonalMidHandleDragPerpendicularToWallOnly && _orthoDragKind == OrthoDragKind.RigidWallRun)
        {
            Vector2 vn = _orthoWallFrameV;
            if (vn.sqrMagnitude > 1e-12f)
            {
                vn.Normalize();
                delta = Vector2.Dot(deltaFull, vn) * vn;
            }
        }

        Vector2 rawOut = new Vector2(
            freeControlPoints[iNext].x - curr.x,
            freeControlPoints[iNext].z - curr.z);
        if (rawOut.sqrMagnitude < 1e-10f)
        {
            rawOut = new Vector2(
                curr.x - freeControlPoints[iPrev].x,
                curr.z - freeControlPoints[iPrev].z);
        }
        if (rawOut.sqrMagnitude < 1e-12f)
            return;

        Vector2 edgeTan = rawOut.normalized;

        if (!orthogonalMidHandleDragsWholeWallRun)
        {
            Vector3 pt = freeControlPoints[index];
            freeControlPoints[index] = new Vector3(pt.x + delta.x, shapeY, pt.z + delta.y);
            return;
        }

        int iLo = index;
        for (int g = 0; g < n; g++)
        {
            int p = (iLo - 1 + n) % n;
            Vector2 seg = new Vector2(
                freeControlPoints[iLo].x - freeControlPoints[p].x,
                freeControlPoints[iLo].z - freeControlPoints[p].z);
            if (!SegmentParallelToEdgeDirectionXZ(seg, edgeTan))
                break;
            iLo = p;
        }

        int iHi = index;
        for (int g = 0; g < n; g++)
        {
            int nx = (iHi + 1) % n;
            Vector2 seg = new Vector2(
                freeControlPoints[nx].x - freeControlPoints[iHi].x,
                freeControlPoints[nx].z - freeControlPoints[iHi].z);
            if (!SegmentParallelToEdgeDirectionXZ(seg, edgeTan))
                break;
            iHi = nx;
        }

        int k = iLo;
        for (int step = 0; step < n; step++)
        {
            Vector3 pt = freeControlPoints[k];
            freeControlPoints[k] = new Vector3(pt.x + delta.x, shapeY, pt.z + delta.y);
            if (k == iHi)
                break;
            k = (k + 1) % n;
        }
    }

    /// <summary>
    /// Quand le coin « vif » tombe dans la branche dégénérée (incoming/outgoing même dominant), l’ancien code
    /// plaçait le sommet à (wx,wz) libre → arêtes non alignées aux axes. On projette sur le coin orthogonal
    /// (intersection de droites XZ parallèles aux axes passant par les voisins) le plus proche du curseur.
    /// </summary>
    static Vector3 NearestAxisAlignedCornerWorldXZ(float wx, float wz, Vector3 prevImm, Vector3 nextImm, float y)
    {
        float bestX = wx;
        float bestZ = wz;
        float bestD = float.MaxValue;
        void Try(float tx, float tz)
        {
            float dx = tx - wx;
            float dz = tz - wz;
            float d = dx * dx + dz * dz;
            if (d < bestD)
            {
                bestD = d;
                bestX = tx;
                bestZ = tz;
            }
        }

        Try(prevImm.x, wz);
        Try(wx, prevImm.z);
        Try(nextImm.x, wz);
        Try(wx, nextImm.z);
        Try(nextImm.x, prevImm.z);
        Try(prevImm.x, nextImm.z);

        return new Vector3(bestX, y, bestZ);
    }

    /// <summary>
    /// Coin orthogonal vif (intérieur ~90° saillant ou ~270° rentrant sur contour CCW) : comme un coin de rectangle,
    /// on met à jour tout le long des deux arêtes (Z constant sur un mur, X constant sur l’autre).
    /// </summary>
    /// <remarks>
    /// Pour un coin rentrant qu’on pousse vers l’extérieur, la cible devient un coin saillant (connexion « externe ») :
    /// les directions d’arête dominantes (horizontal vs vertical) s’inversent. Si on garde l’orientation du début du drag,
    /// les mauvais murs se déplacent et le contour s’effondre ou « autoconnecte ». On repasse donc sur l’orientation
    /// déduite de la position cible dès que le produit vectoriel (rentrant → non rentrant) l’indique.
    /// </remarks>
    void ApplyOrthogonalSharpCornerDrag(int index, Vector3 worldPos, Vector2 segInFull, Vector2 segOutFull)
    {
        int n = freeControlPoints.Count;

        float wx = worldPos.x;
        float wz = worldPos.z;

        int iPrev = (index - 1 + n) % n;
        int iNext = (index + 1) % n;
        Vector3 prevImm = freeControlPoints[iPrev];
        Vector3 nextImm = freeControlPoints[iNext];

        Vector2 dInTarget = new Vector2(wx - prevImm.x, wz - prevImm.z);
        Vector2 dOutTarget = new Vector2(nextImm.x - wx, nextImm.z - wz);

        float crossOld = segInFull.x * segOutFull.y - segInFull.y * segOutFull.x;
        float crossNew = dInTarget.x * dOutTarget.y - dInTarget.y * dOutTarget.x;

        Vector2 segInEff = segInFull;
        Vector2 segOutEff = segOutFull;
        if (crossOld < 0f && crossNew >= -1e-4f &&
            dInTarget.sqrMagnitude > 1e-10f && dOutTarget.sqrMagnitude > 1e-10f)
        {
            segInEff = dInTarget;
            segOutEff = dOutTarget;
        }

        bool incomingHorizontal = Mathf.Abs(segInEff.x) >= Mathf.Abs(segInEff.y);
        bool outgoingHorizontal = Mathf.Abs(segOutEff.x) >= Mathf.Abs(segOutEff.y);

        int prevCorner = FindPrevPolygonCornerBackward(index);
        int nextCorner = FindNextPolygonCornerForward(index);

        if (incomingHorizontal == outgoingHorizontal)
        {
            freeControlPoints[index] = NearestAxisAlignedCornerWorldXZ(wx, wz, prevImm, nextImm, shapeY);
            RedistributeMidsOnRingEdgeForward(prevCorner, index);
            RedistributeMidsOnRingEdgeForward(index, nextCorner);
            return;
        }

        if (incomingHorizontal)
        {
            WalkRingForwardInclusive(prevCorner, index, n, k =>
            {
                Vector3 p = freeControlPoints[k];
                freeControlPoints[k] = new Vector3(p.x, shapeY, wz);
            });
            WalkRingForwardInclusive(index, nextCorner, n, k =>
            {
                Vector3 p = freeControlPoints[k];
                freeControlPoints[k] = new Vector3(wx, shapeY, p.z);
            });
        }
        else
        {
            WalkRingForwardInclusive(prevCorner, index, n, k =>
            {
                Vector3 p = freeControlPoints[k];
                freeControlPoints[k] = new Vector3(wx, shapeY, p.z);
            });
            WalkRingForwardInclusive(index, nextCorner, n, k =>
            {
                Vector3 p = freeControlPoints[k];
                freeControlPoints[k] = new Vector3(p.x, shapeY, wz);
            });
        }

        RedistributeMidsOnRingEdgeForward(prevCorner, index);
        RedistributeMidsOnRingEdgeForward(index, nextCorner);
    }

    /// <summary>
    /// Déplacement orthogonal : CAS 2 mur rigide, CAS 3 paire superposée (u = le long, v = rupture), CAS 1 coin classique.
    /// </summary>
    void ApplyOrthogonalClosedFreeVertexDrag(int index, Vector3 worldPos)
    {
        int n = freeControlPoints.Count;
        if (n < 3)
        {
            freeControlPoints[index] = new Vector3(worldPos.x, shapeY, worldPos.z);
            _freePathWasEdited = true;
            return;
        }

        int iPrev = (index - 1 + n) % n;
        int iNext = (index + 1) % n;
        Vector3 prev = freeControlPoints[iPrev];
        Vector3 curr = freeControlPoints[index];
        Vector3 next = freeControlPoints[iNext];
        Vector2 dInFull = new Vector2(curr.x - prev.x, curr.z - prev.z);
        Vector2 dOutFull = new Vector2(next.x - curr.x, next.z - curr.z);
        const float degenerateEdgeLenSq = 1e-6f;

        // CAS 2 — Mur entier rigide (milieu de mur / « E »)
        if (_orthoDragKind == OrthoDragKind.RigidWallRun && index == _orthoDragStrokeVertexIndex)
        {
            ApplyOrthogonalRigidCollinearChainDrag(index, worldPos);
            FinalizeOrthogonalFreeRingAfterControlEdit();
            return;
        }

        // CAS 3 — Paire interne/externe : même ΔXZ pour A et B seulement si assez proche du coin partenaire B (voir orthogonalStackedPairActivationDistanceXZ).
        if (index == _orthoDragStrokeVertexIndex &&
            _orthoStackPartnerIndex >= 0 &&
            _orthoStackPartnerIndex < n &&
            IsOrthogonalStackedPairWithinActivationDistance(index, _orthoStackPartnerIndex))
        {
            Vector3 refPt = GetControlPointWorld(index);
            Vector2 delta = new Vector2(worldPos.x - refPt.x, worldPos.z - refPt.z);
            if (orthogonalStackedPairNearPartnerOnlyAllowScreenVerticalDrag)
                delta = ProjectOrthogonalStackedPairDeltaToScreenVerticalOnly(delta);

            int ia = index;
            int ib = _orthoStackPartnerIndex;
            Vector3 pa = freeControlPoints[ia];
            Vector3 pb = freeControlPoints[ib];
            freeControlPoints[ia] = new Vector3(pa.x + delta.x, shapeY, pa.z + delta.y);
            freeControlPoints[ib] = new Vector3(pb.x + delta.x, shapeY, pb.z + delta.y);
            FinalizeOrthogonalFreeRingAfterControlEdit();
            return;
        }

        // CAS 1 — Coin classique (et OrthoDragKind.None / secours)
        if (dInFull.sqrMagnitude < degenerateEdgeLenSq || dOutFull.sqrMagnitude < degenerateEdgeLenSq)
        {
            freeControlPoints[index] = new Vector3(worldPos.x, shapeY, worldPos.z);
            FinalizeOrthogonalFreeRingAfterControlEdit();
            return;
        }

        ApplyOrthogonalSharpCornerDrag(index, worldPos, dInFull, dOutFull);
        FinalizeOrthogonalFreeRingAfterControlEdit();
    }

    void RotateFreePolygonAroundCentroid(float deltaRad)
    {
        if (freeControlPoints == null || freeControlPoints.Count == 0)
            return;

        Vector3 c = GetClosedFreeLotCentroidWorld();
        float cos = Mathf.Cos(deltaRad);
        float sin = Mathf.Sin(deltaRad);

        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            float lx = p.x - c.x;
            float lz = p.z - c.z;
            float rx = lx * cos - lz * sin;
            float rz = lx * sin + lz * cos;
            freeControlPoints[i] = new Vector3(c.x + rx, shapeY, c.z + rz);
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
    }

    void RebuildStraightClosedPreviewCacheIfDirty()
    {
        if (!_straightClosedPreviewDirty)
            return;

        _straightClosedPreviewDirty = false;

        List<Vector3> cache = _straightClosedPreviewCache;
        cache.Clear();

        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return;

        for (int i = 0; i < freeControlPoints.Count; i++)
            cache.Add(new Vector3(freeControlPoints[i].x, shapeY, freeControlPoints[i].z));

        // Pas de RemoveTooShortSegments ici : avec minClosedSegmentLength, deux poignées au même XZ
        // donnaient un segment « trop court » et un sommet était supprimé → les points se « connectaient ».
        // Le polygone du mur suit exactement la liste des poignées (même longueur nulle).

        cache.Add(cache[0]);
        // Ne pas appeler EnsureCounterClockwiseXZ sur ce cache : ça peut inverser l’ordre sans toucher
        // freeControlPoints → le mur / les liens ne correspondent plus aux indices des poignées (ex. « point de fin »).
        // L’orientation du contour est déjà assurée par EnsureClosedFreeRingCounterClockwiseXZ dans Finalize / init fusion.
    }

    List<Vector3> BuildFreePreviewPath()
    {
        if (_closedLoop && shapeKind == ShapeKind.Free && _closedFreeOrthogonalPolylineMode)
        {
            RebuildStraightClosedPreviewCacheIfDirty();
            if (_straightClosedPreviewCache.Count >= 3)
                return _straightClosedPreviewCache;
        }

        // Ne pas exiger « !_freePathWasEdited » : dès le premier drag en mode non-orthogonal, l’ancien code mettait
        // <see cref="_mergeFootprintUseExactPolyline"/> à faux et on passait en Catmull-Rom → murs « fondus » alors
        // que les poignées restent un polygone. Tant que le drapeau fusion est actif, le mur doit suivre la polyline.
        if (_mergeFootprintUseExactPolyline && _closedLoop)
        {
            List<Vector3> exact = BuildExactMergedFootprintPolyline();
            if (exact != null && exact.Count >= 3)
                return exact;
        }

        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return null;

        if (preserveInitialFreeDrawnPath && !_freePathWasEdited)
        {
            List<Vector3> rawPreserved = BuildPreservedRawFreePath();
            if (rawPreserved != null && rawPreserved.Count >= 2)
                return rawPreserved;
        }

        return BuildHandleDrivenFreePath();
    }

    List<Vector3> BuildExactMergedFootprintPolyline()
    {
        List<Vector3> ring = null;
        if (_freeRawPath != null && _freeRawPath.Count >= 3)
            ring = _freeRawPath;
        else if (freeControlPoints != null && freeControlPoints.Count >= 3)
            ring = freeControlPoints;

        if (ring == null)
            return null;

        var r = new List<Vector3>(ring.Count + 1);
        for (int i = 0; i < ring.Count; i++)
            r.Add(new Vector3(ring[i].x, shapeY, ring[i].z));

        if (r.Count >= 2 && Vector3.Distance(r[0], r[r.Count - 1]) < 0.001f)
            return r;

        r.Add(r[0]);
        return r;
    }

    List<Vector3> BuildPreservedRawFreePath()
    {
        if (_freeRawPath == null || _freeRawPath.Count < 2)
            return null;

        if (_closedLoop)
        {
            List<Vector3> raw = new List<Vector3>(_freeRawPath);
            if (raw.Count > 1 && Vector3.Distance(raw[0], raw[raw.Count - 1]) < 0.001f)
                raw.RemoveAt(raw.Count - 1);

            raw = RemoveTooShortSegmentsClosed(raw, Mathf.Min(minClosedSegmentLength, Mathf.Max(0.02f, rawFreeMinPointSpacing)));

            List<Vector3> validated = ValidateClosedCandidate(raw);
            if (validated != null)
                return validated;

            if (useSafeClosedFallback)
                return BuildHandleDrivenFreePath();

            return null;
        }

        return SimplifyOpenByMinSpacing(_freeRawPath, rawFreeMinPointSpacing);
    }

    List<Vector3> BuildHandleDrivenFreePath()
    {
        if (_closedLoop)
            return BuildHandleDrivenClosedFreePath();

        if (freeControlPoints == null || freeControlPoints.Count < 2)
            return null;

        if (freeControlPoints.Count == 2)
            return new List<Vector3>(freeControlPoints);

        int openTargetCount = Mathf.Max(freeWallResolution, freeControlPoints.Count * 10);
        return BuildOpenCatmullRomThroughControls(freeControlPoints, openTargetCount);
    }

    List<Vector3> BuildHandleDrivenClosedFreePath()
    {
        List<Vector3> work = new List<Vector3>(freeControlPoints);
        work = RemoveTooShortSegmentsClosed(work, minClosedSegmentLength);

        if (work.Count < 3)
            return BuildSafeClosedFallbackFromControls();

        int targetCount = Mathf.Max(work.Count * 10, closedFreeWallResolution);
        List<Vector3> denseClosed = BuildClosedCatmullRomThroughControls(work, targetCount);
        denseClosed = RemoveTooShortSegmentsClosed(denseClosed, minClosedSegmentLength);

        List<Vector3> validated = ValidateClosedCandidate(denseClosed);
        if (validated != null)
            return validated;

        if (useSafeClosedFallback)
            return BuildSafeClosedFallbackFromControls();

        return null;
    }

    List<Vector3> BuildSafeClosedFallbackFromControls()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return null;

        List<Vector3> raw = new List<Vector3>(freeControlPoints);
        raw = RemoveTooShortSegmentsClosed(raw, minClosedSegmentLength);

        List<Vector3> validated = ValidateClosedCandidate(raw);
        if (validated != null)
            return validated;

        return BuildConvexHullClosedFallbackFromControls();
    }

    List<Vector3> BuildConvexHullClosedFallbackFromControls()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return null;

        List<Vector2> hull = ComputeConvexHullXZ(freeControlPoints);
        if (hull == null || hull.Count < 3)
            return null;

        List<Vector3> pts = new List<Vector3>(hull.Count + 1);
        for (int i = 0; i < hull.Count; i++)
            pts.Add(new Vector3(hull[i].x, shapeY, hull[i].y));

        pts.Add(pts[0]);
        EnsureCounterClockwiseXZ(pts);
        return pts;
    }

    List<Vector3> ValidateClosedCandidate(List<Vector3> candidate)
    {
        if (candidate == null || candidate.Count < 3)
            return null;

        List<Vector3> work = new List<Vector3>(candidate);

        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        work = RemoveTooShortSegmentsClosed(work, minClosedSegmentLength);
        if (work.Count < 3)
            return null;

        if (ComputeAbsoluteSignedAreaXZ(work) < 0.0025f)
            return null;

        if (HasSelfIntersectionXZ(work))
            return null;

        work.Add(work[0]);
        EnsureCounterClockwiseXZ(work);
        return work;
    }

    static List<Vector3> BuildOpenCatmullRomThroughControls(List<Vector3> controls, int targetCount)
    {
        if (controls == null || controls.Count < 2)
            return new List<Vector3>();

        if (controls.Count == 2)
            return new List<Vector3>(controls);

        targetCount = Mathf.Max(targetCount, controls.Count * 4);

        float[] segLengths = new float[controls.Count - 1];
        float totalLength = 0f;
        for (int i = 0; i < controls.Count - 1; i++)
        {
            segLengths[i] = Vector3.Distance(controls[i], controls[i + 1]);
            totalLength += segLengths[i];
        }

        if (totalLength < 0.0001f)
            return new List<Vector3>(controls);

        List<Vector3> result = new List<Vector3>(targetCount + controls.Count);

        for (int i = 0; i < controls.Count - 1; i++)
        {
            Vector3 p1 = controls[i];
            Vector3 p2 = controls[i + 1];
            Vector3 p0 = i == 0 ? (p1 + (p1 - p2)) : controls[i - 1];
            Vector3 p3 = i == controls.Count - 2 ? (p2 + (p2 - p1)) : controls[i + 2];

            int steps = Mathf.Max(6, Mathf.RoundToInt((segLengths[i] / totalLength) * targetCount));

            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                Vector3 pt = CatmullRom(p0, p1, p2, p3, t);
                pt.y = p1.y;

                if (result.Count == 0 || Vector3.Distance(result[result.Count - 1], pt) > 0.0001f)
                    result.Add(pt);
            }

            if (result.Count == 0 || Vector3.Distance(result[result.Count - 1], p2) > 0.0001f)
                result.Add(p2);
        }

        return result;
    }

    static List<Vector3> BuildClosedCatmullRomThroughControls(List<Vector3> controls, int targetCount)
    {
        if (controls == null || controls.Count < 3)
            return controls == null ? new List<Vector3>() : new List<Vector3>(controls);

        List<Vector3> work = new List<Vector3>(controls);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        int n = work.Count;
        if (n < 3)
            return new List<Vector3>(work);

        targetCount = Mathf.Max(targetCount, n * 6);

        float[] segLengths = new float[n];
        float totalLength = 0f;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            segLengths[i] = Vector3.Distance(work[i], work[next]);
            totalLength += segLengths[i];
        }

        if (totalLength < 0.0001f)
            return new List<Vector3>(work);

        List<Vector3> result = new List<Vector3>(targetCount + n + 1);

        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = work[(i - 1 + n) % n];
            Vector3 p1 = work[i];
            Vector3 p2 = work[(i + 1) % n];
            Vector3 p3 = work[(i + 2) % n];

            int steps = Mathf.Max(6, Mathf.RoundToInt((segLengths[i] / totalLength) * targetCount));

            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                Vector3 pt = CatmullRom(p0, p1, p2, p3, t);
                pt.y = p1.y;

                if (result.Count == 0 || Vector3.Distance(result[result.Count - 1], pt) > 0.0001f)
                    result.Add(pt);
            }
        }

        if (result.Count > 0)
        {
            if (Vector3.Distance(result[0], result[result.Count - 1]) > 0.0001f)
                result.Add(result[0]);
        }

        return result;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    static List<Vector3> Chaikin(List<Vector3> pts, int iterations, bool closed)
    {
        if (pts == null || pts.Count < 2)
            return pts == null ? new List<Vector3>() : new List<Vector3>(pts);

        List<Vector3> work = new List<Vector3>(pts);

        if (closed && work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        for (int it = 0; it < iterations; it++)
        {
            List<Vector3> res = new List<Vector3>(work.Count * 2);
            int n = work.Count;

            if (closed)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = work[i];
                    Vector3 b = work[(i + 1) % n];
                    res.Add(Vector3.Lerp(a, b, 0.25f));
                    res.Add(Vector3.Lerp(a, b, 0.75f));
                }
            }
            else
            {
                res.Add(work[0]);

                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 a = work[i];
                    Vector3 b = work[i + 1];
                    res.Add(Vector3.Lerp(a, b, 0.25f));
                    res.Add(Vector3.Lerp(a, b, 0.75f));
                }

                res.Add(work[n - 1]);
            }

            work = res;
        }

        return work;
    }

    static List<Vector3> ResampleOpenByCount(List<Vector3> pts, int count)
    {
        if (pts == null || pts.Count == 0)
            return new List<Vector3>();

        if (pts.Count == 1)
            return new List<Vector3>(pts);

        count = Mathf.Max(2, count);

        float[] dist = new float[pts.Count];
        dist[0] = 0f;

        for (int i = 1; i < pts.Count; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);

        float total = dist[pts.Count - 1];
        if (total < 1e-6f)
            return new List<Vector3>(pts);

        List<Vector3> res = new List<Vector3>(count);

        for (int k = 0; k < count; k++)
        {
            float t = (k / (float)(count - 1)) * total;
            int i = 1;

            while (i < dist.Length && dist[i] < t)
                i++;

            i = Mathf.Clamp(i, 1, dist.Length - 1);
            float segT = Mathf.InverseLerp(dist[i - 1], dist[i], t);
            res.Add(Vector3.Lerp(pts[i - 1], pts[i], segT));
        }

        return res;
    }

    static List<Vector3> ResampleClosedByCount(List<Vector3> pts, int count)
    {
        if (pts == null || pts.Count == 0)
            return new List<Vector3>();

        if (pts.Count == 1)
            return new List<Vector3>(pts);

        count = Mathf.Max(3, count);

        List<Vector3> work = new List<Vector3>(pts);
        if (Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        int n = work.Count;
        if (n < 2)
            return new List<Vector3>(work);

        float[] dist = new float[n + 1];
        dist[0] = 0f;

        for (int i = 1; i < n; i++)
            dist[i] = dist[i - 1] + Vector3.Distance(work[i - 1], work[i]);

        dist[n] = dist[n - 1] + Vector3.Distance(work[n - 1], work[0]);

        float total = dist[n];
        if (total < 1e-6f)
            return new List<Vector3>(work);

        List<Vector3> res = new List<Vector3>(count);

        for (int k = 0; k < count; k++)
        {
            float t = (k / (float)count) * total;
            int seg = 1;

            while (seg < dist.Length && dist[seg] < t)
                seg++;

            seg = Mathf.Clamp(seg, 1, dist.Length - 1);

            int aIndex = seg - 1;
            int bIndex = seg % n;

            float segT = Mathf.InverseLerp(dist[seg - 1], dist[seg], t);
            res.Add(Vector3.Lerp(work[aIndex], work[bIndex], segT));
        }

        return res;
    }

    /// <summary>Comme <see cref="RemoveTooShortSegmentsClosed"/> mais sans allouer une nouvelle liste.</summary>
    static void RemoveTooShortSegmentsClosedInPlace(List<Vector3> work, float minLen)
    {
        if (work == null || work.Count == 0)
            return;

        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 3)
            return;

        bool changed = true;
        int guard = 0;

        while (changed && work.Count >= 3 && guard < 64)
        {
            changed = false;
            guard++;

            for (int i = work.Count - 1; i >= 0; i--)
            {
                int next = (i + 1) % work.Count;
                if (Vector3.Distance(work[i], work[next]) < minLen)
                {
                    work.RemoveAt(next);
                    changed = true;
                    if (work.Count < 3)
                        break;
                }
            }
        }
    }

    static List<Vector3> RemoveTooShortSegmentsClosed(List<Vector3> pts, float minLen)
    {
        List<Vector3> work = new List<Vector3>();
        if (pts == null || pts.Count == 0)
            return work;

        work.AddRange(pts);

        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        if (work.Count < 3)
            return work;

        bool changed = true;
        int guard = 0;

        while (changed && work.Count >= 3 && guard < 64)
        {
            changed = false;
            guard++;

            for (int i = work.Count - 1; i >= 0; i--)
            {
                int next = (i + 1) % work.Count;
                if (Vector3.Distance(work[i], work[next]) < minLen)
                {
                    work.RemoveAt(next);
                    changed = true;
                    if (work.Count < 3)
                        break;
                }
            }
        }

        return work;
    }

    static List<Vector3> SimplifyOpenByMinSpacing(List<Vector3> pts, float minSpacing)
    {
        List<Vector3> res = new List<Vector3>();
        if (pts == null || pts.Count == 0)
            return res;

        minSpacing = Mathf.Max(0.001f, minSpacing);

        res.Add(pts[0]);
        Vector3 last = pts[0];

        for (int i = 1; i < pts.Count - 1; i++)
        {
            if (Vector3.Distance(last, pts[i]) >= minSpacing)
            {
                res.Add(pts[i]);
                last = pts[i];
            }
        }

        if (pts.Count > 1)
        {
            Vector3 end = pts[pts.Count - 1];
            if (res.Count == 0 || Vector3.Distance(res[res.Count - 1], end) > 0.0001f)
                res.Add(end);
        }

        return res;
    }

    static float ComputeAbsoluteSignedAreaXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 3)
            return 0f;

        int count = pts.Count;
        if (Vector3.Distance(pts[0], pts[count - 1]) < 0.0001f)
            count--;

        if (count < 3)
            return 0f;

        float area = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % count];
            area += (a.x * b.z - b.x * a.z);
        }

        return Mathf.Abs(area) * 0.5f;
    }

    /// <summary>
    /// Arêtes i et j d’un n-gone fermé (sans point de fermeture dupliqué) : partagent-elles un sommet ?
    /// </summary>
    static bool ClosedPolygonEdgesAdjacent(int n, int edgeI, int edgeJ)
    {
        if (edgeI == edgeJ)
            return true;
        int nextI = (edgeI + 1) % n;
        int nextJ = (edgeJ + 1) % n;
        return nextI == edgeJ || nextJ == edgeI;
    }

    static bool HasSelfIntersectionXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4)
            return false;

        List<Vector3> work = new List<Vector3>(pts);
        if (work.Count > 1 && Vector3.Distance(work[0], work[work.Count - 1]) < 0.0001f)
            work.RemoveAt(work.Count - 1);

        int n = work.Count;
        if (n < 4)
            return false;

        for (int i = 0; i < n; i++)
        {
            int nextI = (i + 1) % n;
            Vector3 dI = work[nextI] - work[i];
            dI.y = 0f;
            if (dI.sqrMagnitude < 1e-14f)
                continue;

            for (int j = i + 1; j < n; j++)
            {
                int nextJ = (j + 1) % n;

                if (ClosedPolygonEdgesAdjacent(n, i, j))
                    continue;

                Vector3 dJ = work[nextJ] - work[j];
                dJ.y = 0f;
                if (dJ.sqrMagnitude < 1e-14f)
                    continue;

                if (SegmentsIntersectXZ(work[i], work[nextI], work[j], work[nextJ]))
                    return true;
            }
        }

        return false;
    }

    static bool PointOnClosedSegmentXZ(Vector3 p, Vector3 a, Vector3 b, float eps)
    {
        float minx = Mathf.Min(a.x, b.x) - eps;
        float maxx = Mathf.Max(a.x, b.x) + eps;
        float minz = Mathf.Min(a.z, b.z) - eps;
        float maxz = Mathf.Max(a.z, b.z) + eps;
        if (p.x < minx || p.x > maxx || p.z < minz || p.z > maxz)
            return false;

        float dx = b.x - a.x;
        float dz = b.z - a.z;
        float cross = dx * (p.z - a.z) - dz * (p.x - a.x);
        return Mathf.Abs(cross) <= eps * (Mathf.Abs(dx) + Mathf.Abs(dz) + 1f);
    }

    /// <summary>
    /// Croisement propre + chevauchements colinéaires + sommet sur l’autre arête (évite les « encoches » validées par l’ancien test).
    /// </summary>
    static bool SegmentsIntersectXZ(Vector3 a1, Vector3 a2, Vector3 b1, Vector3 b2)
    {
        const float eps = 2e-5f;

        float o1 = OrientationXZ(a1, a2, b1);
        float o2 = OrientationXZ(a1, a2, b2);
        float o3 = OrientationXZ(b1, b2, a1);
        float o4 = OrientationXZ(b1, b2, a2);

        if (o1 * o2 < -eps * eps && o3 * o4 < -eps * eps)
            return true;

        if (Mathf.Abs(o1) <= eps && PointOnClosedSegmentXZ(b1, a1, a2, eps))
            return true;
        if (Mathf.Abs(o2) <= eps && PointOnClosedSegmentXZ(b2, a1, a2, eps))
            return true;
        if (Mathf.Abs(o3) <= eps && PointOnClosedSegmentXZ(a1, b1, b2, eps))
            return true;
        if (Mathf.Abs(o4) <= eps && PointOnClosedSegmentXZ(a2, b1, b2, eps))
            return true;

        return false;
    }

    static float OrientationXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
    }

    bool IsMostlyStraightOpen(List<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return true;

        float pathLen = 0f;
        for (int i = 0; i < points.Count - 1; i++)
            pathLen += Vector3.Distance(points[i], points[i + 1]);

        float chord = Vector3.Distance(points[0], points[points.Count - 1]);
        if (chord < 0.0001f)
            return false;

        float arcRatio = pathLen / chord;

        float turnSum = 0f;
        int turnCount = 0;

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 a = (points[i] - points[i - 1]).normalized;
            Vector3 b = (points[i + 1] - points[i]).normalized;

            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f)
                continue;

            turnSum += Vector3.Angle(a, b);
            turnCount++;
        }

        float avgTurn = turnCount > 0 ? turnSum / turnCount : 0f;

        return arcRatio <= mostlyStraightArcRatioThreshold && avgTurn <= mostlyStraightAverageTurnThreshold;
    }

    /// <summary>
    /// Verrou final avant SetPath : un mur intérieur ouvert à 2 points ne doit jamais être appliqué en segment nul.
    /// Si un système annexe (snap/fusion/reprojection) produit un segment trop court, on restaure le dernier segment valide.
    /// </summary>
    void EnforceOpenInteriorTwoPointHardFloorBeforeApply(ref List<Vector3> path)
    {
        if (!ShouldApplyInteriorLotConstraint() ||
            shapeKind != ShapeKind.Free ||
            _closedLoop ||
            freeControlPoints == null ||
            freeControlPoints.Count != 2)
            return;

        // Rejoue la contrainte lot + longueur min.
        ClampOpenFreeVerticesToInteriorLotConstraint();
        path = BuildFreePreviewPath();

        float minSeg = GetMinOpenInteriorWallSegmentLengthMeters(wall);
        float d = (path != null && path.Count >= 2) ? Vector3.Distance(path[0], path[path.Count - 1]) : 0f;
        if (d >= minSeg - 1e-4f)
        {
            _hasLastValidOpenInteriorTwoPointSegment = true;
            _lastValidOpenInteriorTwoPointA = freeControlPoints[0];
            _lastValidOpenInteriorTwoPointB = freeControlPoints[1];
            return;
        }

        if (!_hasLastValidOpenInteriorTwoPointSegment)
            return;

        // Fallback robuste : ne jamais remplacer le mur par un point.
        freeControlPoints[0] = new Vector3(_lastValidOpenInteriorTwoPointA.x, shapeY, _lastValidOpenInteriorTwoPointA.z);
        freeControlPoints[1] = new Vector3(_lastValidOpenInteriorTwoPointB.x, shapeY, _lastValidOpenInteriorTwoPointB.z);
        if (_freeRawPath.Count >= 2)
        {
            _freeRawPath[0] = freeControlPoints[0];
            _freeRawPath[_freeRawPath.Count - 1] = freeControlPoints[1];
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
        path = new List<Vector3>(2) { freeControlPoints[0], freeControlPoints[1] };
    }

    public void ApplyToWall()
    {
        if (wall == null)
            return;

        List<Vector3> path = null;

        switch (shapeKind)
        {
            case ShapeKind.Ellipse:
                path = BuildEllipsePath(ellipseWallResolution);
                break;

            case ShapeKind.Rectangle:
                path = BuildRectanglePath();
                break;

            case ShapeKind.Triangle:
                path = BuildTrianglePath();
                break;

            case ShapeKind.OpenArc:
                path = BuildOpenArcPath(openArcWallResolution);
                break;

            default:
                path = BuildFreePreviewPath();
                break;
        }

        EnforceOpenInteriorTwoPointHardFloorBeforeApply(ref path);

        if (path != null && path.Count >= 2)
        {
            wall.closedLoop = _closedLoop;
            wall.SetPath(path);
        }

        HouseParquetFloor floor = wall.GetComponent<HouseParquetFloor>();
        if (floor != null)
        {
            if (!_closedLoop)
                floor.ClearFloor();
            else if (shapeKind == ShapeKind.Rectangle)
                floor.ApplyOrRefresh(wall, this);
            else if (shapeKind == ShapeKind.Free)
                floor.ApplyOrRefreshClosedFreeLoop(wall, this);
            else if (shapeKind == ShapeKind.Ellipse || shapeKind == ShapeKind.Triangle)
                floor.ApplyOrRefreshFromClosedPreviewPath(wall, this);
            else
                floor.ClearFloor();
        }
    }

    void ComputeBounds(List<Vector3> points)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        minZ = float.MaxValue;
        maxZ = float.MinValue;

        for (int i = 0; i < points.Count; i++)
        {
            minX = Mathf.Min(minX, points[i].x);
            maxX = Mathf.Max(maxX, points[i].x);
            minZ = Mathf.Min(minZ, points[i].z);
            maxZ = Mathf.Max(maxZ, points[i].z);
        }
    }

    Vector3 GetBoundsCenter()
    {
        return new Vector3((minX + maxX) * 0.5f, shapeY, (minZ + maxZ) * 0.5f);
    }

    bool IsClosed(List<Vector3> points)
    {
        if (points == null || points.Count < 3)
            return false;

        // Keep closure detection consistent with WallObject.SetPath.
        return Vector3.Distance(points[0], points[points.Count - 1]) < 0.001f;
    }

    float ComputePerimeter(List<Vector3> points, bool closed)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float len = 0f;

        for (int i = 0; i < points.Count - 1; i++)
            len += Vector3.Distance(points[i], points[i + 1]);

        if (closed)
            len += Vector3.Distance(points[points.Count - 1], points[0]);

        return len;
    }

    List<Vector2> ComputeConvexHullXZ(List<Vector3> pts3)
    {
        List<Vector2> pts = new List<Vector2>(pts3.Count);
        for (int i = 0; i < pts3.Count; i++)
            pts.Add(new Vector2(pts3[i].x, pts3[i].z));

        if (pts.Count <= 3)
            return new List<Vector2>(pts);

        pts.Sort(delegate (Vector2 p1, Vector2 p2)
        {
            int cmp = p1.x.CompareTo(p2.x);
            return cmp == 0 ? p1.y.CompareTo(p2.y) : cmp;
        });

        List<Vector2> lower = new List<Vector2>();
        foreach (Vector2 p in pts)
        {
            while (lower.Count >= 2 &&
                   Cross(lower[lower.Count - 1] - lower[lower.Count - 2], p - lower[lower.Count - 1]) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(p);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            Vector2 p = pts[i];
            while (upper.Count >= 2 &&
                   Cross(upper[upper.Count - 1] - upper[upper.Count - 2], p - upper[upper.Count - 1]) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    void EnsureCounterClockwiseXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 4)
            return;

        int count = pts.Count;
        bool duplicatedClose = Vector3.Distance(pts[0], pts[count - 1]) < 0.0001f;
        int effectiveCount = duplicatedClose ? count - 1 : count;

        float area = 0f;
        for (int i = 0; i < effectiveCount; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % effectiveCount];
            area += (a.x * b.z - b.x * a.z);
        }

        if (area < 0f)
        {
            if (duplicatedClose)
            {
                pts.RemoveAt(pts.Count - 1);
                pts.Reverse();
                pts.Add(pts[0]);
            }
            else
            {
                pts.Reverse();
            }
        }
    }

    /// <summary>
    /// Contour fusionné orthogonal : garde le même ordre que le chemin mur / preview
    /// (<see cref="RebuildStraightClosedPreviewCacheIfDirty"/> ne doit pas renverser sans mettre à jour les poignées).
    /// </summary>
    void EnsureClosedFreeRingCounterClockwiseXZ()
    {
        if (freeControlPoints == null || freeControlPoints.Count < 3)
            return;

        if (ComputeSignedAreaXZClosedRingNoDup(freeControlPoints) >= 0f)
            return;

        freeControlPoints.Reverse();
        if (_freeRawPath != null && _freeRawPath.Count == freeControlPoints.Count)
            _freeRawPath.Reverse();
    }

    static float ComputeSignedAreaXZClosedRingNoDup(List<Vector3> ring)
    {
        int n = ring.Count;
        if (n < 3)
            return 0f;

        float a = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = ring[i];
            Vector3 p1 = ring[(i + 1) % n];
            a += p0.x * p1.z - p1.x * p0.z;
        }

        return a * 0.5f;
    }

    bool ShouldApplyInteriorLotConstraint()
    {
        return shapeKind == ShapeKind.Free &&
               !_closedLoop &&
               interiorWallsStayInsideLot != null;
    }

    /// <summary>
    /// Après projection / clip, impose <paramref name="minSeg"/> ; si les deux points sont encore trop proches
    /// (ex. même projection sur le bord), étend autour du milieu selon la direction du segment utilisateur.
    /// </summary>
    void RefineOpenInteriorSegmentToMinLengthXZ(
        ref Vector3 ca,
        ref Vector3 cb,
        Vector2 qaHint,
        Vector2 qbHint,
        Vector2 userA2,
        Vector2 userB2,
        float inset,
        float minSeg)
    {
        TryEnsureOpenInteriorWallTwoPointMinLengthXZ(ref ca, ref cb, minSeg, _scratchFootprintRing, inset);

        float d = Vector2.Distance(new Vector2(ca.x, ca.z), new Vector2(cb.x, cb.z));
        if (d < minSeg - 1e-3f)
        {
            Vector2 mid = (qaHint + qbHint) * 0.5f;
            Vector2 dir = userB2 - userA2;
            if (dir.sqrMagnitude < 1e-10f)
                dir = Vector2.right;
            else
                dir = dir.normalized;

            float half = minSeg * 0.5f;
            Vector2 na = mid - dir * half;
            Vector2 nb = mid + dir * half;
            if (PointInsideLotConstraintXZ(na, _scratchFootprintRing, inset) &&
                PointInsideLotConstraintXZ(nb, _scratchFootprintRing, inset))
            {
                ca = new Vector3(na.x, shapeY, na.y);
                cb = new Vector3(nb.x, shapeY, nb.y);
            }
            else
            {
                Vector2 perp = new Vector2(-dir.y, dir.x);
                na = mid - perp * half;
                nb = mid + perp * half;
                if (PointInsideLotConstraintXZ(na, _scratchFootprintRing, inset) &&
                    PointInsideLotConstraintXZ(nb, _scratchFootprintRing, inset))
                {
                    ca = new Vector3(na.x, shapeY, na.y);
                    cb = new Vector3(nb.x, shapeY, nb.y);
                }
            }
        }
    }

    /// <summary>
    /// Quand le segment ne peut pas être clipé dans le polygone du lot, évite le flux « un point par extrémité »
    /// qui fusionne les deux sommets ; on projette puis on impose <paramref name="minSeg"/> (ex. 0,5 m).
    /// </summary>
    void ApplyOpenInteriorTwoPointWhenClipFails(Vector3 worldA, Vector3 worldB, float inset, float minSeg)
    {
        if (freeControlPoints == null || freeControlPoints.Count != 2)
            return;

        Vector2 a2 = new Vector2(worldA.x, worldA.z);
        Vector2 b2 = new Vector2(worldB.x, worldB.z);
        Vector2 qa = FindClosestInteriorPointApproxXZ(a2, _scratchFootprintRing, inset);
        Vector2 qb = FindClosestInteriorPointApproxXZ(b2, _scratchFootprintRing, inset);
        Vector3 ca = new Vector3(qa.x, shapeY, qa.y);
        Vector3 cb = new Vector3(qb.x, shapeY, qb.y);
        RefineOpenInteriorSegmentToMinLengthXZ(ref ca, ref cb, qa, qb, a2, b2, inset, minSeg);

        freeControlPoints[0] = new Vector3(ca.x, shapeY, ca.z);
        freeControlPoints[1] = new Vector3(cb.x, shapeY, cb.z);
        if (_freeRawPath.Count >= 2)
        {
            _freeRawPath[0] = new Vector3(ca.x, shapeY, ca.z);
            _freeRawPath[_freeRawPath.Count - 1] = new Vector3(cb.x, shapeY, cb.z);
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
    }

    void ClampOpenFreeVerticesToInteriorLotConstraint(bool applyPeerSeparation = true)
    {
        if (!ShouldApplyInteriorLotConstraint() || freeControlPoints == null)
            return;

        WallEditShape lot = interiorWallsStayInsideLot;
        if (lot == null)
            return;

        _scratchFootprintRing.Clear();
        if (!lot.TryGetClosedLotFootprintRingXZ(_scratchFootprintRing) || _scratchFootprintRing.Count < 3)
            return;

        float inset = ClampInsetToFeasibleRingXZ(
            _scratchFootprintRing,
            ComputeOpenInteriorWallFootprintInsetMeters(lot.wall != null ? lot.wall.thickness : 0.25f, wall != null ? wall.thickness : 0.25f));

        float minSeg = GetMinOpenInteriorWallSegmentLengthMeters(wall);

        if (freeControlPoints.Count == 2)
        {
            Vector3 a = freeControlPoints[0];
            Vector3 b = freeControlPoints[1];
            if (!TryClipOpenWorldSegmentToLotRingXZ(a, b, _scratchFootprintRing, out Vector3 ca, out Vector3 cb, inset, minSeg) &&
                !TryClipOpenWorldSegmentToLotRingXZ(a, b, _scratchFootprintRing, out ca, out cb, inset, 0f))
            {
                ApplyOpenInteriorTwoPointWhenClipFails(a, b, inset, minSeg);
                return;
            }
            else
            {
                Vector2 a2 = new Vector2(ca.x, ca.z);
                Vector2 b2 = new Vector2(cb.x, cb.z);
                if (applyPeerSeparation)
                    TryShrinkOpenInteriorTwoPointSegmentAgainstPeerWalls(ref a2, ref b2, minSeg);

                if (TryClipOpenWorldSegmentToLotRingXZ(
                        new Vector3(a2.x, shapeY, a2.y),
                        new Vector3(b2.x, shapeY, b2.y),
                        _scratchFootprintRing,
                        out Vector3 ca2,
                        out Vector3 cb2,
                        inset,
                        minSeg))
                {
                    ca = ca2;
                    cb = cb2;
                }
                else if (TryClipOpenWorldSegmentToLotRingXZ(
                             new Vector3(a2.x, shapeY, a2.y),
                             new Vector3(b2.x, shapeY, b2.y),
                             _scratchFootprintRing,
                             out ca2,
                             out cb2,
                             inset,
                             0f))
                {
                    ca = ca2;
                    cb = cb2;
                }
                else
                {
                    ca = new Vector3(a2.x, shapeY, a2.y);
                    cb = new Vector3(b2.x, shapeY, b2.y);
                    Vector2 qa = FindClosestInteriorPointApproxXZ(new Vector2(ca.x, ca.z), _scratchFootprintRing, inset);
                    Vector2 qb = FindClosestInteriorPointApproxXZ(new Vector2(cb.x, cb.z), _scratchFootprintRing, inset);
                    ca = new Vector3(qa.x, shapeY, qa.y);
                    cb = new Vector3(qb.x, shapeY, qb.y);
                }

                Vector2 qaRef = new Vector2(ca.x, ca.z);
                Vector2 qbRef = new Vector2(cb.x, cb.z);
                RefineOpenInteriorSegmentToMinLengthXZ(ref ca, ref cb, qaRef, qbRef, a2, b2, inset, minSeg);

                freeControlPoints[0] = new Vector3(ca.x, shapeY, ca.z);
                freeControlPoints[1] = new Vector3(cb.x, shapeY, cb.z);

                if (_freeRawPath.Count >= 2)
                {
                    _freeRawPath[0] = new Vector3(ca.x, shapeY, ca.z);
                    _freeRawPath[_freeRawPath.Count - 1] = new Vector3(cb.x, shapeY, cb.z);
                }

                _freePathWasEdited = true;
                InvalidateStraightClosedPreviewCache();
                return;
            }
        }

        for (int i = 0; i < freeControlPoints.Count; i++)
        {
            Vector3 p = freeControlPoints[i];
            Vector2 xz = FindClosestInteriorPointApproxXZ(new Vector2(p.x, p.z), _scratchFootprintRing, inset);
            freeControlPoints[i] = new Vector3(xz.x, shapeY, xz.y);
        }

        for (int i = 0; i < _freeRawPath.Count; i++)
        {
            Vector3 p = _freeRawPath[i];
            Vector2 xz = FindClosestInteriorPointApproxXZ(new Vector2(p.x, p.z), _scratchFootprintRing, inset);
            _freeRawPath[i] = new Vector3(xz.x, shapeY, xz.y);
        }

        _freePathWasEdited = true;
        InvalidateStraightClosedPreviewCache();
    }

    /// <summary>
    /// Après init / snap : ramène les sommets dans le lot (ex. grille qui dépasse).
    /// </summary>
    /// <param name="applyPeerSeparation">
    /// Si vrai (défaut), raccourcit le segment ouvert à 2 points pour éviter le chevauchement avec d’autres murs intérieurs.
    /// À désactiver lors d’une translation rigide avec le lot (sinon réouverture d’un vide le long du mur périphérique).
    /// </param>
    public void ClampInteriorWallToLotFootprintIfConfigured(bool applyPeerSeparation = true)
    {
        ClampOpenFreeVerticesToInteriorLotConstraint(applyPeerSeparation);
    }

    void GatherOtherOpenInteriorWallsOnSameLot(WallEditShape lotEdit)
    {
        _scratchPeerInteriorWalls.Clear();
        if (lotEdit == null)
            return;

        if (s_cachedBuildControllerForInteriorMove == null)
            s_cachedBuildControllerForInteriorMove = FindFirstObjectByType<WallBuildController>();

        if (s_cachedBuildControllerForInteriorMove != null)
        {
            IReadOnlyList<WallObject> walls = s_cachedBuildControllerForInteriorMove.Walls;
            for (int i = 0; i < walls.Count; i++)
            {
                WallObject wo = walls[i];
                if (wo == null)
                    continue;

                WallEditShape e = wo.GetComponent<WallEditShape>();
                if (e == null || e == this)
                    continue;

                if (e.interiorWallsStayInsideLot != lotEdit)
                    continue;

                if (e.shapeKind != ShapeKind.Free || e.IsClosedLoopPath)
                    continue;

                if (e.freeControlPoints == null || e.freeControlPoints.Count != 2)
                    continue;

                _scratchPeerInteriorWalls.Add(e);
            }

            return;
        }

        WallEditShape[] all = FindObjectsByType<WallEditShape>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            WallEditShape e = all[i];
            if (e == null || e == this)
                continue;

            if (e.interiorWallsStayInsideLot != lotEdit)
                continue;

            if (e.shapeKind != ShapeKind.Free || e.IsClosedLoopPath)
                continue;

            if (e.freeControlPoints == null || e.freeControlPoints.Count != 2)
                continue;

            _scratchPeerInteriorWalls.Add(e);
        }
    }

    /// <summary>
    /// Évite que le volume (faces latérales) de ce mur coupe d’autres murs intérieurs du même lot :
    /// distance des axes &gt;= somme des demi-épaisseurs. Ne réduit pas en dessous de <paramref name="minSegmentLen"/>.
    /// </summary>
    void TryShrinkOpenInteriorTwoPointSegmentAgainstPeerWalls(ref Vector2 a, ref Vector2 b, float minSegmentLen)
    {
        WallEditShape lot = interiorWallsStayInsideLot;
        if (lot == null)
            return;

        Vector2 a0 = a;
        Vector2 b0 = b;
        float fullLen = Vector2.Distance(a, b);
        if (fullLen < minSegmentLen + 1e-5f)
            return;

        GatherOtherOpenInteriorWallsOnSameLot(lot);
        if (_scratchPeerInteriorWalls.Count == 0)
            return;

        float halfSelf = wall != null ? wall.thickness * 0.5f : 0.125f;
        const float sepEps = 0.0025f;
        // Au coin (segments ~perpendiculaires), la distance d’axes peut être 0 : exiger halfSelf+ho créait un vide visible.
        const float parallelPeerDotThreshold = 0.92f;

        bool SegmentClearOfPeers(Vector2 p0, Vector2 p1)
        {
            if ((p1 - p0).sqrMagnitude < minSegmentLen * minSegmentLen * 0.999f)
                return false;

            Vector2 eSelf = p1 - p0;
            float lenSq = eSelf.sqrMagnitude;
            if (lenSq < 1e-10f)
            {
                for (int i = 0; i < _scratchPeerInteriorWalls.Count; i++)
                {
                    WallEditShape peer = _scratchPeerInteriorWalls[i];
                    if (peer.freeControlPoints == null || peer.freeControlPoints.Count != 2)
                        continue;

                    Vector2 o0 = new Vector2(peer.freeControlPoints[0].x, peer.freeControlPoints[0].z);
                    Vector2 o1 = new Vector2(peer.freeControlPoints[1].x, peer.freeControlPoints[1].z);
                    float ho = peer.wall != null ? peer.wall.thickness * 0.5f : 0.125f;
                    float d0 = DistancePointToSegmentXZ(p0, o0, o1);
                    if (d0 < halfSelf + ho - sepEps)
                        return false;
                }

                return true;
            }

            eSelf /= Mathf.Sqrt(lenSq);

            for (int i = 0; i < _scratchPeerInteriorWalls.Count; i++)
            {
                WallEditShape peer = _scratchPeerInteriorWalls[i];
                if (peer.freeControlPoints == null || peer.freeControlPoints.Count != 2)
                    continue;

                Vector2 o0 = new Vector2(peer.freeControlPoints[0].x, peer.freeControlPoints[0].z);
                Vector2 o1 = new Vector2(peer.freeControlPoints[1].x, peer.freeControlPoints[1].z);
                float ho = peer.wall != null ? peer.wall.thickness * 0.5f : 0.125f;
                Vector2 ePeer = o1 - o0;
                if (ePeer.sqrMagnitude < 1e-14f)
                {
                    float dPeerPointToSeg = DistancePointToSegmentXZ(o0, p0, p1);
                    if (dPeerPointToSeg < halfSelf + ho - sepEps)
                        return false;
                    continue;
                }

                ePeer /= Mathf.Sqrt(ePeer.sqrMagnitude);
                float parallelDot = Mathf.Abs(Vector2.Dot(eSelf, ePeer));
                float minSep = parallelDot > parallelPeerDotThreshold ? (halfSelf + ho - sepEps) : 0f;
                float dSeg = SegmentSegmentDistanceXZ(p0, p1, o0, o1);
                if (dSeg < minSep)
                    return false;
            }

            return true;
        }

        if (SegmentClearOfPeers(a, b))
            return;

        float tMin = minSegmentLen / Mathf.Max(fullLen, 1e-8f);
        if (tMin > 1f - 1e-6f)
        {
            a = a0;
            b = b0;
            return;
        }

        // Raccourcir depuis B vers A : garder une longueur >= minSegmentLen.
        if (SegmentClearOfPeers(a, Vector2.LerpUnclamped(a, b, tMin)))
        {
            float lo = tMin;
            float hi = 1f;
            for (int it = 0; it < 22; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (SegmentClearOfPeers(a, Vector2.LerpUnclamped(a, b, mid)))
                    lo = mid;
                else
                    hi = mid;
            }

            b = Vector2.LerpUnclamped(a, b, lo);
        }

        if (SegmentClearOfPeers(a, b))
        {
            if (Vector2.Distance(a, b) < minSegmentLen - 1e-4f)
            {
                a = a0;
                b = b0;
            }

            return;
        }

        // Puis depuis A vers B.
        if (SegmentClearOfPeers(Vector2.LerpUnclamped(b, a, tMin), b))
        {
            float lo = tMin;
            float hi = 1f;
            for (int it = 0; it < 22; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (SegmentClearOfPeers(Vector2.LerpUnclamped(b, a, mid), b))
                    lo = mid;
                else
                    hi = mid;
            }

            a = Vector2.LerpUnclamped(b, a, lo);
        }

        if (!SegmentClearOfPeers(a, b) || Vector2.Distance(a, b) < minSegmentLen - 1e-4f)
        {
            a = a0;
            b = b0;
        }
    }

    /// <summary>
    /// Si le segment est trop court pour un mesh valide, tente d’étendre autour du milieu (même direction puis perpendiculaire).
    /// </summary>
    void TryEnsureOpenInteriorWallTwoPointMinLengthXZ(
        ref Vector3 ca,
        ref Vector3 cb,
        float minLen,
        IReadOnlyList<Vector2> ring,
        float inset)
    {
        Vector2 a = new Vector2(ca.x, ca.z);
        Vector2 b = new Vector2(cb.x, cb.z);
        float d = Vector2.Distance(a, b);
        if (d >= minLen - 1e-4f)
            return;

        Vector2 mid = (a + b) * 0.5f;
        Vector2 dir = d > 1e-5f ? (b - a).normalized : Vector2.right;
        float half = minLen * 0.5f;

        Vector2 na = mid - dir * half;
        Vector2 nb = mid + dir * half;
        if (PointInsideLotConstraintXZ(na, ring, inset) && PointInsideLotConstraintXZ(nb, ring, inset))
        {
            ca = new Vector3(na.x, shapeY, na.y);
            cb = new Vector3(nb.x, shapeY, nb.y);
            return;
        }

        Vector2 perp = new Vector2(-dir.y, dir.x);
        na = mid - perp * half;
        nb = mid + perp * half;
        if (PointInsideLotConstraintXZ(na, ring, inset) && PointInsideLotConstraintXZ(nb, ring, inset))
        {
            ca = new Vector3(na.x, shapeY, na.y);
            cb = new Vector3(nb.x, shapeY, nb.y);
        }
    }

    /// <summary>
    /// Demi-épaisseur mur extérieur + demi-épaisseur mur intérieur : retrait du contour centre-ligne du lot
    /// pour que le volume du mur intérieur ne coupe pas la pierre des murs périphériques.
    /// </summary>
    public static float ComputeInteriorWallBoundaryInsetMeters(float lotWallThickness, float interiorWallThickness)
    {
        return Mathf.Max(0.01f, lotWallThickness * 0.5f + interiorWallThickness * 0.5f);
    }

    /// <summary>
    /// Plus petite longueur d’axe (m) acceptable pour un mur intérieur ouvert à deux points : distance entre les
    /// deux poignées d’extrémité sur la ligne blanche d’édition (la poignée du milieu reste au centre du segment).
    /// Ne pas descendre en dessous : c’est le « petit mur » minimal (bouchon ~demi-maille si la grille ≈ 1 m).
    /// </summary>
    public const float OpenInteriorWallHalfMeterShapeLimitMeters = 0.5f;

    /// <summary>
    /// Retrait intérieur appliqué au clip du lot pour un mur intérieur ouvert.
    /// 0 = aucun retrait (contact autorisé avec le bord du lot).
    /// </summary>
    public static float ComputeOpenInteriorWallFootprintInsetMeters(float lotWallThickness, float interiorWallThickness)
    {
        return 0f;
    }

    /// <summary>
    /// Longueur minimale de l’axe du mur (segment ouvert à 2 points) : au moins
    /// <see cref="OpenInteriorWallHalfMeterShapeLimitMeters"/> (cf. plus petit mur intérieur possible en édition),
    /// et au moins 1,5× l’épaisseur si celle-ci impose plus.
    /// </summary>
    public static float GetMinOpenInteriorWallSegmentLengthMeters(WallObject wallObject)
    {
        float t = wallObject != null ? wallObject.thickness : 0.25f;
        return Mathf.Max(OpenInteriorWallHalfMeterShapeLimitMeters, t * 1.5f);
    }

    /// <summary>
    /// Évite un retrait plus grand que la zone intérieure réelle (polygone trop petit).
    /// </summary>
    public static float ClampInsetToFeasibleRingXZ(IReadOnlyList<Vector2> ring, float desiredInset)
    {
        if (ring == null || ring.Count < 3)
            return Mathf.Max(0f, desiredInset);

        if (desiredInset <= 0f)
            return 0f;

        Vector2 c = ComputeRingCentroid2D(ring);
        float d = DistanceToPolygonEdgesXZ(c, ring);
        float maxInset = Mathf.Max(0f, d - 0.02f);
        return Mathf.Min(desiredInset, maxInset * 0.98f);
    }

    static Vector2 ComputeRingCentroid2D(IReadOnlyList<Vector2> ring)
    {
        Vector2 s = Vector2.zero;
        int n = ring.Count;
        for (int i = 0; i < n; i++)
            s += ring[i];
        return n > 0 ? s / n : Vector2.zero;
    }

    /// <summary>
    /// Raccourcit un segment monde (XZ) pour qu’il reste dans le polygone du lot (plus longue portion intérieure).
    /// </summary>
    /// <param name="minDistanceInsideFromBoundary">
    /// Distance minimale aux arêtes du contour (ligne centrale des murs). Utiliser le retrait demi-épaisseurs
    /// pour rester dans la pièce sans traverser le volume des murs extérieurs.
    /// </param>
    /// <param name="minOutputSegmentLength">Si &gt; 0, rejette les clips dont la portion intérieure est plus courte (murs intérieurs).</param>
    public static bool TryClipOpenWorldSegmentToLotRingXZ(
        Vector3 worldA,
        Vector3 worldB,
        List<Vector2> ringXZ,
        out Vector3 outA,
        out Vector3 outB,
        float minDistanceInsideFromBoundary = 0f,
        float minOutputSegmentLength = 0f)
    {
        outA = worldA;
        outB = worldB;
        if (ringXZ == null || ringXZ.Count < 3)
            return false;

        Vector2 a2 = new Vector2(worldA.x, worldA.z);
        Vector2 b2 = new Vector2(worldB.x, worldB.z);
        if (!TryClipOpenSegmentDenseXZ(a2, b2, ringXZ, out Vector2 oa, out Vector2 ob, minDistanceInsideFromBoundary, minOutputSegmentLength))
            return false;

        outA = new Vector3(oa.x, worldA.y, oa.y);
        outB = new Vector3(ob.x, worldB.y, ob.y);
        return true;
    }

    static bool PointInsideLotConstraintXZ(Vector2 p, IReadOnlyList<Vector2> ring, float insetFromBoundary)
    {
        if (ring == null || ring.Count < 3)
            return false;

        if (insetFromBoundary <= 0f)
            return PointInPolygonOrOnEdgeXZ(p, ring);

        return PointInPolygonXZ(p, ring) && DistanceToPolygonEdgesXZ(p, ring) >= insetFromBoundary - 1e-4f;
    }

    static bool TryClipOpenSegmentDenseXZ(
        Vector2 a,
        Vector2 b,
        IReadOnlyList<Vector2> ring,
        out Vector2 outA,
        out Vector2 outB,
        float insetFromBoundary,
        float minOutputSegmentLength = 0f)
    {
        outA = a;
        outB = b;
        const int steps = 96;
        float bestLen = -1f;
        Vector2 bestA = a;
        Vector2 bestB = b;
        bool started = false;
        float tStart = 0f;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector2 p = Vector2.LerpUnclamped(a, b, t);
            bool ins = PointInsideLotConstraintXZ(p, ring, insetFromBoundary);
            if (ins && !started)
            {
                started = true;
                tStart = t;
            }
            else if (!ins && started)
            {
                float tEnd = (i - 1) / (float)steps;
                Vector2 pa = Vector2.LerpUnclamped(a, b, tStart);
                Vector2 pb = Vector2.LerpUnclamped(a, b, tEnd);
                float len = (pb - pa).magnitude;
                if (len > bestLen)
                {
                    bestLen = len;
                    bestA = pa;
                    bestB = pb;
                }

                started = false;
            }
        }

        if (started)
        {
            float tEnd = 1f;
            Vector2 pa = Vector2.LerpUnclamped(a, b, tStart);
            Vector2 pb = Vector2.LerpUnclamped(a, b, tEnd);
            float len = (pb - pa).magnitude;
            if (len > bestLen)
            {
                bestLen = len;
                bestA = pa;
                bestB = pb;
            }
        }

        bool ok = bestLen > 1e-4f &&
                  (minOutputSegmentLength <= 0f || bestLen >= minOutputSegmentLength - 1e-5f);
        if (ok)
        {
            outA = bestA;
            outB = bestB;
        }

        return ok;
    }

    static Vector2 FindClosestInteriorPointApproxXZ(Vector2 p, IReadOnlyList<Vector2> ring, float insetFromBoundary)
    {
        if (insetFromBoundary < 1e-6f)
            return FindClosestInteriorPointApproxXZNoInset(p, ring);

        if (PointInsideLotConstraintXZ(p, ring, insetFromBoundary))
            return p;

        float minx = float.PositiveInfinity;
        float maxx = float.NegativeInfinity;
        float minz = float.PositiveInfinity;
        float maxz = float.NegativeInfinity;
        for (int i = 0; i < ring.Count; i++)
        {
            Vector2 v = ring[i];
            minx = Mathf.Min(minx, v.x);
            maxx = Mathf.Max(maxx, v.x);
            minz = Mathf.Min(minz, v.y);
            maxz = Mathf.Max(maxz, v.y);
        }

        const int g = 22;
        float best = float.MaxValue;
        Vector2 bestPt = p;
        for (int iy = 0; iy <= g; iy++)
        {
            for (int ix = 0; ix <= g; ix++)
            {
                float x = Mathf.Lerp(minx, maxx, ix / (float)g);
                float z = Mathf.Lerp(minz, maxz, iy / (float)g);
                Vector2 q = new Vector2(x, z);
                if (!PointInsideLotConstraintXZ(q, ring, insetFromBoundary))
                    continue;
                float d = (q - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestPt = q;
                }
            }
        }

        if (best < float.MaxValue)
            return bestPt;

        Vector2 c = ComputeRingCentroid2D(ring);
        if (PointInsideLotConstraintXZ(c, ring, insetFromBoundary))
            return c;

        return FindClosestInteriorPointApproxXZ(p, ring, insetFromBoundary * 0.5f);
    }

    static Vector2 FindClosestInteriorPointApproxXZNoInset(Vector2 p, IReadOnlyList<Vector2> ring)
    {
        if (PointInPolygonOrOnEdgeXZ(p, ring))
            return p;

        float minx = float.PositiveInfinity;
        float maxx = float.NegativeInfinity;
        float minz = float.PositiveInfinity;
        float maxz = float.NegativeInfinity;
        for (int i = 0; i < ring.Count; i++)
        {
            Vector2 v = ring[i];
            minx = Mathf.Min(minx, v.x);
            maxx = Mathf.Max(maxx, v.x);
            minz = Mathf.Min(minz, v.y);
            maxz = Mathf.Max(maxz, v.y);
        }

        const int g = 22;
        float best = float.MaxValue;
        Vector2 bestPt = p;
        for (int iy = 0; iy <= g; iy++)
        {
            for (int ix = 0; ix <= g; ix++)
            {
                float x = Mathf.Lerp(minx, maxx, ix / (float)g);
                float z = Mathf.Lerp(minz, maxz, iy / (float)g);
                Vector2 q = new Vector2(x, z);
                if (!PointInPolygonOrOnEdgeXZ(q, ring))
                    continue;
                float d = (q - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestPt = q;
                }
            }
        }

        if (best < float.MaxValue)
            return bestPt;

        Vector2 sum = ComputeRingCentroid2D(ring);
        if (PointInPolygonOrOnEdgeXZ(sum, ring))
            return sum;

        return ring[0];
    }

    static bool PointInPolygonOrOnEdgeXZ(Vector2 p, IReadOnlyList<Vector2> ring)
    {
        if (PointInPolygonXZ(p, ring))
            return true;
        return DistanceToPolygonEdgesXZ(p, ring) <= 0.03f;
    }

    static bool PointInPolygonXZ(Vector2 p, IReadOnlyList<Vector2> ring)
    {
        int n = ring.Count;
        if (n < 3)
            return false;

        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = ring[i];
            Vector2 pj = ring[j];
            float dy = pj.y - pi.y;
            if (Mathf.Abs(dy) < 1e-10f)
                continue;

            if ((pi.y > p.y) != (pj.y > p.y))
            {
                float xInt = (pj.x - pi.x) * (p.y - pi.y) / dy + pi.x;
                if (p.x < xInt)
                    inside = !inside;
            }
        }

        return inside;
    }

    static float DistanceToPolygonEdgesXZ(Vector2 p, IReadOnlyList<Vector2> ring)
    {
        int n = ring.Count;
        if (n < 2)
            return float.MaxValue;

        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = ring[i];
            Vector2 b = ring[(i + 1) % n];
            float d = DistancePointToSegmentXZ(p, a, b);
            if (d < best)
                best = d;
        }

        return best;
    }

    static float DistancePointToSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float den = ab.sqrMagnitude;
        if (den < 1e-14f)
            return Vector2.Distance(p, a);
        float t = Vector2.Dot(p - a, ab) / den;
        t = Mathf.Clamp01(t);
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }

    /// <summary>
    /// Distance minimale entre deux segments dans le plan XZ (y du Vector2 = coordonnée monde z).
    /// Croisement au milieu : 0 (indispensable ; le min des seules extrémités serait faux).
    /// </summary>
    static float SegmentSegmentDistanceXZ(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        if (SegmentsIntersectXZ(p1, q1, p2, q2))
            return 0f;

        float d = float.MaxValue;
        d = Mathf.Min(d, DistancePointToSegmentXZ(p1, p2, q2));
        d = Mathf.Min(d, DistancePointToSegmentXZ(q1, p2, q2));
        d = Mathf.Min(d, DistancePointToSegmentXZ(p2, p1, q1));
        d = Mathf.Min(d, DistancePointToSegmentXZ(q2, p1, q1));
        return d;
    }

    static int OrientationCollinearXZ(Vector2 p, Vector2 q, Vector2 r)
    {
        float val = (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
        const float eps = 1e-8f;
        if (Mathf.Abs(val) < eps)
            return 0;

        return val > 0f ? 1 : 2;
    }

    static bool OnSegmentXZ(Vector2 p, Vector2 q, Vector2 r)
    {
        const float eps = 1e-7f;
        return q.x <= Mathf.Max(p.x, r.x) + eps && q.x >= Mathf.Min(p.x, r.x) - eps &&
               q.y <= Mathf.Max(p.y, r.y) + eps && q.y >= Mathf.Min(p.y, r.y) - eps;
    }

    static bool SegmentsIntersectXZ(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        int o1 = OrientationCollinearXZ(p1, q1, p2);
        int o2 = OrientationCollinearXZ(p1, q1, q2);
        int o3 = OrientationCollinearXZ(p2, q2, p1);
        int o4 = OrientationCollinearXZ(p2, q2, q1);

        if (o1 != o2 && o3 != o4)
            return true;

        if (o1 == 0 && OnSegmentXZ(p1, p2, q1))
            return true;

        if (o2 == 0 && OnSegmentXZ(p1, q2, q1))
            return true;

        if (o3 == 0 && OnSegmentXZ(p2, p1, q2))
            return true;

        if (o4 == 0 && OnSegmentXZ(p2, q1, q2))
            return true;

        return false;
    }
}
