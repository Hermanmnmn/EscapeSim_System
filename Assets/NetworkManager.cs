using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    [Header("Server Settings")]
    public int port = 8888;

    [Header("Debug (按此測試)")]
    public bool TestLeft = false;
    public bool TestRight = false;
    public bool TestStop = false;

    private TcpListener server;
    private Thread serverThread;
    private List<TcpClient> clients = new List<TcpClient>();
    private bool isRunning = false;

    public static NetworkManager Instance;

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

    void Update()
    {
        // 1. 鍵盤測試
        if (Input.GetKeyDown(KeyCode.Alpha1)) SendToAll("DIR:L");
        if (Input.GetKeyDown(KeyCode.Alpha2)) SendToAll("DIR:R");
        if (Input.GetKeyDown(KeyCode.Alpha3)) SendToAll("DIR:S");

        // 2. Inspector 按鈕測試 (打勾後自動發送並取消勾選)
        if (TestLeft) { SendToAll("DIR:L"); TestLeft = false; }
        if (TestRight) { SendToAll("DIR:R"); TestRight = false; }
        if (TestStop) { SendToAll("DIR:S"); TestStop = false; }
    }

    private void StartServer()
    {
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;

            serverThread = new Thread(ListenForClients);
            serverThread.IsBackground = true;
            serverThread.Start();

            Debug.Log($"[TCP] 伺服器啟動於 Port {port}...");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TCP Error] {e.Message}");
        }
    }

    private void ListenForClients()
    {
        while (isRunning)
        {
            try
            {
                TcpClient newClient = server.AcceptTcpClient();
                lock (clients)
                {
                    clients.Add(newClient);
                }
                Debug.Log($"[TCP] 新裝置連線! IP: {((IPEndPoint)newClient.Client.RemoteEndPoint).Address}");
            }
            catch { }
        }
    }

    public void SendToAll(string message)
    {
        if (!message.EndsWith("\n")) message += "\n";
        byte[] data = Encoding.UTF8.GetBytes(message);

        lock (clients)
        {
            // 移除斷線的
            clients.RemoveAll(c => !c.Connected);

            if (clients.Count == 0)
            {
                Debug.LogWarning("⚠️ 沒有連線的 ESP32，無法發送指令！");
                return;
            }

            foreach (var client in clients)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(data, 0, data.Length);
                    Debug.Log($"[發送成功] -> {message.Trim()}");
                }
                catch
                {
                    Debug.LogWarning("發送失敗");
                }
            }
        }
    }

    private void StopServer()
    {
        isRunning = false;
        if (server != null) server.Stop();
        if (serverThread != null) serverThread.Abort();
        foreach (var c in clients) c.Close();
    }
}