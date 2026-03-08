
using UnityEngine;

public enum NeedProviderType
{
    Food,
    Bed,
    Warmth,
    Social,
    Safety
}

public class NeedProviderBase : MonoBehaviour
{
    public NeedProviderType type;
    public Transform interactionPoint;
}
