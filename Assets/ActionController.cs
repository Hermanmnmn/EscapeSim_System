using UnityEngine;

public class ActionController : MonoBehaviour
{
    public GameObject firePrefab; // 拖入火的 Prefab
    private bool isPressed = false; // 防止按住不放連續生成

    void Update()
    {
        if (SerialController.Instance == null) return;

        // 讀取按鈕狀態 (1 = 按下)
        int btnState = SerialController.Instance.Button;

        // 偵測「剛按下去」的那一瞬間 (防連點)
        if (btnState == 1 && !isPressed)
        {
            SpawnFire();
            isPressed = true;
        }
        else if (btnState == 0)
        {
            isPressed = false;
        }
    }

    void SpawnFire()
    {
        // 在球的位置生成火
        Instantiate(firePrefab, transform.position , Quaternion.identity);
        Debug.Log("🔥 火源已放置！");

        // (選做) 可以在這裡通知 ESP32 變燈
        // NetworkManager.Instance.SendToAll("DIR:L"); 
    }
}