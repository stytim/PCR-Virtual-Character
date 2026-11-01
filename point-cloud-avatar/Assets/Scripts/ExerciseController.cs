using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Defines a single exercise with its name, animation index, and speech cue.
/// </summary>
[System.Serializable]
public class ExerciseDefinition
{
    [Tooltip("Name of the exercise (e.g. 'Standing Forward Arm Circles')")]
    public string name;
    
    [Tooltip("Animation index in the animator (1-5)")]
    public int animationIndex;
    
    [Tooltip("Speech cue the avatar says before this exercise")]
    [TextArea(1, 3)]
    public string cue;
}

/// <summary>
/// Central controller for the exercise session.
/// Manages randomized exercise order, TTS cues, speech buffering, and session state.
/// Drives the animator by setting ExerciseIndex and re-entering the Exercise SSM for each step.
/// 
/// Speech buffering: User speech during animations is buffered and processed after the animation
/// finishes. If user is done talking, response is immediate (no listening animation).
/// If user is still talking, goes to listening animation first.
/// </summary>
public class ExerciseController : MonoBehaviour
{
    public static ExerciseController Instance { get; private set; }
    
    [Header("References")]
    [Tooltip("Reference to the STT Handler")]
    public STTHandler sttHandler;
    
    [Tooltip("Reference to the TTS Handler for speaking cues")]
    public TTSHandler ttsHandler;
    
    [Tooltip("Reference to the LLM Handler for processing buffered speech")]
    public LLMHandler llmHandler;
    
    [Tooltip("Reference to the Animator (auto-found if not set)")]
    public Animator avatarAnimator;
    
    [Header("Exercise Definitions")]
    [Tooltip("Define all exercises here. The order will be randomized at session start.")]
    public ExerciseDefinition[] exercises = new ExerciseDefinition[]
    {
        new ExerciseDefinition { name = "Standing Forward Arm Circles", animationIndex = 1, cue = "Let's start with arm circles. Big circles, keep the arms straight." },
        new ExerciseDefinition { name = "Dynamic Squat-to-Reach", animationIndex = 2, cue = "Good! Now, Dynamic Squat-to-Reach. Sit back, then reach for the ceiling." },
        new ExerciseDefinition { name = "High Knee Marching", animationIndex = 3, cue = "Next up, High Knee Marching. Drive the knee up, keep your chest proud." },
        new ExerciseDefinition { name = "Vertical Squat Jumps", animationIndex = 4, cue = "Now for Vertical Squat Jumps. Land quietly. Soft knees." },
        new ExerciseDefinition { name = "Jumping Jacks", animationIndex = 5, cue = "Finally, Jumping Jacks. Light on your toes. Full range of motion." },
    };
    
    [Header("Animator Parameter Names")]
    [Tooltip("Name of the integer parameter in animator for exercise index")]
    public string exerciseIndexParam = "ExerciseIndex";
    
    [Header("Randomization")]
    [Tooltip("Randomize the exercise order each session")]
    public bool randomizeOrder = true;
    
    [Header("Completion Settings")]
    [Tooltip("What the avatar says when the entire exercise sequence is complete")]
    [TextArea(2, 4)]
    public string exerciseCompleteSpeech = "Excellent work! You've completed the full routine.";
    
    [Tooltip("Delay before returning to idle after completion speech (seconds)")]
    public float returnToIdleDelay = 2f;
    
    [Header("Status (Read-Only)")]
    [SerializeField] private bool isExercising = false;
    [SerializeField] private bool hasCompletedSession = false;
    [SerializeField] private bool singleExerciseMode = false;
    [SerializeField] private int singleExerciseTarget = 0;
    [SerializeField] private int currentExerciseIndex = 0;
    [SerializeField] private int currentStep = 0;

    public  ControlModeSwitch controlModeSwitch;
    
    // Shuffled order
    private int[] shuffledOrder;
    
    // Speech buffering
    private string pendingUserSpeech = "";
    private bool userStillTalking = false;
    private bool animationPlaying = false;
    private bool returnToSessionAfterSingle = false;
    
