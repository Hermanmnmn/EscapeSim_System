using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI Text_Time;      // 時間顯示（格式：TIME: X s）
    public TextMeshProUGUI Text_Escaped;   // 逃生人數（格式：ESCAPED: X）
    public TextMeshProUGUI densityText;

    [Header("Controls Guide")]
    [Tooltip("拖入場景中的 TextMeshProUGUI 物件，用於顯示按鍵說明")]
    public TextMeshProUGUI controlsGuideText;

    [Header("Zone Sensor Reference")]
    public ZoneSensor zoneSensor; // 拖入場景中的 ZoneSensor 物件

    private const string CONTROLS_GUIDE =
        "[Space/SW]: Start/Pause  |  [P]: Spawn  |  [R]: Reset\n" +
        "[V]: Drone View  |  [F]: Free Cam  |  [C]: CCTV  |  [B/Fire Btn]: Toggle Transparency";

    void Start()
    {
        // 寫死按鍵說明，啟動後固定顯示
        if (controlsGuideText != null)
            controlsGuideText.text = CONTROLS_GUIDE;
    }

    void Update()
    {
        if (WorldState.Instance == null) return;

        // ── 狀態 (ARMED / LOCKED) ──────────────────────────────────────
        if (statusText != null)
        {
            bool armed = WorldState.Instance.Switch == 1;
            statusText.text = armed
                ? "SYSTEM: <color=green>ARMED</color>"
                : "SYSTEM: <color=red>LOCKED</color>";
        }

        // ── 計時器（即時顯示 SimulationTime 秒數）────────────────────
        if (Text_Time != null)
        {
            Text_Time.text = $"TIME: {Mathf.FloorToInt(WorldState.Instance.SimulationTime)} s";
        }

        // ── 已疏散人數（即時更新）─────────────────────────────────────
        if (Text_Escaped != null)
        {
            Text_Escaped.text = $"ESCAPED: {WorldState.Instance.EvacuatedCount}";
        }

        // ── 區域密度（從 ZoneSensor 獲取）────────────────────────────
        if (densityText != null)
        {
            int density = (zoneSensor != null) ? zoneSensor.CurrentAgentCount : 0;
            densityText.text = $"DENSITY: {density}";
        }
    }
}