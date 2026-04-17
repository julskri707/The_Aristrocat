using UnityEngine;

public class HeldItemSway : MonoBehaviour
{
    [Header("Idle Sway")]
    [SerializeField] private float moveAmount = 0.035f;
    [SerializeField] private float rotateAmount = 5.5f;
    [SerializeField] private float smooth = 10f;
    [SerializeField] private float maxMouseSample = 3f;

    [Header("Breathing")]
    [SerializeField] private bool enableBreathing = true;
    [SerializeField] private float breathingSpeed = 1.75f;
    [SerializeField] private float breathingPositionAmount = 0.012f;
    [SerializeField] private float breathingRotationAmount = 1.25f;

    [Header("Walk Bob")]
    [SerializeField] private bool enableWalkBob = true;
    [SerializeField] private float bobSpeed = 8f;
    [SerializeField] private float bobPositionAmount = 0.018f;
    [SerializeField] private float bobRotationAmount = 2.5f;

    [Header("Swing")]
    [SerializeField] private bool useSwingAnimation = true;
    [SerializeField] private Vector3 swingStartPositionOffset = new Vector3(0.06f, -0.04f, -0.02f);
    [SerializeField] private Vector3 swingStartRotationOffset = new Vector3(-18f, 24f, 10f);
    [SerializeField] private Vector3 swingEndPositionOffset = new Vector3(-0.09f, 0.02f, 0.08f);
    [SerializeField] private Vector3 swingEndRotationOffset = new Vector3(58f, -42f, -16f);
    [SerializeField] private float swingWindupDuration = 0.08f;
    [SerializeField] private float swingStrikeDuration = 0.12f;
    [SerializeField] private float swingRecoverDuration = 0.16f;
    [SerializeField] private AnimationCurve swingEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Activation")]
    [SerializeField] private bool onlyActiveWhenHeldByPlayer = true;

    private Vector3 startLocalPosition;
    private Quaternion startLocalRotation;

    private float walkBobTimer;
    private bool isSwinging;
    private float swingTimer;
    private int swingPhase;
    private Vector3 currentSwingPosOffset;
    private Quaternion currentSwingRotOffset = Quaternion.identity;

    private void Awake()
    {
        CacheStartPose();
    }

    private void OnEnable()
    {
        CacheStartPose();
        ResetRuntimeOffsets();
    }

    private void Update()
    {
        if (onlyActiveWhenHeldByPlayer && !IsCurrentlyHeldByPlayer())
            return;

        float mouseX = Mathf.Clamp(Input.GetAxis("Mouse X"), -maxMouseSample, maxMouseSample);
        float mouseY = Mathf.Clamp(Input.GetAxis("Mouse Y"), -maxMouseSample, maxMouseSample);

        Vector3 swayPosOffset = new Vector3(-mouseX, -mouseY, 0f) * moveAmount;
        Quaternion swayRotOffset = Quaternion.Euler(mouseY * rotateAmount, -mouseX * rotateAmount, mouseX * rotateAmount);

        Vector3 breathingPosOffset = Vector3.zero;
        Quaternion breathingRotOffset = Quaternion.identity;

        if (enableBreathing)
        {
            float t = Time.time * breathingSpeed;
            float breathSin = Mathf.Sin(t);
            breathingPosOffset = new Vector3(0f, breathSin * breathingPositionAmount, 0f);
            breathingRotOffset = Quaternion.Euler(breathSin * breathingRotationAmount, 0f, breathSin * breathingRotationAmount * 0.35f);
        }

        Vector3 bobPosOffset = Vector3.zero;
        Quaternion bobRotOffset = Quaternion.identity;

        if (enableWalkBob)
        {
            float moveInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) + Mathf.Abs(Input.GetAxisRaw("Vertical"));
            bool isMoving = moveInput > 0.05f && Cursor.lockState == CursorLockMode.Locked;

            if (isMoving)
            {
                walkBobTimer += Time.deltaTime * bobSpeed;
                float bobSin = Mathf.Sin(walkBobTimer);
                float bobCos = Mathf.Cos(walkBobTimer * 0.5f);

                bobPosOffset = new Vector3(bobSin * bobPositionAmount * 0.5f, Mathf.Abs(bobSin) * bobPositionAmount, 0f);
                bobRotOffset = Quaternion.Euler(Mathf.Abs(bobSin) * bobRotationAmount, 0f, bobCos * bobRotationAmount);
            }
            else
            {
                walkBobTimer = Mathf.Lerp(walkBobTimer, 0f, Time.deltaTime * 5f);
            }
        }

