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

            // Runtime stones create meshes with new Mesh(); release them explicitly before destroying GOs.
            MeshFilter[] mfs = child.GetComponentsInChildren<MeshFilter>(true);
            for (int j = 0; j < mfs.Length; j++)
            {
                MeshFilter mfChild = mfs[j];
                if (mfChild == null || mfChild.sharedMesh == null)
                    continue;

                Mesh ownedMesh = mfChild.sharedMesh;
                mfChild.sharedMesh = null;
                DestroyObjectSafe(ownedMesh);
            }

            // Combined mode allocates cloned materials; free them on merged holder destruction.
            if (child.name == "MergedWallCladding")
            {
                MeshRenderer mr = child.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Material[] mats = mr.sharedMaterials;
                    mr.sharedMaterials = new Material[0];
                    for (int j = 0; j < mats.Length; j++)
                        DestroyObjectSafe(mats[j]);
                }
            }

            DestroyObjectSafe(child);
        }

        MeshFilter mf = root.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Mesh ownedMesh = mf.sharedMesh;
            mf.sharedMesh = null;
            DestroyObjectSafe(ownedMesh);
        }
    }

    static void DestroyObjectSafe(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}
