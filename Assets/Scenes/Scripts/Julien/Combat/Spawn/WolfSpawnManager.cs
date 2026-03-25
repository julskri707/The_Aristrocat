using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class WolfSpawnManager : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject wolfPrefab;

    [Header("Spawn Sources")]
    [SerializeField] private WolfSpawnArea[] spawnAreas = new WolfSpawnArea[0];
    [SerializeField] private WolfSpawnPoint[] spawnPoints = new WolfSpawnPoint[0];

    [Header("Player")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private DamageableHealth playerHealth;
    [SerializeField] private bool autoFindPlayerByTag = true;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float playerSearchInterval = 2f;

    [Header("Limits")]
    [SerializeField] private int maxActiveWolves = 5;
    [SerializeField] private int initialSpawnCount = 0;
    [SerializeField] private float spawnInterval = 5f;
    [SerializeField] private float minDistanceToPlayer = 18f;

    [Header("Respawn")]
    [SerializeField] private bool enableRespawn = true;
    [SerializeField] private bool useRespawnDelayAfterDeath = false;
    [SerializeField] private float respawnDelayAfterDeath = 8f;

    [Header("Spawn Validation")]
    [SerializeField] private int maxSpawnAttemptsPerCycle = 12;
    [SerializeField] private bool randomizeYawOnAreaSpawn = true;
    [SerializeField] private bool assignPlayerDirectlyToWolfBrain = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private readonly List<DamageableHealth> activeWolfHealths = new List<DamageableHealth>();

    private float nextSpawnTime;
    private float nextPlayerSearchTime;
    private bool warnedMissingPlayer;
    private bool warnedMissingSpawnSources;
    private bool warnedMissingPrefab;

    public int ActiveWolfCount
    {
        get
        {
            CleanupActiveList();
            return activeWolfHealths.Count;
        }
    }

    private void OnValidate()
    {
        maxActiveWolves = Mathf.Max(0, maxActiveWolves);
        initialSpawnCount = Mathf.Max(0, initialSpawnCount);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        minDistanceToPlayer = Mathf.Max(0f, minDistanceToPlayer);
        respawnDelayAfterDeath = Mathf.Max(0f, respawnDelayAfterDeath);
        maxSpawnAttemptsPerCycle = Mathf.Max(1, maxSpawnAttemptsPerCycle);
        playerSearchInterval = Mathf.Max(0.25f, playerSearchInterval);
    }

    private void Start()
    {
        ResolvePlayerReference(forceNow: true);
        CleanupActiveList();

        for (int i = 0; i < initialSpawnCount && ActiveWolfCount < maxActiveWolves; i++)
        {
            TrySpawnOneWolf(ignoreTiming: true);
        }

        nextSpawnTime = Time.time + spawnInterval;
    }

    private void Update()
    {
        CleanupActiveList();
        ResolvePlayerReference(forceNow: false);

        if (!enableRespawn && ActiveWolfCount > 0)
            return;

        if (ActiveWolfCount >= maxActiveWolves)
            return;

        if (Time.time < nextSpawnTime)
            return;

        bool spawned = TrySpawnOneWolf(ignoreTiming: false);
        nextSpawnTime = Time.time + spawnInterval;

        if (!spawned && debugLogs)
        {
            Debug.Log($"[{nameof(WolfSpawnManager)}] No valid wolf spawn position found this cycle.", this);
        }
    }

    private bool TrySpawnOneWolf(bool ignoreTiming)
    {
        if (!ValidateSpawnBasics())
            return false;

        CleanupActiveList();

        if (ActiveWolfCount >= maxActiveWolves)
            return false;

        if (!ignoreTiming && !enableRespawn && ActiveWolfCount > 0)
            return false;

        int totalSources = GetTotalSpawnSourceCount();
        if (totalSources <= 0)
        {
            WarnMissingSpawnSourcesOnce();
            return false;
        }

        for (int attempt = 0; attempt < maxSpawnAttemptsPerCycle; attempt++)
        {
            if (!TryGetCandidateSpawn(out Vector3 spawnPosition, out Quaternion spawnRotation))
                continue;

            if (!IsFarEnoughFromPlayer(spawnPosition))
                continue;

            GameObject spawnedObject = Instantiate(wolfPrefab, spawnPosition, spawnRotation);
            if (spawnedObject == null)
            {
                Debug.LogError($"[{nameof(WolfSpawnManager)}] Failed to instantiate wolf prefab.", this);
                return false;
            }

            if (!TrySetupSpawnedWolf(spawnedObject, spawnPosition))
            {
                Destroy(spawnedObject);
                return false;
            }

            if (debugLogs)
            {
                Debug.Log($"[{nameof(WolfSpawnManager)}] Spawned wolf '{spawnedObject.name}' at {spawnPosition}.", spawnedObject);
            }

            return true;
        }

        return false;
    }

    private bool TrySetupSpawnedWolf(GameObject spawnedObject, Vector3 spawnPosition)
    {
        if (spawnedObject == null)
            return false;

        DamageableHealth wolfHealth = spawnedObject.GetComponent<DamageableHealth>();
        if (wolfHealth == null)
        {
            Debug.LogError($"[{nameof(WolfSpawnManager)}] Spawned wolf '{spawnedObject.name}' has no {nameof(DamageableHealth)} on its root.", spawnedObject);
            return false;
        }

        WolfBrain wolfBrain = spawnedObject.GetComponent<WolfBrain>();
        if (wolfBrain == null)
        {
            Debug.LogError($"[{nameof(WolfSpawnManager)}] Spawned wolf '{spawnedObject.name}' has no {nameof(WolfBrain)} on its root.", spawnedObject);
            return false;
        }

        NavMeshAgent agent = spawnedObject.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
        {
            bool warped = agent.Warp(spawnPosition);
            if (!warped && debugLogs)
            {
                Debug.LogWarning($"[{nameof(WolfSpawnManager)}] NavMeshAgent.Warp failed for '{spawnedObject.name}'.", spawnedObject);
            }
        }

        if (assignPlayerDirectlyToWolfBrain)
        {
            wolfBrain.SetTarget(playerRoot, playerHealth);
        }

        wolfBrain.SetHomePosition(spawnPosition);

        RegisterWolf(wolfHealth);
        return true;
    }

    private void RegisterWolf(DamageableHealth wolfHealth)
    {
        if (wolfHealth == null)
            return;

        CleanupActiveList();

        if (activeWolfHealths.Contains(wolfHealth))
            return;

        activeWolfHealths.Add(wolfHealth);
        wolfHealth.Died += OnRegisteredWolfDied;
    }

    private void DeregisterWolf(DamageableHealth wolfHealth)
    {
        if (wolfHealth != null)
        {
            wolfHealth.Died -= OnRegisteredWolfDied;
        }

        activeWolfHealths.Remove(wolfHealth);
    }

    private void OnRegisteredWolfDied(DamageableHealth deadWolf, DamageInfo killingDamage)
    {
        DeregisterWolf(deadWolf);

        if (useRespawnDelayAfterDeath)
        {
            nextSpawnTime = Mathf.Max(nextSpawnTime, Time.time + respawnDelayAfterDeath);
        }

        if (debugLogs && deadWolf != null)
        {
            Debug.Log($"[{nameof(WolfSpawnManager)}] Deregistered dead wolf '{deadWolf.name}'.", deadWolf);
        }
    }

    private void CleanupActiveList()
    {
        for (int i = activeWolfHealths.Count - 1; i >= 0; i--)
        {
            DamageableHealth entry = activeWolfHealths[i];

            if (entry == null)
            {
                activeWolfHealths.RemoveAt(i);
                continue;
            }

            if (entry.IsDead)
            {
                entry.Died -= OnRegisteredWolfDied;
                activeWolfHealths.RemoveAt(i);
            }
        }
    }

    private bool TryGetCandidateSpawn(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        spawnPosition = Vector3.zero;
        spawnRotation = Quaternion.identity;

        int areaCount = spawnAreas != null ? spawnAreas.Length : 0;
        int pointCount = spawnPoints != null ? spawnPoints.Length : 0;
        int total = areaCount + pointCount;

        if (total <= 0)
            return false;

        int randomIndex = Random.Range(0, total);

        if (randomIndex < pointCount)
        {
            WolfSpawnPoint point = spawnPoints[randomIndex];
            if (point == null)
                return false;

            if (!point.TryGetSpawnPosition(out spawnPosition))
                return false;

            spawnRotation = point.GetSpawnRotation();
            return true;
        }
        else
        {
            int areaIndex = randomIndex - pointCount;
            WolfSpawnArea area = spawnAreas[areaIndex];
            if (area == null)
                return false;

            if (!area.TryGetSpawnPosition(out spawnPosition))
                return false;

            spawnRotation = randomizeYawOnAreaSpawn
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : (wolfPrefab != null ? wolfPrefab.transform.rotation : Quaternion.identity);

            return true;
        }
    }

    private bool IsFarEnoughFromPlayer(Vector3 spawnPosition)
    {
        if (playerRoot == null)
            return true;

        float distance = Vector3.Distance(spawnPosition, playerRoot.position);
        return distance >= minDistanceToPlayer;
    }

    private bool ValidateSpawnBasics()
    {
        if (wolfPrefab == null)
        {
            WarnMissingPrefabOnce();
            return false;
        }

        if (GetTotalSpawnSourceCount() <= 0)
        {
            WarnMissingSpawnSourcesOnce();
            return false;
        }

        return true;
    }

    private int GetTotalSpawnSourceCount()
    {
        int areaCount = spawnAreas != null ? spawnAreas.Length : 0;
        int pointCount = spawnPoints != null ? spawnPoints.Length : 0;
        return areaCount + pointCount;
    }

    private void ResolvePlayerReference(bool forceNow)
    {
        if (playerRoot != null && playerHealth == null)
        {
            playerHealth = playerRoot.GetComponent<DamageableHealth>();
            if (playerHealth == null)
            {
                playerHealth = playerRoot.GetComponentInParent<DamageableHealth>();
            }

            if (playerHealth == null)
            {
                playerHealth = playerRoot.GetComponentInChildren<DamageableHealth>(true);
            }
        }

        if (playerRoot != null && playerHealth != null)
        {
            warnedMissingPlayer = false;
            return;
        }

        if (!autoFindPlayerByTag)
        {
            WarnMissingPlayerOnce();
            return;
        }

        if (!forceNow && Time.time < nextPlayerSearchTime)
            return;

        nextPlayerSearchTime = Time.time + playerSearchInterval;

        try
        {
            GameObject found = GameObject.FindGameObjectWithTag(playerTag);
            if (found != null)
            {
                playerRoot = found.transform;
                playerHealth = found.GetComponent<DamageableHealth>();

                if (playerHealth == null)
                {
                    playerHealth = found.GetComponentInParent<DamageableHealth>();
                }

                if (playerHealth == null)
                {
                    playerHealth = found.GetComponentInChildren<DamageableHealth>(true);
                }

                warnedMissingPlayer = false;

                if (debugLogs && playerHealth != null)
                {
                    Debug.Log($"[{nameof(WolfSpawnManager)}] Found player '{playerRoot.name}'.", this);
                }
            }
            else
            {
                WarnMissingPlayerOnce();
            }
        }
        catch (UnityException)
        {
            WarnMissingPlayerOnce();
        }
    }

    private void WarnMissingPlayerOnce()
    {
        if (warnedMissingPlayer)
            return;

        warnedMissingPlayer = true;
        Debug.LogWarning(
            $"[{nameof(WolfSpawnManager)}] No valid player reference found. Assign playerRoot + playerHealth in the Inspector or make sure a GameObject with tag '{playerTag}' exists and has {nameof(DamageableHealth)}.",
            this
        );
    }

    private void WarnMissingSpawnSourcesOnce()
    {
        if (warnedMissingSpawnSources)
            return;

        warnedMissingSpawnSources = true;
        Debug.LogWarning(
            $"[{nameof(WolfSpawnManager)}] No spawn sources assigned. Add at least one {nameof(WolfSpawnArea)} or {nameof(WolfSpawnPoint)}.",
            this
        );
    }

    private void WarnMissingPrefabOnce()
    {
        if (warnedMissingPrefab)
            return;

        warnedMissingPrefab = true;
        Debug.LogWarning($"[{nameof(WolfSpawnManager)}] No wolfPrefab assigned.", this);
    }

    private void OnDisable()
    {
        for (int i = 0; i < activeWolfHealths.Count; i++)
        {
            if (activeWolfHealths[i] != null)
            {
                activeWolfHealths[i].Died -= OnRegisteredWolfDied;
            }
        }
    }
}
