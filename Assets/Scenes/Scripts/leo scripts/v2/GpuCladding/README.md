# Feuille de route : cladding pierres côté GPU

Le générateur actuel construit **une géométrie différente par pierre** (largeur, hauteur, relief aléatoire) puis instancie des `GameObject`. Pour un vrai gain GPU, il faut séparer **ce qui varie** et **ce qui est identique**.

## Piste A — Instancing (recommandée en premier)

**Principe** : un petit nombre de **meshes “LOD”** (ex. pierre S / M / L) + **matrice + paramètres** par instance.

- **CPU** : placement, choix du LOD, seed → produit une liste de `Matrix4x4` (+ éventuellement indices de variante).
- **GPU** : `Graphics.DrawMeshInstanced` / `DrawMeshInstancedIndirect` ou **SRP Batcher** avec beaucoup d’instances partageant le même matériau de base.
- **Matériau** : activer **GPU Instancing** sur le shader ; variations couleur → **couleur par instance** (`UNITY_INSTANCING_BUFFER`) ou texture d’atlas + `instanceID`.
- **Limite** : 1023 matrices par appel `DrawMeshInstanced` → boucler par paquets (voir `GpuStoneInstancingUtility.DrawMeshInstancedRange` / `DrawMeshInstancedAll`).

**Migration depuis l’existant** : regrouper les pierres en “familles” (même mesh ou même mesh + scale discret), arrêter de `new Mesh()` par pierre pour ces familles.

## Piste B — Compute shader + draw indirect

**Principe** : buffer d’instances (matrices, params) rempli ou mis à jour en **compute** ; un seul (ou peu) `DrawMeshInstancedIndirect`.

- Utile quand le nombre d’instances est **très** grand ou quand une partie de la logique peut être **parallélisée** (ex. culling grossier sur GPU).
- Plus de code pipeline (buffers, compteurs, compatibilité HDRP).

## Piste C — Fusion CPU d’un seul mesh par côté

**Principe** : `CombineMeshes` sur toutes les pierres d’un même matériau → **un** mesh, **un** renderer.

- Réduit draw calls et coût `Transform` / hiérarchie ; **toujours du CPU** pour la fusion.
- Perd les `MaterialPropertyBlock` par pierre sauf si tu **bakes** couleur en **vertex colors** avant fusion.

**Implémenté** : sur `WallCladdingGenerator`, active **`Combine Generated Stones Per Side`** (`combineGeneratedStonesPerSide`). Voir tooltip sur le composant (variation couleur par pierre).

## Ordre de travail suggéré

1. Choisir 1–3 **meshes de pierre** statiques (pas de génération procédurale par pierre pour cette variante).
2. Shader URP avec **instancing** + paramètre de teinte par instance.
3. Remplacer la boucle `GameObject` + `MeshFilter` par accumulation de matrices + `GpuStoneInstancingUtility.DrawMatricesBatched`.
4. Option : **frustum culling** CPU simple avant draw, ou culling GPU plus tard.

## Fichiers dans ce dossier

- `GpuStoneInstancingUtility.cs` — appels `DrawMeshInstanced` par paquets de 1023 (sans intégration au `WallCladdingGenerator` pour l’instant).
