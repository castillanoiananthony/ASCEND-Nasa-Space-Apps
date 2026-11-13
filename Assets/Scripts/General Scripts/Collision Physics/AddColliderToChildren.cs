using UnityEngine;

public class AddCollidersToChildren : MonoBehaviour
{
    void Start()
    {
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();
        foreach (var mf in meshFilters)
        {
            if (mf.GetComponent<Collider>() == null && mf.sharedMesh != null)
            {
                MeshCollider collider = mf.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mf.sharedMesh;
            }
        }
        Debug.Log("Colliders successfully generated for all child meshes!");
    }
}
