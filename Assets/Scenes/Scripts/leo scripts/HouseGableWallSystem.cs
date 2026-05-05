using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remplissage simple des ouvertures sous le toit (murs pignon) entre le haut du mur vertical et la pente du toit.
/// Composant séparé : ne modifie pas le cladding mur/toit ni les générateurs existants.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(WallObject))]
[DefaultExecutionOrder(32000)]
public class HouseGableWallSystem : MonoBehaviour
{
    public const string GableRootChildName = "__HouseGableWalls";

    [Tooltip("Écart vertical sous le faîtage pour éviter le z-fighting avec la sous-face du toit / la feuillure (m).")]
    [SerializeField] float roofUndersideClearanceMeters = 0.018f;

    [Tooltip("Descend légèrement la base du pignon sous wallTop pour éviter le z-fighting avec le mesh du mur ou le cladding (m).")]
    [SerializeField] float bottomEdgeDropBelowWallTopMeters = 0.006f;

    [Tooltip("Allonge la base du pignon le long de l’arête aux deux bouts pour combler le jeu sous débord du toit (m minimum).")]
    [SerializeField] float gableEdgeEndpointExtendMinMeters = 0.022f;

    [Tooltip("Allonge la base en proportion du débord du toit (overhang).")]
    [SerializeField] float gableEdgeExtendOverhangFraction = 0.5f;

    [Tooltip("Plafond : extension ≤ cette fraction de la longueur d’arête (évite de croiser l’arête voisine sur maillages courts).")]
    [SerializeField] float gableEdgeExtendMaxEdgeFraction = 0.28f;

    [Tooltip("Rayon XZ (m) autour du milieu de façade pour chercher le sommet du toit ; proportionnel à la longueur d’arête si > 1.")]
    [SerializeField] float apexSearchRadiusAlongEdgeFactor = 0.65f;

    [Tooltip("Rayon minimum (m) pour la recherche du faîtage au-dessus du milieu de façade.")]
    [SerializeField] float apexSearchRadiusMinMeters = 1.15f;

    const float MinGableTriangleHeightMeters = 0.05f;

    [SerializeField] Material gableWallMaterial;
    [SerializeField] float surfaceOffsetMeters = 0.01f;

    [Tooltip("Saillie ajoutée après mi-épaisseur : face extérieure à (mur.thickness/2 + max(surfaceOffset, cette valeur)), comme la pierre du WallObject.")]
    [SerializeField] float minExteriorProtrusionMeters = 0.042f;

    [Tooltip("Désactive le culling dos si le shader l’expose (_Cull). Sécurité avec les faces dupliquées.")]
    [SerializeField] bool forceDisableBackfaceCullingOnMaterial = true;

    [Tooltip("Correctifs UV pour la face intérieure du prisme (vue depuis la pièce).")]
    [SerializeField] bool interiorUvFlipU = true;

    [SerializeField] bool interiorUvFlipV = true;

    [Tooltip("Extrusion du pignon sur la même épaisseur que WallObject.thickness (face intérieure + flancs).")]
    [SerializeField] bool matchWallThickness = true;

    [Tooltip("Si matchWallThickness est faux : épaisseur fixe du prisme pignon (m).")]
    [SerializeField] float gableThicknessFallbackMeters = 0.25f;

    [Tooltip("Toit latéral : déplacement XZ du faîtage vs centroïde requis pour autoriser un pignon (hip symétrique = masqué).")]
    [SerializeField] float minLateralRoofShiftMetersForGable = 0.065f;

    [Tooltip("Alignement mini (produit scalaire XZ) entre la normale de façade et le vecteur centroïde→apex latéral ; limite aux côtés où l’extension a été tirée.")]
    [SerializeField] float facadeExtensionAlignmentDotMin = 0.4f;

    [SerializeField] bool autoRebuild = true;
    [SerializeField] bool logDebug = false;

    [Tooltip("Si vrai : une ligne de diagnostic Console à chaque rebuild (indépendant de logDebug). Désactiver pour réduire le bruit.")]
    [SerializeField] bool emitRebuildDiagnostics = true;

    [Tooltip("Logs courts toujours visibles pendant les tests (cycle de vie, root, counts mesh).")]
    [SerializeField] bool alwaysEmitLifecycleLogs = true;

    WallObject _wall;
    WallEditShape _edit;
    HouseRoofSystem _roof;

    Transform _gableRoot;
    MeshFilter _gableMf;
    MeshRenderer _gableMr;
    Mesh _gableMesh;

    /// <summary>Ne doit être mis à jour qu’après un rebuild réussi — sinon le premier OnEnable avant que le toit ait un mesh bloque tous les rebuilds suivants.</summary>
    int _lastSuccessfulRebuildHash = int.MinValue;

    /// <summary>Évite de répéter le bloc diagnostic identique à chaque frame.</summary>
    string _lastDiagnosticSignature = "";

    bool _loggedLifecycleHeaderThisEnable;

    static Material s_DebugMagentaFallback;

    void Awake()
    {
        CacheRefs();
        EnsureRoot();
    }

    void Start()
    {
        CacheRefs();
        EnsureRoot();
        RebuildInternal(force: true, reason: "Start");
    }

    void OnEnable()
    {
        CacheRefs();
        EnsureRoot();
        RebuildInternal(force: true, reason: "OnEnable");
    }

    void LateUpdate()
    {
        if (!autoRebuild)
            return;
        RebuildInternal(force: false, reason: "LateUpdate");
    }

    void OnDisable()
    {
        _loggedLifecycleHeaderThisEnable = false;
    }

    void OnDestroy()
    {
        if (_gableMesh != null)
        {
            if (Application.isPlaying)
                Destroy(_gableMesh);
            else
                DestroyImmediate(_gableMesh);
            _gableMesh = null;
        }
    }

    /// <summary>Forcer une régénération (éditeur ou après changement runtime).</summary>
    public void RebuildNow()
    {
        if (alwaysEmitLifecycleLogs)
            Debug.Log($"[GableWall] RebuildNow called on {GetHierarchyPath()}", this);
        CacheRefs();
        EnsureRoot();
        RebuildInternal(force: true, reason: "RebuildNow()");
    }

    /// <summary>Crée ou retrouve toujours l’enfant <see cref="GableRootChildName"/> sous ce GameObject (même mesh vide).</summary>
    public void EnsureRoot()
    {
        CacheRefs();
        EnsureGableRootStructure();
    }

