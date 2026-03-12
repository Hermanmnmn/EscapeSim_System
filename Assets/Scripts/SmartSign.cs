using UnityEngine;

/// <summary>
/// 零配置動態指示牌（Smart Sign）：取代舊版 DynamicSign。
/// 掛在分岔口的 Trigger Collider 上，當 CrowdAgent 進入觸發區時，
/// 自動計算到所有 ExitNode 的「總成本 = 距離 + 擁擠懲罰」，
/// 並覆寫 Agent 的目的地為成本最低的出口。
/// 無需手動設定任何 Default / Alternative Exit。
/// </summary>
[RequireComponent(typeof(Collider))]
public class SmartSign : MonoBehaviour
{
    [Header("即時狀態 (唯讀)")]
    [Tooltip("上一次觸發時，所選出的最佳出口名稱")]
    public string lastChosenExit = "None";

    void Start()
    {
        // 確保 Collider 設為 Trigger
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[SmartSign] {gameObject.name} 的 Collider 已自動設為 Trigger");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        CrowdAgent crowdAgent = other.GetComponent<CrowdAgent>();
        if (crowdAgent == null) return;

        // 1. 取得場景中所有出口節點
        ExitNode[] exits = FindObjectsByType<ExitNode>(FindObjectsSortMode.None);
        if (exits == null || exits.Length == 0)
        {
            Debug.LogWarning($"[SmartSign] {gameObject.name}: 場景中找不到任何 ExitNode！");
            return;
        }

        // 2. 核心演算法：找總成本最低的出口
        //    Total Cost = 直線距離 + 擁擠懲罰
        ExitNode bestExit = null;
        float bestCost = float.MaxValue;

        foreach (ExitNode exit in exits)
        {
            float dist = Vector3.Distance(other.transform.position, exit.transform.position);
            float penalty = exit.GetCongestionPenalty();
            float totalCost = dist + penalty;

            if (totalCost < bestCost)
            {
                bestCost = totalCost;
                bestExit = exit;
            }
        }

        // 3. 覆寫 Agent 目的地
        if (bestExit != null)
        {
            lastChosenExit = bestExit.gameObject.name;
            crowdAgent.ChangeDestination(bestExit.transform);
            Debug.Log($"[SmartSign] {gameObject.name} → Agent {crowdAgent.agentID} 導向 {bestExit.name} (cost={bestCost:F1})");
        }
    }
}
