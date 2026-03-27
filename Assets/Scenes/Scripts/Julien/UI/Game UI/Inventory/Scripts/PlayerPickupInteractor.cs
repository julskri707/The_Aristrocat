using UnityEngine;

public class PlayerPickupInteractor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera lookCamera;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PickupPromptUI promptUI;

    [Header("Interaction")]
    [SerializeField] private float interactDistance = 4f;
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField] private KeyCode pickupMouseButton = KeyCode.Mouse0;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay = false;

    private WorldPickupItem currentPickup;

    private void Awake()
    {
        if (playerInventory == null)
            playerInventory = GetComponent<PlayerInventory>();

        if (playerInventory == null)
            playerInventory = GetComponentInParent<PlayerInventory>();

        if (lookCamera == null)
            lookCamera = Camera.main;
    }

    private void Update()
    {
        UpdateLookTarget();

        if (currentPickup != null && Input.GetKeyDown(pickupMouseButton))
        {
            bool pickedUp = currentPickup.TryPickup(playerInventory);
            if (pickedUp)
            {
                currentPickup = null;
                if (promptUI != null)
                    promptUI.Hide();
            }
        }
    }

    private void UpdateLookTarget()
    {
        currentPickup = null;

        if (lookCamera == null)
        {
            if (promptUI != null)
                promptUI.Hide();
            return;
        }

        Ray ray = new Ray(lookCamera.transform.position, lookCamera.transform.forward);

        if (drawDebugRay)
            Debug.DrawRay(ray.origin, ray.direction * interactDistance, Color.green);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, raycastMask, QueryTriggerInteraction.Collide))
        {
            WorldPickupItem pickup = hit.collider.GetComponentInParent<WorldPickupItem>();
            if (pickup == null)
                pickup = hit.collider.GetComponent<WorldPickupItem>();

            if (pickup != null && pickup.AllowLookPickup)
            {
                currentPickup = pickup;

                if (promptUI != null)
                    promptUI.Show(pickup.GetPromptText());

                return;
            }
        }

        if (promptUI != null)
            promptUI.Hide();
    }
}
