using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 動態指示牌（多出口負載均衡版）：掛在分岔路口的觸發器上，
/// 根據監控區域的人群密度，以 Round-Robin 輪流分發方式
/// 將人群均衡導向多個備用出口，避免二次擁堵。
/// </summary>
[RequireComponent(typeof(Collider))]
public class DynamicSign : MonoBehaviour
{
    [Header("出口設定")]
    public Transform DefaultExit;                       // 預設出口（正常時導向）
    public List<Transform> AlternativeExits;            // 備用出口列表（擁擠時輪流分配）

    [Header("感測器設定")]
    public ZoneSensor monitoredZone;                    // 監控的區域感測器
    public int crowdThreshold = 50;                     // 擁擠判定閾值

    [Header("即時狀態 (唯讀)")]
    public bool isDiverting = false;                    // 目前是否啟動分流

    private int nextExitIndex = 0;                      // Round-Robin 輪流分配索引

    void Start()
    {
        // 確保 Collider 設為 Trigger
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[DynamicSign] {gameObject.name} 的 Collider 已自動設為 Trigger");
        }

        if (monitoredZone == null)
            Debug.LogWarning($"[DynamicSign] {gameObject.name} 未指定 monitoredZone！");
        if (DefaultExit == null)
            Debug.LogWarning($"[DynamicSign] {gameObject.name} 未指定 DefaultExit！");
        if (AlternativeExits == null || AlternativeExits.Count == 0)
            Debug.LogWarning($"[DynamicSign] {gameObject.name} 未指定任何 AlternativeExits！");
    }

    void Update()
    {
        // 根據監控區域的人數決定是否啟動分流
        if (monitoredZone != null)
        {
            isDiverting = monitoredZone.CurrentAgentCount > crowdThreshold;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        CrowdAgent crowdAgent = other.GetComponent<CrowdAgent>();
        if (crowdAgent == null) return;

        if (isDiverting && AlternativeExits != null && AlternativeExits.Count > 0)
        {
            // 擁擠：Round-Robin 輪流分發到各備用出口
            Transform selectedExit = AlternativeExits[nextExitIndex];
            nextExitIndex = (nextExitIndex + 1) % AlternativeExits.Count;

            if (selectedExit != null)
                crowdAgent.ChangeDestination(selectedExit);
        }
        else if (DefaultExit != null)
        {
            // 正常：導向預設出口
            crowdAgent.ChangeDestination(DefaultExit);
        }
    }
}
