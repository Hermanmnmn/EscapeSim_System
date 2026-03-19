using UnityEngine;
using UnityEditor;

public class DoorSetupEditor
{
    [MenuItem("Tools/Setup Doors for NavMesh")]
    public static void SetupDoors()
    {
        int modifiedCount = 0;
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains("Door") || obj.name.Contains("door"))
            {
                MeshCollider collider = obj.GetComponent<MeshCollider>();
                if (collider != null && !collider.isTrigger)
                {
                    // Unity 的 MeshCollider 若要設為 isTrigger，必須是 Convex，因此一併設定
                    collider.convex = true;
                    collider.isTrigger = true;
                    EditorUtility.SetDirty(obj);
                    modifiedCount++;
                }
            }
        }

        Debug.Log($"Door Setup Complete: {modifiedCount} doors modified to be triggers.");
    }
}
