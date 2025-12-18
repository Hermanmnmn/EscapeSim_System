using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;       // 要跟隨的目標 (GodCursor)
    public Vector3 offset = new Vector3(0, 15, -10); // 鏡頭相對位置 (高空俯視)
    public float smoothSpeed = 0.125f; // 平滑係數 (越小越慢/越滑)

    void LateUpdate()
    {
        if (target == null) return;

        // 1. 計算目標位置 (球的位置 + 偏移量)
        Vector3 desiredPosition = target.position + offset;

        // 2. 使用 Lerp (線性插值) 進行平滑移動
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);

        // 3. 更新鏡頭位置
        transform.position = smoothedPosition;

        // 4. (選用) 永遠看著球
        transform.LookAt(target);
    }
}