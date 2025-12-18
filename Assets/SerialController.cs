using System;
using System.Collections.Concurrent; // 引入執行緒安全的佇列
using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class SerialController : MonoBehaviour
{
    [Header("🔧 除錯模式")]
    public bool useKeyboardDebug = false; // 預設關閉，用實體搖桿

    [Header("COM Port Settings")]
    public string portName = "COM3";
    public int baudRate = 115200;

    public static SerialController Instance;

    [Header("Live Data")]
    public int JoyX = 512;
    public int JoyY = 512;
    public int Knob = 0;
    public int Switch = 0;
    public int Button = 0;
    public int Fire = 0;

    [Header("Debug")]
    public string rawDataDisplay = "";

    private SerialPort stream;
    private Thread readThread;
    private bool isRunning = false;

    // 這是防閃退的神器：執行緒安全佇列
    private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (!useKeyboardDebug) StartConnection();
    }

    void Update()
    {
        // 1. 鍵盤模式
        if (useKeyboardDebug)
        {
            ProcessKeyboardInput();
            return;
        }

        // 2. 實體模式：從「信箱」裡面拿信出來處理
        // 只有在 Update (主執行緒) 裡，才更新 Unity 的變數
        while (messageQueue.TryDequeue(out string message))
        {
            rawDataDisplay = message; // 更新 Inspector (現在安全了)
            ParseData(message);       // 解析數據 (現在安全了)
        }
    }

    void OnDisable() { CloseConnection(); }
    void OnApplicationQuit() { CloseConnection(); }

    private void StartConnection()
    {
        if (isRunning) return;
        try
        {
            stream = new SerialPort(portName, baudRate);
            stream.ReadTimeout = 10;
            stream.DtrEnable = true;
            stream.RtsEnable = true;
            stream.Open();

            isRunning = true;
            readThread = new Thread(ReadSerialLoop);
            readThread.IsBackground = true;
            readThread.Start();
            Debug.Log($"[Serial] 連線成功: {portName}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Serial] 連線失敗 (切換為鍵盤模式): {e.Message}");
            useKeyboardDebug = true;
        }
    }

    private void CloseConnection()
    {
        isRunning = false;
        // 給執行緒一點時間自殺
        if (readThread != null && readThread.IsAlive) readThread.Join(100);
        if (stream != null && stream.IsOpen) { try { stream.Close(); } catch { } stream = null; }
    }

    // === 後台執行緒 (只負責收信，不做任何解析) ===
    private void ReadSerialLoop()
    {
        string buffer = "";
        while (isRunning && stream != null && stream.IsOpen)
        {
            try
            {
                string chunk = stream.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    buffer += chunk;
                    int newlineIndex;
                    while ((newlineIndex = buffer.IndexOf('\n')) >= 0)
                    {
                        string line = buffer.Substring(0, newlineIndex).Trim();
                        buffer = buffer.Substring(newlineIndex + 1);

                        if (!string.IsNullOrEmpty(line))
                        {
                            // 關鍵：不要在這裡解析！丟進 Queue 就好！
                            messageQueue.Enqueue(line);
                        }
                    }
                }
                Thread.Sleep(10); // 讓 CPU 休息，防當機
            }
            catch { }
        }
    }

    private void ParseData(string data)
    {
        try
        {
            string[] parts = data.Split(',');
            foreach (var part in parts)
            {
                string[] kv = part.Split(':');
                if (kv.Length == 2)
                {
                    string key = kv[0].Trim();
                    if (int.TryParse(kv[1].Trim(), out int value))
                    {
                        switch (key)
                        {
                            case "J_X": JoyX = value; break;
                            case "J_Y": JoyY = value; break;
                            case "KNOB": Knob = value; break;
                            case "SW": Switch = value; break;
                            case "J_BTN": Button = value; break;
                            case "FIRE": Fire = value; break;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void ProcessKeyboardInput()
    {
        JoyX = 512; JoyY = 512;
        if (Input.GetKey(KeyCode.A)) JoyX = 0;
        if (Input.GetKey(KeyCode.D)) JoyX = 1023;
        if (Input.GetKey(KeyCode.W)) JoyY = 1023;
        if (Input.GetKey(KeyCode.S)) JoyY = 0;
        if (Input.GetKey(KeyCode.Q)) Knob = Mathf.Clamp(Knob + 10, 0, 1023);
        if (Input.GetKey(KeyCode.E)) Knob = Mathf.Clamp(Knob - 10, 0, 1023);
        Switch = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }
}