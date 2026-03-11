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

    // 動畫與特效 (如果有的話)
    private Animator anim;
    public ParticleSystem congestionVFX;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();

        // 1. 隨機化初始參數，製造推擠感
        agent.avoidancePriority = Random.Range(30, 80);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance; 
        agent.autoBraking = false; // 不到終點不減速
        
        // 2. 自動尋找最近的出口
        if (target == null) FindNearestExit();
    }

    void Update()
    {
        if (WorldState.Instance == null) return;

        // 3. 系統暫停時，小人停止
        if (WorldState.Instance.Switch == 0)
        {
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            if (anim != null) anim.SetFloat("Speed", 0); // 停播走路動畫
            return;
        }
        else
        {
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
        }

        // (移除連續導航，改用 StartEvacuation 同步觸發)

        // 5. 恐慌數值映射 (速度與半徑)
        float panicRatio = WorldState.Instance.KnobSpeed / 1023f;
        agent.speed = 1.2f + panicRatio * 2.3f;             // 速度: 1.2 ~ 3.5 m/s
        agent.radius = Mathf.Lerp(0.3f, 0.15f, panicRatio); // 半徑: 越恐慌越擠

        // 6. 動畫連動
        if (anim != null && agent.enabled)
        {
            anim.SetFloat("Speed", agent.velocity.magnitude);
        }

        // 7. 擁堵特效 (選用：如果速度太慢，代表塞車了)
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
        
        // 8. 抵達出口判定 (防卡死與防原地踏步機制)
        if (!isEvacuated && agent.enabled && agent.isOnNavMesh && !agent.pathPending && target != null)
        {
            float distToTarget = Vector3.Distance(transform.position, target.position);
            
            // 安全抵達：或是因為擠不進 Trigger 但距離已經夠近且降速卡住 (防疊羅漢)
            if (agent.remainingDistance <= 1.5f || (distToTarget <= 2.0f && agent.velocity.magnitude < 0.1f))
            {
                ProcessEvacuation(target.gameObject.name);
            }
        }
    }

    /// <summary>
    /// 對齊起跑線：統一由總控端發出尋路指令，避免生成時卡頓。
    /// </summary>
    public void StartEvacuation()
    {
        if (target != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.SetDestination(target.position);
        }
    }

    // 🚪 處理逃生邏輯獨立拉出
    private void ProcessEvacuation(string exitName)
    {
        if (isEvacuated) return;
        
        isEvacuated = true;
        WorldState.Instance.EvacuatedCount++; // 逃生人數 +1
        WorldState.Instance.ActiveAgentCount--; // 場上活動人數 -1
        
        // 記錄該個體的「逃生耗時」
        escapeTime = WorldState.Instance.SimulationTime;
        
        // 寫入 DataLogger
        if (DataLogger.Instance != null)
        {
            DataLogger.Instance.LogAgentEscape(agentID, spawnZone, escapeTime, exitName);
        }
        
        // 關閉物理與導航，避免引擎當機
        if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        agent.enabled = false;
        
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        
        // 避免 GC Spike，直接停用物件而非 Destroy
        gameObject.SetActive(false);
    }

    // 🔥 提供給 DynamicSign 呼叫的換路指令
    public void ChangeDestination(Transform newTarget)
    {
        target = newTarget;
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.SetDestination(target.position);
        }
    }

    // 保留原本的物理觸發器作為第二道防線 (以防有剛體的情況)
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Finish") && !isEvacuated)
        {
            // 如果撞到出口，立刻判定逃生，避免疊羅漢
            ProcessEvacuation(other.gameObject.name);
            gameObject.SetActive(false); // 確保立刻消失，釋放空間給後方 Agent
        }
    }

    // 🔍 找最近出口的演算法 (上一版漏掉的部分)
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
        {
            target = nearestExit;
        }
    }
    
    // --- DataLogger 輔助方法 ---
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