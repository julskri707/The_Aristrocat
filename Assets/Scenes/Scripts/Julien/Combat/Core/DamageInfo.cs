using UnityEngine;

[System.Serializable]
public struct DamageInfo
{
    public float amount;
    public Vector3 hitPoint;
    public Vector3 hitDirection;
    public GameObject source;
    public Transform sourceTransform;
    public CombatTeam sourceTeam;
    public bool ignoresFriendlyFire;
    public string damageId;

    public bool isCriticalHit;
    public float criticalMultiplierApplied;

    public DamageInfo(
        float amount,
        Vector3 hitPoint,
        Vector3 hitDirection,
        GameObject source,
        Transform sourceTransform,
        CombatTeam sourceTeam,
        bool ignoresFriendlyFire = false,
        string damageId = "",
        bool isCriticalHit = false,
        float criticalMultiplierApplied = 1f)
    {
        this.amount = amount;
        this.hitPoint = hitPoint;
        this.hitDirection = hitDirection;
        this.source = source;
        this.sourceTransform = sourceTransform;
        this.sourceTeam = sourceTeam;
        this.ignoresFriendlyFire = ignoresFriendlyFire;
        this.damageId = damageId;

        this.isCriticalHit = isCriticalHit;
        this.criticalMultiplierApplied = criticalMultiplierApplied;
    }
}
