using System.Collections.Concurrent;
using UnityEngine;

public class WorldState : MonoBehaviour
{
    public static WorldState Instance;

    [Header("Micro:bit 原始數據")]
    public int JoyX = 512;       // LX
    public int JoyY = 512;       // LY
    public int JoyRX = 512;      // RX
    public int JoyRY = 512;      // RY
    public int KnobHeight = 0;   // KA
    public int KnobSpeed = 0;    // KB
    public int Switch = 0;       // SW
    public int CamBtn = 0;       // CR

    [Header("瞬間觸發事件 (Rising Edge)")]
    public bool IsFireTriggered = false;        // FIRE
    public bool IsResetTriggered = false;       // RST
    public bool IsTransparentTriggered = false; // BLK 單次觸發
    public int JoyLButton = 0;                  // JoyL 按鈕

    [Header("透明模式")]
    public bool IsTransparentMode = false;      // 建築透明開關

    [Header("系統與地圖數據")]
    public ConcurrentQueue<Vector3> IncomingMapPoints = new ConcurrentQueue<Vector3>();
    public Vector3 CursorPosition;

    [Header("模擬統計")]
    public float SimulationStartTime = 0f;   // 模擬開始時間 (Time.time)
    public float SimulationTime = 0f;        // 模擬經過時間
    public int EvacuatedCount = 0;           // 成功逃生人數
    void Awake()
    {
        
        if (Instance == null) 
        { 
            Instance = this; 
            DontDestroyOnLoad(gameObject); 
        }
        else 
        { 
            Destroy(gameObject); 
        }
    }
}