using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class CrowdAgent : MonoBehaviour
{
    public Transform target;
    private NavMeshAgent agent;
    private bool isEvacuated = false;
    public float escapeTime = 0f; // 記錄該個體的逃生耗時
    
    [Header("實驗數據追蹤")]
    public int agentID = -1;
    public string spawnZone = "Unknown";

    // 特效
    public ParticleSystem congestionVFX;

    // 同步預計算的路徑物件 (Path Pre-calculation)
    private NavMeshPath currentPath;

    // ── 路徑就緒旗標 ──────────────────────────────────────────────────
    /// <summary>外部（ExperimentRunner）可查詢此旗標，確認路徑是否已算好。</summary>
    public bool IsPathReady { get; private set; } = false;

    // ── 密度速度壓縮模型 ──────────────────────────────────────────────
    private float baseSpeed;                     // 個體基礎速度 (Start 時隨機 1.5~3.0)
    private bool hasStarted = false;             // 是否已放開煞車（同步起步用）
    private static readonly Collider[] _densityBuffer = new Collider[64]; // 靜態共用緩衝，避免 GC
    private int _cachedHitCount = 0;             // 最近一次密度偵測結果

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (currentPath == null) currentPath = new NavMeshPath();

        // ── 1. 避障參數優化 (Anti-Gridlock + Anti-Clipping) ──────────────
        // 使用 LowQuality 而非 NoObstacleAvoidance，保留基本的遮摭碰撞避免穿模，
        // 但不用高精度避障以節省 CPU。
        // 加入 avoidancePriority 隨機化，打破對稱死鎖：
        //   高優先級的人直接擠過去，低優先級的人讓開。
        if (agent != null)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = Random.Range(0, 100); // 打破對稱性
            agent.autoBraking = false; // 不到終點不減速
        }
    }

    void Start()
    {
        // ── 2. 個體基礎速度隨機化 (個體差異模型) ─────────────────────────
        baseSpeed = Random.Range(1.5f, 3.0f);

        // ── 3. 初始物理煞車（鎖定在原地等候鳴槍）──────────────────────────
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        // ── 4. 自動尋找最近的出口（純距離，不考慮擁擠度）──────────────────
        if (target == null) FindNearestExit();

        // ── 5. 路徑預計算 ─────────────────────────────────────────────────
        // 自動化模式下，由 ExperimentRunner 分批呼叫 CalculatePathNow()，
        // 避免上百個 Agent 在同一幀同步算路導致卡頓。
        // 手動模式（按 P 生成）則在 Start() 裡直接算好。
        if (!SystemManager.IsAutoMode)
        {
            CalculatePathNow();
            TrueSyncStart(); // 手動模式下算好馬上起跑
        }

        // ── 6. 降低更新頻率 (方法 3: Lower Update Frequency) ──────────────
        // 放棄每幀 Update()，改用 Staggered Coroutine (5Hz) 進行邏輯計算，
        // 大幅降低 1000 人規模時的 CPU 負擔。
        StartCoroutine(AgentLogicLoop());
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 方法 3：降低更新頻率（降頻至 5Hz）
    /// 合併原本的 Update() 與 UpdateLocalDensity()。加入隨機延遲 (Staggering)，
    /// 把 1000 個小人的運算徹底打散到不同影格，消除單幀效能峰值 (Spike)。
    /// </summary>
    private IEnumerator AgentLogicLoop()
    {
        // 隨機錯開每隻 Agent 的初始執行時機 (Staggering)
        // ★ 使用 WaitForSecondsRealtime，不受 Time.timeScale 影響，
        //   避免 timeScale=0 時 Coroutine 永久卡死、或 timeScale=10 時即時過渡無效。
        yield return new WaitForSecondsRealtime(Random.Range(0.0f, 0.2f));

        WaitForSecondsRealtime waitTime = new WaitForSecondsRealtime(0.2f); // 5Hz 更新率（不受加速影響）

        while (gameObject.activeInHierarchy && !isEvacuated)
        {
            if (WorldState.Instance == null) 
            {
                yield return waitTime;
                continue;
            }

            // ── 7. 尚未起跑時：等待 TrueSyncStart() 統一釋放 ────────────────
            //    hasStarted 只會被 ApplyPathAndRelease() 設為 true，
            //    因此在路徑灌入完成之前，這裡會一直 continue。
            if (!hasStarted)
            {
                yield return waitTime;
                continue;
            }

            // ── 8. 模擬暫停/停止時，鎖死所有小人 ────────────────────────────
            if (!SystemManager.IsSimulationActive)
            {
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero; // 確保速度歸零，防止滑行
                }
                
                yield return waitTime;
                continue;
            }

            // ── 8b. 已經由 ExperimentRunner 直接呼叫 ReleaseBrake() 同步解除，
            // 這裡不再依賴 Staggered Coroutine 的延遲，確保 1000 人在同一幀起跑！

            // ── 9. 合併 Local Density 計算 ──────────────────────────────
            int count = Physics.OverlapSphereNonAlloc(transform.position, 1.5f, _densityBuffer);
            int neighborCount = 0;
            for (int i = 0; i < count; i++)
            {
                // 去掉自己
                if (_densityBuffer[i] != null && _densityBuffer[i].gameObject != gameObject)
                    neighborCount++;
            }
            _cachedHitCount = neighborCount;

            // ── 10. 密度速度壓縮模型 ─────────────────────────────────────────
            if (agent != null && agent.enabled)
            {
                const float minSpeed = 0.5f; // 最低速度保底：再擠也能蠕動，防止模擬卡死
                float calculatedSpeed = (baseSpeed * SystemManager.GlobalSpeedMultiplier) / (1f + _cachedHitCount * 0.1f);
                agent.speed = Mathf.Max(minSpeed, calculatedSpeed);
                
                // 恐慌半徑映射
                float panicRatio = WorldState.Instance.KnobSpeed / 1023f;
                agent.radius = Mathf.Lerp(0.3f, 0.15f, panicRatio); 

                // 擁堵特效
                if (congestionVFX != null && target != null)
                {
                    if (agent.velocity.magnitude < 0.2f && Vector3.Distance(transform.position, target.position) > 2f)
                    {
                        if (!congestionVFX.isPlaying) congestionVFX.Play();
                    }
                    else
                    {
                        if (congestionVFX.isPlaying) congestionVFX.Stop();
                    }
                }

                // 抵達出口判定
                if (!isEvacuated && agent.isOnNavMesh && !agent.pathPending && target != null)
                {
                    float distToTarget = Vector3.Distance(transform.position, target.position);
                    if (agent.remainingDistance <= 1.5f || (distToTarget <= 2.0f && agent.velocity.magnitude < 0.1f))
                    {
                        ProcessEvacuation(target.gameObject.name);
                    }
                }
            }

            yield return waitTime;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 同步計算路徑並儲存，但**不上傳給 Agent**。
    /// 保持 Agent 處於停用狀態，徹底繞過 Unity 非同步的元件啟動延遲。
    /// </summary>
    public void CalculatePathNow()
    {
        if (target == null) FindNearestExit();
        if (target == null) { IsPathReady = true; return; }

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (currentPath == null) currentPath = new NavMeshPath();

        // 關閉 agent 來預算路徑（避免引擎底層占先跑）
        agent.enabled = false;

        // ★ 關鍵修復：agent 停用時 transform.position 是課桌椅的實際高度（如 Y=10），
        //   而非 NavMesh 表面。直接用此位置呼叫 CalculatePath 必定失敗。
        //   必須先用 NavMesh.SamplePosition 把起點吸附到最近的 NavMesh 表面。
        Vector3 startPos = transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(startPos, out hit, 2.0f, NavMesh.AllAreas))
        {
            startPos = hit.position; // 吸附成功，使用 NavMesh 上的正確起點
        }
        else
        {
            // 2m 範圍內找不到 NavMesh → 標記完成但不存有效路徑，交由 ApplyPathAndRelease 使用 SetDestination fallback
            Debug.LogWarning($"[CrowdAgent] Agent {agentID} 在 2m 內找不到 NavMesh 表面，將以 SetDestination 替代。位置: {transform.position}");
            agent.enabled = true;
            IsPathReady = true;
            return;
        }

        bool pathFound = NavMesh.CalculatePath(startPos, target.position, NavMesh.AllAreas, currentPath);
        if (!pathFound || currentPath.status == NavMeshPathStatus.PathInvalid)
        {
            // 路徑無效時不 spam，debug 用一條即可；實際移動會靠 SetDestination fallback
            currentPath.ClearCorners(); // 清除無效路徑，確保 ApplyPathAndRelease 走 fallback
        }

        // ★ 重新啟用 agent，讓它在路徑預算階段內自行安置到 NavMesh 上。
        // 這樣到 ApplyPathAndRelease() 被呼叫時，isOnNavMesh 已經是 true。
        agent.enabled = true;
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        IsPathReady = true;
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 真．同步起跑：瞬間啟用 Agent 並強制灌入已算好的路徑。
    /// 完全繞過 NavMeshAgent 自己跑 Update 的非同步延遲。
    /// </summary>
    public void TrueSyncStart()
    {
        if (agent == null) return;
        // 還沒無特殊處理要做，直接啟動 Coroutine 來協調路徑發射
        StartCoroutine(ApplyPathAndRelease());
    }

    /// <summary>
    /// 內部協程：等待 NavMeshAgent 穩定在 NavMesh 上後，再發射路徑並釋放煮車。
    /// 從根本解決 TrueSyncStart 含路徑發射失敗的問題。
    /// </summary>
    private IEnumerator ApplyPathAndRelease()
    {
        // 確保 agent 已啟用
        if (!agent.enabled) agent.enabled = true;

        // 等到 isOnNavMesh 為 true（最多備用 10 次 check）
        int waitFrames = 0;
        while (!agent.isOnNavMesh && waitFrames < 10)
        {
            waitFrames++;
            yield return null;
        }

        // ★ 灌入路徑，但保持煞車。不在這裡釋放 isStopped！
        //   AgentLogicLoop 看到 hasStarted=true 且 IsSimulationActive=true 後，
        //   會在正確的時機自行解除 isStopped。
        if (agent.isOnNavMesh)
        {
            if (currentPath != null && currentPath.status != NavMeshPathStatus.PathInvalid)
                agent.SetPath(currentPath);
            else if (target != null)
                agent.SetDestination(target.position);
        }
        else
        {
            Debug.LogWarning($"[CrowdAgent] Agent {agentID} 無法安置到 NavMesh，將以 SetDestination Fallback");
            if (target != null) agent.SetDestination(target.position);
        }

        // 確保煞車仍然鎖住（路徑灌入後 NavMeshAgent 有時會自動解除）
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        // 通知 AgentLogicLoop：路徑已就緒，等待 IsSimulationActive 再動
        hasStarted = true;
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 由 ExperimentRunner 統一在第 0 幀呼叫，保證 1000 人同時解除煞車。
    /// 完全繞過 Update/Coroutine 的隨機甦醒延遲。
    /// </summary>
    public void ReleaseBrake()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 保留相容接口，改呼叫 TrueSyncStart
    /// </summary>
    public void StartEvacuation()
    {
        TrueSyncStart();
    }

    // ─────────────────────────────────────────────────────────────────
    // 🚪 處理逃生邏輯
    private void ProcessEvacuation(string exitName)
    {
        if (isEvacuated) return;
        
        isEvacuated = true;
        WorldState.Instance.EvacuatedCount++;  // 逃生人數 +1
        WorldState.Instance.ActiveAgentCount--; // 場上活動人數 -1
        
        escapeTime = WorldState.Instance.SimulationTime;
        
        if (DataLogger.Instance != null)
        {
            DataLogger.Instance.LogAgentEscape(agentID, spawnZone, escapeTime, exitName);
        }
        
        if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        
        // 避免 GC Spike，停用而非 Destroy
        gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────
    // 🔥 提供給 SmartSign / DynamicSign 呼叫的換路指令
    public void ChangeDestination(Transform newTarget)
    {
        target = newTarget;
        if (agent.enabled && agent.isOnNavMesh)
        {
            // 方法 1：分批設定目的地 (Batch SetDestination)
            // 將原本強制同步計算 (CalculatePath) 改回 Unity 原生的異步 SetDestination。
            // 這樣萬一 500 人同時觸發指示牌換路，引擎會自動把算路負載分攤到多個 Frame 執行，避免卡頓。
            agent.SetDestination(target.position);
        }
    }

    // 🔥 相容接口多載
    public void SetDestination(Transform newTarget)
    {
        ChangeDestination(newTarget);
    }

    // ─────────────────────────────────────────────────────────────────
    // 物理觸發器：第二道防線
    void OnTriggerEnter(Collider other)
    {
        // ★ 防呆：模擬尚未正式開始（路徑預算階段）時，忽略出口碰觸，避免計數污染
        if (!SystemManager.IsSimulationActive) return;
        
        if (other.CompareTag("Finish") && !isEvacuated)
        {
            ProcessEvacuation(other.gameObject.name);
            gameObject.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // 🔍 找最近出口（純直線距離，無擁擠考量，模擬無知群眾初始行為）
    private void FindNearestExit()
    {
        GameObject[] exits = GameObject.FindGameObjectsWithTag("Finish");
        float minDistance = Mathf.Infinity;
        Transform nearestExit = null;

        foreach (GameObject exit in exits)
        {
            float dist = Vector3.Distance(transform.position, exit.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearestExit = exit.transform;
            }
        }

        if (nearestExit != null)
            target = nearestExit;
    }
    
    // ─────────────────────────────────────────────────────────────────
    // DataLogger 輔助方法
    public float GetCurrentSpeed()
    {
        return agent != null && agent.enabled ? agent.velocity.magnitude : 0f;
    }

    public Transform GetDestination()
    {
        return target;
    }

    public bool IsEvacuated()
    {
        return isEvacuated;
    }
}