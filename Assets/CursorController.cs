using UnityEngine;

public class CursorController : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 10f;
    public float mapSize = 20f;
    public float heightScale = 10f;

    void Update()
    {
        // 1. 安全檢查：避免 SerialController 尚未初始化時導致錯誤
        if (SerialController.Instance == null) return;

        // 2. 讀取數據 (0~1023)
        int rawX = SerialController.Instance.JoyX;
        int rawY = SerialController.Instance.JoyY;
        int rawKnob = SerialController.Instance.Knob;

        // 3. 處理搖桿偏移量 (-1 ~ 1)
        float inputX = -(rawX - 512) / 512f;
        float inputZ = (rawY - 512) / 512f;

        // 死區處理：使用優化後的條件判斷
        if (Mathf.Abs(inputX) < 0.15f) inputX = 0;
        if (Mathf.Abs(inputZ) < 0.15f) inputZ = 0;

        // 4. 計算新位置
        Vector3 currentPos = transform.position;

        // 計算水平移動 (X, Z)
        currentPos.x += inputX * moveSpeed * Time.deltaTime;
        currentPos.z += inputZ * moveSpeed * Time.deltaTime;

        // 5. 處理旋鈕 (高度 Y)
        // 使用絕對高度映射
        float targetHeight = (rawKnob / 1023f) * heightScale;
        currentPos.y = targetHeight;

        // 6. 限制邊界 (Clamp)
        currentPos.x = Mathf.Clamp(currentPos.x, -mapSize, mapSize);
        currentPos.z = Mathf.Clamp(currentPos.z, -mapSize, mapSize);

        // 最後統一套用位置
        transform.position = currentPos;
    }
}