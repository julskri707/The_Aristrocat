using UnityEngine;

public class PlayerCombatWeaponSwitcher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerSwordCombat swordCombat;
    [SerializeField] private PlayerBowCombat bowCombat;
    [SerializeField] private Animator animator;

    [Header("Start Mode")]
    [SerializeField] private PlayerCombatWeaponMode startMode = PlayerCombatWeaponMode.Sword;

    [Header("Input")]
    [SerializeField] private bool useBuiltInInput = true;
    [SerializeField] private KeyCode swordKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode bowKey = KeyCode.Alpha2;

    [Header("Optional Visuals")]
    [SerializeField] private GameObject[] swordVisuals = new GameObject[0];
    [SerializeField] private GameObject[] bowVisuals = new GameObject[0];

    [Header("Animator Optional")]
    [SerializeField] private bool driveAnimator = true;
    [SerializeField] private string weaponModeIntName = "WeaponMode";
    [SerializeField] private bool useSwitchTrigger = false;
    [SerializeField] private string switchTriggerName = "SwitchWeapon";

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private PlayerCombatWeaponMode currentMode;

    public PlayerCombatWeaponMode CurrentMode => currentMode;

    private void Awake()
    {
        if (swordCombat == null)
        {
            swordCombat = GetComponent<PlayerSwordCombat>();
        }

        if (bowCombat == null)
        {
            bowCombat = GetComponent<PlayerBowCombat>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (swordCombat == null)
        {
            Debug.LogWarning($"[{nameof(PlayerCombatWeaponSwitcher)}] Missing {nameof(PlayerSwordCombat)} on '{name}'.", this);
        }

        if (bowCombat == null)
        {
            Debug.LogWarning($"[{nameof(PlayerCombatWeaponSwitcher)}] Missing {nameof(PlayerBowCombat)} on '{name}'.", this);
        }
    }

    private void Start()
    {
        SetMode(startMode, true);
    }

    private void Update()
    {
        if (!useBuiltInInput)
            return;

        if (Input.GetKeyDown(swordKey))
        {
            SetMode(PlayerCombatWeaponMode.Sword);
        }
        else if (Input.GetKeyDown(bowKey))
        {
            SetMode(PlayerCombatWeaponMode.Bow);
        }
    }

    public void SetMode(PlayerCombatWeaponMode newMode)
    {
        SetMode(newMode, false);
    }

    public void SetMode(PlayerCombatWeaponMode newMode, bool force)
    {
        if (!force && currentMode == newMode)
            return;

        currentMode = newMode;

        if (currentMode == PlayerCombatWeaponMode.Sword)
        {
            ActivateSwordMode();
        }
        else
        {
            ActivateBowMode();
        }

        UpdateVisuals();
        UpdateAnimator(force);

        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerCombatWeaponSwitcher)}] Switched to {currentMode}.", this);
        }
    }

    private void ActivateSwordMode()
    {
        if (bowCombat != null)
        {
            bowCombat.CancelBowState();
            bowCombat.enabled = false;
        }

        if (swordCombat != null)
        {
            swordCombat.FinishAttack();
            swordCombat.enabled = true;
        }
    }

    private void ActivateBowMode()
    {
        if (swordCombat != null)
        {
            swordCombat.FinishAttack();
            swordCombat.enabled = false;
        }

        if (bowCombat != null)
        {
            bowCombat.enabled = true;
            bowCombat.CancelBowState();
        }
    }

    private void UpdateVisuals()
    {
        bool swordActive = currentMode == PlayerCombatWeaponMode.Sword;
        bool bowActive = currentMode == PlayerCombatWeaponMode.Bow;

        SetVisualGroupActive(swordVisuals, swordActive);
        SetVisualGroupActive(bowVisuals, bowActive);
    }

    private void UpdateAnimator(bool force)
    {
        if (!driveAnimator || animator == null)
            return;

        if (!string.IsNullOrWhiteSpace(weaponModeIntName))
        {
            animator.SetInteger(weaponModeIntName, (int)currentMode);
        }

        if (!force && useSwitchTrigger && !string.IsNullOrWhiteSpace(switchTriggerName))
        {
            animator.ResetTrigger(switchTriggerName);
            animator.SetTrigger(switchTriggerName);
        }
    }

    private void SetVisualGroupActive(GameObject[] objects, bool value)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
            {
                objects[i].SetActive(value);
            }
        }
    }
}
