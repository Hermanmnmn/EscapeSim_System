using System.Collections;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ExperimentRunner : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════════════
    // 目標函數權重 (Objective Function Weights)
    // Score = TotalEvacTime + Alpha * AvgPeakCrowding + Beta * TotalStuckTime
    // 分數越低越好（類似最小化成本函數）
    // ══════════════════════════════════════════════════════════════════
    [Header("目標函數權重")]
    public float Alpha = 2.0f;   // 平均最大擁擠度的懲罰係數
    public float Beta  = 1.5f;   // 小人卡住總時間的懲罰係數

    [Header("實驗參數陣列 (若有 config.json 則被覆蓋)")]
    public int[] populations = { 100, 200, 300 }; // 測試人數陣列
    public int[] seeds = { 1, 2, 3 };             // 測試亂數種子陣列

    [Header("邊際效益實驗：啟用的 SmartSign 數量")]
    public int[] activeSignCounts = { 0, 2, 5, 10 }; // 每組實驗啟用的指示牌數量

    [Header("控制器")]
    public SystemManager systemManager;
    public DataLogger dataLogger;
    public MultiZoneSpawner spawner;

    public float maxSimulationTime = 300f; // 大超時強制結束 (防卡死)
    public float pathWaitTimeout = 30f;    // 路徑預算防呆超時 (開放給用戶自行調整)

    /// <summary>單次實驗牆鐘上限（秒），含生成與路徑預算；與外部 Python --timeout 對齊。</summary>
    public const float TIMEOUT_LIMIT = 1200f;

    const float HeartbeatStallSeconds = 10f;
    /// <summary>剩餘 ≤3 人時放寬心跳，避免最後幾人在門口擠撞時被誤判卡死。</summary>
    const float HeartbeatStallSecondsLastFew = 45f;
    const float HeartbeatGraceSimulationTime = 5f;

    // ── 黑箱模式旗標 ──────────────────────────────────────────────────
    // 當 config.json 存在時，切換為「單次黑箱執行」，跑完直接輸出 result.json 並退出。
    private bool _headlessMode = false;

    // ── config.json 覆寫值 ────────────────────────────────────────────
    private int   _cfgActiveSigns = -1;   // -1 = 未設定，使用陣列
    private float _cfgAgentRadius = -1f;
    private float _cfgAgentSpeed  = -1f;

    private int _popIndex = 0;
    private int _seedIndex = 0;
    private int _signIndex = 0;
    private bool _isExperimentRunning = false;

    // ══════════════════════════════════════════════════════════════════
    // 簡易 JSON 容器（避免引入 Newtonsoft.Json 依賴）
    // ══════════════════════════════════════════════════════════════════
    [System.Serializable]
    private class SimConfig
    {
        public int   activeSigns  = -1;
        public float agentRadius  = -1f;
        public float agentSpeed   = -1f;
    }

    [System.Serializable]
    private class SimResult
    {
        public float score;
        public float totalEvacTime;
        public float avgPeakCrowding;
        public float totalStuckTime;
        public float alpha;
        public float beta;
        public int   activeSigns;
        public float agentRadius;
        public float agentSpeed;
        public int   population;
        public int   seed;
    }

    // ══════════════════════════════════════════════════════════════════
    void Start()
    {
        // ── 嘗試讀取 config.json（專案根目錄 = Assets 的上一層）────────
        string configPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "config.json"));

        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                SimConfig cfg = JsonUtility.FromJson<SimConfig>(json);

                _cfgActiveSigns = cfg.activeSigns;
                _cfgAgentRadius = cfg.agentRadius;
                _cfgAgentSpeed  = cfg.agentSpeed;

                Debug.Log($"[ExperimentRunner] 讀取 config.json 成功 → " +
                          $"activeSigns={_cfgActiveSigns}, " +
                          $"agentRadius={_cfgAgentRadius}, " +
                          $"agentSpeed={_cfgAgentSpeed}");

                _headlessMode = true; // 進入黑箱模式：只跑一次，跑完退出
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ExperimentRunner] 解析 config.json 失敗: {e.Message}，使用陣列預設值。");
            }
        }
        else
        {
            Debug.Log($"[ExperimentRunner] 未找到 config.json（路徑: {configPath}），使用陣列模式。");
        }

        SystemManager.IsAutoMode = true;
        StartCoroutine(RunAllExperiments());
    }

    public bool IsRunning()
    {
        return _isExperimentRunning;
    }

    /// <summary>
    /// 透過 UI 觸發或外部腳本呼叫開始一連串的自動化實驗
    /// </summary>
    public void StartExperiments()
    {
        if (_isExperimentRunning) return;
        StartCoroutine(RunAllExperiments());
    }

    // ══════════════════════════════════════════════════════════════════
    // 主要實驗迴圈
    // ══════════════════════════════════════════════════════════════════
    private IEnumerator RunAllExperiments()
    {
        _isExperimentRunning = true;

        // 奪取控制權：自動尋找並關閉 SerialController，防止硬體訊號干擾
        var serialController = FindAnyObjectByType<SerialController>();
        if (serialController != null)
        {
            serialController.enabled = false;
            Debug.Log("[ExperimentRunner] 已自動停用 SerialController，防止硬體訊號干擾。");
        }

        // ── 黑箱模式：把 config 覆寫進陣列，讓後面的迴圈只跑一次 ─────
        if (_headlessMode)
        {
            if (_cfgActiveSigns >= 0) activeSignCounts = new int[] { _cfgActiveSigns };
            // agentRadius / agentSpeed 由下方 SpawnAll 前套用
        }

        int totalRuns = populations.Length * seeds.Length * activeSignCounts.Length;
        Debug.Log($"[ExperimentRunner] 實驗開始！共 {totalRuns} 組測試（人數×種子×指示牌數量）。");

        // 取得場景中所有 SmartSign，避免每次都 FindObjects
        SmartSign[] allSigns = FindObjectsByType<SmartSign>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (_popIndex = 0; _popIndex < populations.Length; _popIndex++)
        {
            int targetPopulation = populations[_popIndex];

            for (_seedIndex = 0; _seedIndex < seeds.Length; _seedIndex++)
            {
                int currentSeed = seeds[_seedIndex];

                for (_signIndex = 0; _signIndex < activeSignCounts.Length; _signIndex++)
                {
                    int signCount = activeSignCounts[_signIndex];

                    Debug.Log($"[ExperimentRunner] 準備執行 → 人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount}");

                    // ── 指示牌控制：依序啟用前 N 個，其餘關閉 ──────────────────
                    for (int i = 0; i < allSigns.Length; i++)
                    {
                        allSigns[i].gameObject.SetActive(i < signCount);
                    }

                    // Step 1: 清場 (清除場上所有的 CrowdAgent)
                    CrowdAgent[] oldAgents = FindObjectsByType<CrowdAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var a in oldAgents) Destroy(a.gameObject);
                    WorldState.Instance.EvacuatedCount = 0;
                    WorldState.Instance.ActiveAgentCount = 0;

                    // ── 重置目標函數累積器 ──────────────────────────────────────
                    WorldState.Instance.PeakCrowdingAccumulator = 0f;
                    WorldState.Instance.CrowdingSampleCount = 0;
                    WorldState.Instance.TotalStuckTime = 0f;

                    // 等待一幀確保舊物件被完全 Destroy 掉
                    yield return null;

                    // Step 2: 設定變因
                    WorldState.Instance.RandomSeed = currentSeed;
                    UnityEngine.Random.InitState(currentSeed);

                    SystemManager.IsSimulationActive = false;
                    WorldState.Instance.Switch = 0;

                    MultiZoneSpawner foundSpawner = FindAnyObjectByType<MultiZoneSpawner>();

                    // Step 3: 生成
                    if (foundSpawner != null)
                    {
                        foundSpawner.SpawnAll(targetPopulation);
                    }

                    yield return null;

                    // ── 黑箱模式：套用 config 的 agentRadius / agentSpeed ───────
                    if (_headlessMode && (_cfgAgentRadius > 0f || _cfgAgentSpeed > 0f))
                    {
                        CrowdAgent[] freshAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);
                        foreach (var a in freshAgents)
                        {
                            var nav = a.GetComponent<UnityEngine.AI.NavMeshAgent>();
                            if (nav == null) continue;
                            if (_cfgAgentRadius > 0f) nav.radius = _cfgAgentRadius;
                            if (_cfgAgentSpeed  > 0f) nav.speed  = _cfgAgentSpeed;
                        }
                        Debug.Log($"[ExperimentRunner] 已套用 config 參數到 {freshAgents.Length} 個 Agent。");
                    }

                    // Step 4: 分批路徑預算 ──────────────────────────────────────
                    CrowdAgent[] allAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);
                    int totalAgents = allAgents.Length;

                    const int BATCH_SIZE = 30;
                    int calculated = 0;
                    Debug.Log($"[ExperimentRunner] 開始分批路徑預算...（共 {totalAgents} 個 Agent，每幀 {BATCH_SIZE} 個）");

                    float pathCalcStartTime = Time.realtimeSinceStartup;

                    for (int i = 0; i < totalAgents; i++)
                    {
                        allAgents[i].CalculatePathNow();
                        calculated++;
                        if (calculated % BATCH_SIZE == 0)
                        {
                            yield return null;
                        }
                    }
                    yield return null;

                    float pathWaitTimer = 0f;
                    while (pathWaitTimer < pathWaitTimeout)
                    {
                        bool allReady = true;
                        foreach (var a in allAgents)
                        {
                            if (a != null && a.gameObject.activeInHierarchy && !a.IsPathReady)
                            {
                                allReady = false;
                                break;
                            }
                        }
                        if (allReady) break;
                        pathWaitTimer += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    float totalCalcTime = Time.realtimeSinceStartup - pathCalcStartTime;

                    if (pathWaitTimer >= pathWaitTimeout)
                        Debug.LogWarning($"[ExperimentRunner] 路徑預算超時（{pathWaitTimeout} 秒），強制開始模擬。");
                    Debug.Log($"[ExperimentRunner] 所有 Agent 路徑就緒！（{totalAgents} 個，耗時約 {totalCalcTime:F2} 秒）");

                    // Step 5: 鳴槍起跑 ────────────────────────────────────────────
                    Debug.Log($"[ExperimentRunner] 執行真．同步起跑準備！啟動 {totalAgents} 個 Agent 的路徑灌入協程");
                    foreach (var a in allAgents)
                    {
                        if (a != null && a.gameObject.activeInHierarchy)
                            a.TrueSyncStart();
                    }

                    yield return new WaitForSecondsRealtime(0.25f);

                    WorldState.Instance.SimulationTime = 0f;
                    WorldState.Instance.Switch = 1;
                    SystemManager.IsSimulationActive = true;
                    Debug.Log($"[ExperimentRunner] 所有 Agent 路徑發射完畢，模擬計時正式開始。");

                    foreach (var a in allAgents)
                    {
                        if (a != null && a.gameObject.activeInHierarchy)
                            a.ReleaseBrake();
                    }

                    // 攝影機 & 透明模式
                    CameraFollow camFollow = Camera.main?.GetComponent<CameraFollow>();
                    if (camFollow != null)
                        camFollow.currentMode = CameraFollow.CameraMode.Free;

                    if (systemManager != null && !WorldState.Instance.IsTransparentMode)
                        systemManager.ToggleSchoolTransparency();

                    if (systemManager != null)
                        systemManager.SetSimulationSpeed(systemManager.simulationTimeScale);

                    if (dataLogger != null)
                        dataLogger.StartLogging(targetPopulation, currentSeed, signCount);

                    // ── 取得 ZoneSensor，供擁擠度採樣使用 ───────────────────────
                    ZoneSensor[] zoneSensors = FindObjectsByType<ZoneSensor>(FindObjectsSortMode.None);

                    // Step 6: 等待結束 (含擁擠度採樣) ──────────────────────────
                    float idleTimer = 0f;
                    CrowdAgent[] spawnedAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);

                    float phaseRealtimeStart = Time.realtimeSinceStartup;
                    int lastHeartbeatEvac = WorldState.Instance.EvacuatedCount;
                    float evacStallAccum = 0f;

                    while (WorldState.Instance.EvacuatedCount < targetPopulation)
                    {
                        // ── 每幀採樣最大擁擠度 ────────────────────────────────────
                        int maxCongestion = 0;
                        foreach (var z in zoneSensors)
                        {
                            if (z.CurrentAgentCount > maxCongestion)
                                maxCongestion = z.CurrentAgentCount;
                        }
                        WorldState.Instance.PeakCrowdingAccumulator += maxCongestion;
                        WorldState.Instance.CrowdingSampleCount++;

                        // 靜止超時偵測
                        bool isAnyAgentMoving = false;
                        foreach (var a in spawnedAgents)
                        {
                            if (a.gameObject.activeInHierarchy && a.GetCurrentSpeed() > 0.1f)
                            {
                                isAnyAgentMoving = true;
                                break;
                            }
                        }

                        if (isAnyAgentMoving)
                            idleTimer = 0f;
                        else
                            idleTimer += Time.deltaTime;

                        if (idleTimer >= maxSimulationTime) break;

                        if (Time.realtimeSinceStartup - phaseRealtimeStart >= TIMEOUT_LIMIT)
                        {
                            Debug.LogWarning($"[ExperimentRunner] 已達牆鐘 TIMEOUT_LIMIT ({TIMEOUT_LIMIT}s)，強制結束本輪實驗。" +
                                             $"(人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount})");
                            break;
                        }

                        if (WorldState.Instance.SimulationTime >= HeartbeatGraceSimulationTime)
                        {
                            if (WorldState.Instance.EvacuatedCount != lastHeartbeatEvac)
                            {
                                lastHeartbeatEvac = WorldState.Instance.EvacuatedCount;
                                evacStallAccum = 0f;
                            }
                            else
                                evacStallAccum += Time.unscaledDeltaTime;

                            int remaining = targetPopulation - WorldState.Instance.EvacuatedCount;
                            float stallLimit = (remaining <= 3 && remaining > 0)
                                ? HeartbeatStallSecondsLastFew
                                : HeartbeatStallSeconds;

                            if (evacStallAccum >= stallLimit)
                            {
                                Debug.LogWarning("[ExperimentRunner] 心跳：EvacuatedCount 已 " + stallLimit +
                                                 " 秒未變化，視為逃生卡死，強制結束並寫入紀錄。" +
                                                 $"(人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount})");
                                break;
                            }
                        }

                        yield return null;
                    }

                    if (WorldState.Instance.EvacuatedCount >= targetPopulation)
                    {
                        Debug.Log($"[ExperimentRunner] 實驗順利完成 (人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount}), " +
                                  $"耗時: {WorldState.Instance.SimulationTime:F2}s");
                    }
                    else if (idleTimer >= maxSimulationTime)
                    {
                        Debug.LogWarning($"[ExperimentRunner] 實驗強制結束 (全員靜止超過 {maxSimulationTime} 秒)。" +
                                         $"(人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount})");
                    }

                    // Step 7: 記錄 CSV
                    if (dataLogger != null)
                        dataLogger.SaveAndCloseLogs();

                    // ── 計算目標函數分數 ──────────────────────────────────────────
                    float totalEvacTime   = WorldState.Instance.SimulationTime;
                    float avgPeakCrowding = WorldState.Instance.CrowdingSampleCount > 0
                        ? WorldState.Instance.PeakCrowdingAccumulator / WorldState.Instance.CrowdingSampleCount
                        : 0f;
                    float totalStuckTime  = WorldState.Instance.TotalStuckTime;
                    float score = totalEvacTime + Alpha * avgPeakCrowding + Beta * totalStuckTime;

                    Debug.Log($"[ExperimentRunner] ★ Score = {score:F3} " +
                              $"(EvacTime={totalEvacTime:F2}, " +
                              $"Alpha({Alpha})×AvgCrowd({avgPeakCrowding:F2})={Alpha*avgPeakCrowding:F2}, " +
                              $"Beta({Beta})×StuckTime({totalStuckTime:F2})={Beta*totalStuckTime:F2})");

                    // ── 輸出 result.json ──────────────────────────────────────────
                    SaveResultJson(score, totalEvacTime, avgPeakCrowding, totalStuckTime,
                                   targetPopulation, currentSeed, signCount);

                    // 關閉模擬狀態
                    WorldState.Instance.Switch = 0;
                    SystemManager.IsSimulationActive = false;

                    // ── 黑箱模式：寫完後直接退出，不等待下一組 ───────────────────
                    if (_headlessMode)
                    {
                        Debug.Log("[ExperimentRunner] 黑箱模式完成，正在退出...");
                        QuitApplication();
                        yield break;
                    }

                    // 等待幾秒後再進入下一組
                    yield return new WaitForSecondsRealtime(2f);
                }
            }
        }

        // 實驗結束後，重新啟用所有 SmartSign（恢復場景預設狀態）
        foreach (var sign in allSigns)
        {
            sign.gameObject.SetActive(true);
        }

        _isExperimentRunning = false;
        Debug.Log("[ExperimentRunner] 所有實驗組合已全部執行完畢！");
    }

    // ══════════════════════════════════════════════════════════════════
    // 輸出 result.json 到專案根目錄
    // ══════════════════════════════════════════════════════════════════
    private void SaveResultJson(float score, float totalEvacTime, float avgPeakCrowding,
                                float totalStuckTime, int population, int seed, int signCount)
    {
        SimResult result = new SimResult
        {
            score           = score,
            totalEvacTime   = totalEvacTime,
            avgPeakCrowding = avgPeakCrowding,
            totalStuckTime  = totalStuckTime,
            alpha           = Alpha,
            beta            = Beta,
            activeSigns     = signCount,
            agentRadius     = _cfgAgentRadius > 0f ? _cfgAgentRadius : -1f,
            agentSpeed      = _cfgAgentSpeed  > 0f ? _cfgAgentSpeed  : -1f,
            population      = population,
            seed            = seed
        };

        string json = JsonUtility.ToJson(result, true); // prettyPrint=true

        // 寫到專案根目錄（與 Assets/ 同層）
        string resultPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "result.json"));

        try
        {
            File.WriteAllText(resultPath, json);
            Debug.Log($"[ExperimentRunner] result.json 已寫出 → {resultPath}\n{json}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ExperimentRunner] 寫出 result.json 失敗: {e.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 跨平台退出（編輯器 & 建置版本兼容）
    // ══════════════════════════════════════════════════════════════════
    private void QuitApplication()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
