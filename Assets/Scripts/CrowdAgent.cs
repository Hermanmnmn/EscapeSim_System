using UnityEngine;
using UnityEngine.AI;

public class CrowdAgent : MonoBehaviour
{
    public Transform target;
    private NavMeshAgent agent;
    private bool isEvacuated = false;

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

        // 4. 正常導航
        if (target != null && agent.isOnNavMesh && agent.enabled)
        {
            agent.SetDestination(target.position);
        }

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

    // 🚪 碰到出口消失與計數
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Finish") && !isEvacuated)
        {
            isEvacuated = true;
            WorldState.Instance.EvacuatedCount++; // 逃生人數 +1
            
            // 關閉物理與導航，避免引擎當機
            agent.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            
            // 隱藏外觀
            foreach (Transform child in transform) {
                child.gameObject.SetActive(false);
            }

            // 延遲刪除記憶體
            Destroy(gameObject, 0.5f);
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
}