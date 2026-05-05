using System;
using System.Collections.Generic;
using UnityEngine;

public enum WallOpeningKind
{
    Door = 0,
    Window = 1
}

[Serializable]
public struct WallOpeningEntry
{
    public int segmentIndex;
    [Range(0f, 1f)] public float t0;
    [Range(0f, 1f)] public float t1;
    [Range(0f, 1f)] public float h0;
    [Range(0f, 1f)] public float h1;
    public WallOpeningKind kind;
}

/// <summary>
/// Per-<see cref="WallObject"/> openings (doors/windows). Consumed by <see cref="WallObject"/> mesh rebuild.
/// </summary>
[DisallowMultipleComponent]
public class WallOpeningRegistry : MonoBehaviour
{
    public List<WallOpeningEntry> entries = new List<WallOpeningEntry>();

    /// <returns>Index de l’entrée ajoutée, ou -1 si refus.</returns>
    public int AddOpening(int segmentIndex, float tCenter, float widthAlongWallMeters, float segmentLengthMeters,
        float h0, float h1, WallOpeningKind kind)
    {
        if (segmentLengthMeters < 0.01f || widthAlongWallMeters < 0.01f)
            return -1;

        float half = (widthAlongWallMeters * 0.5f) / segmentLengthMeters;
        float t0 = Mathf.Clamp01(tCenter - half);
        float t1 = Mathf.Clamp01(tCenter + half);
        if (t1 - t0 < 0.02f)
            return -1;

        float hLo = Mathf.Clamp01(Mathf.Min(h0, h1));
        float hHi = Mathf.Clamp01(Mathf.Max(h0, h1));
        h0 = hLo;
        h1 = hHi;
        if (h1 - h0 < 0.02f)
            return -1;

        entries.Add(new WallOpeningEntry
        {
            segmentIndex = segmentIndex,
            t0 = t0,
            t1 = t1,
            h0 = h0,
            h1 = h1,
            kind = kind
        });

        return entries.Count - 1;
    }

    public bool TryGetEntry(int index, out WallOpeningEntry e)
    {
        if (index < 0 || index >= entries.Count)
        {
            e = default;
            return false;
        }

        e = entries[index];
        return true;
    }

    public void SetEntry(int index, WallOpeningEntry e)
    {
        if (index < 0 || index >= entries.Count)
            return;
        entries[index] = e;
    }

    public bool HasOpeningsForSegment(int segmentIndex)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].segmentIndex == segmentIndex)
                return true;
        }

        return false;
    }

    public void GetOpeningsForSegment(int segmentIndex, List<WallOpeningEntry> outList)
    {
        outList.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].segmentIndex == segmentIndex)
                outList.Add(entries[i]);
        }
    }
}
