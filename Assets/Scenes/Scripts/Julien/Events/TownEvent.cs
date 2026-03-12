
using UnityEngine;

public enum TownEventType
{
    Fire,
    TaxCollection,
    Storm,
    Raid,
    Sickness
}

public class TownEvent
{
    public TownEventType type;
    public Vector3 position;
    public float radius;
    public int remainingTicks;
    public float intensity;

    public TownEvent(TownEventType t,Vector3 p,float r,int d,float i)
    {
        type=t; position=p; radius=r; remainingTicks=d; intensity=i;
    }
}
