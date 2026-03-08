
using UnityEngine;

[CreateAssetMenu(menuName="NPC/NeedsProfile")]
public class NPCNeedsProfileSO : ScriptableObject
{
    public float hungerDecay=0.75f;
    public float energyDecay=0.45f;
    public float warmthDecay=0.2f;
    public float safetyDecay=0.05f;
    public float socialDecay=0.15f;
}
