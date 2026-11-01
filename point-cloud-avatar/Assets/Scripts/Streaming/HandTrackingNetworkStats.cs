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
        
        // Bone Map (OVR HandJointId Order):
        // 0: Wrist
        // 1: Forearm
        // 2: Thumb0 (Metacarpal)
        // 3: Thumb1 (Proximal)
        // 4: Thumb2 (Distal)
        // 5: Thumb3 (Tip base / Null?) -> Actually Thumb3 is usually Distal, ThumbTip is separate. 
        // Let's check OVRHandPrimitives again: 
        // Thumb0, Thumb1, Thumb2, Thumb3. 
        // ThumbTip is HandJointId 19.
        // So indices 2-5 are Thumb bones. 
        // 6-8: Index1, Index2, Index3.
        // ...
        // 19: ThumbTip
        // ...
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
