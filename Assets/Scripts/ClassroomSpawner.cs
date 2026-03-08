using UnityEngine;
using UnityEngine.AI;

public class ClassroomSpawner : MonoBehaviour
{
    public GameObject agentPrefab;
    public int agentsPerRoom = 15;
    public float spawnRadius = 3f;

    void Start()
    {
        if (agentPrefab == null)
        {
            Debug.LogWarning("Agent Prefab not assigned in ClassroomSpawner!");
            return;
        }

        for (int i = 0; i < agentsPerRoom; i++)
        {
            Vector3 randomDir = Random.insideUnitSphere * spawnRadius;
            randomDir.y = 0; // 保持在同一個水平面上
            Vector3 spawnPos = transform.position + randomDir;

            NavMeshHit hit;
            // 尋找半徑內的 NavMesh 有效位置
            if (NavMesh.SamplePosition(spawnPos, out hit, 2.0f, NavMesh.AllAreas))
            {
                Instantiate(agentPrefab, hit.position, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning($"ClassroomSpawner at {gameObject.name}: Could not find valid NavMesh position for agent {i}.");
            }
        }
    }
}
