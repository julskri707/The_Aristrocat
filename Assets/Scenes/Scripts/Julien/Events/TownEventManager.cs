
using System.Collections.Generic;
using UnityEngine;

public class TownEventManager : MonoBehaviour
{
    public static TownEventManager Instance;

    public List<TownEvent> events = new List<TownEvent>();
    List<NPCEventContext> contexts = new List<NPCEventContext>();

    void Awake(){ Instance=this; }

    public void RegisterContext(NPCEventContext c)
    {
        if(!contexts.Contains(c)) contexts.Add(c);
    }

    public void StartFire(Vector3 pos,float radius,int duration)
    {
        events.Add(new TownEvent(TownEventType.Fire,pos,radius,duration,1));
        Debug.Log("[EVENT] Fire started");
    }

    public void Tick()
    {
        foreach(var c in contexts)
            c.BeginTick();

        foreach(var e in events)
        {
            if(e.type==TownEventType.Fire)
                foreach(var c in contexts)
                    c.ApplyFire();

            e.remainingTicks--;
        }

        events.RemoveAll(e=>e.remainingTicks<=0);
    }
}
