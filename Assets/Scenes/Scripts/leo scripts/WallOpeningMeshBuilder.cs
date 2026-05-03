using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds outer/inner wall quads with a rectangular cut-out plus jambs (tunnel through thickness).
/// Parameterization matches <see cref="WallObject"/> segment quads: f in [0,1] from control point i to n, h in [0,1] from base to top.
/// </summary>
public static class WallOpeningMeshBuilder
{
    public static void AppendSegmentWallFacesWithHoles(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 outBi,
        Vector3 outTi,
        Vector3 outTn,
        Vector3 outBn,
        Vector3 inBi,
        Vector3 inTi,
        Vector3 inTn,
        Vector3 inBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        Vector3 expectedOuterNormal,
        List<WallOpeningEntry> holes)
    {
        if (holes == null || holes.Count == 0)
        {
            AddQuadTwoSided(verts, uvs, tris, outBi, outTi, outTn, outBn, 0f, vSeg0, uHeight, vSeg1, expectedOuterNormal);
            AddQuadTwoSided(verts, uvs, tris, inBn, inTn, inTi, inBi, 0f, vSeg1, uHeight, vSeg0, -expectedOuterNormal);
            return;
        }

        float f0 = holes[0].t0;
        float f1 = holes[0].t1;
        float h0 = holes[0].h0;
        float h1 = holes[0].h1;
        for (int i = 1; i < holes.Count; i++)
        {
            WallOpeningEntry e = holes[i];
            f0 = Mathf.Min(f0, e.t0);
            f1 = Mathf.Max(f1, e.t1);
            h0 = Mathf.Min(h0, e.h0);
            h1 = Mathf.Max(h1, e.h1);
        }

        f0 = Mathf.Clamp01(f0);
        f1 = Mathf.Clamp01(f1);
        h0 = Mathf.Clamp01(h0);
        h1 = Mathf.Clamp01(h1);
        if (f1 - f0 < 1e-4f || h1 - h0 < 1e-4f)
        {
            AddQuadTwoSided(verts, uvs, tris, outBi, outTi, outTn, outBn, 0f, vSeg0, uHeight, vSeg1, expectedOuterNormal);
            AddQuadTwoSided(verts, uvs, tris, inBn, inTn, inTi, inBi, 0f, vSeg1, uHeight, vSeg0, -expectedOuterNormal);
            return;
        }

        // Outer / inner: up to 8 patches around the hole (3x3 grid minus center).
        EmitOuterInnerPatches(verts, uvs, tris,
            outBi, outTi, outTn, outBn, inBi, inTi, inTn, inBn,
            vSeg0, vSeg1, uHeight, expectedOuterNormal, f0, f1, h0, h1);

        AppendJambs(verts, uvs, tris,
            outBi, outTi, outTn, outBn, inBi, inTi, inTn, inBn,
            vSeg0, vSeg1, uHeight, f0, f1, h0, h1);
    }

    static void EmitOuterInnerPatches(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 outBi,
        Vector3 outTi,
        Vector3 outTn,
        Vector3 outBn,
        Vector3 inBi,
        Vector3 inTi,
        Vector3 inTn,
        Vector3 inBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        Vector3 expectedOuterNormal,
        float f0,
        float f1,
        float h0,
        float h1)
    {
        // Outer: regions [0,f0]x[0,1], [f1,1]x[0,1], [f0,f1]x[0,h0], [f0,f1]x[h1,1]
        TryEmitOuterRect(verts, uvs, tris, outBi, outTi, outTn, outBn, vSeg0, vSeg1, uHeight, expectedOuterNormal, 0f, f0, 0f, 1f);
        TryEmitOuterRect(verts, uvs, tris, outBi, outTi, outTn, outBn, vSeg0, vSeg1, uHeight, expectedOuterNormal, f1, 1f, 0f, 1f);
        TryEmitOuterRect(verts, uvs, tris, outBi, outTi, outTn, outBn, vSeg0, vSeg1, uHeight, expectedOuterNormal, f0, f1, 0f, h0);
        TryEmitOuterRect(verts, uvs, tris, outBi, outTi, outTn, outBn, vSeg0, vSeg1, uHeight, expectedOuterNormal, f0, f1, h1, 1f);

        // Inner winding uses inBn, inTn, inTi, inBi with v reversed (vSeg1..vSeg0).
        TryEmitInnerRect(verts, uvs, tris, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, -expectedOuterNormal, 0f, f0, 0f, 1f);
        TryEmitInnerRect(verts, uvs, tris, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, -expectedOuterNormal, f1, 1f, 0f, 1f);
        TryEmitInnerRect(verts, uvs, tris, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, -expectedOuterNormal, f0, f1, 0f, h0);
        TryEmitInnerRect(verts, uvs, tris, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, -expectedOuterNormal, f0, f1, h1, 1f);
    }

