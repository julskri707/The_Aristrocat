using UnityEngine;

public class SwordCombatAnimationRelay : MonoBehaviour
{
    [SerializeField] private PlayerSwordCombat targetCombat;

    private void Awake()
    {
        if (targetCombat == null)
        {
            targetCombat = GetComponentInParent<PlayerSwordCombat>();
        }

        if (targetCombat == null)
        {
            Debug.LogWarning($"[{nameof(SwordCombatAnimationRelay)}] No {nameof(PlayerSwordCombat)} found for '{name}'.", this);
        }
    }

    public void AE_BeginSwordHitWindow()
    {
        if (targetCombat != null)
        {
            targetCombat.AE_BeginSwordHitWindow();
        }
    }

    public void AE_EndSwordHitWindow()
    {
        if (targetCombat != null)
        {
            targetCombat.AE_EndSwordHitWindow();
        }
    }

    public void AE_FinishSwordAttack()
    {
        if (targetCombat != null)
        {
            targetCombat.AE_FinishSwordAttack();
        }
    }
}
