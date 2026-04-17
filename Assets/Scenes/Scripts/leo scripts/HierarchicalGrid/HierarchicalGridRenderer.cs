using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class HierarchicalGridRenderer : MonoBehaviour
{
    struct LevelRenderData
    {
        public GameObject go;
        public Mesh mesh;
        public MeshFilter filter;
        public MeshRenderer renderer;
    }

    readonly Dictionary<int, LevelRenderData> _perLevel = new Dictionary<int, LevelRenderData>();
    readonly Dictionary<int, QuadMeshBuilder> _builders = new Dictionary<int, QuadMeshBuilder>();
    readonly List<int> _usedLevels = new List<int>();
    Material _runtimeMaterial;

    public void Render(
        IReadOnlyList<HierarchicalGridNode> nodes,
        HierarchicalGridNode hoverCell,
        HierarchicalGridSettings settings)
    {
        if (settings == null || nodes == null)
        {
            DisableAll();
            return;
        }

        _usedLevels.Clear();
        _builders.Clear();

        int maxDepth = Mathf.Max(1, settings.maxDepth);

        if (settings.uniformSubdivision && nodes.Count > 0)
        {
            RenderUniformHierarchy(nodes[0], maxDepth, settings);

            if (settings.highlightCellUnderMouse && hoverCell != null)
            {
                int level = Mathf.Clamp(hoverCell.depth, 0, maxDepth);
                QuadMeshBuilder hb = GetBuilder(level);
                Color hc = settings.highlightColor;
                hc.a *= settings.globalOpacity;
                float hw = settings.baseLineThickness * 1.3f;
                AddCellOutlineXZ(hb, hoverCell, settings.gridPlaneY + settings.surfaceYOffset + 0.002f, hw, hc);
            }

            FlushBuilders(settings);
            return;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            HierarchicalGridNode cell = nodes[i];
            if (cell == null)
                continue;

            int level = cell.depth;
            QuadMeshBuilder b = GetBuilder(level);
            float t = Mathf.Clamp01(level / (float)maxDepth);
            Color color = settings.levelColorGradient.Evaluate(t);
            color.a *= settings.globalOpacity;

            float lineWidth = Mathf.Lerp(
                settings.baseLineThickness,
                settings.baseLineThickness * settings.deepLevelThicknessFactor,
                t);
            lineWidth = Mathf.Max(0.0005f, lineWidth);

            AddCellOutlineXZ(b, cell, settings.gridPlaneY + settings.surfaceYOffset, lineWidth, color);

            if (settings.showInternalSubGrid && cell.IsLeaf && settings.internalSubGridResolution >= 2)
            {
                Color subColor = color;
                subColor.a *= settings.internalSubGridOpacity;
                float subWidth = lineWidth * settings.internalSubGridThicknessFactor;
                AddInternalSubGridXZ(
                    b,
                    cell,
                    settings.gridPlaneY + settings.surfaceYOffset,
                    settings.internalSubGridResolution,
                    Mathf.Max(0.0004f, subWidth),
                    subColor);
            }
        }

        if (settings.highlightCellUnderMouse && hoverCell != null)
        {
            int level = Mathf.Clamp(hoverCell.depth, 0, maxDepth);
            QuadMeshBuilder b = GetBuilder(level);
            Color hc = settings.highlightColor;
            hc.a *= settings.globalOpacity;
            float hw = settings.baseLineThickness * 1.3f;
            AddCellOutlineXZ(b, hoverCell, settings.gridPlaneY + settings.surfaceYOffset + 0.002f, hw, hc);
        }

        FlushBuilders(settings);
    }

    void FlushBuilders(HierarchicalGridSettings settings)
    {
        foreach (var kv in _builders)
        {
            int level = kv.Key;
            QuadMeshBuilder b = kv.Value;
            if (b.TriangleCount <= 0)
                continue;

            EnsureLevel(level, settings);
            _usedLevels.Add(level);

            LevelRenderData data = _perLevel[level];
            b.ApplyTo(data.mesh);
            data.mesh.RecalculateBounds();
            data.go.SetActive(true);
        }

        DisableUnusedLevels();
    }

    void RenderUniformHierarchy(HierarchicalGridNode root, int maxDepth, HierarchicalGridSettings settings)
    {
        if (root == null)
            return;

        float y = settings.gridPlaneY + settings.surfaceYOffset;
        Vector2 min = root.Min;
        Vector2 max = root.Max;
        int maxLines = Mathf.Max(16, settings.maxLinesPerAxisPerLevel);

        for (int level = 0; level <= maxDepth; level++)
        {
            QuadMeshBuilder b = GetBuilder(level);
            float t = Mathf.Clamp01(level / (float)maxDepth);
            Color color = settings.levelColorGradient.Evaluate(t);
            color.a *= settings.globalOpacity;

            float width = Mathf.Lerp(
                settings.baseLineThickness,
                settings.baseLineThickness * settings.deepLevelThicknessFactor,
                t);
            width = Mathf.Max(0.0005f, width);

            int divisions = IntPow(3, level);
            int sampleStep = Mathf.Max(1, Mathf.CeilToInt((divisions + 1) / (float)maxLines));
            float step = root.size / divisions;

            for (int i = 0; i <= divisions; i += sampleStep)
            {
                float x = min.x + i * step;
                b.AddLineQuad(new Vector3(x, y, min.y), new Vector3(x, y, max.y), width, color);
            }

            if (divisions % sampleStep != 0)
            {
                float x = max.x;
                b.AddLineQuad(new Vector3(x, y, min.y), new Vector3(x, y, max.y), width, color);
            }

            for (int i = 0; i <= divisions; i += sampleStep)
            {
                float z = min.y + i * step;
                b.AddLineQuad(new Vector3(min.x, y, z), new Vector3(max.x, y, z), width, color);
            }

            if (divisions % sampleStep != 0)
            {
                float z = max.y;
                b.AddLineQuad(new Vector3(min.x, y, z), new Vector3(max.x, y, z), width, color);
            }
        }
    }

    static int IntPow(int b, int exp)
    {
        int r = 1;
        for (int i = 0; i < exp; i++)
            r *= b;
        return r;
    }

    public void ClearAll()
    {
        foreach (var kv in _perLevel)
        {
            if (kv.Value.mesh != null)
                kv.Value.mesh.Clear();
            if (kv.Value.go != null)
                kv.Value.go.SetActive(false);
        }
    }

    QuadMeshBuilder GetBuilder(int level)
    {
        if (!_builders.TryGetValue(level, out QuadMeshBuilder b))
        {
            b = new QuadMeshBuilder(2048);
            _builders.Add(level, b);
        }
        return b;
    }

    void EnsureLevel(int level, HierarchicalGridSettings settings)
    {
        if (_perLevel.ContainsKey(level))
            return;

        GameObject go = new GameObject($"GridLevel_{level:00}");
        go.transform.SetParent(transform, false);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Mesh mesh = new Mesh { name = $"GridMesh_{level:00}" };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;

        Material mat = ResolveMaterial(settings);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = settings.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = settings.receiveShadows;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        _perLevel.Add(level, new LevelRenderData
        {
            go = go,
            mesh = mesh,
            filter = mf,
            renderer = mr
        });
    }

    Material ResolveMaterial(HierarchicalGridSettings settings)
    {
        if (settings != null && settings.lineMaterial != null)
            return settings.lineMaterial;

        if (_runtimeMaterial != null)
            return _runtimeMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        _runtimeMaterial = new Material(shader);
        _runtimeMaterial.name = "HierarchicalGrid_RuntimeLineMat";
        return _runtimeMaterial;
    }

    void DisableUnusedLevels()
    {
        foreach (var kv in _perLevel)
        {
            if (!_usedLevels.Contains(kv.Key) && kv.Value.go != null)
                kv.Value.go.SetActive(false);
        }
    }

    void DisableAll()
    {
        foreach (var kv in _perLevel)
        {
            if (kv.Value.go != null)
                kv.Value.go.SetActive(false);
        }
    }

    static void AddCellOutlineXZ(QuadMeshBuilder b, HierarchicalGridNode node, float y, float width, Color color)
    {
        Vector2 min = node.Min;
        Vector2 max = node.Max;

        Vector3 a = new Vector3(min.x, y, min.y);
        Vector3 c = new Vector3(max.x, y, min.y);
        Vector3 d = new Vector3(max.x, y, max.y);
        Vector3 e = new Vector3(min.x, y, max.y);

        b.AddLineQuad(a, c, width, color);
        b.AddLineQuad(c, d, width, color);
        b.AddLineQuad(d, e, width, color);
        b.AddLineQuad(e, a, width, color);
    }

    static void AddInternalSubGridXZ(
        QuadMeshBuilder b,
        HierarchicalGridNode node,
        float y,
        int resolution,
        float width,
        Color color)
    {
        if (resolution < 2)
            return;

        Vector2 min = node.Min;
        float size = node.size;
        float step = size / resolution;

        for (int i = 1; i < resolution; i++)
        {
            float x = min.x + step * i;
            float z = min.y + step * i;

            b.AddLineQuad(
                new Vector3(x, y, min.y),
                new Vector3(x, y, min.y + size),
                width,
                color);

            b.AddLineQuad(
                new Vector3(min.x, y, z),
                new Vector3(min.x + size, y, z),
                width,
                color);
        }
    }

    sealed class QuadMeshBuilder
    {
        readonly List<Vector3> _verts;
        readonly List<int> _tris;
        readonly List<Color> _colors;

        public int TriangleCount => _tris.Count;

        public QuadMeshBuilder(int capacity)
        {
            _verts = new List<Vector3>(capacity);
            _tris = new List<int>(capacity * 3 / 2);
            _colors = new List<Color>(capacity);
        }

        public void AddLineQuad(Vector3 a, Vector3 b, float width, Color color)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 0.00001f)
                return;

            dir /= len;
            Vector3 n = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);

            int start = _verts.Count;
            _verts.Add(a - n);
            _verts.Add(a + n);
            _verts.Add(b + n);
            _verts.Add(b - n);

            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);

            _tris.Add(start + 0);
            _tris.Add(start + 1);
            _tris.Add(start + 2);
            _tris.Add(start + 0);
            _tris.Add(start + 2);
            _tris.Add(start + 3);
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear(false);
            mesh.SetVertices(_verts);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0, true);
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
        }
    }
}

