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

    const float RoofSurfaceEpsilonMeters = 0.002f;

    [Tooltip("Rayon XZ (m) autour du milieu de façade pour chercher le sommet du toit ; proportionnel à la longueur d’arête si > 1.")]
    [SerializeField] float apexSearchRadiusAlongEdgeFactor = 0.65f;

    [Tooltip("Rayon minimum (m) pour la recherche du faîtage au-dessus du milieu de façade.")]
    [SerializeField] float apexSearchRadiusMinMeters = 1.15f;

    const float MinGableTriangleHeightMeters = 0.05f;

    [SerializeField] Material gableWallMaterial;
    [SerializeField] float surfaceOffsetMeters = 0.01f;
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
        }
    }

    int ComputeRebuildHash()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt(surfaceOffsetMeters * 10000f);
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
                out _))
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
        if (outerBaseCorners == null || outerBaseCorners.Count != n || n < 3)
        {
            skippedReason = "footprint corner count mismatch";
            ClearGableMesh();
            EmitLifecycleMeshSnapshot(roofFound, wallFound, 0, 0);
            EmitDiagnostics(n, 0, 0, 0, false);
            if (logDebug)
                Debug.Log("[GableWall] No roof found", this);
            return;
        }

        // Modes non latéraux / dôme : on continue avec la même approximation par plan (pan → faîtage) ;
        // ce n’est pas exact pour le dôme mais évite un trou vide ; emit diagnostics garde roofModeSupported pour l’info.
        if (_roof.useDomeProfile || !_roof.useLateralFaceSystem)
        {
            roofModeSupported = false;
            if (logDebug)
                Debug.Log("[GableWall] Non-lateral or dome roof — using approximate gable planes", this);
        }

        float wallTopY = _edit.shapeY + _wall.height;

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

        var verts = new List<Vector3>(n * 3);
        var uvs = new List<Vector2>(n * 3);
        var tris = new List<int>(n * 3);

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

            candidateFacades++;

            Vector3 edgeDir = edge / edgeLen;
            Vector3 outward = Vector3.Cross(Vector3.up, edgeDir);
            if (!ccw)
                outward = -outward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-12f)
                continue;
            outward.Normalize();

            Vector3 off = outward * Mathf.Max(0f, surfaceOffsetMeters);

            Vector3 bottomLeft = new Vector3(pi.x, wallTopY, pi.z) + off;
            Vector3 bottomRight = new Vector3(pj.x, wallTopY, pj.z) + off;

            Vector2 midXz = new Vector2((pi.x + pj.x) * 0.5f, (pi.z + pj.z) * 0.5f);
            float searchR = Mathf.Max(edgeLen * apexSearchRadiusAlongEdgeFactor, apexSearchRadiusMinMeters);

            Vector3 apexRoof = SelectFacadeRoofApex(roofWorldVerts, midXz, searchR, wallTopY, globalHighestRoof);

            if (apexRoof.y <= wallTopY + MinGableTriangleHeightMeters)
            {
                if (logDebug)
                {
                    Debug.Log("[GableWall] skipped triangle because height too small", this);
                    Debug.Log(
                        $"[GableWall] bottomLeft={bottomLeft} bottomRight={bottomRight} apex(raw)={apexRoof} triangleHeight={apexRoof.y - wallTopY:F4}",
                        this);
                }

                continue;
            }

            Vector3 apex = apexRoof + off;
            apex.y -= RoofSurfaceEpsilonMeters;

            float triangleHeight = apex.y - wallTopY;

            if (logDebug)
            {
                Debug.Log("[GableWall] building main triangle gable", this);
                Debug.Log($"[GableWall] bottomLeft={bottomLeft}", this);
                Debug.Log($"[GableWall] bottomRight={bottomRight}", this);
                Debug.Log($"[GableWall] apex={apex}", this);
                Debug.Log($"[GableWall] triangleHeight={triangleHeight:F4}", this);
            }

            int v0 = verts.Count;
            verts.Add(bottomLeft);
            verts.Add(apex);
            verts.Add(bottomRight);
            uvs.Add(UvXZ(bottomLeft));
            uvs.Add(UvXZ(apex));
            uvs.Add(UvXZ(bottomRight));

            // Normale vers l’extérieur (même convention que le mur).
            Vector3 nTri = Vector3.Cross(apex - bottomLeft, bottomRight - bottomLeft);
            if (Vector3.Dot(nTri, outward) < 0f)
            {
                tris.Add(v0);
                tris.Add(v0 + 2);
                tris.Add(v0 + 1);
            }
            else
            {
                tris.Add(v0);
                tris.Add(v0 + 1);
                tris.Add(v0 + 2);
            }
        }

        if (verts.Count == 0 || tris.Count == 0)
        {
            AppendMinimalTestGableTriangle(
                prepared, ccw, wallTopY, basePlateY + _roof.roofHeightMeters, verts, uvs, tris, ref candidateFacades);
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

    /// <summary>Un triangle visible sur la première arête valide : mur haut → faîtage (centré), pour valider le pipeline.</summary>
    static void AppendMinimalTestGableTriangle(
        List<Vector3> prepared,
        bool ccw,
        float wallTopY,
        float apexYTop,
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        ref int candidateFacades)
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

        Vector3 edgeDir = edge.normalized;
        Vector3 outward = Vector3.Cross(Vector3.up, edgeDir);
        if (!ccw)
            outward = -outward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-12f)
            return;
        outward.Normalize();

        Vector3 off = outward * 0.01f;
        Vector3 wi = new Vector3(pi.x, wallTopY, pi.z) + off;
        Vector3 wj = new Vector3(pj.x, wallTopY, pj.z) + off;
        Vector3 midTop = new Vector3((pi.x + pj.x) * 0.5f, Mathf.Max(wallTopY, apexYTop - 0.05f), (pi.z + pj.z) * 0.5f) + off;

        int v0 = verts.Count;
        verts.Add(wi);
        verts.Add(wj);
        verts.Add(midTop);
        uvs.Add(UvXZ(wi));
        uvs.Add(UvXZ(wj));
        uvs.Add(UvXZ(midTop));

        tris.Add(v0);
        tris.Add(v0 + 1);
        tris.Add(v0 + 2);
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
    /// Plus haut sommet du mesh toit au-dessus du milieu de façade (XZ) ; sinon <paramref name="globalFallback"/>.
    /// </summary>
    static Vector3 SelectFacadeRoofApex(
        List<Vector3> roofWorldVerts,
        Vector2 facadeMidXz,
        float searchRadiusMeters,
        float wallTopY,
        Vector3 globalFallback)
    {
        Vector3 localBest = Vector3.zero;
        float localBestY = float.NegativeInfinity;

        foreach (Vector3 v in roofWorldVerts)
        {
            if (v.y <= wallTopY + 0.02f)
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

        if (localBestY > wallTopY + MinGableTriangleHeightMeters)
            return localBest;

        return globalFallback;
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
