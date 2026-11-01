using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using com.rfilkov.components;
using BodyTrackingStreaming;

/// <summary>
/// Manages switching between two scene roots (prefab-based activation/deactivation).
/// Handles the full lifecycle: stop conversation → disconnect → deactivate → activate → readiness check → auto-start.
/// </summary>

public class SceneSessionManager : MonoBehaviour
{
    public static SceneSessionManager Instance { get; private set; }

    [Header("Scene Roots")]
    [Tooltip("Root GameObject for AIControl scene content")]
    public GameObject sceneRootA;

    [Tooltip("Root GameObject for AIControl_Exercise scene content")]
    public GameObject sceneRootB;

    [Header("Handler References - Scene A")]
    public ConversationHandler conversationHandlerA;
    public STTHandler sttHandlerA;
    public LLMHandler llmHandlerA;
    public Transform headA;
    public SkinnedMeshRenderer headA_SMR;
    public SkinnedMeshRenderer teethA_SMR;
    public SkinnedMeshRenderer tongueA_SMR;
    public ControlModeSwitch controlModeSwitchA;
    public PCRSwitch pcrSwitchA;

    [Header("Handler References - Scene B")]
    public ConversationHandler conversationHandlerB;
    public STTHandler sttHandlerB;
    public LLMHandler llmHandlerB;
    public Transform headB;
    public SkinnedMeshRenderer headB_SMR;
    public SkinnedMeshRenderer teethB_SMR;
    public SkinnedMeshRenderer tongueB_SMR;
    public ControlModeSwitch controlModeSwitchB;
    public PCRSwitch pcrSwitchB;

    [Header("Avatar References (for AvatarBehaviorController re-init)")]
    [Tooltip("The avatar GameObject in Scene A (must have an Animator component)")]
    public GameObject avatarA;
    [Tooltip("The avatar GameObject in Scene B (must have an Animator component)")]
    public GameObject avatarB;

    public UserAvatarMatcher userAvatarMatcher;
    public BodyDataReceiver bodyDataReceiver;

    [Header("Settings")]
    [Tooltip("If true, Scene A starts active. If false, Scene B starts active.")]
    public bool startWithSceneA = true;

    [Tooltip("Delay (seconds) after all services are ready before auto-starting conversation")]
    public float autoStartDelay = 3.0f;

    [Tooltip("Maximum time (seconds) to wait for services before giving up")]
    public float maxWaitTime = 30.0f;

    [Tooltip("If false, auto-start will proceed even if STT server is unreachable")]
    public bool requireSTT = false;

    [Header("Events")]
    public UnityEvent OnSceneChanged;

    [HideInInspector]
    public Transform currentHead;

    [HideInInspector]
    public SkinnedMeshRenderer currentHeadSMR;

    [HideInInspector]
    public SkinnedMeshRenderer currentTeethSMR;

    [HideInInspector]
    public SkinnedMeshRenderer currentTongueSMR;

    // Current state
    private bool isSceneAActive;
    private Coroutine readinessCoroutine;

    // Labels for logging
    public string SceneAName = "AIControl";
    public string SceneBName = "AIControl_Exercise";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Set initial state
        isSceneAActive = startWithSceneA;

        if (sceneRootA != null) sceneRootA.SetActive(isSceneAActive);
        if (sceneRootB != null) sceneRootB.SetActive(!isSceneAActive);


        string activeName = isSceneAActive ? SceneAName : SceneBName;
        Debug.Log($"[SceneManager] Starting with {activeName}");

        SetCurrentTransformsAndSkinnedMeshRenderers();

        // Begin readiness check for the initially active scene
        StartReadinessCheck();

