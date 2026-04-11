using UnityEngine;

public class HeldItemSway : MonoBehaviour
{
    [Header("Sway")]
    [SerializeField] private float moveAmount = 0.03f;
    [SerializeField] private float rotateAmount = 4f;
    [SerializeField] private float smooth = 8f;

    [Header("Use Swing")]
    [SerializeField] private Vector3 useRotationOffset = new Vector3(-35f, 18f, 0f);
    [SerializeField] private float useSwingSpeed = 14f;

    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;
    private Quaternion currentUseOffset = Quaternion.identity;

    private void Awake()
    {
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
    }

    private void Update()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        Vector3 targetPos = startLocalPosition + new Vector3(-mouseX, -mouseY, 0f) * moveAmount;
        Quaternion targetRot = startLocalRotation * Quaternion.Euler(mouseY * rotateAmount, -mouseX * rotateAmount, mouseX * rotateAmount);

        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * smooth);

        currentUseOffset = Quaternion.Slerp(currentUseOffset, Quaternion.identity, Time.deltaTime * useSwingSpeed);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot * currentUseOffset, Time.deltaTime * smooth);
    }

    public void TriggerUseSwing()
    {
        currentUseOffset = Quaternion.Euler(useRotationOffset);
    }
}
