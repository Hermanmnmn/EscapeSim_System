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
    public float pathWaitTimeout = 30f; // 路徑預算防呆超時 (開放給用戶自行調整)

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
                    // ★ 使用 FindObjectsByType 並加上 FindObjectsInactive.Include，
                    //   這樣才能找到之前透過 SetActive(false) 逃生的小人！
                    //   舊方法 FindGameObjectsWithTag 會完全错過這些小人，導致它們累積左偉。
                    CrowdAgent[] oldAgents = FindObjectsByType<CrowdAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var a in oldAgents) Destroy(a.gameObject);
                    WorldState.Instance.EvacuatedCount = 0;
                    WorldState.Instance.ActiveAgentCount = 0; // ★ 重置 Active Count
                    
                    // 等待一幀確保舊物件被完全 Destroy 掉
                    yield return null;

                    // Step 2: 設定變因
                    WorldState.Instance.RandomSeed = currentSeed;
                    UnityEngine.Random.InitState(currentSeed);

                    // ★ 防殘留：強制關閉模擬狀態，確保上一輪的殘留 flag 不會讓新 Agent 提前起跑
                    SystemManager.IsSimulationActive = false;
                    WorldState.Instance.Switch = 0;

                    // ★ 重置 timeScale = 1：上一輪結束時 timeScale 被設為 0，
                    //   如果不恢復，後面的 yield return null 和所有基於 Time.timeScale 的邏輯都會被凍結。
                    //   路徑預算階段需要正常的幀流動，但模擬尚未開始，所以設 1x 即可。
                    Time.timeScale = 1f;
                    Time.fixedDeltaTime = 0.02f;

                    MultiZoneSpawner foundSpawner = FindAnyObjectByType<MultiZoneSpawner>();
                    
                    // Step 3: 生成
                    if (foundSpawner != null)
                    {
                        foundSpawner.SpawnAll(targetPopulation);
                    }

                    // ★ 等待一幀：確保所有 Agent 的 Awake() + Start() 完整跑完，
                    //   這樣 AgentLogicLoop 已啟動並正確被 hasStarted==false 卡住。
                    yield return null;

                    // Step 4: 分批路徑預算 (Batched Path Calculation) ───────────────────
                    // 自動化模式下 CrowdAgent.Start() 不會自行算路，
                    // 改由這裡每幀算一批（BATCH_SIZE），攤平 CPU 負載。
                    // 全部算完後才進入 Step 5 鳴槍，徹底消除「擠牙膏效應」。
                    CrowdAgent[] allAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);
                    int totalAgents = allAgents.Length;

                    const int BATCH_SIZE = 30; // 每幀最多算幾條路徑（可依硬體能力調整）
                    int calculated = 0;
                    Debug.Log($"[ExperimentRunner] 開始分批路徑預算...（共 {totalAgents} 個 Agent，每幀 {BATCH_SIZE} 個）");
                    
                    float pathCalcStartTime = Time.realtimeSinceStartup;

                    for (int i = 0; i < totalAgents; i++)
                    {
                        allAgents[i].CalculatePathNow();
                        calculated++;
                        if (calculated % BATCH_SIZE == 0)
                        {
                            yield return null; // 每算完一批就讓出一幀
                        }
                    }
                    yield return null; // 最後再讓出一幀，確保所有 SetPath 生效

                    // 安全檢查：輪詢所有 Agent 的 IsPathReady（理論上此時應全部 true）
                    // 這裡的 pathWaitTimer 用 Time.unscaledDeltaTime，不受 timeScale 影響
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
                    {
                        Debug.LogWarning($"[ExperimentRunner] 路徑預算超時（{pathWaitTimeout} 秒），強制開始模擬。");
                    }
                    Debug.Log($"[ExperimentRunner] 所有 Agent 路徑就緒！（{totalAgents} 個，耗時約 {totalCalcTime:F2} 秒）");

                    // Step 5: 鳴槍起跑 ────────────────────────────────────────────────
                    // ★ 先呼叫 TrueSyncStart 讓所有 ApplyPathAndRelease 協程開始執行，
                    //   但 Switch 和 IsSimulationActive 此刻仍為 0/false，小人不會移動。
                    Debug.Log($"[ExperimentRunner] 執行真．同步起跑準備！啟動 {totalAgents} 個 Agent 的路徑灌入協程");
                    foreach (var a in allAgents)
                    {
                        if (a != null && a.gameObject.activeInHierarchy)
                        {
                            a.TrueSyncStart();
                        }
                    }

                    // ★ 等待所有 ApplyPathAndRelease 協程完成（確保 isOnNavMesh 確認 + 路徑灌入）
                    yield return new WaitForSecondsRealtime(0.25f);

                    // ★ 路徑全數灌入完畢後，才正式鳴槍：計時器歸零＋開啟模擬狀態
                    WorldState.Instance.SimulationTime = 0f;
                    WorldState.Instance.Switch = 1;
                    SystemManager.IsSimulationActive = true;
                    Debug.Log($"[ExperimentRunner] 所有 Agent 路徑發射完畢，模擬計時正式開始。");

                    // ★ 真・同步釋放煞車（繞過小人的 Staggered Coroutine 延遲）
                    // 確保 1000 個小人在同一幀（0ms 誤差）放開煞車！
                    foreach (var a in allAgents)
                    {
                        if (a != null && a.gameObject.activeInHierarchy)
                        {
                            a.ReleaseBrake();
                        }
                    }

                    // **自動切換鏡頭為自由模式，並開啟建築透明**
                    CameraFollow camFollow = Camera.main?.GetComponent<CameraFollow>();
                    if (camFollow != null)
                    {
                        camFollow.currentMode = CameraFollow.CameraMode.Free;
                    }
                    
                    if (systemManager != null && !WorldState.Instance.IsTransparentMode)
                    {
                        // 若為非透明，切換為透明
                        systemManager.ToggleSchoolTransparency();
                    }

                    // 恢復 SystemManager 設定的速度倍率
                    if (systemManager != null)
                        systemManager.SetSimulationSpeed(systemManager.simulationTimeScale);
                    else
                        Time.timeScale = 1f;
                    
                    if (dataLogger != null)
                    {
                        dataLogger.StartLogging(targetPopulation, currentSeed, signCount);
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
