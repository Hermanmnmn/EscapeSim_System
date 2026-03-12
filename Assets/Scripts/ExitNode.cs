using UnityEngine;

/// <summary>
/// 掛在每個出口 GameObject 上，提供擁擠懲罰值給 SmartSign 演算法使用。
/// 不需要手動設定任何路徑，SmartSign 會自動抓取所有 ExitNode。
/// </summary>
public class ExitNode : MonoBehaviour
{
    [Header("擁擠懲罰設定")]
    [Tooltip("對應該出口的瓶頸感測器（掛在出口附近的 ZoneSensor）")]
    public ZoneSensor bottleneckSensor;

    [Tooltip("懲罰權重：每個 Agent 佔用的等效距離（公尺/人）")]
    public float penaltyWeight = 0.5f;

    /// <summary>
    /// 回傳此出口的擁擠懲罰值（等效距離加成）。
    /// 懲罰值 = 感測器內人數 × penaltyWeight。
    /// 若感測器未設定則回傳 0（純距離模式）。
    /// </summary>
    public float GetCongestionPenalty()
    {
        if (bottleneckSensor == null) return 0f;
        return bottleneckSensor.CurrentAgentCount * penaltyWeight;
    }
}
