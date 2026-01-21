using UnityEngine;

public class ConeTopUIHandle : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public RectTransform canvasRoot;
    public UIHandleDrag handlePrefab;

    [Header("Target")]
    public ConeMesh cone;         // ton script
    public Transform coneTransform; // l’objet du cône (pour position world)
    public float minHeight = 0.2f;
    public float maxHeight = 20f;

    UIHandleDrag handle;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Start()
    {
        handle = Instantiate(handlePrefab, canvasRoot);
        handle.cam = cam;

        // drag => change la hauteur du cone
        handle.onDeltaY = (delta) =>
        {
            cone.height = Mathf.Clamp(cone.height + delta, minHeight, maxHeight);
            cone.Build();
        };
    }

    void LateUpdate()
    {
        if (handle == null || cone == null || coneTransform == null) return;

        // position world du sommet (approx) : base + height sur Y local
        Vector3 topWorld = coneTransform.TransformPoint(new Vector3(0f, cone.height, 0f));

        Vector2 screen = cam.WorldToScreenPoint(topWorld);
        handle.SetScreenPosition(screen);
    }
}
