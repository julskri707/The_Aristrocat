
using UnityEngine;

public class NPCBrain : MonoBehaviour
{
    public NPCDecisionBrain decision;
    public NPCMovementController movement;

    void Awake()
    {
        decision = GetComponent<NPCDecisionBrain>();
        movement = GetComponent<NPCMovementController>();
    }

    void Update()
    {
        if(decision!=null && movement!=null)
            movement.SetTarget(decision.CurrentTarget);
    }
}
