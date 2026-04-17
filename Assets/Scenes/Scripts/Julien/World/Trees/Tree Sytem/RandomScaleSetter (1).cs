using UnityEngine;

public class RandomScaleSetter : MonoBehaviour
{
    [SerializeField] private float minScale = 2f;
    [SerializeField] private float maxScale = 5f;

    private void Start()
    {
        float randomScale = Random.Range(minScale, maxScale);
        transform.localScale = Vector3.one * randomScale;
    }
}
