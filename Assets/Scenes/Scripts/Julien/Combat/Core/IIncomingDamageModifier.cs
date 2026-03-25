public interface IIncomingDamageModifier
{
    int Priority { get; }

    void ModifyIncomingDamage(DamageableHealth target, IncomingDamageContext context);
}
