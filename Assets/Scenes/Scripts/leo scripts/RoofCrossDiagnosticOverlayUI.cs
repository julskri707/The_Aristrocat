using UnityEngine;

/// <summary>
/// Overlay Game View (OnGUI) pour l’état RoofCrossDiag sans lire la console Unity.
/// Ajouter sur un GameObject actif en jeu (ex. une caméra ou un gestionnaire UI léger).
/// Par défaut : overlay masqué ; le diagnostic shell complexe est désactivé côté HouseRoofSystem.
/// </summary>
public class RoofCrossDiagnosticOverlayUI : MonoBehaviour
{
    [Tooltip("Si faux : aucun rendu OnGUI, F9 ignoré. Réactiver pour voir l’overlay Game View.")]
    [SerializeField] bool allowGameViewRoofDebugOverlay = false;

    [SerializeField] bool showOverlay = false;
    [SerializeField] KeyCode toggleKey = KeyCode.F9;

    [Tooltip("Prioritaire si renseigné.")]
    [SerializeField] HouseRoofSystem roofOverride;

    [Tooltip("Optionnel : mur sélectionné utilisé en priorité pour trouver le toit.")]
    [SerializeField] WallBuildController wallBuildController;

    GUIStyle _boxStyle;
    GUIStyle _titleStyle;
    bool _stylesInitialized;

    void Awake()
    {
        if (wallBuildController == null)
            wallBuildController = FindObjectOfType<WallBuildController>();
    }

    void Update()
    {
        if (!allowGameViewRoofDebugOverlay)
            return;
        if (Input.GetKeyDown(toggleKey))
            showOverlay = !showOverlay;
    }

    void EnsureStyles()
    {
        if (_stylesInitialized)
            return;

        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 14,
            richText = true,
            wordWrap = true
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            richText = true,
            wordWrap = true
        };

        _stylesInitialized = true;
    }

    void OnGUI()
    {
        if (!allowGameViewRoofDebugOverlay)
            return;
        if (!showOverlay)
            return;

        EnsureStyles();

        HouseRoofSystem roof = ResolveRoofSystem();
        GUILayout.BeginArea(new Rect(12f, 12f, 460f, 680f));

        if (roof == null)
        {
            GUI.contentColor = Color.white;
            GUILayout.Label("<color=#cccccc>ROOF DEBUG: NO ROOF</color>", _titleStyle);
            GUILayout.Label("No HouseRoofSystem found.\nAssign roof Override or add a roof to the scene.", _boxStyle);
            GUILayout.EndArea();
            return;
        }

        bool shellDiag = roof.IsRoofCrossShellDiagnosticEnabled;
        GUI.contentColor = shellDiag ? new Color(0.75f, 0.9f, 1f) : new Color(0.75f, 0.75f, 0.75f);
        GUILayout.Label(
            shellDiag ? "<color=#aaccff>ROOF DEBUG: DIAG ENABLED</color>" : "<color=#cccccc>ROOF DEBUG: DISABLED</color>",
            _titleStyle);
        GUI.contentColor = Color.white;

        GUILayout.Label(
            $"Corner anchors blocked: {(roof.DisableRoofCornerAnchorsTemporary ? "ON" : "OFF")}",
            _boxStyle);

        if (!shellDiag)
        {
            GUILayout.EndArea();
            return;
        }

        if (roof.IsExperimentalRoofShellCutEnabled)
        {
            GUILayout.Label(
                $"Experimental cut: {(roof.IsExperimentalRoofShellCutEnabled ? "ON" : "OFF")}",
                _boxStyle);
            GUILayout.Label(
                $"Single slot mode: {(roof.IsExperimentalSingleSlotMode ? "ON" : "OFF")}",
                _boxStyle);
            GUILayout.Label($"Current slot: {roof.ExperimentalCurrentSlotDisplay}", _boxStyle);
            GUILayout.Label($"Cut slots: {roof.ExperimentalCutTriangleSlotsDisplay}", _boxStyle);
        }

        if (!roof.RoofCrossDiagScanCompleted)
        {
            GUI.contentColor = new Color(0.85f, 0.85f, 0.85f);
            GUILayout.Label("<color=#dddddd>ROOF DIAG SCAN: PENDING</color>", _titleStyle);
            GUI.contentColor = Color.white;
            GUILayout.Label("No roof diagnostic scan yet.\nRebuild the roof after enabling shell diagnostics.", _boxStyle);
            GUILayout.EndArea();
            return;
        }

        string st = roof.LastRoofCrossDiagStatus;
        if (st == "CLEAN")
            GUI.contentColor = new Color(0.45f, 0.95f, 0.55f);
        else if (st == "PROBLEM")
            GUI.contentColor = new Color(1f, 0.35f, 0.25f);
        else
            GUI.contentColor = Color.white;

        GUILayout.Label($"ROOF DIAG: {st}", _titleStyle);
        GUI.contentColor = Color.white;

        int rawProblems = roof.LastRoofCrossDiagRawProblemCount;
        GUILayout.Label($"Triangles scanned: {roof.LastRoofCrossDiagTrianglesScanned}", _boxStyle);
        GUILayout.Label($"Raw problems: {rawProblems}", _boxStyle);
        GUILayout.Label($"DualCornerAnchor: {roof.LastRoofCrossDiagDualCornerAnchor}", _boxStyle);
        if (!HouseRoofSystem.IsRoofCrossDiagProblemCountConsideredRealProblem(rawProblems))
        {
            GUILayout.Label(
                "Rule: only 7 or 12 contacts are treated as real roof crossing.",
                _boxStyle);
        }

        GUILayout.Label("Chosen problem: NONE", _boxStyle);
        if (!string.IsNullOrEmpty(roof.LastRoofCrossDiagChosenReason))
            GUILayout.Label($"Reason: {roof.LastRoofCrossDiagChosenReason}", _boxStyle);

        GUILayout.Label("CutPoint: NOT READY", _boxStyle);
        GUILayout.Label(
            "CutPoint: disabled until parasite triangle family is identified",
            _boxStyle);

        if (roof.RoofCrossDiagScanCompleted)
        {
            GUILayout.Label(
                $"Triangle family audit: {(roof.IsRoofCrossTriangleFamilyAuditOn ? "ON" : "OFF")}",
                _boxStyle);
            GUILayout.Label(
                $"X candidates: {roof.LastRoofCrossXDiagonalCandidatesDisplay}",
                _boxStyle);
            GUILayout.Label(
                $"X participants: {roof.LastRoofCrossXCrossParticipantsDisplay}",
                _boxStyle);
            GUILayout.Label(
                $"Parasite likely: {roof.LastRoofCrossParasiteLikelySlotsDisplay}",
                _boxStyle);
            GUILayout.Label(
                $"Draw triangles: {(roof.IsDebugDrawRoofCrossTriangleFamilyEnabled ? "ON" : "OFF")}",
                _boxStyle);
        }

        GUILayout.EndArea();
    }

    HouseRoofSystem ResolveRoofSystem()
    {
        if (roofOverride != null)
            return roofOverride;

        if (wallBuildController != null && wallBuildController.SelectedWall != null)
        {
            HouseRoofSystem r = wallBuildController.SelectedWall.GetComponent<HouseRoofSystem>();
            if (r != null)
                return r;
        }

        // Dernier repli : premier HouseRoofSystem de la scène (y compris inactif si l’API le permet).
        HouseRoofSystem[] all = FindObjectsByType<HouseRoofSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return all != null && all.Length > 0 ? all[0] : null;
    }
}
