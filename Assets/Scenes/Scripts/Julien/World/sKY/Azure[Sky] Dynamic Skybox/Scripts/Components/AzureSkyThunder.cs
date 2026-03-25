using System;

namespace UnityEngine.AzureSky
{
    [Serializable]
    public struct AzureSkyThunder
    {
        public AudioClip audioClip;
        public float audioDelay;
        public AnimationCurve lightingCurve;
        public float lightingSpeed;
    }
}