        UpdateSwingAnimation();

        Vector3 finalPos = startLocalPosition + swayPosOffset + breathingPosOffset + bobPosOffset + currentSwingPosOffset;
        Quaternion finalRot = startLocalRotation * swayRotOffset * breathingRotOffset * bobRotOffset * currentSwingRotOffset;

        transform.localPosition = Vector3.Lerp(transform.localPosition, finalPos, Time.deltaTime * smooth);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, finalRot, Time.deltaTime * smooth);
    }

    public void TriggerUseSwing()
    {
        if (!useSwingAnimation)
            return;

        if (onlyActiveWhenHeldByPlayer && !IsCurrentlyHeldByPlayer())
            return;

        isSwinging = true;
        swingTimer = 0f;
        swingPhase = 0;
    }

    private void UpdateSwingAnimation()
    {
        if (!isSwinging)
        {
            currentSwingPosOffset = Vector3.Lerp(currentSwingPosOffset, Vector3.zero, Time.deltaTime * 12f);
            currentSwingRotOffset = Quaternion.Slerp(currentSwingRotOffset, Quaternion.identity, Time.deltaTime * 12f);
            return;
        }

        float phaseDuration = GetCurrentPhaseDuration();
        if (phaseDuration <= 0.0001f)
            phaseDuration = 0.0001f;

        swingTimer += Time.deltaTime;
        float t = Mathf.Clamp01(swingTimer / phaseDuration);
        float eased = swingEase != null ? swingEase.Evaluate(t) : t;

        if (swingPhase == 0)
        {
            currentSwingPosOffset = Vector3.Lerp(Vector3.zero, swingStartPositionOffset, eased);
            currentSwingRotOffset = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(swingStartRotationOffset), eased);
        }
        else if (swingPhase == 1)
        {
            currentSwingPosOffset = Vector3.Lerp(swingStartPositionOffset, swingEndPositionOffset, eased);
            currentSwingRotOffset = Quaternion.Slerp(Quaternion.Euler(swingStartRotationOffset), Quaternion.Euler(swingEndRotationOffset), eased);
        }
        else
        {
            currentSwingPosOffset = Vector3.Lerp(swingEndPositionOffset, Vector3.zero, eased);
            currentSwingRotOffset = Quaternion.Slerp(Quaternion.Euler(swingEndRotationOffset), Quaternion.identity, eased);
        }

        if (t >= 1f)
        {
            swingTimer = 0f;
            swingPhase++;

            if (swingPhase > 2)
            {
                isSwinging = false;
                swingPhase = 0;
            }
        }
    }

    private float GetCurrentPhaseDuration()
    {
        if (swingPhase == 0) return swingWindupDuration;
        if (swingPhase == 1) return swingStrikeDuration;
        return swingRecoverDuration;
    }

    private void CacheStartPose()
    {
        startLocalPosition = transform.localPosition;
        startLocalRotation = transform.localRotation;
    }

    private void ResetRuntimeOffsets()
    {
        walkBobTimer = 0f;
        isSwinging = false;
        swingTimer = 0f;
        swingPhase = 0;
        currentSwingPosOffset = Vector3.zero;
        currentSwingRotOffset = Quaternion.identity;
    }

    private bool IsCurrentlyHeldByPlayer()
    {
        if (transform.parent == null)
            return false;

        PlayerEquipment equipment = GetComponentInParent<PlayerEquipment>();
        return equipment != null;
    }
}
