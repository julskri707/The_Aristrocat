using UnityEngine;

/// <summary>
/// Runtime LOD for a generated cladding stone or quoin: swap between high- and low-triangle meshes
/// instead of disabling the renderer (Distant-Horizons-style distant detail). Quoins use a 6-face box LOD.
/// </summary>
[DisallowMultipleComponent]
public sealed class WallCladdingStoneLod : MonoBehaviour
{
    [SerializeField] MeshFilter targetFilter;
    [SerializeField] Mesh highDetailMesh;
    [SerializeField] Mesh lowDetailMesh;

    bool _usingLowDetail;

    public void Initialize(MeshFilter filter, Mesh high, Mesh low)
    {
        targetFilter = filter;
        highDetailMesh = high;
        lowDetailMesh = low;
        _usingLowDetail = false;
        if (targetFilter != null && highDetailMesh != null)
            targetFilter.sharedMesh = highDetailMesh;
    }

    /// <param name="forceLowFromHorizonDecimation">Thins the horizon without hiding: always low-poly.</param>
    public void ApplyLod(
        float distanceToCamera,
        float fullDetailWithin,
        float lowDetailBeyond,
        bool forceLowFromHorizonDecimation)
    {
        if (targetFilter == null || highDetailMesh == null || lowDetailMesh == null)
            return;

        if (forceLowFromHorizonDecimation)
        {
            _usingLowDetail = true;
        }
        else
        {
            fullDetailWithin = Mathf.Max(0.5f, fullDetailWithin);
            lowDetailBeyond = Mathf.Max(fullDetailWithin + 0.25f, lowDetailBeyond);

            if (!_usingLowDetail)
            {
                if (distanceToCamera >= lowDetailBeyond)
                    _usingLowDetail = true;
            }
            else
            {
                if (distanceToCamera <= fullDetailWithin)
                    _usingLowDetail = false;
            }
        }

        Mesh want = _usingLowDetail ? lowDetailMesh : highDetailMesh;
        if (want != null && targetFilter.sharedMesh != want)
            targetFilter.sharedMesh = want;
    }
}
