using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parametric roof generated from a closed wall footprint.
/// Controls:
/// - roofHeightMeters: raise/lower roof
/// - roundness: 0 = boxy/flat-ish, 1 = strongly rounded toward center
/// - overhangMeters: roof base extension outside wall footprint
/// </summary>
[DisallowMultipleComponent]
public class HouseRoofSystem : MonoBehaviour
{
    const string RoofChildName = "__HouseRoof";
    const float TriEps = 1e-5f;
    public const float MinRoofHeightMeters = 0.25f;
    public const float MaxRoofHeightMeters = 10f;
    public const float MinOverhangMeters = 0.4f;
    public const float MaxOverhangMeters = 1f;

    /// <summary>Décalage vertical fixe de la semelle du toit au-dessus du mur.</summary>
    public const float RoofBuiltInVerticalLiftMeters = 0.15f;

    /// <summary>Raccord rentré dans le mur : distance perpendiculaire aux façades.</summary>
    public const float EaveInsetPerpendicularToWallMeters = 0.2f;

    [Header("Shape")]
    [Range(MinRoofHeightMeters, MaxRoofHeightMeters)] public float roofHeightMeters = 1.2f;
    [Range(0f, 1f)] public float roundness = 0.45f;
    [Range(MinOverhangMeters, MaxOverhangMeters)] public float overhangMeters = MinOverhangMeters;
    [Min(0.02f)] public float roofThicknessMeters = 0.16f;
    [Min(0f)] public float yOffsetAboveWallTop = 0f;

    [Header("Runtime")]
    public bool autoRebuild = true;

    MeshFilter _mf;
    MeshRenderer _mr;
    Mesh _mesh;
    Material _connectorMaterial;
    Material _roofFallbackSkinMaterial;
    int _lastHash;

    public static HouseRoofSystem EnsureOnWall(WallObject wall)
    {
        if (wall == null)
            return null;
        HouseRoofSystem roof = wall.GetComponent<HouseRoofSystem>();
        if (roof == null)
            roof = wall.gameObject.AddComponent<HouseRoofSystem>();
        roof.EnsureComponents();
        roof.RebuildNow();
        return roof;
    }

    void Awake() => EnsureComponents();
    void OnEnable() => EnsureComponents();

    void LateUpdate()
    {
        if (!autoRebuild)
            return;
        int h = ComputeHash();
        if (h == _lastHash)
            return;
        RebuildNow();
    }

    public void RebuildNow()
    {
        EnsureComponents();
        if (_mf == null || _mr == null)
            return;

        WallObject wall = GetComponent<WallObject>();
        WallEditShape edit = GetComponent<WallEditShape>();
        if (wall == null || edit == null || !edit.IsClosedLoopPath)
        {
            ClearMesh();
            return;
        }

        List<Vector3> ring = edit.GetPreviewPathWorld();
        if (!TryPrepareClosedRing(ring, out List<Vector3> prepared))
        {
            ClearMesh();
            return;
        }

        float baseY = edit.shapeY + wall.height + yOffsetAboveWallTop + RoofBuiltInVerticalLiftMeters;
        roofHeightMeters = Mathf.Clamp(roofHeightMeters, MinRoofHeightMeters, MaxRoofHeightMeters);
        overhangMeters = Mathf.Clamp(overhangMeters, MinOverhangMeters, MaxOverhangMeters);

        float h = roofHeightMeters;
        var wallCorners = new List<Vector3>(prepared.Count);
        var baseCorners = new List<Vector3>(prepared.Count);

        Vector2 centroid = ComputeCentroidXZ(prepared);

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
            Vector2 dir = new Vector2(p.x - centroid.x, p.z - centroid.y);
            if (dir.sqrMagnitude > 1e-8f)
                dir.Normalize();
            baseCorners.Add(new Vector3(p.x + dir.x * overhangMeters, baseY, p.z + dir.y * overhangMeters));
        }

        const int edgeSubdivisions = 8;
        List<Vector3> baseRing = BuildSubdividedClosedRing(baseCorners, edgeSubdivisions);
        List<Vector3> wallRing = BuildSubdividedClosedRing(wallCorners, edgeSubdivisions);
        int n = baseRing.Count;

        const int radialBands = 18; // dense enough near the summit to avoid a visible cone spike.
        int ringCount = radialBands;
        int centerIndex = ringCount * n;

        var verts = new List<Vector3>(centerIndex + 1);
        var uvs = new List<Vector2>(centerIndex + 1);
        var roofTris = new List<int>(n * (ringCount * 12 + 6));
        var connectorTris = new List<int>(n * 24);

        for (int r = 0; r < ringCount; r++)
        {
            float t = r / (float)radialBands;
            float alpha = 1f - Mathf.Pow(1f - t, 1.65f); // pack more rings near the top cap.
            float profileY = EvaluateDomeProfile(alpha, Mathf.Clamp01(roundness));
            float y = baseY + h * profileY;
            for (int i = 0; i < n; i++)
            {
                Vector3 b = baseRing[i];
                float x = Mathf.Lerp(b.x, centroid.x, alpha);
                float z = Mathf.Lerp(b.z, centroid.y, alpha);
                verts.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x * 0.2f, z * 0.2f));
            }
        }

        // Apex (always present, avoids disappearing summit).
        Vector3 apex = new Vector3(centroid.x, baseY + h, centroid.y);
        verts.Add(apex);
        uvs.Add(new Vector2(apex.x * 0.2f, apex.z * 0.2f));

        // Connect rings.
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

        // Last ring to apex fan.
        int lastRow = (ringCount - 1) * n;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            roofTris.Add(lastRow + i); roofTris.Add(centerIndex); roofTris.Add(lastRow + j);
        }

        AddThickInteriorAndEaveConnector(verts, uvs, roofTris, connectorTris, baseRing, wallRing, Mathf.Max(0.02f, roofThicknessMeters));

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

    static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    static Vector2 UvXZ(Vector3 v) => new Vector2(v.x * 0.2f, v.z * 0.2f);

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
        roundness = Mathf.Clamp01(roundness + delta);
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
        if (path == null || path.Count < 4)
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
    /// Dome profile from edge (0) to center (1).
    /// roundness in [0..1]:
    /// - < 0.5 : inverted dome family
    /// - = 0.5 : near-linear cone-like profile
    /// - > 0.5 : normal dome family
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
            h = h * 31 + Mathf.RoundToInt(roofThicknessMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(roundness * 1000f);
            h = h * 31 + Mathf.RoundToInt(yOffsetAboveWallTop * 1000f);
            h = h * 31 + Mathf.RoundToInt(RoofBuiltInVerticalLiftMeters * 1000f);
            h = h * 31 + Mathf.RoundToInt(EaveInsetPerpendicularToWallMeters * 1000f);
            List<Vector3> ring = edit != null ? edit.GetPreviewPathWorld() : null;
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
