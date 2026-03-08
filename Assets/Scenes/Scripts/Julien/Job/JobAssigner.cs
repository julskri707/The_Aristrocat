using System.Collections.Generic;
using UnityEngine;

public class JobAssigner : MonoBehaviour
{
    public static JobAssigner Instance { get; private set; }

    private readonly List<JobSite> jobSites = new List<JobSite>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[JobAssigner] Duplicate instance found. Destroying this one.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public void RegisterJobSite(JobSite site)
    {
        if (site != null && !jobSites.Contains(site))
            jobSites.Add(site);
    }

    public void UnregisterJobSite(JobSite site)
    {
        if (site != null)
            jobSites.Remove(site);
    }

    public JobSite FindClosestAvailableJob(Vector3 position, WorkerAssignment worker = null)
    {
        JobSite best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < jobSites.Count; i++)
        {
            JobSite site = jobSites[i];
            if (site == null || !site.HasFreeSlot(worker))
                continue;

            float dist = (site.transform.position - position).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = site;
            }
        }

        return best;
    }
}