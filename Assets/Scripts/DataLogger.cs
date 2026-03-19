using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class DataLogger : MonoBehaviour
{
    public static DataLogger Instance;

    [Header("設定")]
    public float logInterval = 1f; // 每幾秒記錄一次
    public string logDirectory = "LogBook";
    
    // 即時快取
    private ZoneSensor[] _zoneSensors;
    private DynamicSign[] _dynamicSigns; // 如果有需要紀錄 Exit Queue
    private GameObject[] _exits;
    
    // 輸出字串緩衝
    private StringBuilder _mainLogCsv;
    private StringBuilder _agentLogCsv;
    private StringBuilder _txtLog;
    private Dictionary<string, string> _previousValues;
    
    // 目前實驗參數
    private int _currentPopulation;
    private int _currentSeed;
    private int _currentSignCount;
    private bool _isLogging = false;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// 初始化新的實驗日誌紀錄
    /// </summary>
    public void StartLogging(int population, int seed, int signCount)
    {
        _currentPopulation = population;
        _currentSeed = seed;
        _currentSignCount = signCount;
        
        // 尋找場上的感測與出口
        _zoneSensors = FindObjectsByType<ZoneSensor>(FindObjectsSortMode.None);
        _exits = GameObject.FindGameObjectsWithTag("Finish");
        
        _mainLogCsv = new StringBuilder();
        _agentLogCsv = new StringBuilder();
        _txtLog = new StringBuilder();
        _previousValues = new Dictionary<string, string>();

        // 建立主 CSV 表頭
        _mainLogCsv.Append("Time(s),TotalEscaped,ActiveAgents,AverageSpeed,MaxCongestion,Seed,ActiveSigns");
        
        // 動態加上各出口與各區域
        if (_exits != null)
        {
            foreach (var exit in _exits)
            {
                _mainLogCsv.Append($",Exit_{exit.name}");
            }
        }
        
        if (_zoneSensors != null)
        {
            foreach (var zone in _zoneSensors)
            {
                _mainLogCsv.Append($",Zone_{zone.gameObject.name}");
            }
        }
        _mainLogCsv.AppendLine();

        // 建立個體 CSV 表頭
        _agentLogCsv.AppendLine("AgentID,SpawnZone,EscapeTime(s),ExitUsed,Seed,ActiveSigns");

        _isLogging = true;
        StartCoroutine(LogRoutine());
    }

    /// <summary>
    /// 1Hz 定期記錄邏輯
    /// </summary>
    private IEnumerator LogRoutine()
    {
        while (_isLogging)
        {
            float curTime = WorldState.Instance.SimulationTime;
            int totalEscaped = WorldState.Instance.EvacuatedCount;
            int activeAgents = WorldState.Instance.ActiveAgentCount;
            
            // 計算全場平均速度
            float avgSpeed = 0f;
            int speedCount = 0;
            var allAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None); // (如果人數多這裡可優化，或透過 WorldState 追蹤)
            
            foreach (var a in allAgents)
            {
                if (a.gameObject.activeInHierarchy && !a.IsEvacuated())
                {
                    avgSpeed += a.GetCurrentSpeed();
                    speedCount++;
                }
            }
            if (speedCount > 0) avgSpeed /= speedCount;

            // 計算最大擁擠度
            int maxCongestion = 0;
            if (_zoneSensors != null)
            {
                foreach (var z in _zoneSensors)
                {
                    if (z.CurrentAgentCount > maxCongestion)
                        maxCongestion = z.CurrentAgentCount;
                }
            }

            _mainLogCsv.Append($"{curTime:F2},{totalEscaped},{activeAgents},{avgSpeed:F4},{maxCongestion},{_currentSeed},{_currentSignCount}");

            bool hasChanges = false;
            StringBuilder changeLog = new StringBuilder();
            changeLog.Append($"[{curTime:F2}s] ");

            void CheckAndRecord(string key, string newVal)
            {
                if (!_previousValues.TryGetValue(key, out string oldVal) || oldVal != newVal)
                {
                    if (oldVal != null)
                    {
                        changeLog.Append($"{key}:{oldVal}->{newVal} ");
                        hasChanges = true;
                    }
                    _previousValues[key] = newVal;
                }
            }

            CheckAndRecord("TotalEscaped", totalEscaped.ToString());
            CheckAndRecord("ActiveAgents", activeAgents.ToString());
            CheckAndRecord("AvgSpeed", avgSpeed.ToString("F4"));
            CheckAndRecord("MaxCongestion", maxCongestion.ToString());

            // 統計目前聚集在出口附近的人數 (假設出口有掛 ZoneSensor 或透過 DynamicSign 算)
            if (_exits != null)
            {
                foreach (var exit in _exits)
                {
                    // 這裡的邏輯可依實際 Exit 的計算方式調整，目前填 0，可改由 Agent 身上的目標統計
                    int headingToExit = allAgents.Count(a => a.gameObject.activeInHierarchy && a.GetDestination() == exit.transform);
                    _mainLogCsv.Append($",{headingToExit}");
                    CheckAndRecord($"Exit_{exit.name}", headingToExit.ToString());
                }
            }

            if (_zoneSensors != null)
            {
                foreach (var z in _zoneSensors)
                {
                    _mainLogCsv.Append($",{z.CurrentAgentCount}");
                    CheckAndRecord($"Zone_{z.gameObject.name}", z.CurrentAgentCount.ToString());
                }
            }
            
            _mainLogCsv.AppendLine();

            if (hasChanges)
            {
                _txtLog.AppendLine(changeLog.ToString());
            }

            yield return new WaitForSeconds(logInterval);
        }
    }

    /// <summary>
    /// 當小人成功逃生時，寫入個體資料表
    /// </summary>
    public void LogAgentEscape(int agentID, string spawnZone, float escapeTime, string exitUsed)
    {
        if (!_isLogging) return;
        _agentLogCsv.AppendLine($"{agentID},{spawnZone},{escapeTime:F2},{exitUsed},{_currentSeed},{_currentSignCount}");
    }

    /// <summary>
    /// 結束記錄並儲存檔案
    /// </summary>
    public void SaveAndCloseLogs()
    {
        _isLogging = false; StopAllCoroutines();

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dirPath = Path.Combine(Application.dataPath, "..", logDirectory);
        dirPath = Path.GetFullPath(dirPath); // 取絕對路徑，方便 Debug 顯示
        if (!Directory.Exists(dirPath))
            Directory.CreateDirectory(dirPath);

        string mainPath = Path.Combine(dirPath, $"Log_Agents{_currentPopulation}_Seed{_currentSeed}_Signs{_currentSignCount}_{timestamp}.csv");
        string agentPath = Path.Combine(dirPath, $"Log_Agents{_currentPopulation}_Seed{_currentSeed}_Signs{_currentSignCount}_Individuals_{timestamp}.csv");
        string txtPath = Path.Combine(dirPath, $"Log_Agents{_currentPopulation}_Seed{_currentSeed}_Signs{_currentSignCount}_Changes_{timestamp}.txt");

        File.WriteAllText(mainPath, _mainLogCsv.ToString());
        File.WriteAllText(agentPath, _agentLogCsv.ToString());
        File.WriteAllText(txtPath, _txtLog.ToString());

        Debug.Log($"[DataLogger] 實驗記錄已儲存: \n{mainPath}\n{agentPath}\n{txtPath}");
    }
}
