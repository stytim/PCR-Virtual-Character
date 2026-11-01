using UnityEngine;

namespace BodyTrackingStreaming
{
    public static class BodyTrackingNetworkStats
    {
        public const int DefaultPort = 5001; 
        
        // Body (23) + Left Fingers (15) + Right Fingers (15) = 53
        public const int MaxBones = 53; 

        // Packet Structure:
        // Root Pos (12) + Root Rot (16) + Bones (53 * 16) = 28 + 848 = 876 bytes
        public const int PacketSize = 12 + 16 + (MaxBones * 16);
    }

    public class BodyData
    {
        public Vector3 RootPosition;
        public Quaternion RootRotation;
        public Quaternion[] BoneRotations;

        public BodyData()
        {
            RootPosition = Vector3.zero;
            RootRotation = Quaternion.identity;
            BoneRotations = new Quaternion[BodyTrackingNetworkStats.MaxBones];
            for(int i=0; i<BoneRotations.Length; i++) BoneRotations[i] = Quaternion.identity;
        }
    }

    // Unified Enum for Full Body + Fingers
    public enum StreamBodyJointId
    {
        // --- Body Core (23) ---
        Hips = 0,
        Spine = 1,
        Spine1 = 2,
        Spine2 = 3,
        Chest = 4, 
        Neck = 5,
        Head = 6,

        LeftShoulder = 7,
        LeftArm = 8,
        LeftForeArm = 9,
        LeftHand = 10, // Wrist

        RightShoulder = 11,
        RightArm = 12,
        RightForeArm = 13,
        RightHand = 14, // Wrist

        LeftUpLeg = 15,
        LeftLeg = 16,
        LeftFoot = 17,
        LeftToeBase = 18,

        RightUpLeg = 19,
        RightLeg = 20,
        RightFoot = 21,
        RightToeBase = 22,

        // --- Left Hand Fingers (15) ---
        // Thumb (Proximal, Intermediate, Distal) - Metacarpal usually handled by Thumb0/1 depending on rig
        LeftHandThumb1 = 23,
        LeftHandThumb2 = 24,
        LeftHandThumb3 = 25,
        
        LeftHandIndex1 = 26,
        LeftHandIndex2 = 27,
        LeftHandIndex3 = 28,

        LeftHandMiddle1 = 29,
        LeftHandMiddle2 = 30,
        LeftHandMiddle3 = 31,

        LeftHandRing1 = 32,
        LeftHandRing2 = 33,
        LeftHandRing3 = 34,

        LeftHandPinky1 = 35,
        LeftHandPinky2 = 36,
        LeftHandPinky3 = 37,

        // --- Right Hand Fingers (15) ---
        RightHandThumb1 = 38,
        RightHandThumb2 = 39,
        RightHandThumb3 = 40,
        
        RightHandIndex1 = 41,
        RightHandIndex2 = 42,
        RightHandIndex3 = 43,

        RightHandMiddle1 = 44,
        RightHandMiddle2 = 45,
        RightHandMiddle3 = 46,

        RightHandRing1 = 47,
        RightHandRing2 = 48,
        RightHandRing3 = 49,

        RightHandPinky1 = 50,
        RightHandPinky2 = 51,
        RightHandPinky3 = 52,

        End = 53
    }
}
