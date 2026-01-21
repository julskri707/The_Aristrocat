using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    public float moveSpeed = 15f;
    public float lookSpeed = 3f;
    public float zoomSpeed = 100f;

    float rotationX;
    float rotationY;

    void Start()
    {
        Vector3 rot = transform.eulerAngles;
        rotationX = rot.y;
        rotationY = rot.x;
    }

    void Update()
    {
        // Rotation avec clic droit
        if (Input.GetMouseButton(1))
        {
            rotationX += Input.GetAxis("Mouse X") * lookSpeed;
            rotationY -= Input.GetAxis("Mouse Y") * lookSpeed;
            rotationY = Mathf.Clamp(rotationY, -80f, 80f);

            transform.rotation = Quaternion.Euler(rotationY, rotationX, 0);
        }

        // Déplacement
        Vector3 move = new Vector3(
            Input.GetAxis("Horizontal"),
            0,
            Input.GetAxis("Vertical")
        );

        if (Input.GetKey(KeyCode.E))
            move.y += 1;
        if (Input.GetKey(KeyCode.Q))
            move.y -= 1;

        transform.Translate(move * moveSpeed * Time.deltaTime, Space.Self);

        // Zoom molette
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        transform.Translate(Vector3.forward * scroll * zoomSpeed * Time.deltaTime, Space.Self);
    }
}