        OnSceneChanged?.Invoke();
    }

    void Update()
    {
        // Keyboard shortcut for editor testing
        if (Input.GetKeyDown(KeyCode.F5))
        {
            SwitchScene();
        }
    }

    private void SetCurrentTransformsAndSkinnedMeshRenderers()
    {
        currentHead = isSceneAActive ? headA : headB;
        currentHeadSMR = isSceneAActive ? headA_SMR : headB_SMR;
        currentTeethSMR = isSceneAActive ? teethA_SMR : teethB_SMR;
        currentTongueSMR = isSceneAActive ? tongueA_SMR : tongueB_SMR;
    }

    /// <summary>
    /// OnGUI draws scene switch button + all ConversationHandler buttons for the active scene.
    /// This ensures buttons are always visible regardless of which scene root is active.
    /// </summary>
    void OnGUI()
    {
        // --- Scene Switch (top-left) ---
        GUILayout.BeginArea(new Rect(10, 10, 200, 60));
        string currentScene = isSceneAActive ? SceneAName : SceneBName;
        string otherScene = isSceneAActive ? SceneBName : SceneAName;
        GUILayout.Label($"Active: {currentScene}");
        if (GUILayout.Button($"Switch to {otherScene}"))
        {
            SwitchScene();
        }
        GUILayout.EndArea();

        // --- Active scene's conversation buttons (top-right, same position as ConversationHandler.OnGUI) ---
        ConversationHandler conversation = isSceneAActive ? conversationHandlerA : conversationHandlerB;
        LLMHandler llm = isSceneAActive ? llmHandlerA : llmHandlerB;
        if (conversation != null)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 160, 0, 160, 400));

            if (GUILayout.Button("Trigger Conversation"))
            {
                conversation.TrrigerConversation();
            }
            if (GUILayout.Button("Explain"))
            {
                AvatarBehaviorController.Instance?.TriggerBehavior(AvatarBehavior.Explain);
            }
            if (GUILayout.Button("Idle"))
            {
                AvatarBehaviorController.Instance?.TriggerBehavior(AvatarBehavior.Idle);
            }
            if (GUILayout.Button("Disagree"))
            {
                AvatarBehaviorController.Instance?.TriggerBehavior(AvatarBehavior.Disagree);
            }

            GUILayout.Space(10);
            GUILayout.Label("--- Vicon Behaviors ---");

            if (GUILayout.Button("ActiveListen"))
            {
                AvatarBehaviorController.Instance?.TriggerBehavior(AvatarBehavior.ActiveListen);
            }
            if (GUILayout.Button("FillerIdle"))
            {
                AvatarBehaviorController.Instance?.TriggerBehavior(AvatarBehavior.FillerIdle);
            }
            if (GUILayout.Button("Counting"))
            {
                AvatarBehaviorController.Instance?.TriggerBehavior(AvatarBehavior.Counting);
            }
            if (GUILayout.Button("Test Closure"))
            {
                AvatarBehaviorController.Instance?.TriggerClosureAnimation();
            }

            GUILayout.Space(10);

            if (llm != null)
            {
                if (GUILayout.Button("Preparation Start"))
                {
                    llm.Question("System: Preparation Start");
                }
                if (GUILayout.Button("Detect"))
                {
                    llm.ExecuteCommand("detect");
                }
                if (GUILayout.Button("Preparation Done"))
                {
                    llm.Question("System: Preparation Done");
                }
            }

            if (GUILayout.Button("Trigger Conversation 2"))
            {
                conversation.TrrigerConversation2();
            }

            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// Switches between Scene A and Scene B.
    /// Full lifecycle: stop → disconnect → deactivate → activate → readiness → auto-start.
    /// </summary>
    public void SwitchScene()
    {
        if (sceneRootA == null || sceneRootB == null)
        {
            Debug.LogError("[SceneManager] Scene roots not assigned!");
            return;
        }

        // Cancel any running readiness check
        if (readinessCoroutine != null)
        {
            StopCoroutine(readinessCoroutine);
            readinessCoroutine = null;
        }

        // 1. Stop conversation on the current scene
        ConversationHandler currentConversation = isSceneAActive ? conversationHandlerA : conversationHandlerB;
        if (currentConversation != null)
        {
            currentConversation.StopConversation();
            Debug.Log("[SceneManager] Stopped conversation on outgoing scene.");
        }

        // 1b. Reset exercise and LLM state
        if (ExerciseController.Instance != null)
        {
            ExerciseController.Instance.ResetState();
        }
        
        // Reset LLM for both scenes (clear conversation history)
        if (currentConversation != null && currentConversation.GetComponent<LLMHandler>() != null)
        {
            currentConversation.GetComponent<LLMHandler>().ResetState();
        }

        // Read Current Mode from the outgoing scene's ControlModeSwitch and apply it to the incoming scene's ControlModeSwitch
        ControlModeSwitch outgoingCMS = isSceneAActive ? controlModeSwitchA : controlModeSwitchB;
        ControlModeSwitch incomingCMS = isSceneAActive ? controlModeSwitchB : controlModeSwitchA;

        // Read PCR state from the outgoing scene's PCRSwitch and apply it to the incoming scene's PCRSwitch
        PCRSwitch outgoingPCR = isSceneAActive ? pcrSwitchA : pcrSwitchB;
        PCRSwitch incomingPCR = isSceneAActive ? pcrSwitchB : pcrSwitchA;

        // 2. Deactivate current, activate other
        string oldScene = isSceneAActive ? SceneAName : SceneBName;
        isSceneAActive = !isSceneAActive;
        string newScene = isSceneAActive ? SceneAName : SceneBName;

        sceneRootA.SetActive(isSceneAActive);
        sceneRootB.SetActive(!isSceneAActive);

        SetCurrentTransformsAndSkinnedMeshRenderers();

        Debug.Log($"[SceneManager] Switched from {oldScene} → {newScene}");

        // 3. Re-initialize AvatarBehaviorController with the new scene's avatar
        ReinitializeAvatar();

        // 4. Fire event
        OnSceneChanged?.Invoke();

        // 5. Start readiness check for the newly active scene
        StartReadinessCheck();

        // 6. Sync Control Mode in the new scene to match the old scene
        if (incomingCMS != null)
        {
            incomingCMS.SetControlMode(outgoingCMS.currentMode);
        }

        // 7. Sync PCR state in the new scene to match the old scene
        if (outgoingPCR != null && incomingPCR != null)
        {
            incomingPCR.SetMode(outgoingPCR.currentMode);
        }
    }

    /// <summary>
    /// Begins polling services for readiness, then auto-starts conversation.
    /// </summary>
    private void StartReadinessCheck()
    {
        if (readinessCoroutine != null)
        {
            StopCoroutine(readinessCoroutine);
        }

        // Get handlers for the currently active scene
        ConversationHandler conversation = isSceneAActive ? conversationHandlerA : conversationHandlerB;
        STTHandler stt = isSceneAActive ? sttHandlerA : sttHandlerB;
        LLMHandler llm = isSceneAActive ? llmHandlerA : llmHandlerB;
        readinessCoroutine = StartCoroutine(CheckReadinessAndAutoStart(conversation, stt, llm));
    }

    /// <summary>
    /// Coroutine that polls all services for readiness and then auto-triggers conversation.
    /// </summary>
    private IEnumerator CheckReadinessAndAutoStart(
        ConversationHandler conversation, STTHandler stt, LLMHandler llm)
    {
        string sceneName = isSceneAActive ? SceneAName : SceneBName;
        Debug.Log($"[SceneManager] Checking service readiness for {sceneName}...");

        float elapsed = 0f;
        bool sttReady = false;
        bool llmReady = false;
        bool ttsReady = false;

        while (elapsed < maxWaitTime)
        {
            // Check STT (optional based on requireSTT setting)
            if (!sttReady && stt != null)
            {
                sttReady = stt.lastServerCheckResult;
                if (sttReady) Debug.Log($"[SceneManager] ✓ STT server ready");
            }

            // Check LLM
            if (!llmReady && llm != null)
            {
                llmReady = llm.isWarmedUp;
                if (llmReady) Debug.Log($"[SceneManager] ✓ LLM warmed up");
            }

            // Determine if we can proceed
            bool sttOk = sttReady || !requireSTT;
            bool allReady = sttOk && llmReady && ttsReady;

            if (allReady)
            {
                if (!sttReady)
                {
                    Debug.LogWarning($"[SceneManager] ⚠ STT server not available, but proceeding anyway (requireSTT=false)");
                }
                Debug.Log($"[SceneManager] ✓ All required services ready for {sceneName}! Waiting {autoStartDelay}s before auto-starting conversation...");

                yield return new WaitForSeconds(autoStartDelay);

                // Auto-trigger conversation
                if (conversation != null)
                {
                    Debug.Log($"[SceneManager] Auto-triggering conversation for {sceneName}");
                    conversation.TrrigerConversation();
                }
                else
                {
                    Debug.LogWarning($"[SceneManager] ConversationHandler is null for {sceneName}, cannot auto-start.");
                }

                readinessCoroutine = null;
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;

            // Log progress every 5 seconds
            if (elapsed % 5f < 0.5f)
            {
                Debug.Log($"[SceneManager] Waiting for services ({elapsed:F0}s / {maxWaitTime:F0}s) — " +
                          $"STT:{(sttReady ? "✓" : "✗")} LLM:{(llmReady ? "✓" : "✗")} TTS:{(ttsReady ? "✓" : "✗")}");
            }
        }

        // Timeout
        Debug.LogWarning($"[SceneManager] ⚠ Service readiness timeout after {maxWaitTime}s for {sceneName}! " +
                         $"STT:{(sttReady ? "✓" : "✗")} LLM:{(llmReady ? "✓" : "✗")} TTS:{(ttsReady ? "✓" : "✗")}. " +
                         $"You can still trigger conversation manually.");

        readinessCoroutine = null;
    }

    /// <summary>
    /// Returns the name of the currently active scene.
    /// </summary>
    public string GetActiveSceneName()
    {
        return isSceneAActive ? SceneAName : SceneBName;
    }

    /// <summary>
    /// Returns whether Scene A is currently active.
    /// </summary>
    public bool IsSceneAActive()
    {
        return isSceneAActive;
    }

    /// <summary>
    /// Re-initializes AvatarBehaviorController with the current scene's avatar.
    /// </summary>
    private void ReinitializeAvatar()
    {
        GameObject activeAvatar = isSceneAActive ? avatarA : avatarB;

        if (activeAvatar != null && AvatarBehaviorController.Instance != null)
        {
            AvatarBehaviorController.Instance.Init(activeAvatar);
            Debug.Log($"[SceneManager] Re-initialized AvatarBehaviorController with avatar: {activeAvatar.name}");
        }
        else if (activeAvatar == null)
        {
            Debug.LogWarning("[SceneManager] No avatar assigned for the active scene.");
        }
        userAvatarMatcher.avatarModel = activeAvatar;
        userAvatarMatcher.isAvatarInitialized = false;
        bodyDataReceiver.avatarRoot = activeAvatar.transform.GetChild(0).GetChild(0);
        bodyDataReceiver.AutoMapBones();
    }
}
