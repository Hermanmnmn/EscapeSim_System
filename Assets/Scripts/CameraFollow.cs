using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    public enum CameraMode { Free, DroneThirdPerson, DroneFirstPerson, CCTV }

    [Header("模式設定")]
    public CameraMode currentMode = CameraMode.DroneThirdPerson;

    [Header("跟隨設定 (Drone)")]
    public Transform target;       // 放入 GodCursor
    public float smoothSpeed = 0.125f;

    [Header("右搖桿/滑鼠設定 (Orbit & Zoom)")]
    public float orbitSpeed = 100f; // 旋轉速度
    public float zoomSpeed = 20f;   // 縮放速度
    public float minZoom = 5f;      // 最近距離
    public float maxZoom = 40f;     // 最遠距離
    
    // 儲存目前的旋轉角度與距離
    private float currentYaw = 0f;
    private float currentPitch = 45f; 
    private float currentDistance = 15f; 

    [Header("自由視角設定 (Free Cam)")]
    public float freeMoveSpeed = 20f;
    public float freeLookSensitivity = 2f;

    [Header("多機位設定 (CCTV)")]
    public List<Transform> cctvPoints;
    private int currentCctvIndex = 0;

    void LateUpdate()
    {
        if (WorldState.Instance == null) return;

        // ── 1. 模式切換 ──
        HandleModeSwitch();

        // ── 2. 鏡頭邏輯執行 ──
        switch (currentMode)
        {
            case CameraMode.DroneThirdPerson: UpdateDroneThirdPerson(); break;
            case CameraMode.DroneFirstPerson: UpdateDroneFirstPerson(); break;
            case CameraMode.Free:             UpdateFreeCam();          break;
            case CameraMode.CCTV:             UpdateCCTV();             break;
        }
    }

    void HandleModeSwitch()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.vKey.wasPressedThisFrame)
        {
            currentMode = (currentMode == CameraMode.DroneThirdPerson) ? CameraMode.DroneFirstPerson : CameraMode.DroneThirdPerson;
        }
        
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            currentMode = (currentMode == CameraMode.Free) ? CameraMode.DroneThirdPerson : CameraMode.Free;
        }

        if (Keyboard.current.cKey.wasPressedThisFrame && cctvPoints != null && cctvPoints.Count > 0)
        {
            if (currentMode != CameraMode.CCTV)
            {
                currentMode = CameraMode.CCTV;
            }
            else
            {
                currentCctvIndex = (currentCctvIndex + 1) % cctvPoints.Count;
            }
        }
    }

    void UpdateDroneThirdPerson()
    {
        if (target == null) return;

        // A. 讀取右搖桿 (RX, RY) - 通常由方向鍵模擬
        float inputRX = (WorldState.Instance.JoyRX - 512) / 512f;
        float inputRY = (WorldState.Instance.JoyRY - 512) / 512f;

        if (Mathf.Abs(inputRX) < 0.15f) inputRX = 0;
        if (Mathf.Abs(inputRY) < 0.15f) inputRY = 0;

        currentYaw += inputRX * orbitSpeed * Time.unscaledDeltaTime;
        currentDistance -= inputRY * zoomSpeed * Time.unscaledDeltaTime;

        // B. 支援滑鼠右鍵旋轉與滾輪縮放
        if (Mouse.current != null)
        {
            if (Mouse.current.rightButton.isPressed)
            {
                currentYaw += Mouse.current.delta.x.ReadValue() * 0.1f * orbitSpeed * Time.unscaledDeltaTime;
            }
            
            float scroll = Mouse.current.scroll.y.ReadValue();
            if (Mathf.Abs(scroll) > 0.1f)
            {
                currentDistance -= scroll * 0.01f * zoomSpeed;
            }
        }

        currentDistance = Mathf.Clamp(currentDistance, minZoom, maxZoom);

        // 重置視角
        if (WorldState.Instance.CamBtn == 1)
        {
            currentYaw = 0f;
            currentPitch = 45f;
            currentDistance = 15f;
        }

        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -currentDistance);
        Vector3 desiredPosition = target.position + offset;
        
        // 使用更直觀的平滑插值，避免 Mathf.Exp 導致 T 值過小而卡住
        float t = Mathf.Clamp01(smoothSpeed * 50f * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, t);
        transform.LookAt(target);
    }

    void UpdateDroneFirstPerson()
    {
        if (target == null) return;
        // 稍微往下一點避免穿模，並跟隨無人機旋轉
        transform.position = target.position + new Vector3(0, -0.2f, 0);
        transform.rotation = target.rotation;
    }

    void UpdateFreeCam()
    {
        // 自由移動 (WASD + Q/E)
        Vector3 move = Vector3.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) move += transform.forward;
            if (Keyboard.current.sKey.isPressed) move -= transform.forward;
            if (Keyboard.current.aKey.isPressed) move -= transform.right;
            if (Keyboard.current.dKey.isPressed) move += transform.right;
            if (Keyboard.current.eKey.isPressed) move += Vector3.up;
            if (Keyboard.current.qKey.isPressed) move -= Vector3.up;
        }
        transform.position += move * freeMoveSpeed * Time.unscaledDeltaTime;

        // 自由旋轉 (按住右鍵)
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            float yaw = transform.eulerAngles.y + delta.x * freeLookSensitivity * 0.1f;
            float pitch = transform.eulerAngles.x - delta.y * freeLookSensitivity * 0.1f;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }
    }

    void UpdateCCTV()
    {
        if (cctvPoints == null || cctvPoints.Count == 0) return;
        Transform pt = cctvPoints[currentCctvIndex];
        if (pt != null)
        {
            transform.position = pt.position;
            transform.rotation = pt.rotation;
        }
    }
}