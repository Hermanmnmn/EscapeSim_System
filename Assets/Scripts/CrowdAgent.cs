using UnityEngine;
using UnityEngine.AI;

public class CrowdAgent : MonoBehaviour
{
    public Transform target;
    private NavMeshAgent agent;
    private bool isEvacuated = false;
    public float escapeTime = 0f; // 記錄該個體的逃生耗時
    
    [Header("實驗數據追蹤")]
    public int agentID = -1;
    public string spawnZone = "Unknown";

    // 動畫與特效
    private Animator anim;
    public ParticleSystem congestionVFX;

    // 同步預計算的路徑物件 (Path Pre-calculation)
    private NavMeshPath currentPath;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        currentPath = new NavMeshPath();

        // ── 1. RVO 優先權隨機化 (避障死鎖破解) ──────────────────────────
        // 範圍 0~99 打破對稱性：高優先級的人直接擠過去，
        // 低優先級的人讓開，產生真實推擠感，消除 RVO 對稱死鎖。
        agent.avoidancePriority = Random.Range(0, 100);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance; 
        agent.autoBraking = false; // 不到終點不減速

        // ── 2. 初始物理煞車（鎖定在原地等候鳴槍）──────────────────────
        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        // ── 3. 自動尋找最近的出口（純距離，不考慮擁擠度）──────────────
        if (target == null) FindNearestExit();

        // ── 4. 強制同步路徑預計算 (Path Pre-calculation) ─────────────────
        // ⚡ 使用 NavMesh.CalculatePath 而非 SetDestination。
        //    SetDestination 會放入 Unity 非同步佇列，造成擠牙膏效應。
        //    CalculatePath 是同步計算，生成瞬間完成，1000 個 Agent 可能造成 1~2 秒 Hitch，
        //    但科學實驗的數據精準度優先於視覺觀感。
        //    算好後以 SetPath 直接塞入，Agent 取得路徑的瞬間等同所有人同時就位。
        if (target != null && agent.isOnNavMesh)
        {
            bool pathFound = NavMesh.CalculatePath(transform.position, target.position, NavMesh.AllAreas, currentPath);
            if (pathFound && currentPath.status != NavMeshPathStatus.PathInvalid)
            {
                agent.SetPath(currentPath);
            }
            else
            {
                // Fallback：如果算不到路（例如在 NavMesh 邊緣生成），退回異步方式
                agent.SetDestination(target.position);
                Debug.LogWarning($"[CrowdAgent] Agent {agentID} 無法同步計算路徑，使用非同步 Fallback。位置: {transform.position}");
            }
            agent.isStopped = true; // 算完路徑後再次確認煞住，避免引擎自動起步
        }
    }

    void Update()
    {
        if (WorldState.Instance == null) return;

        // ── 5. 模擬暫停/停止時，鎖死所有小人 ────────────────────────────
        if (!SystemManager.IsSimulationActive)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero; // 確保速度歸零，防止滑行
            }
            if (anim != null) anim.SetFloat("Speed", 0); // 停播走路動畫
            return;
        }

        // ── 6. 放開煞車（IsSimulationActive 變為 true 的瞬間同步彈射起步）──
        //    路徑已在 Start() 同步算好，isStopped = false 即可瞬間全員起跑。
        if (agent.enabled && agent.isOnNavMesh && agent.isStopped)
        {
            agent.isStopped = false;
        }

        // ── 7. 恐慌數值映射 (速度與半徑) ─────────────────────────────────
        // ⚠️ agent.speed 使用真實世界的步行/奔跑速度（1.5 ~ 6.0 m/s）。
        // 快轉功能統一由 Time.timeScale 處理，絕不修改此值，確保逃生耗時數據正確。
        float panicRatio = WorldState.Instance.KnobSpeed / 1023f;
        agent.speed = 1.5f + panicRatio * 4.5f;             // 速度: 1.5 ~ 6.0 m/s (真實物理速度)
        agent.radius = Mathf.Lerp(0.3f, 0.15f, panicRatio); // 半徑: 越恐慌越擠

        // ── 8. 動畫連動 ──────────────────────────────────────────────────
        if (anim != null && agent.enabled)
        {
            anim.SetFloat("Speed", agent.velocity.magnitude);
        }

        // ── 9. 擁堵特效 (速度太慢 = 塞車) ───────────────────────────────
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
        
        // ── 10. 抵達出口判定 (防卡死與防疊羅漢機制) ─────────────────────
        if (!isEvacuated && agent.enabled && agent.isOnNavMesh && !agent.pathPending && target != null)
        {
            float distToTarget = Vector3.Distance(transform.position, target.position);
            
            if (agent.remainingDistance <= 1.5f || (distToTarget <= 2.0f && agent.velocity.magnitude < 0.1f))
            {
                ProcessEvacuation(target.gameObject.name);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 對齊起跑線（同步預計算版）：路徑已在 Start() 同步算好，
    /// 此方法僅作保留相容接口。實際釋放由 Update() 的 IsSimulationActive 監聽觸發。
    /// </summary>
    public void StartEvacuation()
    {
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
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
    //    換路時重新用同步 CalculatePath，確保換路後路徑也即時就緒。
    public void ChangeDestination(Transform newTarget)
    {
        target = newTarget;
        if (agent.enabled && agent.isOnNavMesh)
        {
            NavMeshPath newPath = new NavMeshPath();
            bool found = NavMesh.CalculatePath(transform.position, target.position, NavMesh.AllAreas, newPath);
            if (found && newPath.status != NavMeshPathStatus.PathInvalid)
                agent.SetPath(newPath);
            else
                agent.SetDestination(target.position); // Fallback
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