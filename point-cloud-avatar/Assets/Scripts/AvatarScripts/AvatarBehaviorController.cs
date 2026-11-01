using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A singleton controller for managing an avatar's animations and expressions using an enum-driven, Scriptable Object architecture.
/// </summary>
public class AvatarBehaviorController : MonoBehaviour
{
    private static readonly int GestureRandom = Animator.StringToHash("GestureRandom");

    [System.Serializable]
    public class BehaviorMapping
    {
        public AvatarBehavior behavior;
        public AvatarBehaviorSO behaviorData;
    }

    // =================================================================================================================
    // SINGLETON PATTERN
    // =================================================================================================================

    public static AvatarBehaviorController Instance { get; private set; }
    
    [Header("Settings")]
    [Tooltip("How long to wait for the head to turn towards the user before nodding/shaking.")]
    [SerializeField] private float headTurnDelay = 0.6f;
    
    // We need to remember the "Default Explain" behavior so we can switch to it from Disagree
    [SerializeField] private AvatarBehaviorSO explainBehaviorSO; 

    // Track the currently active logic
    private AvatarBehaviorSO _currentActiveBehaviorSO;
    private bool _isStopping = false; // Prevents new loops from starting when we want to stop
    
    private ProceduralSpeakerIK _speakerIK;

    private Coroutine behaviorCoroutine;
    private Coroutine headNodCoroutine;
    private Coroutine activeLookClearCoroutine;

