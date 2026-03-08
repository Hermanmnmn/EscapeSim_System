using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class MapRenderer : MonoBehaviour
{
    [Header("設定")]
    public GameObject pointPrefab;
    public int port = 8889; // 記得是 8889
    
    private TcpListener server;
    private Thread serverThread;
    private bool isRunning = false;

    void Start()
    {
        StartServer();
    }

    void OnDisable()
    {
        StopServer();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    // 這裡我們不需要 Update，因為生成工作已經交給 SystemManager 了
    // MapRenderer 只負責收資料，丟給 WorldState

    private void StartServer()
    {
        if (isRunning) return; // 防止重複啟動
        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;
            serverThread = new Thread(ListenLoop);
            serverThread.IsBackground = true;
            serverThread.Start();
            Debug.Log($"[Map] 地圖接收伺服器啟動: {port}");
        }
        catch (Exception e) { Debug.LogWarning($"[Map] 伺服器啟動失敗: {e.Message}"); }
    }

    private void ListenLoop()
    {
        while (isRunning && server != null)
        {
            try
            {
                if (!server.Pending())
                {
                    Thread.Sleep(50);
                    continue;
                }

                TcpClient client = server.AcceptTcpClient();
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[1024 * 100]; 
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    ParseAndQueue(json);
                }
                client.Close();
            }
            catch (SocketException) { } // 伺服器停止時正常拋出
            catch (Exception) { }
        }
    }

    private void ParseAndQueue(string json)
    {
        if (WorldState.Instance == null) return;
        if (!json.Contains("obstacles_3d")) return;

        try 
        {
            // 暴力解析 JSON
            string clean = json.Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "").Replace("\"", "").Replace("obstacles_3d", "").Replace("type", "").Replace("3d_pointcloud", "").Replace(":", "");
            string[] parts = clean.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < parts.Length - 2; i += 3)
            {
                if (float.TryParse(parts[i], out float x) &&
                    float.TryParse(parts[i+1], out float y) &&
                    float.TryParse(parts[i+2], out float z))
                {
                    WorldState.Instance.IncomingMapPoints.Enqueue(new Vector3(x, y, -z)); 
                }
            }
        }
        catch { }
    }

    private void StopServer()
    {
        isRunning = false;

        // 安全停止 TcpListener
        if (server != null)
        {
            try { server.Stop(); }
            catch { }
        }

        // 安全等待執行緒結束 (不使用已棄用的 Thread.Abort)
        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Join(200); // 等待最多 200ms
        }

        server = null;
        serverThread = null;
    }
}