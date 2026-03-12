using System.Collections;
using UnityEngine;

public class ExperimentRunner : MonoBehaviour
{
    [Header("實驗參數陣列")]
    public int[] populations = { 100, 200, 300 }; // 測試人數陣列
    public int[] seeds = { 1, 2, 3 };             // 測試亂數種子陣列

    [Header("邊際效益實驗：啟用的 SmartSign 數量")]
    public int[] activeSignCounts = { 0, 2, 5, 10 }; // 每組實驗啟用的指示牌數量

    [Header("控制器")]
    public SystemManager systemManager;
    public DataLogger dataLogger;
    public MultiZoneSpawner spawner;
    
    public float maxSimulationTime = 300f; // 大超時強制結束 (防卡死)

    private int _popIndex = 0;
    private int _seedIndex = 0;
    private int _signIndex = 0;
    private bool _isExperimentRunning = false;

    void Start()
    {
        // 自動在啟動時執行實驗
        SystemManager.IsAutoMode = true; // 強制接管狀態
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
        
        int totalRuns = populations.Length * seeds.Length * activeSignCounts.Length;
        Debug.Log($"[ExperimentRunner] 實驗開始！共 {totalRuns} 組測試（人數×種子×指示牌數量）。");

        // 取得場景中所有 SmartSign，避免每次都 FindObjects
        SmartSign[] allSigns = FindObjectsByType<SmartSign>(FindObjectsInactive.Include, FindObjectsSortMode.None); // true = 包含 inactive

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
                    var agents = GameObject.FindGameObjectsWithTag("CrowdAgent");
                    foreach (var a in agents) Destroy(a);
                    WorldState.Instance.EvacuatedCount = 0;
                    
                    // 等待一幀確保舊物件被完全 Destroy 掉
                    yield return null; 

                    // Step 2: 設定變因
                    WorldState.Instance.RandomSeed = currentSeed;
                    UnityEngine.Random.InitState(currentSeed);
                    
                    MultiZoneSpawner foundSpawner = FindAnyObjectByType<MultiZoneSpawner>();
                    if (foundSpawner != null)
                    {
                        foundSpawner.spawnCount = targetPopulation;
                    }

                    // Step 3: 生成
                    if (foundSpawner != null)
                    {
                        foundSpawner.SpawnAll();
                    }

                    // Step 4: 路徑預熱 (Path Pre-warming) ─────────────────────────────
                    // Agent 在 Start() 中已送出 SetDestination，NavMesh 正在背景算路。
                    // 這 10 秒是黃金預熱視窗，讓 CPU 消化所有 SetDestination 的計算，
                    // 確保每一個 Agent 起跑前路徑都已就緒，消除「擠牙膏效應」。
                    // 期間 Switch=0、IsSimulationActive=false，Agent 腳黏在地上不動。
                    Debug.Log($"[ExperimentRunner] 路徑預熱中...（{targetPopulation} 個 Agent，等待 10 秒）");
                    yield return new WaitForSecondsRealtime(10.0f);

                    // Step 5: 鳴槍起跑 ────────────────────────────────────────────────
                    // 預熱完畢，重置計時器並同步解除所有 Agent 煞車。
                    WorldState.Instance.SimulationTime = 0f;
                    WorldState.Instance.Switch = 1;         
                    SystemManager.IsSimulationActive = true; // Agent 的 Update() 監聽到此 flag 即自動起跑

                    // 恢復 SystemManager 設定的速度倍率
                    if (systemManager != null)
                        systemManager.SetSimulationSpeed(systemManager.simulationTimeScale);
                    else
                        Time.timeScale = 1f;
                    
                    if (dataLogger != null)
                    {
                        dataLogger.StartLogging(targetPopulation, currentSeed);
                    }

                    // Step 6: 等待結束 (防卡死機制：改為靜止超時偵測) ────────────────
                    // 如果場上全數小人卡住不動超過 maxSimulationTime 秒，才強制結束。
                    float idleTimer = 0f;
                    CrowdAgent[] spawnedAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);

                    while (WorldState.Instance.EvacuatedCount < targetPopulation)
                    {
                        // 檢查場上是否還有移動中的小人
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
                        {
                            idleTimer = 0f; // 有人還在動，重置超時倒數
                        }
                        else
                        {
                            idleTimer += Time.deltaTime; // 全員靜止，開始倒數
                        }

                        if (idleTimer >= maxSimulationTime)
                        {
                            break; // 靜止超時，強制結束
                        }

                        yield return null; // 每一幀檢查
                    }

                    if (idleTimer >= maxSimulationTime)
                    {
                        Debug.LogWarning($"[ExperimentRunner] 實驗強制結束 (全員靜止超過 {maxSimulationTime} 秒)。(人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount})");
                    }
                    else
                    {
                        Debug.Log($"[ExperimentRunner] 實驗順利完成 (人數: {targetPopulation}, 種子: {currentSeed}, 指示牌: {signCount}), 耗時: {WorldState.Instance.SimulationTime:F2}s");
                    }

                    // Step 7: 記錄與換場
                    if (dataLogger != null)
                    {
                        dataLogger.SaveAndCloseLogs();
                    }

                    // 關閉模擬狀態
                    WorldState.Instance.Switch = 0;
                    SystemManager.IsSimulationActive = false;
                    Time.timeScale = 0f;
                    Time.fixedDeltaTime = 0.02f; // 重置物理時步

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
}
