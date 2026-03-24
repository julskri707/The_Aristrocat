using UnityEngine;

[DisallowMultipleComponent]
public class CampfireBench : MonoBehaviour
{
    [Header("Food Site")]
    [SerializeField] private FoodSite explicitFoodSite;
    [SerializeField] private bool autoFindNearestFoodSite = true;
    [SerializeField, Min(0.5f)] private float autoFindRadius = 25f;

    [Header("Seat References")]
    [SerializeField] private Transform seatLeft;
    [SerializeField] private Transform standLeft;
    [SerializeField] private Transform seatRight;
    [SerializeField] private Transform standRight;

    [Header("Runtime")]
    [SerializeField] private bool watchTransformChanges = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool debugWarnings = true;

    private FoodSite ownerFoodSite;
    private Vector3 lastPosition;
    private Quaternion lastRotation;

    public FoodSite OwnerFoodSite => ownerFoodSite;
    public int SeatCount => 2;

    private void Awake()
    {
        CacheTransformState();
    }

    private void OnEnable()
    {
        RefreshRegistration();
        CacheTransformState();
    }

    private void Start()
    {
        RefreshRegistration();
        CacheTransformState();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !watchTransformChanges)
            return;

        bool moved = (transform.position - lastPosition).sqrMagnitude > 0.000001f;
        bool rotated = Quaternion.Angle(transform.rotation, lastRotation) > 0.05f;

        if (!moved && !rotated)
            return;

        FoodSite previousOwner = ownerFoodSite;
        RefreshRegistration();

        if (ownerFoodSite != null)
            ownerFoodSite.NotifyBenchChanged(this);
        else if (previousOwner != null)
            previousOwner.NotifyBenchChanged(this);

        CacheTransformState();
    }

    private void OnDisable()
    {
        UnregisterFromOwner();
    }

    private void OnDestroy()
    {
        UnregisterFromOwner();
    }

    private void OnValidate()
    {
        autoFindRadius = Mathf.Max(0.5f, autoFindRadius);
    }

    public bool HasSeatPair(int seatIndex)
    {
        if (seatIndex == 0)
            return seatLeft != null && standLeft != null;

        if (seatIndex == 1)
            return seatRight != null && standRight != null;

        return false;
    }

    public Transform GetSeatTransform(int seatIndex)
    {
        if (seatIndex == 0) return seatLeft;
        if (seatIndex == 1) return seatRight;
        return null;
    }

    public Transform GetStandTransform(int seatIndex)
    {
        if (seatIndex == 0) return standLeft;
        if (seatIndex == 1) return standRight;
        return null;
    }

    public void RefreshRegistration()
    {
        FoodSite targetSite = ResolveTargetFoodSite();

        if (targetSite == ownerFoodSite)
            return;

        if (ownerFoodSite != null)
            ownerFoodSite.UnregisterBench(this);

        ownerFoodSite = targetSite;

        if (ownerFoodSite != null)
        {
            ownerFoodSite.RegisterBench(this);

            if (debugLogs)
                Debug.Log($"[CampfireBench] '{name}' registered to FoodSite '{ownerFoodSite.name}'.", this);
        }
        else if (debugWarnings)
        {
            Debug.LogWarning($"[CampfireBench] '{name}' could not find a FoodSite.", this);
        }
    }

    private FoodSite ResolveTargetFoodSite()
    {
        if (explicitFoodSite != null && explicitFoodSite.isActiveAndEnabled)
            return explicitFoodSite;

        if (!autoFindNearestFoodSite)
            return null;

        SiteRegistry registry = SiteRegistry.Instance;
        if (registry == null)
            return null;

        FoodSite best = null;
        float bestDist = float.MaxValue;
        float maxDistSqr = autoFindRadius * autoFindRadius;
        Vector3 from = transform.position;

        var sites = registry.FoodSites;
        for (int i = 0; i < sites.Count; i++)
        {
            FoodSite site = sites[i];
            if (site == null || !site.isActiveAndEnabled)
                continue;

            float dist = (site.transform.position - from).sqrMagnitude;
            if (dist > maxDistSqr)
                continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = site;
            }
        }

        return best;
    }

    private void UnregisterFromOwner()
    {
        if (ownerFoodSite != null)
        {
            ownerFoodSite.UnregisterBench(this);
            ownerFoodSite = null;
        }
    }

    private void CacheTransformState()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
    }
}