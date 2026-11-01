using System;

public interface ITTSSpeaker
{
    public enum AudioStatus
    {
        Idle,
        Downloading,
        Playing,
        Completed,
        Error
    }
    void Speak(string text, string language = "English", Action onComplete = null, Action onAudioStarted = null); // COPY TO MAIN
    // void SpeakDelayed();
    void Stop();
    // void TestConnection();
    AudioStatus GetStatus();
}
