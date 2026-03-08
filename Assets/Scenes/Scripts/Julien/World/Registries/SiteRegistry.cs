using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SiteRegistry : MonoBehaviour
{
    public static SiteRegistry Instance { get; private set; }

    private readonly List<HomeSite> homeSites = new List<HomeSite>();
    private readonly List<FoodSite> foodSites = new List<FoodSite>();
    private readonly List<LeisureSite> leisureSites = new List<LeisureSite>();
    private readonly List<JobSite> jobSites = new List<JobSite>();

    public IReadOnlyList<HomeSite> HomeSites => homeSites;
    public IReadOnlyList<FoodSite> FoodSites => foodSites;
    public IReadOnlyList<LeisureSite> LeisureSites => leisureSites;
    public IReadOnlyList<JobSite> JobSites => jobSites;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SiteRegistry] Duplicate instance found. Destroying this one.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public void RegisterHomeSite(HomeSite site)
    {
        if (site != null && !homeSites.Contains(site))
            homeSites.Add(site);
    }

    public void UnregisterHomeSite(HomeSite site)
    {
        if (site != null)
            homeSites.Remove(site);
    }

    public void RegisterFoodSite(FoodSite site)
    {
        if (site != null && !foodSites.Contains(site))
            foodSites.Add(site);
    }

    public void UnregisterFoodSite(FoodSite site)
    {
        if (site != null)
            foodSites.Remove(site);
    }

    public void RegisterLeisureSite(LeisureSite site)
    {
        if (site != null && !leisureSites.Contains(site))
            leisureSites.Add(site);
    }

    public void UnregisterLeisureSite(LeisureSite site)
    {
        if (site != null)
            leisureSites.Remove(site);
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

    public bool TryClaimNearestHomeSite(Vector3 fromPosition, GameObject npc, out HomeSite bestSite)
    {
        bestSite = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < homeSites.Count; i++)
        {
            HomeSite site = homeSites[i];
            if (site == null || !site.CanReserve(npc))
                continue;

            float dist = (site.GetUsePosition() - fromPosition).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestSite = site;
            }
        }

        if (bestSite != null)
        {
            bestSite.TryReserve(npc);
            return true;
        }

        return false;
    }

    public bool TryClaimNearestFoodSite(Vector3 fromPosition, GameObject npc, out FoodSite bestSite)
    {
        bestSite = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < foodSites.Count; i++)
        {
            FoodSite site = foodSites[i];
            if (site == null || !site.CanReserve(npc))
                continue;

            float dist = (site.GetUsePosition() - fromPosition).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestSite = site;
            }
        }

        if (bestSite != null)
        {
            bestSite.TryReserve(npc);
            return true;
        }

        return false;
    }

    public bool TryClaimNearestLeisureSite(Vector3 fromPosition, GameObject npc, out LeisureSite bestSite)
    {
        bestSite = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < leisureSites.Count; i++)
        {
            LeisureSite site = leisureSites[i];
            if (site == null || !site.CanReserve(npc))
                continue;

            float dist = (site.GetUsePosition() - fromPosition).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestSite = site;
            }
        }

        if (bestSite != null)
        {
            bestSite.TryReserve(npc);
            return true;
        }

        return false;
    }

    public JobSite FindJobSiteByResourceBehaviour(ResourceTickBehaviour behaviour)
    {
        if (behaviour == null)
            return null;

        for (int i = 0; i < jobSites.Count; i++)
        {
            JobSite site = jobSites[i];
            if (site == null)
                continue;

            if (site.ResourceBehaviour == behaviour)
                return site;
        }

        return null;
    }

    public void ReleaseReservations(GameObject npc)
    {
        if (npc == null)
            return;

        for (int i = 0; i < homeSites.Count; i++)
            if (homeSites[i] != null) homeSites[i].Release(npc);

        for (int i = 0; i < foodSites.Count; i++)
            if (foodSites[i] != null) foodSites[i].Release(npc);

        for (int i = 0; i < leisureSites.Count; i++)
            if (leisureSites[i] != null) leisureSites[i].Release(npc);
    }
}