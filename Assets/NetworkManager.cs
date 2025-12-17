using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 8888; // 憲法規定的 Port

    // 讓其他腳本呼叫發送指令
    public static NetworkManager Instance;

    private TcpListener server;
    private Thread serverThread;
    private bool isRunning = false;

    // 儲存 ESP32 的連線
    private TcpClient esp32Client;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        StartServer();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    private void StartServer()
    {
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;
            serverThread = new Thread(ListenLoop);
            serverThread.IsBackground = true;
            serverThread.Start();
            Debug.Log($"[TCP] 伺服器啟動於 Port {port}，等待 ESP32/iPhone 連線...");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TCP Error] 無法啟動伺服器: {e.Message}");
        }
    }

    private void StopServer()
    {
        isRunning = false;
        if (server != null) server.Stop();
        if (serverThread != null && serverThread.IsAlive) serverThread.Abort();
    }

    private void ListenLoop()
    {
        while (isRunning)
        {
            try
            {
                // 等待連線 (會卡住直到有人連進來)
                TcpClient client = server.AcceptTcpClient();
                Debug.Log("[TCP] 新裝置連線！");

                // 為了簡單起見，我們假設最後一個連進來的如果是 ESP32，就把它存起來
                // 這裡開一個新執行緒去處理這個客戶端
                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.IsBackground = true;
                clientThread.Start();
            }
            catch (SocketException) { break; }
        }
    }

    private void HandleClient(TcpClient client)
    {
        // 假設這是 ESP32，存起來以便稍後發送指令
        esp32Client = client;

        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            while (client.Connected)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                // 這裡未來會處理 iPhone 傳來的地圖 JSON
                // 目前先單純印出來
                // Debug.Log($"[Recv] {msg}");
            }
        }
        catch { }
        finally
        {
            Debug.Log("裝置斷線");
            client.Close();
        }
    }

    // === 公用功能：發送指令給 ESP32 ===
    // 在其他腳本呼叫 NetworkManager.Instance.SendToESP32("DIR:L");
    public void SendToESP32(string message)
    {
        if (esp32Client != null && esp32Client.Connected)
        {
            try
            {
                NetworkStream stream = esp32Client.GetStream();
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(data, 0, data.Length);
                Debug.Log($"[Sent to ESP32] {message}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("發送失敗: " + e.Message);
            }
        }
        else
        {
            Debug.LogWarning("ESP32 尚未連線！");
        }
    }
}