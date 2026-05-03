/**
 * Simulation du cap triple faîtage (XZ). node tools/simulate_triple_roof_cap.mjs
 */

function sub(a, b) {
  return [a[0] - b[0], a[1] - b[1]];
}
function dot(a, b) {
  return a[0] * b[0] + a[1] * b[1];
}
function lensq(v) {
  return dot(v, v);
}
function projectSegment(p, a, b) {
  const ab = sub(b, a);
  const ab2 = lensq(ab);
  if (ab2 < 1e-14) return a.slice();
  const t = Math.max(0, Math.min(1, dot(sub(p, a), ab) / ab2));
  return [a[0] + ab[0] * t, a[1] + ab[1] * t];
}
function closestBroken(p, a0, a1, a2) {
  const qAb = projectSegment(p, a0, a1);
  const qBc = projectSegment(p, a1, a2);
  const dAb = lensq(sub(p, qAb));
  const dBc = lensq(sub(p, qBc));
  return dAb <= dBc ? qAb : qBc;
}
function triSignedAreaXZ(a, b, c) {
  return (
    0.5 *
    ((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1]))
  );
}
function orderTripleAlongAnchors(centroid, anchor, anchor2) {
  const e = sub(anchor2, anchor);
  const el2 = lensq(e);
  const eps = 1e-10;
  let items;
  if (el2 >= eps) {
    const ln = Math.sqrt(el2);
    const eu = [e[0] / ln, e[1] / ln];
    const kC = dot(sub(centroid, anchor), eu);
    items = [
      [kC, centroid],
      [0, anchor],
      [ln, anchor2],
    ];
  } else {
    const mid = [(anchor[0] + anchor2[0]) * 0.5, (anchor[1] + anchor2[1]) * 0.5];
    let d = sub(mid, centroid);
    if (lensq(d) < eps) d = sub(anchor2, centroid);
    if (lensq(d) < eps) return [anchor.slice(), anchor2.slice(), centroid.slice()];
    const mag = Math.sqrt(lensq(d));
    d = [d[0] / mag, d[1] / mag];
    items = [
      [0, centroid],
      [dot(sub(anchor, centroid), d), anchor],
      [dot(sub(anchor2, centroid), d), anchor2],
    ];
  }
  items.sort((a, b) => a[0] - b[0]);
  return [items[0][1], items[1][1], items[2][1]];
}
function ridgeParamFromA0(p, a0, a1, a2) {
  const q = closestBroken(p, a0, a1, a2);
  const qAb = projectSegment(q, a0, a1);
  const qBc = projectSegment(q, a1, a2);
  const errAb = lensq(sub(q, qAb));
  const errBc = lensq(sub(q, qBc));
  const lenAbSq = lensq(sub(a1, a0));
  const lenBcSq = lensq(sub(a2, a1));
  const lenAb = Math.sqrt(Math.max(lenAbSq, 1e-14));
  const lenBc = Math.sqrt(Math.max(lenBcSq, 1e-14));
  if (errAb <= errBc) {
    const t =
      lenAbSq > 1e-14
        ? dot(sub(q, a0), sub(a1, a0)) / lenAbSq
        : 0;
    const tc = Math.max(0, Math.min(1, t));
    return lenAb * tc;
  }
  const t2 =
    lenBcSq > 1e-14
      ? dot(sub(q, a1), sub(a2, a1)) / lenBcSq
      : 0;
  const t2c = Math.max(0, Math.min(1, t2));
  return lenAb + lenBc * t2c;
}

