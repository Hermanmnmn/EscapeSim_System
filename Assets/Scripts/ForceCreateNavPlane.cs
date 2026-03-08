using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

public class ForceCreateNavPlane : Editor
{
    [MenuItem("Tools/老闆專用/生成導航專用平面")]
    public static void CreatePlane()
    {
        // 修正版：加上了 Axis.Up 參數
        ProBuilderMesh pbMesh = ShapeGenerator.GeneratePlane(
            PivotLocation.Center, 
            2f,        // Width 寬
            5f,        // Length 長
            1,         // Width Steps
            1,         // Length Steps
            Axis.Up    // 加上這個軸向設定
        );

        pbMesh.gameObject.name = "NavMesh_Patch_Slope";
        
        // 自動加上 Mesh Collider 確保 NavMesh 抓得到它
        if (!pbMesh.gameObject.GetComponent<MeshCollider>())
            pbMesh.gameObject.AddComponent<MeshCollider>();

        Selection.activeGameObject = pbMesh.gameObject;
        SceneView.lastActiveSceneView.FrameSelected();
        
        Debug.Log("老闆，導航平面已生成！這下絕對沒問題了。");
    }
}