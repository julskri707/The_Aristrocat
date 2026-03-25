public interface IDamageable
{
    CombatTeam Team { get; }
    bool IsDead { get; }

    bool CanReceiveDamage(DamageInfo damageInfo);
    bool ApplyDamage(DamageInfo damageInfo);
}