function lastRingWithSubdivisions(steps) {
  const baseCorners = [
    [-5, -5],
    [5, -5],
    [5, 5],
    [-5, 5],
  ];
  const centroid = [0, 0];
  const anchor = [-3, 0];
  const anchor2 = [3, 0];
  const [o0, o1, o2] = orderTripleAlongAnchors(centroid, anchor, anchor2);
  const ring = [];
  const nC = baseCorners.length;
  for (let i = 0; i < nC; i++) {
    const a = baseCorners[i];
    const b = baseCorners[(i + 1) % nC];
    const ca = baseCorners[i];
    const cb = baseCorners[(i + 1) % nC];
    for (let s = 0; s < steps; s++) {
      const t = s / steps;
      const pAlong = [
        ca[0] + (cb[0] - ca[0]) * t,
        ca[1] + (cb[1] - ca[1]) * t,
      ];
      ring.push(closestBroken(pAlong, o0, o1, o2));
    }
  }
  let area = 0;
  for (let i = 0; i < ring.length; i++) {
    const j = (i + 1) % ring.length;
    area += ring[i][0] * ring[j][1] - ring[j][0] * ring[i][1];
  }
  area *= 0.5;
  return { ring, areaAbs: Math.abs(area), o0, o1, o2 };
}

function main() {
  const baseCorners = [
    [-5, -5],
    [5, -5],
    [5, 5],
    [-5, 5],
  ];
  const centroid = [0, 0];
  const anchor = [-3, 0];
  const anchor2 = [3, 0];
  const [o0, o1, o2] = orderTripleAlongAnchors(centroid, anchor, anchor2);
  console.log("Ordered ridge O0,O1,O2:", o0, o1, o2);

  const lastRing = baseCorners.map((c) => closestBroken(c, o0, o1, o2));
  console.log("Last ring XZ:", lastRing);

  const stitches = [];
  for (let i = 0; i < 4; i++) {
    const j = (i + 1) % 4;
    const mid = [
      (lastRing[i][0] + lastRing[j][0]) * 0.5,
      (lastRing[i][1] + lastRing[j][1]) * 0.5,
    ];
    stitches.push(closestBroken(mid, o0, o1, o2));
  }
  console.log("Stitches on ridge:", stitches);

  const chain = [...[o0, o1, o2].map((x) => x.slice()), ...stitches];
  const uniq = [];
  const eps = 3e-3;
  for (const p of chain) {
    if (!uniq.some((q) => lensq(sub(p, q)) <= eps * eps)) uniq.push(p);
  }
  uniq.sort((a, b) => ridgeParamFromA0(a, o0, o1, o2) - ridgeParamFromA0(b, o0, o1, o2));
  console.log("Deduped sorted chain:", uniq);

  console.log("Interior fan signed areas (CCW in XZ => +Y normal in RH):");
  for (let k = 0; k + 1 < uniq.length; k++) {
    const ar = triSignedAreaXZ(centroid, uniq[k], uniq[k + 1]);
    console.log(`  ${k}: ${ar.toFixed(6)}`);
  }

  // Chevauchement : triangle premier fan vs stitch répété
  console.log("\n--- Problème structurel ---");
  console.log(
    "Les facettes (centroid, stitches le long du faîtage) sont COPLANAIRES Y=cste avec les facettes (bord, stitch, bord).",
    "=> doubles faces au même plan => z-fighting / profondeur bizarre si les normales diffèrent."
  );

  console.log("\n--- Avec subdivisions d’arête = 2 (même convention que Unity: t = s/steps, s = 0..steps-1) ---");
  const sub2 = lastRingWithSubdivisions(2);
  console.log("Last ring point count:", sub2.ring.length, "| polygon |area| XZ:", sub2.areaAbs.toFixed(4));
  if (sub2.areaAbs < 1e-6) {
    console.log(
      "  → Aire XZ ~ 0 attendue ici: périmètre rectangle + crête dans le plan z=0 ⇒ projections sur la crête sont colinéaires en XZ.",
      "La couverture 3D reste définie par les hauteurs Y le long de l’anneau (les pentes), pas par l’aire de cette projection."
    );
  }

  console.log("\n--- Cas centroïde décalé perpendiculairement au faîtage ---");
  const c2 = [0, 1.5];
  const [p0, p1, p2] = orderTripleAlongAnchors(c2, [-4, 0], [4, 0]);
  console.log("Ordered:", p0, p1, p2);
  console.log("Param centroid projection:", ridgeParamFromA0(c2, p0, p1, p2));
}

main();
