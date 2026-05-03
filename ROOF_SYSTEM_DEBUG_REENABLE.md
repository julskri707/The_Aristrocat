# Toit — réactiver le debug et la mise au point

Ce fichier liste tout ce qu’il faut **réactiver** pour continuer à travailler sur le système de toit (diagnostic shell « grand X », coupe expérimentale, logs, overlay Game View, grille visuelle).

---

## 1. Logs console (`HouseRoofSystem`)

Sur le composant **HouseRoofSystem** du mur / prefab :

| Champ Inspector | Rôle |
|-----------------|------|
| **Enable Verbose Roof Logs** (`enableVerboseRoofLogs`) | À **cocher** pour : `[RoofCornerBlock]`, logs « Connector end cap », touches **comma/period** en mode coupe expérimentale (`[RoofExperimentalCut]`). Par défaut **désactivé**. |

Les logs **`[RoofCrossDiag]`**, **`[RoofCrossDiagTri]`**, **`[RoofCrossFix]`**, **`[RoofExperimentalCut]`** (hors cycle touches) ne s’affichent que si les options correspondantes ci‑dessous sont déjà activées (chemins de code).

---

## 2. Overlay Game View (`RoofCrossDiagnosticOverlayUI`)

Présent sur la scène **Main** (ou autre) : composant **`RoofCrossDiagnosticOverlayUI`**.

| Champ | Pour réafficher l’overlay |
|-------|---------------------------|
| **Allow Game View Roof Debug Overlay** | **Cocher** (sinon OnGUI ne dessine rien, **F9** est ignoré). |
| **Show Overlay** | Cocher, ou laisser décoché et appuyer sur **F9** en jeu pour basculer. |

Sans ces réglages, aucun panneau « ROOF DEBUG » ne s’affiche dans la Game view.

---

## 3. Diagnostic shell / triangle (`HouseRoofSystem`)

Section **Debug — roof shell diagnostics** :

- **Debug Detect Roof Shell Crossing Detailed** — analyse + logs `[RoofCrossDiag]` lors des rebuilds.
- **Debug Detect Roof Triangle Intersections** — alias du même diagnostic (nom historique dans l’Inspector).

Section **Roof shell crossing — local fix** :

- **Apply Roof Shell Cross Local Fix** — correction locale sur le shell (avant épaisseur).

Section **Roof shell — experimental cut** :

- **Experimental Cut Raw Problem Triangles**
- **Experimental Use Single Triangle Slot** / **Experimental Single Triangle Slot** / liste de slots / **Experimental Cut Amount**
- **Experimental Cycle Triangle Slot With Keys** + touches comma/period (voir aussi **Enable Verbose Roof Logs** pour un log au changement de slot).

Logs / audit famille :

- **Debug Roof Cross Triangle Family Audit** → `[RoofCrossDiagTri]` / `[RoofCrossDiagFamily]` (spam si activé).
- **Auto Run Triangle Family Audit When Problem**
- **Debug Draw Roof Cross Triangle Family** — `Debug.DrawLine` + Gizmos sur les triangles suspects.

---

## 4. Blocage temporaire des coins (points jaune/orange)

Section **Temporary — éviter ancrage coin des poignées latérales** :

- **Disable Roof Corner Anchors Temporary** — décocher pour retrouver le snap exact sur coin + `MaybeSnapLateralOffsetToExactFootprintCorner` côté provider.
- **Roof Corner Anchor Block Radius** / **Roof Corner Anchor Push Distance** — ajuster le repoussement hors coin.

---

## 5. Grille visuelle empreinte (`HouseRoofFootprintGridOverlay`)

Si un GameObject porte **`HouseRoofFootprintGridOverlay`** :

- **Show Grid** — à cocher pour la boucle grise + segment guide ancrage (LineRenderer). Par défaut souvent **décoché**.

Les méthodes **statiques** de cette classe continuent de servir au snap même sans overlay affiché.

---

## 6. Raccourci contrôle

| Action | Composant / lieu |
|--------|-------------------|
| Overlay Game View | `RoofCrossDiagnosticOverlayUI` : **Allow Game View Roof Debug Overlay** + **Show Overlay** ou **F9**. |
| Logs divers toit | `HouseRoofSystem` : **Enable Verbose Roof Logs** + options shell/experimental selon le besoin. |
| Revenir au comportement « coin anchor » complet | `HouseRoofSystem` : **Disable Roof Corner Anchors Temporary** = **false**. |

---

*Dernière mise à jour : alignée sur les champs SerializeField actuels du projet.*
