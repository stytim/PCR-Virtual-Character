using LLMUnity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UI;
using Newtonsoft.Json;
using TMPro;

public class LLMHandler : MonoBehaviour
{
    [Header("Scripts")]
    public TTSHandler ttsHandler;
    public STTHandler sttHandler;

    //[Header("WebRTC Streaming")]
    //public TextStreamSender textStreamSender; // Removed in favor of Singleton access

    [Header("LLM")]
    public LLMAgent llmCharacter;
    public bool waitingForReply = false;
    public bool waitingForReply2 = false;
    public bool isWarmedUp = false;

    public bool isSelf = true;

    [Header("Status")]
    public Image statusIndicator; // UI element to show status
    bool isConnected = false;
    public TMP_Text llmResponseText;

    public TMP_Text subtitle;
    public float displayTime = 3f; // How long the subtitle stays fully visible
    public float fadeOutDuration = 0.5f;

    private Coroutine _activeFadeCoroutine;


    [Header("Other")]
   // public MotionSimulation motionSimulation;
    // public AnimationHandler animationHandler;
    // public CommandPublisher commandPublisher;
    // public MovementController movementController;

    public string inputForLLM;
    private DateTime startTime;
    float timer = 0.0f;
    public float timeout = 1.0f;
    private string latestQuestion;
    private string LatestResponse;

    // Start is called before the first frame update
    void Start()
    {
       _ = llmCharacter.Warmup(WarmUpCallback);
    }

    // Update is called once per frame
    void Update()
    {
        // add timer when waitingForReply
        if (waitingForReply)
        {
            timer += Time.deltaTime;
            if (timer >= timeout)
            {
                Debug.LogWarning("LLM took too long");
                timer = 0.0f;
               // ReTry();
            }
        }
    }

    private void WarmUpCallback()
    {
        isWarmedUp = true;
        if(statusIndicator)
            statusIndicator.color = Color.green;
    }


    [ContextMenu("Test Health Check")]
    public async void TestConnection()
    {
        // bool result = await llmCharacter.CheckServerHealth("completion");
        // isConnected = result;
        // statusIndicator.color = result ? Color.green : Color.red;
    }

    /// <summary>
    /// Reset LLM state for scene switch. Clears conversation history and flags.
    /// </summary>
    public void ResetState()
    {
        Debug.Log("<color=yellow>[LLMHandler] Full state reset (scene switch).</color>");
        
        waitingForReply = false;
        waitingForReply2 = false;
        latestQuestion = "";
        LatestResponse = "";
        
        // Clear LLM conversation history for a fresh start
        if (llmCharacter != null)
        {
            llmCharacter.ClearHistory();
        }
    }

    [ContextMenu("Test LLM")]
    public void TestLLM()
    {
        Question(inputForLLM);
    }

    public async void Question(string inputString)
    {
        if (llmCharacter == null) return;
        
        // Handle exercise context
        var exerciseController = ExerciseController.Instance;
        if (exerciseController != null && exerciseController.IsExercising)
        {
            if (exerciseController.IsAnimationPlaying)
            {
                // Animation is playing — buffer the speech, don't interrupt
                Debug.Log("<color=yellow>[LLM] User spoke during exercise animation — buffering speech.</color>");
                exerciseController.BufferUserSpeech(inputString, isFinal: true);
                return;
            }
            else
            {
                // Between moves — add context tag and process normally
                Debug.Log("<color=cyan>[LLM] User speaking between exercise moves — sending to LLM.</color>");
                inputString = "[EXERCISE_BETWEEN_MOVES] " + inputString;
                // Fall through to normal LLM handling
            }
        }
        else if (exerciseController != null && exerciseController.HasCompletedSession)
        {
            Debug.Log("<color=cyan>[LLM] Post-exercise question - adding EXERCISE_COMPLETED context.</color>");
            inputString = "[EXERCISE_COMPLETED] " + exerciseController.GetPlayedOrderContext() + inputString;
        }
        
        // Don't snap to idle if exercise is actively running
        if (exerciseController == null || !exerciseController.IsExercising)
        {
            if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
                AvatarBehaviorController.Instance.TriggerBehavior(AvatarBehavior.Idle);
        }


        latestQuestion = inputString;
        Debug.Log($"[LLM] Sending to LLM: {inputString}");

        waitingForReply = true;
        startTime = DateTime.Now;
        string response = await llmCharacter.Chat(inputString,null, ReplyCompleted);
        ProcessLLMResponse(response);
    }
    

