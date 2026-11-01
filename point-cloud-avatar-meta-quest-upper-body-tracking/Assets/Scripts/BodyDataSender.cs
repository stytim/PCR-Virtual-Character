using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using System;

namespace BodyTrackingStreaming
{
    public class BodyDataSender : MonoBehaviour
    {
        [Header("Network Settings")]
        public string targetIP = "192.168.1.100";
        public int targetPort = BodyTrackingNetworkStats.DefaultPort;

        [Header("Avatar Source")]
        [Tooltip("The root transform of the source avatar (usually Hips or a parent container)")]
        public Transform avatarRoot;
        [Tooltip("Bones mapped to StreamBodyJointId indices. These must exactly match the Receiver's structure.")]
        public List<Transform> avatarBones;

        private UdpClient udpClient;
        private IPEndPoint remoteEndPoint;
        private bool isConnected = false;

        private byte[] sendBuffer = new byte[BodyTrackingNetworkStats.PacketSize];

        [ContextMenu("Auto Map Bones (Full Skeleton)")]
        public void AutoMapBones()
        {
            if (avatarRoot == null)
            {
                Debug.LogError("[BodyDataSender] Assign Avatar Root first!");
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
            
            Debug.Log("[BodyDataSender] Auto-Mapping complete. Verify indices manually.");
        }

        void Start()
        {
            InitializeNetwork();
        }

        void InitializeNetwork()
        {
            try
            {
                udpClient = new UdpClient();
                remoteEndPoint = new IPEndPoint(IPAddress.Parse(targetIP), targetPort);
                isConnected = true;
                Debug.Log($"[BodyDataSender] UDP Client initialized. Target: {targetIP}:{targetPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BodyDataSender] UDP Initialization failed: {e.Message}");
                isConnected = false;
            }
        }

        void Update()
        {
            if (isConnected && avatarRoot != null)
            {
                SendBodyData();
            }
        }

        void SendBodyData()
        {
            int offset = 0;

            // 1. Root Pose
            Vector3 pos = avatarRoot.localPosition;
            Quaternion rot = avatarRoot.localRotation;

            WriteVector3(pos, sendBuffer, ref offset);
            WriteQuaternion(rot, sendBuffer, ref offset);

            // 2. All Bones (Body + Hands)
            for (int i = 0; i < BodyTrackingNetworkStats.MaxBones; i++)
            {
                Quaternion q = Quaternion.identity;

                if (avatarBones != null && i < avatarBones.Count && avatarBones[i] != null)
                {
                    q = avatarBones[i].localRotation;
                }
                
                WriteQuaternion(q, sendBuffer, ref offset);
            }

            try
            {
                udpClient.Send(sendBuffer, sendBuffer.Length, remoteEndPoint);
                // if (Time.frameCount % 60 == 0) Debug.Log($"[BodyDataSender] Sent packet {sendBuffer.Length} bytes");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BodyDataSender] Send failed: {e.Message}. Re-initializing...");
                InitializeNetwork();
            }
        }

        // Helper to write float into buffer
        void WriteFloat(float value, byte[] buffer, ref int offset)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
            offset += 4;
        }

        void WriteVector3(Vector3 v, byte[] buffer, ref int offset)
        {
            WriteFloat(v.x, buffer, ref offset);
            WriteFloat(v.y, buffer, ref offset);
            WriteFloat(v.z, buffer, ref offset);
        }

        void WriteQuaternion(Quaternion q, byte[] buffer, ref int offset)
        {
            WriteFloat(q.x, buffer, ref offset);
            WriteFloat(q.y, buffer, ref offset);
            WriteFloat(q.z, buffer, ref offset);
            WriteFloat(q.w, buffer, ref offset);
        }
        
        void OnDestroy()
        {
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }
        }
        
        public void SetTargetIP(string ip)
        {
            targetIP = ip;
            InitializeNetwork();
        }
    }
}
