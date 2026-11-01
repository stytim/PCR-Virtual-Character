using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ConversationHandler : MonoBehaviour
{
    [Header("Scripts")]
    public STTHandler sttHandler;
    public LLMHandler llmHandler;
    public TTSHandler ttsHandler;

    [Header("Settings")]
    public bool allowInterruptions = false;
    public string userRoleName = "Customer";

    [Header("Conversation")]
    public AudioSource audioSource;

    //public Image statusIndicator; // UI element to show status

    private Coroutine conversationRoutine;

    private bool isAudioPlaying = true;

    // Start is called before the first frame update
    void Start()
    {
        // TrrigerConversation();
        if (ttsHandler.selectedLocalTTS == TTSHandler.TTSOption.ElevenlabsTTS)
        {
            isAudioPlaying = false;
        }
        
        sttHandler.SetUserRoleName(userRoleName);
        WebRTCAudioProcessor.Instance.ProcessAEC = allowInterruptions;
    }
    

    // OnGUI buttons are now handled by SceneSessionManager to ensure visibility across scene switches.
    // void OnGUI()
    // {
    //     (moved to SceneSessionManager)
    // }

    public void TrrigerConversation()
    {
        if (conversationRoutine == null)
        {
            llmHandler.Question("System: Start the Conversation");
            StartConversation();
            //statusIndicator.color = Color.blue; 
        }
        else
        {
            StopConversation();
            //statusIndicator.color = Color.white; 
        }
    }

    public void TrrigerConversation2()
    {
        if (conversationRoutine == null)
        {
            StartConversation();
            //statusIndicator.color = Color.blue;
        }
        else
        {
            StopConversation();
           // statusIndicator.color = Color.white;
        }
    }

    public void RedoLastRequest()
    {
        StopConversation();
        StartConversation();
        llmHandler.ReTry();

    }

    public void ToggleInterruptions()
    {
        allowInterruptions = !allowInterruptions;
        WebRTCAudioProcessor.Instance.ProcessAEC = allowInterruptions;
    }

    private void StartConversation()
    {

        if ((sttHandler == null) || (llmHandler == null) || (audioSource == null) || conversationRoutine != null) return;

        sttHandler.RequestConnect();
        conversationRoutine = StartCoroutine(ListeningRoutine());
    }

    public void StopConversation()
    {
        if (conversationRoutine != null)
        {
            StopCoroutine(conversationRoutine);
            conversationRoutine = null;
        }

        _ = sttHandler.Disconnect();
        sttHandler.Mute();

        try
        {
            if (llmHandler != null)
            {
                llmHandler.CancelReply();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error cancelling LLM Handler reply: {ex.Message}");
        }

        // llmHandler.CancelReply();
        ttsHandler.CancelSpeak();

    }

    public void HandleVoiceInterruption()
    {
        Debug.Log("Voice interruption detected.");
        llmHandler.CancelReply();
        ttsHandler.CancelSpeak();
    }

    string GetTimeOfDay()
    {
        System.DateTime now = System.DateTime.Now;
        int hour = now.Hour;

        if (hour >= 5 && hour < 12) return "Morning";
        else if (hour >= 12 && hour < 17) return "Afternoon";
        else if (hour >= 17 && hour < 21) return "Evening";
        else return "Night";
    }

    string GetGreeting(string timeOfDay, string yourName, string myName, string template)
    {
        string result = string.Format("Good {0}, {1}. I'm {2}. {3}", timeOfDay, yourName, myName, template);

        return result;
    }

    private IEnumerator ListeningRoutine()
    {
        while (true)
        {
            // No need for these. stt handler stay unmuted as the AEC will handle the echo cancelation.
            // // Handle exercise session state. 
            // var exerciseController = ExerciseController.Instance;
            // bool isExercising = exerciseController != null && exerciseController.IsExercising;
            
            // if (isExercising && exerciseController.IsSpeakingCue)
            // {
               
            //     // sttHandler.Mute();
            // }
            // else if (isExercising)
            // {
            //     // During exercise animations — STT stays active so user speech can be buffered
            //     sttHandler.Unmute();
            // }
            if (allowInterruptions)
            {
               // If interruptions are allowed, STT should generally always be recording.
               // The interruption is handled when STT detects speech while TTS is active.
                sttHandler.Unmute();
               
            }
            else
            {

                if ((audioSource.isPlaying && isAudioPlaying) || llmHandler.waitingForReply || ttsHandler.waitingForAudioSynthesize())
                {
                    // sttHandler.StopRecording();
                    // Debug.Log("Muted...");
                    sttHandler.Mute();
                }
                else
                {
                    // sttHandler.StartRecording();
                    // Debug.Log("Listening...");
                    sttHandler.Unmute();
                }
            }

            yield return null;
        }
    }
}