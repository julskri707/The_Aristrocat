public sealed class IncomingDamageContext
{
    public DamageInfo DamageInfo;
    public bool CancelDamage;

    public IncomingDamageContext(DamageInfo damageInfo)
    {
        DamageInfo = damageInfo;
        CancelDamage = false;
    }
}
