using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO.Ports;
using System.Threading;

public class SerialController : MonoBehaviour
{
    [Header("設定")]
    public string portName = "COM3"; // ⚠️ 請確認你的 Port
    public int baudRate = 115200;
    public bool useKeyboardDebug = false;

    [Header("除錯顯示")]
    [TextArea] public string rawDataDisplay = "";

    private SerialPort stream;
    private Thread readThread;
    private bool isRunning = false;
    private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

    private int lastFireBtn = 0;
    private int lastResetBtn = 0;
    private int lastBlkBtn = 0;

    void Start() { if (!useKeyboardDebug) StartConnection(); }
    void OnDisable() { CloseConnection(); }
    void OnApplicationQuit() { CloseConnection(); }

    void Update()
    {
        if (useKeyboardDebug)
        {
            ProcessKeyboardDebug();
            return;
        }

        while (messageQueue.TryDequeue(out string message))
        {
            rawDataDisplay = message;
            ParseData(message);
        }
    }

    /// <summary>
    /// 無硬體時的鍵盤模擬（useKeyboardDebug == true）
    /// WASD  → 左搖桿 (JoyX / JoyY)  — 控制無人機移動
    /// 方向鍵 → 右搖桿 (JoyRX / JoyRY) — 控制攝像機旋轉/縮放
    /// Space → SW (總開關)
    /// G/H   → KnobSpeed
    /// </summary>
    private void ProcessKeyboardDebug()
    {
        if (WorldState.Instance == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // ── 左搖桿 (無人機移動) ──────────────────────────────────────
        int joyX = 512, joyY = 512;
        if (kb.aKey.isPressed) joyX = 0;
        else if (kb.dKey.isPressed) joyX = 1023;
        if (kb.sKey.isPressed) joyY = 0;
        else if (kb.wKey.isPressed) joyY = 1023;
        WorldState.Instance.JoyX = joyX;
        WorldState.Instance.JoyY = joyY;

        // ── 右搖桿 (攝影機旋轉) ──────────────────────────────────────
        int joyRX = 512, joyRY = 512;
        if (kb.leftArrowKey.isPressed)  joyRX = 0;
        else if (kb.rightArrowKey.isPressed) joyRX = 1023;
        if (kb.downArrowKey.isPressed)  joyRY = 0;   // 縮小
        else if (kb.upArrowKey.isPressed)    joyRY = 1023; // 放大
        WorldState.Instance.JoyRX = joyRX;
        WorldState.Instance.JoyRY = joyRY;

        // ── 總開關 Space ────────────────────────────────────────────
        if (kb.spaceKey.wasPressedThisFrame)
            WorldState.Instance.Switch = (WorldState.Instance.Switch == 0) ? 1 : 0;

        // ── 速度旋鈕 G(+) / H(-) ─────────────────────────────────────
        int speedStep = 50;
        if (kb.gKey.wasPressedThisFrame)
            WorldState.Instance.KnobSpeed = Mathf.Clamp(WorldState.Instance.KnobSpeed + speedStep, 0, 1023);
        if (kb.hKey.wasPressedThisFrame)
            WorldState.Instance.KnobSpeed = Mathf.Clamp(WorldState.Instance.KnobSpeed - speedStep, 0, 1023);

        // ── 放置火源 (左搖桿按下模擬：鍵盤 E 替代) ──────────────────
        WorldState.Instance.JoyLButton = kb.eKey.isPressed ? 1 : 0;

        rawDataDisplay = $"KB: JoyX={joyX} JoyY={joyY} JoyRX={joyRX} JoyRY={joyRY} SW={WorldState.Instance.Switch} KS={WorldState.Instance.KnobSpeed}";
    }

    private void StartConnection()
    {
        if (isRunning) return;
        try {
            stream = new SerialPort(portName, baudRate);
            stream.ReadTimeout = 10; stream.DtrEnable = true; stream.RtsEnable = true; stream.Open();
            isRunning = true;
            readThread = new Thread(ReadLoop);
            readThread.IsBackground = true; readThread.Start();
            Debug.Log($"[Serial] 連線成功: {portName}");
        } catch (Exception e) { Debug.LogWarning($"連線失敗: {e.Message}"); useKeyboardDebug = true; }
    }

    private void CloseConnection()
    {
        isRunning = false;
        if (readThread != null && readThread.IsAlive) readThread.Join(100);
        if (stream != null && stream.IsOpen) { try { stream.Close(); } catch {} stream = null; }
    }

    private void ReadLoop()
    {
        string buffer = "";
        while (isRunning && stream != null && stream.IsOpen) {
            try {
                string chunk = stream.ReadExisting();
                if (!string.IsNullOrEmpty(chunk)) {
                    buffer += chunk;
                    int idx;
                    while ((idx = buffer.IndexOf('\n')) >= 0) {
                        string line = buffer.Substring(0, idx).Trim();
                        buffer = buffer.Substring(idx + 1);
                        if (!string.IsNullOrEmpty(line)) messageQueue.Enqueue(line);
                    }
                }
                Thread.Sleep(10);
            } catch {}
        }
    }

    private void ParseData(string data)
    {
        if (WorldState.Instance == null) return;
        try {
            string[] parts = data.Split(',');
            foreach (var part in parts) {
                string[] kv = part.Split(':');
                if (kv.Length == 2) {
                    string key = kv[0].Trim();
                    if (int.TryParse(kv[1].Trim(), out int val)) {
                        switch (key) {
                            case "LX": WorldState.Instance.JoyX = val; break;
                            case "LY": WorldState.Instance.JoyY = val; break;
                            case "RX": WorldState.Instance.JoyRX = val; break;
                            case "RY": WorldState.Instance.JoyRY = val; break;
                            case "KA": WorldState.Instance.KnobHeight = val; break;
                            case "KB": WorldState.Instance.KnobSpeed = val; break;
                            case "SW": WorldState.Instance.Switch = val; break;
                            case "CR": WorldState.Instance.CamBtn = val; break;
                            case "FIRE": 
                                if (val == 1 && lastFireBtn == 0) WorldState.Instance.IsFireTriggered = true;
                                lastFireBtn = val; break;
                            case "RST":
                                if (val == 1 && lastResetBtn == 0) WorldState.Instance.IsResetTriggered = true;
                                lastResetBtn = val; break;
                            case "BLK":
                                if (val == 1 && lastBlkBtn == 0) WorldState.Instance.IsTransparentTriggered = true;
                                lastBlkBtn = val; break;
                            case "JL":
                                WorldState.Instance.JoyLButton = val; break;
                        }
                    }
                }
            }
        } catch {}
    }
}