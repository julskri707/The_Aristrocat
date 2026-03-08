using UnityEngine;

[DisallowMultipleComponent]
public class NPCEventContext : MonoBehaviour
{
    [SerializeField] private bool panicRecommended;
    [SerializeField] private float panicScoreBonus;

    public bool PanicRecommended => panicRecommended;
    public float PanicScoreBonus => panicScoreBonus;

    public void BeginTick()
    {
        panicRecommended = false;
        panicScoreBonus = 0f;
    }

    public void ApplyFire()
    {
        panicRecommended = true;
        panicScoreBonus += 40f;
    }

    public void ApplyRaid()
    {
        panicRecommended = true;
        panicScoreBonus += 50f;
    }
}