    string GetHierarchyPath()
    {
        if (transform == null)
            return name;
        var parts = new List<string>();
        for (Transform t = transform; t != null; t = t.parent)
            parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    void CacheRefs()
    {
        _wall = GetComponent<WallObject>();
        _edit = GetComponent<WallEditShape>() ?? GetComponentInParent<WallEditShape>();
        _roof = GetComponent<HouseRoofSystem>() ?? GetComponentInChildren<HouseRoofSystem>(true) ?? GetComponentInParent<HouseRoofSystem>();
    }

    Material ResolveEffectiveMaterial()
    {
        if (gableWallMaterial != null)
            return gableWallMaterial;
        if (_wall != null && _wall.wallMaterial != null)
            return _wall.wallMaterial;
        MeshRenderer wallMr = _wall != null ? _wall.GetComponent<MeshRenderer>() : null;
        if (wallMr != null && wallMr.sharedMaterial != null)
            return wallMr.sharedMaterial;
        var cladding = GetComponent<WallCladdingGenerator>();
        if (cladding != null)
        {
            MeshRenderer cladMr = cladding.GetComponent<MeshRenderer>();
            if (cladMr != null && cladMr.sharedMaterial != null)
                return cladMr.sharedMaterial;
        }

        return null;
    }

    Material ResolveEffectiveMaterialWithFallback()
    {
        Material m = ResolveEffectiveMaterial();
        if (m != null)
            return m;
        return GetOrCreateDebugMagentaMaterial();
    }

    static Material GetOrCreateDebugMagentaMaterial()
    {
        if (s_DebugMagentaFallback != null)
            return s_DebugMagentaFallback;

        Shader sh =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Standard");

        Shader fallbackSh = Shader.Find("Hidden/InternalErrorShader");
        var mat = new Material(sh != null ? sh : (fallbackSh != null ? fallbackSh : Shader.Find("Sprites/Default")));
        mat.hideFlags = HideFlags.HideAndDontSave;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.magenta);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.magenta);