    static void TryEmitOuterRect(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 outBi,
        Vector3 outTi,
        Vector3 outTn,
        Vector3 outBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        Vector3 expectedOuterNormal,
        float fA,
        float fB,
        float hA,
        float hB)
    {
        if (fB - fA < 1e-5f || hB - hA < 1e-5f)
            return;

        Vector3 a = EvalFace(outBi, outTi, outTn, outBn, fA, hA);
        Vector3 b = EvalFace(outBi, outTi, outTn, outBn, fA, hB);
        Vector3 c = EvalFace(outBi, outTi, outTn, outBn, fB, hB);
        Vector3 d = EvalFace(outBi, outTi, outTn, outBn, fB, hA);

        float u0 = hA * uHeight;
        float u1 = hB * uHeight;
        float v0 = Mathf.Lerp(vSeg0, vSeg1, fA);
        float v1 = Mathf.Lerp(vSeg0, vSeg1, fB);
        AddQuadTwoSided(verts, uvs, tris, a, b, c, d, u0, v0, u1, v1, expectedOuterNormal);
    }

    static void TryEmitInnerRect(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 inBi,
        Vector3 inTi,
        Vector3 inTn,
        Vector3 inBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        Vector3 expectedInnerNormal,
        float fA,
        float fB,
        float hA,
        float hB)
    {
        if (fB - fA < 1e-5f || hB - hA < 1e-5f)
            return;

        // Same (f,h) corner order as outer patches; normals face into the room.
        Vector3 a = EvalFace(inBi, inTi, inTn, inBn, fA, hA);
        Vector3 b = EvalFace(inBi, inTi, inTn, inBn, fA, hB);
        Vector3 c = EvalFace(inBi, inTi, inTn, inBn, fB, hB);
        Vector3 d = EvalFace(inBi, inTi, inTn, inBn, fB, hA);

        float u0 = hA * uHeight;
        float u1 = hB * uHeight;
        float v0 = Mathf.Lerp(vSeg0, vSeg1, fA);
        float v1 = Mathf.Lerp(vSeg0, vSeg1, fB);
        AddQuadTwoSided(verts, uvs, tris, a, b, c, d, u0, v0, u1, v1, expectedInnerNormal);
    }

    static Vector3 EvalFace(Vector3 bi, Vector3 ti, Vector3 tn, Vector3 bn, float f, float h)
    {
        Vector3 bot = Vector3.Lerp(bi, bn, f);
        Vector3 top = Vector3.Lerp(ti, tn, f);
        return Vector3.Lerp(bot, top, h);
    }

    static void AppendJambs(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 outBi,
        Vector3 outTi,
        Vector3 outTn,
        Vector3 outBn,
        Vector3 inBi,
        Vector3 inTi,
        Vector3 inTn,
        Vector3 inBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        float f0,
        float f1,
        float h0,
        float h1)
    {
        // Left / right vertical jambs at f0, f1
        EmitJambQuad(verts, uvs, tris, outBi, outTi, outTn, outBn, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, f0, h0, h1, true);
        EmitJambQuad(verts, uvs, tris, outBi, outTi, outTn, outBn, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, f1, h0, h1, false);

        // Bottom / top sill
        EmitSillQuad(verts, uvs, tris, outBi, outTi, outTn, outBn, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, f0, f1, h0, true);
        EmitSillQuad(verts, uvs, tris, outBi, outTi, outTn, outBn, inBi, inTi, inTn, inBn, vSeg0, vSeg1, uHeight, f0, f1, h1, false);
    }

