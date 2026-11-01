using UnityEngine;
using RootMotion.FinalIK;
using System.Collections;

public class ProceduralSpeakerIK : MonoBehaviour
{
    [Header("References")]
    public FullBodyBipedIK ik;
    public Animator animator;
    public InteractionSystem interactionSystem;

    [Header("Settings")]
    public bool isTalking = false;
    [Tooltip("Check this to freeze movement so you can adjust rotations easily")]
    public bool debugFreeze = false; // <--- NEW TOOL
    [Range(0f, 5f)] public float blendSpeed = 3f;

    [Header("Gesture Rhythm")]
    public float minChangeInterval = 1.5f;
    public float maxChangeInterval = 4.0f;
    [Range(0, 1)] public float restChance = 0.3f;

    [Header("Position Config")]
    public float gestureSpeed = 1.5f;
    public Vector3 gestureBoxSize = new Vector3(0.3f, 0.2f, 0.2f);
    public Vector3 gestureCenterOffset = new Vector3(0f, 0.35f, 0.45f);
    
    [Header("Rest Config")]
    public Vector3 restOffsetLeft = new Vector3(-0.25f, -0.05f, 0.1f);
    public Vector3 restOffsetRight = new Vector3(0.25f, -0.05f, 0.1f);

    [Header("Biomechanics")]
    public float wristRotateSpeed = 5.0f; // Faster speed so you see changes instantly

    [Header("Wrist Limits (Noise)")]
    public float limitFlexionExtension = 30f; 
    public float limitRadialUlnar = 15f;
    public float limitPronationSupination = 10f;

    [Header("Axis Correction (CALIBRATE HERE)")]
    [Tooltip("Rotate the Left Hand to face In/Up. Try (0,0,90) or (0,90,0).")]
    public Vector3 axisCorrectionLeft = new Vector3(0, 0, 0); 
    [Tooltip("Rotate the Right Hand to face In/Up.")]
    public Vector3 axisCorrectionRight = new Vector3(0, 0, 0);

    // Internal State
    private float targetLeftW, targetRightW;
    private float currentLeftW, currentRightW;
    private Vector3 noisePosLeft, noisePosRight;
    
    private Quaternion currentWristRotLeft = Quaternion.identity;
    private Quaternion currentWristRotRight = Quaternion.identity;

    private bool _hasStarted = false;

    private void Start()
    {
        AvatarBehaviorController.Instance.SetSpeakerIK(this);
        if (ik == null) ik = GetComponent<FullBodyBipedIK>();
        if (animator == null) animator = GetComponent<Animator>();
        StartCoroutine(RhythmEngine());
        _hasStarted = true;
    }

    private void OnEnable()
    {
        // Re-register with AvatarBehaviorController on reactivation
        if (_hasStarted && AvatarBehaviorController.Instance != null)
        {
            AvatarBehaviorController.Instance.SetSpeakerIK(this);
            Debug.Log("[ProceduralSpeakerIK] Re-registered with AvatarBehaviorController on reactivation.");
        }
    }

    void Update()
    {
        if (ik == null) return;

        if (interactionSystem != null && interactionSystem.inInteraction)
        {
            KillWeights();
            return;
        }

        // Blend weights
        if (!isTalking) { targetLeftW = 0f; targetRightW = 0f; }
        
        // If Debug Freeze is on, force weights to 1 so you can see the pose even if Rhythm says 0
        float debugOverride = debugFreeze && isTalking ? 1f : -1f;

        currentLeftW = Mathf.Lerp(currentLeftW, debugOverride >= 0 ? 1f : targetLeftW, Time.deltaTime * blendSpeed);
        currentRightW = Mathf.Lerp(currentRightW, debugOverride >= 0 ? 1f : targetRightW, Time.deltaTime * blendSpeed);

        // Update Noise
        if (!debugFreeze)
        {
            CalculateGestureNoise();
        }
        else
        {
            // FREEZE MODE: Set noise to absolute zero (center of the box)
            noisePosLeft = Vector3.zero;
            noisePosRight = Vector3.zero;
        }

        ApplyHandIK(true, currentLeftW, noisePosLeft);
        ApplyHandIK(false, currentRightW, noisePosRight);

        if (animator) animator.SetBool("HandsOpen", (currentLeftW > 0.1f || currentRightW > 0.1f));
    }

    void KillWeights()
    {
        ik.solver.leftHandEffector.positionWeight = 0f;
        ik.solver.rightHandEffector.positionWeight = 0f;
        ik.solver.leftHandEffector.rotationWeight = 0f;
        ik.solver.rightHandEffector.rotationWeight = 0f;
    }

