using ElevenLabs;
using ElevenLabs.Models;
using ElevenLabs.TextToSpeech;
using ElevenLabs.Voices;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Collections;
using Utilities.Audio;
using App.Audio; // Add this namespace
using System;

public class ElevenLabsTTS : MonoBehaviour, ITTSSpeaker
{
    [Header("References")]
    [SerializeField] private A2FClient a2fClient;
    [SerializeField] private MultiMeshAvatarAnimator avatarAnimator;
    [SerializeField] private ElevenLabsConfiguration configuration;

    // [SerializeField] private StreamAudioSource streamAudioSource;
    [SerializeField] private SyncAudioSource streamAudioSource;

    [Header("Settings")]
    [SerializeField] private string voiceId = "yUy9CCX9brt8aPVvIWy3";
    [SerializeField] private OutputFormat outputFormat = OutputFormat.PCM_16000;

    [Tooltip("If true, processes audio for Audio2Face (Resampling + Network Send). Disable to debug crashes.")]
    public bool enableA2F = true;

    [Tooltip("If true, streams audio in chunks (faster start). If false, downloads full clip before playing (better sync).")]
    public bool useStreaming = true; // NEW: Toggle for the mode

    private ElevenLabsClient api;
    private Voice voice;
    private CancellationTokenSource speechCancellationTokenSource;
    
    // Store both text and callback together
    private readonly Queue<(string text, Action onComplete, Action onAudioStarted)> speechQueue = new Queue<(string, Action, Action)>();
    
    private volatile bool isSpeaking = false;
    private volatile ITTSSpeaker.AudioStatus currentStatus = ITTSSpeaker.AudioStatus.Completed;
    private float _resetTimestamp = 0f; // For A2F latency diagnostics
    private bool _firstHandleAudioThisSentence = false;

    /// <summary>True once the ElevenLabs voice has been fetched successfully.</summary>
    public bool isVoiceReady { get; private set; } = false;


    [TextArea(3, 10)][SerializeField] private string message;

    private async void Start()
    {
        Debug.Log("Playback Sample Rate:" + AudioSettings.outputSampleRate);
        
        // Ensure MainThreadDispatcher exists
        if (FindObjectOfType<MainThreadDispatcher>() == null)
        {
            var go = new GameObject("MainThreadDispatcher");
            go.AddComponent<MainThreadDispatcher>();
            Debug.Log("[ElevenLabsTTS] Created MainThreadDispatcher.");
        }

        // Auto-resolve
        if (streamAudioSource == null) streamAudioSource = GetComponent<SyncAudioSource>();
        if (avatarAnimator == null) avatarAnimator = GetComponent<MultiMeshAvatarAnimator>();
        if (avatarAnimator == null && a2fClient != null) avatarAnimator = a2fClient.GetComponent<MultiMeshAvatarAnimator>();

        // Ensure the Source is Playing for OnAudioFilterRead to work
        if (streamAudioSource != null)
        {
            var unitySource = streamAudioSource.GetComponent<AudioSource>();
            if (unitySource != null)
            {
                unitySource.Stop();
                unitySource.Play();
            }
        }

        api = new ElevenLabsClient(configuration); // Original initialization

        if (voice == null)
        {
            try
            {
                voice = await api.VoicesEndpoint.GetVoiceAsync(voiceId); // Original voice fetching
                isVoiceReady = true;
                Debug.Log("[ElevenLabsTTS] Voice fetched successfully.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to fetch voice: {e.Message}");
            }
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            TestSpeak();
        }
    }

    public ITTSSpeaker.AudioStatus GetStatus()
    {
        return currentStatus;
    }

    [ContextMenu("Test Speak")]
    public void TestSpeak()
    {
        if (!string.IsNullOrEmpty(message)) Speak(message);
    }

    public void Speak(string input, string language = "English", Action onComplete = null, Action onAudioStarted = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        
        // Store text, completion callback, and audio-started callback
        speechQueue.Enqueue((input, onComplete, onAudioStarted));
        
        if (!isSpeaking) ProcessSpeechQueueAsync();
    }

