using UnityEngine;

public class PlayerAxeInteractor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera lookCamera;
    [SerializeField] private PlayerEquipment playerEquipment;

    [Header("Interaction")]
    [SerializeField] private float hitDistance = 3.5f;
    [SerializeField] private float hitCooldown = 0.45f;
    [SerializeField] private LayerMask raycastMask = ~0;

    private float cooldownTimer;

    private void Awake()
    {
        if (lookCamera == null)
            lookCamera = Camera.main;

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();

        if (playerEquipment == null)
            playerEquipment = GetComponentInParent<PlayerEquipment>();
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        if (cooldownTimer > 0f)
            return;

        if (playerEquipment == null || playerEquipment.GetActiveChopPower() <= 0)
            return;

        if (lookCamera == null)
            return;

        Ray ray = new Ray(lookCamera.transform.position, lookCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, hitDistance, raycastMask, QueryTriggerInteraction.Ignore))
        {
            AxeTree tree = hit.collider.GetComponentInParent<AxeTree>();
            if (tree == null)
                tree = hit.collider.GetComponent<AxeTree>();

            if (tree == null)
                return;

            tree.ApplyHit(playerEquipment.GetActiveChopPower());
            cooldownTimer = hitCooldown;

            HeldItemSway sway = playerEquipment.GetComponentInChildren<HeldItemSway>();
            if (sway != null)
                sway.TriggerUseSwing();
        }
    }
}
