using UnityEngine;

public class ArmCorrector : MonoBehaviour
{
    [Header("Bone References")]
    public Transform leftArm;   // Shoulder/Upper Arm bone
    public Transform rightArm;  // Shoulder/Upper Arm bone

    [Header("Offsets (Tweak in Play Mode)")]
    public Vector3 leftArmOffset = new Vector3(0, 0, -15); 
    public Vector3 rightArmOffset = new Vector3(0, 0, 15);

    [Header("Settings")]
    public bool applyCorrection = true;

    void LateUpdate()
    {
        if (!applyCorrection) return;

        if (leftArm)
        {
            leftArm.localEulerAngles += leftArmOffset;
        }

        if (rightArm)
        {
            rightArm.localEulerAngles += rightArmOffset;
        }
    }
}