    private async void ProcessSpeechQueueAsync()
    {
        isSpeaking = true;

        // Restart AEC to prevent drift accumulation from previous runs
        // REVERTED: Clearing state causes initial echo to leak and trigger VAD
        /* if (WebRTCAudioProcessor.Instance != null) 
        {
            WebRTCAudioProcessor.Instance.Restart();
        } */

        while (speechQueue.Count > 0)
        {
            var (textToSpeak, onComplete, onAudioStarted) = speechQueue.Dequeue();
            currentStatus = ITTSSpeaker.AudioStatus.Playing;
            bool firstChunkReceived = false;

            // 1. Reset Sync Session (Clears buffers and resets Sample Clock to 0)
            if (avatarAnimator != null) avatarAnimator.ResetSession();
            if (streamAudioSource != null) streamAudioSource.ResetTime();
            _resetTimestamp = Time.time;
            _firstHandleAudioThisSentence = true;
            // Debug.Log($"📌 [TTS] Session reset at t={_resetTimestamp:F3}s");

            // Start a fresh A2F stream for this sentence
            if (a2fClient != null && enableA2F)
            {
                a2fClient.StartNewStream();
            }

            speechCancellationTokenSource?.Dispose();
            speechCancellationTokenSource = new CancellationTokenSource();
            var token = speechCancellationTokenSource.Token;
            Debug.Log($"[TTS] Starting to process speech: \"{textToSpeak}\"");

            try
            {
                var request = new TextToSpeechRequest(
                    voice,
                    textToSpeak,
                    model: Model.MultiLingualV2,
                    outputFormat: outputFormat,
                    withTimestamps: true
                );

                if (useStreaming)
                {
                    // --- MODE A: STREAMING (Low Latency) ---
                    await api.TextToSpeechEndpoint.TextToSpeechAsync(request, async partialClip =>
                    {
                        try
                        {
                            // 1. Copy data IMMEDIATELY on the callback thread (Background)
                            float[] floatArray = new float[0];
                            if (partialClip.ClipSamples.IsCreated) 
                            {
                                floatArray = partialClip.ClipSamples.ToArray();
                            }

                            // 2. Dispatch to Main Thread (FIRE AND FORGET)
                            MainThreadDispatcher.Enqueue(() =>
                            {
                                try
                                {
                                    if (floatArray.Length > 0)
                                    {
                                        // Fire onAudioStarted on the very first chunk
                                        if (!firstChunkReceived)
                                        {
                                            firstChunkReceived = true;
                                            Debug.Log("[TTS] First audio chunk received – triggering onAudioStarted");
                                            onAudioStarted?.Invoke();
                                        }
                                        HandleAudioData(floatArray);
                                    }
                                }
                                catch (Exception e)
                                {
                                    Debug.LogError($"[TTS] Error processing stream chunk: {e.Message}");
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                             Debug.LogError($"[TTS] Fatal Error in callback: {ex.Message}");
                        }
                        
                        await Task.CompletedTask;
                    }, cancellationToken: token);
                }
                else
                {
                    // --- MODE B: FULL DOWNLOAD (Better Sync) ---
                    var voiceClip = await api.TextToSpeechEndpoint.TextToSpeechAsync(request, cancellationToken: token);

                    Debug.Log($"TTS Clip - Duration: {voiceClip.AudioClip.length}");
                    if (voiceClip.ClipSamples.IsCreated)
                    {
                        // Fire onAudioStarted before playing
                        Debug.Log("[TTS] Full clip received – triggering onAudioStarted");
                        onAudioStarted?.Invoke();
                        HandleAudioData(voiceClip.ClipSamples.ToArray());
                    }
                }

                // *** CRITICAL: Signal A2F that this sentence's audio is complete ***
                if (a2fClient != null && enableA2F)
                {
                    try
                    {
                        await a2fClient.SendEndOfAudio();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TTS] Error sending EndOfAudio: {e.Message}");
                    }
                }

                // Wait for buffer to drain
                while (streamAudioSource != null && !streamAudioSource.IsEmpty && !token.IsCancellationRequested)
                {
                    await Task.Delay(100);
                }
                
                // Invoke the completion callback
                if (onComplete != null && !token.IsCancellationRequested)
                {
                    onComplete.Invoke();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TTS Error: {e.Message}");
            }
        }

        currentStatus = ITTSSpeaker.AudioStatus.Completed;
        isSpeaking = false;
    }

    // Shared logic to handle audio buffers copy
    private void HandleAudioData(float[] samples)
    {
        // DIAGNOSTIC: Log timing of first audio chunk relative to reset
        if (_firstHandleAudioThisSentence)
        {
            _firstHandleAudioThisSentence = false;
            float delta = Time.time - _resetTimestamp;
            float playbackTime = (streamAudioSource != null) ? streamAudioSource.PlaybackTime : -1f;
            // Debug.Log($"📌 [TTS] First audio chunk arrived {delta:F3}s after reset. " +
            //           $"PlaybackTime={playbackTime:F3}s | Samples={samples.Length}");
        }
        // 1. Play Audio (High Quality Native Rate)
        if (streamAudioSource != null)
        {
            try 
            {
                // Log detailed stats about what we are sending to speakers (every ~100 logs might spam, but good for debug)
                // Debug.Log($"[TTS -> AudioSource] Sending {samples.Length} samples.");
                streamAudioSource.SampleCallback(samples);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TTS] Error sending to AudioSource: {e.Message}");
            }
        }

        // 2. Animate Face (Resampled to 16k)
        if (a2fClient != null && enableA2F && samples.Length > 0)
        {
            try
            {
                int currentRate = AudioSettings.outputSampleRate;
                int targetRate = 16000;

                // We need to resample managed array. PCMEncoder might only take NativeArray?
                // Using a temporary native array just for Resampling (which is a job usually)
                // OR check if PCMEncoder supports float[]
                // Assuming it takes NativeArray based on previous code.
                
                using (var nativeInput = new NativeArray<float>(samples, Allocator.TempJob))
                {
                    var resampledForA2F = PCMEncoder.Resample(
                       nativeInput,
                       currentRate,
                       targetRate,
                       Allocator.TempJob
                    );

                    try
                    {
                        a2fClient.SendAudioData(resampledForA2F);
                    }
                    finally
                    {
                        resampledForA2F.Dispose();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TTS] Error in A2F processing: {e.Message}");
            }
        }
    }

    public void Stop()
    {
        speechCancellationTokenSource?.Cancel();
        speechQueue.Clear();
        if (streamAudioSource != null) streamAudioSource.ClearBuffer();
        isSpeaking = false;
        currentStatus = ITTSSpeaker.AudioStatus.Completed;
    }
}