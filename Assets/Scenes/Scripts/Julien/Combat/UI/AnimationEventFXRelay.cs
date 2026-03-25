using UnityEngine;

public class AnimationEventFXRelay : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clip0;
    [SerializeField] private AudioClip clip1;
    [SerializeField] private AudioClip clip2;
    [SerializeField] [Range(0f, 1f)] private float clip0Volume = 1f;
    [SerializeField] [Range(0f, 1f)] private float clip1Volume = 1f;
    [SerializeField] [Range(0f, 1f)] private float clip2Volume = 1f;

    [Header("VFX")]
    [SerializeField] private ParticleSystem vfx0;
    [SerializeField] private ParticleSystem vfx1;
    [SerializeField] private ParticleSystem vfx2;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    public void AE_PlayClip0()
    {
        PlayClip(clip0, clip0Volume, "Clip0");
    }

    public void AE_PlayClip1()
    {
        PlayClip(clip1, clip1Volume, "Clip1");
    }

    public void AE_PlayClip2()
    {
        PlayClip(clip2, clip2Volume, "Clip2");
    }

    public void AE_PlayVfx0()
    {
        PlayVfx(vfx0, "Vfx0");
    }

    public void AE_PlayVfx1()
    {
        PlayVfx(vfx1, "Vfx1");
    }

    public void AE_PlayVfx2()
    {
        PlayVfx(vfx2, "Vfx2");
    }

    private void PlayClip(AudioClip clip, float volume, string label)
    {
        if (clip == null)
            return;

        if (audioSource == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[{nameof(AnimationEventFXRelay)}] No AudioSource available for {label} on '{name}'.", this);
            }

            return;
        }

        audioSource.PlayOneShot(clip, volume);
    }

    private void PlayVfx(ParticleSystem particleSystem, string label)
    {
        if (particleSystem == null)
            return;

        particleSystem.Play(true);

        if (debugLogs)
        {
            Debug.Log($"[{nameof(AnimationEventFXRelay)}] Played {label} on '{name}'.", this);
        }
    }
}
