using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TTSHandler : MonoBehaviour
{
    public enum TTSOption
    {
        KokoroTTS,
        ElevenlabsTTS,
    }

    [Header("Select Local TTS")]
    public TTSOption selectedLocalTTS = TTSOption.KokoroTTS;

    [Header("TTS Speakers")]
    public AudioSource TTSAudioSource;
    // public KokoroTTSSpeaker kokoroTTSSpeaker;
    public ElevenLabsTTS elevenlabsTTSSpeaker;
    // [Header("Salsa Instance")]
    // public Salsa salsaInstance;

    private ITTSSpeaker TSSpeaker;


    [Header("Head")]
    public Transform head;

    private void OnEnable()
    {
        SceneSessionManager.Instance.OnSceneChanged.AddListener(SetTTSHandler);
    }

    private void OnDisable()
    {
        SceneSessionManager.Instance.OnSceneChanged.RemoveListener(SetTTSHandler);
    }

    private void SetTTSHandler()
    {
        head = SceneSessionManager.Instance.currentHead;
    }

    // Start is called before the first frame update
    void Start()
    {
        TSSpeaker = GetSelectedSpeaker();
        TestTTSConnection();
    }

    private void TestTTSConnection()
    {
        //TSSpeaker.TestConnection();
    }

    private ITTSSpeaker GetSelectedSpeaker()
    {
        switch (selectedLocalTTS)
        {
            // case TTSOption.KokoroTTS:
            //     salsaInstance.useExternalAnalysis = false;
            //     return kokoroTTSSpeaker;
            case TTSOption.ElevenlabsTTS:
                // salsaInstance.useExternalAnalysis = true;
                return elevenlabsTTSSpeaker;
            default:
                return elevenlabsTTSSpeaker;
        }
    }

    // Update is called once per frame
    void Update()
    {
        TTSAudioSource.transform.position = head.position;
    }

    // COPY TO MAIN
    public void Speak(string input, string language = "English", Action onComplete = null, Action onAudioStarted = null)
    {
        TSSpeaker.Speak(input, language, onComplete, onAudioStarted);
    }

    public bool waitingForAudioSynthesize()
    {
        return TSSpeaker.GetStatus() == ITTSSpeaker.AudioStatus.Playing;
    }

    // COPY TO MAIN
    public void ReportError()
    {
        TSSpeaker.Speak("I'm sorry, could you repeat that?", "English", null);
    }

    public void PlayDelayedAudio()
    {
        //TSSpeaker.SpeakDelayed();
    }

    public void CancelSpeak()
    {
        TSSpeaker.Stop();
        // sttHandler.NotifyTTSStopped();
    }
}