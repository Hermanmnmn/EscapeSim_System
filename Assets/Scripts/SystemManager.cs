using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class SystemManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────
    public static SystemManager Instance { get; private set; }

    [Header("連結物件")]
    public Transform godCursor;
    public GameObject wallPrefab;
    public GameObject firePrefab;
    public MultiZoneSpawner spawner;       // 新版多區域生成器
    public Material transparentMaterial;
    public GameObject schoolModel;

    [Header("參數設定")]
    public float moveSpeed = 10f;
    public float heightScale = 10f;
    public float mapSize = 50f;

    [Header("模擬速度")]
    [Range(1f, 10f)]
    public float simulationTimeScale = 1.0f; // 目前的模擬倍速 (1x ~ 10x)

    // 全域速度倍率（由搖桿 KnobSpeed 控制）
    public static float GlobalSpeedMultiplier = 1f;

    // 系統狀態
    public static bool IsSimulationActive = false;
    public static bool IsAutoMode = false; // 自動化模式標記

    /// <summary>1~10 模擬倍速（不再綁定 Time.timeScale，供 CrowdAgent 速度計算使用）。</summary>
    public static float SimulationSpeedScale = 1f;

    // 快取
    private Camera _mainCam;
    private Dictionary<MeshRenderer, Material[]> _originalMaterials = new Dictionary<MeshRenderer, Material[]>();
    private int _lastJoyLButton = 0;

    // 透明度鍵盤 rising edge
    private bool _prevBKey = false;
    private int _lastCrowdSwitch = -1;
    private bool _spawnRoutineRunning = false;

    /// <summary>與 WorldState.Switch + IsSimulationActive 一致；供尋路/換路判斷，不依賴 Time.timeScale。</summary>
    public static bool IsSimulationRunningState()
    {
        return WorldState.Instance != null && WorldState.Instance.Switch == 1 && IsSimulationActive;
    }

    void Awake()
    {
        // Singleton 設定
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        SimulationSpeedScale = simulationTimeScale;
    }

    void Start()
    {
        Application.targetFrameRate = -1;
        _mainCam = Camera.main;
        
        // 確保一致的隨機行為 (Monte Carlo simulation)
        if (WorldState.Instance != null)
        {
            UnityEngine.Random.InitState(WorldState.Instance.RandomSeed);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 設定模擬速度倍率（僅更新 SimulationSpeedScale，不再改 Time.timeScale）。
    /// </summary>
    public void SetSimulationSpeed(float scale)
    {
        simulationTimeScale = Mathf.Clamp(scale, 1f, 10f);
        SimulationSpeedScale = simulationTimeScale;
    }

    private void SetAllCrowdAgentScriptsEnabled(bool on)
    {
        CrowdAgent[] agents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);
        foreach (var a in agents)
        {
            if (a != null) a.enabled = on;
        }
    }

    void Update()
    {
        if (WorldState.Instance == null) return;

        // 自動化實驗保護：如果在做自動化實驗，跳過硬體 Switch 檢查，讓 ExperimentRunner 自己管
        ExperimentRunner expRunner = FindAnyObjectByType<ExperimentRunner>();
        bool isAutomated = (expRunner != null && expRunner.IsRunning()) || IsAutoMode;

        // ── 1. 總開關 ──────────────────────────────────────────────────        
        if (!isAutomated)
        {
            int sw = WorldState.Instance.Switch;
            IsSimulationActive = (sw == 1);
            if (sw != _lastCrowdSwitch)
            {
                _lastCrowdSwitch = sw;
                if (sw == 1)
                    SetAllCrowdAgentScriptsEnabled(true);
                else if (!_spawnRoutineRunning)
                    SetAllCrowdAgentScriptsEnabled(false);
            }
        }

        // ── 2. SimulationTime 計時（只在 Switch==1 時增加）────────────
        if (WorldState.Instance.Switch == 1)
        {
            WorldState.Instance.SimulationTime += Time.deltaTime;
        }

        // ── 3. 建築透明度（硬體 IsTransparentTriggered 或鍵盤 B）────────
        if (WorldState.Instance.IsTransparentTriggered)
        {
            ToggleSchoolTransparency();
            WorldState.Instance.IsTransparentTriggered = false;
        }

        bool bNow = Keyboard.current != null && Keyboard.current.bKey.isPressed;
        if (bNow && !_prevBKey)
        {
            ToggleSchoolTransparency();
        }
        _prevBKey = bNow;

        // ── 4. 移動紅球 (GodCursor) ────────────────────────────────────
        if (godCursor != null)
        {
            int rawX = WorldState.Instance.JoyX;
            int rawY = WorldState.Instance.JoyY;
            int rawH = WorldState.Instance.KnobHeight;
            int rawS = WorldState.Instance.KnobSpeed;

            GlobalSpeedMultiplier = 1f + (rawS / 1023f) * 5f;

            float inputX = (rawX - 512) / 512f;
            float inputZ = (rawY - 512) / 512f;
            if (Mathf.Abs(inputX) < 0.15f) inputX = 0;
            if (Mathf.Abs(inputZ) < 0.15f) inputZ = 0;

            Vector3 pos = godCursor.position;

            if (_mainCam != null)
            {
                Vector3 forward = _mainCam.transform.forward;
                Vector3 right   = _mainCam.transform.right;
                forward.y = 0; forward.Normalize();
                right.y   = 0; right.Normalize();
                // 根據輸入合成移動向量，使用 unscaledDeltaTime 確保暫停時也能飛
                Vector3 moveDir = right * inputX + forward * inputZ;
                pos += moveDir * moveSpeed * Time.unscaledDeltaTime;
            }
            else
            {
                // Fallback
                _mainCam = Camera.main;
                pos.x += inputX * moveSpeed * Time.unscaledDeltaTime;
                pos.z += inputZ * moveSpeed * Time.unscaledDeltaTime;
            }

            float targetY = (rawH / 1023f) * heightScale;
            pos.y = Mathf.Lerp(pos.y, targetY, 1.0f - Mathf.Exp(-5f * Time.unscaledDeltaTime));

            pos.x = Mathf.Clamp(pos.x, -mapSize, mapSize);
            pos.z = Mathf.Clamp(pos.z, -mapSize, mapSize);

            godCursor.position = pos;
            WorldState.Instance.CursorPosition = pos;
        }

        // ── 5. 放置火源（左搖桿下壓 rising edge）────────────────────────
        int currentJoyL = WorldState.Instance.JoyLButton;
        if (currentJoyL == 1 && _lastJoyLButton == 0)
        {
            if (firePrefab != null && godCursor != null)
                Instantiate(firePrefab, godCursor.position, Quaternion.identity);
        }
        _lastJoyLButton = currentJoyL;

        // ── 6. 硬體重置（IsResetTriggered）───────────────────────────────
        if (WorldState.Instance.IsResetTriggered)
        {
            PerformSoftReset();
            WorldState.Instance.IsResetTriggered = false;
        }

        // ── 7. 鍵盤：P 生成群眾 ────────────────────────────────────────
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            MultiZoneSpawner activeSpawner = spawner != null ? spawner : FindAnyObjectByType<MultiZoneSpawner>();
            
            if (activeSpawner != null)
                StartCoroutine(SpawnAndStartEvacuationRoutine(activeSpawner));
            else
                Debug.LogWarning("SystemManager: 未綁定且找不到 Spawner！");
        }

        // ── 8. 鍵盤：R 軟重置 ──────────────────────────────────────────
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            PerformSoftReset();
        }

        // ── 9. 模擬速度快捷鍵：[ 減速 / ] 加速 ──────────────────────────
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
            {
                SetSimulationSpeed(simulationTimeScale - 1f);
            }
            else if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
            {
                SetSimulationSpeed(simulationTimeScale + 1f);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Soft Reset：清除所有 CrowdAgent 與 Fire，計時與疏散數歸零，重新生成群眾。
    /// </summary>
    public void PerformSoftReset()
    {
        // 刪除所有 CrowdAgent
        var agents = GameObject.FindGameObjectsWithTag("CrowdAgent");
        foreach (var a in agents) Destroy(a);

        // 刪除所有火災物件
        var fires = GameObject.FindGameObjectsWithTag("Fire");
        foreach (var f in fires) Destroy(f);

        // 重置紅球位置
        if (godCursor != null)
            godCursor.position = new Vector3(0, 5, 0);

        // 歸零計時與疏散計數
        WorldState.Instance.SimulationTime  = 0f;
        WorldState.Instance.EvacuatedCount  = 0;
        WorldState.Instance.SimulationStartTime = -1f;

        // 重新生成群眾
        MultiZoneSpawner activeSpawner = spawner != null ? spawner : FindAnyObjectByType<MultiZoneSpawner>();
        if (activeSpawner != null)
            StartCoroutine(SpawnAndStartEvacuationRoutine(activeSpawner));
        else
            Debug.LogWarning("Soft Reset：未綁定且找不到 Spawner，無法重新生成！");
    }

    private IEnumerator SpawnAndStartEvacuationRoutine(MultiZoneSpawner activeSpawner)
    {
        _spawnRoutineRunning = true;
        // 1. 生成所有小人
        activeSpawner.SpawnAll();
        
        // 2. 延遲 0.5 秒等待 NavMesh 初始化
        yield return new WaitForSecondsRealtime(0.5f);

        // 3. 統一發送尋路請求 (防卡頓)
        CrowdAgent[] allAgents = FindObjectsByType<CrowdAgent>(FindObjectsSortMode.None);
        foreach (var agent in allAgents)
        {
            agent.StartEvacuation();
        }

        _spawnRoutineRunning = false;
        if (WorldState.Instance != null && WorldState.Instance.Switch == 0)
            SetAllCrowdAgentScriptsEnabled(false);
    }

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// 切換學校模型的材質透明度 (Normal ↔ Transparent)
    /// </summary>
    public void ToggleSchoolTransparency()
    {
        if (schoolModel == null || transparentMaterial == null) return;

        WorldState.Instance.IsTransparentMode = !WorldState.Instance.IsTransparentMode;
        bool isTransparent = WorldState.Instance.IsTransparentMode;

        MeshRenderer[] renderers = schoolModel.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer r in renderers)
        {
            if (isTransparent)
            {
                if (!_originalMaterials.ContainsKey(r))
                    _originalMaterials[r] = r.sharedMaterials;

                Material[] transMats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < transMats.Length; i++)
                    transMats[i] = transparentMaterial;
                r.sharedMaterials = transMats;
            }
            else
            {
                if (_originalMaterials.ContainsKey(r))
                    r.sharedMaterials = _originalMaterials[r];
            }
        }
    }
}
