using UnityEngine;

[DisallowMultipleComponent]
public class FieldOutline : MonoBehaviour
{
    public float yOffset = 0.02f;
    public float lineWidth = 0.05f;

    public Color normalColor = Color.black;
    public Color selectedColor = Color.yellow;

    private LineRenderer _lr;
    private Collider _col;
    private bool _selected;

    private void Awake()
    {
        _col = GetComponentInChildren<Collider>();
        if (_col == null) _col = GetComponent<Collider>();

        EnsureLR();
        Rebuild();
    }

    private void OnValidate()
    {
        _col = GetComponentInChildren<Collider>();
        if (_col == null) _col = GetComponent<Collider>();

        EnsureLR();
        Rebuild();
    }

    public void SetSelected(bool value)
    {
        _selected = value;
        var c = _selected ? selectedColor : normalColor;
        _lr.startColor = c;
        _lr.endColor = c;
    }

    private void EnsureLR()
    {
        if (_lr == null)
        {
            _lr = GetComponent<LineRenderer>();
            if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();
        }

        _lr.useWorldSpace = true;
        _lr.loop = true;
        _lr.positionCount = 4;
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;

        if (_lr.sharedMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _lr.sharedMaterial = new Material(shader);
        }

        SetSelected(_selected);
    }

    private void Rebuild()
    {
        if (_lr == null || _col == null) return;

        Bounds b = _col.bounds;

        Vector3 p0 = new Vector3(b.min.x, b.center.y, b.min.z);
        Vector3 p1 = new Vector3(b.min.x, b.center.y, b.max.z);
        Vector3 p2 = new Vector3(b.max.x, b.center.y, b.max.z);
        Vector3 p3 = new Vector3(b.max.x, b.center.y, b.min.z);

        p0.y += yOffset; p1.y += yOffset; p2.y += yOffset; p3.y += yOffset;

        _lr.SetPosition(0, p0);
        _lr.SetPosition(1, p1);
        _lr.SetPosition(2, p2);
        _lr.SetPosition(3, p3);

        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;
    }
}