    public void ProcessLLMResponse(string response)
    {
        llmResponseText.text = response;

        var latency = DateTime.Now - startTime;
        Debug.Log($"[LLM] Latency: {latency.Milliseconds} ms.");
        Debug.Log($"[LLM] Response: {response}");
        //LogToFile(response);
        // Initialize default values
        string responseText = "";
        string command = "";
        string behavior = "";

        try
        {
   
            var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(response);
            if (jsonResponse != null)
            {
                jsonResponse.TryGetValue("responseText", out responseText);
                jsonResponse.TryGetValue("command", out command);
                jsonResponse.TryGetValue("behavior", out behavior);
            }

            // Debug extracted values
            // Debug.Log($"Parsed Response - Text: {responseText}, Command: {command}, Behavior: {behavior}");
            // Stream to remote spectator via WebRTC Singleton
            TextStreamSender.Instance?.SendLLMText(responseText);
            
            // Check for exercise control commands FIRST
            bool isResumeExercise = !string.IsNullOrEmpty(command) && command.ToLower() == "resume_exercise";
            bool isRepeatExercise = !string.IsNullOrEmpty(command) && command.ToLower() == "repeat_exercise";
            bool isShowExercise = !string.IsNullOrEmpty(command) && command.ToLower().StartsWith("show_exercise:");
            int showExerciseIndex = 0;
            if (isShowExercise)
            {
                string indexStr = command.Substring("show_exercise:".Length);
                if (!int.TryParse(indexStr, out showExerciseIndex) || showExerciseIndex < 1 || showExerciseIndex > 5)
                {
                    Debug.LogWarning($"[LLM] Invalid show_exercise index: '{indexStr}', ignoring command.");
                    isShowExercise = false;
                }
            }
            
            // Track special behavior scenarios
            bool isPhoneTalking = false;
            bool isExercise = false;
            
            if (!string.IsNullOrEmpty(behavior))
            {
                // Try to convert the string from the LLM into our AvatarBehavior enum.
                if (Enum.TryParse(behavior, true, out AvatarBehavior parsedBehavior))
                {
                    // Track special behaviors that need post-TTS handling
                    isPhoneTalking = (parsedBehavior == AvatarBehavior.PhoneTalking);
                    isExercise = (parsedBehavior == AvatarBehavior.Exercise);
                    
                    // For Exercise, don't trigger immediately - wait until after TTS
                    if (isExercise)
                    {
                        Debug.Log("<color=green>[LLM] Exercise detected - will trigger after TTS completes.</color>");
                    }
                    else if (isResumeExercise)
                    {
                        // Don't trigger any behavior for resume - we'll resume the paused animator
                        Debug.Log("<color=green>[LLM] Resume exercise - skipping behavior trigger.</color>");
                    }
                    else if (isRepeatExercise)
                    {
                        // Don't trigger any behavior for repeat - we'll go back and resume
                        Debug.Log("<color=yellow>[LLM] Repeat exercise - skipping behavior trigger, will go back one step.</color>");
                    }
                    else if (isShowExercise)
                    {
                        // Don't trigger any behavior - we'll show a single exercise after TTS
                        Debug.Log($"<color=yellow>[LLM] Show single exercise {showExerciseIndex} - skipping behavior trigger.</color>");
                    }
                    else
                    {
                        // Defer non-exercise/resume behaviors to onAudioStarted callback
                        // (handled in HandleAvatarReply via the onAudioStarted parameter)
                        Debug.Log($"<color=cyan>[LLM] Behavior '{parsedBehavior}' will trigger when audio starts.</color>");
                    }
                    
                    if (isPhoneTalking)
                    {
                        Debug.Log("<color=yellow>[LLM] PhoneTalking detected - will trigger ExitPhoneTalking after TTS completes.</color>");
                    }
                }
                else
                {
                    Debug.LogWarning($"[LLM] returned an unknown behavior: '{behavior}'");
                }
            }   
            
            
            // Process extracted values
            if (!string.IsNullOrEmpty(responseText))
            {
                // Determine which behavior to trigger when audio starts (if any)
                AvatarBehavior? deferredBehavior = null;
                if (!string.IsNullOrEmpty(behavior) 
                    && Enum.TryParse(behavior, true, out AvatarBehavior pb) 
                    && !isExercise && !isResumeExercise && !isRepeatExercise && !isShowExercise)
                {
                    deferredBehavior = pb;
                }
                
                HandleAvatarReply(responseText, isPhoneTalking, isExercise, isResumeExercise, isRepeatExercise, isShowExercise, showExerciseIndex, deferredBehavior);
            }

            if (!string.IsNullOrEmpty(command))
            {
                // Handle exercise control commands (already detected above, just log)
                if (isResumeExercise)
                {
                    Debug.Log("<color=green>[LLM] LLM decided to resume exercise - will resume after TTS.</color>");
                    // Don't resume here - wait for TTS callback in HandleAvatarReply
                }
                else if (isRepeatExercise)
                {
                    Debug.Log("<color=yellow>[LLM] LLM decided to repeat exercise - will go back after TTS.</color>");
                }
                else if (isShowExercise)
                {
                    Debug.Log($"<color=yellow>[LLM] LLM decided to show exercise {showExerciseIndex} - will play after TTS.</color>");
                }
                else
                {
                    StartCoroutine(ExecuteCommandCoroutine(command));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing LLM response: {ex.Message}");
            ttsHandler.Speak("Sorry, could you repeat that?");
        }
    }


    void HandleAvatarReply(string reply, bool isPhoneTalking = false, bool isExercise = false, bool isResumeExercise = false, bool isRepeatExercise = false, bool isShowExercise = false, int showExerciseIndex = 0, AvatarBehavior? deferredBehavior = null)
    {
        if (ttsHandler == null) return;
        
        if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
            AvatarBehaviorController.Instance.LookAtTarget(Camera.main.transform);

        //if (reply == LatestResponse) return;
        //LatestResponse = reply;

        Action<string, string> updateUIAndSpeak = (text, language) =>
        {
            if (subtitle != null)
            {
                subtitle.text = text; // Update subtitle text field
            }
            // Debug.Log($"Input for Avatar TTS: {text}");
            ttsHandler.Speak(text, language, 
                onComplete: () => 
                {
                    Debug.Log("<color=green>TTS playback complete.</color>");
                
                    if (isShowExercise)
                    {
                        // TTS finished - play a single exercise and stop
                        Debug.Log($"<color=yellow>>>> TTS complete - playing single exercise {showExerciseIndex}.</color>");
                    
                        if (ExerciseController.Instance != null)
                        {
                            ExerciseController.Instance.PlaySingleExercise(showExerciseIndex);
                        }
                    }
                    else if (isRepeatExercise || isResumeExercise)
                    {
                        // TTS finished - resume the exercise sequence
                        Debug.Log("<color=green>>>> TTS complete - resuming/repeating exercise sequence.</color>");
                    
                        if (ExerciseController.Instance != null)
                        {
                            if (isRepeatExercise)
                            {
                                ExerciseController.Instance.RepeatCurrentStep();
                            }
                            else
                            {
                                ExerciseController.Instance.ResumeSequence();
                            }
                        }
                        // Don't trigger closure animations - exercise continues
                    }
                    else if (isExercise)
                    {
                        // TTS finished - now start the exercise session
                        Debug.Log("<color=green>>>> TTS complete - starting exercise session.</color>");
                    
                        if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
                        {
                            AvatarBehaviorController.Instance.SetTalkingState(false);
                        }
                    
                        // StartExerciseSession handles shuffling, speaking the first cue,
                        // and entering the SSM with the correct ExerciseIndex.
                        if (ExerciseController.Instance != null)
                        {
                            ExerciseController.Instance.StartExerciseSession();
                        }
                        // Don't trigger closure animations - let Exercise play
                    }
                    else if (isPhoneTalking)
                    {
                        // Phone call ended - trigger ExitPhoneTalking animator trigger directly
                        Debug.Log("<color=yellow>>>> Phone call ended - triggering ExitPhoneTalking animator trigger.</color>");
                    
                        if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
                        {
                            // Trigger the animator directly
                            AvatarBehaviorController.Instance.TriggerAnimatorTrigger("ExitPhoneTalking");
                            AvatarBehaviorController.Instance.SetTalkingState(false);
                        }
                    
                        // After a short delay, speak the follow-up to transition back naturally
                        StartCoroutine(SpeakPhoneClosingFollowUp());
                    }
                    else
                    {
                        if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
                        {
                            // Normal closure animation for other responses
                            AvatarBehaviorController.Instance.TriggerClosureAnimation();
                            AvatarBehaviorController.Instance.SetTalkingState(false);
                        
                            float naturalDelay = UnityEngine.Random.Range(1.5f, 2.5f);
                            AvatarBehaviorController.Instance.ClearLookTarget(naturalDelay);
                        }
                    }
                },
                onAudioStarted: () =>
                {
                    // Trigger the behavior animation only when audio actually starts playing
                    if (deferredBehavior.HasValue)
                    {
                        Debug.Log($"<color=cyan>[LLM] Audio started – triggering deferred behavior: {deferredBehavior.Value}</color>");
                        if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
                            AvatarBehaviorController.Instance.TriggerBehavior(deferredBehavior.Value);
                    }
                });
        };

        updateUIAndSpeak(reply, "English");
    }
    
    /// <summary>
    /// Speaks a follow-up phrase after ending a phone call to transition back naturally.
    /// </summary>
    private IEnumerator SpeakPhoneClosingFollowUp()
    {
        // Wait for the phone closing animation to play a bit
        yield return new WaitForSeconds(2.0f);
        
        Debug.Log("<color=cyan>Speaking phone closing follow-up...</color>");
        
        // Speak the follow-up phrase
        ttsHandler.Speak("Sorry, where were we?", "English", () =>
        {
            Debug.Log("<color=green>Phone closing follow-up complete. Returning to normal state.</color>");
            
            // After the follow-up, trigger normal closure and return to idle
            if (AvatarBehaviorController.Instance && AvatarBehaviorController.Instance.isActiveAndEnabled)
            {
                AvatarBehaviorController.Instance.TriggerClosureAnimation();
                float naturalDelay = UnityEngine.Random.Range(1.5f, 2.5f);
                AvatarBehaviorController.Instance.ClearLookTarget(naturalDelay);
            }
        });
    }

    public void ShowSubtitle()
    {
        if (subtitle != null)
        {
            subtitle.gameObject.SetActive(true); // Ensure the GameObject is active

            // Reset alpha to fully opaque
            Color subtitleColor = subtitle.color;
            subtitleColor.a = 1f; // 1 is fully opaque
            subtitle.color = subtitleColor;

            // If there's an old fade coroutine running, stop it
            if (_activeFadeCoroutine != null)
            {
                StopCoroutine(_activeFadeCoroutine);
            }

            // Start the new fade coroutine
            //_activeFadeCoroutine = StartCoroutine(FadeOutSubtitleAfterDelay());
        }
    }

    /// <summary>
    /// Public method to be called from another script to initiate the subtitle fade-out.
    /// </summary>
    public void TriggerSubtitleFadeOut()
    {
        // Check if the subtitle is active, has text, and is not already fading or fully transparent
        if (subtitle != null && subtitle.gameObject.activeInHierarchy && !string.IsNullOrEmpty(subtitle.text) && subtitle.color.a > 0)
        {
            if (_activeFadeCoroutine != null)
            {
                StopCoroutine(_activeFadeCoroutine); // Stop any existing fade if this is called again
            }
            _activeFadeCoroutine = StartCoroutine(FadeOutEffect());
        }
        else
        {
            if (subtitle != null && subtitle.color.a <= 0)
            {
                Debug.Log("Subtitle is already faded out or transparent. Fade-out trigger ignored.");
            }
            else
            {
                Debug.LogWarning("Cannot trigger subtitle fade-out: Subtitle is not active, has no text, or reference is missing.");
            }
        }
    }

    private IEnumerator FadeOutEffect()
    {
        // Fade out the subtitle
        float currentTime = 0f;
        Color startColor = subtitle.color; // Current color (should be opaque or partially faded if re-triggered)

        while (currentTime < fadeOutDuration)
        {
            currentTime += Time.deltaTime;
            // Calculate the new alpha value by linearly interpolating from current alpha to 0 (transparent)
            float alpha = Mathf.Lerp(startColor.a, 0f, currentTime / fadeOutDuration);

            subtitle.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null; // Wait for the next frame before continuing the loop
        }

        // Ensure alpha is exactly 0 at the end
        Color finalColor = subtitle.color;
        finalColor.a = 0f;
        subtitle.color = finalColor;

        // Optional: Deactivate the subtitle GameObject after it has faded out
        // subtitle.gameObject.SetActive(false);

        _activeFadeCoroutine = null; // Clear the reference to the completed coroutine
    }


    private IEnumerator FadeOutSubtitleAfterDelay()
    {
        // 1. Wait for the initial display time (3 seconds)
        yield return new WaitForSeconds(displayTime);

        // 2. Fade out the subtitle
        float currentTime = 0f;
        Color startColor = subtitle.color; // Should be fully opaque here

        while (currentTime < fadeOutDuration)
        {
            currentTime += Time.deltaTime;
            // Calculate the new alpha value by linearly interpolating from 1 (opaque) to 0 (transparent)
            float alpha = Mathf.Lerp(startColor.a, 0f, currentTime / fadeOutDuration);

            subtitle.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null; // Wait for the next frame before continuing the loop
        }

        // Ensure alpha is exactly 0 at the end
        subtitle.color = new Color(startColor.r, startColor.g, startColor.b, 0f);

        // Optional: Deactivate the subtitle GameObject after it has faded out
        subtitle.gameObject.SetActive(false);

        _activeFadeCoroutine = null; // Clear the reference to the completed coroutine
    }

    // void HandleDoctorReply(string reply)
    // {
    //     Debug.Log($"Input for Doctor TTS: {reply}");
    //     MessageHandler.Instance?.SendMessageToNetwork(reply);
    // }


    IEnumerator ExecuteCommandCoroutine(string command)
    {
        yield return null; // Ensures it does not block immediately
        ExecuteCommand(command);
    }

    public void ExecuteCommand(string command)
    {
        // commandPublisher.PublishRobotCommand(command);
    }

    public void ReTry()
    {
        Question(latestQuestion);
    }

    void Reply2Completed()
    {
        waitingForReply2 = false;
        timer = 0.0f;
    }

    void ReplyCompleted()
    {
        waitingForReply = false;
    }

    public void CancelReply()
    {
        try
        {
            llmCharacter.CancelRequests();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error cancelling LLM Handler reply: {ex.Message}");
        }
       
        waitingForReply = false;
    }
}
