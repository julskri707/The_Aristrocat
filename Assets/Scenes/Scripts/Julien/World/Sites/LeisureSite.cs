using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LeisureSite : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform interactionPoint;
    [SerializeField, Min(1)] private int capacity = 3;

    [Header("Need Restore Per Tick")]
    [SerializeField, Range(0f, 100f)] private float socialBonusPerTick = 12f;
    [SerializeField, Range(0f, 100f)] private float safetyBonusPerTick = 1f;

    private readonly HashSet<int> reservedNpcIds = new HashSet<int>();

    public Transform InteractionPoint => interactionPoint != null ? interactionPoint : transform;
    public int Capacity => Mathf.Max(1, capacity);
    public int ReservedCount => reservedNpcIds.Count;

    public float SocialBonusPerTick => socialBonusPerTick;
    public float SafetyBonusPerTick => safetyBonusPerTick;

    private void OnEnable()
    {
        SiteRegistry.Instance?.RegisterLeisureSite(this);
    }

    private void OnDisable()
    {
        SiteRegistry.Instance?.UnregisterLeisureSite(this);
        reservedNpcIds.Clear();
    }

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        socialBonusPerTick = Mathf.Clamp(socialBonusPerTick, 0f, 100f);
        safetyBonusPerTick = Mathf.Clamp(safetyBonusPerTick, 0f, 100f);
    }

    public bool IsReservedBy(GameObject npc)
    {
        if (npc == null)
            return false;

        return reservedNpcIds.Contains(npc.GetInstanceID());
    }

    public bool CanReserve(GameObject npc)
    {
        if (npc == null)
            return false;

        if (IsReservedBy(npc))
            return true;

        return reservedNpcIds.Count < Capacity;
    }

    public bool TryReserve(GameObject npc)
    {
        if (!CanReserve(npc))
            return false;

        reservedNpcIds.Add(npc.GetInstanceID());
        return true;
    }

    public void Release(GameObject npc)
    {
        if (npc == null)
            return;

        reservedNpcIds.Remove(npc.GetInstanceID());
    }

    public Vector3 GetUsePosition()
    {
        return InteractionPoint.position;
    }
}