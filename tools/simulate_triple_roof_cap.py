"""
Simulation hors Unity du cap triple faîtage (plan XZ, z = deuxième coordonnée).
Exécute: python tools/simulate_triple_roof_cap.py
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import List, Tuple

Vec2 = Tuple[float, float]


def sub(a: Vec2, b: Vec2) -> Vec2:
    return (a[0] - b[0], a[1] - b[1])


def dot(a: Vec2, b: Vec2) -> float:
    return a[0] * b[0] + a[1] * b[1]


def lensq(v: Vec2) -> float:
    return dot(v, v)


def project_segment(p: Vec2, a: Vec2, b: Vec2) -> Vec2:
    ab = sub(b, a)
    ab2 = lensq(ab)
    if ab2 < 1e-14:
        return a
    t = max(0.0, min(1.0, dot(sub(p, a), ab) / ab2))
    return (a[0] + ab[0] * t, a[1] + ab[1] * t)


def closest_broken(p: Vec2, a0: Vec2, a1: Vec2, a2: Vec2) -> Vec2:
    q_ab = project_segment(p, a0, a1)
    q_bc = project_segment(p, a1, a2)
    d_ab = lensq(sub(p, q_ab))
    d_bc = lensq(sub(p, q_bc))
    return q_ab if d_ab <= d_bc else q_bc


def tri_signed_area_xz(a: Vec2, b: Vec2, c: Vec2) -> float:
    """Demi produit vectoriel (orientation dans le plan x-z)."""
    return 0.5 * (
        (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
    )


def order_triple_along_anchors(centroid: Vec2, anchor: Vec2, anchor2: Vec2) -> Tuple[Vec2, Vec2, Vec2]:
    """Même logique que OrderTripleSummitAlongAnchorLineXZ (projection sur droite des deux ancres)."""
    e = sub(anchor2, anchor)
    el2 = lensq(e)
    eps = 1e-10
    items = []
    if el2 >= eps:
        ln = math.sqrt(el2)
        eu = (e[0] / ln, e[1] / ln)
        k_c = dot(sub(centroid, anchor), eu)
        k_a = 0.0
        k_b = ln
        items = [(k_c, centroid, 0), (k_a, anchor, 1), (k_b, anchor2, 2)]
    else:
        mid = ((anchor[0] + anchor2[0]) * 0.5, (anchor[1] + anchor2[1]) * 0.5)
        d = sub(mid, centroid)
        if lensq(d) < eps:
            d = sub(anchor2, centroid)
        if lensq(d) < eps:
            return anchor, anchor2, centroid
        mag = math.sqrt(lensq(d))
        d = (d[0] / mag, d[1] / mag)
        items = [
            (0.0, centroid, 0),
            (dot(sub(anchor, centroid), d), anchor, 1),
            (dot(sub(anchor2, centroid), d), anchor2, 2),
        ]
    items.sort(key=lambda t: (t[0], t[2]))
    return items[0][1], items[1][1], items[2][1]


def ridge_param_from_a0(p: Vec2, a0: Vec2, a1: Vec2, a2: Vec2) -> float:
    q = closest_broken(p, a0, a1, a2)
    q_ab = project_segment(q, a0, a1)
    q_bc = project_segment(q, a1, a2)
    err_ab = lensq(sub(q, q_ab))
    err_bc = lensq(sub(q, q_bc))
    len_ab = math.sqrt(max(lensq(sub(a1, a0)), 1e-14))
    len_bc = math.sqrt(max(lensq(sub(a2, a1)), 1e-14))
    len_ab_sq = lensq(sub(a1, a0))
    len_bc_sq = lensq(sub(a2, a1))
    if err_ab <= err_bc:
        t = dot(sub(q, a0), sub(a1, a0)) / len_ab_sq if len_ab_sq > 1e-14 else 0.0
        t = max(0.0, min(1.0, t))
        return len_ab * t
    t2 = dot(sub(q, a1), sub(a2, a1)) / len_bc_sq if len_bc_sq > 1e-14 else 0.0
    t2 = max(0.0, min(1.0, t2))
    return len_ab + len_bc * t2


def simulate_square_ridge_across():
    # Carré 10x10, centroïde origine
    base_corners = [(-5.0, -5.0), (5.0, -5.0), (5.0, 5.0), (-5.0, 5.0)]
    centroid = (0.0, 0.0)
    # Ancres aux deux bouts du faîtage sur l’axe X, centroïde au milieu (cas typique jaune + deux ambres)
    anchor = (-3.0, 0.0)
    anchor2 = (3.0, 0.0)
    o0, o1, o2 = order_triple_along_anchors(centroid, anchor, anchor2)
    print("Ordered ridge polyline O0,O1,O2:", o0, o1, o2)

    # Anneau haut (approximation : même que ridgeTarget à alpha=1 pour quad steps=1)
    last_ring = []
    n = 4
    for i in range(n):
        ca = base_corners[i]
        cb = base_corners[(i + 1) % n]
        pa = closest_broken(ca, o0, o1, o2)
        pb = closest_broken(cb, o0, o1, o2)
        # edgeLocalT=0 pour premier point de l’arête
        rt = pa  # simplify: vertex i at corner i uses t=0 along edge i->i+1
        last_ring.append(rt)

    # Réassign correctement comme le code C# : pour vertex i, edgeIdx=i, edgeLocalT=0 → ridgeTarget = proj(corner_i)
    last_ring = [closest_broken(base_corners[i], o0, o1, o2) for i in range(n)]

    print("Last ring XZ (should lie toward ridge):", last_ring)

    # Stitch samples: milieux des arêtes du last ring (comme les mids du cap)
    centroid_xz = centroid
    stitches = []
    for i in range(n):
        j = (i + 1) % n
        mid = ((last_ring[i][0] + last_ring[j][0]) * 0.5, (last_ring[i][1] + last_ring[j][1]) * 0.5)
        q = closest_broken(mid, o0, o1, o2)
        stitches.append(q)

    # Chaîne intérieure : sommets faîtage + stitches (sans dédoublonnage pour voir les params)
    apex_pts = [o0, o1, o2]
    chain_pts = list(apex_pts) + stitches

    def key(p):
        return ridge_param_from_a0(p, o0, o1, o2)

    chain_pts_sorted = sorted(chain_pts, key=key)
    print("Interior fan chain (sorted by arc length from O0):")
    for p in chain_pts_sorted:
        print(" ", p, "param", key(p))

    # Aires signées des triangles (centroid, Vk, Vk+1) — vue depuis +Y (normale sortante ~ signe aire)
    print("Interior fan triangle signed areas (want consistent sign for 'up' mesh):")
    for k in range(len(chain_pts_sorted) - 1):
        a = tri_signed_area_xz(centroid_xz, chain_pts_sorted[k], chain_pts_sorted[k + 1])
        print(f"  fan {k}: area={a:.6f}")

    # Problème potentiel : double couche — aire des couvertures edge cap vs fan
    print("\nEdge cap mids projection (ridge stitches):", stitches)

    # Cas L / centroïde hors segment entre ancres mais ordering met centroïde au milieu le long de la droite
    print("\n--- Cas : ancres proches d’un côté, centroïde « décalé » ---")
    centroid2 = (0.0, 1.5)
    anchor_b = (-4.0, 0.0)
    anchor2_b = (4.0, 0.0)
    p0, p1, p2 = order_triple_along_anchors(centroid2, anchor_b, anchor2_b)
    print("Ordered:", p0, p1, p2)
    # Param du centroïde sur la ligne des ancres (perpendiculairement projeté)
    kc = ridge_param_from_a0(centroid2, p0, p1, p2)
    print("Centroid ridge param:", kc)


if __name__ == "__main__":
    simulate_square_ridge_across()
