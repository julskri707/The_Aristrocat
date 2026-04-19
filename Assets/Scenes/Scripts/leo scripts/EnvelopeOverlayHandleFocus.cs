using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Désambiguïse rose (plan source) vs violet (pivot enveloppe) quand les deux se superposent :
/// le dernier type de poignée ayant reçu un clic réussi reçoit les raycasts jusqu’au prochain clic sur l’autre.
/// Après rebuild overlay / undo, appeler <see cref="ClearFocusForWall"/> ou <see cref="ClearAllFocus"/> pour repasser en défaut (rose prioritaire si enveloppe multi-sources).
/// </summary>
public static class EnvelopeOverlayHandleFocus
{
    public enum Kind
    {
        None,
        SourceLotPink,
        EnvelopePivotViolet
    }

    static readonly Dictionary<WallObject, Kind> s_FocusByWall = new Dictionary<WallObject, Kind>(8);

    public static void SetFocusPink(WallObject wall)
    {
        if (wall == null)
            return;
        s_FocusByWall[wall] = Kind.SourceLotPink;
    }

    public static void SetFocusViolet(WallObject wall)
    {
        if (wall == null)
            return;
        s_FocusByWall[wall] = Kind.EnvelopePivotViolet;
    }

    public static void ClearFocusForWall(WallObject wall)
    {
        if (wall == null)
            return;
        s_FocusByWall.Remove(wall);
    }

    public static void ClearAllFocus()
    {
        s_FocusByWall.Clear();
    }

    /// <summary>Enveloppe multi-sources : rose recevable au raycast (défaut si <see cref="Kind.None"/> : oui).</summary>
    public static bool ShouldPinkReceiveRaycasts(WallObject wall)
    {
        if (wall == null || !IsMultiSourceEnvelope(wall))
            return true;

        if (!s_FocusByWall.TryGetValue(wall, out Kind k))
            return true;

        return k != Kind.EnvelopePivotViolet;
    }

    /// <summary>Enveloppe multi-sources : violet recevable au raycast (défaut si None : oui — sinon le pivot ne capte jamais le premier clic).</summary>
    public static bool ShouldVioletReceiveRaycasts(WallObject wall)
    {
        if (wall == null || !IsMultiSourceEnvelope(wall))
            return true;

        if (!s_FocusByWall.TryGetValue(wall, out Kind k))
            return true;

        return k == Kind.EnvelopePivotViolet;
    }

    static bool IsMultiSourceEnvelope(WallObject wall)
    {
        if (wall == null)
            return false;
        HouseExteriorEnvelopeSources hes = wall.GetComponent<HouseExteriorEnvelopeSources>();
        return hes != null && hes.HasMultipleSourceLots;
    }
}
