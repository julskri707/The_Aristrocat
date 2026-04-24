using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstThirdPersonController : MonoBehaviour
{
    [Header("General")]
    public Camera firstPersonCamera;
    public Camera thirdPersonCamera;
    public KeyCode toggleViewKey = KeyCode.V;
    public KeyCode toggleUICursorKey = KeyCode.E;
    public KeyCode toggleInventoryKey = KeyCode.I;
    public KeyCode closeUIKey = KeyCode.Escape;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode jumpKey = KeyCode.Space;
    public bool startInFirstPerson = true;
    [Range(0f, 100f)] public float mouseSensitivity = 50f;

    [Header("UI Cursor Mode")]
    public bool startWithUICursorUnlocked = false;
    public bool blockMouseLookWhileUICursorUnlocked = true;
    public bool blockViewToggleWhileUICursorUnlocked = true;

    [Header("Assignment UI Toggle")]
    public GameObject assignmentPanelRoot;
    public bool toggleAssignmentPanelWithE = true;
    public bool startWithAssignmentPanelHidden = true;

    [Header("Inventory UI Toggle")]
    public GameObject inventoryPanelRoot;
    public bool toggleInventoryPanelWithI = true;
    public bool startWithInventoryPanelHidden = true;

    [Header("Movement")]
    public float walkSpeed = 3f;
    public float sprintSpeed = 5f;
    public float crouchSpeed = 1.5f;
    public float jumpSpeed = 5f;
    public float gravity = 9.81f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.3f;
    public LayerMask groundMask;

    [Header("Coyote Time")]
    public bool coyoteTimeEnabled = true;
    public float coyoteTimeDuration = 0.2f;

    [Header("Crouch")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;
    public float crouchTransitionSpeed = 10f;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("FOV")]
    public float normalFov = 60f;
    public float sprintFov = 70f;
    public float fovLerpSpeed = 10f;

    [Header("Look")]
    public float fpMinPitch = -90f;
    public float fpMaxPitch = 90f;
    public float thirdPersonMinPitch = -30f;
    public float thirdPersonMaxPitch = 75f;

    [Header("Headbob (First Person)")]
    public bool enableHeadbob = true;
    public float walkingBobbingSpeed = 10f;
    public float bobbingAmount = 0.05f;
    public float sprintBobMultiplier = 1.5f;
    public float headbobMoveThreshold = 0.1f;

    [Header("Runtime Debug")]
    [SerializeField] private bool uiCursorUnlocked;

    private CharacterController controller;
    private bool isFirstPerson;
    private bool isGrounded;
    private bool isCrouching;
    private bool isSprinting;

    private float coyoteTimer;
    private float currentFov;
    private float pitch;

    private Vector3 velocity;

    private float headbobTimer;
    private float currentHeadbobOffset;
    private Vector3 firstPersonCameraBaseLocalPos;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (firstPersonCamera != null)
        {
            firstPersonCamera.fieldOfView = normalFov;
            firstPersonCameraBaseLocalPos = firstPersonCamera.transform.localPosition;
        }

        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.fieldOfView = normalFov;
        }

        currentFov = normalFov;

        if (controller != null)
        {
            controller.height = standHeight;
            controller.center = new Vector3(0f, standHeight * 0.5f, 0f);
        }

        if (assignmentPanelRoot != null)
            assignmentPanelRoot.SetActive(!startWithAssignmentPanelHidden);

        if (inventoryPanelRoot != null)
            inventoryPanelRoot.SetActive(!startWithInventoryPanelHidden);
    }

    private void Start()
    {
        isFirstPerson = startInFirstPerson;

        bool anyPanelVisible = IsAssignmentPanelOpen() || IsInventoryPanelOpen();
        uiCursorUnlocked = anyPanelVisible ? true : startWithUICursorUnlocked;

        if (firstPersonCamera != null)
            firstPersonCameraBaseLocalPos = firstPersonCamera.transform.localPosition;

        ApplyCursorState();
        UpdateActiveCamera();
        ApplyCameraPitch();
        ApplyHeadbobOffset();
    }

    private void Update()
    {
        if (controller == null)
            return;

        HandlePanelToggles();
        HandleGroundCheck();
        HandleViewToggle();
        HandleMouseLook();
        HandleMovementAndJump();
        HandleCrouch();
        HandleFov();
        ApplyHeadbobOffset();
        EnforceCursorState();
    }

    public void SetUICursorUnlockedFromExternal(bool unlocked)
    {
        uiCursorUnlocked = unlocked;
        ApplyCursorState();
    }

    public void SetAssignmentPanelVisibleFromExternal(bool visible)
    {
        if (assignmentPanelRoot != null)
            assignmentPanelRoot.SetActive(visible);

        UpdateCursorFromPanelState();
    }

    public void SetInventoryPanelVisibleFromExternal(bool visible)
    {
        if (inventoryPanelRoot != null)
            inventoryPanelRoot.SetActive(visible);

        UpdateCursorFromPanelState();
    }

    private void HandlePanelToggles()
    {
        if (Input.GetKeyDown(closeUIKey))
        {
            if (IsInventoryPanelOpen())
            {
                SetInventoryPanelVisible(false);
                return;
            }

            if (IsAssignmentPanelOpen())
            {
                SetAssignmentPanelVisible(false);
                return;
            }

            if (!toggleAssignmentPanelWithE && !toggleInventoryPanelWithI)
            {
                uiCursorUnlocked = false;
                ApplyCursorState();
                return;
            }
        }

        if (toggleAssignmentPanelWithE && Input.GetKeyDown(toggleUICursorKey))
        {
            SetAssignmentPanelVisible(!IsAssignmentPanelOpen());
            return;
        }

        if (toggleInventoryPanelWithI && Input.GetKeyDown(toggleInventoryKey))
        {
            SetInventoryPanelVisible(!IsInventoryPanelOpen());
            return;
        }

        if (!toggleAssignmentPanelWithE && !toggleInventoryPanelWithI && Input.GetKeyDown(toggleUICursorKey))
        {
            uiCursorUnlocked = !uiCursorUnlocked;
            ApplyCursorState();
        }
    }

    private void SetAssignmentPanelVisible(bool visible)
    {
        if (assignmentPanelRoot != null)
            assignmentPanelRoot.SetActive(visible);

        if (visible && inventoryPanelRoot != null)
            inventoryPanelRoot.SetActive(false);

        UpdateCursorFromPanelState();
    }

    private void SetInventoryPanelVisible(bool visible)
    {
        if (inventoryPanelRoot != null)
            inventoryPanelRoot.SetActive(visible);

        if (visible && assignmentPanelRoot != null)
            assignmentPanelRoot.SetActive(false);

        UpdateCursorFromPanelState();
    }

    private void UpdateCursorFromPanelState()
    {
        uiCursorUnlocked = IsAssignmentPanelOpen() || IsInventoryPanelOpen();
        ApplyCursorState();
    }

    private bool IsAssignmentPanelOpen()
    {
        return assignmentPanelRoot != null && assignmentPanelRoot.activeSelf;
    }

    public bool IsInventoryPanelOpen()
    {
        return inventoryPanelRoot != null && inventoryPanelRoot.activeSelf;
    }

    private void ApplyCursorState()
    {
        if (uiCursorUnlocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void EnforceCursorState()
    {
        if (uiCursorUnlocked)
        {
            if (Cursor.lockState != CursorLockMode.None)
                Cursor.lockState = CursorLockMode.None;

            if (!Cursor.visible)
                Cursor.visible = true;
        }
        else
        {
            if (Cursor.lockState != CursorLockMode.Locked)
                Cursor.lockState = CursorLockMode.Locked;

            if (Cursor.visible)
                Cursor.visible = false;
        }
    }

    private void HandleGroundCheck()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(
                groundCheck.position,
                groundCheckRadius,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
        }
        else
        {
            isGrounded = controller.isGrounded;
        }

        if (isGrounded)
        {
            coyoteTimer = coyoteTimeEnabled ? coyoteTimeDuration : 0f;

            if (velocity.y < 0f)
                velocity.y = -2f;
        }
        else if (coyoteTimeEnabled)
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    private void HandleViewToggle()
    {
        if (blockViewToggleWhileUICursorUnlocked && uiCursorUnlocked)
            return;

        if (Input.GetKeyDown(toggleViewKey))
        {
            isFirstPerson = !isFirstPerson;
            headbobTimer = 0f;
            currentHeadbobOffset = 0f;
            UpdateActiveCamera();
            ApplyCameraPitch();
            ApplyHeadbobOffset();
        }
    }

    private void HandleMouseLook()
    {
        if (blockMouseLookWhileUICursorUnlocked && uiCursorUnlocked)
            return;

        float mouseX = Input.GetAxis("Mouse X") * 10f * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * 10f * mouseSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up * mouseX);

        pitch -= mouseY;

        if (isFirstPerson)
            pitch = Mathf.Clamp(pitch, fpMinPitch, fpMaxPitch);
        else
            pitch = Mathf.Clamp(pitch, thirdPersonMinPitch, thirdPersonMaxPitch);

        ApplyCameraPitch();
    }

    private void ApplyCameraPitch()
    {
        Quaternion targetRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (firstPersonCamera != null)
            firstPersonCamera.transform.localRotation = targetRotation;

        if (thirdPersonCamera != null)
            thirdPersonCamera.transform.localRotation = targetRotation;
    }

    private void HandleMovementAndJump()
    {
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");

        bool wantsToSprint = Input.GetKey(sprintKey) && inputZ > 0.1f && !isCrouching;
        isSprinting = wantsToSprint;

        float targetSpeed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : walkSpeed);

        Vector3 move = transform.right * inputX + transform.forward * inputZ;
        move = Vector3.ClampMagnitude(move, 1f);

        velocity.x = move.x * targetSpeed;
        velocity.z = move.z * targetSpeed;

        bool onGroundOrCoyote = isGrounded || (coyoteTimeEnabled && coyoteTimer > 0f);

        if (Input.GetKeyDown(jumpKey) && onGroundOrCoyote && !isCrouching)
        {
            velocity.y = jumpSpeed;
            isGrounded = false;
            coyoteTimer = 0f;
        }

        velocity.y -= gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        Vector3 horizontalVelocity = new Vector3(controller.velocity.x, 0f, controller.velocity.z);
        UpdateHeadbob(horizontalVelocity);
    }

    private void HandleCrouch()
    {
        bool crouchPressed = Input.GetKey(crouchKey);
        float targetHeight = crouchPressed ? crouchHeight : standHeight;

        if (!crouchPressed && isCrouching)
        {
            float checkDistance = standHeight - controller.height + 0.1f;
            Vector3 top = transform.position + controller.center + Vector3.up * (controller.height * 0.5f);

            if (Physics.SphereCast(
                top,
                controller.radius * 0.95f,
                Vector3.up,
                out RaycastHit hit,
                checkDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
            {
                targetHeight = crouchHeight;
            }
        }

        float newHeight = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);
        controller.height = newHeight;
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

        isCrouching = controller.height < (standHeight - 0.05f);
    }

    private void HandleFov()
    {
        float targetFov = isSprinting ? sprintFov : normalFov;
        currentFov = Mathf.Lerp(currentFov, targetFov, Time.deltaTime * fovLerpSpeed);

        if (firstPersonCamera != null)
            firstPersonCamera.fieldOfView = currentFov;

        if (thirdPersonCamera != null)
            thirdPersonCamera.fieldOfView = currentFov;
    }

    private void UpdateHeadbob(Vector3 horizontalVelocity)
    {
        if (!enableHeadbob || !isFirstPerson)
        {
            headbobTimer = 0f;
            currentHeadbobOffset = 0f;
            return;
        }

        bool isMovingEnough = horizontalVelocity.magnitude > headbobMoveThreshold && isGrounded && !isCrouching;

        if (isMovingEnough)
        {
            float bobSpeed = walkingBobbingSpeed * (isSprinting ? sprintBobMultiplier : 1f);
            headbobTimer += Time.deltaTime * bobSpeed;
            float targetOffset = Mathf.Sin(headbobTimer) * bobbingAmount;
            currentHeadbobOffset = Mathf.Lerp(currentHeadbobOffset, targetOffset, Time.deltaTime * walkingBobbingSpeed);
        }
        else
        {
            headbobTimer = 0f;
            currentHeadbobOffset = Mathf.Lerp(currentHeadbobOffset, 0f, Time.deltaTime * walkingBobbingSpeed);
        }
    }

    private void ApplyHeadbobOffset()
    {
        if (firstPersonCamera == null)
            return;

        Vector3 targetLocalPos = firstPersonCameraBaseLocalPos;

        if (isFirstPerson)
            targetLocalPos.y += currentHeadbobOffset;

        firstPersonCamera.transform.localPosition = targetLocalPos;
    }

    private void UpdateActiveCamera()
    {
        if (firstPersonCamera != null)
            firstPersonCamera.gameObject.SetActive(isFirstPerson);

        if (thirdPersonCamera != null)
            thirdPersonCamera.gameObject.SetActive(!isFirstPerson);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            return;

        ApplyCursorState();
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
