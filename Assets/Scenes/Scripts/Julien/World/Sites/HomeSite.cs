using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HomeSite : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform bedPoint;
    [SerializeField, Min(1)] private int capacity = 1;

    private readonly HashSet<int> reservedNpcIds = new HashSet<int>();

    public Transform BedPoint => bedPoint != null ? bedPoint : transform;
    public int Capacity => Mathf.Max(1, capacity);
    public int ReservedCount => reservedNpcIds.Count;

    private void OnEnable()
    {
        SiteRegistry.Instance?.RegisterHomeSite(this);
    }

    private void OnDisable()
    {
        SiteRegistry.Instance?.UnregisterHomeSite(this);
        reservedNpcIds.Clear();
    }

    public bool IsReservedBy(GameObject npc)
    {
        if (npc == null) return false;
        return reservedNpcIds.Contains(npc.GetInstanceID());
    }

    public bool CanReserve(GameObject npc)
    {
        if (npc == null) return false;
        if (IsReservedBy(npc)) return true;
        return reservedNpcIds.Count < Capacity;
    }

    public bool TryReserve(GameObject npc)
    {
        if (!CanReserve(npc)) return false;
        reservedNpcIds.Add(npc.GetInstanceID());
        return true;
    }

    public void Release(GameObject npc)
    {
        if (npc == null) return;
        reservedNpcIds.Remove(npc.GetInstanceID());
    }

    public Vector3 GetUsePosition()
    {
        return BedPoint.position;
    }
}