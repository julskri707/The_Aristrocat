using UnityEngine;

[DisallowMultipleComponent]
public sealed class RoofCladdingRuntime : MonoBehaviour
{
    [SerializeField] private RoofCladdingProfile currentProfile;
    [SerializeField] private int currentSeed;
    [SerializeField] private bool dirty = true;
    [SerializeField] private Transform generatedRoot;

    public RoofCladdingProfile CurrentProfile => currentProfile;
    public int CurrentSeed => currentSeed;
    public bool IsDirty => dirty;

    public void SetProfile(RoofCladdingProfile profile, int seed)
    {
        currentProfile = profile;
        currentSeed = seed;
        dirty = true;
    }

    /// <summary>Assigne <paramref name="profile"/> uniquement si aucun profil n’est encore défini (défaut inspecteur sur <see cref="HouseRoofSystem"/>).</summary>
    /// <returns><c>true</c> si le profil a été assigné.</returns>
    public bool EnsureCurrentProfileIfEmpty(RoofCladdingProfile profile)
    {
        if (currentProfile != null || profile == null)
            return false;
        currentProfile = profile;
        dirty = true;
        return true;
    }

    public void MarkDirty() => dirty = true;
    public void MarkClean() => dirty = false;

    public Transform GetOrCreateRoot()
    {
        if (generatedRoot != null)
            return generatedRoot;

        const string childName = "GeneratedRoofCladding";
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            generatedRoot = existing;
            return generatedRoot;
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = gameObject.layer;
        generatedRoot = go.transform;
        return generatedRoot;
    }

    public void ClearRoot()
    {
        Transform root = generatedRoot;
        if (root == null)
        {
            Transform existing = transform.Find("GeneratedRoofCladding");
            if (existing == null)
                return;
            root = existing;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;
            MeshFilter[] mfs = child.GetComponentsInChildren<MeshFilter>(true);
            for (int j = 0; j < mfs.Length; j++)
            {
                MeshFilter mfChild = mfs[j];
                if (mfChild == null || mfChild.sharedMesh == null)
                    continue;
                Mesh owned = mfChild.sharedMesh;
                mfChild.sharedMesh = null;
                DestroyObjectSafe(owned);
            }
            DestroyObjectSafe(child);
        }

        MeshFilter mf = root.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Mesh owned = mf.sharedMesh;
            mf.sharedMesh = null;
            DestroyObjectSafe(owned);
        }

        MeshRenderer mr = root.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.enabled = false;
            mr.sharedMaterial = null;
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
