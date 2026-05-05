using UnityEngine;

/// <summary>
/// Identifiant simple par provider : les poignées sont celles du contour « forme » murale
/// (<see cref="WallEditShape"/>, polyline <see cref="WallObject"/>, etc.) ou d’un système attaché (toit, mobilier catalogue…).
/// </summary>
public interface IControlPointWallShapeBinding
{
    bool ControlPointsBelongToWallShape { get; }
}

/// <summary>
/// Racine unique pour « ce point appartient-il à une forme murale ? » — à utiliser depuis les poignées overlay ou la logique jeu.
/// Pour classifier les providers (toit vs mur, etc.) ; la logique Suppr sur les sommets utilise surtout <see cref="WallEditShape.IsNonDeletableMovementHandleIndex"/>.
/// </summary>
public static class ControlPointShapeMembership
{
    /// <summary>
    /// Vrai si le provider expose des sommets du contour / forme du mur ; faux pour toit, escalier catalogue, objet placé, etc.
    /// </summary>
    public static bool BelongsToWallShape(IControlPointProvider provider)
    {
        if (provider == null)
            return false;

        // Proxy éditeur / runtime : déléguer au provider réellement sélectionné.
        if (provider is SelectedWallControlPointProvider proxy)
            return BelongsToWallShape(proxy.ActiveWallProvider);

        if (provider is IControlPointWallShapeBinding binding)
            return binding.ControlPointsBelongToWallShape;

        return false;
    }
}
