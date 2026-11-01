using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using System;
using Oculus.Interaction.Input; // Required for IHand

namespace HandTrackingStreaming
{
    public class HandDataSender : MonoBehaviour
    {
        [Header("Network Settings")]
        public string targetIP = "192.168.1.100";
        public int targetPort = HandTrackingNetworkStats.DefaultPort;

        [Header("Hand References")]
        // We use MonoBehaviour because Unity Inspector doesn't support Interface serialization natively until recently.
        // Assign the GameObject with the Hand/OVRHand component here.
        [Tooltip("Assign the GameObject with the Hand component (implementing IHand)")]
        public MonoBehaviour leftHandSource;
        [Tooltip("Assign the GameObject with the Hand component (implementing IHand)")]
        public MonoBehaviour rightHandSource;

        private IHand leftHand;
        private IHand rightHand;

        [Header("Settings")]
        [Tooltip("Convert OpenXR Z-Forward/Y-Up to Avatar Y-Forward/Z-Down")]
        public bool fixCoordinateSystem = true;

        private UdpClient udpClient;
        private IPEndPoint remoteEndPoint;
        private bool isConnected = false;

        private byte[] sendBuffer = new byte[HandTrackingNetworkStats.PacketSize];
        private Camera mainCamera;

        // Coordinate Fix Basis Change
        // OpenXR (Z-Fwd, Y-Up) -> Avatar (Y-Fwd, Z-Down)
        // Transformation: Rotate -90 around X.
        private readonly Quaternion fixRot = Quaternion.Euler(-90, 0, 0); // OpenXR Basis -> Avatar Basis
        private readonly Quaternion fixRotInv = Quaternion.Euler(90, 0, 0);

        void Start()
        {
            mainCamera = Camera.main;
            InitializeNetwork();
            
            if (leftHandSource != null) leftHand = leftHandSource.GetComponent<IHand>();
            if (rightHandSource != null) rightHand = rightHandSource.GetComponent<IHand>();

            if (leftHand == null) Debug.LogError("[HandDataSender] Left Hand Source does not implement IHand!");
            if (rightHand == null) Debug.LogError("[HandDataSender] Right Hand Source does not implement IHand!");
            
            LogJointMapping();
        }

        void LogJointMapping()
        {
            Debug.Log("[HandDataSender] OpenXR Joint Mapping Structure (Dumping Loop):");
            for (int i = 0; i < 26; i++)
            {
                string name = (i < (int)HandJointId.HandEnd) ? ((HandJointId)i).ToString() : "N/A (Padding)";
                Debug.Log($"Joint {i}: {name}");
            }
        }

        void InitializeNetwork()
        {
            try
            {
                udpClient = new UdpClient();
                remoteEndPoint = new IPEndPoint(IPAddress.Parse(targetIP), targetPort);
                isConnected = true;
                Debug.Log($"[HandDataSender] UDP Client initialized. Target: {targetIP}:{targetPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[HandDataSender] UDP Initialization failed: {e.Message}");
                isConnected = false;
            }
        }

        void Update()
        {
            if (isConnected)
            {
                bool leftValid = leftHand != null && leftHand.IsTrackedDataValid;
                bool rightValid = rightHand != null && rightHand.IsTrackedDataValid;

                if (Time.frameCount % 60 == 0) // Log once per second approx
                {
                    Debug.Log($"[HandDataSender] Status - Connected: {isConnected}, LeftValid: {leftValid}, RightValid: {rightValid}");
                }

                if (leftValid)
                {
                    SendHandData(leftHand, HandTrackingNetworkStats.Handedness.Left);
                }
                
                if (rightValid)
                {
                    SendHandData(rightHand, HandTrackingNetworkStats.Handedness.Right);
                }
            }
        }

        void SendHandData(IHand hand, HandTrackingNetworkStats.Handedness handedness)
        {
            // Prepare buffer
            int offset = 0;

            // 1. Handedness (1 byte)
            sendBuffer[offset++] = (byte)handedness;

            // 2. Root Pose
            Pose rootPose;
            if (hand.GetRootPose(out rootPose))
            {
                 Vector3 pos = rootPose.position;
                 Quaternion rot = rootPose.rotation;

         /*       // Convert to local space of the main camera
                if (mainCamera != null)
                {
                    Matrix4x4 camMatrix = mainCamera.transform.localToWorldMatrix;
                    Matrix4x4 camMatrixInv = camMatrix.inverse;
    
                    // Transform position
                    pos = camMatrixInv.MultiplyPoint3x4(pos);
                    // Transform rotation
                    rot = Quaternion.LookRotation(
                        camMatrixInv.MultiplyVector(rot * Vector3.forward),
                        camMatrixInv.MultiplyVector(rot * Vector3.up)
                    );
                }*/


                if (fixCoordinateSystem)
                 {
                     // Fix Root Rotation: World Space Correction
                     // We want the object which was Z-fwd to be Y-fwd contextually.
                     // q_new = q_old * fixRotInv
                     rot = fixRot * rot * fixRotInv;
                 }

                 WriteVector3(pos, sendBuffer, ref offset);
                 WriteQuaternion(rot, sendBuffer, ref offset);
            }
            else
            {
                 WriteVector3(Vector3.zero, sendBuffer, ref offset);
                 WriteQuaternion(Quaternion.identity, sendBuffer, ref offset);
            }

            // 3. Bone Rotations
            for (int i = 0; i < 26; i++)
            {
                Quaternion q = Quaternion.identity;
                
                if (i < (int)HandJointId.HandEnd) 
                {
                    if (hand.GetJointPoseLocal((HandJointId)i, out Pose jointPose))
                    {
                        q = jointPose.rotation;

                        if (fixCoordinateSystem)
                        {
                            // Fix Local Bone Rotation: Change of Basis
                            // q_new = fixRot * q_old * fixRotInv
                            q = fixRot * q * fixRotInv;
                        }
                    }
                }
                
                WriteQuaternion(q, sendBuffer, ref offset);
            }

            try
            {
                udpClient.Send(sendBuffer, sendBuffer.Length, remoteEndPoint);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HandDataSender] Send failed: {e.Message}. Re-initializing...");
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