    public void SetSpeakerIK(ProceduralSpeakerIK speakerIK)
    {
        _speakerIK = speakerIK;
    }

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (avatarAnimator)
        {
            isInitialized = true;
        }
    }

    // =================================================================================================================
    // REFERENCES & DATA
    // =================================================================================================================

    [SerializeField] private Animator avatarAnimator;
    
    [Tooltip("Assign each AvatarBehavior enum to its corresponding Scriptable Object asset here.")] [SerializeField]
    private List<BehaviorMapping> behaviorMappings = new List<BehaviorMapping>();

    private bool isInitialized = false;
    private List<AvatarExpression> currentActiveExpressions = new List<AvatarExpression>();
    
    // --- CHANGED: Track Enum instead of String ---
    private AnimationClosureType _currentClosureType = AnimationClosureType.None;
    
    // Track if we're in idle mode
    private bool _isInIdleMode = false;
    
    // =================================================================================================================
    // INITIALIZATION
    // =================================================================================================================

    public void Init(GameObject avatar)
    {
        if (!avatar)
        {
            Debug.LogError("[AvatarBehaviorController] Init failed: Avatar GameObject is null.");
            isInitialized = false;
            return;
        }

        avatarAnimator = avatar.GetComponent<Animator>();
        
        if (!avatarAnimator) Debug.LogError($"[AvatarBehaviorController] Init failed: Animator not found on '{avatar.name}'.");
       
        isInitialized = avatarAnimator;
        if (isInitialized) Debug.Log($"[AvatarBehaviorController] Successfully initialized with avatar: {avatar.name}");
    }

    // =================================================================================================================
    // PUBLIC PART
    // =================================================================================================================

    public void TriggerBehavior(AvatarBehavior behaviorToTrigger)
    {
        Debug.Log($"<color=cyan>[BehaviorController]</color> Triggering Behavior: <b>{behaviorToTrigger}</b>");
        _isStopping = false;

        if (!isInitialized)
        {
            Debug.LogWarning("Controller not initialized. Cannot trigger behavior.");
            return;
        }

        if (behaviorCoroutine != null) StopCoroutine(behaviorCoroutine);
        
        if (behaviorToTrigger != AvatarBehavior.Idle && _isInIdleMode) ExitIdleMode();
        
        if (behaviorToTrigger == AvatarBehavior.Idle)
        {
            EnterIdleMode();
            return;
        }

        behaviorCoroutine = StartCoroutine(ApplyBehaviorRoutine(behaviorToTrigger));
    }
    
    private IEnumerator ApplyBehaviorRoutine(AvatarBehavior behaviorToTrigger)
    {
        yield return null; 
        
        BehaviorMapping mapping = behaviorMappings.FirstOrDefault(m => m.behavior == behaviorToTrigger);
        
        if (mapping == null || !mapping.behaviorData)
        {
            EnterIdleMode();
            yield break;
        }
        
        AvatarBehaviorSO behaviorData = mapping.behaviorData;
        _currentActiveBehaviorSO = behaviorData;
        
        // --- HANDLE PROCEDURAL IK ---
        if (_speakerIK)
        {
            _speakerIK.SetTalking(!behaviorData.disableProceduralIK);
        }
        
        
        // --- LOOK AT USER ---
        if (behaviorData.lookAtUser && Camera.main != null)
        {
            LookAtTarget(Camera.main.transform);
        }
        
        // --- MODIFIED: BLEND TREE RANDOMIZER WITH LIST MAPPING ---
        if (!string.IsNullOrEmpty(behaviorData.blendParameter))
        {
            // Check if the SO has the mapping list we added
            if (behaviorData.blendTreeMapping != null && behaviorData.blendTreeMapping.Count > 0)
            {
                // 1. Pick random index based on list size
                int randomIndex = Random.Range(0, behaviorData.blendTreeMapping.Count);
                
                // 2. Set Animator Parameter
                avatarAnimator.SetFloat(behaviorData.blendParameter, (float)randomIndex);
                
                // 3. Track Closure Type from the list
                _currentClosureType = behaviorData.blendTreeMapping[randomIndex];
                
                Debug.Log($"<color=cyan>[Behavior]</color> Index: {randomIndex} | Closure: {_currentClosureType}");
            }
            else
            {
                // Fallback to old random 0-1 if list is missing
                float randomValue = Random.Range(0f, 1f);
                avatarAnimator.SetFloat(behaviorData.blendParameter, randomValue);
                _currentClosureType = AnimationClosureType.None;
            }
        }
        else
        {
             _currentClosureType = AnimationClosureType.None;
        }
        
        // --- TRIGGER ANIMATOR STATE ---
        if (!string.IsNullOrEmpty(behaviorData.animatorTrigger))
        {
            avatarAnimator.SetTrigger(behaviorData.animatorTrigger);
            Debug.Log($"<color=cyan>[Behavior]</color> Triggered: {behaviorData.animatorTrigger}");
        }
        
    }
    
    // =================================================================================================================
    // CLOSURE ANIMATION SYSTEM
    // =================================================================================================================
    
    [Header("Closure Settings")]
    [SerializeField] private float closureAnimationDuration = 1.5f;
    
    private Coroutine _closureCoroutine;
    
    public void TriggerClosureAnimation()
    {
        _isStopping = false;
        
        if (_closureCoroutine != null) StopCoroutine(_closureCoroutine);
        
        // --- MODIFIED: Use the Helper to get trigger string from Enum ---
        string closureTrigger = GetClosureTriggerName(_currentClosureType);

        if (!string.IsNullOrEmpty(closureTrigger))
        {
            _closureCoroutine = StartCoroutine(PlayClosureThenIdle(closureTrigger));
        }
        else
        {
            EnterIdleMode();
        }
        
        // Reset
        _currentClosureType = AnimationClosureType.None;
    }
    
    private IEnumerator PlayClosureThenIdle(string closureTrigger)
    {
        avatarAnimator.SetTrigger(closureTrigger);
        Debug.Log($"<color=yellow>[Closure]</color> Playing: {closureTrigger}");
        
        yield return new WaitForSeconds(closureAnimationDuration);
        
        EnterIdleMode();
        _closureCoroutine = null;
    }
    
    // --- MODIFIED: Switch on Enum instead of String ---
    private string GetClosureTriggerName(AnimationClosureType type)
    {
        return type switch
        {
            AnimationClosureType.BothHands => "EnterClosureBothHands",
            AnimationClosureType.LeftHand => "EnterClosureLeftHand",
            AnimationClosureType.RightHand => "EnterClosureRightHand",
            _ => ""
        };
    }
    
    // =================================================================================================================
    // ANIMATOR STATE CONTROL
    // =================================================================================================================
    
    private static readonly int EnterIdleSSM = Animator.StringToHash("EnterIdle");
    private static readonly int EnterTalkingSSM = Animator.StringToHash("EnterTalking");
    private static readonly int EnterListeningSSM = Animator.StringToHash("EnterListening");
    private static readonly int ExitToIdleSSM = Animator.StringToHash("ExitToIdle");
    
    public void EnterIdleMode()
    {
        if (!isInitialized || !avatarAnimator) return;
        _isInIdleMode = true;
        avatarAnimator.SetTrigger(EnterIdleSSM);
        Debug.Log("<color=green>[BehaviorController]</color> Triggered EnterIdle SSM");
    }
    
    public void ExitIdleMode()
    {
        _isInIdleMode = false;
        Debug.Log("<color=yellow>[BehaviorController]</color> Exited Idle Mode");
    }
    
    public void EnterTalkingMode()
    {
        if (!isInitialized || !avatarAnimator) return;
        if (_isInIdleMode) ExitIdleMode();
        avatarAnimator.SetTrigger(EnterTalkingSSM);
        Debug.Log("<color=cyan>[BehaviorController]</color> Triggered EnterTalking SSM");
    }
    
    public void EnterListeningMode()
    {
        if (!isInitialized || !avatarAnimator) return;
        if (_isInIdleMode) ExitIdleMode();
        avatarAnimator.SetTrigger(EnterListeningSSM);
        Debug.Log("<color=blue>[BehaviorController]</color> Triggered EnterListening SSM");
    }
    
    public void ExitToIdle()
    {
        if (!isInitialized || !avatarAnimator) return;
        avatarAnimator.SetTrigger(ExitToIdleSSM);
        EnterIdleMode();
    }

    // =================================================================================================================
    // HELPER METHODS (EXPRESSIONS & ANIMATION)
    // =================================================================================================================
    

    public void PlayAnimation(AvatarAnimation animationToPlay, int layerIndex = 0, float transitionDuration = 0.25f, bool isTrigger = false)
    {
        if (!isInitialized || !avatarAnimator || animationToPlay == AvatarAnimation.None) return;
        if (isTrigger) avatarAnimator.SetTrigger(animationToPlay.ToString());
        else avatarAnimator.CrossFade(Animator.StringToHash(animationToPlay.ToString()), transitionDuration, layerIndex);
    }
    
    /// <summary>
    /// Directly triggers an animator trigger by name.
    /// </summary>
    public void TriggerAnimatorTrigger(string triggerName)
    {
        if (!isInitialized || !avatarAnimator || string.IsNullOrEmpty(triggerName)) return;
        avatarAnimator.SetTrigger(triggerName);
        Debug.Log($"<color=magenta>[BehaviorController] Triggered animator: {triggerName}</color>");
    }
    

    // =================================================================================================================
    // HEAD NOD & GAZE (Kept exactly as you provided)
    // =================================================================================================================
    
    
    
    public void LookAtTarget(Transform lookAtTarget)
    {
        if (!isInitialized || !lookAtTarget) return;
        // TODO do with IK
        //eyes.headEnabled = true;
        //eyes.lookTarget = lookAtTarget;
    }

    public void ClearLookTarget(float delay = 0f)
    {
        if (!isInitialized) return;
        if (activeLookClearCoroutine != null) StopCoroutine(activeLookClearCoroutine);

        if (delay <= 0f)
        {
            // TODO do with IK
            //eyes.lookTarget = null;
            //eyes.headEnabled = true;
        }
        else
        {
            activeLookClearCoroutine = StartCoroutine(ClearLookTargetRoutine(delay));
        }
    }
    
    private IEnumerator ClearLookTargetRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        //eyes.lookTarget = null;
        //eyes.headEnabled = true;
        // TODO do with IK
        activeLookClearCoroutine = null;
    }
    
    public void SetTalkingState(bool isTalking)
    {
        if (_speakerIK)
        {
            _speakerIK.SetTalking(isTalking);
        }
    }
    
    public void LoopTalkingBehavior()
    {
        if (_isStopping) return;
        
        if (!isInitialized || !_currentActiveBehaviorSO || _isInIdleMode) return;
        PickNewRandomVariation(_currentActiveBehaviorSO);
        avatarAnimator.SetTrigger(_currentActiveBehaviorSO.animatorTrigger);
    }

    public void SwitchToExplainLoop()
    {
        if (!isInitialized || !explainBehaviorSO || _isInIdleMode) return;
        _currentActiveBehaviorSO = explainBehaviorSO;
        PickNewRandomVariation(explainBehaviorSO);
        avatarAnimator.SetTrigger(explainBehaviorSO.animatorTrigger);
        
        Debug.Log("<color=cyan>[Behavior]</color> Switched from Disagree to Explain Loop.");
    }

    // Helper to pick random index and set closure type
    private void PickNewRandomVariation(AvatarBehaviorSO data)
    {
        if (data.blendTreeMapping != null && data.blendTreeMapping.Count > 0)
        {
            int randomIndex = Random.Range(0, data.blendTreeMapping.Count);
            avatarAnimator.SetFloat(data.blendParameter, (float)randomIndex);
            _currentClosureType = data.blendTreeMapping[randomIndex];
        }
    }
    
    public void LoopListeningBehavior()
    {
        if (!isInitialized || !_currentActiveBehaviorSO || _isInIdleMode) return;

        PickNewRandomVariation(_currentActiveBehaviorSO);
        
        avatarAnimator.SetTrigger(_currentActiveBehaviorSO.animatorTrigger);
    }
}