using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class ControlPointLinkUI : MonoBehaviour
{
    [Header("Binding (assigné par le Manager)")]
    public Camera cam;
    public IControlPointProvider provider;
    public int indexA;
    public int indexB;

    [Header("Look")]
    public float thickness = 4f;
    public float zOffset = 0f; // laisse à 0

    private RectTransform _rect;

    void Awake()
    {
        _rect = (RectTransform)transform;
    }

    void LateUpdate()
    {
        if (cam == null || provider == null) return;

        if (!provider.IsControlPointEditable(indexA) || !provider.IsControlPointEditable(indexB))
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        Vector3 wa = provider.GetControlPointWorld(indexA);
        Vector3 wb = provider.GetControlPointWorld(indexB);

        Vector3 sa = cam.WorldToScreenPoint(wa);
        Vector3 sb = cam.WorldToScreenPoint(wb);

        // Si un point est derrière la caméra -> hide
        if (sa.z <= 0f || sb.z <= 0f)
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf) gameObject.SetActive(true);

        Vector2 a = sa;
        Vector2 b = sb;

        Vector2 dir = (b - a);
        float length = dir.magnitude;
        if (length < 0.001f) length = 0.001f;

        Vector2 mid = (a + b) * 0.5f;

        // Position au milieu
        _rect.position = new Vector3(mid.x, mid.y, zOffset);

        // Taille: longueur + épaisseur
        _rect.sizeDelta = new Vector2(length, thickness);

        // Rotation
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _rect.rotation = Quaternion.Euler(0f, 0f, angle);
    }
}
