using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Toit sur empreinte fermée :
/// - sommet central réglable en hauteur
/// - profil arrondi (dôme) via <see cref="useDomeProfile"/> + <see cref="roundness"/>
/// - débord via <see cref="overhangMeters"/>
/// - surélévation du socle au-dessus du mur via <see cref="yOffsetAboveWallTop"/> (borne <see cref="MaxYOffsetAboveWallTopMeters"/>).
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofSystem : MonoBehaviour
{
    public const string RoofChildName = "__HouseRoof";
    const float TriEps = 1e-5f;
    public const float MinRoofHeightMeters = 0.25f;
    public const float MaxRoofHeightMeters = 10f;
    public const float MinOverhangMeters = 0.4f;
    public const float MaxOverhangMeters = 1f;

    /// <summary>Nombre maximal de poignées latérales snap (grille empreinte) pilotées par la liste et le mesh.</summary>
    public const int MaxLateralApexPoints = 4;

    /// <summary>Décalage vertical fixe de la semelle du toit au-dessus du mur.</summary>
    public const float RoofBuiltInVerticalLiftMeters = 0.15f;

    /// <summary>Plafond pour <see cref="yOffsetAboveWallTop"/> (surélévation réglable au-delà du haut du mur).</summary>
    public const float MaxYOffsetAboveWallTopMeters = 2.5f;

    /// <summary>Raccord rentré dans le mur : distance perpendiculaire aux façades.</summary>
    public const float EaveInsetPerpendicularToWallMeters = 0.2f;

    [Header("Arrondi — profil dôme")]
    [Tooltip("Active le système d’arrondi : plusieurs bandes radiales + courbe de dôme (Roundness). Réactivé aussi automatiquement dès qu’on règle l’arrondi (molette Ctrl ou poignées).")]
    public bool useDomeProfile = false;
    [Tooltip("Forme de la courbe du dôme (0 = plus plat vers le faîtage, 1 = plus bombé).")]
    [Range(0f, 1f)] public float roundness = 0.45f;

    [Header("Shape")]
    [Tooltip("Conserve la topologie pilotée par les points de contour (pas de subdivisions d'arêtes auto). Les nouvelles faces viennent seulement de nouveaux points ajoutés au contour.")]
    public bool preserveControlPointDrivenTopology = true;
    [Range(MinRoofHeightMeters, MaxRoofHeightMeters)] public float roofHeightMeters = 1.2f;
    [Range(MinOverhangMeters, MaxOverhangMeters)] public float overhangMeters = MinOverhangMeters;
    [Tooltip("Nouveau systeme simple : un pan = un triangle plan par arete. Les poignées de roundness deplacent lateralement les sommets des pans (XZ).")]
    public bool useLateralFaceSystem = true;
    [Tooltip("Decalage XZ du sommet de chaque pan, relatif au centroide de l'empreinte (meme index que l'arete correspondante).")]
    public Vector2[] lateralFaceOffsetsXZ = new Vector2[4];

    [Tooltip("Désactivé = comportement d’origine (diagonale faîtage central ↔ poignée, covents mesh sur cette noue). Activé = après avoir généré ce maillage classique, une passe enlève uniquement l’arête centre↔poignée et reconnecte en quad triangulé le long du bas du pan (b0–b1).")]
    [SerializeField] bool lateralExtensionStructuralQuadAlongBaseEdge = false;

    [Tooltip("Tolérance monde (m) pour retrouver les deux triangles structurels si les découpes locales ont dupliqué ou réindexé des sommets. 0 = correspondance stricte par indices uniquement.")]
    [SerializeField] float lateralExtensionStructuralMergeVertexEpsilonMeters = 0.005f;

    /// <summary>Lu par <see cref="RoofCladdingGenerator"/> pour savoir si une noue mesh centre↔ancre peut manquer (couvre-joint synthétique).</summary>
    public bool LateralExtensionStructuralQuadAlongBaseEdge => lateralExtensionStructuralQuadAlongBaseEdge;
    /// <summary>Offsets XZ des poignées latérales (snap grille), jusqu’à <see cref="MaxLateralApexPoints"/> entrées — utilisées par le générateur de mesh.</summary>
    [SerializeField]
    List<Vector2> lateralApexOffsetsXZ = new List<Vector2>();

    [Tooltip("Compat anciennes scènes : synchronisé depuis lateralApexOffsetsXZ.")]
    public bool lateralApexHandleEnabled = false;
    [Tooltip("Compat sérialisation.")]
    public Vector2 lateralApexOffsetXZ = Vector2.zero;
    [Tooltip("Compat sérialisation.")]
    public bool secondLateralApexHandleEnabled = false;
    [Tooltip("Compat sérialisation.")]
    public Vector2 secondLateralApexOffsetXZ = Vector2.zero;

    /// <summary>Cycle clic droit : après 4 ajouts, les clics suivants retirent jusqu’à liste vide.</summary>
    bool roofPointCycleIsRemoving = false;

    [Header("Temporary — éviter ancrage coin des poignées latérales (grand X)")]
    [Tooltip("Si vrai, repousse les points jaunes/orange hors des coins d’empreinte (réversible).")]
    [SerializeField] bool disableRoofCornerAnchorsTemporary = true;

    [Tooltip("Zone XZ (m) autour d’un coin : si le point entre dans ce disque (en plus de la zone moteur), on le projette sur l’arête. Réduire pour ne bloquer que le voisinage du coin.")]
    [SerializeField] float roofCornerAnchorBlockRadius = 0.12f;

    [Tooltip("Distance minimale le long de l’arête depuis le sommet du coin (m), après projection du drag ; doit dépasser la tolérance coin du moteur (~11 % de l’arête la plus courte).")]
    [SerializeField] float roofCornerAnchorPushDistance = 0.06f;

    /// <summary>Accès lecture pour <see cref="HouseRoofControlPointProvider"/> (UI / logique de drag).</summary>
    public bool DisableRoofCornerAnchorsTemporary => disableRoofCornerAnchorsTemporary;

    [Header("Development logging")]
    [Tooltip("Logs console additionnels (corner block, caps connecteur, touches coupe expérimentale). Désactivé par défaut.")]
    [SerializeField] bool enableVerboseRoofLogs = false;

    [Tooltip("Logs [RoofScalePoint] : conversion hit / offset / monde pour les points latéraux du toit (debug BuildingScale).")]
    [SerializeField] bool logRoofScalePoint;

    [Tooltip("Logs [RoofApexHeightLock] : verrouillage Y des poignées latérales sur le sommet central (debug).")]
    [SerializeField] bool logRoofApexHeightLock;

    [Min(0.02f)] public float roofThicknessMeters = 0.16f;
    [Tooltip("Surélévation de la semelle du toit au-dessus du haut du mur (m), en plus du décalage fixe intégré. Plafonné à MaxYOffsetAboveWallTopMeters.")]
    [Range(0f, MaxYOffsetAboveWallTopMeters)]
    public float yOffsetAboveWallTop = 0f;

    [Header("Runtime")]
    public bool autoRebuild = true;

    [Header("Roof cladding — profil par défaut")]
    [Tooltip("Assignable hors Play sur le mur / prefab : appliqué à RoofCladdingRuntime et RoofCladdingGenerator quand leurs profils sont encore vides (composants créés par ce script).")]
    [SerializeField] RoofCladdingProfile defaultRoofCladdingProfile;

    [Header("Roof shell crossing — local fix (shell uniquement, avant épaisseur)")]
    [Tooltip("Si le diagnostic trouve une intersection XZ, duplique un sommet au cut proposé et ne modifie qu’un seul triangle du shell (indices locaux).")]
    [SerializeField] bool applyRoofShellCrossLocalFix = false;

    [Header("Roof shell — coupe manuelle (shell uniquement, avant épaisseur)")]
    [Tooltip("Applique un point de coupe monde : un coin du triangle choisi est remplacé par un nouveau sommet à cette position (même mécanique que la correction auto).")]
    [SerializeField] bool manualRoofShellCrossCut = false;

    [SerializeField] Vector3 manualRoofShellCrossCutWorld = Vector3.zero;

    [Tooltip("Index du triangle dans le shell généré (0 = premier triangle, etc.).")]
    [SerializeField] int manualRoofShellCrossTriSlot = 0;

    [Tooltip("Quel coin du triangle remplacer : 0, 1 ou 2 (ordre des indices dans roofTris pour ce triangle).")]
    [SerializeField] int manualRoofShellCrossTriCorner = 0;

    [Tooltip("Si activé, annule la coupe manuelle si elle recrée encore une intersection XZ. Désactivé par défaut pour que le déplacement soit visible même en cas de croisement résiduel.")]
    [SerializeField] bool manualRoofShellCrossCutRejectIfStillIntersecting = false;

    [Header("Roof shell — experimental cut (debug visuel, avant épaisseur)")]
    [Tooltip("Modifie brutalement les triangles listés pour tester sans changer la génération du shell. Désactivé par défaut (stabilité).")]
    [SerializeField] bool experimentalCutRawProblemTriangles = false;

    [Tooltip("Si vrai, ignore experimentalCutTriangleSlots et ne coupe que experimentalSingleTriangleSlot.")]
    [SerializeField] bool experimentalUseSingleTriangleSlot = true;

    [Tooltip("Index du triangle du shell (mode single-slot uniquement).")]
    [SerializeField] int experimentalSingleTriangleSlot = 0;

    [Tooltip("Liste de triSlot séparés par des virgules si experimentalUseSingleTriangleSlot est faux (ex. « 4,5 »). Vide = aucune coupe.")]
    [SerializeField] string experimentalCutTriangleSlots = "";

    [Tooltip("En jeu : experimentalPreviousSlotKey / experimentalNextSlotKey pour changer experimentalSingleTriangleSlot et rebuild.")]
    [SerializeField] bool experimentalCycleTriangleSlotWithKeys = false;

    [SerializeField] KeyCode experimentalPreviousSlotKey = KeyCode.Comma;

    [SerializeField] KeyCode experimentalNextSlotKey = KeyCode.Period;

    [Tooltip("Lerp du sommet le plus éloigné du centre d’empreinte vers le centre du triangle (mode raccourcissement).")]
    [SerializeField] float experimentalCutAmount = 0.65f;

    [Tooltip("Si vrai, supprime les triangles listés de roofTris ; sinon raccourcit localement un sommet par triangle.")]
    [SerializeField] bool experimentalRemoveTrianglesInsteadOfShorten = false;

    [Header("Debug — roof shell diagnostics")]
    [Tooltip("Analyse les triangles du shell latéral avant épaisseur. Logs [RoofCrossDiag]. Désactivé par défaut (stabilité).")]
    [SerializeField] bool debugDetectRoofShellCrossingDetailed = false;
    [Tooltip("Alias : si activé, même diagnostic détaillé (compatibilité avec l’ancien nom d’inspecteur).")]
    [SerializeField] bool debugDetectRoofTriangleIntersections = false;

    [Tooltip("Logs [RoofCrossDiagTri] + [RoofCrossDiagFamily] pour chaque triangle du shell (spam console si activé).")]
    [SerializeField] bool debugRoofCrossTriangleFamilyAudit = false;

    [Tooltip("Si Raw problems vaut 7 ou 12, calcule suspects + overlay sans exiger debugRoofCrossTriangleFamilyAudit (pas de logs détaillés sauf si celui-ci est activé). Désactivé par défaut.")]
    [SerializeField] bool autoRunTriangleFamilyAuditWhenProblem = false;

    [Tooltip("Dessine le shell en Game/Scene view (Debug.DrawLine + Gizmos) — diagnostic uniquement, aucun GameObject permanent.")]
    [SerializeField] bool debugDrawRoofCrossTriangleFamily = false;

    [Tooltip("Logs [RoofLevelDiagnostic] : hauteur du pan principal vs sommet d’extension (ancre) au même XZ, normales, BuildingScale.")]
    [SerializeField] bool debugRoofLevelCoplanarityDiagnostic = false;

    /// <summary>Overlay : coupe expérimentale du shell activée.</summary>
    public bool IsExperimentalRoofShellCutEnabled => experimentalCutRawProblemTriangles;

    /// <summary>Overlay : mode un seul triangle.</summary>
    public bool IsExperimentalSingleSlotMode => experimentalUseSingleTriangleSlot;

    /// <summary>Overlay : slot courant et borne max après dernier rebuild du shell.</summary>
    public string ExperimentalCurrentSlotDisplay
    {
        get
        {
            if (_lastRoofShellTriangleCountForExperimental <= 0)
                return experimentalSingleTriangleSlot.ToString(CultureInfo.InvariantCulture);
            int maxS = _lastRoofShellTriangleCountForExperimental - 1;
            return $"{experimentalSingleTriangleSlot.ToString(CultureInfo.InvariantCulture)} (0–{maxS.ToString(CultureInfo.InvariantCulture)})";
        }
    }

    /// <summary>Overlay : chaîne multi-slots (ignorée si mode single).</summary>
    public string ExperimentalCutTriangleSlotsDisplay =>
        string.IsNullOrWhiteSpace(experimentalCutTriangleSlots) ? "(empty)" : experimentalCutTriangleSlots;

    /// <summary>Dernier scan RoofCrossDiag terminé (false tant qu’aucun rebuild avec diagnostic activé n’a eu lieu).</summary>
    public bool RoofCrossDiagScanCompleted { get; private set; }

    /// <summary>
    /// UNKNOWN tant que <see cref="RoofCrossDiagScanCompleted"/> est faux ;
    /// sinon CLEAN ou PROBLEM selon <see cref="IsRoofCrossDiagProblemCountConsideredRealProblem"/> sur <see cref="LastRoofCrossDiagProblemCount"/> (7 ou 12 seulement).
    /// </summary>
    public string LastRoofCrossDiagStatus =>
        !RoofCrossDiagScanCompleted
            ? "UNKNOWN"
            : (IsRoofCrossDiagProblemCountConsideredRealProblem(LastRoofCrossDiagProblemCount) ? "PROBLEM" : "CLEAN");

    /// <summary>
    /// Nombre brut de contacts / problèmes géométriques détectés par le scan (logs détaillés inchangés).
    /// Le statut UI / summary utilise uniquement 7 ou 12 comme cas « réels ».
    /// </summary>
    public int LastRoofCrossDiagProblemCount { get; private set; }

    /// <summary>Même valeur que <see cref="LastRoofCrossDiagProblemCount"/> : nombre brut de détections pendant le dernier scan.</summary>
    public int LastRoofCrossDiagRawProblemCount { get; private set; }

    public string LastRoofCrossDiagLastProblemType { get; private set; }
    public string LastRoofCrossDiagLastReason { get; private set; }
    public Vector3 LastRoofCrossDiagLastProposedCutPoint { get; private set; }
    public int LastRoofCrossDiagLastVertexToShorten { get; private set; }
    public int LastRoofCrossDiagLastTriA { get; private set; }
    public int LastRoofCrossDiagLastTriB { get; private set; }
    public bool LastRoofCrossDiagDualCornerAnchor { get; private set; }
    public int LastRoofCrossDiagTrianglesScanned { get; private set; }

    /// <summary>Toujours -1 tant qu’aucun candidat de coupe fiable n’est défini (mode honnête).</summary>
    public int LastRoofCrossDiagChosenProblemIndex { get; private set; } = -1;

    public float LastRoofCrossDiagChosenScore { get; private set; }
    public Vector2 LastRoofCrossDiagChosenHitXZ { get; private set; }

    /// <summary>Indices de slots triangles du candidat retenu (identiques à <see cref="LastRoofCrossDiagLastTriA"/> après choix).</summary>
    public int LastRoofCrossDiagChosenTriA { get; private set; } = -1;

    public int LastRoofCrossDiagChosenTriB { get; private set; } = -1;
    public string LastRoofCrossDiagChosenReason { get; private set; } = "";

    /// <summary>Logs détaillés [RoofCrossDiagTri] activés.</summary>
    public bool IsRoofCrossTriangleFamilyAuditLogsEnabled => debugRoofCrossTriangleFamilyAudit;

    /// <summary>Recalcul auto des suspects si Raw problems ∈ {7,12}.</summary>
    public bool IsAutoRunTriangleFamilyAuditWhenProblemEnabled => autoRunTriangleFamilyAuditWhenProblem;

    /// <summary>Dessin debug des triangles (couleurs).</summary>
    public bool IsDebugDrawRoofCrossTriangleFamilyEnabled => debugDrawRoofCrossTriangleFamily;

    /// <summary>Détail LOGS / AUTO / OFF (audit triangle famille). L’overlay utilise plutôt <see cref="IsRoofCrossTriangleFamilyAuditOn"/> (ON/OFF).</summary>
    public string GetRoofCrossTriangleFamilyAuditModeLabel()
    {
        if (!RoofCrossDiagScanCompleted)
            return "OFF";
        if (debugRoofCrossTriangleFamilyAudit)
            return "LOGS";
        if (autoRunTriangleFamilyAuditWhenProblem &&
            IsRoofCrossDiagProblemCountConsideredRealProblem(LastRoofCrossDiagRawProblemCount))
            return "AUTO";
        return "OFF";
    }

    /// <summary>Dernier résumé pour l’overlay : triangles serrés (participants X ∪ parasite probable).</summary>
    public string LastRoofCrossSuspectTriangleSlotsDisplay { get; private set; } = "none";

    public string LastRoofCrossXDiagonalCandidatesDisplay { get; private set; } = "none";

    public string LastRoofCrossXCrossParticipantsDisplay { get; private set; } = "none";

    public string LastRoofCrossParasiteLikelySlotsDisplay { get; private set; } = "none";

    /// <summary>Vrai si l’audit famille triangle est actif (logs, dessin, ou auto sur cas 7/12).</summary>
    public bool IsRoofCrossTriangleFamilyAuditOn =>
        RoofCrossDiagScanCompleted &&
        (debugRoofCrossTriangleFamilyAudit ||
         debugDrawRoofCrossTriangleFamily ||
         (autoRunTriangleFamilyAuditWhenProblem &&
          IsRoofCrossDiagProblemCountConsideredRealProblem(LastRoofCrossDiagRawProblemCount)));

    /// <summary>Même condition que l’exécution du scan dans <see cref="TryRebuildLateralFaceRoofMesh"/>.</summary>
    public bool IsRoofCrossShellDiagnosticEnabled =>
        debugDetectRoofShellCrossingDetailed || debugDetectRoofTriangleIntersections;

    /// <summary>
    /// Règle UI / résumé : seuls les dénombrements issus des tests empiriques (grand X visible) sont traités comme PROBLEM.
    /// Ne modifie pas le mesh ; filtre les faux positifs fréquents sur toits sains.
    /// </summary>
    public static bool IsRoofCrossDiagProblemCountConsideredRealProblem(int problemCount) =>
        problemCount == 7 || problemCount == 12;

    void ResetRoofCrossDiagStateForScan()
    {
        RoofCrossDiagScanCompleted = false;
        LastRoofCrossDiagProblemCount = 0;
        LastRoofCrossDiagLastProblemType = "";
        LastRoofCrossDiagLastReason = "";
        LastRoofCrossDiagLastProposedCutPoint = Vector3.zero;
        LastRoofCrossDiagLastVertexToShorten = -1;
        LastRoofCrossDiagLastTriA = -1;
        LastRoofCrossDiagLastTriB = -1;
        LastRoofCrossDiagTrianglesScanned = 0;
        LastRoofCrossDiagRawProblemCount = 0;
        LastRoofCrossDiagChosenProblemIndex = -1;
        LastRoofCrossDiagChosenScore = 0f;
        LastRoofCrossDiagChosenHitXZ = Vector2.zero;
        LastRoofCrossDiagChosenTriA = -1;
        LastRoofCrossDiagChosenTriB = -1;
        LastRoofCrossDiagChosenReason = "";
        LastRoofCrossSuspectTriangleSlotsDisplay = "none";
        LastRoofCrossXDiagonalCandidatesDisplay = "none";
        LastRoofCrossXCrossParticipantsDisplay = "none";
        LastRoofCrossParasiteLikelySlotsDisplay = "none";
        ClearRoofCrossTriangleFamilyGizmoCache();
    }

    void FinalizeRoofCrossDiagState(int problems, int triCount, bool dualCornerAnchorMode)
    {
        LastRoofCrossDiagProblemCount = problems;
        LastRoofCrossDiagRawProblemCount = problems;
        LastRoofCrossDiagTrianglesScanned = triCount;
        LastRoofCrossDiagDualCornerAnchor = dualCornerAnchorMode;
        RoofCrossDiagScanCompleted = true;
    }

    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    Material _connectorMaterial;
    Material _roofFallbackSkinMaterial;
    int _lastHash;

    /// <summary>Dernier nombre de triangles du shell (pour clamp touches et overlay).</summary>
    int _lastRoofShellTriangleCountForExperimental;

    readonly List<Vector3> _roofCrossFamilyGizmoA = new List<Vector3>(128);
    readonly List<Vector3> _roofCrossFamilyGizmoB = new List<Vector3>(128);
    readonly List<Vector3> _roofCrossFamilyGizmoC = new List<Vector3>(128);
    readonly List<Color> _roofCrossFamilyGizmoColors = new List<Color>(128);

    void ClearRoofCrossTriangleFamilyGizmoCache()
    {
        _roofCrossFamilyGizmoA.Clear();
        _roofCrossFamilyGizmoB.Clear();
        _roofCrossFamilyGizmoC.Clear();
        _roofCrossFamilyGizmoColors.Clear();
    }

    public static HouseRoofSystem EnsureOnWall(WallObject wall)
    {
        if (wall == null)
            return null;
        HouseRoofSystem roof = wall.GetComponent<HouseRoofSystem>();
        if (roof == null)
        {
            roof = wall.gameObject.AddComponent<HouseRoofSystem>();
            Debug.Log("[RoofAutoCreate] HouseRoofSystem added by = EnsureOnWall (Add Roof / explicit code path)", wall);
        }

        roof.EnsureComponents();
        roof.RebuildNow();
        return roof;
    }

    /// <summary>
    /// Copie les réglages de forme du toit (pas les flags debug / coupe expérimentale), typiquement depuis un lot source
    /// vers le mur enveloppe après agrandissement : un seul toit pour l’empreinte fusionnée.
    /// </summary>
    public void CopyShallowGenerationSettingsFrom(HouseRoofSystem donor)
    {
        if (donor == null || donor == this)
            return;

        useDomeProfile = donor.useDomeProfile;
        roundness = donor.roundness;
        preserveControlPointDrivenTopology = donor.preserveControlPointDrivenTopology;
        roofHeightMeters = donor.roofHeightMeters;
        overhangMeters = donor.overhangMeters;
        useLateralFaceSystem = donor.useLateralFaceSystem;

        if (donor.lateralFaceOffsetsXZ != null && donor.lateralFaceOffsetsXZ.Length > 0)
            lateralFaceOffsetsXZ = (Vector2[])donor.lateralFaceOffsetsXZ.Clone();

        lateralApexOffsetsXZ = donor.lateralApexOffsetsXZ != null
            ? new List<Vector2>(donor.lateralApexOffsetsXZ)
            : new List<Vector2>();
        SyncLegacyLateralFieldsFromList();

        disableRoofCornerAnchorsTemporary = donor.disableRoofCornerAnchorsTemporary;
        roofCornerAnchorBlockRadius = donor.roofCornerAnchorBlockRadius;
        roofCornerAnchorPushDistance = donor.roofCornerAnchorPushDistance;
        roofThicknessMeters = donor.roofThicknessMeters;
        yOffsetAboveWallTop = donor.yOffsetAboveWallTop;
        ClampYOffsetAboveWallTopInPlace();
        defaultRoofCladdingProfile = donor.defaultRoofCladdingProfile;
        lateralExtensionStructuralQuadAlongBaseEdge = donor.lateralExtensionStructuralQuadAlongBaseEdge;
        lateralExtensionStructuralMergeVertexEpsilonMeters = donor.lateralExtensionStructuralMergeVertexEpsilonMeters;
    }

    public void MigrateLegacyLateralAnchorsToListIfNeeded()
    {
        if (lateralApexOffsetsXZ == null)
            lateralApexOffsetsXZ = new List<Vector2>();
        if (lateralApexOffsetsXZ.Count > 0)
            return;
        if (lateralApexHandleEnabled && lateralApexOffsetXZ.sqrMagnitude > 1e-8f)
            lateralApexOffsetsXZ.Add(lateralApexOffsetXZ);
        if (secondLateralApexHandleEnabled && secondLateralApexOffsetXZ.sqrMagnitude > 1e-8f)
            lateralApexOffsetsXZ.Add(secondLateralApexOffsetXZ);
    }

    public void SyncLegacyLateralFieldsFromList()
    {
        if (lateralApexOffsetsXZ == null)
            lateralApexOffsetsXZ = new List<Vector2>();
        int n = Mathf.Min(MaxLateralApexPoints, lateralApexOffsetsXZ.Count);
        lateralApexHandleEnabled = n >= 1 && lateralApexOffsetsXZ[0].sqrMagnitude > 1e-8f;
        lateralApexOffsetXZ = n >= 1 ? lateralApexOffsetsXZ[0] : Vector2.zero;
        secondLateralApexHandleEnabled = n >= 2 && lateralApexOffsetsXZ[1].sqrMagnitude > 1e-8f;
        secondLateralApexOffsetXZ = n >= 2 ? lateralApexOffsetsXZ[1] : Vector2.zero;
    }

    public int LateralApexOffsetCount =>
        lateralApexOffsetsXZ == null ? 0 : Mathf.Min(MaxLateralApexPoints, lateralApexOffsetsXZ.Count);

    public bool IsPrimaryLateralNearlyCoincidentWithCentroid =>
        lateralApexOffsetsXZ == null ||
        lateralApexOffsetsXZ.Count == 0 ||
        lateralApexOffsetsXZ[0].sqrMagnitude <= 1e-8f;

    public void ClearLateralApexOffsets()
    {
        if (lateralApexOffsetsXZ == null)
            lateralApexOffsetsXZ = new List<Vector2>();
        lateralApexOffsetsXZ.Clear();
        SyncLegacyLateralFieldsFromList();
    }

    public Vector2 GetLateralApexOffsetSnapshot(int index)
    {
        if (lateralApexOffsetsXZ == null || index < 0 || index >= lateralApexOffsetsXZ.Count)
            return Vector2.zero;
        return lateralApexOffsetsXZ[index];
    }

    public void SetLateralApexOffsetAtIndex(int index, Vector2 offsetXZ)
    {
        if (lateralApexOffsetsXZ == null || index < 0 || index >= lateralApexOffsetsXZ.Count)
            return;
        lateralApexOffsetsXZ[index] = offsetXZ;
        SyncLegacyLateralFieldsFromList();
    }

    public bool TryAddLateralOffsetXZ(Vector2 offsetXZ)
    {
        if (lateralApexOffsetsXZ == null)
            lateralApexOffsetsXZ = new List<Vector2>();
        if (lateralApexOffsetsXZ.Count >= MaxLateralApexPoints)
            return false;
        lateralApexOffsetsXZ.Add(offsetXZ);
        SyncLegacyLateralFieldsFromList();
        return true;
    }

    public bool TryRemoveLateralAtIndex(int index)
    {
        if (lateralApexOffsetsXZ == null || index < 0 || index >= lateralApexOffsetsXZ.Count)
            return false;
        lateralApexOffsetsXZ.RemoveAt(index);
        SyncLegacyLateralFieldsFromList();
        return true;
    }

    /// <summary>Échelle bâtiment (1 si pas de <see cref="WallBuildController"/> sur ce mur).</summary>
    public float GetRoofPointBuildingScaleOrOne()
    {
        var wbc = GetComponent<WallBuildController>();
        return wbc != null ? Mathf.Max(0.01f, wbc.GetEffectiveBuildingScale()) : 1f;
    }

    /// <summary>
    /// Offsets latéraux : delta XZ monde par rapport au centroïde <see cref="TryComputeFootprintBaseCornersWorld"/> (même repère que le mesh).
    /// Aucun facteur <see cref="GetRoofPointBuildingScaleOrOne"/> n'est appliqué sur l'offset : chemin et dimensions sont déjà à l'échelle monde.
    /// </summary>
    void LogRoofScalePoint(
        string context,
        Vector3? worldHit,
        Vector2 footprintCentroidXZ,
        Vector2 storedOffsetXZ,
        Vector3 resolvedWorld)
    {
        if (!logRoofScalePoint)
            return;
        float bs = GetRoofPointBuildingScaleOrOne();
        var inv = CultureInfo.InvariantCulture;
        Debug.Log($"[RoofScalePoint] buildingScale={bs.ToString("F5", inv)} context={context}", this);
        if (worldHit.HasValue)
        {
            Debug.Log($"[RoofScalePoint] worldHit={worldHit.Value.ToString("F4", inv)}", this);
            Vector2 hitXz = new Vector2(worldHit.Value.x, worldHit.Value.z);
            Vector2 resXz = new Vector2(resolvedWorld.x, resolvedWorld.z);
            float dDelta = (hitXz - resXz).magnitude;
            Debug.Log($"[RoofScalePoint] deltaWorldHitToResolved={dDelta.ToString("F5", inv)}", this);
        }
        else
        {
            Debug.Log("[RoofScalePoint] worldHit=(n/a)", this);
            Debug.Log("[RoofScalePoint] deltaWorldHitToResolved=(n/a)", this);
        }

        Debug.Log($"[RoofScalePoint] centroid=({footprintCentroidXZ.x.ToString("F4", inv)},{footprintCentroidXZ.y.ToString("F4", inv)})", this);
        Debug.Log($"[RoofScalePoint] storedOffset=({storedOffsetXZ.x.ToString("F4", inv)},{storedOffsetXZ.y.ToString("F4", inv)})", this);
        Debug.Log($"[RoofScalePoint] resolvedWorld={resolvedWorld.ToString("F4", inv)}", this);
        Debug.Log("[RoofScalePoint] usingScaledOffset=False (offsets XZ = monde - centroid empreinte mesh ; pas de re-multiply BuildingScale sur l'offset)", this);
    }

    /// <summary>
    /// Clic droit : ajoute jusqu’à 4 offsets (ordre fixe par rapport au monde XZ : droite, gauche, devant, derrière),
    /// puis retire du dernier au premier (LIFO).
    /// </summary>
    public void ApplyRoofHeightHandleRightClickCycle(
        Vector3 worldHit,
        HouseRoofControlPointProvider provider,
        bool ctrlHeld,
        out bool changed)
    {
        changed = false;
        if (provider == null)
            return;

        if (lateralApexOffsetsXZ == null)
            lateralApexOffsetsXZ = new List<Vector2>();

        if (ctrlHeld)
        {
            ClearLateralApexOffsets();
            roofPointCycleIsRemoving = false;
            RebuildNow();
            changed = true;
            Debug.Log("[RoofPointCycle] count=0 action=CLEAR");
            return;
        }

        if (!roofPointCycleIsRemoving)
        {
            if (lateralApexOffsetsXZ.Count < MaxLateralApexPoints)
            {
                int slot = lateralApexOffsetsXZ.Count;
                if (provider.TryComputeLateralSnapOffsetForCycleSlot(slot, out Vector2 off))
                {
                    lateralApexOffsetsXZ.Add(off);
                    SyncLegacyLateralFieldsFromList();
                    RebuildNow();
                    changed = true;
                    string dir = slot switch
                    {
                        0 => "RIGHT(+X)",
                        1 => "LEFT(-X)",
                        2 => "FRONT(+Z)",
                        3 => "BACK(-Z)",
                        _ => "?"
                    };
                    Debug.Log($"[RoofPointCycle] count={lateralApexOffsetsXZ.Count.ToString(CultureInfo.InvariantCulture)} action=ADD slotDir={dir}");
                    if (TryComputeFootprintBaseCornersWorld(out _, out Vector2 cAdd, out _, out _) &&
                        TryGetLateralApexWorldAtIndex(slot, out Vector3 wAdd))
                        LogRoofScalePoint($"RightClickAdd slot={slot} {dir}", worldHit, cAdd, off, wAdd);
                }

                if (lateralApexOffsetsXZ.Count >= MaxLateralApexPoints)
                    roofPointCycleIsRemoving = true;
            }
        }
        else
        {
            if (lateralApexOffsetsXZ.Count > 0)
            {
                lateralApexOffsetsXZ.RemoveAt(lateralApexOffsetsXZ.Count - 1);
                SyncLegacyLateralFieldsFromList();
                RebuildNow();
                changed = true;
                Debug.Log($"[RoofPointCycle] count={lateralApexOffsetsXZ.Count.ToString(CultureInfo.InvariantCulture)} action=REMOVE");
                if (lateralApexOffsetsXZ.Count <= 0)
                    roofPointCycleIsRemoving = false;
            }
        }
    }

    void Awake() => EnsureComponents();
    void OnEnable() => EnsureComponents();

    void Update()
    {
        ExperimentalTryCycleTriangleSlotWithKeys();
    }

    void ExperimentalTryCycleTriangleSlotWithKeys()
    {
        if (!experimentalCutRawProblemTriangles ||
            !experimentalCycleTriangleSlotWithKeys ||
            !experimentalUseSingleTriangleSlot)
            return;

        int maxSlot = Mathf.Max(0, _lastRoofShellTriangleCountForExperimental - 1);
        if (_lastRoofShellTriangleCountForExperimental <= 0)
            return;

        int oldSlot = experimentalSingleTriangleSlot;
        int newSlot = oldSlot;

        if (Input.GetKeyDown(experimentalNextSlotKey))
            newSlot = Mathf.Min(oldSlot + 1, maxSlot);
        else if (Input.GetKeyDown(experimentalPreviousSlotKey))
            newSlot = Mathf.Max(oldSlot - 1, 0);
        else
            return;

        if (newSlot == oldSlot)
            return;

        experimentalSingleTriangleSlot = newSlot;
        if (enableVerboseRoofLogs)
            Debug.Log(
                $"[RoofExperimentalCut] cycleSlot old={oldSlot.ToString(CultureInfo.InvariantCulture)} new={newSlot.ToString(CultureInfo.InvariantCulture)} reason=EXPERIMENTAL_KEY_CYCLE",
                this);
        RebuildNow();
    }

    void LateUpdate()
    {
        if (debugDrawRoofCrossTriangleFamily && _roofCrossFamilyGizmoA.Count > 0)
        {
            for (int i = 0; i < _roofCrossFamilyGizmoA.Count; i++)
            {
                Color col = i < _roofCrossFamilyGizmoColors.Count ? _roofCrossFamilyGizmoColors[i] : Color.white;
                Vector3 a = _roofCrossFamilyGizmoA[i], b = _roofCrossFamilyGizmoB[i], c = _roofCrossFamilyGizmoC[i];
                Debug.DrawLine(a, b, col, 0f, false);
                Debug.DrawLine(b, c, col, 0f, false);
                Debug.DrawLine(c, a, col, 0f, false);
            }
        }

        if (!autoRebuild)
            return;
        int h = ComputeHash();
        if (h == _lastHash)
            return;
        RebuildNow();
    }

    void OnDrawGizmos()
    {
        if (!debugDrawRoofCrossTriangleFamily || _roofCrossFamilyGizmoA.Count == 0)
            return;
        for (int i = 0; i < _roofCrossFamilyGizmoA.Count; i++)
        {
            Gizmos.color = i < _roofCrossFamilyGizmoColors.Count ? _roofCrossFamilyGizmoColors[i] : Color.white;
            Vector3 a = _roofCrossFamilyGizmoA[i], b = _roofCrossFamilyGizmoB[i], c = _roofCrossFamilyGizmoC[i];
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, a);
        }
    }

    /// <summary>
    /// Empreinte réelle du toit : même polygone que le maillage (chemin fermé + débord), avant subdivision des arêtes.
    /// Utiliser pour grille / snap afin d’éviter tout décalage avec un quad « déduit » du contour.
    /// </summary>
    public bool TryComputeFootprintBaseCornersWorld(
        out float basePlateWorldY,
        out Vector2 centroidXZ,
        out List<Vector3> outerBaseCornersAtPlateY,
        out List<Vector3> wallTopCornersAtPlateY)
    {
        basePlateWorldY = 0f;
        centroidXZ = default;
        outerBaseCornersAtPlateY = null;
        wallTopCornersAtPlateY = null;

        WallObject wall = GetComponent<WallObject>();
        WallEditShape edit = GetComponent<WallEditShape>();
        if (wall == null || edit == null || !edit.IsClosedLoopPath)
            return false;

        // Même contour que l’overlay / le mesh affiché quand le preview analytique diverge (voir WallEditShape.GetOverlayPathWorld).
        List<Vector3> ring = edit.GetOverlayPathWorld();
        if (!TryPrepareClosedRing(ring, out List<Vector3> prepared))
            return false;

        roofHeightMeters = Mathf.Clamp(roofHeightMeters, MinRoofHeightMeters, MaxRoofHeightMeters);
        overhangMeters = Mathf.Clamp(overhangMeters, MinOverhangMeters, MaxOverhangMeters);

        float baseY = edit.shapeY + wall.height + yOffsetAboveWallTop + RoofBuiltInVerticalLiftMeters;
        basePlateWorldY = baseY;

        var wallCorners = new List<Vector3>(prepared.Count);
        var baseCorners = new List<Vector3>(prepared.Count);
        centroidXZ = ComputeCentroidXZ(prepared);

        var footprintXz = new List<Vector2>(prepared.Count);
        for (int i = 0; i < prepared.Count; i++)
            footprintXz.Add(new Vector2(prepared[i].x, prepared[i].z));

        if (!TryInsetPolygonXZPerpendicular(footprintXz, EaveInsetPerpendicularToWallMeters, out List<Vector2> wallFootprintXz))
            wallFootprintXz = footprintXz;

        for (int i = 0; i < prepared.Count; i++)
        {
            Vector3 p = prepared[i];
            Vector2 wc = wallFootprintXz[i];
            wallCorners.Add(new Vector3(wc.x, baseY, wc.y));
            Vector2 dir = new Vector2(p.x - centroidXZ.x, p.z - centroidXZ.y);
            if (dir.sqrMagnitude > 1e-8f)
                dir.Normalize();
            baseCorners.Add(new Vector3(p.x + dir.x * overhangMeters, baseY, p.z + dir.y * overhangMeters));
        }

        outerBaseCornersAtPlateY = baseCorners;
        wallTopCornersAtPlateY = wallCorners;
        return true;
    }

    /// <summary>
    /// Maillage du composant toit (enfant <see cref="RoofChildName"/>), null ou vide s’il n’est pas encore généré.
    /// Submesh 0 = shell, 1 = raccord gouttière.
    /// </summary>
    public Mesh GetRoofSharedMesh() => _mesh != null && _mesh.vertexCount > 0 ? _mesh : null;

    public MeshFilter GetRoofMeshFilter() => _mf;

    /// <summary>
    /// Nombre de triangles du shell extérieur avant l’ajout de l’épaisseur / sous-face.
    /// Les triangles ajoutés après ce seuil sont structurels (dos, épaisseur) et ne doivent pas recevoir de cladding.
    /// </summary>
    public int GetRoofExteriorShellTriangleCount() => Mathf.Max(0, _lastRoofShellTriangleCountForExperimental);

    /// <summary>Hash de configuration (même logique que le rebuild auto) — pour régénérer le habillage de toit.</summary>
    public int GetRoofConfigurationHash() => ComputeHash();

    /// <summary>
    /// Si <see cref="disableRoofCornerAnchorsTemporary"/> : lorsque le faitage latéral entre dans la zone « coin » (même ordre de grandeur que <see cref="IsAnchorOnBaseCornerXZ"/>),
    /// projette le point sur l’arête complète choisie et le repousse juste hors de cette zone, pour garder un placement continu le long du bord.
    /// </summary>
    /// <returns>Vrai si l’offset a été modifié (un log est alors émis).</returns>
    public bool TryPushLateralOffsetAwayFromFootprintCornersTemporary(
        Vector2 dragWorldXZ,
        Vector2 footprintCentroidXZ,
        float apexWorldY,
        ref Vector2 lateralOffsetXZ)
    {
        if (!disableRoofCornerAnchorsTemporary)
            return false;
        if (!TryComputeFootprintBaseCornersWorld(out _, out _, out List<Vector3> corners, out _))
            return false;
        if (corners == null || corners.Count < 3)
            return false;

        float minEdgeLenSq = FootprintMinEdgeLengthSqXZ(corners);
        float minEdgeLen = Mathf.Sqrt(Mathf.Max(1e-8f, minEdgeLenSq));
        float engineCornerRadius = minEdgeLen * 0.11f;
        float cornerEpsSq = Mathf.Max(1e-6f, minEdgeLenSq * (0.11f * 0.11f));
        float blockR = Mathf.Max(0f, roofCornerAnchorBlockRadius);
        float triggerSq = Mathf.Max(cornerEpsSq, blockR * blockR);

        float keepAlong = Mathf.Max(
            engineCornerRadius + 1e-3f,
            Mathf.Max(1e-3f, roofCornerAnchorPushDistance));

        Vector2 apex = new Vector2(
            footprintCentroidXZ.x + lateralOffsetXZ.x,
            footprintCentroidXZ.y + lateralOffsetXZ.y);

        int n = corners.Count;
        int hitCorner = -1;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Vector2 c = new Vector2(corners[i].x, corners[i].z);
            float dsq = (apex - c).sqrMagnitude;
            if (dsq <= triggerSq && dsq < bestSq)
            {
                bestSq = dsq;
                hitCorner = i;
            }
        }

        if (hitCorner < 0)
            return false;

        Vector2 C = new Vector2(corners[hitCorner].x, corners[hitCorner].z);
        Vector2 Cnext = new Vector2(corners[(hitCorner + 1) % n].x, corners[(hitCorner + 1) % n].z);
        Vector2 Cprev = new Vector2(corners[(hitCorner + n - 1) % n].x, corners[(hitCorner + n - 1) % n].z);

        Vector2 toNext = Cnext - C;
        Vector2 toPrev = Cprev - C;
        float lenNext = toNext.magnitude;
        float lenPrev = toPrev.magnitude;
        if (lenNext < 1e-6f || lenPrev < 1e-6f)
            return false;

        Vector2 dirNext = toNext / lenNext;
        Vector2 dirPrev = toPrev / lenPrev;

        Vector2 fromApex = apex - C;
        if (fromApex.sqrMagnitude < 1e-10f)
            fromApex = dragWorldXZ - C;
        if (fromApex.sqrMagnitude < 1e-10f)
            fromApex = dirNext;

        fromApex.Normalize();
        float dn = Vector2.Dot(fromApex, dirNext);
        float dp = Vector2.Dot(fromApex, dirPrev);
        bool useNext = dn >= dp;
        Vector2 chosenDir = useNext ? dirNext : dirPrev;
        float edgeLen = useNext ? lenNext : lenPrev;

        float t = Mathf.Clamp(Vector2.Dot(apex - C, chosenDir), 0f, edgeLen);
        if (edgeLen < 2f * keepAlong + 1e-4f)
            t = edgeLen * 0.5f;
        else
        {
            if (t < keepAlong)
                t = keepAlong;
            else if (t > edgeLen - keepAlong)
                t = edgeLen - keepAlong;
        }

        Vector2 pushed = C + chosenDir * t;
        if ((pushed - apex).sqrMagnitude < 1e-10f)
            return false;

        Vector2 originalOffset = lateralOffsetXZ;
        lateralOffsetXZ = new Vector2(
            pushed.x - footprintCentroidXZ.x,
            pushed.y - footprintCentroidXZ.y);

        Vector3 origWorld = new Vector3(
            footprintCentroidXZ.x + originalOffset.x,
            apexWorldY,
            footprintCentroidXZ.y + originalOffset.y);
        Vector3 pushWorld = new Vector3(pushed.x, apexWorldY, pushed.y);

        if (enableVerboseRoofLogs)
            Debug.Log(
                $"[RoofCornerBlock] blocked corner anchor point=({pushWorld.x.ToString("F4", CultureInfo.InvariantCulture)},{pushWorld.y.ToString("F4", CultureInfo.InvariantCulture)},{pushWorld.z.ToString("F4", CultureInfo.InvariantCulture)}) cornerIndex={hitCorner.ToString(CultureInfo.InvariantCulture)} originalWorld=({origWorld.x.ToString("F4", CultureInfo.InvariantCulture)},{origWorld.y.ToString("F4", CultureInfo.InvariantCulture)},{origWorld.z.ToString("F4", CultureInfo.InvariantCulture)}) pushedWorld=({pushWorld.x.ToString("F4", CultureInfo.InvariantCulture)},{pushWorld.y.ToString("F4", CultureInfo.InvariantCulture)},{pushWorld.z.ToString("F4", CultureInfo.InvariantCulture)}) reason=TEMPORARY_DISABLE_CORNER_ANCHORS",
                this);

        return true;
    }

    public void EnsureLateralFaceOffsetArray(int n)
    {
        if (n <= 0)
            return;
        if (lateralFaceOffsetsXZ == null || lateralFaceOffsetsXZ.Length != n)
        {
            Vector2[] old = lateralFaceOffsetsXZ;
            lateralFaceOffsetsXZ = new Vector2[n];
            if (old != null)
            {
                int copy = Mathf.Min(old.Length, n);
                for (int i = 0; i < copy; i++)
                    lateralFaceOffsetsXZ[i] = old[i];
            }
        }
    }

    float ComputeFootprintClampRadius(List<Vector3> baseCorners, Vector2 centroidXZ)
    {
        float maxR = 0f;
        if (baseCorners != null)
        {
            for (int i = 0; i < baseCorners.Count; i++)
            {
                Vector2 d = new Vector2(baseCorners[i].x - centroidXZ.x, baseCorners[i].z - centroidXZ.y);
                maxR = Mathf.Max(maxR, d.magnitude);
            }
        }
        return Mathf.Max(0.1f, maxR * 0.95f);
    }

    public bool TryGetLateralFaceHandleWorld(int edgeIndex, out Vector3 world)
    {
        world = default;
        if (!useLateralFaceSystem || useDomeProfile)
            return false;
        if (!TryComputeFootprintBaseCornersWorld(out float baseY, out Vector2 centroid, out List<Vector3> baseCorners, out _))
            return false;
        int n = baseCorners != null ? baseCorners.Count : 0;
        if (edgeIndex < 0 || edgeIndex >= n)
            return false;

        EnsureLateralFaceOffsetArray(n);
        float clampRadius = ComputeFootprintClampRadius(baseCorners, centroid);
        Vector3 refApex = new Vector3(centroid.x, baseY + roofHeightMeters, centroid.y);
        Vector3 b0 = baseCorners[edgeIndex];
        Vector3 b1 = baseCorners[(edgeIndex + 1) % n];
        world = ComputeLateralFaceApexWorld(b0, b1, refApex, centroid, lateralFaceOffsetsXZ[edgeIndex], baseY, clampRadius, out _);
        return true;
    }

    /// <summary>Boucle fermée pour LineRenderer : coins du débord extérieur + milieux d’arête (2N points).</summary>
    public bool TryGetFootprintGuideLoopWorld(float yWorld, out Vector3[] loopPositions)
    {
        loopPositions = null;
        if (!TryComputeFootprintBaseCornersWorld(out _, out _, out List<Vector3> outer, out _))
            return false;
        int n = outer != null ? outer.Count : 0;
        if (n < 3)
            return false;

        loopPositions = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            Vector3 a = outer[i];
            Vector3 b = outer[(i + 1) % n];
            loopPositions[i * 2] = new Vector3(a.x, yWorld, a.z);
            loopPositions[i * 2 + 1] = new Vector3((a.x + b.x) * 0.5f, yWorld, (a.z + b.z) * 0.5f);
        }

        return true;
    }

    /// <summary>Points d’aimantation : boucle (2N) + centre empreinte (dernier élément), au niveau Y — même base que le maillage.</summary>
    public bool TryGetFootprintSnapPointsWorld(float yWorld, out Vector3[] points)
    {
        points = null;
        if (!TryComputeFootprintBaseCornersWorld(out _, out Vector2 centroid, out List<Vector3> outer, out _))
            return false;
        int n = outer != null ? outer.Count : 0;
        if (n < 3)
            return false;

        points = new Vector3[n * 2 + 1];
        for (int i = 0; i < n; i++)
        {
            Vector3 a = outer[i];
            Vector3 b = outer[(i + 1) % n];
            points[i * 2] = new Vector3(a.x, yWorld, a.z);
            points[i * 2 + 1] = new Vector3((a.x + b.x) * 0.5f, yWorld, (a.z + b.z) * 0.5f);
        }

        points[points.Length - 1] = new Vector3(centroid.x, yWorld, centroid.y);
        return true;
    }

    void OnValidate() => ClampYOffsetAboveWallTopInPlace();

    /// <summary>Borne <see cref="yOffsetAboveWallTop"/> entre 0 et <see cref="MaxYOffsetAboveWallTopMeters"/>.</summary>
    public void ClampYOffsetAboveWallTopInPlace() =>
        yOffsetAboveWallTop = Mathf.Clamp(yOffsetAboveWallTop, 0f, MaxYOffsetAboveWallTopMeters);

    public void RebuildNow()
    {
        EnsureComponents();
        ClampYOffsetAboveWallTopInPlace();
        MigrateLegacyLateralAnchorsToListIfNeeded();
        SyncLegacyLateralFieldsFromList();
        if (lateralApexOffsetsXZ != null && lateralApexOffsetsXZ.Count >= MaxLateralApexPoints)
            roofPointCycleIsRemoving = true;

        if (_mf == null || _mr == null)
            return;

        WallObject wall = GetComponent<WallObject>();
        WallEditShape edit = GetComponent<WallEditShape>();
        if (wall == null || edit == null || !edit.IsClosedLoopPath)
        {
            ClearMesh();
            return;
        }

        if (!TryComputeFootprintBaseCornersWorld(out float baseY, out Vector2 centroid, out List<Vector3> baseCorners, out List<Vector3> wallCorners))
        {
            ClearMesh();
            return;
        }

        float h = roofHeightMeters;
        Vector2 centroidXZ = centroid;
        float apexYTop = baseY + h;

        if (!useDomeProfile && useLateralFaceSystem)
        {
            if (TryRebuildLateralFaceRoofMesh(centroidXZ, baseY, apexYTop, baseCorners, wallCorners))
                return;
        }

        const int LegacyIluEdgeSteps = 2;
        int iluEdgeSteps = preserveControlPointDrivenTopology ? 1 : LegacyIluEdgeSteps;
        // Pipeline unique : sommet central + option dôme (arrondi)
        int edgeSubdivisions = baseCorners.Count == 4 ? iluEdgeSteps : 1;
        if (useDomeProfile && !preserveControlPointDrivenTopology)
            edgeSubdivisions = Mathf.Max(edgeSubdivisions, 3);

        List<Vector3> baseRingGen = BuildSubdividedClosedRing(baseCorners, edgeSubdivisions);
        List<Vector3> wallRingGen = BuildSubdividedClosedRing(wallCorners, edgeSubdivisions);
        int n = baseRingGen.Count;

        const int domeRadialBandsLow = 6;
        int ringCount = useDomeProfile ? domeRadialBandsLow : 2;

        var verts = new List<Vector3>(ringCount * n + 4 + n);
        var uvs = new List<Vector2>(ringCount * n + 4 + n);
        var roofTris = new List<int>(n * (ringCount * 12 + 20));
        var connectorTris = new List<int>(n * 24);

        for (int r = 0; r < ringCount; r++)
        {
            float alpha;
            float profileY;
            if (!useDomeProfile)
            {
                alpha = ringCount > 1 ? r / (float)(ringCount - 1) : 1f;
                profileY = alpha;
            }
            else
            {
                // Ensure the last dome ring reaches the summit neighborhood (t=1),
                // otherwise apex offset looks like a global lateral drift.
                float t = ringCount > 1 ? r / (float)(ringCount - 1) : 1f;
                alpha = preserveControlPointDrivenTopology
                    ? t
                    : 1f - Mathf.Pow(1f - t, 1.65f);
                profileY = EvaluateDomeProfile(alpha, Mathf.Clamp01(roundness));
            }

            float y = baseY + h * profileY;
            for (int i = 0; i < n; i++)
            {
                Vector3 b = baseRingGen[i];
                    float x = Mathf.Lerp(b.x, centroidXZ.x, alpha);
                    float z = Mathf.Lerp(b.z, centroidXZ.y, alpha);
                verts.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x * 0.2f, z * 0.2f));
            }
        }

        int apexCentralIdx = -1;
        for (int r = 0; r < ringCount - 1; r++)
        {
            int row0 = r * n;
            int row1 = (r + 1) * n;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a0 = row0 + i;
                int a1 = row0 + j;
                int b0 = row1 + i;
                int b1 = row1 + j;
                roofTris.Add(a0); roofTris.Add(b0); roofTris.Add(b1);
                roofTris.Add(a0); roofTris.Add(b1); roofTris.Add(a1);
            }
        }

        int lastRow = (ringCount - 1) * n;

        apexCentralIdx = verts.Count;
        verts.Add(new Vector3(centroid.x, apexYTop, centroid.y));
        uvs.Add(new Vector2(centroid.x * 0.2f, centroid.y * 0.2f));

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            roofTris.Add(lastRow + i); roofTris.Add(apexCentralIdx); roofTris.Add(lastRow + j);
        }

        _lastRoofShellTriangleCountForExperimental = roofTris.Count / 3;

        if (manualRoofShellCrossCut)
            TryApplyManualRoofShellCrossCut(verts, uvs, roofTris);

        if (experimentalCutRawProblemTriangles)
            ApplyExperimentalRoofShellTriangleCuts(verts, uvs, roofTris, centroidXZ);

        AddThickInteriorAndEaveConnector(verts, uvs, roofTris, connectorTris, baseRingGen, wallRingGen, Mathf.Max(0.02f, roofThicknessMeters));

        FinalizeRoofMesh(verts, uvs, roofTris, connectorTris);
    }

    /// <summary>4 slots max : remplis depuis <see cref="lateralApexOffsetsXZ"/> dans <see cref="TryRebuildLateralFaceRoofMesh"/>.</summary>
    struct ResolvedLateralAnchorSlot
    {
        public bool use;
        public int edgePrev;
        public int edgeMain;
        public int edgeNext;
        public Vector3 anchorWorld;
    }

    readonly struct PendingStructuralQuadDissolve
    {
        public readonly int anchorIdx;
        public readonly int centerIdx;
        public readonly int b0;
        public readonly int b1;

        public PendingStructuralQuadDissolve(int anchorIdx, int centerIdx, int b0, int b1)
        {
            this.anchorIdx = anchorIdx;
            this.centerIdx = centerIdx;
            this.b0 = b0;
            this.b1 = b1;
        }
    }

    void ApplyPendingStructuralQuadDissolves(List<Vector3> verts, List<int> roofTris, List<PendingStructuralQuadDissolve> pending)
    {
        if (pending == null || pending.Count == 0 || verts == null || roofTris == null)
            return;

        float eps = Mathf.Max(0f, lateralExtensionStructuralMergeVertexEpsilonMeters);
        for (int i = 0; i < pending.Count; i++)
        {
            PendingStructuralQuadDissolve p = pending[i];
            TrySwapStructuralTrianglesOffCenterAnchorEdge(
                roofTris, verts,
                p.anchorIdx, p.centerIdx, p.b0, p.b1,
                eps);
        }
    }

    bool TryRebuildLateralFaceRoofMesh(
        Vector2 centroidXZ,
        float baseY,
        float apexYTop,
        List<Vector3> baseCorners,
        List<Vector3> wallCorners)
    {
        int n = baseCorners != null ? baseCorners.Count : 0;
        if (n < 3 || wallCorners == null || wallCorners.Count != n)
            return false;

        if (lateralApexOffsetsXZ == null)
            lateralApexOffsetsXZ = new List<Vector2>();

        EnsureLateralFaceOffsetArray(n);
        float clampRadius = ComputeFootprintClampRadius(baseCorners, centroidXZ);
        var anchorSlots = new ResolvedLateralAnchorSlot[4];
        int slotCount = Mathf.Min(MaxLateralApexPoints, lateralApexOffsetsXZ != null ? lateralApexOffsetsXZ.Count : 0);
        for (int si = 0; si < slotCount; si++)
        {
            Vector2 off = lateralApexOffsetsXZ[si];
            if (off.sqrMagnitude <= 1e-8f)
                continue;
            if (!TryResolveThreeFaceEdgesFromAnchor(
                    baseCorners, centroidXZ, off, clampRadius,
                    out int ep, out int em, out int en, out Vector2 clampedOff))
                continue;

            anchorSlots[si] = new ResolvedLateralAnchorSlot
            {
                use = true,
                edgePrev = ep,
                edgeMain = em,
                edgeNext = en,
                anchorWorld = GetLockedLateralApexWorld(clampedOff),
            };
        }

        if (debugRoofLevelCoplanarityDiagnostic)
            LogRoofLevelCoplanarityDiagnostics(anchorSlots, slotCount, baseCorners, centroidXZ, baseY, apexYTop);

        var verts = new List<Vector3>(n * 4 + 8);
        var uvs = new List<Vector2>(verts.Capacity);
        var roofTris = new List<int>(n * 12);
        var connectorTris = new List<int>(n * 24);
        List<PendingStructuralQuadDissolve> pendingStructuralQuadDissolves =
            lateralExtensionStructuralQuadAlongBaseEdge ? new List<PendingStructuralQuadDissolve>(4) : null;
        if (!TryAppendLateralFaceRoofShell(
                verts, uvs, roofTris, baseCorners, baseY, apexYTop, centroidXZ, lateralFaceOffsetsXZ, clampRadius,
                anchorSlots,
                lateralExtensionStructuralQuadAlongBaseEdge,
                pendingStructuralQuadDissolves,
                out bool[] connectorAllowedByEdge))
            return false;

        _lastRoofShellTriangleCountForExperimental = roofTris.Count / 3;

        bool dualCornerAnchorMode = anchorSlots[0].use && anchorSlots[1].use &&
                                    IsAnchorOnBaseCornerXZ(baseCorners, anchorSlots[0].edgeMain, anchorSlots[0].anchorWorld) &&
                                    IsAnchorOnBaseCornerXZ(baseCorners, anchorSlots[1].edgeMain, anchorSlots[1].anchorWorld);

        if (manualRoofShellCrossCut)
            TryApplyManualRoofShellCrossCut(verts, uvs, roofTris);

        if (applyRoofShellCrossLocalFix)
            TryApplyRoofShellCrossLocalFix(verts, uvs, roofTris, centroidXZ);

        ApplyPendingStructuralQuadDissolves(verts, roofTris, pendingStructuralQuadDissolves);

        if (experimentalCutRawProblemTriangles)
            ApplyExperimentalRoofShellTriangleCuts(verts, uvs, roofTris, centroidXZ);

        if (debugDetectRoofShellCrossingDetailed || debugDetectRoofTriangleIntersections)
            DebugDetectRoofShellCrossingProblemsDetailed(verts, roofTris, centroidXZ, baseCorners, dualCornerAnchorMode);

        float thickness = Mathf.Max(0.02f, roofThicknessMeters);
        AddRoofOnlyThickness(verts, uvs, roofTris, thickness);
        List<Vector3> baseRing = BuildSubdividedClosedRing(baseCorners, 1);
        List<Vector3> wallRing = BuildSubdividedClosedRing(wallCorners, 1);
        AddEaveConnectorOnly(verts, uvs, connectorTris, baseRing, wallRing, connectorAllowedByEdge, thickness, enableVerboseRoofLogs);
        FinalizeRoofMesh(verts, uvs, roofTris, connectorTris);
        return true;
    }

    void LogRoofLevelCoplanarityDiagnostics(
        ResolvedLateralAnchorSlot[] anchorSlots,
        int slotCount,
        List<Vector3> baseCorners,
        Vector2 centroidXZ,
        float baseY,
        float apexYTop)
    {
        if (anchorSlots == null || baseCorners == null)
            return;
        int n = baseCorners.Count;
        if (n < 3)
            return;

        float buildingScale = 1f;
        var wbc = GetComponent<WallBuildController>();
        if (wbc != null)
            buildingScale = Mathf.Max(0.01f, wbc.GetEffectiveBuildingScale());

        Vector3 refApexWorld = new Vector3(centroidXZ.x, apexYTop, centroidXZ.y);

        for (int si = 0; si < slotCount && si < anchorSlots.Length; si++)
        {
            if (!anchorSlots[si].use)
                continue;

            int em = anchorSlots[si].edgeMain;
            if (em < 0 || em >= n)
                continue;

            Vector3 anchor = anchorSlots[si].anchorWorld;
            float ax = anchor.x;
            float az = anchor.z;

            Vector3 b0 = baseCorners[em];
            Vector3 b1 = baseCorners[(em + 1) % n];
            bool planeOk = TryVerticalLineIntersectRoofFacePlane(b0, b1, refApexWorld, ax, az, out float yMainPlane);
            if (!planeOk)
                yMainPlane = apexYTop;

            float yFlatLegacy = apexYTop;
            float extY = anchor.y;
            float deltaY = extY - yMainPlane;

            Vector3 e1 = b1 - b0;
            Vector3 e2 = refApexWorld - b0;
            Vector3 nMain = Vector3.Cross(e1, e2);
            if (nMain.sqrMagnitude > 1e-20f)
                nMain.Normalize();

            Vector3 nExt = Vector3.Cross(refApexWorld - anchor, b0 - anchor);
            if (nExt.sqrMagnitude > 1e-20f)
                nExt.Normalize();

            float ndot = Vector3.Dot(nMain, nExt);
            var inv = CultureInfo.InvariantCulture;

            Debug.Log(
                $"[RoofLevelDiagnostic] slot={si.ToString(inv)} edgeMain={em.ToString(inv)} " +
                $"main face vertex y = {yMainPlane.ToString("F5", inv)} (roof plane at anchor xz; flat apexYTop would be {yFlatLegacy.ToString("F5", inv)})",
                this);
            Debug.Log(
                $"[RoofLevelDiagnostic] extension face vertex y = {extY.ToString("F5", inv)}",
                this);
            Debug.Log(
                $"[RoofLevelDiagnostic] deltaY = {deltaY.ToString("F5", inv)}",
                this);
            Debug.Log(
                $"[RoofLevelDiagnostic] main normal = ({nMain.x.ToString("F5", inv)},{nMain.y.ToString("F5", inv)},{nMain.z.ToString("F5", inv)})",
                this);
            Debug.Log(
                $"[RoofLevelDiagnostic] extension normal (anchor,refApex,b0) = ({nExt.x.ToString("F5", inv)},{nExt.y.ToString("F5", inv)},{nExt.z.ToString("F5", inv)})",
                this);
            Debug.Log(
                $"[RoofLevelDiagnostic] normal dot = {ndot.ToString("F5", inv)}",
                this);
            Debug.Log(
                $"[RoofLevelDiagnostic] buildingScale = {buildingScale.ToString("F5", inv)}",
                this);
        }
    }

    static float FootprintMinEdgeLengthSqXZ(List<Vector3> baseCorners)
    {
        int n = baseCorners != null ? baseCorners.Count : 0;
        if (n < 2)
            return 1f;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = baseCorners[i];
            Vector3 b = baseCorners[(i + 1) % n];
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            float lenSq = dx * dx + dz * dz;
            if (lenSq < best)
                best = lenSq;
        }
        return best >= float.MaxValue * 0.5f ? 1f : best;
    }

    static bool TryResolveThreeFaceEdgesFromAnchor(
        List<Vector3> baseCorners,
        Vector2 centroidXZ,
        Vector2 anchorOffsetXZ,
        float clampRadius,
        out int edgePrev,
        out int edgeMain,
        out int edgeNext,
        out Vector2 clampedOffsetXZ)
    {
        edgePrev = edgeMain = edgeNext = -1;
        clampedOffsetXZ = Vector2.zero;
        int n = baseCorners != null ? baseCorners.Count : 0;
        if (n < 3)
            return false;

        clampedOffsetXZ = Vector2.ClampMagnitude(anchorOffsetXZ, clampRadius);
        if (clampedOffsetXZ.sqrMagnitude < 1e-10f)
            return false;

        Vector2 anchorWorld = centroidXZ + clampedOffsetXZ;
        float minEdgeLenSq = FootprintMinEdgeLengthSqXZ(baseCorners);
        float cornerEpsSq = Mathf.Max(1e-6f, minEdgeLenSq * (0.11f * 0.11f));

        // Corner snap: grid point on a footprint vertex. Midpoint-based edge pick is ambiguous here.
        int nearestCorner = -1;
        float bestCornerSq = float.MaxValue;
        for (int k = 0; k < n; k++)
        {
            Vector3 c = baseCorners[k];
            float dx = anchorWorld.x - c.x;
            float dz = anchorWorld.y - c.z;
            float dSq = dx * dx + dz * dz;
            if (dSq < bestCornerSq)
            {
                bestCornerSq = dSq;
                nearestCorner = k;
            }
        }

        if (nearestCorner >= 0 && bestCornerSq <= cornerEpsSq)
        {
            // Edge leaving this corner: V_k -> V_{k+1}. Controls faces k-1, k, k+1 (both pans au coin + voisin).
            edgeMain = nearestCorner;
            edgePrev = (nearestCorner + n - 1) % n;
            edgeNext = (nearestCorner + 1) % n;
            // Snap exactement sur le coin empreinte : le clamp global (rayon 0.95×max) tirait le point vers l'intérieur.
            Vector3 corner = baseCorners[nearestCorner];
            clampedOffsetXZ = new Vector2(corner.x - centroidXZ.x, corner.z - centroidXZ.y);
            return true;
        }

        int nearestEdge = 0;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = baseCorners[i];
            Vector3 b = baseCorners[(i + 1) % n];
            Vector2 mid = new Vector2((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f);
            float d = (mid - anchorWorld).sqrMagnitude;
            if (d < bestSq - 1e-12f)
            {
                bestSq = d;
                nearestEdge = i;
            }
            else if (Mathf.Abs(d - bestSq) <= 1e-12f && i < nearestEdge)
            {
                // Deterministic tie-break (e.g. rectangle centre-line equidistant from two mids).
                nearestEdge = i;
            }
        }

        edgeMain = nearestEdge;
        edgePrev = (nearestEdge + n - 1) % n;
        edgeNext = (nearestEdge + 1) % n;
        return true;
    }

    static Vector3 ComputeLateralFaceApexWorld(
        Vector3 b0,
        Vector3 b1,
        Vector3 refApexWorld,
        Vector2 centroidXZ,
        Vector2 lateralOffsetXZ,
        float baseY,
        float clampRadius,
        out Vector2 appliedOffsetXZ)
    {
        appliedOffsetXZ = Vector2.ClampMagnitude(lateralOffsetXZ, clampRadius);
        float x = centroidXZ.x + appliedOffsetXZ.x;
        float z = centroidXZ.y + appliedOffsetXZ.y;
        TryVerticalLineIntersectRoofFacePlane(b0, b1, refApexWorld, x, z, out float y);
        float yMax = refApexWorld.y + Mathf.Max(refApexWorld.y - baseY, 0.01f) * 2.5f;
        y = Mathf.Clamp(y, baseY - 0.02f, yMax);
        return new Vector3(x, y, z);
    }

    static bool TryVerticalLineIntersectRoofFacePlane(Vector3 b0, Vector3 b1, Vector3 refApex, float x, float z, out float y)
    {
        Vector3 e1 = b1 - b0;
        Vector3 e2 = refApex - b0;
        Vector3 n = Vector3.Cross(e1, e2);
        float nm = n.magnitude;
        if (nm < 1e-12f)
        {
            y = refApex.y;
            return false;
        }

        n /= nm;
        float rhs = Vector3.Dot(n, b0);
        if (Mathf.Abs(n.y) < 1e-5f)
        {
            y = refApex.y;
            return false;
        }

        y = (rhs - n.x * x - n.z * z) / n.y;
        return true;
    }

    /// <summary>
    /// Sommet d’ancrage d’extension sur le même plan que le pan <paramref name="edgeMain"/> (arête base → refApex),
    /// pour éviter une hauteur plate <c>apexYTop</c> incohérente avec <see cref="ComputeLateralFaceApexWorld"/>.
    /// </summary>
    static Vector3 ComputeLateralAnchorWorldOnMainFacePlane(
        List<Vector3> baseCorners,
        Vector2 centroidXZ,
        Vector2 clampedOffsetXZ,
        float baseY,
        float apexYTop,
        int edgeMain)
    {
        int n = baseCorners != null ? baseCorners.Count : 0;
        float ax = centroidXZ.x + clampedOffsetXZ.x;
        float az = centroidXZ.y + clampedOffsetXZ.y;
        if (n < 3 || edgeMain < 0 || edgeMain >= n)
            return new Vector3(ax, apexYTop, az);

        Vector3 refApexWorld = new Vector3(centroidXZ.x, apexYTop, centroidXZ.y);
        Vector3 b0 = baseCorners[edgeMain];
        Vector3 b1 = baseCorners[(edgeMain + 1) % n];
        if (!TryVerticalLineIntersectRoofFacePlane(b0, b1, refApexWorld, ax, az, out float y))
            return new Vector3(ax, apexYTop, az);

        float yMax = refApexWorld.y + Mathf.Max(refApexWorld.y - baseY, 0.01f) * 2.5f;
        y = Mathf.Clamp(y, baseY - 0.02f, yMax);
        return new Vector3(ax, y, az);
    }

    static bool IsAnchorOnBaseCornerXZ(List<Vector3> baseCorners, int cornerIndex, Vector3 anchorWorld)
    {
        int n = baseCorners != null ? baseCorners.Count : 0;
        if (n < 3 || cornerIndex < 0 || cornerIndex >= n)
            return false;

        float minEdgeLenSq = FootprintMinEdgeLengthSqXZ(baseCorners);
        float cornerEpsSq = Mathf.Max(1e-6f, minEdgeLenSq * (0.11f * 0.11f));
        Vector3 c = baseCorners[cornerIndex];
        float dx = anchorWorld.x - c.x;
        float dz = anchorWorld.z - c.z;
        return dx * dx + dz * dz <= cornerEpsSq;
    }

    /// <summary>
    /// Toit à pans latéraux : jusqu’à 4 ancrages latéraux via <paramref name="anchors"/> (longueur 4).
    /// </summary>
    static bool TryAppendLateralFaceRoofShell(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        List<Vector3> baseCorners,
        float baseY,
        float apexYTop,
        Vector2 centroidXZ,
        Vector2[] lateralOffsets,
        float clampRadius,
        ResolvedLateralAnchorSlot[] anchors,
        bool structuralQuadAlongBaseEdge,
        List<PendingStructuralQuadDissolve> structuralQuadDissolvesOut,
        out bool[] connectorAllowedByEdge)
    {
        int n = baseCorners != null ? baseCorners.Count : 0;
        connectorAllowedByEdge = null;
        if (n < 3 || lateralOffsets == null || lateralOffsets.Length < n || anchors == null || anchors.Length != 4)
            return false;
        connectorAllowedByEdge = new bool[n];

        Vector3 refApex = new Vector3(centroidXZ.x, apexYTop, centroidXZ.y);
        var apexPts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 b0 = baseCorners[i];
            Vector3 b1 = baseCorners[(i + 1) % n];
            apexPts[i] = ComputeLateralFaceApexWorld(b0, b1, refApex, centroidXZ, lateralOffsets[i], baseY, clampRadius, out _);
        }

        int baseStart = verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = baseCorners[i];
            verts.Add(new Vector3(p.x, baseY, p.z));
            uvs.Add(UvXZ(verts[baseStart + i]));
        }

        int apexStart = verts.Count;
        for (int i = 0; i < n; i++)
        {
            verts.Add(apexPts[i]);
            uvs.Add(UvXZ(apexPts[i]));
        }

        var anchorIdx = new int[4];
        var anchorPrevIdx = new int[4];
        var anchorNextIdx = new int[4];
        var anchorMainTop = new int[4];
        var anchorPrevTop = new int[4];
        var anchorNextTop = new int[4];
        var diagonalAnchor = new bool[4];
        for (int a = 0; a < 4; a++)
        {
            anchorIdx[a] = anchorPrevIdx[a] = anchorNextIdx[a] = -1;
            anchorMainTop[a] = anchorPrevTop[a] = anchorNextTop[a] = -1;
            diagonalAnchor[a] = false;
        }

        for (int a = 0; a < 4; a++)
        {
            if (!anchors[a].use)
                continue;
            diagonalAnchor[a] = IsAnchorOnBaseCornerXZ(baseCorners, anchors[a].edgeMain, anchors[a].anchorWorld);
            int ep = anchors[a].edgePrev;
            int em = anchors[a].edgeMain;
            int en = anchors[a].edgeNext;

            anchorIdx[a] = verts.Count;
            verts.Add(anchors[a].anchorWorld);
            uvs.Add(UvXZ(anchors[a].anchorWorld));

            if (ep >= 0 && ep < n && ep != em)
            {
                Vector3 p = apexPts[ep];
                anchorPrevIdx[a] = verts.Count;
                verts.Add(p);
                uvs.Add(UvXZ(p));
            }

            if (en >= 0 && en < n && en != em)
            {
                Vector3 p = apexPts[en];
                anchorNextIdx[a] = verts.Count;
                verts.Add(p);
                uvs.Add(UvXZ(p));
            }

            anchorMainTop[a] = anchorIdx[a];
            anchorPrevTop[a] = anchorPrevIdx[a] >= 0 ? anchorPrevIdx[a] : anchorIdx[a];
            anchorNextTop[a] = anchorNextIdx[a] >= 0 ? anchorNextIdx[a] : anchorIdx[a];
        }

        var faceTopIdx = new int[n];
        for (int i = 0; i < n; i++)
            faceTopIdx[i] = apexStart + i;

        for (int a = 0; a < 4; a++)
        {
            if (!anchors[a].use)
                continue;
            int ep = anchors[a].edgePrev;
            int em = anchors[a].edgeMain;
            int en = anchors[a].edgeNext;
            faceTopIdx[ep] = diagonalAnchor[a] ? anchorMainTop[a] : anchorPrevTop[a];
            faceTopIdx[em] = anchorMainTop[a];
            if (!diagonalAnchor[a])
                faceTopIdx[en] = anchorNextTop[a];
        }

        for (int a = 0; a < 4; a++)
        {
            if (!anchors[a].use)
                continue;
            if (diagonalAnchor[a])
                faceTopIdx[anchors[a].edgePrev] = anchorMainTop[a];
            faceTopIdx[anchors[a].edgeMain] = anchorMainTop[a];
        }

        bool warnedConnectivityReject = false;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int bi = baseStart + i;
            int bj = baseStart + j;
            int ai = faceTopIdx[i];
            if (!IsVerticalOrDegenerateYellowCenterRidgeTriangle(verts, bi, bj, ai))
            {
                connectorAllowedByEdge[i] = true;
                AppendRoofTriangleIfConnectivityAllowed(
                    verts, roofTris,
                    bi, bj, ai,
                    bi, bj,
                    bi, ai,
                    bj, ai,
                    "lateral face pan",
                    ref warnedConnectivityReject);
            }
        }

        bool anyAnchor = anchors[0].use || anchors[1].use || anchors[2].use || anchors[3].use;

        if (!anyAnchor)
        {
            for (int k = 0; k < n; k++)
            {
                int eLeft = (k + n - 1) % n;
                int eRight = k;
                int tL = faceTopIdx[eLeft];
                int tR = faceTopIdx[eRight];
                if (tL != tR && IsLateralCornerStitchAllowedMulti(eLeft, eRight, anchors))
                {
                    int bk = baseStart + k;
                    AppendRoofTriangleIfConnectivityAllowed(
                        verts, roofTris,
                        bk, tL, tR,
                        bk, tL,
                        bk, tR,
                        tL, tR,
                        "lateral face corner stitch",
                        ref warnedConnectivityReject);
                }
            }
        }
        else
        {
            int centerIdx = verts.Count;
            verts.Add(refApex);
            uvs.Add(UvXZ(refApex));

            for (int a = 0; a < 4; a++)
            {
                if (!anchors[a].use)
                    continue;
                if (diagonalAnchor[a])
                    AppendYellowTwoEdgeStructuralTriangles(verts, roofTris, baseStart, n, anchors[a].edgePrev, anchors[a].edgeMain, anchorIdx[a], centerIdx);
                else
                    AppendYellowMainEdgeStructuralTriangles(verts, roofTris, baseStart, n, anchors[a].edgeMain, anchorIdx[a], centerIdx);
            }

            if (structuralQuadAlongBaseEdge && structuralQuadDissolvesOut != null)
            {
                for (int a = 0; a < 4; a++)
                {
                    if (!anchors[a].use || diagonalAnchor[a])
                        continue;

                    int em = anchors[a].edgeMain;
                    int b0 = baseStart + em;
                    int b1 = baseStart + ((em + 1) % n);
                    structuralQuadDissolvesOut.Add(new PendingStructuralQuadDissolve(
                        anchorIdx[a], centerIdx, b0, b1));
                }
            }
        }

        if (!anyAnchor)
            AppendLateralRoofTopCapSafe(verts, roofTris, faceTopIdx, n, centroidXZ);

        return true;
    }

    static bool IsLateralCornerStitchAllowedMulti(int eLeft, int eRight, ResolvedLateralAnchorSlot[] anchors)
    {
        if (anchors == null || anchors.Length != 4)
            return true;
        bool any = false;
        for (int i = 0; i < 4; i++)
        {
            if (anchors[i].use)
                any = true;
        }

        if (!any)
            return true;
        for (int i = 0; i < 4; i++)
        {
            if (!anchors[i].use)
                continue;
            if (IsLocalAnchorEdgePair(eLeft, eRight, true, anchors[i].edgePrev, anchors[i].edgeMain, anchors[i].edgeNext))
                return true;
        }

        return false;
    }

    public bool TryGetLateralApexWorld(out Vector3 world) => TryGetLateralApexWorldAtIndex(0, out world);

    public bool TryGetSecondLateralApexWorld(out Vector3 world) => TryGetLateralApexWorldAtIndex(1, out world);

    /// <summary>
    /// Position monde d’un apex latéral : XZ depuis <paramref name="offsetXZ"/> relatif au centroïde d’empreinte
    /// (<see cref="TryComputeFootprintBaseCornersWorld"/>), Y = sommet central jaune (même source que l’overlay et le mesh).
    /// </summary>
    Vector3 GetLockedLateralApexWorld(Vector2 offsetXZ)
    {
        if (!TryComputeFootprintBaseCornersWorld(out float baseY, out Vector2 centroidXZ, out _, out _))
            return default;
        float centralY = baseY + roofHeightMeters;
        Vector3 w = new Vector3(centroidXZ.x + offsetXZ.x, centralY, centroidXZ.y + offsetXZ.y);
        if (logRoofApexHeightLock)
        {
            var inv = CultureInfo.InvariantCulture;
            Debug.Log($"[RoofApexHeightLock] handleWorld={w.ToString("F4", inv)}", this);
            Debug.Log($"[RoofApexHeightLock] meshWorld={w.ToString("F4", inv)}", this);
            Debug.Log("[RoofApexHeightLock] deltaHandleToMesh=0", this);
        }

        return w;
    }

    public bool TryGetLateralApexWorldAtIndex(int lateralIndex, out Vector3 world)
    {
        world = default;
        if (!useLateralFaceSystem || useDomeProfile || lateralApexOffsetsXZ == null)
            return false;
        if (lateralIndex < 0 || lateralIndex >= Mathf.Min(MaxLateralApexPoints, lateralApexOffsetsXZ.Count))
            return false;
        Vector2 off = lateralApexOffsetsXZ[lateralIndex];
        if (off.sqrMagnitude <= 1e-8f)
            return false;
        if (!TryComputeFootprintBaseCornersWorld(out _, out Vector2 centroidXZ, out List<Vector3> baseCorners, out _))
            return false;
        int n = baseCorners != null ? baseCorners.Count : 0;
        if (n < 3)
            return false;

        float clampRadius = ComputeFootprintClampRadius(baseCorners, centroidXZ);
        if (!TryResolveThreeFaceEdgesFromAnchor(
                baseCorners, centroidXZ, off, clampRadius,
                out _, out _, out _, out Vector2 clampedAnchorOffset))
            return false;
        world = GetLockedLateralApexWorld(clampedAnchorOffset);
        return true;
    }

    void FinalizeRoofMesh(List<Vector3> verts, List<Vector2> uvs, List<int> roofTris, List<int> connectorTris)
    {
        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uvs);
        _mesh.subMeshCount = 2;
        _mesh.SetTriangles(roofTris, 0);
        _mesh.SetTriangles(connectorTris, 1);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        _mf.sharedMesh = _mesh;
        _mr.enabled = true;

        if (_mf != null)
            UpdateRoofPickCollider(_mf.gameObject);

        _lastHash = ComputeHash();
    }

    static void AddThickInteriorAndEaveConnector(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        List<int> connectorTris,
        List<Vector3> outerBaseRing,
        List<Vector3> wallRing,
        float thickness)
    {
        int frontVertexCount = verts.Count;
        int frontTriangleIndexCount = roofTris.Count;
        Vector3 down = Vector3.down * thickness;

        for (int i = 0; i < frontVertexCount; i++)
        {
            verts.Add(verts[i] + down);
            uvs.Add(uvs[i]);
        }

        for (int i = 0; i < frontTriangleIndexCount; i += 3)
        {
            roofTris.Add(frontVertexCount + roofTris[i + 2]);
            roofTris.Add(frontVertexCount + roofTris[i + 1]);
            roofTris.Add(frontVertexCount + roofTris[i]);
        }

        int n = outerBaseRing != null ? outerBaseRing.Count : 0;
        if (n < 3 || wallRing == null || wallRing.Count != n)
            return;

        int lowerOuterStart = frontVertexCount; // first original ring starts at vertex 0.
        int wallTopStart = verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 wallTop = wallRing[i];
            Vector3 wallBottom = wallTop + down;
            verts.Add(wallTop);
            uvs.Add(new Vector2(wallTop.x * 0.2f, wallTop.z * 0.2f));
            verts.Add(wallBottom);
            uvs.Add(new Vector2(wallBottom.x * 0.2f, wallBottom.z * 0.2f));
        }

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int topOuterI = i;
            int topOuterJ = j;
            int bottomOuterI = lowerOuterStart + i;
            int bottomOuterJ = lowerOuterStart + j;
            int wallTopI = wallTopStart + i * 2;
            int wallTopJ = wallTopStart + j * 2;
            int wallBottomI = wallTopI + 1;
            int wallBottomJ = wallTopJ + 1;

            // Visible roof thickness around the exterior lip: same material as the roof.
            AddQuad(roofTris, topOuterI, topOuterJ, bottomOuterJ, bottomOuterI);

            // Plateau horizontal marron entre mur et débord (visible dessus et depuis l'intérieur sous la face).
            AddQuadTwoSidedDupVerts(verts, uvs, connectorTris,
                verts[wallTopI], verts[wallTopJ], verts[topOuterJ], verts[topOuterI]);

            // Soffit + cloison verticale au mur : géométrie dupliquée pour deux faces sans normales incohérentes.
            AddQuadTwoSidedDupVerts(verts, uvs, connectorTris,
                verts[bottomOuterI], verts[bottomOuterJ], verts[wallBottomJ], verts[wallBottomI]);

            AddQuadTwoSidedDupVerts(verts, uvs, connectorTris,
                verts[wallBottomI], verts[wallBottomJ], verts[wallTopJ], verts[wallTopI]);
        }
    }

    static void AddRoofOnlyThickness(List<Vector3> verts, List<Vector2> uvs, List<int> roofTris, float thickness)
    {
        int sourceTriangleIndexCount = roofTris != null ? roofTris.Count : 0;
        if (verts == null || uvs == null || sourceTriangleIndexCount < 3)
            return;

        Vector3 boundsCenter = Vector3.zero;
        for (int i = 0; i < verts.Count; i++)
            boundsCenter += verts[i];
        boundsCenter /= Mathf.Max(1, verts.Count);

        var usedToLower = new Dictionary<int, int>();
        var edgeUse = new Dictionary<ulong, int>(sourceTriangleIndexCount);
        for (int i = 0; i < sourceTriangleIndexCount; i += 3)
        {
            int a = roofTris[i];
            int b = roofTris[i + 1];
            int c = roofTris[i + 2];
            usedToLower[a] = -1;
            usedToLower[b] = -1;
            usedToLower[c] = -1;
            CountUndirectedRoofEdge(edgeUse, a, b);
            CountUndirectedRoofEdge(edgeUse, b, c);
            CountUndirectedRoofEdge(edgeUse, c, a);
        }

        Vector3 down = Vector3.down * thickness;
        var used = new List<int>(usedToLower.Keys);
        for (int i = 0; i < used.Count; i++)
        {
            int src = used[i];
            int lower = verts.Count;
            verts.Add(verts[src] + down);
            uvs.Add(uvs[src]);
            usedToLower[src] = lower;
        }

        for (int i = 0; i < sourceTriangleIndexCount; i += 3)
        {
            roofTris.Add(usedToLower[roofTris[i + 2]]);
            roofTris.Add(usedToLower[roofTris[i + 1]]);
            roofTris.Add(usedToLower[roofTris[i]]);
        }

        for (int i = 0; i < sourceTriangleIndexCount; i += 3)
        {
            AddRoofThicknessSideIfBoundary(edgeUse, usedToLower, roofTris[i], roofTris[i + 1], verts, uvs, roofTris, boundsCenter);
            AddRoofThicknessSideIfBoundary(edgeUse, usedToLower, roofTris[i + 1], roofTris[i + 2], verts, uvs, roofTris, boundsCenter);
            AddRoofThicknessSideIfBoundary(edgeUse, usedToLower, roofTris[i + 2], roofTris[i], verts, uvs, roofTris, boundsCenter);
        }
    }

    static void CountUndirectedRoofEdge(Dictionary<ulong, int> edgeUse, int a, int b)
    {
        ulong key = UndirectedRoofEdgeKey(a, b);
        edgeUse.TryGetValue(key, out int count);
        edgeUse[key] = count + 1;
    }

    static void AddRoofThicknessSideIfBoundary(
        Dictionary<ulong, int> edgeUse,
        Dictionary<int, int> usedToLower,
        int a,
        int b,
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        Vector3 boundsCenter)
    {
        if (!edgeUse.TryGetValue(UndirectedRoofEdgeKey(a, b), out int count) || count != 1)
            return;
        if (!usedToLower.TryGetValue(a, out int al) || !usedToLower.TryGetValue(b, out int bl))
            return;

        AddRoofThicknessQuadLocalUV(verts, uvs, roofTris, verts[a], verts[b], verts[bl], verts[al], boundsCenter);
    }

    static ulong UndirectedRoofEdgeKey(int a, int b)
    {
        uint lo = (uint)Mathf.Min(a, b);
        uint hi = (uint)Mathf.Max(a, b);
        return ((ulong)lo << 32) | hi;
    }

    static void AddRoofThicknessQuadLocalUV(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 boundsCenter)
    {
        int i0 = verts.Count;
        verts.Add(a);
        uvs.Add(new Vector2(0f, 0f));
        int i1 = verts.Count;
        verts.Add(b);
        uvs.Add(new Vector2(Mathf.Max(0.01f, Vector3.Distance(a, b) * 0.2f), 0f));
        int i2 = verts.Count;
        verts.Add(c);
        uvs.Add(new Vector2(Mathf.Max(0.01f, Vector3.Distance(a, b) * 0.2f), Mathf.Max(0.01f, Vector3.Distance(a, d) * 0.2f)));
        int i3 = verts.Count;
        verts.Add(d);
        uvs.Add(new Vector2(0f, Mathf.Max(0.01f, Vector3.Distance(a, d) * 0.2f)));

        Vector3 normal = Vector3.Cross(b - a, c - a);
        Vector3 quadCenter = (a + b + c + d) * 0.25f;
        Vector3 outward = quadCenter - boundsCenter;
        if (Vector3.Dot(normal, outward) >= 0f)
            AddQuad(roofTris, i0, i1, i2, i3);
        else
            AddQuad(roofTris, i0, i3, i2, i1);
    }

    static void AddEaveConnectorOnly(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> connectorTris,
        List<Vector3> outerBaseRing,
        List<Vector3> wallRing,
        bool[] connectorAllowedByEdge,
        float thickness,
        bool verboseRoofLogs)
    {
        int n = outerBaseRing != null ? outerBaseRing.Count : 0;
        if (verts == null || uvs == null || connectorTris == null || n < 3 || wallRing == null || wallRing.Count != n)
            return;

        Vector3 down = Vector3.down * thickness;
        for (int i = 0; i < n; i++)
        {
            if (!ConnectorEdgeAllowed(connectorAllowedByEdge, i))
                continue;

            int j = (i + 1) % n;
            int prev = (i + n - 1) % n;
            bool capStart = !ConnectorEdgeAllowed(connectorAllowedByEdge, prev);
            bool capEnd = !ConnectorEdgeAllowed(connectorAllowedByEdge, j);
            Vector3 outerTopI = outerBaseRing[i];
            Vector3 outerTopJ = outerBaseRing[j];
            Vector3 outerBottomI = outerTopI + down;
            Vector3 outerBottomJ = outerTopJ + down;
            Vector3 wallTopI = wallRing[i];
            Vector3 wallTopJ = wallRing[j];
            Vector3 wallBottomI = wallTopI + down;
            Vector3 wallBottomJ = wallTopJ + down;

            // Keep only the useful brown/eave pieces: horizontal ledge, soffit, and inner vertical lip.
            // Do not add any gable/triangular fill between wall top and roof apex.
            AddQuadTwoSidedDupVertsLocalUV(verts, uvs, connectorTris, wallTopI, wallTopJ, outerTopJ, outerTopI);
            AddQuadTwoSidedDupVertsLocalUV(verts, uvs, connectorTris, outerBottomI, outerBottomJ, wallBottomJ, wallBottomI);
            AddQuadTwoSidedDupVertsLocalUV(verts, uvs, connectorTris, wallBottomI, wallBottomJ, wallTopJ, wallTopI);

            if (capStart)
            {
                if (verboseRoofLogs)
                    Debug.Log($"Connector end cap edge {i} START => ADD");
                AddQuadTwoSidedDupVertsLocalUV(verts, uvs, connectorTris, wallTopI, outerTopI, outerBottomI, wallBottomI);
            }

            if (capEnd)
            {
                if (verboseRoofLogs)
                    Debug.Log($"Connector end cap edge {i} END => ADD");
                AddQuadTwoSidedDupVertsLocalUV(verts, uvs, connectorTris, outerTopJ, wallTopJ, wallBottomJ, outerBottomJ);
            }
        }
    }

    static bool ConnectorEdgeAllowed(bool[] connectorAllowedByEdge, int edge)
    {
        if (connectorAllowedByEdge == null)
            return true;
        return edge >= 0 && edge < connectorAllowedByEdge.Length && connectorAllowedByEdge[edge];
    }

    static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    static Vector2 UvXZ(Vector3 v) => new Vector2(v.x * 0.2f, v.z * 0.2f);

    static void AddQuadTwoSidedDupVertsLocalUV(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        AddQuadDupVertsWithUV(verts, uvs, tris, a, b, c, d, false);
        AddQuadDupVertsWithUV(verts, uvs, tris, a, d, c, b, true);
    }

    static void AddQuadDupVertsWithUV(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        bool reversed)
    {
        float uLen = Mathf.Max(0.01f, Vector3.Distance(a, b) * 0.2f);
        float vLen = Mathf.Max(0.01f, Vector3.Distance(a, d) * 0.2f);

        int i0 = verts.Count;
        verts.Add(a);
        uvs.Add(new Vector2(0f, 0f));
        int i1 = verts.Count;
        verts.Add(b);
        uvs.Add(new Vector2(uLen, 0f));
        int i2 = verts.Count;
        verts.Add(c);
        uvs.Add(new Vector2(uLen, vLen));
        int i3 = verts.Count;
        verts.Add(d);
        uvs.Add(new Vector2(0f, vLen));

        if (reversed)
            AddQuad(tris, i0, i3, i2, i1);
        else
            AddQuad(tris, i0, i1, i2, i3);
    }

    /// <summary>Double face avec sommets dupliqués : évite les artefacts de normales liés aux triangles opposés sur les mêmes indices.</summary>
    static void AddQuadTwoSidedDupVerts(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        int i0 = verts.Count;
        verts.Add(a);
        uvs.Add(UvXZ(a));
        int i1 = verts.Count;
        verts.Add(b);
        uvs.Add(UvXZ(b));
        int i2 = verts.Count;
        verts.Add(c);
        uvs.Add(UvXZ(c));
        int i3 = verts.Count;
        verts.Add(d);
        uvs.Add(UvXZ(d));
        AddQuad(tris, i0, i1, i2, i3);

        int j0 = verts.Count;
        verts.Add(a);
        uvs.Add(UvXZ(a));
        int j1 = verts.Count;
        verts.Add(b);
        uvs.Add(UvXZ(b));
        int j2 = verts.Count;
        verts.Add(c);
        uvs.Add(UvXZ(c));
        int j3 = verts.Count;
        verts.Add(d);
        uvs.Add(UvXZ(d));
        AddQuad(tris, j0, j3, j2, j1);
    }

    void UpdateRoofPickCollider(GameObject roofGo)
    {
        if (roofGo == null)
            return;
        var bc = roofGo.GetComponent<BoxCollider>();
        if (bc == null)
            bc = roofGo.AddComponent<BoxCollider>();
        if (_mesh == null || _mesh.vertexCount == 0)
        {
            bc.enabled = false;
            return;
        }
        Bounds b = _mesh.bounds;
        bc.center = b.center;
        bc.size = Vector3.Max(b.size, new Vector3(0.08f, 0.08f, 0.08f));
        bc.enabled = true;
    }

    public void AdjustHeight(float delta)
    {
        roofHeightMeters = Mathf.Clamp(roofHeightMeters + delta, MinRoofHeightMeters, MaxRoofHeightMeters);
        RebuildNow();
    }

    public void AdjustRoundness(float delta)
    {
        if (Mathf.Abs(delta) > 1e-8f)
            useDomeProfile = true;
        roundness = Mathf.Clamp01(roundness + delta);
        RebuildNow();
    }

    /// <summary>Décale la base du toit verticalement au-dessus du mur (0 … <see cref="MaxYOffsetAboveWallTopMeters"/>).</summary>
    public void AdjustYOffsetAboveWallTop(float delta)
    {
        yOffsetAboveWallTop = Mathf.Clamp(yOffsetAboveWallTop + delta, 0f, MaxYOffsetAboveWallTopMeters);
        RebuildNow();
    }

    public void AdjustOverhang(float delta)
    {
        overhangMeters = Mathf.Clamp(overhangMeters + delta, MinOverhangMeters, MaxOverhangMeters);
        RebuildNow();
    }

    bool TryPrepareClosedRing(List<Vector3> path, out List<Vector3> ring)
    {
        ring = null;
        // Triangles et polygones fermés sans vertex de fermeture dupliqué : count == 3 est valide (< 4 ne doit pas bloquer).
        if (path == null || path.Count < 3)
            return false;
        ring = new List<Vector3>(path);
        if (Vector3.Distance(ring[0], ring[ring.Count - 1]) < 0.001f)
            ring.RemoveAt(ring.Count - 1);
        return ring.Count >= 3;
    }

    Vector2 ComputeCentroidXZ(List<Vector3> ring)
    {
        float sx = 0f, sz = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            sx += ring[i].x;
            sz += ring[i].z;
        }
        float inv = 1f / Mathf.Max(1, ring.Count);
        return new Vector2(sx * inv, sz * inv);
    }

    static List<Vector3> BuildSubdividedClosedRing(List<Vector3> corners, int subdivisionsPerEdge)
    {
        int count = corners != null ? corners.Count : 0;
        var ring = new List<Vector3>(Mathf.Max(0, count * Mathf.Max(1, subdivisionsPerEdge)));
        if (count < 3)
            return ring;

        int steps = Mathf.Max(1, subdivisionsPerEdge);
        for (int i = 0; i < count; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[(i + 1) % count];
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                ring.Add(Vector3.Lerp(a, b, t));
            }
        }

        return ring;
    }

    static Vector2 ProjectXZOntoSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSq = ab.sqrMagnitude;
        if (abSq < 1e-12f)
            return a;
        float t = Vector2.Dot(p - a, ab) / abSq;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    /// <summary>Plus proche point sur la ligne brisée A→B→C (plan XZ), pour le faîtage à trois sommets.</summary>
    static Vector2 ClosestOnBrokenRidgePolylineXZ(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 onAb = ProjectXZOntoSegment(p, a, b);
        Vector2 onBc = ProjectXZOntoSegment(p, b, c);
        return (p - onAb).sqrMagnitude <= (p - onBc).sqrMagnitude ? onAb : onBc;
    }

    /// <summary>
    /// Indices originaux : 0 = centroïde (jaune), 1 = meshAnchor, 2 = meshAnchor2.
    /// Les trois points sont ordonnés le long du faîtage réel (droite des deux ancres), pas dans l’ordre UI,
    /// sinon la ligne brisée fait un zigzag (centre → extrémité → autre extrémité) et le maillage se déchire.
    /// </summary>
    static void OrderTripleSummitAlongAnchorLineXZ(
        Vector2 centroidXZ,
        Vector2 meshAnchor,
        Vector2 meshAnchor2,
        out int sortedOrig0,
        out int sortedOrig1,
        out int sortedOrig2)
    {
        const float eps = 1e-10f;
        Vector2 e = meshAnchor2 - meshAnchor;
        float elSq = e.sqrMagnitude;

        float k0, k1, k2;
        // 0 = centroid, 1 = anchor, 2 = anchor2
        if (elSq >= eps)
        {
            float len = Mathf.Sqrt(elSq);
            Vector2 eu = e / len;
            k0 = Vector2.Dot(centroidXZ - meshAnchor, eu);
            k1 = 0f;
            k2 = len;
        }
        else
        {
            Vector2 dir = (meshAnchor + meshAnchor2) * 0.5f - centroidXZ;
            if (dir.sqrMagnitude < eps)
                dir = meshAnchor2 - centroidXZ;
            if (dir.sqrMagnitude < eps)
            {
                sortedOrig0 = 0;
                sortedOrig1 = 1;
                sortedOrig2 = 2;
                return;
            }

            dir.Normalize();
            k0 = 0f;
            k1 = Vector2.Dot(meshAnchor - centroidXZ, dir);
            k2 = Vector2.Dot(meshAnchor2 - centroidXZ, dir);
        }

        var keys = new List<(float k, int orig)>(3)
        {
            (k0, 0),
            (k1, 1),
            (k2, 2),
        };
        keys.Sort((a, b) =>
        {
            int c = a.k.CompareTo(b.k);
            return c != 0 ? c : a.orig.CompareTo(b.orig);
        });

        sortedOrig0 = keys[0].orig;
        sortedOrig1 = keys[1].orig;
        sortedOrig2 = keys[2].orig;
    }

    /// <summary>
    /// Pour chaque point du pourtour, indique vers quel « sommet » horizontal (centroïde ou ancrage mesh)
    /// la surface doit converger — même plan que <see cref="AppendDualSummitCap"/> (faîtage vertical).
    /// Appliqué à tous les anneaux radiaux pour que tout le volume suive la ligne de crête, pas seulement le chapeau.
    /// </summary>
    static Vector2 RidgeHorizontalTargetXZ(Vector2 pBaseXZ, Vector2 centroidXZ, Vector2 meshAnchorXZ)
    {
        Vector2 u = meshAnchorXZ - centroidXZ;
        float uLen = u.magnitude;
        if (uLen < 1e-8f)
            return centroidXZ;
        Vector2 perp = new Vector2(-u.y, u.x) / uLen;
        float s = Vector2.Dot(pBaseXZ - centroidXZ, perp);
        return s >= 0f ? centroidXZ : meshAnchorXZ;
    }

    /// <summary>
    /// Fermeture du haut : deux sommets au même niveau (centre + ancrage mesh), reliés par une arête de faîtage.
    /// Coupe l’anneau supérieur selon la position le long du segment faîtage (dot avec u), pas un plan perpendiculaire
    /// au centroïde — compatible avec une cible faîtage interpolée par arête de mur (quatre pans distincts).
    /// </summary>
    static void AppendDualSummitCap(
        List<int> roofTris,
        List<Vector3> verts,
        List<Vector2> uvs,
        int lastRowStart,
        int n,
        Vector2 centroidXZ,
        Vector2 meshAnchorXZ,
        int apexCentralIdx,
        int apexAnchorIdx)
    {
        Vector2 u = meshAnchorXZ - centroidXZ;
        float uLenSq = u.sqrMagnitude;
        if (uLenSq < 1e-12f)
            return;

        float midParam = 0.5f * uLenSq;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int viIdx = lastRowStart + i;
            int vjIdx = lastRowStart + j;
            Vector3 vi = verts[viIdx];
            Vector3 vj = verts[vjIdx];
            Vector2 pi = new Vector2(vi.x, vi.z);
            Vector2 pj = new Vector2(vj.x, vj.z);
            float dotI = Vector2.Dot(pi - centroidXZ, u);
            float dotJ = Vector2.Dot(pj - centroidXZ, u);
            float hi = dotI - midParam;
            float hj = dotJ - midParam;

            if (hi * hj >= 0f)
            {
                float hAvg = (hi + hj) * 0.5f;
                int ap = hAvg >= 0f ? apexAnchorIdx : apexCentralIdx;
                roofTris.Add(viIdx); roofTris.Add(ap); roofTris.Add(vjIdx);
                continue;
            }

            Vector2 seg = pj - pi;
            float denom = Vector2.Dot(seg, u);
            if (Mathf.Abs(denom) < 1e-8f)
            {
                float hAvg = (hi + hj) * 0.5f;
                int ap = hAvg >= 0f ? apexAnchorIdx : apexCentralIdx;
                roofTris.Add(viIdx); roofTris.Add(ap); roofTris.Add(vjIdx);
                continue;
            }

            float t = (midParam - dotI) / denom;
            t = Mathf.Clamp01(t);
            Vector2 q2 = pi + seg * t;
            float qy = Mathf.Lerp(vi.y, vj.y, t);
            int qIdx = verts.Count;
            verts.Add(new Vector3(q2.x, qy, q2.y));
            uvs.Add(new Vector2(q2.x * 0.2f, q2.y * 0.2f));

            int apexVi = hi >= 0f ? apexAnchorIdx : apexCentralIdx;
            int apexVj = hj >= 0f ? apexAnchorIdx : apexCentralIdx;

            roofTris.Add(viIdx); roofTris.Add(qIdx); roofTris.Add(apexVi);
            roofTris.Add(qIdx); roofTris.Add(vjIdx); roofTris.Add(apexVj);
            roofTris.Add(apexCentralIdx); roofTris.Add(apexAnchorIdx); roofTris.Add(qIdx);
        }
    }

    static int AppendSummitStitchRingVertex(List<Vector3> verts, List<Vector2> uvs, Vector2 xz, float y)
    {
        int idx = verts.Count;
        verts.Add(new Vector3(xz.x, y, xz.y));
        uvs.Add(new Vector2(xz.x * 0.2f, xz.y * 0.2f));
        return idx;
    }

    /// <returns>0 si le plus proche point de la ligne brisée tombe sur le segment [a0,a1], sinon 1 pour [a1,a2].</returns>
    static int RidgePolylineClosestSegmentIndex(Vector2 p, Vector2 a0, Vector2 a1, Vector2 a2)
    {
        Vector2 qAb = ProjectXZOntoSegment(p, a0, a1);
        Vector2 qBc = ProjectXZOntoSegment(p, a1, a2);
        float dAb = (p - qAb).sqrMagnitude;
        float dBc = (p - qBc).sqrMagnitude;
        float tie = 1e-6f * Mathf.Max(1f, Mathf.Min(dAb, dBc));
        if (dAb + tie < dBc)
            return 0;
        if (dBc + tie < dAb)
            return 1;
        return 0;
    }

    /// <summary>
    /// Toutes les coupures où le bras AB / BC le plus proche change le long de [pi,pj].
    /// </summary>
    static void AppendAllPolylineSegmentTransitionCutsXZ(
        Vector2 pi,
        Vector2 pj,
        Vector2 a0,
        Vector2 a1,
        Vector2 a2,
        List<float> cutsOut)
    {
        Vector2 edge = pj - pi;
        if (edge.sqrMagnitude < 1e-14f)
            return;

        const float stepEps = 2e-4f;
        float lo = 0f;
        while (lo < 1f - stepEps)
        {
            int sLo = RidgePolylineClosestSegmentIndex(pi + edge * lo, a0, a1, a2);
            int sEnd = RidgePolylineClosestSegmentIndex(pj, a0, a1, a2);
            float midT = (lo + 1f) * 0.5f;
            int sMid = RidgePolylineClosestSegmentIndex(pi + edge * midT, a0, a1, a2);

            if (sMid == sLo && sEnd == sLo)
                break;

            float bLo = lo;
            float bHi = 1f;
            for (int k = 0; k < 26; k++)
            {
                float t = (bLo + bHi) * 0.5f;
                Vector2 pm = pi + edge * t;
                int sm = RidgePolylineClosestSegmentIndex(pm, a0, a1, a2);
                if (sm == sLo)
                    bLo = t;
                else
                    bHi = t;
            }

            float uCut = (bLo + bHi) * 0.5f;
            if (uCut <= lo + stepEps || uCut >= 1f - stepEps)
                break;

            cutsOut.Add(uCut);
            lo = uCut + stepEps;
        }
    }

    static void AppendRoofCapTriangleWorldUp(List<Vector3> verts, List<int> roofTris, int i0, int i1, int i2)
    {
        Vector3 a = verts[i0];
        Vector3 b = verts[i1];
        Vector3 c = verts[i2];
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.y >= 0f)
        {
            roofTris.Add(i0);
            roofTris.Add(i1);
            roofTris.Add(i2);
        }
        else
        {
            roofTris.Add(i0);
            roofTris.Add(i2);
            roofTris.Add(i1);
        }
    }

    static void AppendRoofTriangleIfConnectivityAllowed(
        List<Vector3> verts,
        List<int> roofTris,
        int i0,
        int i1,
        int i2,
        int allowedA0,
        int allowedA1,
        int allowedB0,
        int allowedB1,
        int allowedC0,
        int allowedC1,
        string family,
        ref bool warnedReject)
    {
        bool distinct = i0 != i1 && i1 != i2 && i2 != i0;
        bool allowed =
            distinct &&
            IsRoofConnectivityEdgeAllowed(i0, i1, allowedA0, allowedA1, allowedB0, allowedB1, allowedC0, allowedC1) &&
            IsRoofConnectivityEdgeAllowed(i1, i2, allowedA0, allowedA1, allowedB0, allowedB1, allowedC0, allowedC1) &&
            IsRoofConnectivityEdgeAllowed(i2, i0, allowedA0, allowedA1, allowedB0, allowedB1, allowedC0, allowedC1);

        if (!allowed)
        {
            if (!warnedReject)
            {
                Debug.LogWarning($"HouseRoofSystem skipped a {family} triangle because its connectivity is not locally allowed.");
                warnedReject = true;
            }
            return;
        }

        AppendRoofCapTriangleWorldUp(verts, roofTris, i0, i1, i2);
    }

    static void AppendYellowCenterRidgeTriangles(
        List<Vector3> verts,
        List<int> roofTris,
        int baseStart,
        int n,
        int edgePrev,
        int edgeMain,
        int edgeNext,
        int anchorIdx,
        int centerIdx,
        string family,
        ref bool warnedReject)
    {
        if (n < 3 || edgeMain < 0 || edgeMain >= n || anchorIdx < 0 || centerIdx < 0)
            return;

        var localBase = new List<int>(6);
        AddLocalRidgeBaseCorners(localBase, baseStart, n, edgePrev);
        AddLocalRidgeBaseCorners(localBase, baseStart, n, edgeMain);
        AddLocalRidgeBaseCorners(localBase, baseStart, n, edgeNext);

        if (localBase.Count < 2)
            return;

        // Each triangle contains anchor->center, so the diagonal ridge becomes a shared edge for the local sides.
        // No high-high A_i->A_j edge is introduced here.
        for (int i = 0; i < localBase.Count; i++)
        {
            int b = localBase[i];
            bool isMainEdgeCorner = IsBaseCornerOnEdge(baseStart, n, b, edgeMain);
            if (i % 2 == 0)
            {
                AppendYellowCenterRidgeTriangleIfValidRoofSurface(
                    verts, roofTris,
                    anchorIdx, centerIdx, b,
                    anchorIdx, centerIdx,
                    anchorIdx, b,
                    centerIdx, b,
                    family,
                    isMainEdgeCorner,
                    ref warnedReject);
            }
            else
            {
                AppendYellowCenterRidgeTriangleIfValidRoofSurface(
                    verts, roofTris,
                    anchorIdx, b, centerIdx,
                    anchorIdx, centerIdx,
                    anchorIdx, b,
                    centerIdx, b,
                    family,
                    isMainEdgeCorner,
                    ref warnedReject);
            }
        }
    }

    static void AppendYellowMainEdgeStructuralTriangles(
        List<Vector3> verts,
        List<int> roofTris,
        int baseStart,
        int n,
        int edgeMain,
        int anchorIdx,
        int centerIdx)
    {
        if (n < 3 || edgeMain < 0 || edgeMain >= n || anchorIdx < 0 || centerIdx < 0)
            return;

        int b0 = baseStart + edgeMain;
        int b1 = baseStart + ((edgeMain + 1) % n);

        AppendYellowRidgeTriangleIfNotVerticalWall(verts, roofTris, anchorIdx, centerIdx, b0);
        AppendYellowRidgeTriangleIfNotVerticalWall(verts, roofTris, anchorIdx, b1, centerIdx);
    }

    /// <summary>
    /// Trouve les deux triangles structurels qui partagent l’arête poignée↔faîtage central et les remplace par une triangulation sur l’arête basse du pan (b0↔b1).
    /// </summary>
    static bool TrySwapStructuralTrianglesOffCenterAnchorEdge(
        List<int> roofTris,
        List<Vector3> verts,
        int anchorIdx,
        int centerIdx,
        int b0,
        int b1,
        float mergeVertexEpsilonMeters)
    {
        float epsSq = Mathf.Max(0f, mergeVertexEpsilonMeters);
        epsSq *= epsSq;
        if (TrySwapStructuralTrianglesOffCenterAnchorEdgeStrict(roofTris, verts, anchorIdx, centerIdx, b0, b1))
            return true;
        if (epsSq <= 0f)
            return false;
        return TrySwapStructuralTrianglesOffCenterAnchorEdgeFuzzy(roofTris, verts, anchorIdx, centerIdx, b0, b1, epsSq);
    }

    static bool TrySwapStructuralTrianglesOffCenterAnchorEdgeStrict(
        List<int> roofTris,
        List<Vector3> verts,
        int anchorIdx,
        int centerIdx,
        int b0,
        int b1)
    {
        if (roofTris == null || verts == null || roofTris.Count < 6)
            return false;

        int nt = roofTris.Count / 3;
        var matchSlots = new List<int>(2);
        for (int t = 0; t < nt; t++)
        {
            int i0 = roofTris[t * 3];
            int i1 = roofTris[t * 3 + 1];
            int i2 = roofTris[t * 3 + 2];
            if (!TriangleUsesUndirectedEdge(i0, i1, i2, anchorIdx, centerIdx))
                continue;

            int third = ThirdVertexOfTriangleOppositeEdge(i0, i1, i2, anchorIdx, centerIdx);
            if (third == b0 || third == b1)
                matchSlots.Add(t);
        }

        if (matchSlots.Count != 2)
            return false;

        var thirds = new HashSet<int>();
        for (int mi = 0; mi < matchSlots.Count; mi++)
        {
            int t = matchSlots[mi];
            int i0 = roofTris[t * 3];
            int i1 = roofTris[t * 3 + 1];
            int i2 = roofTris[t * 3 + 2];
            thirds.Add(ThirdVertexOfTriangleOppositeEdge(i0, i1, i2, anchorIdx, centerIdx));
        }

        if (thirds.Count != 2 || !thirds.Contains(b0) || !thirds.Contains(b1))
            return false;

        RemoveRoofTrianglesBySlotIndicesAndAppendStructuralQuad(
            roofTris, verts, nt, matchSlots, b0, anchorIdx, b1, centerIdx);
        return true;
    }

    static bool TrySwapStructuralTrianglesOffCenterAnchorEdgeFuzzy(
        List<int> roofTris,
        List<Vector3> verts,
        int anchorIdx,
        int centerIdx,
        int b0,
        int b1,
        float epsSq)
    {
        if (roofTris == null || verts == null || roofTris.Count < 6 || epsSq <= 0f)
            return false;

        bool VertMatch(int i, int j)
        {
            if ((uint)i >= (uint)verts.Count || (uint)j >= (uint)verts.Count)
                return false;
            if (i == j)
                return true;
            return (verts[i] - verts[j]).sqrMagnitude <= epsSq;
        }

        bool PairAnchorCenter(int ia, int ib) =>
            (VertMatch(ia, anchorIdx) && VertMatch(ib, centerIdx)) ||
            (VertMatch(ia, centerIdx) && VertMatch(ib, anchorIdx));

        int ThirdOppositeAnchorCenterEdge(int i0, int i1, int i2)
        {
            if (PairAnchorCenter(i0, i1))
                return i2;
            if (PairAnchorCenter(i1, i2))
                return i0;
            if (PairAnchorCenter(i2, i0))
                return i1;
            return -1;
        }

        int nt = roofTris.Count / 3;
        var candB0 = new List<int>();
        var candB1 = new List<int>();

        for (int t = 0; t < nt; t++)
        {
            int i0 = roofTris[t * 3];
            int i1 = roofTris[t * 3 + 1];
            int i2 = roofTris[t * 3 + 2];
            int third = ThirdOppositeAnchorCenterEdge(i0, i1, i2);
            if (third < 0)
                continue;

            bool m0 = VertMatch(third, b0);
            bool m1 = VertMatch(third, b1);
            if (m0 && m1)
                continue;
            if (m0 && !m1)
                candB0.Add(t);
            else if (m1 && !m0)
                candB1.Add(t);
        }

        if (candB0.Count == 0 || candB1.Count == 0)
            return false;

        int PickPreferExactBase(List<int> cand, int baseIdx)
        {
            int pick = cand[0];
            for (int c = 0; c < cand.Count; c++)
            {
                int t = cand[c];
                int i0 = roofTris[t * 3];
                int i1 = roofTris[t * 3 + 1];
                int i2 = roofTris[t * 3 + 2];
                int third = ThirdOppositeAnchorCenterEdge(i0, i1, i2);
                if (third == baseIdx)
                    return t;
            }

            return pick;
        }

        int pickB0 = PickPreferExactBase(candB0, b0);
        int pickB1 = PickPreferExactBase(candB1, b1);
        if (pickB0 == pickB1)
            return false;

        var matchSlots = new List<int>(2) { pickB0, pickB1 };
        RemoveRoofTrianglesBySlotIndicesAndAppendStructuralQuad(
            roofTris, verts, nt, matchSlots, b0, anchorIdx, b1, centerIdx);
        return true;
    }

    static void RemoveRoofTrianglesBySlotIndicesAndAppendStructuralQuad(
        List<int> roofTris,
        List<Vector3> verts,
        int triangleCount,
        List<int> matchSlots,
        int b0,
        int anchorIdx,
        int b1,
        int centerIdx)
    {
        var matchSet = new HashSet<int>(matchSlots);
        var kept = new List<int>(roofTris.Count - 6);
        for (int t = 0; t < triangleCount; t++)
        {
            if (matchSet.Contains(t))
                continue;
            kept.Add(roofTris[t * 3]);
            kept.Add(roofTris[t * 3 + 1]);
            kept.Add(roofTris[t * 3 + 2]);
        }

        roofTris.Clear();
        roofTris.AddRange(kept);

        AppendRoofCapTriangleWorldUp(verts, roofTris, b0, anchorIdx, b1);
        AppendRoofCapTriangleWorldUp(verts, roofTris, b0, b1, centerIdx);
    }

    static bool TriangleUsesUndirectedEdge(int i0, int i1, int i2, int e0, int e1)
    {
        bool Has(int a, int b) => (a == e0 && b == e1) || (a == e1 && b == e0);
        return Has(i0, i1) || Has(i1, i2) || Has(i2, i0);
    }

    static int ThirdVertexOfTriangleOppositeEdge(int i0, int i1, int i2, int e0, int e1)
    {
        if (i0 != e0 && i0 != e1)
            return i0;
        if (i1 != e0 && i1 != e1)
            return i1;
        return i2;
    }

    static void AppendYellowTwoEdgeStructuralTriangles(
        List<Vector3> verts,
        List<int> roofTris,
        int baseStart,
        int n,
        int edgeA,
        int edgeB,
        int anchorIdx,
        int centerIdx)
    {
        if (n < 3 || anchorIdx < 0 || centerIdx < 0)
            return;

        var localBase = new List<int>(4);
        AddLocalRidgeBaseCorners(localBase, baseStart, n, edgeA);
        AddLocalRidgeBaseCorners(localBase, baseStart, n, edgeB);

        for (int i = 0; i < localBase.Count; i++)
        {
            int b = localBase[i];
            if (i % 2 == 0)
                AppendYellowRidgeTriangleIfNotVerticalWall(verts, roofTris, anchorIdx, centerIdx, b);
            else
                AppendYellowRidgeTriangleIfNotVerticalWall(verts, roofTris, anchorIdx, b, centerIdx);
        }
    }

    static void AppendYellowRidgeTriangleIfNotVerticalWall(List<Vector3> verts, List<int> roofTris, int i0, int i1, int i2)
    {
        if (IsVerticalOrDegenerateYellowCenterRidgeTriangle(verts, i0, i1, i2))
            return;

        AppendRoofCapTriangleWorldUp(verts, roofTris, i0, i1, i2);
    }

    static void AppendYellowCenterRidgeTriangleIfValidRoofSurface(
        List<Vector3> verts,
        List<int> roofTris,
        int i0,
        int i1,
        int i2,
        int allowedA0,
        int allowedA1,
        int allowedB0,
        int allowedB1,
        int allowedC0,
        int allowedC1,
        string family,
        bool allowStructuralEdge,
        ref bool warnedReject)
    {
        if (!allowStructuralEdge && IsVerticalOrDegenerateYellowCenterRidgeTriangle(verts, i0, i1, i2))
        {
            if (!warnedReject)
            {
                Debug.LogWarning($"HouseRoofSystem skipped a {family} triangle because it would create a vertical/degenerate interior wall.");
                warnedReject = true;
            }
            return;
        }

        AppendRoofTriangleIfConnectivityAllowed(
            verts, roofTris,
            i0, i1, i2,
            allowedA0, allowedA1,
            allowedB0, allowedB1,
            allowedC0, allowedC1,
            family,
            ref warnedReject);
    }

    static bool IsVerticalOrDegenerateYellowCenterRidgeTriangle(List<Vector3> verts, int i0, int i1, int i2)
    {
        if (verts == null || i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count)
            return true;

        Vector3 a = verts[i0];
        Vector3 b = verts[i1];
        Vector3 c = verts[i2];
        Vector3 n = Vector3.Cross(b - a, c - a);
        float mag = n.magnitude;
        if (mag <= TriEps)
            return true;

        // A valid roof patch should have a meaningful upward component. Near-horizontal normals are thin vertical walls.
        float upRatio = Mathf.Abs(n.y) / mag;
        if (upRatio < 0.12f)
            return true;

        return false;
    }

    static bool IsBaseCornerOnEdge(int baseStart, int n, int baseIdx, int edge)
    {
        if (n < 3 || edge < 0 || edge >= n)
            return false;

        return baseIdx == baseStart + edge || baseIdx == baseStart + ((edge + 1) % n);
    }

    static void AddLocalRidgeBaseCorners(List<int> dst, int baseStart, int n, int edge)
    {
        if (dst == null || n < 3 || edge < 0 || edge >= n)
            return;

        AddUniqueIndex(dst, baseStart + edge);
        AddUniqueIndex(dst, baseStart + ((edge + 1) % n));
    }

    static void AddUniqueIndex(List<int> dst, int value)
    {
        for (int i = 0; i < dst.Count; i++)
        {
            if (dst[i] == value)
                return;
        }
        dst.Add(value);
    }

    static bool IsRoofConnectivityEdgeAllowed(
        int a,
        int b,
        int allowedA0,
        int allowedA1,
        int allowedB0,
        int allowedB1,
        int allowedC0,
        int allowedC1)
    {
        return SameUndirectedEdge(a, b, allowedA0, allowedA1) ||
               SameUndirectedEdge(a, b, allowedB0, allowedB1) ||
               SameUndirectedEdge(a, b, allowedC0, allowedC1);
    }

    static bool SameUndirectedEdge(int a, int b, int e0, int e1)
    {
        return (a == e0 && b == e1) || (a == e1 && b == e0);
    }

    static bool IsLocalAnchorEdgePair(int eLeft, int eRight, bool enabled, int edgePrev, int edgeMain, int edgeNext)
    {
        if (!enabled)
            return false;

        return SameUndirectedEdge(eLeft, eRight, edgePrev, edgeMain) ||
               SameUndirectedEdge(eLeft, eRight, edgeMain, edgeNext);
    }

    /// <summary>
    /// Chapeau entre les sommets haut d’arête uniquement si leur ordre naturel A0→A1→... forme déjà une couronne fiable.
    /// Les A_i sont des sommets de pans, pas les coins garantis d’un plateau : si la validation échoue, aucun chapeau n’est ajouté.
    /// </summary>
    static void AppendLateralRoofTopCapSafe(
        List<Vector3> verts,
        List<int> roofTris,
        int[] faceTopIdx,
        int n,
        Vector2 centroidXZ)
    {
        if (n < 3 || faceTopIdx == null || faceTopIdx.Length < n)
            return;

        var capLoop = new List<int>(n);
        var polyCopy = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            int vi = faceTopIdx[i];
            if (vi < 0 || vi >= verts.Count)
                return;

            // Identical top vertices mean the A_i loop is a set of pan peaks, not a reliable cap boundary.
            for (int j = 0; j < capLoop.Count; j++)
            {
                if (capLoop[j] == vi)
                    return;
            }

            capLoop.Add(vi);
            Vector3 p = verts[vi];
            polyCopy.Add(new Vector2(p.x, p.z));
        }

        if (!IsStrictlyValidRoofCapPolygon(polyCopy, centroidXZ))
            return;

        if (SignedArea(polyCopy) < 0f)
        {
            polyCopy.Reverse();
            capLoop.Reverse();
        }

        if (!TryTriangulateEarClip(polyCopy, out List<int> topIx))
            return;

        for (int t = 0; t < topIx.Count; t += 3)
        {
            int ia = capLoop[topIx[t]];
            int ib = capLoop[topIx[t + 1]];
            int ic = capLoop[topIx[t + 2]];
            AppendRoofCapTriangleWorldUp(verts, roofTris, ia, ib, ic);
        }
    }

    static bool IsStrictlyValidRoofCapPolygon(List<Vector2> poly, Vector2 requiredInsidePoint)
    {
        int n = poly != null ? poly.Count : 0;
        if (n < 3)
            return false;

        float areaAbs = Mathf.Abs(SignedArea(poly));
        if (areaAbs <= TriEps)
            return false;

        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            if ((b - a).sqrMagnitude <= TriEps * TriEps)
                return false;
        }

        // Natural A_i order must already be simple: no non-neighbor edge intersections.
        for (int i = 0; i < n; i++)
        {
            int iNext = (i + 1) % n;
            for (int j = i + 1; j < n; j++)
            {
                int jNext = (j + 1) % n;
                bool neighbors = i == j || iNext == j || jNext == i;
                if (!neighbors && SegmentsIntersectStrict(poly[i], poly[iNext], poly[j], poly[jNext]))
                    return false;
            }
        }

        float sign = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            Vector2 c = poly[(i + 2) % n];
            float turn = Cross2(b - a, c - b);
            if (Mathf.Abs(turn) <= TriEps)
                return false;
            if (sign == 0f)
                sign = Mathf.Sign(turn);
            else if (turn * sign <= TriEps)
                return false;
        }

        return PointInConvexPolygon(requiredInsidePoint, poly);
    }

    static bool SegmentsIntersectStrict(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float abC = Cross2(b - a, c - a);
        float abD = Cross2(b - a, d - a);
        float cdA = Cross2(d - c, a - c);
        float cdB = Cross2(d - c, b - c);
        return abC * abD < -TriEps && cdA * cdB < -TriEps;
    }

    static bool PointInConvexPolygon(Vector2 p, List<Vector2> poly)
    {
        int n = poly != null ? poly.Count : 0;
        if (n < 3)
            return false;

        float sign = Mathf.Sign(SignedArea(poly));
        if (sign == 0f)
            return false;

        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            if (Cross2(b - a, p - a) * sign < -TriEps)
                return false;
        }

        return true;
    }

    /// <summary>Fermeture du faîtage avec trois sommets : chaque stitch relie le jaune aux points du dernier anneau vers la ligne brisée.</summary>
    static void AppendTripleSummitCap(
        List<int> roofTris,
        List<Vector3> verts,
        List<Vector2> uvs,
        int lastRowStart,
        int n,
        int apexCentralMeshIdx,
        float apexYWorld,
        Vector2 a0xz,
        Vector2 a1xz,
        Vector2 a2xz,
        int apex0,
        int apex1,
        int apex2)
    {
        const float cutEps = 1e-4f;
        var cutsScratch = new List<float>(8);

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int viIdx = lastRowStart + i;
            int vjIdx = lastRowStart + j;
            Vector3 vi = verts[viIdx];
            Vector3 vj = verts[vjIdx];
            Vector2 pi = new Vector2(vi.x, vi.z);
            Vector2 pj = new Vector2(vj.x, vj.z);
            Vector2 seg = pj - pi;

            cutsScratch.Clear();
            AppendAllPolylineSegmentTransitionCutsXZ(pi, pj, a0xz, a1xz, a2xz, cutsScratch);

            cutsScratch.Sort();
            var merged = new List<float>(cutsScratch.Count);
            for (int c = 0; c < cutsScratch.Count; c++)
            {
                float u = cutsScratch[c];
                if (merged.Count == 0 || Mathf.Abs(u - merged[merged.Count - 1]) > cutEps)
                    merged.Add(u);
            }

            var param = new List<float>(merged.Count + 2) { 0f };
            for (int c = 0; c < merged.Count; c++)
            {
                float u = merged[c];
                if (u <= cutEps || u >= 1f - cutEps)
                    continue;
                if (Mathf.Abs(u - param[param.Count - 1]) < cutEps)
                    continue;
                param.Add(u);
            }

            param.Add(1f);

            for (int k = 0; k < param.Count - 1; k++)
            {
                float ua = param[k];
                float ub = param[k + 1];
                if (ub - ua < cutEps)
                    continue;

                Vector2 pa = pi + seg * ua;
                Vector2 pb = pi + seg * ub;
                float ya = Mathf.Lerp(vi.y, vj.y, ua);
                float yb = Mathf.Lerp(vi.y, vj.y, ub);
                Vector2 mid = (pa + pb) * 0.5f;
                Vector2 qRidge = ClosestOnBrokenRidgePolylineXZ(mid, a0xz, a1xz, a2xz);
                int ridgeVertIdx = AppendSummitStitchRingVertex(verts, uvs, qRidge, apexYWorld);

                int ia = ua <= cutEps ? viIdx : AppendSummitStitchRingVertex(verts, uvs, pa, ya);
                int ib = ub >= 1f - cutEps ? vjIdx : AppendSummitStitchRingVertex(verts, uvs, pb, yb);

                AppendRoofCapTriangleWorldUp(verts, roofTris, apexCentralMeshIdx, ia, ridgeVertIdx);
                AppendRoofCapTriangleWorldUp(verts, roofTris, apexCentralMeshIdx, ridgeVertIdx, ib);
            }
        }

        AppendRoofCapTriangleWorldUp(verts, roofTris, apex0, apex1, apex2);
    }

    public static Vector2 ClampMeshAnchorOffsetToRoofFootprint(List<Vector3> baseCorners, Vector2 centroid, Vector2 desiredOffset)
    {
        float maxRadius = 0f;
        if (baseCorners != null)
        {
            for (int i = 0; i < baseCorners.Count; i++)
            {
                Vector2 d = new Vector2(baseCorners[i].x - centroid.x, baseCorners[i].z - centroid.y);
                maxRadius = Mathf.Max(maxRadius, d.magnitude);
            }
        }

        // Keep apex inside/near footprint to avoid fold-overs while still enabling strong skew.
        float limit = Mathf.Max(0.1f, maxRadius * 0.95f);
        return centroid + Vector2.ClampMagnitude(desiredOffset, limit);
    }

    static float SignedAreaXZPoly(List<Vector2> poly)
    {
        double a = 0.0;
        int n = poly != null ? poly.Count : 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            a += (double)poly[i].x * poly[j].y - (double)poly[j].x * poly[i].y;
        }
        return (float)(0.5 * a);
    }

    static bool LineLineIntersectXZ(Vector2 origin0, Vector2 dir0, Vector2 origin1, Vector2 dir1, out Vector2 hit)
    {
        hit = default;
        float cross = dir0.x * dir1.y - dir0.y * dir1.x;
        if (Mathf.Abs(cross) < 1e-7f)
            return false;
        Vector2 d = origin1 - origin0;
        float t = (d.x * dir1.y - d.y * dir1.x) / cross;
        hit = origin0 + dir0 * t;
        return true;
    }

    /// <summary>
    /// Décale le polygone du footprint vers l'intérieur du plan, perpendiculairement à chaque façade (comme un offset « dans le mur »).
    /// Polygone CCW en XZ ; formes très concaves peuvent échouer — repli sans inset.
    /// </summary>
    static bool TryInsetPolygonXZPerpendicular(List<Vector2> poly, float inset, out List<Vector2> result)
    {
        result = null;
        int n = poly != null ? poly.Count : 0;
        if (n < 3)
            return false;
        if (inset <= 1e-6f)
        {
            result = new List<Vector2>(poly);
            return true;
        }

        var work = new List<Vector2>(poly);
        if (SignedAreaXZPoly(work) < 0f)
            work.Reverse();

        result = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = work[(i + n - 1) % n];
            Vector2 cur = work[i];
            Vector2 next = work[(i + 1) % n];

            Vector2 e0 = cur - prev;
            Vector2 e1 = next - cur;
            float l0 = e0.magnitude;
            float l1 = e1.magnitude;
            if (l0 < 1e-7f || l1 < 1e-7f)
                return false;

            Vector2 n0 = new Vector2(-e0.y, e0.x) / l0;
            Vector2 n1 = new Vector2(-e1.y, e1.x) / l1;

            Vector2 o0 = prev + n0 * inset;
            Vector2 o1 = cur + n1 * inset;
            Vector2 d0 = e0 / l0;
            Vector2 d1 = e1 / l1;

            if (!LineLineIntersectXZ(o0, d0, o1, d1, out Vector2 hit))
                return false;

            result.Add(hit);
        }

        return true;
    }

    /// <summary>
    /// Profil vertical du dôme (arrondi du toit), actif dans le maillage uniquement si <see cref="useDomeProfile"/>.
    /// radial01 : bord (0) → centre (1). roundness01 dans [0..1] :
    /// &lt; 0.5 famille dôme inversé ; ≈ 0.5 profil presque conique linéaire ; &gt; 0.5 dôme « normal ».
    /// </summary>
    public static float EvaluateDomeProfile(float radial01, float roundness01)
    {
        radial01 = Mathf.Clamp01(radial01);
        roundness01 = Mathf.Clamp01(roundness01);
        float s = roundness01 * 2f - 1f; // [-1..1]
        if (s >= 0f)
        {
            // Normal dome: above the neutral cone, with a flattened tangent at the summit.
            float exponent = Mathf.Lerp(1.0f, 3.4f, s);
            return 1f - Mathf.Pow(Mathf.Max(1e-4f, 1f - radial01), exponent);
        }

        // Inverted dome: below the neutral cone between edge and center.
        float inv = -s;
        float exponentInv = Mathf.Lerp(1.0f, 3.2f, inv);
        return Mathf.Pow(Mathf.Max(1e-4f, radial01), exponentInv);
    }

    /// <summary>
    /// Inverse helper used by roof controls:
    /// given radial position (0 edge -> 1 center) and normalized height,
    /// estimate the roundness parameter that best matches the dome profile.
    /// </summary>
    public static float EstimateRoundnessFromSample(float radial01, float yNorm)
    {
        radial01 = Mathf.Clamp(radial01, 1e-4f, 0.9999f);
        yNorm = Mathf.Clamp(yNorm, 1e-4f, 0.9999f);
        float linear = radial01;
        float curve = yNorm - linear;
        // curve > 0 => normal dome side ; curve < 0 => inverted side
        return Mathf.Clamp01(0.5f + curve * 1.2f);
    }

    void EnsureRoofCladdingProfileDefaults(bool emitInspectorLogs = true)
    {
        RoofCladdingRuntime runtime = GetComponent<RoofCladdingRuntime>();
        RoofCladdingGenerator generator = GetComponent<RoofCladdingGenerator>();
        if (runtime == null || generator == null)
            return;

        runtime.EnsureCurrentProfileIfEmpty(defaultRoofCladdingProfile);
        bool generatorGotDefault = generator.EnsureDefaultCladdingProfileIfEmpty(defaultRoofCladdingProfile);
        if (generatorGotDefault)
            runtime.MarkDirty();

        if (emitInspectorLogs && defaultRoofCladdingProfile != null)
        {
            string defLabel = defaultRoofCladdingProfile.name;
            string rtLabel = runtime.CurrentProfile != null ? runtime.CurrentProfile.name : "(null)";
            string genLabel = generator.SerializedDefaultCladdingProfile != null
                ? generator.SerializedDefaultCladdingProfile.name
                : "(null)";
            Debug.Log($"[RoofCladdingSetup] defaultRoofCladdingProfile = {defLabel}", this);
            Debug.Log($"[RoofCladdingSetup] runtime currentProfile after setup = {rtLabel}", this);
            Debug.Log($"[RoofCladdingSetup] generator defaultProfile after setup = {genLabel}", this);
        }
    }

    /// <summary>Appelé depuis <see cref="WallBuildController.AddRoofFromHouseMenu"/> pour transmettre le profil cladding défini sur le contrôleur.</summary>
    public void SetDefaultRoofCladdingProfile(RoofCladdingProfile profile)
    {
        defaultRoofCladdingProfile = profile;
        EnsureRoofCladdingProfileDefaults(emitInspectorLogs: false);

        string pName = profile != null ? profile.name : "(null)";
        Debug.Log($"[RoofCladdingSetup] profile received from WallBuildController = {pName}", this);
        RoofCladdingRuntime runtime = GetComponent<RoofCladdingRuntime>();
        RoofCladdingGenerator generator = GetComponent<RoofCladdingGenerator>();
        if (runtime != null)
        {
            string rtName = runtime.CurrentProfile != null ? runtime.CurrentProfile.name : "(null)";
            Debug.Log($"[RoofCladdingSetup] runtime currentProfile after Add Roof = {rtName}", this);
        }

        if (generator != null)
        {
            RoofCladdingProfile genDef = generator.SerializedDefaultCladdingProfile;
            string genName = genDef != null ? genDef.name : "(null)";
            Debug.Log($"[RoofCladdingSetup] generator defaultProfile after Add Roof = {genName}", this);
        }

        if (profile != null)
            runtime?.MarkDirty();
    }

    void EnsureComponents()
    {
        Transform child = transform.Find(RoofChildName);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(RoofChildName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = gameObject.layer;
        }
        else
            go = child.gameObject;

        _mf = go.GetComponent<MeshFilter>();
        if (_mf == null) _mf = go.AddComponent<MeshFilter>();
        _mr = go.GetComponent<MeshRenderer>();
        if (_mr == null) _mr = go.AddComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "HouseRoofMesh" };
            _mf.sharedMesh = _mesh;
        }

        if (go.GetComponent<HouseRoofControlPointProvider>() == null)
            go.AddComponent<HouseRoofControlPointProvider>();

        // Le habillage du toit vit sur le même GameObject que le système de toit afin de suivre le même hash/rebuild.
        if (GetComponent<RoofCladdingRuntime>() == null)
            gameObject.AddComponent<RoofCladdingRuntime>();
        if (GetComponent<RoofCladdingGenerator>() == null)
            gameObject.AddComponent<RoofCladdingGenerator>();

        EnsureRoofCladdingProfileDefaults();

        // Reuse wall material by default (never assign null — sinon tout le maillage perd ses textures).
        WallObject wall = GetComponent<WallObject>();
        MeshRenderer wallMr = wall != null ? wall.GetComponent<MeshRenderer>() : null;
        Material roofMaterial = wallMr != null && wallMr.sharedMaterial != null
            ? wallMr.sharedMaterial
            : (_mr.sharedMaterial != null ? _mr.sharedMaterial : null);
        if (roofMaterial == null)
            roofMaterial = EnsureFallbackRoofSkinMaterial();

        Material connectorMaterial = EnsureConnectorMaterial();
        _mr.sharedMaterials = new[] { roofMaterial, connectorMaterial };
    }

    Material EnsureFallbackRoofSkinMaterial()
    {
        if (_roofFallbackSkinMaterial != null)
            return _roofFallbackSkinMaterial;

        Shader shader = TryFindShader(
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/SimpleLit",
            "Universal Render Pipeline/BakedLit",
            "Standard",
            "Legacy Shaders/Diffuse");
        if (shader == null)
            shader = Shader.Find("Hidden/InternalErrorShader");

        _roofFallbackSkinMaterial = new Material(shader)
        {
            name = "HouseRoof Fallback Skin",
            hideFlags = HideFlags.DontSave,
            color = Color.white
        };
        if (_roofFallbackSkinMaterial.HasProperty("_BaseColor"))
            _roofFallbackSkinMaterial.SetColor("_BaseColor", Color.white);
        if (_roofFallbackSkinMaterial.HasProperty("_Smoothness"))
            _roofFallbackSkinMaterial.SetFloat("_Smoothness", 0.25f);
        if (_roofFallbackSkinMaterial.HasProperty("_Metallic"))
            _roofFallbackSkinMaterial.SetFloat("_Metallic", 0f);
        return _roofFallbackSkinMaterial;
    }

    Material EnsureConnectorMaterial()
    {
        Shader shader = ResolveConnectorShader();
        if (_connectorMaterial == null)
            _connectorMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        else if (_connectorMaterial.shader != shader && shader != null)
            _connectorMaterial.shader = shader;

        _connectorMaterial.name = "Roof Eave Connector Dark Brown Matte";
        ApplyConnectorBrownSurface(_connectorMaterial);
        return _connectorMaterial;
    }

    static Shader TryFindShader(params string[] paths)
    {
        if (paths == null)
            return null;
        for (int i = 0; i < paths.Length; i++)
        {
            Shader s = Shader.Find(paths[i]);
            if (s != null)
                return s;
        }

        return null;
    }

    /// <summary>En URP, Standard/Unlit intégrés peuvent être absents : chaîne de repli pour éviter un Material invalide.</summary>
    static Shader ResolveConnectorShader()
    {
        Shader s = TryFindShader(
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/SimpleLit",
            "Unlit/Color",
            "Unlit/Texture",
            "Sprites/Default",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/BakedLit",
            "Standard",
            "Legacy Shaders/Diffuse");
        return s != null ? s : Shader.Find("Hidden/InternalErrorShader");
    }

    static void ApplyConnectorBrownSurface(Material mat)
    {
        if (mat == null)
            return;

        Color brown = new Color(0.46f, 0.32f, 0.22f, 1f);
        mat.color = brown;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", brown);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", brown);

        bool likelyUnlit = mat.shader != null &&
                           (mat.shader.name.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (!likelyUnlit)
        {
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0f);
        }

        if (mat.HasProperty("_Cull"))
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
    }

    #region Roof shell crossing diagnostics (XZ projection, pre-thickness only)

    const string RoofCrossDiagNoReliableCutReason =
        "raw problem count indicates bug, but no reliable cut candidate selected yet";

    void FinalizeRoofCrossDiagScanHonest(
        int problems,
        int triCount,
        bool dualCornerAnchorMode,
        Vector2 footprintCentroidXZ,
        float fpRadius,
        int nearCentroidHits,
        int legacyStrictPairs)
    {
        FinalizeRoofCrossDiagState(problems, triCount, dualCornerAnchorMode);

        LastRoofCrossDiagChosenProblemIndex = -1;
        LastRoofCrossDiagChosenScore = 0f;
        LastRoofCrossDiagChosenHitXZ = Vector2.zero;
        LastRoofCrossDiagChosenTriA = -1;
        LastRoofCrossDiagChosenTriB = -1;
        LastRoofCrossDiagLastProblemType = "";
        LastRoofCrossDiagLastReason = "";
        LastRoofCrossDiagLastProposedCutPoint = Vector3.zero;
        LastRoofCrossDiagLastVertexToShorten = -1;
        LastRoofCrossDiagLastTriA = -1;
        LastRoofCrossDiagLastTriB = -1;

        if (IsRoofCrossDiagProblemCountConsideredRealProblem(problems))
        {
            LastRoofCrossDiagChosenReason = RoofCrossDiagNoReliableCutReason;
            Debug.Log(
                $"[RoofCrossDiag] REAL_VISUAL_PROBLEM_BY_COUNT rawProblems={problems.ToString(CultureInfo.InvariantCulture)} status=PROBLEM reason=KNOWN_PARASITE_ROOF_CASE_7_OR_12",
                this);
        }
        else
        {
            LastRoofCrossDiagChosenReason = "";
            Debug.Log(
                $"[RoofCrossDiag] countBasedStatus rawProblems={problems.ToString(CultureInfo.InvariantCulture)} status=CLEAN reason=RAW_PROBLEM_COUNT_NOT_KNOWN_PARASITE_CASE",
                this);
        }

        Debug.Log(
            $"[RoofCrossDiag] summary status={LastRoofCrossDiagStatus} rawProblems={problems} " +
            $"intersectionsFound={problems} crossInsideFaceFound={nearCentroidHits.ToString(CultureInfo.InvariantCulture)} " +
            $"legacyStrictInteriorEdgePairs={legacyStrictPairs.ToString(CultureInfo.InvariantCulture)} triangles={triCount} " +
            $"dualCornerAnchor={dualCornerAnchorMode.ToString(CultureInfo.InvariantCulture)} " +
            $"footprintCentroidXZ=({footprintCentroidXZ.x.ToString("F4", CultureInfo.InvariantCulture)},{footprintCentroidXZ.y.ToString("F4", CultureInfo.InvariantCulture)}) fpRadius={fpRadius.ToString("F4", CultureInfo.InvariantCulture)}",
            this);
    }

    enum RoofCrossTriFamilyKind
    {
        Normal,
        XDiagonalCandidate,
        XCrossParticipant,
        ParasiteLikely
    }

    static string RoofCrossTriFamilyKindToToken(RoofCrossTriFamilyKind k)
    {
        switch (k)
        {
            case RoofCrossTriFamilyKind.Normal: return "NORMAL";
            case RoofCrossTriFamilyKind.XDiagonalCandidate: return "X_DIAGONAL_CANDIDATE";
            case RoofCrossTriFamilyKind.XCrossParticipant: return "X_CROSS_PARTICIPANT";
            case RoofCrossTriFamilyKind.ParasiteLikely: return "PARASITE_LIKELY";
            default: return "UNKNOWN";
        }
    }

    static Color RoofCrossTriFamilyKindToColor(RoofCrossTriFamilyKind k)
    {
        switch (k)
        {
            case RoofCrossTriFamilyKind.Normal:
                return new Color(0.2f, 0.85f, 0.35f, 1f);
            case RoofCrossTriFamilyKind.XDiagonalCandidate:
                return new Color(0.95f, 0.85f, 0.15f, 1f);
            case RoofCrossTriFamilyKind.XCrossParticipant:
                return new Color(1f, 0.55f, 0.1f, 1f);
            case RoofCrossTriFamilyKind.ParasiteLikely:
                return new Color(0.95f, 0.15f, 0.12f, 1f);
            default:
                return Color.white;
        }
    }

    static string FormatIntListForDiagLog(List<int> slots)
    {
        if (slots == null || slots.Count == 0)
            return "none";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < slots.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(slots[i].ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Audit + logs optionnels + cache gizmos si options actives.</summary>
    void RunRoofCrossTriangleFamilyAuditAfterScan(
        List<Vector3> verts,
        List<int> roofTris,
        Vector2 footprintCentroidXZ,
        float fpRadius,
        int rawProblems,
        int[] triRawProblemHits,
        int[] triStrongHits,
        int[] triWeakHits,
        HashSet<int>[] triPartners)
    {
        if (!IsRoofCrossShellDiagnosticEnabled)
            return;
        bool wantLogs = debugRoofCrossTriangleFamilyAudit;
        bool wantDraw = debugDrawRoofCrossTriangleFamily;
        bool forceAuditForUi =
            autoRunTriangleFamilyAuditWhenProblem &&
            IsRoofCrossDiagProblemCountConsideredRealProblem(rawProblems);
        if (!wantLogs && !wantDraw && !forceAuditForUi)
            return;
        if (verts == null || roofTris == null || roofTris.Count < 9)
            return;

        int nTri = roofTris.Count / 3;
        int[] hitsArr = triRawProblemHits != null && triRawProblemHits.Length >= nTri
            ? triRawProblemHits
            : new int[nTri];
        int[] strongArr = triStrongHits != null && triStrongHits.Length >= nTri
            ? triStrongHits
            : new int[nTri];
        int[] weakArr = triWeakHits != null && triWeakHits.Length >= nTri
            ? triWeakHits
            : new int[nTri];

        bool parasiteCaseEarly = IsRoofCrossDiagProblemCountConsideredRealProblem(rawProblems);

        float roofDiagSpan = ComputeVertsXZDiagonal(verts);
        float medianLongEdge = 0f;
        var longestEdgesBuf = new float[nTri];
        var geomDiagCand = new bool[nTri];
        var crossPartnerGeom = new bool[nTri];
        var crossPartnerContact = new bool[nTri];
        var longEdgeDirUnit = new Vector2[nTri];

        var xCandList = new List<int>(8);
        var xPartList = new List<int>(8);
        var parasiteList = new List<int>(8);
        var normalSlots = new List<int>(16);
        var nonNormalSlots = new List<int>(16);

        if (wantDraw)
            ClearRoofCrossTriangleFamilyGizmoCache();

        // Pass 1 — géométrie + médiane des plus longues arêtes XZ
        for (int s = 0; s < nTri; s++)
        {
            int i0 = roofTris[s * 3], i1 = roofTris[s * 3 + 1], i2 = roofTris[s * 3 + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count)
            {
                longestEdgesBuf[s] = 0f;
                continue;
            }

            Vector2 c0 = VertXZ(verts[i0]), c1 = VertXZ(verts[i1]), c2 = VertXZ(verts[i2]);
            float el0 = (c1 - c0).magnitude;
            float el1 = (c2 - c1).magnitude;
            float el2 = (c0 - c2).magnitude;
            float longest = Mathf.Max(el0, Mathf.Max(el1, el2));
            longestEdgesBuf[s] = longest;
            Vector2 lp0 = c0, lp1 = c1;
            if (el1 >= el0 && el1 >= el2) { lp0 = c1; lp1 = c2; }
            else if (el2 >= el0 && el2 >= el1) { lp0 = c2; lp1 = c0; }

            Vector2 led = lp1 - lp0;
            float ledMag = led.magnitude;
            longEdgeDirUnit[s] = ledMag > 1e-8f ? led / ledMag : Vector2.right;

            float bandFrac = parasiteCaseEarly ? 0.42f : 0.30f;
            float bandEps = Mathf.Max(1e-4f, fpRadius * bandFrac);
            bool crossesCenterBand = DistPointToSegmentXZ(footprintCentroidXZ, lp0, lp1) <= bandEps;
            float longSpanMin = parasiteCaseEarly
                ? Mathf.Max(fpRadius * 0.30f, roofDiagSpan * 0.16f)
                : Mathf.Max(fpRadius * 0.38f, roofDiagSpan * 0.20f);
            bool longSpan = longest >= longSpanMin;

            geomDiagCand[s] = longSpan && crossesCenterBand;
        }

        {
            var sortedLe = new float[nTri];
            System.Array.Copy(longestEdgesBuf, sortedLe, nTri);
            System.Array.Sort(sortedLe);
            medianLongEdge = nTri > 0 ? sortedLe[nTri / 2] : 0f;
            if (medianLongEdge < 1e-8f)
                medianLongEdge = 1e-8f;
        }

        // Raffinement « grande diagonale » relative au maillage (cas 7/12 : plus tolérant)
        float medianLongFactor = parasiteCaseEarly ? 0.48f : 0.62f;
        for (int s = 0; s < nTri; s++)
        {
            if (!geomDiagCand[s])
                continue;
            float longest = longestEdgesBuf[s];
            if (longest < medianLongEdge * medianLongFactor)
                geomDiagCand[s] = false;
        }

        // Paires de directions quasi orthogonales entre candidats géométriques (bras du X)
        for (int s = 0; s < nTri; s++)
        {
            if (!geomDiagCand[s])
                continue;
            Vector2 ds = longEdgeDirUnit[s];
            for (int u = s + 1; u < nTri; u++)
            {
                if (!geomDiagCand[u])
                    continue;
                Vector2 du = longEdgeDirUnit[u];
                float ad = Mathf.Abs(Vector2.Dot(ds, du));
                if (ad < 0.58f)
                {
                    crossPartnerGeom[s] = true;
                    crossPartnerGeom[u] = true;
                }
            }
        }

        // Croisements réels du scan : paires de triangles du rapport d’intersection ; directions croisées
        // via toutes les arêtes XZ (pas seulement la plus longue).
        if (triPartners != null)
        {
            for (int s = 0; s < nTri; s++)
            {
                if (triPartners[s] == null || triPartners[s].Count == 0)
                    continue;
                int ia0 = roofTris[s * 3], ia1 = roofTris[s * 3 + 1], ia2 = roofTris[s * 3 + 2];
                if (ia0 < 0 || ia1 < 0 || ia2 < 0 || ia0 >= verts.Count || ia1 >= verts.Count || ia2 >= verts.Count)
                    continue;
                Vector2 A0 = VertXZ(verts[ia0]), A1 = VertXZ(verts[ia1]), A2 = VertXZ(verts[ia2]);
                foreach (int u in triPartners[s])
                {
                    if (u < 0 || u >= nTri || u <= s)
                        continue;
                    int ja0 = roofTris[u * 3], ja1 = roofTris[u * 3 + 1], ja2 = roofTris[u * 3 + 2];
                    if (ja0 < 0 || ja1 < 0 || ja2 < 0 || ja0 >= verts.Count || ja1 >= verts.Count || ja2 >= verts.Count)
                        continue;
                    Vector2 B0 = VertXZ(verts[ja0]), B1 = VertXZ(verts[ja1]), B2 = VertXZ(verts[ja2]);
                    if (RoofPartnerTrisEdgeFamiliesCrossingXZ(A0, A1, A2, B0, B1, B2))
                    {
                        crossPartnerContact[s] = true;
                        crossPartnerContact[u] = true;
                    }
                }
            }
        }

        bool parasiteCase = parasiteCaseEarly;

        for (int s = 0; s < nTri; s++)
        {
            int i0 = roofTris[s * 3], i1 = roofTris[s * 3 + 1], i2 = roofTris[s * 3 + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count)
                continue;

            Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];
            float minY = Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y));
            float maxY = Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y));
            float avgY = (v0.y + v1.y + v2.y) / 3f;
            Vector2 c0 = VertXZ(v0), c1 = VertXZ(v1), c2 = VertXZ(v2);
            Vector2 centerXZ = (c0 + c1 + c2) / 3f;
            float areaXZ = 0.5f * Mathf.Abs(Cross2(c1 - c0, c2 - c0));
            float el0 = (c1 - c0).magnitude;
            float el1 = (c2 - c1).magnitude;
            float el2 = (c0 - c2).magnitude;
            float longestEdgeXZ = Mathf.Max(el0, Mathf.Max(el1, el2));
            Vector2 lp0 = c0, lp1 = c1;
            if (el1 >= el0 && el1 >= el2) { lp0 = c1; lp1 = c2; }
            else if (el2 >= el0 && el2 >= el1) { lp0 = c2; lp1 = c0; }

            Vector2 ledRaw = lp1 - lp0;
            Vector2 longEdgeDirXZ = ledRaw.sqrMagnitude > 1e-12f ? ledRaw.normalized : Vector2.right;

            int hits = hitsArr[s];
            int strongHitCount = s < strongArr.Length ? strongArr[s] : 0;
            int weakEndpointHitCount = s < weakArr.Length ? weakArr[s] : 0;
            int partnerCount = 0;
            if (triPartners != null && s < triPartners.Length && triPartners[s] != null)
                partnerCount = triPartners[s].Count;

            float bandEps = Mathf.Max(1e-4f, fpRadius * 0.30f);
            bool crossesCenterBand = DistPointToSegmentXZ(footprintCentroidXZ, lp0, lp1) <= bandEps;
            Vector2 cFoot = footprintCentroidXZ;
            float dLp0 = (lp0 - cFoot).magnitude;
            float dLp1 = (lp1 - cFoot).magnitude;
            Vector2 n0 = dLp0 > 1e-5f ? (lp0 - cFoot).normalized : Vector2.right;
            Vector2 n1 = dLp1 > 1e-5f ? (lp1 - cFoot).normalized : Vector2.right;
            bool oppositeSideSpan =
                dLp0 > fpRadius * 0.22f && dLp1 > fpRadius * 0.22f &&
                Vector2.Dot(n0, n1) < -0.08f;

            bool anyCross = crossPartnerGeom[s] || crossPartnerContact[s];

            float diagonalScore = 0f;
            if (crossesCenterBand)
                diagonalScore += 0.45f;
            if (geomDiagCand[s])
                diagonalScore += 0.35f;
            if (oppositeSideSpan)
                diagonalScore += 0.2f;
            if (crossPartnerContact[s])
                diagonalScore += 0.15f;
            diagonalScore = Mathf.Clamp01(diagonalScore);

            bool isolatedEndpointOnly =
                hits == 1 && weakEndpointHitCount == 1 && strongHitCount == 0 && !anyCross;

            int xParticipantScore = 0;
            if (hits >= 2)
                xParticipantScore += 2;
            if (anyCross)
                xParticipantScore += 2;
            if (strongHitCount >= 1)
                xParticipantScore += 1;
            xParticipantScore = Mathf.Clamp(xParticipantScore, 0, 5);

            bool geomEligible = geomDiagCand[s] && !isolatedEndpointOnly;

            bool contactParticipant =
                parasiteCase &&
                crossPartnerContact[s] &&
                !isolatedEndpointOnly &&
                (hits >= 2 || strongHitCount >= 1 || partnerCount >= 2);

            bool participantRule =
                (geomEligible && (hits >= 2 || crossPartnerGeom[s])) ||
                contactParticipant;

            bool parasiteRule =
                parasiteCase &&
                !isolatedEndpointOnly &&
                hits >= 2 &&
                anyCross &&
                (geomDiagCand[s] || crossPartnerContact[s]);

            bool candShellOnly =
                parasiteCase &&
                crossPartnerContact[s] &&
                hits >= 1 &&
                !isolatedEndpointOnly &&
                !participantRule &&
                !parasiteRule;

            RoofCrossTriFamilyKind kind;
            if (isolatedEndpointOnly)
                kind = RoofCrossTriFamilyKind.Normal;
            else if (parasiteRule)
                kind = RoofCrossTriFamilyKind.ParasiteLikely;
            else if (participantRule)
                kind = RoofCrossTriFamilyKind.XCrossParticipant;
            else if (geomDiagCand[s] || candShellOnly)
                kind = RoofCrossTriFamilyKind.XDiagonalCandidate;
            else
                kind = RoofCrossTriFamilyKind.Normal;

            if (kind != RoofCrossTriFamilyKind.Normal)
                nonNormalSlots.Add(s);

            if (kind == RoofCrossTriFamilyKind.XDiagonalCandidate)
                xCandList.Add(s);
            else if (kind == RoofCrossTriFamilyKind.XCrossParticipant)
                xPartList.Add(s);
            else if (kind == RoofCrossTriFamilyKind.ParasiteLikely)
                parasiteList.Add(s);
            else
                normalSlots.Add(s);

            if (wantLogs)
            {
                Debug.Log(
                    $"[RoofCrossDiagTri] triSlot={s.ToString(CultureInfo.InvariantCulture)} indices=({i0.ToString(CultureInfo.InvariantCulture)},{i1.ToString(CultureInfo.InvariantCulture)},{i2.ToString(CultureInfo.InvariantCulture)}) " +
                    $"minY={minY.ToString("F4", CultureInfo.InvariantCulture)} maxY={maxY.ToString("F4", CultureInfo.InvariantCulture)} avgY={avgY.ToString("F4", CultureInfo.InvariantCulture)} " +
                    $"centerXZ=({centerXZ.x.ToString("F4", CultureInfo.InvariantCulture)},{centerXZ.y.ToString("F4", CultureInfo.InvariantCulture)}) areaXZ={areaXZ.ToString("F5", CultureInfo.InvariantCulture)} " +
                    $"longestEdgeXZ={longestEdgeXZ.ToString("F5", CultureInfo.InvariantCulture)} longEdgeDirXZ=({longEdgeDirXZ.x.ToString("F4", CultureInfo.InvariantCulture)},{longEdgeDirXZ.y.ToString("F4", CultureInfo.InvariantCulture)}) " +
                    $"hitCount={hits.ToString(CultureInfo.InvariantCulture)} strongHitCount={strongHitCount.ToString(CultureInfo.InvariantCulture)} weakEndpointHitCount={weakEndpointHitCount.ToString(CultureInfo.InvariantCulture)} " +
                    $"distinctPartnerTris={partnerCount.ToString(CultureInfo.InvariantCulture)} crossesCenterBand={crossesCenterBand.ToString(CultureInfo.InvariantCulture)} diagonalOppositeSpan={oppositeSideSpan.ToString(CultureInfo.InvariantCulture)} crossPartnerGeom={crossPartnerGeom[s].ToString(CultureInfo.InvariantCulture)} crossPartnerContact={crossPartnerContact[s].ToString(CultureInfo.InvariantCulture)} anyCross={anyCross.ToString(CultureInfo.InvariantCulture)} " +
                    $"diagonalScore={diagonalScore.ToString("F3", CultureInfo.InvariantCulture)} xParticipantScore={xParticipantScore.ToString(CultureInfo.InvariantCulture)} " +
                    $"class={RoofCrossTriFamilyKindToToken(kind)} reason=TRIANGLE_FAMILY_AUDIT",
                    this);
            }

            if (wantDraw)
            {
                Color col = RoofCrossTriFamilyKindToColor(kind);
                _roofCrossFamilyGizmoA.Add(v0);
                _roofCrossFamilyGizmoB.Add(v1);
                _roofCrossFamilyGizmoC.Add(v2);
                _roofCrossFamilyGizmoColors.Add(col);
            }
        }

        xCandList.Sort();
        xPartList.Sort();
        parasiteList.Sort();

        var suspectTight = new List<int>(parasiteList.Count + xPartList.Count);
        suspectTight.AddRange(xPartList);
        foreach (int p in parasiteList)
        {
            if (!suspectTight.Contains(p))
                suspectTight.Add(p);
        }

        suspectTight.Sort();
        LastRoofCrossSuspectTriangleSlotsDisplay = FormatIntListForDiagLog(suspectTight);
        LastRoofCrossXDiagonalCandidatesDisplay = FormatIntListForDiagLog(xCandList);
        LastRoofCrossXCrossParticipantsDisplay = FormatIntListForDiagLog(xPartList);
        LastRoofCrossParasiteLikelySlotsDisplay = FormatIntListForDiagLog(parasiteList);

        if (wantLogs)
        {
            string normalReport =
                normalSlots.Count <= 14
                    ? $"({FormatIntListForDiagLog(normalSlots)})"
                    : $"count={normalSlots.Count.ToString(CultureInfo.InvariantCulture)}";
            Debug.Log(
                $"[RoofCrossDiagFamily] xDiagonalCandidates=({FormatIntListForDiagLog(xCandList)}) xCrossParticipants=({FormatIntListForDiagLog(xPartList)}) parasiteLikely=({FormatIntListForDiagLog(parasiteList)}) normalTriangles={normalReport} reason=X_FAMILY_AUDIT_SUMMARY",
                this);

            int nn = nonNormalSlots.Count;
            float broadRatio = nTri > 0 ? (float)nn / nTri : 0f;
            if (nn >= Mathf.Max(4, nTri - 1) || (nTri >= 5 && broadRatio >= 0.55f))
            {
                Debug.LogWarning(
                    $"[RoofCrossDiagFamily] WARNING_TOO_MANY_X_SUSPECTS suspiciousCount={nn.ToString(CultureInfo.InvariantCulture)} triangles=({FormatIntListForDiagLog(nonNormalSlots)}) reason=AUDIT_TOO_BROAD_NOT_ISOLATING_X",
                    this);
            }
        }
    }

    /// <summary>
    /// Diagnostic uniquement : triangles déjà dans <paramref name="roofTris"/> (shell principal),
    /// avant duplication épaisseur / connecteurs.
    /// </summary>
    void DebugDetectRoofShellCrossingProblemsDetailed(
        List<Vector3> verts,
        List<int> roofTris,
        Vector2 footprintCentroidXZ,
        List<Vector3> footprintCornersWorld,
        bool dualCornerAnchorMode)
    {
        if (!IsRoofCrossShellDiagnosticEnabled)
            return;
        if (verts == null || roofTris == null || roofTris.Count < 9)
            return;

        ResetRoofCrossDiagStateForScan();

        float fpRadius = ComputeFootprintHorizontalRadius(footprintCornersWorld, footprintCentroidXZ);
        float scaleEps = Mathf.Max(1e-4f, ComputeVertsXZDiagonal(verts) * 1e-7f);

        int triCount = roofTris.Count / 3;
        int legacyStrictPairs = 0;
        int problems = 0;
        int nearCentroidHits = 0;
        const int maxProblemsLogged = 48;
        var triRawProblemHits = new int[triCount];
        var triStrongHits = new int[triCount];
        var triWeakHits = new int[triCount];
        var triPartners = new HashSet<int>[triCount];
        for (int i = 0; i < triCount; i++)
            triPartners[i] = new HashSet<int>();

        for (int ti = 0; ti < triCount && problems < maxProblemsLogged; ti++)
        {
            int ia0 = roofTris[ti * 3];
            int ia1 = roofTris[ti * 3 + 1];
            int ia2 = roofTris[ti * 3 + 2];

            for (int tj = ti + 1; tj < triCount && problems < maxProblemsLogged; tj++)
            {
                int ja0 = roofTris[tj * 3];
                int ja1 = roofTris[tj * 3 + 1];
                int ja2 = roofTris[tj * 3 + 2];

                if (TriangleDegenerateXZ(verts, ia0, ia1, ia2, scaleEps) ||
                    TriangleDegenerateXZ(verts, ja0, ja1, ja2, scaleEps))
                    continue;

                for (int ea = 0; ea < 3; ea++)
                {
                    int eai0 = ea == 0 ? ia0 : ea == 1 ? ia1 : ia2;
                    int eai1 = ea == 0 ? ia1 : ea == 1 ? ia2 : ia0;
                    Vector2 a0 = VertXZ(verts[eai0]);
                    Vector2 a1 = VertXZ(verts[eai1]);

                    for (int eb = 0; eb < 3; eb++)
                    {
                        int ebj0 = eb == 0 ? ja0 : eb == 1 ? ja1 : ja2;
                        int ebj1 = eb == 0 ? ja1 : eb == 1 ? ja2 : ja0;
                        if (UndirectedEdgesEqual(eai0, eai1, ebj0, ebj1))
                            continue;

                        if (EdgesShareEndpoint(eai0, eai1, ebj0, ebj1))
                            continue;

                        bool legacyStrictThisPair = SegmentsIntersectStrict(a0, a1, VertXZ(verts[ebj0]), VertXZ(verts[ebj1]));
                        if (legacyStrictThisPair)
                            legacyStrictPairs++;

                        if (!TryIntersectSegmentsXZRobust(
                                a0, a1, VertXZ(verts[ebj0]), VertXZ(verts[ebj1]),
                                scaleEps,
                                out Vector2 hitXZ,
                                out bool strictInterior,
                                out _))
                            continue;

                        bool nearCentroid = (hitXZ - footprintCentroidXZ).sqrMagnitude <= (fpRadius * 0.18f) * (fpRadius * 0.18f);
                        if (nearCentroid)
                            nearCentroidHits++;

                        problems++;
                        triRawProblemHits[ti]++;
                        triRawProblemHits[tj]++;
                        if (strictInterior)
                        {
                            triStrongHits[ti]++;
                            triStrongHits[tj]++;
                        }
                        else
                        {
                            triWeakHits[ti]++;
                            triWeakHits[tj]++;
                        }

                        triPartners[ti].Add(tj);
                        triPartners[tj].Add(ti);
                        goto NextPair;
                    }
                }

                TryReportStrictInteriorVertexInsideOtherTriXZ(
                    verts, ia0, ia1, ia2, ja0, ja1, ja2,
                    ti, tj,
                    fpRadius, scaleEps,
                    triRawProblemHits,
                    triStrongHits, triWeakHits, triPartners,
                    ref problems, maxProblemsLogged);

            NextPair:
                ;
            }
        }

        FinalizeRoofCrossDiagScanHonest(
            problems, triCount, dualCornerAnchorMode,
            footprintCentroidXZ, fpRadius, nearCentroidHits, legacyStrictPairs);

        RunRoofCrossTriangleFamilyAuditAfterScan(
            verts, roofTris, footprintCentroidXZ, fpRadius, problems,
            triRawProblemHits, triStrongHits, triWeakHits, triPartners);
    }

    static float ComputeFootprintHorizontalRadius(List<Vector3> corners, Vector2 centroidXZ)
    {
        float r = 0.5f;
        if (corners == null)
            return r;
        for (int i = 0; i < corners.Count; i++)
        {
            float dx = corners[i].x - centroidXZ.x;
            float dz = corners[i].z - centroidXZ.y;
            r = Mathf.Max(r, Mathf.Sqrt(dx * dx + dz * dz));
        }

        return r;
    }

    static float ComputeVertsXZDiagonal(List<Vector3> verts)
    {
        if (verts == null || verts.Count == 0)
            return 10f;
        float minX = verts[0].x, maxX = verts[0].x, minZ = verts[0].z, maxZ = verts[0].z;
        for (int i = 1; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            minX = Mathf.Min(minX, v.x);
            maxX = Mathf.Max(maxX, v.x);
            minZ = Mathf.Min(minZ, v.z);
            maxZ = Mathf.Max(maxZ, v.z);
        }

        float dx = maxX - minX;
        float dz = maxZ - minZ;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static Vector2 VertXZ(Vector3 v) => new Vector2(v.x, v.z);

    static float DistPointToSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-14f)
            return (p - a).magnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        Vector2 close = a + ab * t;
        return (p - close).magnitude;
    }

    /// <summary>
    /// Plus petit |cos θ| entre une arête du triangle A et une arête du triangle B (XZ).
    /// Deux pans qui forment un X ont au moins une paire d’arêtes non parallèles même si la « plus longue » arête ne la reflète pas.
    /// </summary>
    static float MinAbsDotBetweenTriangleEdgesXZ(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 b0, Vector2 b1, Vector2 b2)
    {
        float minAbsDot = 1f;

        void Consider(Vector2 p0, Vector2 p1, Vector2 q0, Vector2 q1)
        {
            Vector2 d0 = p1 - p0;
            Vector2 d1 = q1 - q0;
            float m0 = d0.magnitude;
            float m1 = d1.magnitude;
            if (m0 < 1e-8f || m1 < 1e-8f)
                return;
            d0 /= m0;
            d1 /= m1;
            minAbsDot = Mathf.Min(minAbsDot, Mathf.Abs(Vector2.Dot(d0, d1)));
        }

        Consider(a0, a1, b0, b1);
        Consider(a0, a1, b1, b2);
        Consider(a0, a1, b2, b0);
        Consider(a1, a2, b0, b1);
        Consider(a1, a2, b1, b2);
        Consider(a1, a2, b2, b0);
        Consider(a2, a0, b0, b1);
        Consider(a2, a0, b1, b2);
        Consider(a2, a0, b2, b0);

        return minAbsDot;
    }

    static bool RoofPartnerTrisEdgeFamiliesCrossingXZ(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 b0, Vector2 b1, Vector2 b2) =>
        MinAbsDotBetweenTriangleEdgesXZ(a0, a1, a2, b0, b1, b2) < 0.65f;

    static bool TriangleDegenerateXZ(List<Vector3> verts, int i0, int i1, int i2, float eps)
    {
        Vector2 a = VertXZ(verts[i0]);
        Vector2 b = VertXZ(verts[i1]);
        Vector2 c = VertXZ(verts[i2]);
        float area = Mathf.Abs(Cross2(b - a, c - a));
        return area <= eps * eps * 100f;
    }

    static bool UndirectedEdgesEqual(int a0, int a1, int b0, int b1)
    {
        int eaMin = Mathf.Min(a0, a1);
        int eaMax = Mathf.Max(a0, a1);
        int ebMin = Mathf.Min(b0, b1);
        int ebMax = Mathf.Max(b0, b1);
        return eaMin == ebMin && eaMax == ebMax;
    }

    static bool EdgesShareEndpoint(int a0, int a1, int b0, int b1) =>
        a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1;

    static void ClassifyTriPairTopology(
        int ia0, int ia1, int ia2,
        int ja0, int ja1, int ja2,
        out int sharedVertCount,
        out bool shareEdge)
    {
        sharedVertCount = CountSharedVerticesBetweenTris(ia0, ia1, ia2, ja0, ja1, ja2);
        shareEdge = ShareUndirectedEdgeBetweenTris(ia0, ia1, ia2, ja0, ja1, ja2);
    }

    static int CountSharedVerticesBetweenTris(int a0, int a1, int a2, int b0, int b1, int b2)
    {
        var s = new HashSet<int> { a0, a1, a2 };
        int n = 0;
        if (s.Contains(b0)) n++;
        if (s.Contains(b1)) n++;
        if (s.Contains(b2)) n++;
        return n;
    }

    static bool ShareUndirectedEdgeBetweenTris(int a0, int a1, int a2, int b0, int b1, int b2)
    {
        int[] ae = { a0, a1, a2 };
        int[] be = { b0, b1, b2 };
        for (int i = 0; i < 3; i++)
        {
            int u0 = ae[i];
            int u1 = ae[(i + 1) % 3];
            for (int j = 0; j < 3; j++)
            {
                int v0 = be[j];
                int v1 = be[(j + 1) % 3];
                if (UndirectedEdgesEqual(u0, u1, v0, v1))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Détecte intersections XZ y compris contacts sur sommets sans sommet commun (anciennement « CLEAN » avec SegmentsIntersectStrict seul).
    /// </summary>
    static bool TryIntersectSegmentsXZRobust(
        Vector2 a0, Vector2 a1,
        Vector2 b0, Vector2 b1,
        float eps,
        out Vector2 hitXZ,
        out bool strictInterior,
        out bool endpointTouch)
    {
        hitXZ = default;
        strictInterior = false;
        endpointTouch = false;

        Vector2 da = a1 - a0;
        Vector2 db = b1 - b0;
        Vector2 r = b0 - a0;
        float cross = Cross2(da, db);
        if (Mathf.Abs(cross) < eps * 1e-3f)
            return false;

        float t = Cross2(r, db) / cross;
        float u = Cross2(r, da) / cross;
        hitXZ = a0 + da * t;

        bool tIn = t >= -eps && t <= 1f + eps && u >= -eps && u <= 1f + eps;
        if (!tIn)
            return false;

        strictInterior = t > eps && t < 1f - eps && u > eps && u < 1f - eps;
        endpointTouch = tIn && !strictInterior;
        return tIn;
    }

    static Vector3 ProjectHitOntoDominantParasiteEdge(
        List<Vector3> verts,
        Vector2 footprintCentroidXZ,
        int ia0, int ia1, int ib0, int ib1,
        Vector2 hitXZ)
    {
        Vector2 ma = (VertXZ(verts[ia0]) + VertXZ(verts[ia1])) * 0.5f;
        Vector2 mb = (VertXZ(verts[ib0]) + VertXZ(verts[ib1])) * 0.5f;
        float da = (ma - footprintCentroidXZ).sqrMagnitude;
        float db = (mb - footprintCentroidXZ).sqrMagnitude;
        int p0 = da <= db ? ia0 : ib0;
        int p1 = da <= db ? ia1 : ib1;
        return ClosestPointOnSegmentXZWithLerpY(verts, p0, p1, hitXZ);
    }

    static Vector3 ClosestPointOnSegmentXZWithLerpY(List<Vector3> verts, int i0, int i1, Vector2 hitXZ)
    {
        Vector2 p0 = VertXZ(verts[i0]);
        Vector2 p1 = VertXZ(verts[i1]);
        Vector2 d = p1 - p0;
        float lenSq = d.sqrMagnitude;
        float t = lenSq > 1e-12f ? Mathf.Clamp01(Vector2.Dot(hitXZ - p0, d) / lenSq) : 0f;
        float y = Mathf.Lerp(verts[i0].y, verts[i1].y, t);
        Vector2 px = p0 + d * t;
        return new Vector3(px.x, y, px.y);
    }

    static int PickHighestVertexAmong(int a, int b, int c, int d, List<Vector3> verts)
    {
        int best = a;
        float y = verts[a].y;
        if (verts[b].y > y) { y = verts[b].y; best = b; }
        if (verts[c].y > y) { y = verts[c].y; best = c; }
        if (verts[d].y > y) best = d;
        return best;
    }

    /// <summary>
    /// Couvre les chevauchements sans croisement strict d’arête (sommet au centre projeté).
    /// </summary>
    static void TryReportStrictInteriorVertexInsideOtherTriXZ(
        List<Vector3> verts,
        int ia0, int ia1, int ia2,
        int ja0, int ja1, int ja2,
        int triSlotA,
        int triSlotB,
        float fpRadius,
        float posEps,
        int[] triRawProblemHits,
        int[] triStrongHits,
        int[] triWeakHits,
        HashSet<int>[] triPartners,
        ref int problems,
        int maxProblems)
    {
        if (problems >= maxProblems)
            return;

        TryOneWayInside(
            verts, ia0, ia1, ia2, ja0, ja1, ja2, triSlotA, triSlotB,
            fpRadius, posEps, triRawProblemHits, triStrongHits, triWeakHits, triPartners,
            ref problems, maxProblems);
        if (problems >= maxProblems)
            return;
        TryOneWayInside(
            verts, ja0, ja1, ja2, ia0, ia1, ia2, triSlotB, triSlotA,
            fpRadius, posEps, triRawProblemHits, triStrongHits, triWeakHits, triPartners,
            ref problems, maxProblems);
    }

    static void TryOneWayInside(
        List<Vector3> verts,
        int o0, int o1, int o2,
        int h0, int h1, int h2,
        int triSlotOwner,
        int triSlotHost,
        float fpRadius,
        float posEps,
        int[] triRawProblemHits,
        int[] triStrongHits,
        int[] triWeakHits,
        HashSet<int>[] triPartners,
        ref int problems,
        int maxProblems)
    {
        if (problems >= maxProblems)
            return;

        Vector2 t0 = VertXZ(verts[h0]);
        Vector2 t1 = VertXZ(verts[h1]);
        Vector2 t2 = VertXZ(verts[h2]);
        float triArea = Mathf.Abs(Cross2(t1 - t0, t2 - t0));
        if (triArea <= posEps * fpRadius * 4f)
            return;

        for (int k = 0; k < 3; k++)
        {
            int vk = k == 0 ? o0 : k == 1 ? o1 : o2;
            if (vk == h0 || vk == h1 || vk == h2)
                continue;

            Vector3 pw = verts[vk];
            if (VertsProbablyCoincidentXZ(pw, verts[h0], posEps) ||
                VertsProbablyCoincidentXZ(pw, verts[h1], posEps) ||
                VertsProbablyCoincidentXZ(pw, verts[h2], posEps))
                continue;

            Vector2 p = VertXZ(pw);
            float minB = Mathf.Max(0.004f, fpRadius * 1e-5f);
            if (!PointInTriangleXZBarycentricStrict(p, t0, t1, t2, minB))
                continue;

            if (triRawProblemHits != null)
            {
                if (triSlotOwner >= 0 && triSlotOwner < triRawProblemHits.Length)
                    triRawProblemHits[triSlotOwner]++;
                if (triSlotHost >= 0 && triSlotHost < triRawProblemHits.Length)
                    triRawProblemHits[triSlotHost]++;
            }

            if (triStrongHits != null)
            {
                if (triSlotOwner >= 0 && triSlotOwner < triStrongHits.Length)
                    triStrongHits[triSlotOwner]++;
                if (triSlotHost >= 0 && triSlotHost < triStrongHits.Length)
                    triStrongHits[triSlotHost]++;
            }

            if (triPartners != null)
            {
                if (triSlotOwner >= 0 && triSlotOwner < triPartners.Length && triPartners[triSlotOwner] != null &&
                    triSlotHost >= 0)
                    triPartners[triSlotOwner].Add(triSlotHost);
                if (triSlotHost >= 0 && triSlotHost < triPartners.Length && triPartners[triSlotHost] != null &&
                    triSlotOwner >= 0)
                    triPartners[triSlotHost].Add(triSlotOwner);
            }

            problems++;
            return;
        }
    }

    static bool VertsProbablyCoincidentXZ(Vector3 a, Vector3 b, float eps)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz <= eps * eps && Mathf.Abs(a.y - b.y) <= eps * 10f;
    }

    /// <summary>Barycentriques stricts (évite les faux positifs sur le pourtour).</summary>
    static bool PointInTriangleXZBarycentricStrict(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float minCoord)
    {
        Vector2 v0 = c - a;
        Vector2 v1 = b - a;
        Vector2 v2 = p - a;
        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);
        float den = d00 * d11 - d01 * d01;
        if (Mathf.Abs(den) < 1e-14f)
            return false;

        float v = (d11 * d20 - d01 * d21) / den;
        float w = (d00 * d21 - d01 * d20) / den;
        float u = 1f - v - w;
        return u > minCoord && v > minCoord && w > minCoord;
    }

    #endregion

    #region Roof shell crossing local fix (uses RoofCrossDiag cut heuristic)

    struct RoofShellEdgeCrossProblem
    {
        public int TriA;
        public int TriB;
        public int Ia0, Ia1, Ia2;
        public int Ja0, Ja1, Ja2;
        public int Eai0, Eai1, Ebj0, Ebj1;
        public Vector2 HitXZ;
        public int VertexToShorten;
        public Vector3 CutPosition;
        public bool DominantEdgeFromTriA;
    }

    /// <summary>
    /// Premier croisement XZ entre arêtes de deux triangles (hors arêtes identiques / sommets d’arête communs),
    /// comme le diagnostic détaillé.
    /// </summary>
    static bool TryFindFirstRoofShellEdgeCrossProblem(
        List<Vector3> verts,
        List<int> roofTris,
        Vector2 footprintCentroidXZ,
        float scaleEps,
        out RoofShellEdgeCrossProblem problem)
    {
        problem = default;
        int triCount = roofTris.Count / 3;
        for (int ti = 0; ti < triCount; ti++)
        {
            int ia0 = roofTris[ti * 3];
            int ia1 = roofTris[ti * 3 + 1];
            int ia2 = roofTris[ti * 3 + 2];

            for (int tj = ti + 1; tj < triCount; tj++)
            {
                int ja0 = roofTris[tj * 3];
                int ja1 = roofTris[tj * 3 + 1];
                int ja2 = roofTris[tj * 3 + 2];

                if (TriangleDegenerateXZ(verts, ia0, ia1, ia2, scaleEps) ||
                    TriangleDegenerateXZ(verts, ja0, ja1, ja2, scaleEps))
                    continue;

                for (int ea = 0; ea < 3; ea++)
                {
                    int eai0 = ea == 0 ? ia0 : ea == 1 ? ia1 : ia2;
                    int eai1 = ea == 0 ? ia1 : ea == 1 ? ia2 : ia0;
                    Vector2 a0 = VertXZ(verts[eai0]);
                    Vector2 a1 = VertXZ(verts[eai1]);

                    for (int eb = 0; eb < 3; eb++)
                    {
                        int ebj0 = eb == 0 ? ja0 : eb == 1 ? ja1 : ja2;
                        int ebj1 = eb == 0 ? ja1 : eb == 1 ? ja2 : ja0;
                        if (UndirectedEdgesEqual(eai0, eai1, ebj0, ebj1))
                            continue;

                        if (EdgesShareEndpoint(eai0, eai1, ebj0, ebj1))
                            continue;

                        if (!TryIntersectSegmentsXZRobust(
                                a0, a1, VertXZ(verts[ebj0]), VertXZ(verts[ebj1]),
                                scaleEps,
                                out Vector2 hitXZ,
                                out _, out _))
                            continue;

                        Vector2 ma = (VertXZ(verts[eai0]) + VertXZ(verts[eai1])) * 0.5f;
                        Vector2 mb = (VertXZ(verts[ebj0]) + VertXZ(verts[ebj1])) * 0.5f;
                        bool dominantA = (ma - footprintCentroidXZ).sqrMagnitude <= (mb - footprintCentroidXZ).sqrMagnitude;

                        int vs = PickHighestVertexAmong(eai0, eai1, ebj0, ebj1, verts);
                        Vector3 cut = ProjectHitOntoDominantParasiteEdge(
                            verts, footprintCentroidXZ, eai0, eai1, ebj0, ebj1, hitXZ);

                        problem = new RoofShellEdgeCrossProblem
                        {
                            TriA = ti,
                            TriB = tj,
                            Ia0 = ia0,
                            Ia1 = ia1,
                            Ia2 = ia2,
                            Ja0 = ja0,
                            Ja1 = ja1,
                            Ja2 = ja2,
                            Eai0 = eai0,
                            Eai1 = eai1,
                            Ebj0 = ebj0,
                            Ebj1 = ebj1,
                            HitXZ = hitXZ,
                            VertexToShorten = vs,
                            CutPosition = cut,
                            DominantEdgeFromTriA = dominantA
                        };
                        return true;
                    }
                }
            }
        }

        return false;
    }

    static bool RoofTriContainsVertex(List<int> roofTris, int triSlotIndex, int vertIdx)
    {
        int b = triSlotIndex * 3;
        return roofTris[b] == vertIdx || roofTris[b + 1] == vertIdx || roofTris[b + 2] == vertIdx;
    }

    static int PickTargetTriangleForFix(List<int> roofTris, in RoofShellEdgeCrossProblem p)
    {
        int domTri = p.DominantEdgeFromTriA ? p.TriA : p.TriB;
        int othTri = p.DominantEdgeFromTriA ? p.TriB : p.TriA;
        int vs = p.VertexToShorten;
        if (RoofTriContainsVertex(roofTris, domTri, vs))
            return domTri;
        if (RoofTriContainsVertex(roofTris, othTri, vs))
            return othTri;
        return domTri;
    }

    /// <summary>
    /// Vrai si ce triangle forme encore une intersection XZ avec un autre (même critère que le diagnostic).
    /// </summary>
    static bool RoofShellTriangleHasXZIntersectionWithSoup(
        List<Vector3> verts,
        List<int> roofTris,
        int focusTriIndex,
        float scaleEps)
    {
        int triCount = roofTris.Count / 3;
        int ia0 = roofTris[focusTriIndex * 3];
        int ia1 = roofTris[focusTriIndex * 3 + 1];
        int ia2 = roofTris[focusTriIndex * 3 + 2];

        if (TriangleDegenerateXZ(verts, ia0, ia1, ia2, scaleEps))
            return true;

        for (int tj = 0; tj < triCount; tj++)
        {
            if (tj == focusTriIndex)
                continue;

            int ja0 = roofTris[tj * 3];
            int ja1 = roofTris[tj * 3 + 1];
            int ja2 = roofTris[tj * 3 + 2];

            if (TriangleDegenerateXZ(verts, ja0, ja1, ja2, scaleEps))
                continue;

            for (int ea = 0; ea < 3; ea++)
            {
                int eai0 = ea == 0 ? ia0 : ea == 1 ? ia1 : ia2;
                int eai1 = ea == 0 ? ia1 : ea == 1 ? ia2 : ia0;
                Vector2 a0 = VertXZ(verts[eai0]);
                Vector2 a1 = VertXZ(verts[eai1]);

                for (int eb = 0; eb < 3; eb++)
                {
                    int ebj0 = eb == 0 ? ja0 : eb == 1 ? ja1 : ja2;
                    int ebj1 = eb == 0 ? ja1 : eb == 1 ? ja2 : ja0;
                    if (UndirectedEdgesEqual(eai0, eai1, ebj0, ebj1))
                        continue;

                    if (EdgesShareEndpoint(eai0, eai1, ebj0, ebj1))
                        continue;

                    if (TryIntersectSegmentsXZRobust(
                            a0, a1, VertXZ(verts[ebj0]), VertXZ(verts[ebj1]),
                            scaleEps,
                            out _, out _, out _))
                        return true;
                }
            }
        }

        return false;
    }

    static float XzDistSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    void TryApplyManualRoofShellCrossCut(List<Vector3> verts, List<Vector2> uvs, List<int> roofTris)
    {
        if (!manualRoofShellCrossCut)
            return;
        int nTri = roofTris != null ? roofTris.Count / 3 : 0;
        Debug.Log(
            $"[RoofCrossFix:manual] apply cut triSlot={manualRoofShellCrossTriSlot.ToString(CultureInfo.InvariantCulture)} corner={manualRoofShellCrossTriCorner.ToString(CultureInfo.InvariantCulture)} " +
            $"shellTriangleCount={nTri.ToString(CultureInfo.InvariantCulture)} cutWorld=({manualRoofShellCrossCutWorld.x.ToString("F4", CultureInfo.InvariantCulture)},{manualRoofShellCrossCutWorld.y.ToString("F4", CultureInfo.InvariantCulture)},{manualRoofShellCrossCutWorld.z.ToString("F4", CultureInfo.InvariantCulture)})",
            this);
        TryRoofShellCutReplaceTriangleCorner(
            verts, uvs, roofTris,
            manualRoofShellCrossTriSlot,
            manualRoofShellCrossTriCorner,
            manualRoofShellCrossCutWorld,
            logTag: "manual");
    }

    /// <summary>
    /// Coupe / supprime des triangles du shell listés par slot (avant épaisseur). Debug uniquement.
    /// </summary>
    void ApplyExperimentalRoofShellTriangleCuts(List<Vector3> verts, List<Vector2> uvs, List<int> roofTris, Vector2 footprintCentroidXZ)
    {
        if (!experimentalCutRawProblemTriangles || verts == null || uvs == null || roofTris == null)
            return;

        int nTri = roofTris.Count / 3;
        _lastRoofShellTriangleCountForExperimental = nTri;

        if (nTri <= 0)
            return;

        List<int> rawSlots;

        if (experimentalUseSingleTriangleSlot)
        {
            Debug.Log(
                $"[RoofExperimentalCut] singleSlot triSlot={experimentalSingleTriangleSlot.ToString(CultureInfo.InvariantCulture)} reason=EXPERIMENTAL_SINGLE_TRIANGLE_SLOT",
                this);
            rawSlots = new List<int> { experimentalSingleTriangleSlot };
        }
        else
        {
            string spec = experimentalCutTriangleSlots != null ? experimentalCutTriangleSlots.Trim() : "";
            if (string.IsNullOrEmpty(spec))
            {
                Debug.Log("[RoofExperimentalCut] skipped reason=NO_CUT_SLOTS_DEFINED", this);
                return;
            }

            if (!TryParseExperimentalTriangleSlots(spec, out rawSlots) || rawSlots.Count == 0)
            {
                Debug.Log("[RoofExperimentalCut] skipped reason=NO_CUT_SLOTS_DEFINED", this);
                return;
            }
        }

        var uniqueSlots = new List<int>();
        var seen = new HashSet<int>();
        for (int i = 0; i < rawSlots.Count; i++)
        {
            int s = rawSlots[i];
            if (seen.Add(s))
                uniqueSlots.Add(s);
        }

        if (experimentalRemoveTrianglesInsteadOfShorten)
        {
            uniqueSlots.Sort((a, b) => b.CompareTo(a));
            for (int i = 0; i < uniqueSlots.Count; i++)
                ExperimentalRemoveTriangleAtSlot(roofTris, uniqueSlots[i]);
        }
        else
        {
            float amt = Mathf.Clamp01(experimentalCutAmount);
            for (int i = 0; i < uniqueSlots.Count; i++)
                ExperimentalShortenTriangleAtSlot(verts, uvs, roofTris, uniqueSlots[i], footprintCentroidXZ, amt);
        }
    }

    static bool TryParseExperimentalTriangleSlots(string s, out List<int> slots)
    {
        slots = new List<int>();
        if (string.IsNullOrWhiteSpace(s))
            return false;
        foreach (string part in s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = part.Trim();
            if (t.Length == 0)
                continue;
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                slots.Add(v);
        }

        return slots.Count > 0;
    }

    void ExperimentalRemoveTriangleAtSlot(List<int> roofTris, int slot)
    {
        int nTri = roofTris.Count / 3;
        if (slot < 0 || slot >= nTri)
        {
            Debug.Log(
                $"[RoofExperimentalCut] skipped triSlot={slot.ToString(CultureInfo.InvariantCulture)} reason=INVALID_TRI_SLOT",
                this);
            return;
        }

        int b = slot * 3;
        int a0 = roofTris[b];
        int a1 = roofTris[b + 1];
        int a2 = roofTris[b + 2];
        roofTris.RemoveRange(b, 3);
        Debug.Log(
            $"[RoofExperimentalCut] removed triSlot={slot.ToString(CultureInfo.InvariantCulture)} tri=({a0.ToString(CultureInfo.InvariantCulture)},{a1.ToString(CultureInfo.InvariantCulture)},{a2.ToString(CultureInfo.InvariantCulture)}) reason=EXPERIMENTAL_REMOVE_TRIANGLE",
            this);
    }

    void ExperimentalShortenTriangleAtSlot(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        int slot,
        Vector2 footprintCentroidXZ,
        float cutAmount)
    {
        int nTri = roofTris.Count / 3;
        if (slot < 0 || slot >= nTri)
        {
            Debug.Log(
                $"[RoofExperimentalCut] skipped triSlot={slot.ToString(CultureInfo.InvariantCulture)} reason=INVALID_TRI_SLOT",
                this);
            return;
        }

        int b = slot * 3;
        int i0 = roofTris[b];
        int i1 = roofTris[b + 1];
        int i2 = roofTris[b + 2];
        if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Count || i1 >= verts.Count || i2 >= verts.Count)
        {
            Debug.Log(
                $"[RoofExperimentalCut] skipped triSlot={slot.ToString(CultureInfo.InvariantCulture)} reason=INVALID_TRI_SLOT",
                this);
            return;
        }

        Vector3 v0 = verts[i0];
        Vector3 v1 = verts[i1];
        Vector3 v2 = verts[i2];
        Vector3 center = (v0 + v1 + v2) / 3f;

        Vector2 cXZ = footprintCentroidXZ;
        float d0 = (VertXZ(v0) - cXZ).sqrMagnitude;
        float d1 = (VertXZ(v1) - cXZ).sqrMagnitude;
        float d2 = (VertXZ(v2) - cXZ).sqrMagnitude;

        int farCorner;
        int farVertIndex;
        if (d0 >= d1 && d0 >= d2)
        {
            farCorner = 0;
            farVertIndex = i0;
        }
        else if (d1 >= d0 && d1 >= d2)
        {
            farCorner = 1;
            farVertIndex = i1;
        }
        else
        {
            farCorner = 2;
            farVertIndex = i2;
        }

        Vector3 newPos = Vector3.Lerp(verts[farVertIndex], center, cutAmount);

        verts.Add(newPos);
        uvs.Add(UvXZ(newPos));
        int newIdx = verts.Count - 1;
        roofTris[b + farCorner] = newIdx;

        int x = roofTris[b];
        int y = roofTris[b + 1];
        int z = roofTris[b + 2];
        Debug.Log(
            $"[RoofExperimentalCut] shortened triSlot={slot.ToString(CultureInfo.InvariantCulture)} originalTri=({i0.ToString(CultureInfo.InvariantCulture)},{i1.ToString(CultureInfo.InvariantCulture)},{i2.ToString(CultureInfo.InvariantCulture)}) newTri=({x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)},{z.ToString(CultureInfo.InvariantCulture)}) replacedVertex={farVertIndex.ToString(CultureInfo.InvariantCulture)} cutAmount={cutAmount.ToString(CultureInfo.InvariantCulture)} reason=EXPERIMENTAL_SHORTEN_TRIANGLE",
            this);
    }

    /// <summary>
    /// Duplique le sommet au point de coupe et remplace une référence dans un seul triangle du shell (logique commune auto / manuel).
    /// </summary>
    bool TryRoofShellCutReplaceTriangleCorner(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        int targetTriSlot,
        int triCorner012,
        Vector3 cutWorld,
        string logTag)
    {
        if (verts == null || uvs == null || roofTris == null || roofTris.Count < 9 || verts.Count != uvs.Count)
            return false;

        int triCount = roofTris.Count / 3;
        bool manualModeEarly = string.Equals(logTag, "manual", StringComparison.Ordinal);
        if (targetTriSlot < 0 || targetTriSlot >= triCount)
        {
            LogRoofCrossCutFail(logTag,
                $"skip reason=INVALID_TRI_SLOT slot={targetTriSlot.ToString(CultureInfo.InvariantCulture)} triCount={triCount.ToString(CultureInfo.InvariantCulture)}",
                manualModeEarly);
            return false;
        }

        int corner = Mathf.Clamp(triCorner012, 0, 2);
        int tb = targetTriSlot * 3;
        int vs = roofTris[tb + corner];

        float scaleEps = Mathf.Max(1e-4f, ComputeVertsXZDiagonal(verts) * 1e-7f);
        float minCutMoveSqAuto = scaleEps * scaleEps * 2500f;
        bool manualMode = manualModeEarly;
        // Manuel : distance 3D (sinon un point avec même X,Z qu’avant mais Y différent était refusé) ; seuil bien plus bas.
        float moveSq = manualMode
            ? (cutWorld - verts[vs]).sqrMagnitude
            : XzDistSq(cutWorld, verts[vs]);
        float minMoveThresholdSq = manualMode
            ? Mathf.Max(1e-12f, scaleEps * scaleEps * 0.04f)
            : minCutMoveSqAuto;

        int o0 = roofTris[tb];
        int o1 = roofTris[tb + 1];
        int o2 = roofTris[tb + 2];

        if (float.IsNaN(cutWorld.x) || float.IsInfinity(cutWorld.x) ||
            float.IsNaN(cutWorld.z) || float.IsInfinity(cutWorld.z))
        {
            LogRoofCrossCutFail(logTag, $"keep original tri=({o0},{o1},{o2}) reason=NO_VALID_PROPOSED_CUT_POINT", manualMode);
            return false;
        }

        if (moveSq < minMoveThresholdSq)
        {
            LogRoofCrossCutFail(logTag,
                $"keep original tri=({o0},{o1},{o2}) reason=CUT_TOO_CLOSE_TO_VERTEX moveSq={moveSq.ToString("G9", CultureInfo.InvariantCulture)} thresholdSq={minMoveThresholdSq.ToString("G9", CultureInfo.InvariantCulture)} (manuel=distance3D auto=xzSeulement)",
                manualMode);
            return false;
        }

        if (TriangleDegenerateXZ(verts, o0, o1, o2, scaleEps))
        {
            LogRoofCrossCutFail(logTag, $"keep original tri=({o0},{o1},{o2}) reason=NO_VALID_PROPOSED_CUT_POINT degenerateTri", manualMode);
            return false;
        }

        if (!RoofTriContainsVertex(roofTris, targetTriSlot, vs))
        {
            LogRoofCrossCutFail(logTag, $"keep original tri=({o0},{o1},{o2}) reason=CORNER_VERTEX_MISMATCH", manualMode);
            return false;
        }

        verts.Add(cutWorld);
        uvs.Add(UvXZ(cutWorld));
        int newIdx = verts.Count - 1;

        bool replaced = false;
        for (int k = 0; k < 3; k++)
        {
            if (roofTris[tb + k] != vs)
                continue;
            roofTris[tb + k] = newIdx;
            replaced = true;
            break;
        }

        if (!replaced)
        {
            verts.RemoveAt(verts.Count - 1);
            uvs.RemoveAt(uvs.Count - 1);
            LogRoofCrossCutFail(logTag, $"fallback original tri=({o0},{o1},{o2}) reason=CUT_FAILED_KEEP_ORIGINAL", manualMode);
            return false;
        }

        int n0 = roofTris[tb];
        int n1 = roofTris[tb + 1];
        int n2 = roofTris[tb + 2];

        if (TriangleDegenerateXZ(verts, n0, n1, n2, scaleEps))
        {
            roofTris[tb] = o0;
            roofTris[tb + 1] = o1;
            roofTris[tb + 2] = o2;
            verts.RemoveAt(verts.Count - 1);
            uvs.RemoveAt(uvs.Count - 1);
            LogRoofCrossCutFail(logTag, $"fallback original tri=({o0},{o1},{o2}) reason=CUT_FAILED_KEEP_ORIGINAL newDegenerate", manualMode);
            return false;
        }

        bool checkIntersection = !manualMode || manualRoofShellCrossCutRejectIfStillIntersecting;
        if (checkIntersection &&
            RoofShellTriangleHasXZIntersectionWithSoup(verts, roofTris, targetTriSlot, scaleEps))
        {
            roofTris[tb] = o0;
            roofTris[tb + 1] = o1;
            roofTris[tb + 2] = o2;
            verts.RemoveAt(verts.Count - 1);
            uvs.RemoveAt(uvs.Count - 1);
            LogRoofCrossCutFail(logTag,
                $"fallback original tri=({o0},{o1},{o2}) reason=SHORTENED_TRIANGLE_CREATES_NEW_PROBLEM (décoche manualRoofShellCrossCutRejectIfStillIntersecting pour forcer la coupe)",
                manualMode);
            return false;
        }

        Debug.Log(
            $"[RoofCrossFix:{logTag}] shortened triSlot={targetTriSlot.ToString(CultureInfo.InvariantCulture)} originalTri=({o0},{o1},{o2}) newTri=({n0},{n1},{n2}) replacedVertex={vs.ToString(CultureInfo.InvariantCulture)} " +
            $"cutPosition=({cutWorld.x.ToString("F4", CultureInfo.InvariantCulture)},{cutWorld.y.ToString("F4", CultureInfo.InvariantCulture)},{cutWorld.z.ToString("F4", CultureInfo.InvariantCulture)}) " +
            $"reason=USE_CUT_POINT",
            this);
        return true;
    }

    void LogRoofCrossCutFail(string logTag, string message, bool manualMode)
    {
        if (manualMode)
            Debug.LogWarning($"[RoofCrossFix:{logTag}] {message}", this);
        else
            Debug.Log($"[RoofCrossFix:{logTag}] {message}", this);
    }

    void TryApplyRoofShellCrossLocalFix(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> roofTris,
        Vector2 footprintCentroidXZ)
    {
        if (!applyRoofShellCrossLocalFix)
            return;
        if (verts == null || uvs == null || roofTris == null || roofTris.Count < 9 || verts.Count != uvs.Count)
            return;

        float scaleEps = Mathf.Max(1e-4f, ComputeVertsXZDiagonal(verts) * 1e-7f);

        if (!TryFindFirstRoofShellEdgeCrossProblem(
                verts, roofTris, footprintCentroidXZ, scaleEps,
                out RoofShellEdgeCrossProblem p))
            return;

        Vector3 cut = p.CutPosition;
        int vs = p.VertexToShorten;

        int targetTri = PickTargetTriangleForFix(roofTris, p);
        int corner = -1;
        int tbPick = targetTri * 3;
        for (int k = 0; k < 3; k++)
        {
            if (roofTris[tbPick + k] == vs)
            {
                corner = k;
                break;
            }
        }

        if (corner < 0)
        {
            Debug.Log($"[RoofCrossFix:auto] keep original reason=VERTEX_NOT_FOUND_IN_TARGET_TRI vertex={vs.ToString(CultureInfo.InvariantCulture)}", this);
            return;
        }

        TryRoofShellCutReplaceTriangleCorner(verts, uvs, roofTris, targetTri, corner, cut, logTag: "auto");
    }

    #endregion

    void ClearMesh()
    {
        if (_mesh != null)
            _mesh.Clear();
        if (_mr != null)
            _mr.enabled = false;
        Transform child = transform.Find(RoofChildName);
        if (child != null)
        {
            var bc = child.GetComponent<BoxCollider>();
            if (bc != null)
                bc.enabled = false;
        }
    }

    int ComputeHash()
    {
        unchecked
        {
            int h = 17;
            WallObject wall = GetComponent<WallObject>();
            WallEditShape edit = GetComponent<WallEditShape>();
            if (wall != null)
            {
                h = h * 31 + Mathf.RoundToInt(wall.height * 1000f);
                h = h * 31 + Mathf.RoundToInt(wall.thickness * 1000f);
            }
            h = h * 31 + Mathf.RoundToInt(roofHeightMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(overhangMeters * 1000f);
            h = h * 31 + (useLateralFaceSystem ? 1 : 0);
            int lac = lateralApexOffsetsXZ != null ? Mathf.Min(MaxLateralApexPoints, lateralApexOffsetsXZ.Count) : 0;
            h = h * 31 + lac;
            for (int li = 0; li < lac; li++)
            {
                h = h * 31 + Mathf.RoundToInt(lateralApexOffsetsXZ[li].x * 1000f);
                h = h * 31 + Mathf.RoundToInt(lateralApexOffsetsXZ[li].y * 1000f);
            }

            h = h * 31 + (lateralApexHandleEnabled ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(lateralApexOffsetXZ.x * 1000f);
            h = h * 31 + Mathf.RoundToInt(lateralApexOffsetXZ.y * 1000f);
            h = h * 31 + (secondLateralApexHandleEnabled ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(secondLateralApexOffsetXZ.x * 1000f);
            h = h * 31 + Mathf.RoundToInt(secondLateralApexOffsetXZ.y * 1000f);
            h = h * 31 + (disableRoofCornerAnchorsTemporary ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(roofCornerAnchorBlockRadius * 1000f);
            h = h * 31 + Mathf.RoundToInt(roofCornerAnchorPushDistance * 1000f);
            h = h * 31 + (lateralExtensionStructuralQuadAlongBaseEdge ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(lateralExtensionStructuralMergeVertexEpsilonMeters * 100000f);
            if (lateralFaceOffsetsXZ != null)
            {
                for (int i = 0; i < lateralFaceOffsetsXZ.Length; i++)
                {
                    h = h * 31 + Mathf.RoundToInt(lateralFaceOffsetsXZ[i].x * 1000f);
                    h = h * 31 + Mathf.RoundToInt(lateralFaceOffsetsXZ[i].y * 1000f);
                }
            }
            h = h * 31 + Mathf.RoundToInt(roofThicknessMeters * 1000f);
            h = h * 31 + (useDomeProfile ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(roundness * 1000f);
            h = h * 31 + Mathf.RoundToInt(yOffsetAboveWallTop * 1000f);
            h = h * 31 + Mathf.RoundToInt(RoofBuiltInVerticalLiftMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(EaveInsetPerpendicularToWallMeters * 1000f);
            h = h * 31 + (experimentalCutRawProblemTriangles ? 1 : 0);
            h = h * 31 + (experimentalUseSingleTriangleSlot ? 1 : 0);
            h = h * 31 + experimentalSingleTriangleSlot;
            h = h * 31 + (experimentalCutTriangleSlots != null ? experimentalCutTriangleSlots.GetHashCode() : 0);
            h = h * 31 + Mathf.RoundToInt(experimentalCutAmount * 1000f);
            h = h * 31 + (experimentalRemoveTrianglesInsteadOfShorten ? 1 : 0);
            List<Vector3> ring = edit != null
                ? (edit.IsClosedLoopPath ? edit.GetOverlayPathWorld() : edit.GetPreviewPathWorld())
                : null;
            if (ring != null)
            {
                int n = ring.Count;
                if (n >= 2 && Vector3.Distance(ring[0], ring[n - 1]) < 0.001f)
                    n--;
                for (int i = 0; i < n; i++)
                {
                    h = h * 31 + Mathf.RoundToInt(ring[i].x * 100f);
                    h = h * 31 + Mathf.RoundToInt(ring[i].z * 100f);
                }
            }
            return h;
        }
    }

    static bool TryTriangulateEarClip(List<Vector2> poly, out List<int> triangles)
    {
        triangles = null;
        int n = poly != null ? poly.Count : 0;
        if (n < 3)
            return false;

        // Ensure CCW winding
        if (SignedArea(poly) < 0f)
            poly.Reverse();

        var idx = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);
        var tris = new List<int>((n - 2) * 3);
        int guard = 0;
        while (idx.Count > 3 && guard++ < n * n + 8)
        {
            bool clipped = false;
            int m = idx.Count;
            for (int k = 0; k < m; k++)
            {
                int iPrev = idx[(k + m - 1) % m];
                int iCur = idx[k];
                int iNext = idx[(k + 1) % m];
                Vector2 a = poly[iPrev];
                Vector2 b = poly[iCur];
                Vector2 c = poly[iNext];
                if (Cross2(b - a, c - b) <= TriEps)
                    continue;
                bool anyInside = false;
                for (int t = 0; t < m; t++)
                {
                    int iv = idx[t];
                    if (iv == iPrev || iv == iCur || iv == iNext)
                        continue;
                    if (PointInTriangle(poly[iv], a, b, c))
                    {
                        anyInside = true;
                        break;
                    }
                }
                if (anyInside)
                    continue;
                tris.Add(iPrev); tris.Add(iCur); tris.Add(iNext);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped)
                return false;
        }

        if (idx.Count == 3)
        {
            tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            triangles = tris;
            return true;
        }
        return false;
    }

    static float SignedArea(List<Vector2> p)
    {
        double a = 0.0;
        for (int i = 0; i < p.Count; i++)
        {
            int j = (i + 1) % p.Count;
            a += (double)p[i].x * p[j].y - (double)p[j].x * p[i].y;
        }
        return (float)(0.5 * a);
    }

    static float Cross2(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross2(b - a, p - a);
        float c2 = Cross2(c - b, p - b);
        float c3 = Cross2(a - c, p - c);
        bool hasNeg = c1 < -TriEps || c2 < -TriEps || c3 < -TriEps;
        bool hasPos = c1 > TriEps || c2 > TriEps || c3 > TriEps;
        return !(hasNeg && hasPos);
    }
}
