using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace BodyTrackingStreaming
{
    public class BodyDataReceiver : MonoBehaviour
    {
        [Header("Network Settings")]
        public int port = BodyTrackingNetworkStats.DefaultPort;
        public float connectionTimeout = 2.0f; // Seconds to wait before considering disconnected

        [Header("Avatar Mapping")]
        [Tooltip("The root transform of the avatar (usually Hips or a parent container)")]
        public Transform avatarRoot;
        [Tooltip("Bones mapped to StreamBodyJointId indices. (Body + Fingers)")]
        public List<Transform> avatarBones; 

        [Header("adjustments")]
        public Vector3 rootRotationOffset = new Vector3(0, 0, 0);
        public bool applyRootPosition = true;
        public bool applyRootRotation = true;
        public bool applyBoneRotations = true; 

        // Runtime State
        private UdpClient udpClient;
        private Thread receiveThread;
        private bool isRunning;
        private BodyData currentBodyData;
        private object dataLock = new object();
        private bool newData = false;
        private long lastPacketTicks;
        private bool isConnected = false;

        // Debug Visuals
        [Header("Debug")]
        public bool showDebugVisuals = false;
        public GameObject debugJointPrefab;
        public Transform debugRoot;
        private GameObject[] debugJoints = new GameObject[BodyTrackingNetworkStats.MaxBones];

        [ContextMenu("Auto Map Bones (Full Skeleton)")]
        public void AutoMapBones()
        {
            if (avatarRoot == null)
            {
                Debug.LogError("[BodyDataReceiver] Assign Avatar Root first!");
                return;
            }

            avatarBones = new List<Transform>(new Transform[BodyTrackingNetworkStats.MaxBones]);

            // Helper recursive search
            Transform[] allChildren = avatarRoot.GetComponentsInChildren<Transform>(true);
            Transform FindNode(string partialName)
            {
                foreach (var t in allChildren)
                {
                    if (t.name.Contains(partialName, StringComparison.OrdinalIgnoreCase))
                        return t;
                }
                return null;
            }

            // --- Body Core ---
            // Mapping based on typical OVR/Mixamo naming
            avatarBones[(int)StreamBodyJointId.Hips] = FindNode("Hips");
            avatarBones[(int)StreamBodyJointId.Spine] = FindNode("Spine");
            avatarBones[(int)StreamBodyJointId.Spine1] = FindNode("Spine1");
            avatarBones[(int)StreamBodyJointId.Spine2] = FindNode("Spine2");
            avatarBones[(int)StreamBodyJointId.Chest] = FindNode("Chest");
            avatarBones[(int)StreamBodyJointId.Neck] = FindNode("Neck");
            avatarBones[(int)StreamBodyJointId.Head] = FindNode("Head");

            avatarBones[(int)StreamBodyJointId.LeftShoulder] = FindNode("LeftShoulder");
            avatarBones[(int)StreamBodyJointId.LeftArm] = FindNode("LeftArm");
            avatarBones[(int)StreamBodyJointId.LeftForeArm] = FindNode("LeftForeArm");
            avatarBones[(int)StreamBodyJointId.LeftHand] = FindNode("LeftHand");

            avatarBones[(int)StreamBodyJointId.RightShoulder] = FindNode("RightShoulder");
            avatarBones[(int)StreamBodyJointId.RightArm] = FindNode("RightArm");
            avatarBones[(int)StreamBodyJointId.RightForeArm] = FindNode("RightForeArm");
            avatarBones[(int)StreamBodyJointId.RightHand] = FindNode("RightHand");

            avatarBones[(int)StreamBodyJointId.LeftUpLeg] = FindNode("LeftUpLeg");
            avatarBones[(int)StreamBodyJointId.LeftLeg] = FindNode("LeftLeg");
            avatarBones[(int)StreamBodyJointId.LeftFoot] = FindNode("LeftFoot");
            avatarBones[(int)StreamBodyJointId.LeftToeBase] = FindNode("LeftToeBase");

            avatarBones[(int)StreamBodyJointId.RightUpLeg] = FindNode("RightUpLeg");
            avatarBones[(int)StreamBodyJointId.RightLeg] = FindNode("RightLeg");
            avatarBones[(int)StreamBodyJointId.RightFoot] = FindNode("RightFoot");
            avatarBones[(int)StreamBodyJointId.RightToeBase] = FindNode("RightToeBase");

            // --- Left Hand Fingers ---
            avatarBones[(int)StreamBodyJointId.LeftHandThumb1] = FindNode("LeftHandThumb1");
            avatarBones[(int)StreamBodyJointId.LeftHandThumb2] = FindNode("LeftHandThumb2");
            avatarBones[(int)StreamBodyJointId.LeftHandThumb3] = FindNode("LeftHandThumb3");
            
            avatarBones[(int)StreamBodyJointId.LeftHandIndex1] = FindNode("LeftHandIndex1");
            avatarBones[(int)StreamBodyJointId.LeftHandIndex2] = FindNode("LeftHandIndex2");
            avatarBones[(int)StreamBodyJointId.LeftHandIndex3] = FindNode("LeftHandIndex3");

            avatarBones[(int)StreamBodyJointId.LeftHandMiddle1] = FindNode("LeftHandMiddle1");
            avatarBones[(int)StreamBodyJointId.LeftHandMiddle2] = FindNode("LeftHandMiddle2");
            avatarBones[(int)StreamBodyJointId.LeftHandMiddle3] = FindNode("LeftHandMiddle3");

            avatarBones[(int)StreamBodyJointId.LeftHandRing1] = FindNode("LeftHandRing1");
            avatarBones[(int)StreamBodyJointId.LeftHandRing2] = FindNode("LeftHandRing2");
            avatarBones[(int)StreamBodyJointId.LeftHandRing3] = FindNode("LeftHandRing3");

            avatarBones[(int)StreamBodyJointId.LeftHandPinky1] = FindNode("LeftHandPinky1");
            avatarBones[(int)StreamBodyJointId.LeftHandPinky2] = FindNode("LeftHandPinky2");
            avatarBones[(int)StreamBodyJointId.LeftHandPinky3] = FindNode("LeftHandPinky3");

            // --- Right Hand Fingers ---
            avatarBones[(int)StreamBodyJointId.RightHandThumb1] = FindNode("RightHandThumb1");
            avatarBones[(int)StreamBodyJointId.RightHandThumb2] = FindNode("RightHandThumb2");
            avatarBones[(int)StreamBodyJointId.RightHandThumb3] = FindNode("RightHandThumb3");
            
            avatarBones[(int)StreamBodyJointId.RightHandIndex1] = FindNode("RightHandIndex1");
            avatarBones[(int)StreamBodyJointId.RightHandIndex2] = FindNode("RightHandIndex2");
            avatarBones[(int)StreamBodyJointId.RightHandIndex3] = FindNode("RightHandIndex3");

            avatarBones[(int)StreamBodyJointId.RightHandMiddle1] = FindNode("RightHandMiddle1");
            avatarBones[(int)StreamBodyJointId.RightHandMiddle2] = FindNode("RightHandMiddle2");
            avatarBones[(int)StreamBodyJointId.RightHandMiddle3] = FindNode("RightHandMiddle3");

            avatarBones[(int)StreamBodyJointId.RightHandRing1] = FindNode("RightHandRing1");
            avatarBones[(int)StreamBodyJointId.RightHandRing2] = FindNode("RightHandRing2");
            avatarBones[(int)StreamBodyJointId.RightHandRing3] = FindNode("RightHandRing3");

            avatarBones[(int)StreamBodyJointId.RightHandPinky1] = FindNode("RightHandPinky1");
            avatarBones[(int)StreamBodyJointId.RightHandPinky2] = FindNode("RightHandPinky2");
            avatarBones[(int)StreamBodyJointId.RightHandPinky3] = FindNode("RightHandPinky3");

            Debug.Log("[BodyDataReceiver] Auto-Mapping complete. Verify indices manually.");
        }

        void Start()
        {
            // Initialize Data
            currentBodyData = new BodyData();

            // Initialize Debug Visuals
            if (showDebugVisuals && debugJointPrefab != null)
            {
                InitializeDebugVisuals();
            }

            StartReceiver();
        }

        void InitializeDebugVisuals()
        {
            Transform parent = debugRoot != null ? debugRoot : this.transform;
            for (int i = 0; i < BodyTrackingNetworkStats.MaxBones; i++)
            {
                debugJoints[i] = Instantiate(debugJointPrefab, parent);
                debugJoints[i].name = "DebugBone_" + i;
                debugJoints[i].SetActive(false);
            }
        }

        void StartReceiver()
        {
            try
            {
                udpClient = new UdpClient(port);
                // Fix for Windows UDP socket exception on close
                try { udpClient.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { }
                
                isRunning = true;
                receiveThread = new Thread(new ThreadStart(ReceiveData));
                receiveThread.IsBackground = true;
                receiveThread.Start();
                Debug.Log($"[BodyDataReceiver] Listening on port {port}...");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BodyDataReceiver] Failed to start receiver: {e.Message}");
            }
        }

        void ReceiveData()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (isRunning)
            {
                try
                {
                    if (udpClient.Available > 0) 
                    {
                        byte[] data = udpClient.Receive(ref remoteEndPoint);
                        if (data.Length >= BodyTrackingNetworkStats.PacketSize) // allow >= in case of extra padding
                        {
                            // if (UnityEngine.Time.frameCount % 60 == 0) Debug.Log($"[BodyDataReceiver] Received packet: {data.Length} bytes from {remoteEndPoint.Address}");
                            ProcessPacket(data);
                        }
                        else
                        {
                            Debug.LogWarning($"[BodyDataReceiver] Received invalid packet size: {data.Length}. Expected >= {BodyTrackingNetworkStats.PacketSize}");
                        }
                    }
                    else
                    {
                        Thread.Sleep(1); 
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BodyDataReceiver] Error receiving: {e.Message}");
                }
            }
        }

        void ProcessPacket(byte[] data)
        {
            int offset = 0;

            // 1. Root Pose
            Vector3 rootPos = ReadVector3(data, ref offset);
            Quaternion rootRot = ReadQuaternion(data, ref offset);

            // 2. All Bones
            Quaternion[] bones = new Quaternion[BodyTrackingNetworkStats.MaxBones];
            for (int i = 0; i < BodyTrackingNetworkStats.MaxBones; i++)
            {
                bones[i] = ReadQuaternion(data, ref offset);
            }

            lock (dataLock)
            {
                currentBodyData.RootPosition = rootPos;
                currentBodyData.RootRotation = rootRot;
                Array.Copy(bones, currentBodyData.BoneRotations, bones.Length);
                
                lastPacketTicks = DateTime.Now.Ticks;
                newData = true;
            }
        }

        Vector3 ReadVector3(byte[] data, ref int offset)
        {
            float x = BitConverter.ToSingle(data, offset); offset += 4;
            float y = BitConverter.ToSingle(data, offset); offset += 4;
            float z = BitConverter.ToSingle(data, offset); offset += 4;
            return new Vector3(x, y, z);
        }

        Quaternion ReadQuaternion(byte[] data, ref int offset)
        {
            float x = BitConverter.ToSingle(data, offset); offset += 4;
            float y = BitConverter.ToSingle(data, offset); offset += 4;
            float z = BitConverter.ToSingle(data, offset); offset += 4;
            float w = BitConverter.ToSingle(data, offset); offset += 4;
            return new Quaternion(x, y, z, w);
        }

        void Update()
        {
            long nowTicks = DateTime.Now.Ticks;
            float timeoutTicks = connectionTimeout * 10000000;

            lock (dataLock)
            {
                isConnected = (nowTicks - lastPacketTicks) < timeoutTicks;

                if (isConnected && newData)
                {
                    ApplyBodyData(currentBodyData);
                    newData = false;
                    if (Time.frameCount % 120 == 0) Debug.Log("[BodyDataReceiver] Applying Body Data (Connected)");
                }
                else if (!isConnected)
                {
                    // Hide visuals if disconnected
                    if(showDebugVisuals) SetVisualsActive(false);
                }
            }
        }

        void ApplyBodyData(BodyData data)
        {
            // Apply Root
            if (avatarRoot != null)
            {
                if(applyRootPosition) avatarRoot.localPosition = data.RootPosition; 
                if(applyRootRotation) avatarRoot.localRotation = data.RootRotation * Quaternion.Euler(rootRotationOffset);
            }

            // Apply Bones
            if (applyBoneRotations && avatarBones != null)
            {
                for (int i = 0; i < Mathf.Min(avatarBones.Count, BodyTrackingNetworkStats.MaxBones); i++)
                {
                    if (avatarBones[i] != null)
                    {
                        avatarBones[i].localRotation = data.BoneRotations[i];
                    }
                }
            }

            // Apply Debug Visuals
            if (showDebugVisuals && debugJointPrefab != null)
            {
                SetVisualsActive(true);
                
                // If we have mapped bones, snap debugs to them
                if (avatarBones != null && avatarBones.Count > 0)
                {
                    for (int i = 0; i < Mathf.Min(avatarBones.Count, BodyTrackingNetworkStats.MaxBones); i++)
                    {
                        if (avatarBones[i] != null)
                        {
                            debugJoints[i].transform.position = avatarBones[i].position;
                            debugJoints[i].transform.rotation = avatarBones[i].rotation;
                        }
                    }
                }
                // If we don't have mapped bones, we can't show much except maybe root
                else if (avatarRoot != null)
                {
                     debugJoints[0].transform.position = avatarRoot.position;
                     debugJoints[0].transform.rotation = avatarRoot.rotation;
                }
            }
        }

        void SetVisualsActive(bool active)
        {
            foreach (var v in debugJoints)
            {
                if (v != null && v.activeSelf != active) v.SetActive(active);
            }
        }

        void OnDestroy()
        {
            isRunning = false;
            if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500);
            if (udpClient != null) udpClient.Close();
        }
    }
}
