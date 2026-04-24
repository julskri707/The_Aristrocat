using UnityEngine;

public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly Vector3 HitPoint;
    public readonly Vector3 HitDirection;
    public readonly GameObject Source;
    public readonly Transform SourceTransform;
    public readonly CombatTeam SourceTeam;
    public readonly bool IgnoresFriendlyFire;
    public readonly string DamageId;
    public readonly bool IsCriticalHit;
    public readonly float CriticalMultiplierApplied;

    public DamageInfo(
        float amount,
        Vector3 hitPoint,
        Vector3 hitDirection,
        GameObject source,
        Transform sourceTransform,
        CombatTeam sourceTeam,
        bool ignoresFriendlyFire,
        string damageId,
        bool isCriticalHit,
        float criticalMultiplierApplied)
    {
        Amount = amount;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        Source = source;
        SourceTransform = sourceTransform;
        SourceTeam = sourceTeam;
        IgnoresFriendlyFire = ignoresFriendlyFire;
        DamageId = damageId;
        IsCriticalHit = isCriticalHit;
        CriticalMultiplierApplied = criticalMultiplierApplied;
    }
}
