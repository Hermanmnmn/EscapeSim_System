using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 基於課桌椅位置的精確生成系統：
/// 自動抓取場景中所有 Tag 為 "SpawnDesk" 的桌子，
/// 隨機分配 Agent 到各桌子旁直到達到指定生成數量。
/// </summary>
public class MultiZoneSpawner : MonoBehaviour
{
    [Header("生成設定")]
    public GameObject agentPrefab;              // CrowdAgent Prefab
    public float navMeshSampleRadius = 1.5f;    // NavMesh 取樣搜尋半徑
    public int spawnCount = 100;                // 每次生成的目標人數

    /// <summary>
    /// 自動尋找所有 SpawnDesk，並隨機生成直到滿指定人數 (spawnCount)
    /// </summary>
    public void SpawnAll()
    {
        if (agentPrefab == null)
        {
            Debug.LogWarning("[MultiZoneSpawner] 缺少 Agent Prefab！");
            return;
        }

        GameObject[] desks = GameObject.FindGameObjectsWithTag("SpawnDesk");

        if (desks == null || desks.Length == 0)
        {
            Debug.LogWarning("[MultiZoneSpawner] 場景中找不到任何 Tag 為 'SpawnDesk' 的物件！請確認桌子已設定 Tag。");
            return;
        }

        int totalSpawned = 0;
        int nextAgentID = 1;

        // 隨機挑選桌子生成直到滿人
        while (totalSpawned < spawnCount)
        {
            GameObject desk = desks[Random.Range(0, desks.Length)];
            
            NavMeshHit hit;
            if (NavMesh.SamplePosition(desk.transform.position, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                GameObject newAgent = Instantiate(agentPrefab, hit.position, Quaternion.identity);
                
                // 寫入個體追蹤資訊
                CrowdAgent ca = newAgent.GetComponent<CrowdAgent>();
                if (ca != null)
                {
                    ca.agentID = nextAgentID++;
                    // 如果桌子有自定義的 Zone 標記就吃該標記，否則用名字
                    ca.spawnZone = desk.name; 
                }
                
                totalSpawned++;
            }
            else
            {
                // 若找不到位置就先跳過，下一個 Loop 再隨機抽
                continue;
            }
        }
        
        WorldState.Instance.ActiveAgentCount = totalSpawned;
        Debug.Log($"[MultiZoneSpawner] 成功在桌子旁生成了 {totalSpawned} 名 Agent");
    }
}
