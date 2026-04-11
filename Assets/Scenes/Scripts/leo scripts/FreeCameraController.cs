using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 15f;
    public float fastMultiplier = 2.5f;

    [Header("Look")]
    public float lookSpeed = 3f;
    public float pitchMin = -80f;
    public float pitchMax = 80f;

    [Header("Zoom")]
    public float zoomSpeed = 100f;

    [Header("Keys")]
    public KeyCode rotateMouseButton = KeyCode.Mouse1; // clic droit
    public KeyCode ascendKey = KeyCode.Space;          // monter
    public KeyCode descendKey = KeyCode.LeftShift;     // descendre (LeftShift ou RightShift)
    public KeyCode fastKey = KeyCode.LeftControl;      // accélérer (Ctrl)

    float yaw;
    float pitch;

    void Start()
    {
        Vector3 rot = transform.eulerAngles;
        yaw = rot.y;
        pitch = rot.x;
    }

    void Update()
    {
        HandleLook();
        HandleMove();
    }

    void LateUpdate()
    {
        // Après ControlPointHandleUI (rotation mur à la molette) pour pouvoir ignorer le zoom cette frame.
        HandleZoom();
    }

    void HandleLook()
    {
        // Rotation avec clic droit
        if (Input.GetKey(rotateMouseButton))
        {
            yaw += Input.GetAxis("Mouse X") * lookSpeed;
            pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    void HandleMove()
    {
        // Déplacement WASD / ZQSD (géré par les axes Unity)
        Vector3 move = new Vector3(
            Input.GetAxis("Horizontal"),
            0f,
            Input.GetAxis("Vertical")
        );

        // Monter / descendre (Space / Shift)
        if (Input.GetKey(ascendKey))
            move.y += 1f;

        if (Input.GetKey(descendKey) || Input.GetKey(KeyCode.RightShift))
            move.y -= 1f;

        // Vitesse boost (Ctrl)
        float speed = moveSpeed;
        if (Input.GetKey(fastKey))
            speed *= fastMultiplier;

        transform.Translate(move * speed * Time.deltaTime, Space.Self);
    }

    void HandleZoom()
    {
        if (ControlPointHandleUI.ConsumeWallScrollBlockForCamera())
            return;

        // Zoom molette
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            transform.Translate(Vector3.forward * scroll * zoomSpeed * Time.deltaTime, Space.Self);
        }
    }
}
