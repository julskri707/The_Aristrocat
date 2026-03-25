using System;
using UnityEngine;

public class SimpleDeathBehaviour : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private DamageableHealth health;

    [Header("Disable On Death")]
    [SerializeField] private Behaviour[] behavioursToDisable = Array.Empty<Behaviour>();
    [SerializeField] private GameObject[] gameObjectsToDisable = Array.Empty<GameObject>();
    [SerializeField] private Collider[] collidersToDisable = Array.Empty<Collider>();
    [SerializeField] private bool disableAllChildCollidersIfListEmpty = true;

    [Header("Rigidbody Handling")]
    [SerializeField] private Rigidbody[] rigidbodiesToDisable = Array.Empty<Rigidbody>();
    [SerializeField] private bool makeRigidbodiesKinematic = true;
    [SerializeField] private bool disableRigidbodyCollisions = true;

    [Header("Destroy")]
    [SerializeField] private bool destroyRootAfterDelay = false;
    [SerializeField] private float destroyDelay = 5f;

    private bool handledDeath;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponent<DamageableHealth>();
        }

        if (health == null)
        {
            Debug.LogWarning($"[{nameof(SimpleDeathBehaviour)}] Missing {nameof(DamageableHealth)} on '{name}'.", this);
        }
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += OnDied;
        }
    }

    private void Start()
    {
        if (health != null && health.IsDead && !handledDeath)
        {
            OnDied(health, health.LastDamageInfo);
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= OnDied;
        }
    }

    private void OnDied(DamageableHealth deadHealth, DamageInfo killingDamage)
    {
        if (handledDeath)
            return;

        handledDeath = true;

        DisableBehaviours();
        DisableGameObjects();
        DisableColliders();
        DisableRigidbodies();

        if (destroyRootAfterDelay && Application.isPlaying)
        {
            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
        }
    }

    private void DisableBehaviours()
    {
        if (behavioursToDisable == null)
            return;

        for (int i = 0; i < behavioursToDisable.Length; i++)
        {
            Behaviour behaviour = behavioursToDisable[i];
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
        }
    }

    private void DisableGameObjects()
    {
        if (gameObjectsToDisable == null)
            return;

        for (int i = 0; i < gameObjectsToDisable.Length; i++)
        {
            GameObject go = gameObjectsToDisable[i];
            if (go != null)
            {
                go.SetActive(false);
            }
        }
    }

    private void DisableColliders()
    {
        if (collidersToDisable != null && collidersToDisable.Length > 0)
        {
            for (int i = 0; i < collidersToDisable.Length; i++)
            {
                if (collidersToDisable[i] != null)
                {
                    collidersToDisable[i].enabled = false;
                }
            }

            return;
        }

        if (!disableAllChildCollidersIfListEmpty)
            return;

        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < allColliders.Length; i++)
        {
            if (allColliders[i] != null)
            {
                allColliders[i].enabled = false;
            }
        }
    }

    private void DisableRigidbodies()
    {
        if (rigidbodiesToDisable == null)
            return;

        for (int i = 0; i < rigidbodiesToDisable.Length; i++)
        {
            Rigidbody rb = rigidbodiesToDisable[i];
            if (rb == null)
                continue;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (makeRigidbodiesKinematic)
            {
                rb.isKinematic = true;
            }

            if (disableRigidbodyCollisions)
            {
                rb.detectCollisions = false;
            }
        }
    }
}
