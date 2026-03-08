using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 基於課桌椅位置的精確生成系統：
/// 自動抓取場景中所有 Tag 為 "SpawnDesk" 的桌子，
/// 在每張桌子旁最近的合法 NavMesh 位置生成一個 Agent。
/// </summary>
public class MultiZoneSpawner : MonoBehaviour
{
    [Header("生成設定")]
    public GameObject agentPrefab;              // CrowdAgent Prefab
    public float navMeshSampleRadius = 1.5f;    // NavMesh 取樣搜尋半徑

    /// <summary>
    /// 自動尋找所有 SpawnDesk，在每張桌子旁安全生成一個 Agent
    /// </summary>
    public void SpawnAll()
    {
        if (agentPrefab == null)
        {
            Debug.LogWarning("[MultiZoneSpawner] 缺少 Agent Prefab！");
            return;
        }

        // 1. 自動抓取場景中所有 Tag 為 "SpawnDesk" 的桌子
        GameObject[] desks = GameObject.FindGameObjectsWithTag("SpawnDesk");

        if (desks == null || desks.Length == 0)
        {
            Debug.LogWarning("[MultiZoneSpawner] 場景中找不到任何 Tag 為 'SpawnDesk' 的物件！請確認桌子已設定 Tag。");
            return;
        }

        int totalSpawned = 0;

        // 2. 一桌一人：針對每張桌子生成 1 個 Agent
        foreach (GameObject desk in desks)
        {
            if (desk == null) continue;

            // 3. NavMesh 安全驗證：尋找桌子周圍最近的合法地板位置
            NavMeshHit hit;
            if (NavMesh.SamplePosition(desk.transform.position, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                Instantiate(agentPrefab, hit.position, Quaternion.identity);
                totalSpawned++;
            }
            else
            {
                Debug.LogWarning($"[MultiZoneSpawner] {desk.name} 周圍 {navMeshSampleRadius}m 內找不到合法 NavMesh 位置，跳過生成");
            }
        }

        Debug.Log($"[MultiZoneSpawner] 成功在 {totalSpawned} 張桌子旁生成了 Agent（共找到 {desks.Length} 張桌子）");
    }
}
