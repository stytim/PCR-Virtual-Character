using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace HandTrackingStreaming
{
    public class HandDataReceiver : MonoBehaviour
    {
        [Header("Network Settings")]
        public int port = HandTrackingNetworkStats.DefaultPort;
        public float connectionTimeout = 1.0f; // Seconds before considering disconnected

        [Header("Debug Visualization")]
        public bool showDebugVisuals = true;
        public GameObject debugJointPrefab;
        public Transform debugRoot;

        [Header("Avatar Mapping")]
        public Transform leftHandRoot;
        public List<Transform> leftHandBones; // Should match OVRSkeleton indices
        public Transform rightHandRoot;
        public List<Transform> rightHandBones;

        [ContextMenu("Auto Map Bones")]
        public void AutoMapBones()
        {
            if (leftHandRoot != null) AutoMapHand(leftHandRoot, ref leftHandBones, "Left");
            if (rightHandRoot != null) AutoMapHand(rightHandRoot, ref rightHandBones, "Right");
        }

        void AutoMapHand(Transform root, ref List<Transform> bones, string side)
        {
            bones = new List<Transform>(new Transform[26]);
            
            // 0: Start (Unused)
            // 1: Wrist (Root) - Usually we don't map this to bone list if root is driven by World Pose
            bones[1] = root; 
            
            // Function to find transform containing string
            Transform[] allChildren = root.GetComponentsInChildren<Transform>();
            Transform FindNode(string partialName)
            {
                foreach (var t in allChildren)
                {
                    if (t.name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return t;
                }
                return null;
            }

            // Map based on User Log: 
            // Thumb: 2(1), 3(2), 4(3), 5(Tip)
            bones[2] = FindNode($"{side}HandThumb1"); 
            bones[3] = FindNode($"{side}HandThumb2");
            bones[4] = FindNode($"{side}HandThumb3");
            // Slot 5 Tip

            // Index: 6(0/Meta), 7(1/Prox), 8(2/Inter), 9(3/Dist), 10(Tip)
            // bones[6] = FindNode($"{side}HandIndex0"); // Meta usually missing
            bones[7] = FindNode($"{side}HandIndex1");
            bones[8] = FindNode($"{side}HandIndex2");
            bones[9] = FindNode($"{side}HandIndex3");

            // Middle: 11(Meta), 12(1), 13(2), 14(3), 15(Tip)
            bones[12] = FindNode($"{side}HandMiddle1");
            bones[13] = FindNode($"{side}HandMiddle2");
            bones[14] = FindNode($"{side}HandMiddle3");

            // Ring: 16(Meta), 17(1), 18(2), 19(3), 20(Tip)
            bones[17] = FindNode($"{side}HandRing1");
            bones[18] = FindNode($"{side}HandRing2");
            bones[19] = FindNode($"{side}HandRing3");

            // Pinky: 21(Meta), 22(1), 23(2), 24(3), 25(Tip)
            // bones[21] = FindNode($"{side}HandPinky0");
            bones[22] = FindNode($"{side}HandPinky1"); 
            bones[23] = FindNode($"{side}HandPinky2");
            bones[24] = FindNode($"{side}HandPinky3");

            Debug.Log($"[HandDataReceiver] Auto-Mapped {side} hand with OpenXR spacing.");
        }

        // Runtime State
        private UdpClient udpClient;
        private Thread receiveThread;
        private bool isRunning = false;
        
        // Data Buffers
        private HandData currentLeftHand;
        private HandData currentRightHand;
        
        // Connection & Timeout Tracking
        private long lastLeftTicks;
        private long lastRightTicks;
        private bool isConnectedLeft = false;
        private bool isConnectedRight = false;
        
        // Debug Instances
        private GameObject[] debugLeftJoints = new GameObject[26];
        private GameObject[] debugRightJoints = new GameObject[26];

        private object dataLock = new object();
        
        // Flags to indicate new data available for main thread
        private bool newLeftData = false;
        private bool newRightData = false;

        void Start()
        {
            // Initialize Data
            currentLeftHand = new HandData(HandTrackingNetworkStats.Handedness.Left);
            currentRightHand = new HandData(HandTrackingNetworkStats.Handedness.Right);

            // Initialize Debug Visuals
            if (showDebugVisuals && debugJointPrefab != null)
            {
                InitializeDebugVisuals(ref debugLeftJoints, "Left_Debug_");
                InitializeDebugVisuals(ref debugRightJoints, "Right_Debug_");
            }

            StartReceiver();
        }

        void InitializeDebugVisuals(ref GameObject[] instances, string prefix)
        {
            Transform parent = debugRoot != null ? debugRoot : this.transform;
            for (int i = 0; i < 26; i++)
            {
                instances[i] = Instantiate(debugJointPrefab, parent);
                instances[i].name = prefix + i;
                instances[i].SetActive(false);
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
                Debug.Log($"[HandDataReceiver] Listening on port {port}...");
            }
            catch (Exception e)
            {
                Debug.LogError($"[HandDataReceiver] Failed to start receiver: {e.Message}");
            }
        }

        void ReceiveData()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            int frameCounter = 0;
            while (isRunning)
            {
                try
                {
                    if (udpClient.Available > 0) 
                    {
                        byte[] data = udpClient.Receive(ref remoteEndPoint);
                        if (data.Length == HandTrackingNetworkStats.PacketSize)
                        {
                            frameCounter++;
                            if (frameCounter % 60 == 0) Debug.Log($"[HandDataReceiver] Received packet from {remoteEndPoint.Address}");
                            ProcessPacket(data);
                        }
                        else
                        {
                            Debug.LogWarning($"[HandDataReceiver] Received invalid packet size: {data.Length} (Expected: {HandTrackingNetworkStats.PacketSize})");
                        }
                    }
                    else
                    {
                        Thread.Sleep(1); // Prevent CPU burn
                    }
                }
                catch (SocketException ex) 
                { 
                     Debug.LogWarning($"[HandDataReceiver] Socket Exception: {ex.Message}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[HandDataReceiver] Error receiving: {e.Message}");
                }
            }
        }

        void ProcessPacket(byte[] data)
        {
            int offset = 0;
            // 1. Handedness
            byte handednessByte = data[offset++];
            HandTrackingNetworkStats.Handedness callbackHand = (HandTrackingNetworkStats.Handedness)handednessByte;

            // 2. Root Pose
            Vector3 rootPos = ReadVector3(data, ref offset);
            Quaternion rootRot = ReadQuaternion(data, ref offset);

            // 3. Bones
            Quaternion[] bones = new Quaternion[26];
            for (int i = 0; i < 26; i++)
            {
                bones[i] = ReadQuaternion(data, ref offset);
            }

            lock (dataLock)
            {
                long now = DateTime.Now.Ticks;
                if (callbackHand == HandTrackingNetworkStats.Handedness.Left)
                {
                    currentLeftHand.RootPosition = rootPos;
                    currentLeftHand.RootRotation = rootRot;
                    Array.Copy(bones, currentLeftHand.BoneRotations, 26);
                    lastLeftTicks = now;
                    newLeftData = true;
                }
                else
                {
                    currentRightHand.RootPosition = rootPos;
                    currentRightHand.RootRotation = rootRot;
                    Array.Copy(bones, currentRightHand.BoneRotations, 26);
                    lastRightTicks = now;
                    newRightData = true;
                }
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

            // Update Connection State
            lock (dataLock)
            {
                isConnectedLeft = (nowTicks - lastLeftTicks) < timeoutTicks;
                isConnectedRight = (nowTicks - lastRightTicks) < timeoutTicks;
                
                // If connected and new data, apply it
                if (isConnectedLeft && newLeftData)
                {
                    ApplyHandData(currentLeftHand, leftHandRoot, leftHandBones, debugLeftJoints);
                    newLeftData = false;
                }
                else if (!isConnectedLeft)
                {
                    // Hide visuals if disconnected
                    SetVisualsActive(debugLeftJoints, false);
                }

                if (isConnectedRight && newRightData)
                {
                    ApplyHandData(currentRightHand, rightHandRoot, rightHandBones, debugRightJoints);
                    newRightData = false;
                }
                else if (!isConnectedRight)
                {
                    SetVisualsActive(debugRightJoints, false);
                }
            }
        }

        void ApplyHandData(HandData data, Transform root, List<Transform> bones, GameObject[] debugJoints)
        {
            // Apply Root (World Space as sent by Sender)
            if (root != null)
            {
                root.position = data.RootPosition;
                root.rotation = data.RootRotation;
            }

            // Apply Bones
            // Assuming bones list matches 1:1 with data.BoneRotations
            // (OVRSkeleton usually has 24 bones, but we send 26. We check bounds.)
            if (bones != null)
            {
                for (int i = 0; i < Mathf.Min(bones.Count, 26); i++)
                {
                    if (bones[i] != null)
                    {
                        // Optimization/Logic check: If bone[i] is the same as root, applying localRotation 
                        // might override the world rotation we just set above (if parent is not purely aligned).
                        // Usually we shouldn't assign root to the bones list, or we should skip it here.
                        if (bones[i] != root)
                        {
                            bones[i].localRotation = data.BoneRotations[i];
                        }
                    }
                }
            }

            // Apply Debug Visuals
            if (showDebugVisuals && debugJoints != null)
            {
                // We need to calculate world positions for debug cubes if we want them to follow the hand strictly.
                // However, doing Forward Kinematics manually is expensive.
                // If we have 'bones' transforms assigned, we can just snap debug cubes to them.
                // If NOT, we can't easily show them without reconstructing the hierarchy.
                // For this implementation, let's assume if 'bones' are assigned, we use them.
                // If not, we just show the root.
                
                SetVisualsActive(debugJoints, true); // Ensure active
                
                if (bones != null && bones.Count > 0)
                {
                    for (int i = 0; i < Mathf.Min(bones.Count, 26); i++)
                    {
                        if (bones[i] != null)
                        {
                            debugJoints[i].transform.position = bones[i].position;
                            debugJoints[i].transform.rotation = bones[i].rotation;
                        }
                    }
                }
                else if (root != null)
                {
                     // Fallback: just show root
                     debugJoints[0].transform.position = root.position;
                     debugJoints[0].transform.rotation = root.rotation;
                }
            }
        }

        void SetVisualsActive(GameObject[] visuals, bool active)
        {
            if (visuals == null) return;
            foreach (var v in visuals)
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
