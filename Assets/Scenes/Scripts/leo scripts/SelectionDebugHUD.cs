using UnityEngine;

public class SelectionDebugHUD : MonoBehaviour
{
    public WallBuildController build;

    void Awake()
    {
        if (build == null) build = FindFirstObjectByType<WallBuildController>();
    }

    void OnGUI()
    {
        if (build == null)
        {
            GUI.Label(new Rect(10, 10, 800, 30), "BuildController = NULL");
            return;
        }

        string name = (build.SelectedWall != null) ? build.SelectedWall.name : "NULL";
        GUI.Label(new Rect(10, 10, 800, 30), "SelectedWall: " + name);
    }
}
