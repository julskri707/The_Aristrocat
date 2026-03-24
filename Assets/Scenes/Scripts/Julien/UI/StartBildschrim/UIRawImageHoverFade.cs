using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class UIRawImageHoverFade : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Images")]
    [SerializeField] private RawImage normalImage;
    [SerializeField] private RawImage hoverImage;

    [Header("Fade")]
    [SerializeField] private float fadeDuration = 0.2f;
    [SerializeField] private bool startInNormalState = true;

    private float currentLerp = 0f;
    private float targetLerp = 0f;

    private void Awake()
    {
        if (normalImage == null || hoverImage == null)
        {
            Debug.LogError($"UIRawImageHoverFade on {name}: normalImage oder hoverImage fehlt.");
            enabled = false;
            return;
        }

        if (startInNormalState)
        {
            currentLerp = 0f;
            targetLerp = 0f;
        }
        else
        {
            currentLerp = 1f;
            targetLerp = 1f;
        }

        ApplyVisual(currentLerp);
    }

    private void Update()
    {
        if (Mathf.Approximately(currentLerp, targetLerp))
            return;

        float speed = fadeDuration <= 0.0001f ? 999f : 1f / fadeDuration;
        currentLerp = Mathf.MoveTowards(currentLerp, targetLerp, Time.unscaledDeltaTime * speed);
        ApplyVisual(currentLerp);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetLerp = 1f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetLerp = 0f;
    }

    private void ApplyVisual(float t)
    {
        SetAlpha(normalImage, 1f - t);
        SetAlpha(hoverImage, t);

        normalImage.raycastTarget = t < 0.999f;
        hoverImage.raycastTarget = false;
    }

    private void SetAlpha(Graphic g, float alpha)
    {
        Color c = g.color;
        c.a = alpha;
        g.color = c;
    }

    public void SetInstantNormal()
    {
        currentLerp = 0f;
        targetLerp = 0f;
        ApplyVisual(currentLerp);
    }

    public void SetInstantHover()
    {
        currentLerp = 1f;
        targetLerp = 1f;
        ApplyVisual(currentLerp);
    }
}