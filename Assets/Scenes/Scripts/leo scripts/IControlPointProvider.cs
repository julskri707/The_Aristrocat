using UnityEngine;

public interface IControlPointProvider
{
    int ControlPointCount { get; }

    Vector3 GetControlPointWorld(int index);

    void SetControlPointWorld(int index, Vector3 worldPos);

    // Tu peux renvoyer true tout le temps si tu veux
    bool IsControlPointEditable(int index);
}
