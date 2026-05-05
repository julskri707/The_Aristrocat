using UnityEngine;

/// <summary>
/// Géométrie procédurale d’un escalier droit sous <see cref="Transform"/>/__StairGeometry.
/// Marches avec nez léger, contre-marches et limons plus lisibles qu’un empilement de cubes identiques.
/// </summary>
public static class StairFlightMeshBuilder
{
    public const string GeometryChildName = "__StairGeometry";

    public static Transform GetOrCreateGeometryRoot(Transform root)
    {
        if (root == null)
            return null;

        Transform existing = root.Find(GeometryChildName);
        if (existing != null)
            return existing;

        GameObject go = new GameObject(GeometryChildName);
        go.transform.SetParent(root, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    public static void ClearGeometry(Transform root)
    {
        Transform geom = root != null ? root.Find(GeometryChildName) : null;
        if (geom == null)
            return;

        for (int i = geom.childCount - 1; i >= 0; i--)
        {
            Transform c = geom.GetChild(i);
            if (c != null)
                Object.Destroy(c.gameObject);
        }
    }

    /// <summary>
    /// Nombre de marches dérivé de la profondeur de volée (run / pas idéal), borné.
    /// </summary>
    public static int ComputeStepCount(
        float runLengthMeters,
        float totalRiseMeters,
        float idealTreadDepthMeters,
        int minSteps,
        int maxSteps)
    {
        float run = Mathf.Max(0.15f, runLengthMeters);
        float raw = run / Mathf.Max(0.06f, idealTreadDepthMeters);
        int n = Mathf.RoundToInt(raw);
        n = Mathf.Clamp(n, Mathf.Max(4, minSteps), Mathf.Max(minSteps, maxSteps));
        float risePer = totalRiseMeters / n;
        const float slabT = 0.045f;
        float minRise = slabT + 0.02f;
        if (risePer < minRise && totalRiseMeters >= minRise * 4f)
            n = Mathf.Clamp(Mathf.FloorToInt(totalRiseMeters / minRise), 4, maxSteps);
        return Mathf.Clamp(n, 4, 48);
    }

    public static void Rebuild(
        Transform root,
        float totalRiseMeters,
        float runLengthMeters,
        float widthMeters,
        int stepCount,
        float treadThickness = 0.042f,
        float noseOverhang = 0.022f,
        float riserPlateDepth = 0.035f)
    {
        if (root == null)
            return;

        Transform geom = GetOrCreateGeometryRoot(root);
        ClearGeometry(root);

        float riseTotal = Mathf.Max(0.12f, totalRiseMeters);
        float runLen = Mathf.Max(0.25f, runLengthMeters);
        float width = Mathf.Max(0.2f, widthMeters);
        int steps = Mathf.Clamp(stepCount, 4, 48);
        float risePerStep = riseTotal / steps;
        float treadDepth = runLen / steps;
        float minRise = treadThickness + 0.02f;
        if (risePerStep < minRise && riseTotal >= minRise * 4f)
        {
            steps = Mathf.Clamp(Mathf.FloorToInt(riseTotal / minRise), 4, 48);
            risePerStep = riseTotal / steps;
            treadDepth = runLen / steps;
        }

        float nose = Mathf.Clamp(noseOverhang, 0f, treadDepth * 0.35f);
        Material treadMat = CreateLitMaterial(new Color(0.55f, 0.40f, 0.28f), 0.42f);
        Material riserMat = CreateLitMaterial(new Color(0.34f, 0.23f, 0.15f), 0.28f);
        Material stringerMat = CreateLitMaterial(new Color(0.26f, 0.175f, 0.11f), 0.22f);

        float treadZExtent = Mathf.Max(0.06f, treadDepth * 0.88f + nose);
        float sideInset = Mathf.Clamp(width * 0.03f, 0.01f, 0.06f);
        float treadW = width - sideInset * 2f;

        for (int i = 0; i < steps; i++)
        {
            float z0 = i * treadDepth;
            float topY = (i + 1) * risePerStep;
            float treadCy = topY - treadThickness * 0.5f;
            float zCenter = z0 + treadDepth * 0.5f + nose * 0.55f;

            CreatePrimitiveBox(
                geom,
                "Tread_" + i,
                new Vector3(0f, treadCy, zCenter),
                new Vector3(treadW, treadThickness, treadZExtent),
                treadMat);

            float riserCenterY = i * risePerStep + risePerStep * 0.5f;
            float riserZ = z0 + treadDepth - riserPlateDepth * 0.48f;
            if (i < steps - 1)
            {
                CreatePrimitiveBox(
                    geom,
                    "Riser_" + i,
                    new Vector3(0f, riserCenterY, riserZ),
                    new Vector3(treadW * 0.96f, risePerStep * 0.94f, riserPlateDepth),
                    riserMat);
            }
        }

        float diagLen = Mathf.Sqrt(riseTotal * riseTotal + runLen * runLen);
        float angleRad = Mathf.Atan2(riseTotal, runLen);
        float stringerThickness = Mathf.Clamp(width * 0.065f, 0.06f, 0.14f);
        float stringerInset = treadW * 0.5f + stringerThickness * 0.5f + 0.012f;

        void AddStringer(float sideSign)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = sideSign < 0 ? "StringerL" : "StringerR";
            go.transform.SetParent(geom, false);
            go.transform.localPosition = new Vector3(sideSign * stringerInset, riseTotal * 0.5f, runLen * 0.5f);
            go.transform.localRotation = Quaternion.Euler(-angleRad * Mathf.Rad2Deg, 0f, 0f);
            go.transform.localScale = new Vector3(stringerThickness, diagLen * 0.505f, stringerThickness * 1.15f);
            ApplyMaterial(go, stringerMat);
        }

        AddStringer(-1f);
        AddStringer(1f);

        CreatePrimitiveBox(
            geom,
            "BottomTrim",
            new Vector3(0f, treadThickness * 0.35f, treadDepth * 0.35f),
            new Vector3(width + stringerThickness * 2f, treadThickness * 0.75f, treadDepth * 0.7f),
            stringerMat);
    }

    static void CreatePrimitiveBox(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;
        ApplyMaterial(go, mat);
    }

    static void ApplyMaterial(GameObject go, Material mat)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;
    }

    static Material CreateLitMaterial(Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        Material mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);
        return mat;
    }
}
