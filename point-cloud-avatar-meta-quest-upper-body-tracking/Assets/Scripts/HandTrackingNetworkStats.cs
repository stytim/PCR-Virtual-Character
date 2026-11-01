using System;
using UnityEngine;

namespace HandTrackingStreaming
{
    public static class HandTrackingNetworkStats
    {
        public const int DefaultPort = 5005;
        // Total bytes: 1 (Handedness) + 12 (RootPos) + 16 (RootRot) + 26 * 16 (Bones) = 445
        public const int PacketSize = 445; 
        
        public enum Handedness : byte
        {
            Left = 0,
            Right = 1
        }
        
    }

    [System.Serializable]
    public struct HandData
    {
        public HandTrackingNetworkStats.Handedness Handedness;
        public Vector3 RootPosition;
        public Quaternion RootRotation;
        public Quaternion[] BoneRotations; // Expected length: 26

        public HandData(HandTrackingNetworkStats.Handedness handedness)
        {
            Handedness = handedness;
            RootPosition = Vector3.zero;
            RootRotation = Quaternion.identity;
            BoneRotations = new Quaternion[26];
            for (int i = 0; i < 26; i++) BoneRotations[i] = Quaternion.identity;
        }
    }
}