    void ApplyHandIK(bool isLeft, float weight, Vector3 noisePos)
    {
        IKEffector effector = isLeft ? ik.solver.leftHandEffector : ik.solver.rightHandEffector;
        Transform forearmBone = isLeft ? ik.references.leftForearm : ik.references.rightForearm;

        // --- POSITION ---
        Vector3 restPos = GetRestPos(isLeft);
        Vector3 gesturePos = GetGesturePos(isLeft, noisePos);
        Vector3 finalPos = Vector3.Lerp(restPos, gesturePos, weight);
        
        effector.position = finalPos;
        effector.positionWeight = 1f;

        // --- ROTATION CALIBRATION ---

        // 1. Calculate direction from Elbow -> New Hand Position
        Vector3 forearmDir = (finalPos - forearmBone.position).normalized;

        // 2. Establish "Zero" Rotation (Looking down the arm, Up is World Up)
        Quaternion forearmLook = Quaternion.LookRotation(forearmDir, Vector3.up);

        // 3. Apply USER CORRECTION (This rotates the coordinate system)
        Vector3 correctionEuler = isLeft ? axisCorrectionLeft : axisCorrectionRight;
        Quaternion correctedBase = forearmLook * Quaternion.Euler(correctionEuler);

        // 4. Apply NOISE (Only if not frozen)
        Quaternion finalRot;
        if (debugFreeze)
        {
            finalRot = correctedBase;
        }
        else
        {
            float t = Time.time * wristRotateSpeed;
            float seed = isLeft ? 0 : 100;
            
            // Reduced limits for subtlety
            float flexExt = (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f * limitFlexionExtension;
            float dev = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f * limitRadialUlnar;
            float twist = (Mathf.PerlinNoise(t, t + seed) - 0.5f) * 2f * limitPronationSupination;

            Quaternion noise = Quaternion.Euler(flexExt, dev, twist);
            finalRot = correctedBase * noise;
        }

        // Smooth rotation
        if (isLeft)
            currentWristRotLeft = Quaternion.Slerp(currentWristRotLeft, finalRot, Time.deltaTime * wristRotateSpeed);
        else
            currentWristRotRight = Quaternion.Slerp(currentWristRotRight, finalRot, Time.deltaTime * wristRotateSpeed);

        effector.rotation = isLeft ? currentWristRotLeft : currentWristRotRight;
        effector.rotationWeight = weight; 
    }

    // ... (Keep CalculateGestureNoise, CalculatePerlinVector, GetGesturePos, GetRestPos, RhythmEngine, SetTalking, OnDrawGizmosSelected as they were) ...
    // Note: I included the critical changed functions above. The helpers below are unchanged from previous versions.
    
    void CalculateGestureNoise()
    {
        float t = Time.time * gestureSpeed;
        noisePosLeft = CalculatePerlinVector(t, gestureBoxSize);
        noisePosRight = CalculatePerlinVector(t + 100, gestureBoxSize);
    }

    Vector3 CalculatePerlinVector(float t, Vector3 scale)
    {
        return new Vector3(
            (Mathf.PerlinNoise(t, 0) - 0.5f) * scale.x,
            (Mathf.PerlinNoise(0, t) - 0.5f) * scale.y,
            (Mathf.PerlinNoise(t, t) - 0.5f) * scale.z
        );
    }
    
    Vector3 GetGesturePos(bool isLeft, Vector3 noise)
    {
        Transform pelvis = ik.references.pelvis;
        Vector3 center = pelvis.position + (pelvis.rotation * gestureCenterOffset);
        if (isLeft) center -= pelvis.rotation * Vector3.right * 0.15f; 
        else center += pelvis.rotation * Vector3.right * 0.15f;
        return center + (pelvis.rotation * noise);
    }

    Vector3 GetRestPos(bool isLeft)
    {
        Transform pelvis = ik.references.pelvis;
        Vector3 offset = isLeft ? restOffsetLeft : restOffsetRight;
        return pelvis.position + (pelvis.rotation * offset);
    }

    IEnumerator RhythmEngine()
    {
        while (true)
        {
            if (isTalking)
            {
                float rng = Random.value;
                if (rng < restChance) { targetLeftW = 0f; targetRightW = 0f; }
                else if (rng < 0.7f) { targetLeftW = 0f; targetRightW = 1f; }
                else if (rng < 0.85f) { targetLeftW = 1f; targetRightW = 0f; }
                else { targetLeftW = 1f; targetRightW = 1f; }
            }
            else { targetLeftW = 0f; targetRightW = 0f; }
            yield return new WaitForSeconds(Random.Range(minChangeInterval, maxChangeInterval));
        }
    }

    public void SetTalking(bool state)
    {
        isTalking = state;
        if (!state) { targetLeftW = 0f; targetRightW = 0f; }
    }
    
    void OnDrawGizmosSelected()
    {
        if (ik == null || ik.references.pelvis == null) return;
        Transform pelvis = ik.references.pelvis;
        Gizmos.matrix = Matrix4x4.TRS(pelvis.position, pelvis.rotation, Vector3.one);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(gestureCenterOffset, gestureBoxSize); 
    }
}