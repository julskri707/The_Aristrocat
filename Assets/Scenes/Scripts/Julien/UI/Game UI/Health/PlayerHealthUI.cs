using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlayerHealthUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text hpText;

    [Header("Options")]
    [SerializeField] private bool hideWhenMissingHealth = false;

    private void OnEnable()
    {
        if (playerHealth != null)
            playerHealth.OnHealthChanged += HandleHealthChanged;

        RefreshNow();
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.OnHealthChanged -= HandleHealthChanged;
    }

    private void Start()
    {
        RefreshNow();
    }

    public void SetPlayerHealth(PlayerHealth value)
    {
        if (playerHealth != null)
            playerHealth.OnHealthChanged -= HandleHealthChanged;

        playerHealth = value;

        if (playerHealth != null)
            playerHealth.OnHealthChanged += HandleHealthChanged;

        RefreshNow();
    }

    private void HandleHealthChanged(int currentHp, int maxHp)
    {
        UpdateUI(currentHp, maxHp);
    }

    private void RefreshNow()
    {
        if (playerHealth == null)
        {
            if (hideWhenMissingHealth)
                gameObject.SetActive(false);

            return;
        }

        gameObject.SetActive(true);
        UpdateUI(playerHealth.CurrentHp, playerHealth.MaxHp);
    }

    private void UpdateUI(int currentHp, int maxHp)
    {
        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = maxHp;
            healthSlider.value = currentHp;
        }

        if (fillImage != null)
        {
            fillImage.enabled = currentHp > 0;
        }

        if (hpText != null)
        {
            hpText.text = $"HP {currentHp} / {maxHp}";
        }
    }
}