    // TTS cue speaking flag (for STT muting)
    private bool isSpeakingCue = false;
    
    // --- Public Properties ---
    
    public bool IsExercising => isExercising;
    public bool HasCompletedSession => hasCompletedSession;
    public bool IsSingleExerciseMode => singleExerciseMode;
    public int SingleExerciseTarget => singleExerciseTarget;
    public int CurrentExerciseIndex => currentExerciseIndex;
    public int CurrentStep => currentStep;
    public int[] ShuffledOrder => shuffledOrder;
    public bool IsSpeakingCue => isSpeakingCue;
    public bool IsAnimationPlaying => animationPlaying;
    
    // --- Unity Lifecycle ---
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    
    private void Start()
    {
        if (avatarAnimator == null && AvatarBehaviorController.Instance != null)
        {
            avatarAnimator = AvatarBehaviorController.Instance.GetComponentInChildren<Animator>();
        }
    }
    
    // --- Shuffling ---
    
    private void GenerateShuffledOrder()
    {
        shuffledOrder = new int[exercises.Length];
        for (int i = 0; i < exercises.Length; i++)
        {
            shuffledOrder[i] = exercises[i].animationIndex;
        }
        
        if (randomizeOrder)
        {
            for (int i = shuffledOrder.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int temp = shuffledOrder[i];
                shuffledOrder[i] = shuffledOrder[j];
                shuffledOrder[j] = temp;
            }
        }
        
        Debug.Log($"<color=cyan>[ExerciseController] Shuffled order: [{string.Join(", ", shuffledOrder)}]</color>");
    }
    
    public ExerciseDefinition GetExerciseByAnimationIndex(int animIndex)
    {
        foreach (var ex in exercises)
        {
            if (ex.animationIndex == animIndex) return ex;
        }
        return null;
    }
    
    // --- Session Start ---
    
    /// <summary>
    /// Start a full exercise session. Reuses existing order if available (e.g. user re-do),
    /// or generates a new shuffled order if none exists (e.g. after scene change).
    /// </summary>
    public void StartExerciseSession()
    {
        // Always stop lingering coroutines (e.g. ReturnToIdleRoutine from a previous session)
        // that could overwrite our flags after we set them
        StopAllCoroutines();
        
        // If already exercising, force-stop the current session before restarting
        if (isExercising)
        {
            Debug.Log("<color=yellow>[ExerciseController] Restarting exercise session mid-run.</color>");
            if (ttsHandler != null) ttsHandler.CancelSpeak();
        }
        
        animationPlaying = false;
        isSpeakingCue = false;
        isExercising = true;
        hasCompletedSession = false;
        singleExerciseMode = false;
        currentStep = 0;
        ClearSpeechBuffer();
        
        // Only shuffle on first start or after a scene change (which nulls shuffledOrder).
        // User-initiated re-dos keep the same order.
        if (shuffledOrder == null)
        {
            GenerateShuffledOrder();
        }
        else
        {
            Debug.Log($"<color=cyan>[ExerciseController] Re-using existing order: [{string.Join(", ", shuffledOrder)}]</color>");
        }
        PlayExerciseAtCurrentStep();
        
        Debug.Log("<color=green>[ExerciseController] Exercise session STARTED.</color>");

    }
    
    // --- Core Sequencing ---
    
    /// <summary>
    /// Play the exercise at the current step. Speaks the cue first, then enters SSM.
    /// </summary>
    private void PlayExerciseAtCurrentStep()
    {
        if (currentStep >= shuffledOrder.Length)
        {
            EndExerciseSession();
            return;
        }
        
        int animIndex = shuffledOrder[currentStep];
        currentExerciseIndex = animIndex;
        
        var exerciseDef = GetExerciseByAnimationIndex(animIndex);
        string exerciseName = exerciseDef != null ? exerciseDef.name : $"Exercise {animIndex}";
        string cue = exerciseDef != null ? exerciseDef.cue : "";
        
        Debug.Log($"<color=green>[ExerciseController] Step {currentStep + 1}/{shuffledOrder.Length}: {exerciseName} (index {animIndex})</color>");
        
        if (!string.IsNullOrEmpty(cue))
        {
            SpeakExerciseCue(cue, () =>
            {
                if (isExercising) EnterExerciseSSM(animIndex);
            });
        }
        else
        {
            EnterExerciseSSM(animIndex);
        }
    }
    
