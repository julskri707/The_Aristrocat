using UnityEngine;

public class AxeTree : MonoBehaviour
{
    [Header("Tree")]
    [SerializeField, Min(1)] private int hitPoints = 3;
    [SerializeField] private GameObject optionalDropPrefab;
    [SerializeField] private Transform optionalDropSpawnPoint;
    [SerializeField] private GameObject optionalStump;
    [SerializeField] private bool destroyWholeTreeOnDeath = true;

    public void ApplyHit(int damage)
    {
        if (damage <= 0)
            return;

        hitPoints -= damage;

        if (hitPoints <= 0)
            ChopDown();
    }

    private void ChopDown()
    {
        if (optionalDropPrefab != null)
        {
            Vector3 spawnPos = optionalDropSpawnPoint != null ? optionalDropSpawnPoint.position : transform.position;
            Instantiate(optionalDropPrefab, spawnPos, Quaternion.identity);
        }

        if (optionalStump != null)
            optionalStump.SetActive(true);

        if (destroyWholeTreeOnDeath)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }
}
