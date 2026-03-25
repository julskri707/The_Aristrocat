using UnityEngine;

public class PlayerBowAnimationRelay : MonoBehaviour
{
    [SerializeField] private PlayerBowCombat targetBowCombat;

    private void Awake()
    {
        if (targetBowCombat == null)
        {
            targetBowCombat = GetComponentInParent<PlayerBowCombat>();
        }

        if (targetBowCombat == null)
        {
            Debug.LogWarning($"[{nameof(PlayerBowAnimationRelay)}] No {nameof(PlayerBowCombat)} found for '{name}'.", this);
        }
    }

    public void AE_ReleaseBowShot()
    {
        if (targetBowCombat != null)
        {
            targetBowCombat.AE_ReleaseBowShot();
        }
    }
}