    static void EmitJambQuad(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 outBi,
        Vector3 outTi,
        Vector3 outTn,
        Vector3 outBn,
        Vector3 inBi,
        Vector3 inTi,
        Vector3 inTn,
        Vector3 inBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        float f,
        float h0,
        float h1,
        bool isLeft)
    {
        Vector3 o0 = EvalFace(outBi, outTi, outTn, outBn, f, h0);
        Vector3 o1 = EvalFace(outBi, outTi, outTn, outBn, f, h1);
        Vector3 i0 = EvalFace(inBi, inTi, inTn, inBn, f, h0);
        Vector3 i1 = EvalFace(inBi, inTi, inTn, inBn, f, h1);

        float eps = 0.001f;
        float fN = Mathf.Clamp01(f + (isLeft ? -eps : eps));
        Vector3 o0n = EvalFace(outBi, outTi, outTn, outBn, fN, h0);
        Vector3 edge = o0n - o0;
        edge.y = 0f;
        Vector3 up = Vector3.up;
        Vector3 n = Vector3.Cross(up, edge);
        if (n.sqrMagnitude < 1e-8f)
            n = Vector3.Cross(Vector3.forward, edge);
        n.Normalize();
        if (!isLeft)
            n = -n;

        float vu0 = Mathf.Lerp(vSeg0, vSeg1, f);
        float uu0 = h0 * uHeight;
        float uu1 = h1 * uHeight;
        AddQuadOriented(verts, uvs, tris, o0, o1, i1, i0, uu0, vu0, uu1, vu0 + 0.01f, n);
        AddQuadOriented(verts, uvs, tris, o0, o1, i1, i0, uu0, vu0, uu1, vu0 + 0.01f, -n);
    }

    static void EmitSillQuad(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 outBi,
        Vector3 outTi,
        Vector3 outTn,
        Vector3 outBn,
        Vector3 inBi,
        Vector3 inTi,
        Vector3 inTn,
        Vector3 inBn,
        float vSeg0,
        float vSeg1,
        float uHeight,
        float f0,
        float f1,
        float h,
        bool isBottom)
    {
        Vector3 o0 = EvalFace(outBi, outTi, outTn, outBn, f0, h);
        Vector3 o1 = EvalFace(outBi, outTi, outTn, outBn, f1, h);
        Vector3 i0 = EvalFace(inBi, inTi, inTn, inBn, f0, h);
        Vector3 i1 = EvalFace(inBi, inTi, inTn, inBn, f1, h);

        float eps = 0.001f;
        float hN = Mathf.Clamp01(h + (isBottom ? -eps : eps));
        Vector3 o0n = EvalFace(outBi, outTi, outTn, outBn, f0, hN);
        Vector3 n = Vector3.Cross(o1 - o0, o0n - o0);
        if (n.sqrMagnitude < 1e-8f)
            return;
        n.Normalize();
        if (!isBottom)
            n = -n;

        float v0 = Mathf.Lerp(vSeg0, vSeg1, f0);
        float v1 = Mathf.Lerp(vSeg0, vSeg1, f1);
        float uu = h * uHeight;
        AddQuadOriented(verts, uvs, tris, o0, o1, i1, i0, uu, v0, uu + 0.01f, v1, n);
        AddQuadOriented(verts, uvs, tris, o0, o1, i1, i0, uu, v0, uu + 0.01f, v1, -n);
    }

    static void AddQuadTwoSided(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float u0,
        float v0,
        float u1,
        float v1,
        Vector3 expectedNormal)
    {
        AddQuadOriented(verts, uvs, tris, a, b, c, d, u0, v0, u1, v1, expectedNormal);
        AddQuadOriented(verts, uvs, tris, a, b, c, d, u0, v0, u1, v1, -expectedNormal);
    }

    static void AddQuadOriented(
        List<Vector3> verts,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float u0,
        float v0,
        float u1,
        float v1,
        Vector3 expectedNormal)
    {
        int start = verts.Count;

        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u0, v1));

        Vector3 triNormal = Vector3.Cross(b - a, c - a);
        bool sameDirection =
            expectedNormal.sqrMagnitude < 0.000001f ||
            triNormal.sqrMagnitude < 0.000001f ||
            Vector3.Dot(triNormal, expectedNormal) >= 0f;

        if (sameDirection)
        {
            tris.Add(start + 0);
            tris.Add(start + 1);
            tris.Add(start + 2);

            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }
        else
        {
            tris.Add(start + 0);
            tris.Add(start + 2);
            tris.Add(start + 1);

            tris.Add(start + 0);
            tris.Add(start + 3);
            tris.Add(start + 2);
        }
    }
}
