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

    /// <summary>
    /// 對場景中所有 SpawnDesk 進行生成。
    /// 保證同一個桌子最多只生成一個 Agent，避免疊羅漢。
    /// 若 targetCount 為 -1，則在所有桌子都生成；若有指定，則隨機挑選對應數量的桌子生成。
    /// </summary>
    public void SpawnAll(int targetCount = -1)
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

        // 優化邏輯：洗牌法 (Fisher-Yates Shuffle) 打亂桌子陣列，保證隨機且不重複
        for (int i = 0; i < desks.Length; i++)
        {
            int swapIndex = Random.Range(i, desks.Length);
            GameObject temp = desks[i];
            desks[i] = desks[swapIndex];
            desks[swapIndex] = temp;
        }

        int totalToSpawn = targetCount == -1 ? desks.Length : Mathf.Min(targetCount, desks.Length);
        int totalSpawned = 0;
        int nextAgentID = 1;

        for (int i = 0; i < totalToSpawn; i++)
        {
            GameObject desk = desks[i];
            
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
        }
        
        WorldState.Instance.ActiveAgentCount = totalSpawned;
        Debug.Log($"[MultiZoneSpawner] 成功生成了 {totalSpawned} 名 Agent (桌子總數: {desks.Length})");
    }
}