        s_DebugMagentaFallback = mat;
        return s_DebugMagentaFallback;
    }

    void EmitLifecycleHeaderOnce()
    {
        if (!alwaysEmitLifecycleLogs || _loggedLifecycleHeaderThisEnable)
            return;
        _loggedLifecycleHeaderThisEnable = true;
        Debug.Log($"[GableWall] HouseGableWallSystem active on {GetHierarchyPath()}", this);
    }

    void EnsureGableRootStructure()
    {
        EmitLifecycleHeaderOnce();

        Transform t = transform.Find(GableRootChildName);
        if (t == null)
        {
            var go = new GameObject(GableRootChildName);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            t = go.transform;
        }

        _gableRoot = t;
        _gableMf = _gableRoot.GetComponent<MeshFilter>();
        if (_gableMf == null)
            _gableMf = _gableRoot.gameObject.AddComponent<MeshFilter>();

        _gableMr = _gableRoot.GetComponent<MeshRenderer>();
        if (_gableMr == null)
            _gableMr = _gableRoot.gameObject.AddComponent<MeshRenderer>();

        if (_gableMesh == null)
        {
            _gableMesh = new Mesh();
            _gableMesh.name = "HouseGableWallMesh";
            _gableMesh.MarkDynamic();
        }

        _gableMf.sharedMesh = _gableMesh;
        ApplyMaterial();

        // Toujours loggé pendant l’intégration (demande produit) — repère Hierarchy + Console.
        Debug.Log($"[GableWall] root ensured = {GableRootChildName} on {GetHierarchyPath()}", this);
    }

    void ApplyMaterial()
    {
        Material m = ResolveEffectiveMaterialWithFallback();
        if (_gableMr != null)
        {
            _gableMr.sharedMaterial = m;
            _gableMr.enabled = true;

            if (forceDisableBackfaceCullingOnMaterial && m != null && Application.isPlaying)
            {
                Material inst = _gableMr.material;
                TryDisableBackfaceCulling(inst);
            }
        }
    }

    static readonly int CullShaderPropertyId = Shader.PropertyToID("_Cull");

    static void TryDisableBackfaceCulling(Material mat)
    {
        if (mat == null)
            return;
        if (mat.HasProperty(CullShaderPropertyId))
            mat.SetFloat(CullShaderPropertyId, 0f);
    }

    int ComputeRebuildHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt(surfaceOffsetMeters * 10000f);
            h = h * 31 + Mathf.RoundToInt(minExteriorProtrusionMeters * 10000f);
            h = h * 31 + Mathf.RoundToInt(roofUndersideClearanceMeters * 10000f);
            h = h * 31 + Mathf.RoundToInt(bottomEdgeDropBelowWallTopMeters * 10000f);
            h = h * 31 + Mathf.RoundToInt(gableEdgeEndpointExtendMinMeters * 10000f);
            h = h * 31 + Mathf.RoundToInt(gableEdgeExtendOverhangFraction * 10000f);
            h = h * 31 + Mathf.RoundToInt(gableEdgeExtendMaxEdgeFraction * 10000f);
            h = h * 31 + (interiorUvFlipU ? 1 : 0);
            h = h * 31 + (interiorUvFlipV ? 1 : 0);
            h = h * 31 + (forceDisableBackfaceCullingOnMaterial ? 1 : 0);
            h = h * 31 + (matchWallThickness ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(gableThicknessFallbackMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(minLateralRoofShiftMetersForGable * 10000f);
            h = h * 31 + Mathf.RoundToInt(facadeExtensionAlignmentDotMin * 10000f);
            h = h * 31 + Mathf.RoundToInt(apexSearchRadiusAlongEdgeFactor * 10000f);
            h = h * 31 + Mathf.RoundToInt(apexSearchRadiusMinMeters * 1000f);
            h = h * 31 + (gableWallMaterial != null ? gableWallMaterial.GetInstanceID() : 0);

            int roofVerts = 0;
            if (_roof != null)
            {
                Mesh rm = _roof.GetRoofSharedMesh();
                roofVerts = rm != null ? rm.vertexCount : 0;
                h = h * 31 + roofVerts;
                h = h * 31 + _roof.GetRoofConfigurationHash();
                h = h * 31 + (_roof.useDomeProfile ? 1 : 0);
                h = h * 31 + (_roof.useLateralFaceSystem ? 1 : 0);
            }

            if (_edit != null)
                h = h * 31 + Mathf.RoundToInt(_edit.shapeY * 1000f);

            if (_wall != null)
            {
                h = h * 31 + Mathf.RoundToInt(_wall.height * 1000f);
                h = h * 31 + Mathf.RoundToInt(_wall.thickness * 1000f);
                h = h * 31 + (_wall.closedLoop ? 1 : 0);
                IReadOnlyList<Vector3> pts = _wall.Points;
                int n = pts != null ? pts.Count : 0;
                h = h * 31 + n;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = pts[i];
                    h = h * 31 + Mathf.RoundToInt(p.x * 1000f);
                    h = h * 31 + Mathf.RoundToInt(p.y * 1000f);
                    h = h * 31 + Mathf.RoundToInt(p.z * 1000f);
                }
            }

            return h;
        }
    }

    void EmitLifecycleMeshSnapshot(bool roofOk, bool wallOk, int vertexCount, int triangleCount)
    {
        if (!alwaysEmitLifecycleLogs)
            return;
        Debug.Log($"[GableWall] roof found = {roofOk}", this);
        Debug.Log($"[GableWall] wall found = {wallOk}", this);
        Debug.Log($"[GableWall] mesh vertices = {vertexCount}", this);
        Debug.Log($"[GableWall] mesh triangles = {triangleCount}", this);
    }

    void RebuildInternal(bool force, string reason)
    {
        CacheRefs();

        WallObject bundledEnv = _wall != null ? HouseEnvelopeBundledSourceTag.GetEnvelopeIfBundled(_wall) : null;
        if (bundledEnv != null && bundledEnv != _wall)
        {
            ClearGableMesh();
            _lastSuccessfulRebuildHash = int.MinValue;
            if (logDebug)
                Debug.Log($"[GableWall] skip bundled source lot — canonical wall is envelope ({bundledEnv.name})", this);
            return;
        }

        EnsureGableRootStructure();
        ApplyMaterial();

        if (logDebug)
            Debug.Log($"[GableWall] rebuild requested ({reason})", this);

        int hh = ComputeRebuildHash();
        if (!force && hh == _lastSuccessfulRebuildHash)
            return;

        Material effectiveMat = ResolveEffectiveMaterialWithFallback();
        string skippedReason = null;
        bool roofFound = _roof != null && _roof.GetRoofSharedMesh() != null && _roof.GetRoofSharedMesh().vertexCount > 0;
        bool wallFound = _wall != null;
        bool closedLoopFound = _wall != null && _edit != null && _edit.IsClosedLoopPath && _wall.closedLoop;
        bool roofModeSupported = true;

        void EmitDiagnostics(int footprintCorners, int candidateFacades, int genVerts, int genTris, bool rendererOn)
        {
            if (!emitRebuildDiagnostics)
                return;

            string sig =
                $"{skippedReason}|fc={footprintCorners}|cf={candidateFacades}|v={genVerts}|t={genTris}|r={roofFound}|rv={(_roof != null && _roof.GetRoofSharedMesh() != null ? _roof.GetRoofSharedMesh().vertexCount : 0)}";
            if (sig == _lastDiagnosticSignature && !force)
                return;
            _lastDiagnosticSignature = sig;

            Debug.Log($"[GableWall] component active on {name}", this);
            Debug.Log($"[GableWall] rebuild requested ({reason}) force={force}", this);
            Debug.Log($"[GableWall] roof found = {roofFound}", this);
            Debug.Log($"[GableWall] wall found = {wallFound}", this);
            Debug.Log($"[GableWall] closed loop found = {closedLoopFound}", this);
            Debug.Log($"[GableWall] roof mode supported = {roofModeSupported}", this);
            Debug.Log($"[GableWall] footprint corners count = {footprintCorners}", this);
            Debug.Log($"[GableWall] candidate facade count = {candidateFacades}", this);
            Debug.Log($"[GableWall] generated vertex count = {genVerts}", this);
            Debug.Log($"[GableWall] generated triangle count = {genTris}", this);
            Debug.Log($"[GableWall] renderer enabled = {rendererOn}", this);
            Debug.Log($"[GableWall] material = {(effectiveMat != null ? effectiveMat.name : "null")}", this);
            Debug.Log($"[GableWall] child created = {GableRootChildName} ({(_gableRoot != null)})", this);
            Debug.Log($"[GableWall] skipped reason = {(skippedReason ?? "none")}", this);
        }

        if (_wall == null || _edit == null || !_edit.IsClosedLoopPath || !_wall.closedLoop)
        {
            skippedReason = "no closed wall loop";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(0, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No closed wall loop found", this);
            return;
        }

        if (_roof == null || _roof.GetRoofSharedMesh() == null || _roof.GetRoofSharedMesh().vertexCount == 0)
        {
            skippedReason = "no roof mesh yet or missing HouseRoofSystem";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(false, true, 0, 0);
            EmitDiagnostics(0, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No roof found", this);
            return;
        }

        List<Vector3> path = _edit.GetPreviewPathWorld();
        if (!TryPrepareClosedRing(path, out List<Vector3> prepared))
        {
            skippedReason = "invalid preview path / ring preparation failed";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(0, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No closed wall loop found", this);
            return;
        }

        if (!_roof.TryComputeFootprintBaseCornersWorld(
                out float basePlateY,
                out Vector2 centroidXZ,
                out List<Vector3> outerBaseCorners,
                out List<Vector3> wallTopCornersAtPlateY))
        {
            skippedReason = "TryComputeFootprintBaseCornersWorld failed";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(0, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No roof found", this);
            return;
        }

        int n = prepared.Count;
        if (outerBaseCorners == null || outerBaseCorners.Count != n || n < 3 ||
            wallTopCornersAtPlateY == null || wallTopCornersAtPlateY.Count != n)
        {
            skippedReason = "footprint corner count mismatch";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(n, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No roof found", this);
            return;
        }

        if (_roof.useLateralFaceSystem &&
            !_roof.useDomeProfile &&
            !RoofHasAnyMeaningfulLateralApex(_roof, centroidXZ, minLateralRoofShiftMetersForGable))
        {
            skippedReason = "lateral roof — no displaced apex (symmetric hip)";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(n, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] Skip gables: lateral hip without roof extension handles", this);
            _lastSuccessfulRebuildHash = hh;
            return;
        }

        float gableThicknessMeters =
            matchWallThickness ? Mathf.Max(0.01f, _wall.thickness) : Mathf.Max(0.01f, gableThicknessFallbackMeters);

        float halfWallThickness = Mathf.Max(0.005f, _wall.thickness * 0.5f);
        float exteriorCosmeticMeters = Mathf.Max(surfaceOffsetMeters, minExteriorProtrusionMeters);
        float exteriorFaceOffsetFromFootprintRing = halfWallThickness + exteriorCosmeticMeters;

        // Modes non latéraux / dôme : on continue avec la même approximation par plan (pan → faîtage) ;
        // ce n’est pas exact pour le dôme mais évite un trou vide ; emit diagnostics garde roofModeSupported pour l’info.
        if (_roof.useDomeProfile || !_roof.useLateralFaceSystem)
        {
            roofModeSupported = false;
            if (logDebug)
                Debug.Log("[GableWall] Non-lateral or dome roof — using approximate gable planes", this);
        }

        float wallTopY = _edit.shapeY + _wall.height;
        float meshVertexFloorY = Mathf.Min(wallTopY, basePlateY) - 0.15f;

        bool ccw = ComputeIsCCW_XZ(prepared);

        if (!TryGetRoofVerticesWorld(_roof, out List<Vector3> roofWorldVerts) || roofWorldVerts.Count == 0)
        {
            skippedReason = "roof mesh vertices unavailable";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(n, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No roof vertices for apex sampling", this);
            return;
        }

        Vector3 globalHighestRoof = FindGlobalHighestRoofVertex(roofWorldVerts);

        var verts = new List<Vector3>(n * 36);
        var uvs = new List<Vector2>(n * 36);
        var tris = new List<int>(n * 48);

        int candidateFacades = 0;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;

            Vector3 pi = prepared[i];
            Vector3 pj = prepared[j];

            Vector3 edge = pj - pi;
            edge.y = 0f;
            float edgeLen = edge.magnitude;
            if (edgeLen < 1e-10f)
                continue;

            Vector3 edgeDir = edge / edgeLen;
            Vector3 outward = Vector3.Cross(Vector3.up, edgeDir);
            if (!ccw)
                outward = -outward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-12f)
                continue;
            outward.Normalize();

            Vector2 midXz = new Vector2((pi.x + pj.x) * 0.5f, (pi.z + pj.z) * 0.5f);

            if (!ShouldEmitGableOnFacadeForRoof(
                    _roof,
                    centroidXZ,
                    outward,
                    minLateralRoofShiftMetersForGable,
                    facadeExtensionAlignmentDotMin))
                continue;

            candidateFacades++;

            Vector3 off = outward * exteriorFaceOffsetFromFootprintRing;

            float bottomY = wallTopY - Mathf.Max(0f, bottomEdgeDropBelowWallTopMeters);
            float endExtend = ComputeGableEdgeEndpointExtend(
                edgeLen,
                _roof,
                gableEdgeEndpointExtendMinMeters,
                gableEdgeExtendOverhangFraction,
                gableEdgeExtendMaxEdgeFraction);

            Vector3 bottomLeft = new Vector3(pi.x, bottomY, pi.z) + off - edgeDir * endExtend;
            Vector3 bottomRight = new Vector3(pj.x, bottomY, pj.z) + off + edgeDir * endExtend;

            float searchR = Mathf.Max(edgeLen * apexSearchRadiusAlongEdgeFactor, apexSearchRadiusMinMeters);

            Vector3 facadePlaneAnchor = new Vector3(midXz.x, bottomY, midXz.y) + off;

            Vector3 bi = wallTopCornersAtPlateY[i];
            Vector3 bj = wallTopCornersAtPlateY[j];
            Vector3 apexRoof = PickFacadeGableApexWorld(
                _roof,
                roofWorldVerts,
                midXz,
                searchR,
                meshVertexFloorY,
                outward,
                facadePlaneAnchor,
                centroidXZ,
                basePlateY,
                bi,
                bj,
                globalHighestRoof);

            if (apexRoof.y <= wallTopY + MinGableTriangleHeightMeters)
            {
                Vector3 apexRef = ResolveFacadeRoofApexReferenceWorld(_roof, basePlateY, centroidXZ, midXz);
                apexRoof = ComputeRoofPlaneHitAboveFacadeMid(midXz, bi, bj, apexRef);
                apexRoof = ProjectOntoVerticalFacadePlane(apexRoof, facadePlaneAnchor, outward);
                apexRoof.y = Mathf.Clamp(
                    apexRoof.y,
                    wallTopY + MinGableTriangleHeightMeters + 0.02f,
                    basePlateY + Mathf.Clamp(_roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters) + 2f);

                if (logDebug)
                    Debug.Log($"[GableWall] analytical apex fallback midXZ={midXz} apexY={apexRoof.y:F3}", this);
            }

            if (apexRoof.y <= wallTopY + MinGableTriangleHeightMeters)
            {
                if (logDebug)
                {
                    Debug.Log("[GableWall] skipped triangle because height too small", this);
                    Debug.Log(
                        $"[GableWall] bottomLeft={bottomLeft} bottomRight={bottomRight} apex(raw)={apexRoof} triangleHeight={apexRoof.y - bottomY:F4}",
                        this);
                }

                continue;
            }

            Vector3 apex = ProjectOntoVerticalFacadePlane(apexRoof, facadePlaneAnchor, outward);
            apex.y -= Mathf.Max(0.001f, roofUndersideClearanceMeters);

            if (apex.y <= bottomY + MinGableTriangleHeightMeters)
            {
                if (logDebug)
                    Debug.Log("[GableWall] skipped triangle after roof underside clearance vs lowered base", this);
                continue;
            }

            float triangleHeight = apex.y - bottomY;

            if (logDebug)
            {
                Debug.Log("[GableWall] building main triangle gable", this);
                Debug.Log($"[GableWall] bottomLeft={bottomLeft}", this);
                Debug.Log($"[GableWall] bottomRight={bottomRight}", this);
                Debug.Log($"[GableWall] apex={apex}", this);
                Debug.Log($"[GableWall] triangleHeight={triangleHeight:F4}", this);
            }

            AppendThickVerticalGablePrism(
                bottomLeft,
                bottomRight,
                apex,
                outward,
                gableThicknessMeters,
                verts,
                uvs,
                tris,
                interiorUvFlipU,
                interiorUvFlipV);
        }

        if (verts.Count == 0 || tris.Count == 0)
        {
            bool allowThinMinimalFallback =
                _roof == null || !_roof.useLateralFaceSystem || _roof.useDomeProfile;

            if (allowThinMinimalFallback)
                AppendMinimalTestGableTriangle(
                    prepared,
                    ccw,
                    wallTopY,
                    basePlateY + (_roof != null ? _roof.roofHeightMeters : 1f),
                    verts,
                    uvs,
                    tris,
                    ref candidateFacades,
                    gableThicknessMeters,
                    exteriorFaceOffsetFromFootprintRing,
                    interiorUvFlipU,
                    interiorUvFlipV,
                    _roof,
                    bottomEdgeDropBelowWallTopMeters,
                    roofUndersideClearanceMeters,
                    gableEdgeEndpointExtendMinMeters,
                    gableEdgeExtendOverhangFraction,
                    gableEdgeExtendMaxEdgeFraction);
        }

        if (verts.Count == 0 || tris.Count == 0)
        {
            skippedReason = "triangle height too small / degenerate geometry / unsupported roof mode";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(n, candidateFacades, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No gable geometry (degenerate or unsupported)", this);
            return;
        }

        _gableMesh.Clear();
        _gableMesh.SetVertices(verts);
        _gableMesh.SetUVs(0, uvs);
        _gableMesh.subMeshCount = 1;
        _gableMesh.SetTriangles(tris, 0);
        _gableMesh.RecalculateNormals();
        _gableMesh.RecalculateBounds();

        if (_gableMr != null)
        {
            _gableMr.enabled = true;
            _gableMr.sharedMaterial = effectiveMat;
        }

        _lastSuccessfulRebuildHash = hh;

        EmitLifecycleMeshSnapshot(roofFound, wallFound, verts.Count, tris.Count / 3);
        EmitDiagnostics(n, candidateFacades, verts.Count, tris.Count / 3, _gableMr != null && _gableMr.enabled);

        if (logDebug)
        {
            Debug.Log(
                $"[GableWall] Created gable wall mesh vertices={verts.Count} triangles={tris.Count / 3}",
                this);
        }
    }

    static float ComputeGableEdgeEndpointExtend(
        float edgeLenMeters,
        HouseRoofSystem roof,
        float minExtend,
        float overhangFrac,
        float maxEdgeFrac)
    {
        float ext = Mathf.Max(0f, minExtend);
        if (roof != null)
            ext = Mathf.Max(ext, roof.overhangMeters * Mathf.Max(0f, overhangFrac));
        float cap = Mathf.Max(0.01f, edgeLenMeters * Mathf.Clamp01(maxEdgeFrac));
        return Mathf.Min(ext, cap);
    }

    /// <summary>Un triangle visible sur la première arête valide : mur haut → faîtage (centré), pour valider le pipeline.</summary>
    static void AppendMinimalTestGableTriangle(
        List<Vector3> prepared,
        bool ccw,
        float wallTopY,
        float apexYTop,
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        ref int candidateFacades,
        float gableThicknessMeters,
        float exteriorFaceOffsetFromFootprintRing,
        bool interiorUvFlipU,
        bool interiorUvFlipV,
        HouseRoofSystem roof,
        float bottomEdgeDropBelowWallTopMeters,
        float roofUndersideClearanceMeters,
        float gableEdgeEndpointExtendMinMeters,
        float gableEdgeExtendOverhangFraction,
        float gableEdgeExtendMaxEdgeFraction)
    {
        if (prepared == null || prepared.Count < 2)
            return;

        int i0 = 0;
        int i1 = 1;
        Vector3 pi = prepared[i0];
        Vector3 pj = prepared[i1];
        Vector3 edge = pj - pi;
        edge.y = 0f;
        if (edge.sqrMagnitude < 1e-10f)
            return;

        candidateFacades++;

        float edgeLen = edge.magnitude;
        Vector3 edgeDir = edge / edgeLen;
        Vector3 outward = Vector3.Cross(Vector3.up, edgeDir);
        if (!ccw)
            outward = -outward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-12f)
            return;
        outward.Normalize();

        float endExtend = ComputeGableEdgeEndpointExtend(
            edgeLen,
            roof,
            gableEdgeEndpointExtendMinMeters,
            gableEdgeExtendOverhangFraction,
            gableEdgeExtendMaxEdgeFraction);
        float bottomY = wallTopY - Mathf.Max(0f, bottomEdgeDropBelowWallTopMeters);

        Vector3 off = outward * Mathf.Max(0f, exteriorFaceOffsetFromFootprintRing);
        Vector3 wi = new Vector3(pi.x, bottomY, pi.z) + off - edgeDir * endExtend;
        Vector3 wj = new Vector3(pj.x, bottomY, pj.z) + off + edgeDir * endExtend;
        Vector2 midXz = new Vector2((pi.x + pj.x) * 0.5f, (pi.z + pj.z) * 0.5f);
        Vector3 facadePlaneAnchor = new Vector3(midXz.x, bottomY, midXz.y) + off;
        float roofGap = Mathf.Max(0.05f, roofUndersideClearanceMeters);
        Vector3 midTopRaw = new Vector3(midXz.x, Mathf.Max(bottomY, apexYTop - roofGap), midXz.y) + off;
        Vector3 midTop = ProjectOntoVerticalFacadePlane(midTopRaw, facadePlaneAnchor, outward);

        AppendThickVerticalGablePrism(wi, wj, midTop, outward, gableThicknessMeters, verts, uvs, tris, interiorUvFlipU, interiorUvFlipV);
    }

    static Vector2 UvInteriorMainFace(Vector3 worldPos, bool flipU, bool flipV)
    {
        Vector2 uv = UvXZ(worldPos);
        if (flipU)
            uv.x = -uv.x;
        if (flipV)
            uv.y = -uv.y;
        return uv;
    }

    static bool RoofHasAnyMeaningfulLateralApex(HouseRoofSystem roof, Vector2 centroidXZ, float minShift)
    {
        if (roof == null)
            return false;

        float minSq = Mathf.Max(1e-8f, minShift * minShift);
        for (int li = 0; li < HouseRoofSystem.MaxLateralApexPoints; li++)
        {
            if (!roof.TryGetLateralApexWorldAtIndex(li, out Vector3 w))
                continue;
            float dx = w.x - centroidXZ.x;
            float dz = w.z - centroidXZ.y;
            if (dx * dx + dz * dz >= minSq)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Toit latéral : n’affiche un pignon que sur les façades vers lesquelles un apex latéral a été déplacé (extension).
    /// </summary>
    static bool ShouldEmitGableOnFacadeForRoof(
        HouseRoofSystem roof,
        Vector2 centroidXZ,
        Vector3 outward,
        float minShift,
        float alignDotMin)
    {
        if (roof == null || roof.useDomeProfile || !roof.useLateralFaceSystem)
            return true;

        Vector2 outXZ = new Vector2(outward.x, outward.z);
        if (outXZ.sqrMagnitude < 1e-12f)
            return false;
        outXZ.Normalize();

        float minSq = Mathf.Max(1e-8f, minShift * minShift);
        for (int li = 0; li < HouseRoofSystem.MaxLateralApexPoints; li++)
        {
            if (!roof.TryGetLateralApexWorldAtIndex(li, out Vector3 w))
                continue;

            Vector2 toApex = new Vector2(w.x - centroidXZ.x, w.z - centroidXZ.y);
            if (toApex.sqrMagnitude < minSq)
                continue;

            toApex.Normalize();
            if (Vector2.Dot(toApex, outXZ) >= alignDotMin)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Prisme triangulaire pleine épaisseur : grandes faces en **doublons de sommets** pour normales opposées
    /// (Lit URP ne cuaille pas la vue depuis l’intérieur). Cotés entre feuilles ext./int.
    /// </summary>
    static void AppendThickVerticalGablePrism(
        Vector3 bottomLeftOut,
        Vector3 bottomRightOut,
        Vector3 apexOut,
        Vector3 outwardUnit,
        float thicknessMeters,
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        bool interiorUvFlipU,
        bool interiorUvFlipV)
    {
        Vector3 o = outwardUnit;
        o.y = 0f;
        if (o.sqrMagnitude < 1e-12f)
            return;
        o.Normalize();

        float t = Mathf.Max(0.01f, thicknessMeters);
        Vector3 shift = -o * t;

        Vector3 blIn = bottomLeftOut + shift;
        Vector3 brIn = bottomRightOut + shift;
        Vector3 apIn = apexOut + shift;

        int b = verts.Count;

        verts.Add(bottomLeftOut);
        uvs.Add(UvXZ(bottomLeftOut));
        verts.Add(apexOut);
        uvs.Add(UvXZ(apexOut));
        verts.Add(bottomRightOut);
        uvs.Add(UvXZ(bottomRightOut));

        verts.Add(blIn);
        uvs.Add(UvInteriorMainFace(blIn, interiorUvFlipU, interiorUvFlipV));
        verts.Add(apIn);
        uvs.Add(UvInteriorMainFace(apIn, interiorUvFlipU, interiorUvFlipV));
        verts.Add(brIn);
        uvs.Add(UvInteriorMainFace(brIn, interiorUvFlipU, interiorUvFlipV));

        verts.Add(bottomLeftOut);
        uvs.Add(UvXZ(bottomLeftOut));
        verts.Add(apexOut);
        uvs.Add(UvXZ(apexOut));
        verts.Add(bottomRightOut);
        uvs.Add(UvXZ(bottomRightOut));

        verts.Add(blIn);
        uvs.Add(UvInteriorMainFace(blIn, interiorUvFlipU, interiorUvFlipV));
        verts.Add(apIn);
        uvs.Add(UvInteriorMainFace(apIn, interiorUvFlipU, interiorUvFlipV));
        verts.Add(brIn);
        uvs.Add(UvInteriorMainFace(brIn, interiorUvFlipU, interiorUvFlipV));

        void Tri(int ia, int ib, int ic)
        {
            tris.Add(b + ia);
            tris.Add(b + ib);
            tris.Add(b + ic);
        }

        Vector3 nExt = Vector3.Cross(apexOut - bottomLeftOut, bottomRightOut - bottomLeftOut);
        bool ext012 = Vector3.Dot(nExt, o) >= 0f;
        if (ext012)
        {
            Tri(0, 1, 2);
            Tri(6, 8, 7);
        }
        else
        {
            Tri(0, 2, 1);
            Tri(6, 7, 8);
        }

        Vector3 nIn = Vector3.Cross(apIn - blIn, brIn - blIn);
        bool inRoomFacing = Vector3.Dot(nIn, -o) >= 0f;
        if (inRoomFacing)
        {
            Tri(3, 5, 4);
            Tri(9, 10, 11);
        }
        else
        {
            Tri(3, 4, 5);
            Tri(9, 11, 10);
        }

        Tri(0, 1, 4);
        Tri(0, 4, 3);

        Tri(2, 5, 4);
        Tri(2, 4, 1);

        Tri(1, 2, 5);
        Tri(1, 5, 4);
    }

    void ClearGableMesh()
    {
        if (_gableMesh != null)
            _gableMesh.Clear();
        if (_gableMr != null)
        {
            _gableMr.sharedMaterial = ResolveEffectiveMaterialWithFallback();
            _gableMr.enabled = true;
        }
    }

    static bool TryGetRoofVerticesWorld(HouseRoofSystem roof, out List<Vector3> worldVerts)
    {
        worldVerts = null;
        if (roof == null)
            return false;
        Mesh mesh = roof.GetRoofSharedMesh();
        if (mesh == null || mesh.vertexCount == 0)
            return false;

        MeshFilter mf = roof.GetRoofMeshFilter();
        Transform t = mf != null ? mf.transform : roof.transform;
        Vector3[] loc = mesh.vertices;
        worldVerts = new List<Vector3>(loc.Length);
        for (int i = 0; i < loc.Length; i++)
            worldVerts.Add(t.TransformPoint(loc[i]));
        return true;
    }

    static Vector3 FindGlobalHighestRoofVertex(List<Vector3> roofWorldVerts)
    {
        Vector3 best = roofWorldVerts[0];
        float bestY = best.y;
        foreach (Vector3 v in roofWorldVerts)
        {
            if (v.y > bestY)
            {
                bestY = v.y;
                best = v;
            }
        }

        return best;
    }

    /// <summary>
    /// Ramène un point monde sur le plan vertical de la façade passant par <paramref name="planeAnchorOnExterior"/>
    /// (normale horizontale outward). Le pignon doit être dans ce plan — sans ça le triangle « suit » la pente du mesh toit.
    /// </summary>
    static Vector3 ProjectOntoVerticalFacadePlane(
        Vector3 worldPoint,
        Vector3 planeAnchorOnExterior,
        Vector3 outwardUnitHorizontal)
    {
        Vector3 n = outwardUnitHorizontal;
        n.y = 0f;
        if (n.sqrMagnitude < 1e-12f)
            return worldPoint;
        n.Normalize();
        float d = Vector3.Dot(worldPoint - planeAnchorOnExterior, n);
        return worldPoint - d * n;
    }

    /// <summary>
    /// Référence pour le plan de pan : parmi les faîtages latéraux (points jaunes / extensions), prend le plus proche XZ du milieu de façade.
    /// </summary>
    static Vector3 ResolveFacadeRoofApexReferenceWorld(
        HouseRoofSystem roof,
        float basePlateY,
        Vector2 centroidXZ,
        Vector2 facadeMidXz)
    {
        if (roof == null)
        {
            float h = HouseRoofSystem.MinRoofHeightMeters;
            return new Vector3(centroidXZ.x, basePlateY + h, centroidXZ.y);
        }

        Vector2 fm = facadeMidXz;
        Vector3 best = default;
        float bestDsq = float.PositiveInfinity;
        bool any = false;

        for (int li = 0; li < HouseRoofSystem.MaxLateralApexPoints; li++)
        {
            if (!roof.TryGetLateralApexWorldAtIndex(li, out Vector3 w))
                continue;
            float dx = w.x - fm.x;
            float dz = w.z - fm.y;
            float dsq = dx * dx + dz * dz;
            if (dsq < bestDsq)
            {
                bestDsq = dsq;
                best = w;
                any = true;
            }
        }

        if (any)
            return best;

        if (roof.TryGetLateralApexWorld(out Vector3 primary))
            return primary;

        float hh = Mathf.Clamp(roof.roofHeightMeters, HouseRoofSystem.MinRoofHeightMeters, HouseRoofSystem.MaxRoofHeightMeters);
        return new Vector3(centroidXZ.x, basePlateY + hh, centroidXZ.y);
    }

    /// <summary>
    /// Choix du sommet du pignon : mesh local, plan analytique (pan → apex ref), et tous les apex latéraux du toit ; puis projection verticale façade.
    /// </summary>
    static Vector3 PickFacadeGableApexWorld(
        HouseRoofSystem roof,
        List<Vector3> roofWorldVerts,
        Vector2 facadeMidXz,
        float searchRadiusMeters,
        float minVertexWorldY,
        Vector3 outwardUnitHorizontal,
        Vector3 facadePlaneAnchor,
        Vector2 footprintCentroidXZ,
        float basePlateWorldY,
        Vector3 roofPlateCornerI,
        Vector3 roofPlateCornerJ,
        Vector3 globalHighestFallback)
    {
        Vector3 apexRef = ResolveFacadeRoofApexReferenceWorld(roof, basePlateWorldY, footprintCentroidXZ, facadeMidXz);
        Vector3 analytical = ComputeRoofPlaneHitAboveFacadeMid(
            facadeMidXz,
            roofPlateCornerI,
            roofPlateCornerJ,
            apexRef);

        Vector3 meshCand = SelectFacadeRoofApex(
            roofWorldVerts,
            facadeMidXz,
            searchRadiusMeters,
            minVertexWorldY,
            globalHighestFallback);

        Vector3 best = default;
        float bestProjY = float.NegativeInfinity;

        void Consider(Vector3 rawWorld)
        {
            if (rawWorld.y <= minVertexWorldY + MinGableTriangleHeightMeters)
                return;
            Vector3 p = ProjectOntoVerticalFacadePlane(rawWorld, facadePlaneAnchor, outwardUnitHorizontal);
            if (p.y > bestProjY)
            {
                bestProjY = p.y;
                best = p;
            }
        }

        Consider(meshCand);
        Consider(analytical);

        if (roof != null)
        {
            for (int li = 0; li < HouseRoofSystem.MaxLateralApexPoints; li++)
            {
                if (roof.TryGetLateralApexWorldAtIndex(li, out Vector3 lat))
                    Consider(lat);
            }
        }

        if (bestProjY <= minVertexWorldY + MinGableTriangleHeightMeters)
            return new Vector3(facadeMidXz.x, minVertexWorldY - 1f, facadeMidXz.y);

        return best;
    }

    /// <summary>
    /// Plan défini par l’arête du plateau toit (coins inset au niveau <paramref name="roofPlateCornerI"/>–<paramref name="roofPlateCornerJ"/>)
    /// et le faîtage de référence : intersection avec la verticale au milieu de façade (XZ).
    /// </summary>
    static Vector3 ComputeRoofPlaneHitAboveFacadeMid(
        Vector2 midXz,
        Vector3 roofPlateCornerI,
        Vector3 roofPlateCornerJ,
        Vector3 apexReferenceWorld)
    {
        Vector3 b0 = roofPlateCornerI;
        Vector3 b1 = roofPlateCornerJ;
        Vector3 n = Vector3.Cross(b1 - b0, apexReferenceWorld - b0);
        float ny = n.y;
        if (Mathf.Abs(ny) < 1e-8f)
            return new Vector3(midXz.x, apexReferenceWorld.y, midXz.y);

        float d = Vector3.Dot(n, b0);
        float y = (d - n.x * midXz.x - n.z * midXz.y) / ny;
        return new Vector3(midXz.x, y, midXz.y);
    }

    /// <summary>
    /// Plus haut sommet du mesh toit près du milieu de façade ; sinon un candidat global raisonnable ; sinon point sentinelle bas pour déclencher le plan analytique.
    /// </summary>
    static Vector3 SelectFacadeRoofApex(
        List<Vector3> roofWorldVerts,
        Vector2 facadeMidXz,
        float searchRadiusMeters,
        float minVertexWorldY,
        Vector3 globalFallback)
    {
        Vector3 localBest = Vector3.zero;
        float localBestY = float.NegativeInfinity;

        foreach (Vector3 v in roofWorldVerts)
        {
            if (v.y <= minVertexWorldY)
                continue;
            float dxz = Vector2.Distance(new Vector2(v.x, v.z), facadeMidXz);
            if (dxz > searchRadiusMeters)
                continue;
            if (v.y > localBestY)
            {
                localBestY = v.y;
                localBest = v;
            }
        }

        if (localBestY > minVertexWorldY + MinGableTriangleHeightMeters)
            return localBest;

        Vector3 g = globalFallback;
        if (g.y > minVertexWorldY + MinGableTriangleHeightMeters)
        {
            float dxzG = Vector2.Distance(new Vector2(g.x, g.z), facadeMidXz);
            if (dxzG <= Mathf.Max(searchRadiusMeters * 3f, 2.5f))
                return g;
        }

        return new Vector3(facadeMidXz.x, minVertexWorldY - 1f, facadeMidXz.y);
    }

    static bool TryPrepareClosedRing(List<Vector3> path, out List<Vector3> ring)
    {
        ring = null;
        if (path == null || path.Count < 3)
            return false;

        ring = new List<Vector3>(path);
        if (ring.Count >= 2 && Vector3.Distance(ring[0], ring[ring.Count - 1]) < 0.001f)
            ring.RemoveAt(ring.Count - 1);

        return ring.Count >= 3;
    }

    static bool ComputeIsCCW_XZ(IReadOnlyList<Vector3> ring)
    {
        int n = ring != null ? ring.Count : 0;
        if (n < 3)
            return true;

        float twiceArea = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = ring[i];
            Vector3 b = ring[(i + 1) % n];
            twiceArea += a.x * b.z - b.x * a.z;
        }

        return twiceArea > 0f;
    }

    static Vector2 UvXZ(Vector3 v) => new Vector2(v.x * 0.2f, v.z * 0.2f);

    static GameObject s_BootstrapHost;
    static float s_LastBootstrapCountLogTime = -1000f;
    static bool s_SceneLoadedHooked;

    internal static void NotifyBootstrapHostDestroyed(GameObject host)
    {
        if (s_BootstrapHost == host)
            s_BootstrapHost = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RuntimeBootstrapHouseGableWall()
    {
        if (!s_SceneLoadedHooked)
        {
            s_SceneLoadedHooked = true;
            SceneManager.sceneLoaded += (_, __) => RunBootstrapScan();
        }

        if (s_BootstrapHost != null)
            return;

        s_BootstrapHost = new GameObject("[GableWallBootstrap]");
        s_BootstrapHost.hideFlags = HideFlags.None;
        UnityEngine.Object.DontDestroyOnLoad(s_BootstrapHost);
        s_BootstrapHost.AddComponent<GableWallBootstrapDriver>();

        RunBootstrapScan();
    }

    /// <summary>
    /// Parcourt les murs avec toit et ajoute <see cref="HouseGableWallSystem"/> sur le même GameObject que <see cref="WallObject"/>.
    /// </summary>
    public static void RunBootstrapScan()
    {
        try
        {
            List<WallObject> walls = CollectWallObjectsInLoadedScenes();
            if (Time.realtimeSinceStartup - s_LastBootstrapCountLogTime >= 0.5f)
            {
                s_LastBootstrapCountLogTime = Time.realtimeSinceStartup;
                Debug.Log($"[GableWallBootstrap] scanning WallObjects count={walls.Count}", s_BootstrapHost);
            }

            foreach (WallObject wall in walls)
            {
                if (wall == null)
                    continue;

                HouseRoofSystem roof = wall.GetComponentInChildren<HouseRoofSystem>(true);
                bool roofFound = roof != null;
                string wallPath = GetTransformPathStatic(wall.transform);

                if (!roofFound)
                    continue;

                if (wall.GetComponent<HouseGableWallSystem>() != null)
                    continue;

                Debug.Log($"[GableWallBootstrap] found wall={wallPath} roofFound=True", s_BootstrapHost);

                try
                {
                    wall.gameObject.AddComponent<HouseGableWallSystem>();
                    Debug.Log($"[GableWallBootstrap] added HouseGableWallSystem to {wallPath}", s_BootstrapHost);

                    HouseGableWallSystem gable = wall.GetComponent<HouseGableWallSystem>();
                    if (gable != null)
                    {
                        gable.EnsureRoot();
                        gable.RebuildNow();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GableWallBootstrap] AddComponent failed on {wallPath}: {ex.Message}", s_BootstrapHost);
                }
            }

            // Passe 2 : tout toit dans la scène dont le parent WallObject n’a pas encore le composant.
            List<HouseRoofSystem> roofs = CollectHouseRoofSystemsInLoadedScenes();
            foreach (HouseRoofSystem r in roofs)
            {
                if (r == null)
                    continue;
                WallObject wall = r.GetComponentInParent<WallObject>();
                if (wall == null)
                    continue;
                if (wall.GetComponent<HouseGableWallSystem>() != null)
                    continue;

                string wallPath = GetTransformPathStatic(wall.transform);
                Debug.Log($"[GableWallBootstrap] found wall={wallPath} roofFound=True (via roof scan)", s_BootstrapHost);

                try
                {
                    wall.gameObject.AddComponent<HouseGableWallSystem>();
                    Debug.Log($"[GableWallBootstrap] added HouseGableWallSystem to {wallPath}", s_BootstrapHost);

                    HouseGableWallSystem gable = wall.GetComponent<HouseGableWallSystem>();
                    if (gable != null)
                    {
                        gable.EnsureRoot();
                        gable.RebuildNow();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GableWallBootstrap] AddComponent failed on {wallPath}: {ex.Message}", s_BootstrapHost);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GableWallBootstrap] scan failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    static List<WallObject> CollectWallObjectsInLoadedScenes()
    {
        var set = new HashSet<WallObject>();

#if UNITY_2023_1_OR_NEWER
        foreach (WallObject w in UnityEngine.Object.FindObjectsByType<WallObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (w != null && IsInLoadedPlayScene(w.gameObject))
                set.Add(w);
        }
#else
        foreach (WallObject w in UnityEngine.Object.FindObjectsOfType<WallObject>(true))
        {
            if (w != null && IsInLoadedPlayScene(w.gameObject))
                set.Add(w);
        }
#endif

        if (set.Count == 0)
        {
            foreach (WallObject w in Resources.FindObjectsOfTypeAll<WallObject>())
            {
                if (w != null && IsInLoadedPlayScene(w.gameObject))
                    set.Add(w);
            }
        }

        return new List<WallObject>(set);
    }

    static List<HouseRoofSystem> CollectHouseRoofSystemsInLoadedScenes()
    {
        var list = new List<HouseRoofSystem>();
#if UNITY_2023_1_OR_NEWER
        HouseRoofSystem[] arr = UnityEngine.Object.FindObjectsByType<HouseRoofSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        HouseRoofSystem[] arr = UnityEngine.Object.FindObjectsOfType<HouseRoofSystem>(true);
#endif
        foreach (HouseRoofSystem r in arr)
        {
            if (r != null && IsInLoadedPlayScene(r.gameObject))
                list.Add(r);
        }

        return list;
    }

    static bool IsInLoadedPlayScene(GameObject go)
    {
        if (go == null)
            return false;
        Scene s = go.scene;
        return s.IsValid() && s.isLoaded;
    }

    static string GetTransformPathStatic(Transform t)
    {
        if (t == null)
            return "";
        var parts = new List<string>();
        for (Transform c = t; c != null; c = c.parent)
            parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        CacheRefs();
        EnsureRoot();
        ApplyMaterial();
        if (autoRebuild && Application.isPlaying && _wall != null)
            RebuildInternal(force: true, reason: "OnValidate");
    }

    /// <summary>Ajoute le composant sur le même GameObject que <see cref="WallObject"/> (éditeur uniquement).</summary>
    [UnityEditor.InitializeOnLoad]
    static class HouseGableWallSystemEditorAttach
    {
        static bool s_Attempted;

        static HouseGableWallSystemEditorAttach()
        {
            UnityEditor.EditorApplication.delayCall += TryAttachMissingComponentsOnce;
        }

        static void TryAttachMissingComponentsOnce()
        {
            if (s_Attempted)
                return;
            s_Attempted = true;

            foreach (HouseRoofSystem roof in UnityEngine.Object.FindObjectsByType<HouseRoofSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (roof == null)
                    continue;
                WallObject wall = roof.GetComponentInParent<WallObject>();
                if (wall == null)
                    continue;
                if (wall.GetComponent<HouseGableWallSystem>() != null)
                    continue;

                var g = wall.gameObject.AddComponent<HouseGableWallSystem>();
                g.EnsureRoot();
                g.RebuildNow();
            }
        }
    }
#endif
}

/// <summary>Visible dans la Hierarchy (DontDestroyOnLoad) : scan ~10 s / 600 frames pour murs spawnés après le load.</summary>
public sealed class GableWallBootstrapDriver : MonoBehaviour
{
    int _framesLeft = 600;
    float _destroyAfterRealtime;

    void Awake()
    {
        _destroyAfterRealtime = Time.realtimeSinceStartup + 10f;
    }

    void Update()
    {
        HouseGableWallSystem.RunBootstrapScan();
        _framesLeft--;
        if (_framesLeft <= 0 && Time.realtimeSinceStartup >= _destroyAfterRealtime)
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        HouseGableWallSystem.NotifyBootstrapHostDestroyed(gameObject);
    }
}
