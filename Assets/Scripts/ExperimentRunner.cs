using System.Collections;
using UnityEngine;

public class ExperimentRunner : MonoBehaviour
{
    [Header("實驗參數陣列")]
    public int[] populations = { 100, 200, 300 }; // 測試人數陣列
    public int[] seeds = { 1, 2, 3 };             // 測試亂數種子陣列

    [Header("控制器")]
    public SystemManager systemManager;
    public DataLogger dataLogger;
    public MultiZoneSpawner spawner;
    
    public float maxSimulationTime = 300f; // 大超時強制結束 (防卡死)

    private int _popIndex = 0;
    private int _seedIndex = 0;
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
        var serialController = FindObjectOfType<SerialController>();
        if (serialController != null)
        {
            serialController.enabled = false;
            Debug.Log("[ExperimentRunner] 已自動停用 SerialController，防止硬體訊號干擾。");
        }
        
        Debug.Log($"[ExperimentRunner] 實驗開始！共 {populations.Length * seeds.Length} 組測試。");

        for (_popIndex = 0; _popIndex < populations.Length; _popIndex++)
        {
            int targetPopulation = populations[_popIndex];

            for (_seedIndex = 0; _seedIndex < seeds.Length; _seedIndex++)
            {
                int currentSeed = seeds[_seedIndex];
                
                Debug.Log($"[ExperimentRunner] 準備執行 -> 人數: {targetPopulation}, 種子: {currentSeed}");
                
                // Step 1: 清場 (清除場上所有的 CrowdAgent)
                var agents = GameObject.FindGameObjectsWithTag("CrowdAgent");
                foreach (var a in agents) Destroy(a);
                WorldState.Instance.EvacuatedCount = 0;
                
                // 等待一幀確保舊物件被完全 Destroy 掉
                yield return null; 

                // Step 2: 設定變因 (設定 RandomSeed 並 Random.InitState，設定 spawner 生成數量)
                WorldState.Instance.RandomSeed = currentSeed;
                UnityEngine.Random.InitState(currentSeed);
                
                MultiZoneSpawner foundSpawner = FindObjectOfType<MultiZoneSpawner>();
                if (foundSpawner != null)
                {
                    foundSpawner.spawnCount = targetPopulation;
                }

                // Step 3: 生成 (呼叫 spawner.SpawnAll)
                if (foundSpawner != null)
                {
                    foundSpawner.SpawnAll();
                }

                // Step 4: 等待就緒 (讓 Unity 有時間把小人放在 NavMesh 上)
                yield return new WaitForSecondsRealtime(1.0f);
                
                // 統一發送尋路請求 (防卡頓)
                CrowdAgent[] spawnedAgents = FindObjectsOfType<CrowdAgent>();
                foreach (var a in spawnedAgents)
                {
                    a.StartEvacuation();
                }

                // Step 5: 鳴槍開跑 - 解決時間不動的問題 (強制設定 IsSimulationActive = true，並重置計時器)
                WorldState.Instance.SimulationTime = 0f;
                WorldState.Instance.Switch = 1;         
                SystemManager.IsSimulationActive = true; 
                Time.timeScale = 1f; // 確保 Time.deltaTime 有在轉
                
                if (dataLogger != null)
                {
                    dataLogger.StartLogging(targetPopulation, currentSeed);
                }

                // Step 6: 等待結束 (防卡死機制)
                float experimentTimer = 0f;

                while (WorldState.Instance.EvacuatedCount < targetPopulation && experimentTimer < maxSimulationTime)
                {
                    experimentTimer += Time.deltaTime;
                    yield return null; // 每一幀檢查
                }

                if (experimentTimer >= maxSimulationTime)
                {
                    Debug.LogWarning($"[ExperimentRunner] 實驗超時 (人數: {targetPopulation}, 種子: {currentSeed})，強制結束該局並進行紀錄。");
                }
                else
                {
                    Debug.Log($"[ExperimentRunner] 實驗完成 (人數: {targetPopulation}, 種子: {currentSeed}), 總耗時: {WorldState.Instance.SimulationTime:F2}s");
                }

                // Step 7: 記錄與換場 (呼叫 DataLogger 存檔，然後進入下一組參數)
                if (dataLogger != null)
                {
                    dataLogger.SaveAndCloseLogs();
                }

                // 關閉模擬狀態
                WorldState.Instance.Switch = 0;
                SystemManager.IsSimulationActive = false;
                Time.timeScale = 0f; // 讓 Agent 暫停

                // 等待幾秒後再進入下一組
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        _isExperimentRunning = false;
        Debug.Log("[ExperimentRunner] 所有實驗組合已全部執行完畢！");
    }
}
