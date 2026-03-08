using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 區域密度感測器：掛在帶有 BoxCollider (isTrigger=true) 的空物件上，
/// 即時計算停留在該區域內的 CrowdAgent 數量。
/// 包含 Destroy 防呆：透過追蹤實際物件列表避免計數錯誤。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ZoneSensor : MonoBehaviour
{
    [Header("即時數據 (唯讀)")]
    public int CurrentAgentCount = 0;

    // 使用 HashSet 追蹤區域內的 Agent，防止 Destroy 導致計數錯誤
    private HashSet<Collider> agentsInZone = new HashSet<Collider>();

    void Start()
    {
        // 確保 Collider 設為 Trigger
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[ZoneSensor] {gameObject.name} 的 BoxCollider 已自動設為 Trigger");
        }
    }

    void Update()
    {
        // 安全保護：移除已被 Destroy 的物件，修正計數
        agentsInZone.RemoveWhere(c => c == null);
        CurrentAgentCount = agentsInZone.Count;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<CrowdAgent>() != null)
        {
            agentsInZone.Add(other);
            CurrentAgentCount = agentsInZone.Count;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<CrowdAgent>() != null)
        {
            agentsInZone.Remove(other);
            CurrentAgentCount = agentsInZone.Count;
        }
    }
}
