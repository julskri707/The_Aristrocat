using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HomeSite : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform bedPoint;
    [SerializeField] private Transform bedStandPoint;
    [SerializeField, Min(1)] private int capacity = 1;

    [Header("Sleep Teleport")]
    [SerializeField, Min(0)] private int teleportIntoBedDelayTicks = 1;

    [Header("Sleep Need Restore Per Tick")]
    [SerializeField, Range(0f, 100f)] private float energyRestorePerTickInBed = 16f;
    [SerializeField, Range(0f, 100f)] private float warmthRestorePerTickInBed = 3f;
    [SerializeField, Range(0f, 100f)] private float safetyRestorePerTickInBed = 1f;
    [SerializeField, Range(-100f, 0f)] private float hungerDeltaPerTickInBed = -0.35f;

    private readonly HashSet<int> reservedNpcIds = new HashSet<int>();

    public Transform BedPoint => bedPoint != null ? bedPoint : transform;
    public Transform BedStandPoint => bedStandPoint != null ? bedStandPoint : BedPoint;

    public int TeleportIntoBedDelayTicks => Mathf.Max(0, teleportIntoBedDelayTicks);

    public float EnergyRestorePerTickInBed => energyRestorePerTickInBed;
    public float WarmthRestorePerTickInBed => warmthRestorePerTickInBed;
    public float SafetyRestorePerTickInBed => safetyRestorePerTickInBed;
    public float HungerDeltaPerTickInBed => hungerDeltaPerTickInBed;

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

    private void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        teleportIntoBedDelayTicks = Mathf.Max(0, teleportIntoBedDelayTicks);

        energyRestorePerTickInBed = Mathf.Clamp(energyRestorePerTickInBed, 0f, 100f);
        warmthRestorePerTickInBed = Mathf.Clamp(warmthRestorePerTickInBed, 0f, 100f);
        safetyRestorePerTickInBed = Mathf.Clamp(safetyRestorePerTickInBed, 0f, 100f);
        hungerDeltaPerTickInBed = Mathf.Clamp(hungerDeltaPerTickInBed, -100f, 0f);
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
        return BedStandPoint.position;
    }
}