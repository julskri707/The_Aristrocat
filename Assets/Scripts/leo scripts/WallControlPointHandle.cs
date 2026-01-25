using UnityEngine;

[RequireComponent(typeof(Collider))]
public class WallControlPointHandle : MonoBehaviour
{
    [HideInInspector] public WallObject wall;
    [HideInInspector] public int pointIndex;

    public void Init(WallObject w, int idx, Vector3 pos)
    {
        wall = w;
        pointIndex = idx;
        transform.position = pos;
        gameObject.name = $"CP_{idx}";
    }
}
