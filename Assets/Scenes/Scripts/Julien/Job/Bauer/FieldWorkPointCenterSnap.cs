using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FieldWorkPointSnapSimple : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private FieldArea fieldArea;

    [Header("Placement")]
    [SerializeField] private float extraYOffset = 0.05f;
    [SerializeField] private bool snapOnStart = true;
    [SerializeField] private bool keepFollowingCenter = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

    private bool triedAutoFind = false;

    private void Awake()
    {
        TryResolveFieldArea();
    }

    private void Start()
    {
        if (snapOnStart)
            StartCoroutine(SnapWhenReady());
    }

    private void LateUpdate()
    {
        if (keepFollowingCenter)
        {
            TryResolveFieldArea();

            if (IsFieldReady())
                SnapNow();
        }
    }

    [ContextMenu("Snap Now")]
    public void SnapNow()
    {
        TryResolveFieldArea();

        if (!IsFieldReady())
        {
            if (debugLogs)
                Debug.LogWarning($"[FieldWorkPointSnapSimple] FieldArea not ready on '{name}'.", this);
            return;
        }

        Vector3 center = GetFieldCenterWorld();
        center.y = GetTargetY(center);

        transform.position = center;

        if (debugLogs)
            Debug.Log($"[FieldWorkPointSnapSimple] '{name}' snapped to {center} on field '{fieldArea.name}'.", this);
    }

    private IEnumerator SnapWhenReady()
    {
        // ein paar Frames warten, damit FieldArea nach SetPoints/Rebuild sicher fertig ist
        for (int i = 0; i < 10; i++)
        {
            TryResolveFieldArea();

            if (IsFieldReady())
            {
                SnapNow();
                yield break;
            }

            yield return null;
        }

        if (debugLogs)
            Debug.LogWarning($"[FieldWorkPointSnapSimple] Could not snap '{name}' because field was not ready after waiting.", this);
    }

    private void TryResolveFieldArea()
    {
        if (fieldArea != null)
            return;

        if (triedAutoFind && fieldArea == null)
            return;

        fieldArea = GetComponentInParent<FieldArea>();
        triedAutoFind = true;

        if (fieldArea == null && debugLogs)
        {
            Debug.LogWarning($"[FieldWorkPointSnapSimple] No FieldArea found in parents of '{name}'. Assign it manually in the Inspector.", this);
        }
    }

    private bool IsFieldReady()
    {
        if (fieldArea == null)
            return false;

        return fieldArea.worldPoints != null && fieldArea.worldPoints.Count >= 3;
    }

    private Vector3 GetFieldCenterWorld()
    {
        // 1) stabilster Fall: selection trigger center benutzen,
        // weil FieldArea den selbst aus den Feld-Bounds berechnet
        if (fieldArea.selectionBoxTrigger != null && fieldArea.selectionBoxTrigger.enabled)
        {
            return fieldArea.selectionBoxTrigger.bounds.center;
        }

        // 2) fallback: Bounds aus worldPoints
        List<Vector3> pts = fieldArea.worldPoints;

        float minX = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;
        float sumY = 0f;

        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];

            if (p.x < minX) minX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.x > maxX) maxX = p.x;
            if (p.z > maxZ) maxZ = p.z;

            sumY += p.y;
        }

        float avgY = sumY / pts.Count;

        return new Vector3(
            (minX + maxX) * 0.5f,
            avgY,
            (minZ + maxZ) * 0.5f
        );
    }

    private float GetTargetY(Vector3 center)
    {
        // selection trigger liegt laut FieldArea leicht über avgY,
        // wir wollen den WorkPoint auf Feldhöhe + Offset setzen
        float avgY = GetAverageY(fieldArea.worldPoints);
        return avgY + Mathf.Max(0f, fieldArea.heightOffset) + extraYOffset;
    }

    private float GetAverageY(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < pts.Count; i++)
            sum += pts[i].y;

        return sum / pts.Count;
    }
}