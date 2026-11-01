using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System;
using System.Threading;

[System.Serializable]
public class JointData
{
    public string name;
    public float[] rot; // [x, y, z, w]
}

[System.Serializable]
public class FrameData
{
    public int seq;        // Sequence number
    public float[] root_pos;
    public List<JointData> joints;
}

/// <summary>
/// Receives animation data from DART streamer via UDP and applies to Unity Humanoid avatars.
/// Features frame interpolation for smooth motion even with network jitter.
/// </summary>
public class MotionReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public string host = "127.0.0.1";
    public int port = 8080;
    
    [Header("SMPL Avatar (Reference)")]
    [Tooltip("The SMPL for Unity avatar - receives direct SMPL rotations")]
    public Animator smplAnimator;
    public float smplPositionScale = 1.0f;
    
    [Header("Standard Avatar (Retargeted)")]
    [Tooltip("Standard Unity humanoid avatar - pose is copied from SMPL via HumanPose")]
    public Animator standardAnimator;
    public float standardPositionScale = 1.0f;
    [Tooltip("Position offset for side-by-side comparison")]
    public Vector3 standardPositionOffset = new Vector3(1.5f, 0, 0);
    
    [Header("Interpolation Settings")]
    [Tooltip("Enable smooth interpolation between frames")]
    public bool enableInterpolation = true;
    [Tooltip("Interpolation smoothness (0.1 = very smooth/laggy, 1.0 = direct/responsive)")]
    [Range(0.1f, 1.0f)]
    public float interpolationSpeed = 0.5f;
    
    [Header("Root Rotation Correction")]
    private float rootCorrectionX = -90f;
    private float rootCorrectionY = 0f;
    private float rootCorrectionZ = 0f;
    
    [Header("Recording (Optional)")]
    public MotionRecorder recorder;
    
    [Header("Debug")]
    public bool debugMode = false;
    public bool showFPS = true;
    
    // UDP Socket
    private UdpClient udpClient;
    private IPEndPoint serverEndpoint;
    
    // Background thread for network reading
    private Thread networkThread;
    private volatile bool isRunning = false;
    
    // Thread-safe frame storage
    private volatile string latestFrameJson = null;
    private readonly object frameLock = new object();
    
    // Stats
    private int framesReceived = 0;
    private int framesProcessed = 0;
    private int lastSeq = -1;
    private int droppedFrames = 0;
    
    // FPS tracking
    private float fpsUpdateInterval = 1.0f;
    private float fpsTimer = 0f;
    private int fpsFrameCount = 0;
    private float currentFPS = 0f;
    private float currentReceiveFPS = 0f;
    private int receivedThisSecond = 0;

    // Bone Mapping for SMPL joints to Unity Humanoid
    private Dictionary<string, HumanBodyBones> boneMap;
    
    // T-pose storage for SMPL avatar
    private Dictionary<HumanBodyBones, Quaternion> smplTPose;
    
    // HumanPose handlers for muscle-space retargeting
    private HumanPoseHandler smplPoseHandler;
    private HumanPoseHandler standardPoseHandler;
    private HumanPose humanPose;
    
    // Interpolation state - store current and target poses
    private Dictionary<HumanBodyBones, Quaternion> currentRotations;
    private Dictionary<HumanBodyBones, Quaternion> targetRotations;
    private Vector3 currentPosition;
    private Vector3 targetPosition;
    private bool hasReceivedFirstFrame = false;

    void Start()
    {
        InitializeBoneMap();
        InitializeInterpolationState();
        CaptureSmplTPose();
        InitializeHumanPoseHandlers();
        Connect();
    }
    
    void InitializeInterpolationState()
    {
        currentRotations = new Dictionary<HumanBodyBones, Quaternion>();
        targetRotations = new Dictionary<HumanBodyBones, Quaternion>();
        currentPosition = Vector3.zero;
        targetPosition = Vector3.zero;
    }
    
    void InitializeHumanPoseHandlers()
    {
        if (smplAnimator != null && smplAnimator.avatar != null && smplAnimator.avatar.isHuman)
        {
            smplPoseHandler = new HumanPoseHandler(smplAnimator.avatar, smplAnimator.transform);
            Debug.Log("[MotionReceiver] Created HumanPoseHandler for SMPL avatar");
        }
        
        if (standardAnimator != null && standardAnimator.avatar != null && standardAnimator.avatar.isHuman)
        {
            standardPoseHandler = new HumanPoseHandler(standardAnimator.avatar, standardAnimator.transform);
            Debug.Log("[MotionReceiver] Created HumanPoseHandler for Standard avatar");
        }
        
        humanPose = new HumanPose();
    }

    void InitializeBoneMap()
    {
        boneMap = new Dictionary<string, HumanBodyBones>
        {
            ["Hips"] = HumanBodyBones.Hips,
            ["Spine"] = HumanBodyBones.Spine,
            ["Chest"] = HumanBodyBones.Chest,
            ["UpperChest"] = HumanBodyBones.UpperChest,
            ["Neck"] = HumanBodyBones.Neck,
            ["Head"] = HumanBodyBones.Head,
            
            ["LeftUpperLeg"] = HumanBodyBones.LeftUpperLeg,
            ["LeftLowerLeg"] = HumanBodyBones.LeftLowerLeg,
            ["LeftFoot"] = HumanBodyBones.LeftFoot,
            ["LeftToes"] = HumanBodyBones.LeftToes,
            
            ["RightUpperLeg"] = HumanBodyBones.RightUpperLeg,
            ["RightLowerLeg"] = HumanBodyBones.RightLowerLeg,
            ["RightFoot"] = HumanBodyBones.RightFoot,
            ["RightToes"] = HumanBodyBones.RightToes,
            
            ["LeftShoulder"] = HumanBodyBones.LeftShoulder,
            ["LeftUpperArm"] = HumanBodyBones.LeftUpperArm,
            ["LeftLowerArm"] = HumanBodyBones.LeftLowerArm,
            ["LeftHand"] = HumanBodyBones.LeftHand,
            
            ["RightShoulder"] = HumanBodyBones.RightShoulder,
            ["RightUpperArm"] = HumanBodyBones.RightUpperArm,
            ["RightLowerArm"] = HumanBodyBones.RightLowerArm,
            ["RightHand"] = HumanBodyBones.RightHand
        };
    }
    
    void CaptureSmplTPose()
    {
        smplTPose = new Dictionary<HumanBodyBones, Quaternion>();
        
        if (smplAnimator == null) return;
        
        foreach (var kvp in boneMap)
        {
            Transform bone = smplAnimator.GetBoneTransform(kvp.Value);
            if (bone != null)
            {
                smplTPose[kvp.Value] = bone.localRotation;
            }
        }
        
        Debug.Log($"[MotionReceiver] Captured SMPL T-pose: {smplTPose.Count} bones");
    }

    void Connect()
    {
        try 
        {
            serverEndpoint = new IPEndPoint(IPAddress.Parse(host), port);
            udpClient = new UdpClient();
            
            // Send registration message to server
            byte[] registerMsg = Encoding.UTF8.GetBytes("register");
            udpClient.Send(registerMsg, registerMsg.Length, serverEndpoint);
            
            Debug.Log($"[MotionReceiver] Registered with DART UDP server at {host}:{port}");
            
            // Start background network reading thread
            isRunning = true;
            networkThread = new Thread(NetworkReadLoop);
            networkThread.IsBackground = true;
            networkThread.Start();
            Debug.Log("[MotionReceiver] Started background UDP receive thread");
        } 
        catch (Exception e) 
        {
            Debug.LogError($"[MotionReceiver] Connection failed: {e.Message}");
        }
    }
    
    void NetworkReadLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        
        while (isRunning && udpClient != null)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEP);
                string json = Encoding.UTF8.GetString(data);
                
                if (!string.IsNullOrEmpty(json))
                {
                    lock (frameLock)
                    {
                        latestFrameJson = json;
                        framesReceived++;
                        receivedThisSecond++;
                    }
                }
            }
            catch (SocketException se)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"[MotionReceiver] UDP receive error: {se.Message}");
                }
                break;
            }
            catch (Exception e)
            {
                if (isRunning)
                {
                    Debug.LogWarning($"[MotionReceiver] Error: {e.Message}");
                }
                break;
            }
        }
        
        Debug.Log("[MotionReceiver] Network thread exited");
    }

    void Update()
    {
        // Update FPS counter
        UpdateFPS();
        
        // Get latest frame from background thread (non-blocking)
        string frameJson = null;
        lock (frameLock)
        {
            frameJson = latestFrameJson;
            latestFrameJson = null;
        }
        
        if (frameJson != null)
        {
            if (debugMode) Debug.Log($"[DART] Processing frame (received: {framesReceived}, processed: {framesProcessed}, dropped: {droppedFrames})");
            
            if (recorder != null)
            {
                recorder.RecordFrame(frameJson);
            }
            
            // Parse and update target pose
            UpdateTargetPose(frameJson);
            framesProcessed++;
            fpsFrameCount++;
        }
        
        // Apply interpolated pose every frame
        if (hasReceivedFirstFrame)
        {
            ApplyInterpolatedPose();
        }
    }
    
    void UpdateFPS()
    {
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= fpsUpdateInterval)
        {
            currentFPS = fpsFrameCount / fpsTimer;
            
            lock (frameLock)
            {
                currentReceiveFPS = receivedThisSecond / fpsTimer;
                receivedThisSecond = 0;
            }
            
            fpsFrameCount = 0;
            fpsTimer = 0f;
            
            if (showFPS)
            {
                Debug.Log($"[MotionReceiver FPS] Process: {currentFPS:F1} fps | Receive: {currentReceiveFPS:F0} Hz | Dropped: {droppedFrames}");
            }
        }
    }

    void UpdateTargetPose(string json)
    {
        FrameData data = JsonUtility.FromJson<FrameData>(json);
        if (data == null) return;
        
        // Track sequence numbers
        if (lastSeq >= 0 && data.seq > lastSeq + 1)
        {
            droppedFrames += (data.seq - lastSeq - 1);
            if (debugMode) Debug.LogWarning($"[MotionReceiver] Dropped {data.seq - lastSeq - 1} frames");
        }
        lastSeq = data.seq;

        // Update target position
        if (data.root_pos != null && data.root_pos.Length == 3)
        {
            Vector3 smplPos = new Vector3(data.root_pos[0], data.root_pos[1], data.root_pos[2]);
            // Convert from SMPL coordinate system (Z-up) to Unity (Y-up)
            targetPosition = new Vector3(smplPos.x, smplPos.z, -smplPos.y);
        }

        // Update target rotations
        Quaternion rootCorrection = Quaternion.Euler(rootCorrectionX, rootCorrectionY, rootCorrectionZ);
        
        foreach (var joint in data.joints)
        {
            if (!boneMap.ContainsKey(joint.name)) continue;
            
            HumanBodyBones boneType = boneMap[joint.name];
            
            Quaternion smplRot = new Quaternion(joint.rot[0], joint.rot[1], joint.rot[2], joint.rot[3]);
            smplRot = ConvertRotationFromSMPL(smplRot);
            
            Quaternion finalRot = smplTPose.ContainsKey(boneType) 
                ? smplTPose[boneType] * smplRot 
                : smplRot;
            
            if (boneType == HumanBodyBones.Hips)
            {
                finalRot = rootCorrection * finalRot;
            }
            
            targetRotations[boneType] = finalRot;
            
            // Initialize current rotation on first frame
            if (!hasReceivedFirstFrame)
            {
                currentRotations[boneType] = finalRot;
            }
        }
        
        if (!hasReceivedFirstFrame)
        {
            currentPosition = targetPosition;
            hasReceivedFirstFrame = true;
        }
    }
    
    void ApplyInterpolatedPose()
    {
        if (smplAnimator == null) return;
        
        // Calculate interpolation factor
        float t = enableInterpolation ? interpolationSpeed : 1.0f;
        
        // Use Time.deltaTime to make interpolation frame-rate independent
        // Multiply by a base factor to control responsiveness
        float smoothFactor = 1.0f - Mathf.Pow(1.0f - t, Time.deltaTime * 60f);
        
        // Interpolate position
        currentPosition = Vector3.Lerp(currentPosition, targetPosition, smoothFactor);
        smplAnimator.transform.position = currentPosition * smplPositionScale;
        
        // Interpolate rotations
        foreach (var kvp in targetRotations)
        {
            HumanBodyBones boneType = kvp.Key;
            Quaternion targetRot = kvp.Value;
            
            Transform bone = smplAnimator.GetBoneTransform(boneType);
            if (bone == null) continue;
            
            if (currentRotations.ContainsKey(boneType))
            {
                currentRotations[boneType] = Quaternion.Slerp(currentRotations[boneType], targetRot, smoothFactor);
                bone.localRotation = currentRotations[boneType];
            }
            else
            {
                currentRotations[boneType] = targetRot;
                bone.localRotation = targetRot;
            }
        }
        
        // Copy pose from SMPL to Standard avatar via HumanPose
        if (smplPoseHandler != null && standardPoseHandler != null)
        {
            smplPoseHandler.GetHumanPose(ref humanPose);
            standardPoseHandler.SetHumanPose(ref humanPose);
            
            if (standardAnimator != null)
            {
                standardAnimator.transform.position = currentPosition * standardPositionScale + standardPositionOffset;
            }
        }
    }
    
    Quaternion ConvertRotationFromSMPL(Quaternion q)
    {
        return new Quaternion(-q.x, q.y, -q.z, q.w);
    }

    void OnDestroy()
    {
        isRunning = false;
        
        if (udpClient != null && serverEndpoint != null)
        {
            try
            {
                byte[] unregisterMsg = Encoding.UTF8.GetBytes("unregister");
                udpClient.Send(unregisterMsg, unregisterMsg.Length, serverEndpoint);
            }
            catch { }
        }
        
        if (networkThread != null && networkThread.IsAlive)
        {
            if (udpClient != null)
            {
                udpClient.Close();
            }
            networkThread.Join(100);
        }
        
        Debug.Log($"[MotionReceiver] Shutdown. Total received: {framesReceived}, processed: {framesProcessed}, dropped: {droppedFrames}");
    }
    
    void OnGUI()
    {
        if (showFPS)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 14;
            style.normal.textColor = Color.white;
            
            string fpsText = $"Motion Receiver: {currentFPS:F1} fps | UDP: {currentReceiveFPS:F0} Hz | Dropped: {droppedFrames}";
            GUI.Label(new Rect(10, 10, 400, 25), fpsText, style);
            
            string interpText = $"Interpolation: {(enableInterpolation ? "ON" : "OFF")} | Speed: {interpolationSpeed:F2}";
            GUI.Label(new Rect(10, 35, 400, 25), interpText, style);
        }
    }
}
