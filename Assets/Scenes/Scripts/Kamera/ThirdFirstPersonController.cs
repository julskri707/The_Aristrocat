using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstThirdPersonController : MonoBehaviour
{
    [Header("General")]
    public Camera playerCamera;
    public KeyCode toggleViewKey = KeyCode.V;
    public bool startInFirstPerson = true;
    [Range(0f, 100f)] public float mouseSensitivity = 50f;
    [Range(0f, 200f)] public float snappiness = 100f;

    [Header("Movement")]
    public float walkSpeed = 3f;
    public float sprintSpeed = 5f;
    public float crouchSpeed = 1.5f;
    public float jumpSpeed = 3f;
    public float gravity = 9.81f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
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

    [Header("First Person")]
    public Transform firstPersonPivot;
    public float fpMinPitch = -90f;
    public float fpMaxPitch = 90f;

    [Header("Headbob (First Person)")]
    public bool enableHeadbob = true;
    public float walkingBobbingSpeed = 10f;
    public float bobbingAmount = 0.05f;
    public float sprintBobMultiplier = 1.5f;
    public float headbobMoveThreshold = 0.1f;

    [Header("Third Person")]
    public Vector3 thirdPersonPivotOffset = new Vector3(0f, 1.6f, 0f);
    public float thirdPersonDistance = 4f;
    public float minThirdPersonDistance = 2f;
    public float maxThirdPersonDistance = 6f;
    public float thirdPersonScrollSpeed = 2f;
    public float thirdPersonMinPitch = -30f;
    public float thirdPersonMaxPitch = 75f;
    public float cameraFollowSmoothTime = 0.05f;
    public LayerMask cameraCollisionMask;

    private CharacterController controller;
    private Transform camTransform;

    private bool isFirstPerson;
    private bool isGrounded;
    private bool isCrouching;
    private bool isSprinting;

    private float yaw;
    private float pitch;
    private float xVelocity;
    private float yVelocity;
    private float coyoteTimer;

    private Vector3 velocity;
    private Vector3 cameraSmoothVelocity;
    private float currentFov;

    // Headbob intern
    private float headbobTimer;
    private float currentHeadbobOffset;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (playerCamera == null)
        {
            if (Camera.main != null)
                playerCamera = Camera.main;
        }

        if (playerCamera != null)
        {
            camTransform = playerCamera.transform;
            currentFov = normalFov;
            playerCamera.fieldOfView = normalFov;
        }

        if (controller != null)
        {
            controller.height = standHeight;
            controller.center = new Vector3(0f, standHeight * 0.5f, 0f);
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        isFirstPerson = startInFirstPerson;

        yaw = transform.rotation.eulerAngles.y;
        pitch = camTransform != null ? camTransform.localRotation.eulerAngles.x : 0f;

        xVelocity = yaw;
        yVelocity = pitch;
    }

    private void Update()
    {
        if (camTransform == null || controller == null)
            return;

        HandleGroundCheck();
        HandleViewToggle();
        HandleMouseLook();
        HandleMovementAndJump();
        HandleCrouch();
        HandleFov();
        HandleCameraPosition();
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

        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
            if (coyoteTimeEnabled)
                coyoteTimer = coyoteTimeDuration;
        }
        else if (coyoteTimeEnabled)
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    private void HandleViewToggle()
    {
        if (Input.GetKeyDown(toggleViewKey))
        {
            isFirstPerson = !isFirstPerson;
            headbobTimer = 0f;
            currentHeadbobOffset = 0f;
        }
    }

    private void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * 10f * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * 10f * mouseSensitivity * Time.deltaTime;

        yaw += mouseX;
        pitch -= mouseY;

        if (isFirstPerson)
        {
            pitch = Mathf.Clamp(pitch, fpMinPitch, fpMaxPitch);
        }
        else
        {
            pitch = Mathf.Clamp(pitch, thirdPersonMinPitch, thirdPersonMaxPitch);
        }

        xVelocity = Mathf.Lerp(xVelocity, yaw, snappiness * Time.deltaTime);
        yVelocity = Mathf.Lerp(yVelocity, pitch, snappiness * Time.deltaTime);

        transform.rotation = Quaternion.Euler(0f, xVelocity, 0f);
    }

    private void HandleMovementAndJump()
    {
        float inputX = Input.GetAxis("Horizontal");
        float inputZ = Input.GetAxis("Vertical");

        bool wantsToSprint = Input.GetKey(KeyCode.LeftShift) && inputZ > 0.1f && !isCrouching && isGrounded;
        isSprinting = wantsToSprint;

        float targetSpeed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed : walkSpeed);

        Vector3 move = transform.right * inputX + transform.forward * inputZ;
        Vector3 horizontal = move.normalized * targetSpeed;

        velocity.x = horizontal.x;
        velocity.z = horizontal.z;

        bool onGroundOrCoyote = isGrounded || (coyoteTimeEnabled && coyoteTimer > 0f);

        if (onGroundOrCoyote)
        {
            if (Input.GetButtonDown("Jump") && !isCrouching)
            {
                velocity.y = jumpSpeed;
                coyoteTimer = 0f;
            }
            else if (velocity.y < 0f)
            {
                velocity.y = -2f;
            }
        }
        else
        {
            velocity.y -= gravity * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);

        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        UpdateHeadbob(horizontalVelocity);
    }

    private void HandleCrouch()
    {
        bool crouchPressed = Input.GetKey(crouchKey);

        float targetHeight = crouchPressed ? crouchHeight : standHeight;

        // Decken-Check beim Aufstehen
        if (!crouchPressed && isCrouching)
        {
            float checkDistance = standHeight - controller.height + 0.1f;
            Vector3 top = transform.position + controller.center + Vector3.up * (controller.height * 0.5f);
            if (Physics.SphereCast(top, controller.radius * 0.95f, Vector3.up, out RaycastHit hit, checkDistance, groundMask))
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
        playerCamera.fieldOfView = currentFov;
    }

    private void HandleCameraPosition()
    {
        if (isFirstPerson)
        {
            HandleFirstPersonCamera();
        }
        else
        {
            HandleThirdPersonCamera();
        }
    }

    private void HandleFirstPersonCamera()
    {
        Vector3 basePos;
        if (firstPersonPivot != null)
        {
            basePos = firstPersonPivot.position;
        }
        else
        {
            basePos = transform.position + Vector3.up * controller.height * 0.8f;
        }

        Vector3 targetPos = basePos + Vector3.up * currentHeadbobOffset;

        camTransform.position = targetPos;
        camTransform.localRotation = Quaternion.Euler(yVelocity, 0f, 0f);
        // Yaw kommt von transform.rotation (xVelocity)
    }

    private void HandleThirdPersonCamera()
    {
        Vector3 pivot = transform.position + thirdPersonPivotOffset;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        thirdPersonDistance -= scroll * thirdPersonScrollSpeed;
        thirdPersonDistance = Mathf.Clamp(thirdPersonDistance, minThirdPersonDistance, maxThirdPersonDistance);

        Quaternion rotation = Quaternion.Euler(yVelocity, xVelocity, 0f);
        Vector3 desiredPos = pivot - (rotation * Vector3.forward * thirdPersonDistance);

        if (cameraCollisionMask != 0)
        {
            Vector3 dir = (desiredPos - pivot).normalized;
            float dist = thirdPersonDistance;
            if (Physics.SphereCast(
                pivot,
                0.2f,
                dir,
                out RaycastHit hit,
                dist,
                cameraCollisionMask,
                QueryTriggerInteraction.Ignore))
            {
                desiredPos = pivot + dir * (hit.distance - 0.1f);
            }
        }

        camTransform.position = Vector3.SmoothDamp(
            camTransform.position,
            desiredPos,
            ref cameraSmoothVelocity,
            cameraFollowSmoothTime
        );
        camTransform.rotation = rotation;
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

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Gizmos.color = Color.cyan;
        Vector3 pivot = transform.position + thirdPersonPivotOffset;
        Gizmos.DrawWireSphere(pivot, 0.1f);
    }
}
