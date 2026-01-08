using UnityEngine;

/// <summary>
/// Ermöglicht das freie "Herumfliegen" der Kamera, ähnlich wie im Unity Scene View.
/// Hängt an der Kamera und wird über WASD und Maus gesteuert.
/// </summary>
public class FlyCamera : MonoBehaviour
{
    [Header("Einstellungen")]
    [Tooltip("Fluggeschwindigkeit in Einheiten pro Sekunde.")]
    [SerializeField] private float movementSpeed = 10f;

    [Tooltip("Geschwindigkeitsmultiplikator beim Halten von Shift.")]
    [SerializeField] private float fastSpeedMultiplier = 3f;

    [Tooltip("Empfindlichkeit der Maus für die Blickrichtung.")]
    [SerializeField] private float lookSensitivity = 1f;

    private float rotationX = 0f;
    private float rotationY = 0f;

    private void Start()
    {
        // Mauszeiger verbergen und im Fenster fixieren
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Startwinkel der Kamera abrufen
        Vector3 rot = transform.localRotation.eulerAngles;
        rotationX = rot.y;
        rotationY = -rot.x;
        // Unity verwendet möglicherweise eine invertierte X-Rotation, daher -rot.x
    }

    private void Update()
    {
        // --- 1. Blickrichtung steuern (Maus) ---
        HandleRotation();

        // --- 2. Bewegung steuern (WASD/Pfeiltasten) ---
        HandleMovement();

        // Optional: Maussteuerung bei Bedarf wieder freigeben (z.B. mit ESC)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void HandleRotation()
    {
        // Delta der Mausbewegung abrufen
        rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
        rotationY += Input.GetAxis("Mouse Y") * lookSensitivity;

        // Die vertikale Rotation (Y-Achse, Kopfnicken) begrenzen, 
        // um Überschlagen zu verhindern (zwischen -90 und 90 Grad)
        rotationY = Mathf.Clamp(rotationY, -90f, 90f);

        // Die Kamera-Rotation anwenden
        transform.localRotation = Quaternion.AngleAxis(rotationX, Vector3.up);
        transform.localRotation *= Quaternion.AngleAxis(rotationY, Vector3.left);
    }

    private void HandleMovement()
    {
        // Aktuelle Geschwindigkeit bestimmen (schneller mit Shift)
        float currentSpeed = movementSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            currentSpeed *= fastSpeedMultiplier;
        }

        // Geschwindigkeit multipliziert mit der Zeit seit dem letzten Frame
        float actualSpeed = currentSpeed * Time.deltaTime;

        // Horizontale und vertikale Eingaben abrufen
        float inputX = Input.GetAxis("Horizontal"); // A/D oder Pfeile
        float inputZ = Input.GetAxis("Vertical");   // W/S oder Pfeile

        // Bewegung anwenden
        // Die Kamera bewegt sich relativ zu ihrer eigenen lokalen Ausrichtung (transform.forward, transform.right)
        transform.position += transform.forward * inputZ * actualSpeed;
        transform.position += transform.right * inputX * actualSpeed;

        // Optional: Auf/Ab-Bewegung (z.B. Q/E oder Leertaste/Strg)
        if (Input.GetKey(KeyCode.E))
        {
            transform.position += Vector3.up * actualSpeed;
        }
        if (Input.GetKey(KeyCode.Q))
        {
            transform.position -= Vector3.up * actualSpeed;
        }
    }
}