using UnityEngine;

public class RandomYRotationSetter : MonoBehaviour
{
    [Header("Random Y Rotation")]
    [SerializeField] private bool setOnStart = true;
    [SerializeField] private float minY = 0f;
    [SerializeField] private float maxY = 360f;

    private void Start()
    {
        if (setOnStart)
            ApplyRandomYRotation();
    }

    [ContextMenu("Apply Random Y Rotation")]
    public void ApplyRandomYRotation()
    {
        Vector3 currentEuler = transform.eulerAngles;
        currentEuler.y = Random.Range(minY, maxY);
        transform.eulerAngles = currentEuler;
    }
}