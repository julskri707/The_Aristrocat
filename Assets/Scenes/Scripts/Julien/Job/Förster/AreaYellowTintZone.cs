using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class AreaYellowTintZone : MonoBehaviour
{
    [Header("WorkArea Sync")]
    [SerializeField] private WoodcutterWorkArea linkedWorkArea;
    [SerializeField] private bool syncToTreeSearchRadius = true;

    [Header("Manual Area Fallback")]
    [SerializeField] private Collider areaCollider;

    [Header("Filtering")]
    [SerializeField] private LayerMask affectedLayers = ~0;
    [SerializeField] private bool ignoreTriggers = true;
    [SerializeField] private bool ignoreOwnHierarchy = true;

    [Header("Tint")]
    [SerializeField] private bool showOnEnable = true;
    [SerializeField] private Color tintColor = new Color(1f, 0.88f, 0.2f, 0.35f);
    [Min(0.02f)]
    [SerializeField] private float refreshInterval = 0.15f;

    [Header("Overlay Camera")]
    [SerializeField] private bool autoCreateOverlayCamera = true;
    [SerializeField] private string overlayCameraName = "YellowAreaOverlayCamera_Auto";
    [SerializeField] private int depthOffset = 1;
    [Range(8, 31)]
    [SerializeField] private int previewLayer = 31;

    [Header("Debug")]
    [SerializeField] private bool tintActive;
    [SerializeField] private Camera overlayCamera;
    [SerializeField] private int rendererCount;
    [SerializeField] private float debugRadius;
    [SerializeField] private Vector3 debugCenter;

    private readonly HashSet<Renderer> cachedRenderers = new HashSet<Renderer>();
    private readonly Collider[] overlapResults = new Collider[512];
    private readonly List<MeshDrawEntry> drawEntries = new List<MeshDrawEntry>(256);

    private Material overlayMaterial;
    private float refreshTimer;
    private Camera cachedMainCamera;
    private bool createdOverlayCameraAtRuntime;

    private struct MeshDrawEntry
    {
        public Mesh mesh;
        public Matrix4x4 matrix;
        public int subMeshIndex;
    }

    private void Awake()
    {
        AutoAssign();
        EnsureOverlayCamera();
    }

    private void OnEnable()
    {
        AutoAssign();
        EnsureOverlayCamera();

        if (showOnEnable)
            ShowTint();
        else
            UpdateOverlayCameraEnabled();
    }

    private void LateUpdate()
    {
        AutoAssign();
        SyncOverlayCameraToMain();

        if (!tintActive)
        {
            UpdateOverlayCameraEnabled();
            return;
        }

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            RefreshTint();
        }

        SubmitOverlayMeshes();
    }

    private void OnDisable()
    {
        HideTint();
    }

    private void OnDestroy()
    {
        HideTint();

        if (overlayMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(overlayMaterial);
            else
                DestroyImmediate(overlayMaterial);
        }

        if (createdOverlayCameraAtRuntime && overlayCamera != null)
        {
            if (Application.isPlaying)
                Destroy(overlayCamera.gameObject);
            else
                DestroyImmediate(overlayCamera.gameObject);
        }
    }

    private void OnValidate()
    {
        refreshInterval = Mathf.Max(0.02f, refreshInterval);
        depthOffset = Mathf.Max(0, depthOffset);
        previewLayer = Mathf.Clamp(previewLayer, 0, 31);

        AutoAssign();

        if (!Application.isPlaying && autoCreateOverlayCamera)
            EnsureOverlayCamera();

        SyncOverlayCameraToMain();
        UpdateOverlayCameraEnabled();
    }

    public void ShowTint()
    {
        tintActive = true;
        refreshTimer = 0f;
        EnsureOverlayCamera();
        RefreshTint();
        UpdateOverlayCameraEnabled();
    }

    public void HideTint()
    {
        tintActive = false;
        cachedRenderers.Clear();
        drawEntries.Clear();
        rendererCount = 0;
        UpdateOverlayCameraEnabled();
    }

    public void SetTintActive(bool active)
    {
        if (active)
            ShowTint();
        else
            HideTint();
    }

    public void RefreshTint()
    {
        cachedRenderers.Clear();
        drawEntries.Clear();

        if (!TryGetArea(out Vector3 center, out float radius))
        {
            rendererCount = 0;
            return;
        }

        debugCenter = center;
        debugRadius = radius;

        QueryTriggerInteraction query = ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;
        int hitCount = Physics.OverlapSphereNonAlloc(center, radius, overlapResults, affectedLayers, query);

        float radiusSqr = radius * radius;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapResults[i];
            if (hit == null)
                continue;

            if (ignoreOwnHierarchy && hit.transform.IsChildOf(transform))
                continue;

            Renderer[] renderers = hit.GetComponentsInChildren<Renderer>(true);
            for (int j = 0; j < renderers.Length; j++)
            {
                Renderer renderer = renderers[j];
                if (renderer == null)
                    continue;

                if (!renderer.enabled)
                    continue;

                if (ignoreOwnHierarchy && renderer.transform.IsChildOf(transform))
                    continue;

                if (!RendererIntersectsSphere(renderer, center, radiusSqr))
                    continue;

                cachedRenderers.Add(renderer);
            }
        }

        rendererCount = cachedRenderers.Count;
        RebuildDrawEntries();
    }

    private void RebuildDrawEntries()
    {
        drawEntries.Clear();

        foreach (Renderer renderer in cachedRenderers)
        {
            if (renderer == null || !renderer.enabled)
                continue;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Mesh mesh = meshFilter.sharedMesh;
                int subMeshCount = mesh.subMeshCount;
                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    drawEntries.Add(new MeshDrawEntry
                    {
                        mesh = mesh,
                        matrix = renderer.localToWorldMatrix,
                        subMeshIndex = sub
                    });
                }

                continue;
            }

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null && skinned.sharedMesh != null)
            {
                Mesh mesh = skinned.sharedMesh;
                int subMeshCount = mesh.subMeshCount;
                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    drawEntries.Add(new MeshDrawEntry
                    {
                        mesh = mesh,
                        matrix = renderer.localToWorldMatrix,
                        subMeshIndex = sub
                    });
                }
            }
        }
    }

    private void SubmitOverlayMeshes()
    {
        if (overlayCamera == null)
            return;

        if (!overlayCamera.enabled)
            return;

        Material mat = GetOrCreateOverlayMaterial();
        if (mat == null)
            return;

        for (int i = 0; i < drawEntries.Count; i++)
        {
            MeshDrawEntry entry = drawEntries[i];
            if (entry.mesh == null)
                continue;

            Graphics.DrawMesh(
                entry.mesh,
                entry.matrix,
                mat,
                previewLayer,
                overlayCamera,
                entry.subMeshIndex,
                null,
                ShadowCastingMode.Off,
                false,
                null,
                LightProbeUsage.Off,
                null);
        }
    }

    private Material GetOrCreateOverlayMaterial()
    {
        if (overlayMaterial != null)
        {
            ApplyMaterialColor(overlayMaterial);
            return overlayMaterial;
        }

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return null;

        overlayMaterial = new Material(shader);
        overlayMaterial.name = "YellowAreaOverlay_Mat_Auto";
        overlayMaterial.hideFlags = HideFlags.HideAndDontSave;

        overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        overlayMaterial.SetInt("_Cull", (int)CullMode.Back);
        overlayMaterial.SetInt("_ZWrite", 0);
        overlayMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        overlayMaterial.DisableKeyword("_ALPHATEST_ON");
        overlayMaterial.EnableKeyword("_ALPHABLEND_ON");
        overlayMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        ApplyMaterialColor(overlayMaterial);
        return overlayMaterial;
    }

    private void ApplyMaterialColor(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", tintColor);
    }

    private bool TryGetArea(out Vector3 center, out float radius)
    {
        if (syncToTreeSearchRadius && linkedWorkArea != null)
        {
            center = linkedWorkArea.GetReferencePosition();
            radius = Mathf.Max(0.01f, linkedWorkArea.TreeSearchRadius);
            return true;
        }

        if (areaCollider != null)
        {
            Bounds bounds = areaCollider.bounds;
            center = bounds.center;
            radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            return true;
        }

        center = transform.position;
        radius = 0f;
        return false;
    }

    private bool RendererIntersectsSphere(Renderer renderer, Vector3 center, float radiusSqr)
    {
        Bounds bounds = renderer.bounds;
        Vector3 closest = bounds.ClosestPoint(center);
        return (closest - center).sqrMagnitude <= radiusSqr;
    }

    private void AutoAssign()
    {
        if (linkedWorkArea == null)
        {
            linkedWorkArea = GetComponent<WoodcutterWorkArea>();
            if (linkedWorkArea == null)
                linkedWorkArea = GetComponentInParent<WoodcutterWorkArea>();
            if (linkedWorkArea == null)
                linkedWorkArea = GetComponentInChildren<WoodcutterWorkArea>();
        }

        if (areaCollider == null)
            areaCollider = GetComponent<Collider>();
    }

    private void EnsureOverlayCamera()
    {
        if (!autoCreateOverlayCamera)
            return;

        if (overlayCamera == null)
        {
            Transform existing = transform.Find(overlayCameraName);
            if (existing != null)
                overlayCamera = existing.GetComponent<Camera>();
        }

        if (overlayCamera == null)
        {
            GameObject camObject = new GameObject(overlayCameraName);
            camObject.transform.SetParent(transform, false);
            camObject.transform.localPosition = Vector3.zero;
            camObject.transform.localRotation = Quaternion.identity;
            camObject.transform.localScale = Vector3.one;

            overlayCamera = camObject.AddComponent<Camera>();
            createdOverlayCameraAtRuntime = true;
        }

        overlayCamera.enabled = tintActive;
        overlayCamera.clearFlags = CameraClearFlags.Depth;
        overlayCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        overlayCamera.cullingMask = 0;
        overlayCamera.gameObject.layer = previewLayer;
        overlayCamera.useOcclusionCulling = false;
        overlayCamera.allowMSAA = true;
        overlayCamera.allowHDR = false;
    }

    private void SyncOverlayCameraToMain()
    {
        if (overlayCamera == null)
            return;

        Camera source = GetMainReferenceCamera();
        if (source == null)
            return;

        overlayCamera.transform.position = source.transform.position;
        overlayCamera.transform.rotation = source.transform.rotation;

        overlayCamera.orthographic = source.orthographic;
        overlayCamera.fieldOfView = source.fieldOfView;
        overlayCamera.orthographicSize = source.orthographicSize;
        overlayCamera.nearClipPlane = source.nearClipPlane;
        overlayCamera.farClipPlane = source.farClipPlane;
        overlayCamera.rect = source.rect;
        overlayCamera.depth = source.depth + depthOffset;
        overlayCamera.aspect = source.aspect;
        overlayCamera.clearFlags = CameraClearFlags.Depth;
    }

    private Camera GetMainReferenceCamera()
    {
        if (cachedMainCamera != null && cachedMainCamera.isActiveAndEnabled)
            return cachedMainCamera;

        Camera main = Camera.main;
        if (main != null && main != overlayCamera && main.isActiveAndEnabled)
        {
            cachedMainCamera = main;
            return cachedMainCamera;
        }

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || cam == overlayCamera || !cam.isActiveAndEnabled)
                continue;

            cachedMainCamera = cam;
            return cachedMainCamera;
        }

        return null;
    }

    private void UpdateOverlayCameraEnabled()
    {
        if (overlayCamera != null)
            overlayCamera.enabled = tintActive;
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryGetArea(out Vector3 center, out float radius))
            return;

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.2f);
        Gizmos.DrawSphere(center, radius);

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(center, radius);
    }
}