    /// <summary>
    /// Enter the Exercise SSM with the given animation index.
    /// </summary>
    private void EnterExerciseSSM(int animIndex)
    {
        animationPlaying = true;
        
        if (controlModeSwitch != null && controlModeSwitch.currentMode == ControlModeSwitch.ControlMode.Human)
        {
            // Human control mode: skip animator/SSM, use a timed delay to advance the sequence
            Debug.Log($"<color=yellow>[ExerciseController] Human control mode — skipping SSM, starting {DelayedCompletionSeconds}s timer for index {animIndex}</color>");
            StartCoroutine(DelayedExerciseCompletion());
            return;
        }
        
        if (avatarAnimator == null && AvatarBehaviorController.Instance != null)
        {
            avatarAnimator = AvatarBehaviorController.Instance.GetComponentInChildren<Animator>();
        }
        
        if (avatarAnimator != null)
        {
            avatarAnimator.SetInteger(exerciseIndexParam, animIndex);
        }
        
        if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
        {
            AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Exercise);
        }
        
        Debug.Log($"<color=green>[ExerciseController] Entered SSM with index {animIndex}</color>");
    }
    
    /// <summary>
    /// Called by ExerciseStateBehaviour when an exercise animation finishes (OnStateExit).
    /// </summary>
    public void OnExerciseAnimationCompleted(int completedExerciseIndex)
    {
        if (!isExercising || !animationPlaying) return;
        
        animationPlaying = false;
        Debug.Log($"<color=cyan>[ExerciseController] Animation {completedExerciseIndex} completed (step {currentStep + 1}/{(shuffledOrder != null ? shuffledOrder.Length : 0)})</color>");
        
        // Single exercise mode
        if (singleExerciseMode)
        {
            // Check for buffered speech first — user may have spoken during the replay
            if (!string.IsNullOrEmpty(pendingUserSpeech))
            {
                singleExerciseMode = false;
                returnToSessionAfterSingle = false;
                
                Debug.Log("<color=yellow>[ExerciseController] Buffered speech during single exercise — processing it.</color>");
                
                if (userStillTalking)
                {
                    Debug.Log("<color=yellow>[ExerciseController] User still talking — entering listening mode.</color>");
                    if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
                    {
                        AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
                    }
                }
                else
                {
                    ProcessBufferedSpeech();
                }
                return;
            }
            
            if (returnToSessionAfterSingle)
            {
                singleExerciseMode = false;
                returnToSessionAfterSingle = false;
                
                Debug.Log("<color=green>[ExerciseController] Single exercise finished, resuming sequence.</color>");
                
                // Resume the exercise sequence from where we left off
                ResumeSequence();
            }
            else
            {
                EndExerciseSession();
            }
            return;
        }
        
        // Check for buffered user speech
        if (!string.IsNullOrEmpty(pendingUserSpeech))
        {
            if (userStillTalking)
            {
                // User is still speaking — go to idle/listening and wait
                // BufferUserSpeech will call ProcessBufferedSpeech when isFinal arrives
                Debug.Log("<color=yellow>[ExerciseController] User still talking — entering listening mode.</color>");
                if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
                {
                    AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
                }
            }
            else
            {
                // User already finished speaking — respond now
                ProcessBufferedSpeech();
            }
            return;
        }
        
        // No speech — advance to next exercise
        currentStep++;
        
        if (currentStep >= shuffledOrder.Length)
        {
            EndExerciseSession();
            return;
        }
        
        // Exit SSM to idle, then play next exercise
        if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
        {
            AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
        }
        
        PlayExerciseAtCurrentStep();
    }
    
    // --- Speech Buffering ---
    
    // Pool of short acknowledgment phrases (spoken while still animating)
    private static readonly string[] quickAcks = new string[]
    {
        "One moment.",
        "Just a sec.",
        "Hold on.",
        "One second.",
        "Give me a sec.",
        "Hang on.",
    };
    private int lastAckIndex = -1;
    
    /// <summary>
    /// Buffer user speech that was captured during an exercise animation.
    /// Called by LLMHandler instead of instant-pausing.
    /// </summary>
    public void BufferUserSpeech(string text, bool isFinal)
    {
        // Stop any ongoing TTS (cue speech) — avatar keeps animating but stops talking
        if (ttsHandler != null && isSpeakingCue)
        {
            ttsHandler.CancelSpeak();
            isSpeakingCue = false;
        }
        
        pendingUserSpeech = text;
        userStillTalking = !isFinal;
        Debug.Log($"<color=yellow>[ExerciseController] Buffered speech (final={isFinal}): \"{text}\"</color>");
        
        if (isFinal)
        {
            if (animationPlaying)
            {
                // User finished speaking but exercise still going → quick ack
                SpeakQuickAck();
                // Full response will be sent in OnExerciseAnimationCompleted
            }
            else if (isExercising && !singleExerciseMode)
            {
                // User finished speaking and exercise already done → respond immediately
                ProcessBufferedSpeech();
            }
        }
        // If !isFinal: user still talking — just buffer, do nothing else.
        // OnExerciseAnimationCompleted will handle it when animation finishes.
    }
    
    private void SpeakQuickAck()
    {
        if (ttsHandler == null) return;
        
        int ackIndex;
        do { ackIndex = Random.Range(0, quickAcks.Length); }
        while (ackIndex == lastAckIndex && quickAcks.Length > 1);
        lastAckIndex = ackIndex;
        
        Debug.Log($"<color=yellow>[ExerciseController] Quick ack: \"{quickAcks[ackIndex]}\"</color>");
        ttsHandler.Speak(quickAcks[ackIndex], "English");
    }
    
    private void ClearSpeechBuffer()
    {
        pendingUserSpeech = "";
        userStillTalking = false;
    }
    
    /// <summary>
    /// Process buffered speech after animation finishes.
    /// If user is done talking → send to LLM immediately (no listening animation).
    /// If user is still talking → go to listening, wait for final transcription.
    /// </summary>
    private void ProcessBufferedSpeech()
    {
        if (string.IsNullOrEmpty(pendingUserSpeech)) return;
        
        if (userStillTalking)
        {
            // User is still talking — go to listening animation, wait for them to finish
            Debug.Log("<color=yellow>[ExerciseController] User still talking — going to listening animation.</color>");
            
            if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
            {
                AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
            }
            
            // When the final transcription arrives via BufferUserSpeech(isFinal=true),
            // it will call ProcessBufferedSpeech again (since animationPlaying is false).
            return;
        }
        
        // User is done talking — send to LLM immediately, no listening animation needed
        string speech = pendingUserSpeech;
        ClearSpeechBuffer();
        
        Debug.Log($"<color=cyan>[ExerciseController] Processing buffered speech: \"{speech}\"</color>");
        
        // Cancel any quick ack TTS still playing so LLM response plays immediately
        if (ttsHandler != null)
        {
            ttsHandler.CancelSpeak();
        }
        
        // Exit SSM to idle so avatar can talk/respond
        if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
        {
            AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
        }
        
        // Send to LLM — don't add [EXERCISE_BETWEEN_MOVES] here,
        // LLMHandler.Question already adds it when isExercising is true
        if (llmHandler != null)
        {
            string contextualInput = GetPlayedOrderContext() + speech;
            llmHandler.Question(contextualInput);
        }
    }
    
    /// <summary>
    /// Returns a context string describing the exercise order shown to the user.
    /// Used so the LLM knows what "the second move" refers to.
    /// </summary>
    public string GetPlayedOrderContext()
    {
        if (shuffledOrder == null || shuffledOrder.Length == 0) return "";
        
        var sb = new System.Text.StringBuilder();
        sb.Append("[Order shown: ");
        for (int i = 0; i < shuffledOrder.Length; i++)
        {
            int idx = shuffledOrder[i];
            string name = "Unknown";
            foreach (var ex in exercises)
            {
                if (ex.animationIndex == idx) { name = ex.name; break; }
            }
            sb.Append($"{i + 1}={name}");
            if (i < shuffledOrder.Length - 1) sb.Append(", ");
        }
        sb.Append("] ");
        
        // Add current position context so LLM knows what "previous" and "current" mean
        sb.Append($"[Currently at step {currentStep + 1} of {shuffledOrder.Length}. ");
        
        // Current exercise
        if (currentStep >= 0 && currentStep < shuffledOrder.Length)
        {
            var currentEx = GetExerciseByAnimationIndex(shuffledOrder[currentStep]);
            if (currentEx != null)
                sb.Append($"Just finished: {currentEx.name} (position {currentStep + 1}). ");
        }
        
        // Previous exercise
        if (currentStep > 0 && currentStep - 1 < shuffledOrder.Length)
        {
            var prevEx = GetExerciseByAnimationIndex(shuffledOrder[currentStep - 1]);
            if (prevEx != null)
                sb.Append($"Previous exercise was: {prevEx.name} (position {currentStep}). ");
        }
        
        sb.Append("] ");
        return sb.ToString();
    }
    
    // --- Single Exercise Playback ---
    
    /// <summary>
    /// Play a single specific exercise and return to idle after it completes.
    /// positionOrIndex: if a shuffled order exists, this is treated as the 1-based position
    /// in the shown order. Otherwise, it's the raw animation index.
    /// </summary>
    public void PlaySingleExercise(int positionOrIndex)
    {
        int actualIndex = positionOrIndex;
        
        // Resolve from shuffled order if available
        if (shuffledOrder != null && shuffledOrder.Length > 0)
        {
            int pos = positionOrIndex - 1; // convert to 0-based
            if (pos >= 0 && pos < shuffledOrder.Length)
            {
                actualIndex = shuffledOrder[pos];
                Debug.Log($"<color=cyan>[ExerciseController] Resolved order position {positionOrIndex} → exercise index {actualIndex}</color>");
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[ExerciseController] Position {positionOrIndex} out of range, using as raw index.</color>");
            }
        }
        
        Debug.Log($"<color=yellow>[ExerciseController] Playing SINGLE exercise: {actualIndex}</color>");
        
        returnToSessionAfterSingle = isExercising && !hasCompletedSession;
        
        // Only set isExercising/hasCompletedSession for mid-session replays.
        // Post-session replays should NOT overwrite these flags —
        // they should only be set by StartExerciseSession or ResetState.
        if (returnToSessionAfterSingle)
        {
            // Mid-session: flags are already correct (isExercising=true, hasCompletedSession=false)
        }
        else
        {
            // Post-session: temporarily mark as exercising for the animation,
            // but preserve hasCompletedSession so we don't try to resume a sequence
            isExercising = true;
            // hasCompletedSession stays as-is (true)
        }
        singleExerciseMode = true;
        singleExerciseTarget = actualIndex;
        currentExerciseIndex = actualIndex;
        ClearSpeechBuffer();
        
        EnterExerciseSSM(actualIndex);
    }
    
    /// <summary>
    /// Continue the exercise sequence after an LLM response (e.g., after answering a question).
    /// Called by LLMHandler when resume_exercise command is received.
    /// </summary>
    public void ResumeSequence()
    {
        if (!isExercising) return;
        
        Debug.Log("<color=green>[ExerciseController] Resuming exercise sequence.</color>");
        
        // Advance to next step and continue
        currentStep++;
        
        if (currentStep >= shuffledOrder.Length)
        {
            EndExerciseSession();
            return;
        }
        
        PlayExerciseAtCurrentStep();
    }

    /// <summary>
    /// Repeats the current step in the sequence instead of advancing.
    /// Called by LLMHandler when repeat_exercise command is received.
    /// </summary>
    public void RepeatCurrentStep()
    {
        if (!isExercising) return;

        Debug.Log("<color=green>[ExerciseController] Repeating current exercise step.</color>");

        if (currentStep >= shuffledOrder.Length)
        {
            EndExerciseSession();
            return;
        }

        PlayExerciseAtCurrentStep();
    }
    
    /// <summary>
    /// Update current exercise index (called by ExerciseStateBehaviour).
    /// </summary>
    public void SetCurrentExerciseIndex(int index)
    {
        currentExerciseIndex = index;
    }
    
    // --- Session End ---
    
    public void EndExerciseSession()
    {
        if (!isExercising) return;
        
        Debug.Log("<color=green>[ExerciseController] Exercise session ENDING.</color>");
        animationPlaying = false;
        
        // Only speak completion message for full routine, not single exercise replays
        if (!singleExerciseMode && !string.IsNullOrEmpty(exerciseCompleteSpeech))
        {
            SpeakExerciseCue(exerciseCompleteSpeech, () =>
            {
                StartCoroutine(ReturnToIdleRoutine());
            });
        }
        else
        {
            StartCoroutine(ReturnToIdleRoutine());
        }
    }
    
    private IEnumerator ReturnToIdleRoutine()
    {
        yield return new WaitForSeconds(returnToIdleDelay);
        
        isExercising = false;
        hasCompletedSession = true;
        singleExerciseMode = false;
        currentExerciseIndex = 0;
        currentStep = 0;
        ClearSpeechBuffer();
        
        if (avatarAnimator != null)
        {
            avatarAnimator.SetInteger(exerciseIndexParam, 1);
        }
        
        if (AvatarBehaviorController.Instance != null && AvatarBehaviorController.Instance.isActiveAndEnabled)
        {
            AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
            Debug.Log("<color=green>[ExerciseController] Avatar returned to Idle.</color>");
        }
    }
    
    /// <summary>
    /// Hard reset all exercise state. Called on scene switch.
    /// Unlike EndExerciseSession, this does NOT speak a completion message or trigger animations.
    /// </summary>
    public void ResetState()
    {
        Debug.Log("<color=yellow>[ExerciseController] Full state reset (scene switch).</color>");
        
        StopAllCoroutines();
        
        if (ttsHandler != null)
        {
            ttsHandler.CancelSpeak();
        }
        
        isExercising = false;
        animationPlaying = false;
        isSpeakingCue = false;
        hasCompletedSession = false;
        singleExerciseMode = false;
        currentExerciseIndex = 0;
        currentStep = 0;
        shuffledOrder = null;
        lastAckIndex = -1;
        ClearSpeechBuffer();
    }
    
    // --- TTS ---
    
    /// <summary>
    /// Delay (in seconds) before auto-completing an exercise in Human control mode.
    /// </summary>
    private const float DelayedCompletionSeconds = 5f;
    
    /// <summary>
    /// In Human control mode, wait then signal exercise animation completed.
    /// Started per-exercise inside EnterExerciseSSM so every step auto-advances.
    /// </summary>
    private IEnumerator DelayedExerciseCompletion()
    {
        int exerciseIndex = currentExerciseIndex;
        Debug.Log($"<color=yellow>[ExerciseController] Waiting {DelayedCompletionSeconds}s for exercise index {exerciseIndex}</color>");
        yield return new WaitForSeconds(DelayedCompletionSeconds);
        Debug.Log($"<color=yellow>[ExerciseController] Delayed completion fired for exercise index {exerciseIndex}</color>");
        OnExerciseAnimationCompleted(exerciseIndex);
    }
    
    public void SpeakExerciseCue(string text, System.Action onComplete = null)
    {
        if (ttsHandler == null || string.IsNullOrEmpty(text)) return;
        
        Debug.Log($"<color=cyan>[ExerciseController] Speaking cue: \"{text}\"</color>");
        
        isSpeakingCue = true;
        
        ttsHandler.Speak(text, "English", () =>
        {
            isSpeakingCue = false;
            onComplete?.Invoke();
        });
    }
    
    public void SpeakExerciseCueDelayed(string text, float delay)
    {
        StartCoroutine(SpeakDelayedRoutine(text, delay));
    }
    
    private IEnumerator SpeakDelayedRoutine(string text, float delay)
    {
        yield return new WaitForSeconds(delay);
        SpeakExerciseCue(text);
    }
}
