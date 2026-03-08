using UnityEngine;

public class TownCenterSite : MonoBehaviour
{
    public static TownCenterSite Instance;

    public Transform safePoint;

    public Transform SafePoint => safePoint != null ? safePoint : transform;

    private void Awake()
    {
        Instance = this;
    }
}