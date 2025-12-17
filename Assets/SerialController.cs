using System;
using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class SerialController : MonoBehaviour
{
    [Header("🔧 除錯模式 (打勾就不用接 Micro:bit)")]
    public bool useKeyboardDebug = true; // <--- 預設打勾！我們先用鍵盤測！

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

    private SerialPort stream;
    private Thread readThread;
    private bool isRunning = false;
    private object lockObj = new object();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (!useKeyboardDebug)
        {
            StartConnection();
        }
    }

    void Update()
    {
        // 如果開啟鍵盤除錯，就用 WASD 假裝是搖桿
        if (useKeyboardDebug)
        {
            // 歸零
            JoyX = 512;
            JoyY = 512;

            // 鍵盤模擬搖桿 X/Y
            if (Input.GetKey(KeyCode.A)) JoyX = 0;    // 左
            if (Input.GetKey(KeyCode.D)) JoyX = 1023; // 右
            if (Input.GetKey(KeyCode.W)) JoyY = 1023; // 上 (前)
            if (Input.GetKey(KeyCode.S)) JoyY = 0;    // 下 (後)

            // 鍵盤模擬旋鈕 (Q/E 控制高度)
            if (Input.GetKey(KeyCode.Q)) Knob += 10;
            if (Input.GetKey(KeyCode.E)) Knob -= 10;
            Knob = Mathf.Clamp(Knob, 0, 1023);

            // 鍵盤模擬開關 (Space)
            Switch = Input.GetKey(KeyCode.Space) ? 1 : 0;
        }
    }

    // 當 Unity 關閉或腳本被停用時，強制殺死連線
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
            Debug.LogWarning($"[Serial] 無法連線 (可能被佔用，已自動切換為鍵盤模式): {e.Message}");
            useKeyboardDebug = true; // 連線失敗就自動切換回鍵盤
        }
    }

    private void CloseConnection()
    {
        isRunning = false;
        if (readThread != null && readThread.IsAlive) readThread.Join(100);
        if (stream != null && stream.IsOpen) { try { stream.Close(); } catch { } stream = null; }
    }

    private void ReadSerialLoop()
    {
        while (isRunning && stream != null && stream.IsOpen)
        {
            try
            {
                string chunk = stream.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    lock (lockObj)
                    {
                        ParseData(chunk);
                    }
                }
                Thread.Sleep(15);
            }
            catch { }
        }
    }

    private string buffer = "";
    private void ParseData(string chunk)
    {
        buffer += chunk;
        int newlineIndex;
        while ((newlineIndex = buffer.IndexOf('\n')) >= 0)
        {
            string line = buffer.Substring(0, newlineIndex).Trim();
            buffer = buffer.Substring(newlineIndex + 1);
            if (!string.IsNullOrEmpty(line))
            {
                try
                {
                    string[] parts = line.Split(',');
                    foreach (var part in parts)
                    {
                        string[] kv = part.Split(':');
                        if (kv.Length == 2)
                        {
                            string key = kv[0].Trim();
                            if (int.TryParse(kv[1].Trim(), out int val))
                            {
                                switch (key)
                                {
                                    case "J_X": JoyX = val; break;
                                    case "J_Y": JoyY = val; break;
                                    case "KNOB": Knob = val; break;
                                    case "SW": Switch = val; break;
                                    case "J_BTN": Button = val; break;
                                    case "FIRE": Fire = val; break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }
}