using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HouseExteriorEnvelopeSources : MonoBehaviour
{
    [SerializeField] private List<GameObject> sourceLotObjects = new List<GameObject>(16);

    [Tooltip(
        "Si vrai (recommandé) : les poignées d’édition sont celles de chaque lot source (rectangles d’origine), " +
        "l’enveloppe ne fait qu’afficher le mur pierre fusionné. Si faux : comportement précédent (contour fusionné unique + poignées blanches sur l’enveloppe).")]
    [SerializeField] bool useIndependentSourceHandlesForHouseEnvelope = true;

    public IReadOnlyList<GameObject> SourceLotObjects => sourceLotObjects;

    /// <summary>
    /// Lots sources gardent leurs poignées d’origine ; l’enveloppe est recalculée à partir d’eux.
    /// </summary>
    public bool UseIndependentSourceHandlesForHouseEnvelope => useIndependentSourceHandlesForHouseEnvelope;

    /// <summary>Au moins deux lots sources encore référencés (enveloppe maison multi-plans).</summary>
    public bool HasMultipleSourceLots
    {
        get
        {
            if (sourceLotObjects == null)
                return false;
            int n = 0;
            for (int i = 0; i < sourceLotObjects.Count; i++)
            {
                GameObject go = sourceLotObjects[i];
                if (go == null)
                    continue;
                if (go.GetComponent<WallObject>() != null)
                    n++;
            }

            return n >= 2;
        }
    }

    public void SetSources(IEnumerable<WallObject> walls)
    {
        sourceLotObjects.Clear();
        if (walls == null)
            return;

        foreach (WallObject w in walls)
        {
            if (w == null)
                continue;
            sourceLotObjects.Add(w.gameObject);
        }
    }

    /// <summary>
    /// Remplace la liste en conservant toutes les références de lots sources déjà enregistrées, puis en y ajoutant
    /// ceux de <paramref name="mergeSet"/> (hors mur enveloppe). Nécessaire quand le BFS ne remonte qu’enveloppe + nouveau lot
    /// mais pas les carrés sources non contigus entre eux, sinon on repasse à 2 formes au lieu de N.
    /// </summary>
    public void SetSourcesMergingWithMergeSet(IEnumerable<WallObject> mergeSet, WallObject envelopeWall)
    {
        HashSet<WallObject> combined = new HashSet<WallObject>();
        if (sourceLotObjects != null)
        {
            for (int i = 0; i < sourceLotObjects.Count; i++)
            {
                GameObject go = sourceLotObjects[i];
                if (go == null)
                    continue;
                WallObject w = go.GetComponent<WallObject>();
                if (w == null)
                    continue;
                if (envelopeWall != null && w == envelopeWall)
                    continue;
                combined.Add(w);
            }
        }
        if (mergeSet != null)
        {
            foreach (WallObject w in mergeSet)
            {
                if (w == null)
                    continue;
                if (envelopeWall != null && w == envelopeWall)
                    continue;
                combined.Add(w);
            }
        }
        SetSources(combined);
    }

    /// <summary>Restauration Ctrl+Z : réapplique aussi le mode poignées indépendantes.</summary>
    public void RestoreUndoState(bool independentHandles, IEnumerable<WallObject> walls)
    {
        useIndependentSourceHandlesForHouseEnvelope = independentHandles;
        SetSources(walls);
    }

    /// <summary>
    /// Depuis un impact sur le mur enveloppe fusionné, détermine quel plan source est le plus « proche » du clic
    /// (distance XZ au pourtour du plan source), pour n’afficher que ses poignées.
    /// </summary>
    public bool TryResolveSourceLotIndexForEnvelopeClick(Vector3 hitWorld, out int sourceLotIndex)
    {
        sourceLotIndex = -1;
        if (sourceLotObjects == null || sourceLotObjects.Count == 0)
            return false;

        Vector2 p = new Vector2(hitWorld.x, hitWorld.z);
        float best = float.MaxValue;
        int bestIdx = -1;
        const float TieEps = 1e-4f;

        for (int i = 0; i < sourceLotObjects.Count; i++)
        {
            GameObject go = sourceLotObjects[i];
            if (go == null)
                continue;
            WallEditShape wes = go.GetComponent<WallEditShape>();
            if (wes == null)
                continue;

            float d = MinDistancePointToSourceFootprintBoundaryXZ(p, wes);
            if (d > 1e9f)
                continue;

            if (d < best - TieEps || (Mathf.Abs(d - best) <= TieEps && (bestIdx < 0 || i < bestIdx)))
            {
                best = d;
                bestIdx = i;
            }
        }

        if (bestIdx < 0)
            return false;

        sourceLotIndex = bestIdx;
        return true;
    }

    static float MinDistancePointToSourceFootprintBoundaryXZ(Vector2 p, WallEditShape wes)
    {
        if (wes == null)
            return float.MaxValue;

        List<Vector3> path = wes.GetPreviewPathWorld();
        if (path == null || path.Count < 2)
            return float.MaxValue;

        bool closed = wes.IsClosedLoopPath;

        int n = path.Count;
        if (closed && n >= 2)
        {
            Vector2 f = new Vector2(path[0].x, path[0].z);
            Vector2 l = new Vector2(path[n - 1].x, path[n - 1].z);
            if ((f - l).sqrMagnitude < 1e-6f)
                n--;
        }

        if (n < 2)
            return float.MaxValue;

        float best = float.MaxValue;
        if (closed)
        {
            for (int i = 0; i < n; i++)
            {
                Vector2 a = new Vector2(path[i].x, path[i].z);
                int j = (i + 1) % n;
                Vector2 b = new Vector2(path[j].x, path[j].z);
                float d = DistancePointToSegmentXZ(p, a, b);
                if (d < best)
                    best = d;
            }
        }
        else
        {
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = new Vector2(path[i].x, path[i].z);
                Vector2 b = new Vector2(path[i + 1].x, path[i + 1].z);
                float d = DistancePointToSegmentXZ(p, a, b);
                if (d < best)
                    best = d;
            }
        }

        return best;
    }

    static float DistancePointToSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-14f)
            return (p - a).magnitude;

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        Vector2 proj = a + ab * t;
        return (p - proj).magnitude;
    }

    /// <summary>
    /// Si <see cref="HouseEnvelopeBundledSourceTag"/> manque sur un lot source, retrouve l'enveloppe via les listes
    /// enregistrées sur les objets enveloppe (undo, vieux prefabs, chemins de fusion sans tag).
    /// </summary>
    public static bool TryFindEnvelopeWallForSourceLot(WallObject sourceLot, out WallObject envelopeWall)
    {
        envelopeWall = null;
        if (sourceLot == null)
            return false;

        HouseExteriorEnvelopeSources[] metas = Object.FindObjectsByType<HouseExteriorEnvelopeSources>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < metas.Length; i++)
        {
            HouseExteriorEnvelopeSources meta = metas[i];
            if (meta == null)
                continue;

            IReadOnlyList<GameObject> src = meta.SourceLotObjects;
            if (src == null)
                continue;

            for (int j = 0; j < src.Count; j++)
            {
                GameObject go = src[j];
                if (go == null)
                    continue;
                WallObject w = go.GetComponent<WallObject>();
                if (w != null && w == sourceLot)
                {
                    envelopeWall = meta.GetComponent<WallObject>();
                    return envelopeWall != null;
                }
            }
        }

        return false;
    }
}
