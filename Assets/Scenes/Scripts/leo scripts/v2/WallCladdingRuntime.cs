using UnityEngine;

[DisallowMultipleComponent]
public sealed class WallCladdingRuntime : MonoBehaviour
{
    [SerializeField] private WallCladdingProfile currentProfile;
    [SerializeField] private int currentSeed;
    [SerializeField] private bool dirty = true;
    [SerializeField] private int lastGeometryHash;

    [SerializeField] private Transform outsideRoot;
    [SerializeField] private Transform insideRoot;

    public WallCladdingProfile CurrentProfile => currentProfile;
    public int CurrentSeed => currentSeed;
    public bool IsDirty => dirty;
    public int LastGeometryHash { get => lastGeometryHash; set => lastGeometryHash = value; }
    public Transform OutsideRoot => outsideRoot;
    public Transform InsideRoot => insideRoot;

    public void SetProfile(WallCladdingProfile profile, int seed)
    {
        currentProfile = profile;
        currentSeed = seed;
        dirty = true;
    }

    public void MarkDirty() => dirty = true;
    public void MarkClean() => dirty = false;

    public Transform GetOrCreateRoot(bool outside)
    {
        Transform target = outside ? outsideRoot : insideRoot;
        if (target != null)
            return target;

        string childName = outside ? "GeneratedWallCladding_Outside" : "GeneratedWallCladding_Inside";
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            if (outside) outsideRoot = existing; else insideRoot = existing;
            return existing;
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        if (outside) outsideRoot = go.transform;
        else insideRoot = go.transform;
        return go.transform;
    }

    public void ClearRoot(bool outside)
    {
        Transform root = outside ? outsideRoot : insideRoot;
        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;
            if (Application.isPlaying) Object.Destroy(child);
            else Object.DestroyImmediate(child);
        }

        MeshFilter mf = root.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            mf.sharedMesh.Clear();
    }
}
