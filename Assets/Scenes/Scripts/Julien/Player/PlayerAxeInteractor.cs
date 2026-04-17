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

    [Header("Auto Find")]
    [SerializeField] private bool autoFindTreeNodeOnHit = true;
    [SerializeField] private bool autoAddMissingTreeNode = true;
    [SerializeField] private int autoTreeHealth = 3;
    [SerializeField] private int autoTreeWoodYield = 6;

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
        if (!Physics.Raycast(ray, out RaycastHit hit, hitDistance, raycastMask, QueryTriggerInteraction.Collide))
            return;

        TreeResourceNode treeNode = FindTreeNode(hit.collider);
        if (treeNode == null && autoAddMissingTreeNode)
            treeNode = TryAutoAddTreeNode(hit.collider);

        if (treeNode == null)
            return;

        if (!treeNode.ApplyChopDamage(playerEquipment.GetActiveChopPower()))
            return;

        cooldownTimer = hitCooldown;

        HeldItemSway sway = playerEquipment.GetComponentInChildren<HeldItemSway>();
        if (sway != null)
            sway.TriggerUseSwing();
    }

    private TreeResourceNode FindTreeNode(Collider hitCollider)
    {
        if (hitCollider == null)
            return null;

        TreeResourceNode treeNode = hitCollider.GetComponent<TreeResourceNode>();
        if (treeNode != null)
            return treeNode;

        if (autoFindTreeNodeOnHit)
        {
            treeNode = hitCollider.GetComponentInParent<TreeResourceNode>();
            if (treeNode != null)
                return treeNode;

            treeNode = hitCollider.GetComponentInChildren<TreeResourceNode>();
            if (treeNode != null)
                return treeNode;
        }

        return null;
    }

    private TreeResourceNode TryAutoAddTreeNode(Collider hitCollider)
    {
        if (hitCollider == null)
            return null;

        Transform root = hitCollider.transform.root;
        if (root == null)
            return null;

        TreeResourceNode existing = root.GetComponent<TreeResourceNode>();
        if (existing != null)
            return existing;

        TreeResourceNode node = root.gameObject.AddComponent<TreeResourceNode>();
        ApplyAutoDefaults(node, hitCollider, root);
        return node;
    }

    private void ApplyAutoDefaults(TreeResourceNode node, Collider hitCollider, Transform root)
    {
        if (node == null)
            return;

        SetPrivateField(node, "maxHealth", Mathf.Max(1, autoTreeHealth));
        SetPrivateField(node, "currentHealth", Mathf.Max(1, autoTreeHealth));
        SetPrivateField(node, "maxWoodYield", Mathf.Max(0, autoTreeWoodYield));
        SetPrivateField(node, "currentWood", Mathf.Max(0, autoTreeWoodYield));
        SetPrivateField(node, "treeCollider", hitCollider);
        SetPrivateField(node, "visualRoot", root);
    }

    private void SetPrivateField<TTarget>(TTarget target, string fieldName, object value) where TTarget : Object
    {
        if (target == null)
            return;

        var field = typeof(TTarget).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
            return;

        field.SetValue(target, value);
    }
}
