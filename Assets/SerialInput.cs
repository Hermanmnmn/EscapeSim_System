using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System;

public class SerialInput : MonoBehaviour
{
    [Header("COM Port Settings")]
    public string portName = "COM3"; // 請確認 Port
    public int baudRate = 115200;

    [Header("Sensor Data")]
    public int joyX;
    public int joyY;
    public int knob;
    public int sw;
    public int jBtn;
    public int fire;
    public int uid;

    private SerialPort _serialPort;
    private Thread _readThread;
    private bool _isRunning = false;
    private bool _hasNewData = false;

    void Start()
    {
        OpenSerialPort();
    }

    void Update()
    {
        if (_hasNewData)
        {
            // 收到數據時才印出，避免 Console 洗版
            // Debug.Log($"[Micro:bit] X:{joyX} Y:{joyY} SW:{sw}"); 
            _hasNewData = false;
        }
    }

    // 這裡是最重要的修改：確保 Unity 關閉時，Serial Port 一定會斷開
    void OnApplicationQuit()
    {
        CloseSerialPort();
    }

    void OnDestroy()
    {
        CloseSerialPort();
    }

    private void OpenSerialPort()
    {
        if (_serialPort != null && _serialPort.IsOpen) return;

        try
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.ReadTimeout = 500; // 縮短 Timeout
            _serialPort.Open();

            _isRunning = true;
            _readThread = new Thread(ReadSerialLoop);
            _readThread.Start();

            Debug.Log($"<color=green>Serial Port {portName} Open Success!</color>");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Serial Error: {e.Message}");
        }
    }

    private void CloseSerialPort()
    {
        _isRunning = false;

        // 強制關閉 Stream，讓 Thread 跳出 ReadLine
        if (_serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }
            catch { }
        }

        // 等待 Thread 結束
        if (_readThread != null && _readThread.IsAlive)
        {
            _readThread.Join(200);
        }
    }

    private void ReadSerialLoop()
    {
        while (_isRunning)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    // 安全機制：只有當有資料在緩衝區時才讀取
                    // 這樣可以避免 Thread 卡死在 ReadLine
                    /* 
                       注意：有些舊版 Driver 可能不支援 BytesToRead，
                       如果這裡報錯，請刪除 if 判斷，改回直接 ReadLine 
                    */
                    // if (_serialPort.BytesToRead > 0) 
                    {
                        string line = _serialPort.ReadLine();
                        Debug.Log($"[RAW DATA]: {line}");
                        if (!string.IsNullOrEmpty(line))
                        {
                            ParseData(line);
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                // 沒讀到資料，休息一下
                Thread.Sleep(10);
            }
            catch (System.Exception)
            {
                // 發生錯誤或斷線，稍微休息避免死迴圈
                Thread.Sleep(100);
            }
        }
    }

    private void ParseData(string rawData)
    {
        try
        {
            string[] parts = rawData.Split(',');
            foreach (string part in parts)
            {
                string[] kv = part.Split(':');
                if (kv.Length == 2)
                {
                    string key = kv[0].Trim();
                    int val = int.Parse(kv[1].Trim());

                    switch (key)
                    {
                        case "J_X": joyX = val; break;
                        case "J_Y": joyY = val; break;
                        case "KNOB": knob = val; break;
                        case "SW": sw = val; break;
                        case "J_BTN": jBtn = val; break;
                        case "FIRE": fire = val; break;
                    }
                }
            }
            _hasNewData = true;
        }
        catch { }
    }
}