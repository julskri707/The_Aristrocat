using System.Collections.Generic;
using UnityEngine;

public class TowerGenerator : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject towerPrefab;

    [Header("Tower Roof")]
    public GameObject towerRoofPrefab;
    public float roofHeightOffset = 0.1f;
public float roofPivotYOffset = 1f;


    [Header("Detection")]
    [Tooltip("110 = normal. Monte vers 140 pour plus de tours.")]
    public float angleThresholdDeg = 110f;

    [Tooltip("Plus petit = tours plus grandes sur coins très serrés (35-45 ok)")]
    public float maxBigAngleDeg = 40f;

    [Tooltip("Évite 2 tours trop proches")]
    public float minCornerDistance = 1.2f;

    [Header("Size (based on angle)")]
    public float minRadius = 0.8f;
    public float maxRadius = 2.2f;
    public float minHeight = 2.0f;
    public float maxHeight = 5.0f;

    [Header("Placement")]
    public float yOffset = 0f;

    [Header("Animation")]
    public float popDuration = 0.22f;
    public float overshoot = 1.12f;
    public AnimationCurve popCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    public void GenerateTowers(List<Vector3> pointsWorld)
    {
        if (towerPrefab == null || pointsWorld == null || pointsWorld.Count < 4)
            return;

        // Supprime anciennes tours/toits
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        // Assure fermé
        bool isClosed = Vector3.Distance(pointsWorld[0], pointsWorld[pointsWorld.Count - 1]) < 0.001f;
        int count = isClosed ? pointsWorld.Count - 1 : pointsWorld.Count;

        List<Vector3> placed = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            Vector3 prev = pointsWorld[(i - 1 + count) % count];
            Vector3 cur = pointsWorld[i];
            Vector3 next = pointsWorld[(i + 1) % count];

            Vector3 a = (prev - cur); a.y = 0f;
            Vector3 b = (next - cur); b.y = 0f;

            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f)
                continue;

            a.Normalize();
            b.Normalize();

            float angle = Vector3.Angle(a, b); // 0..180

            //  pas un coin “assez serré” -> pas de tour
            if (angle > angleThresholdDeg)
                continue;

            //  évite doublons proches
            bool tooClose = false;
            for (int k = 0; k < placed.Count; k++)
            {
                if (Vector3.Distance(placed[k], cur) < minCornerDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            placed.Add(cur);

            //  mapping angle -> taille
            // angle proche de maxBigAngleDeg => grosse tour
            float t = Mathf.InverseLerp(angleThresholdDeg, maxBigAngleDeg, angle);
            t = Mathf.Clamp01(t);

            float radius = Mathf.Lerp(minRadius, maxRadius, t);
            float height = Mathf.Lerp(minHeight, maxHeight, t);

            Vector3 pos = cur + Vector3.up * yOffset;

            // --- Tower ---
            GameObject tower = Instantiate(towerPrefab, pos, Quaternion.identity, transform);

            // oriente vers bisectrice
            Vector3 bis = (a + b);
            bis.y = 0f;
            if (bis.sqrMagnitude > 0.0001f)
                tower.transform.rotation = Quaternion.LookRotation(-bis.normalized, Vector3.up);

            // scale tour (cylindre Unity : scale.y = moitié hauteur visuelle)
            Vector3 towerTargetScale = new Vector3(radius, height * 0.5f, radius);
            StartCoroutine(PopOvershoot(tower.transform, towerTargetScale));

           // --- Roof ---
if (towerRoofPrefab != null)
{
    GameObject roof = Instantiate(towerRoofPrefab, tower.transform);
    roof.transform.localRotation = Quaternion.identity;

    float desiredRadius = radius * 1.20f;
    float desiredHeight = desiredRadius * 1.15f;

    var cone = roof.GetComponent<ConeMesh>();
    if (cone != null)
    {
        cone.radius = desiredRadius;
        cone.height = desiredHeight;
        cone.Build();
    }

roof.transform.localPosition += Vector3.up * 2f;


    //  le toit suit le sommet pendant le pop
System.Collections.IEnumerator AttachRoofToTowerDuringPop(
    Transform tower,
    Transform roof,
    float towerHeight,
    float offset,
    float duration)
{
    if (tower == null || roof == null) yield break;

    float t = 0f;
    while (t < duration)
    {
        t += Time.deltaTime;

        //  toit collé au sommet en LOCAL
        roof.localPosition = new Vector3(0f, towerHeight + offset + roofPivotYOffset, 0f);

        yield return null;
    }
    roof.localPosition = new Vector3(0f, towerHeight + offset + roofPivotYOffset, 0f);
}
    StartCoroutine(PopOvershoot(roof.transform, Vector3.one));
}

        }
    }

    System.Collections.IEnumerator PopOvershoot(Transform tr, Vector3 targetScale)
    {
        tr.localScale = Vector3.zero;

        float half = popDuration * 0.65f;
        float rest = popDuration - half;

        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float x = Mathf.Clamp01(t / half);
            float e = popCurve.Evaluate(x);
            tr.localScale = Vector3.LerpUnclamped(Vector3.zero, targetScale * overshoot, e);
            yield return null;
        }

        float t2 = 0f;
        while (t2 < rest)
        {
            t2 += Time.deltaTime;
            float x = Mathf.Clamp01(t2 / rest);
            float e = popCurve.Evaluate(x);
            tr.localScale = Vector3.LerpUnclamped(targetScale * overshoot, targetScale, e);
            yield return null;
        }

        tr.localScale = targetScale;
    }
System.Collections.IEnumerator AttachRoofToTowerDuringPop(GameObject tower, Transform roof, float offset, float duration)
{
    if (tower == null || roof == null) yield break;

    float t = 0f;

    // pendant l'anim, on recolle chaque frame
    while (t < duration)
    {
        t += Time.deltaTime;

        Renderer r = tower.GetComponentInChildren<Renderer>();
        float topY = (r != null) ? r.bounds.max.y : tower.transform.position.y;

        // roof est enfant de tower => on convertit en local
        Vector3 worldPos = new Vector3(tower.transform.position.x, topY + offset, tower.transform.position.z);
        roof.position = worldPos;

        yield return null;
    }

    //  après l'anim, on fixe une dernière fois
    {
        Renderer r = tower.GetComponentInChildren<Renderer>();
        float topY = (r != null) ? r.bounds.max.y : tower.transform.position.y;
        Vector3 worldPos = new Vector3(tower.transform.position.x, topY + offset, tower.transform.position.z);
        roof.position = worldPos;
    }
}

}