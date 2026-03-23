using UnityEngine;

[DisallowMultipleComponent]
public class AlignToGround : MonoBehaviour
{
    public enum AlignMode
    {
        ManualOnly,
        StartOnly,
        Continuous
    }

    [Header("Mode")]
    [SerializeField] private AlignMode alignMode = AlignMode.StartOnly;

    [Header("Raycast")]
    [SerializeField] private float rayStartHeight = 5f;
    [SerializeField] private float rayDistance = 20f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private bool ignoreTriggers = true;

    [Header("Position")]
    [SerializeField] private bool alignPosition = true;
    [SerializeField] private float surfaceOffset = 0f;

    [Header("Rotation")]
    [SerializeField] private bool alignRotation = true;
    [SerializeField] private bool keepForwardProjectedOnGround = true;

    [Header("Smoothing")]
    [SerializeField] private bool smoothMovement = false;
    [SerializeField] private float positionSmoothSpeed = 10f;
    [SerializeField] private float rotationSmoothSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRay = true;
    [SerializeField] private Color debugRayColor = Color.green;
    [SerializeField] private Color debugHitColor = Color.yellow;

    private void Start()
    {
        if (alignMode == AlignMode.StartOnly)
            AlignNow();
    }

    private void Update()
    {
        if (alignMode == AlignMode.Continuous)
            AlignNow();
    }

    [ContextMenu("Align Now")]
    public void AlignNow()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * rayStartHeight;
        Vector3 rayDirection = Vector3.down;

        QueryTriggerInteraction triggerMode =
            ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;

        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, rayDistance, groundLayers, triggerMode))
        {
            Vector3 targetPosition = transform.position;
            Quaternion targetRotation = transform.rotation;

            if (alignPosition)
            {
                targetPosition = hit.point + hit.normal * surfaceOffset;
            }

            if (alignRotation)
            {
                if (keepForwardProjectedOnGround)
                {
                    Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, hit.normal);

                    if (projectedForward.sqrMagnitude < 0.0001f)
                    {
                        projectedForward = Vector3.ProjectOnPlane(transform.up, hit.normal);
                    }

                    if (projectedForward.sqrMagnitude < 0.0001f)
                    {
                        projectedForward = Vector3.Cross(transform.right, hit.normal);
                    }

                    projectedForward.Normalize();

                    if (projectedForward.sqrMagnitude > 0.0001f)
                    {
                        targetRotation = Quaternion.LookRotation(projectedForward, hit.normal);
                    }
                    else
                    {
                        targetRotation = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
                    }
                }
                else
                {
                    targetRotation = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
                }
            }

            if (smoothMovement)
            {
                if (alignPosition)
                {
                    transform.position = Vector3.Lerp(
                        transform.position,
                        targetPosition,
                        1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime)
                    );
                }

                if (alignRotation)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime)
                    );
                }
            }
            else
            {
                if (alignPosition)
                    transform.position = targetPosition;

                if (alignRotation)
                    transform.rotation = targetRotation;
            }

            if (drawDebugRay)
            {
                Debug.DrawLine(rayOrigin, hit.point, debugRayColor);
                Debug.DrawRay(hit.point, hit.normal * 0.75f, debugHitColor);
            }
        }
        else if (drawDebugRay)
        {
            Debug.DrawRay(rayOrigin, rayDirection * rayDistance, Color.red);
        }
    }
}