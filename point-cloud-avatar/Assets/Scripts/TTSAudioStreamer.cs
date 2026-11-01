using UnityEngine;
using Unity.WebRTC;
using App.Audio;

/// <summary>
/// Streams TTS audio via WebRTC using the SetData API.
/// Feeds audio directly to the AudioStreamTrack without local playback.
/// User hears TTS from the original SyncAudioSource.
/// </summary>
public class TTSAudioStreamer : MonoBehaviour
{
    private AudioStreamTrack _audioTrack;
    public AudioStreamTrack AudioTrack => _audioTrack;
    
    private SyncAudioSource _targetSyncSource;
    private int _sampleRate;

    private void Start()
    {
        _sampleRate = AudioSettings.outputSampleRate;
        _audioTrack = new AudioStreamTrack();
        FindAndConnectSyncSource();
    }
    
    public void SetSyncSource(SyncAudioSource syncSource)
    {
        if (_targetSyncSource != null)
        {
            _targetSyncSource.OnAudioGenerated -= HandleAudioGenerated;
        }

        _targetSyncSource = syncSource;
        if (_targetSyncSource != null)
        {
            _targetSyncSource.OnAudioGenerated += HandleAudioGenerated;
        }
    }

    private void FindAndConnectSyncSource()
    {
        if (_targetSyncSource != null) return;
        
        var tts = FindFirstObjectByType<ElevenLabsTTS>(); 
        if (tts != null)
        {
            var sync = tts.GetComponent<SyncAudioSource>();
            if (sync != null) SetSyncSource(sync);
        }
        else
        {
            var sync = FindFirstObjectByType<SyncAudioSource>();
            if (sync != null) SetSyncSource(sync);
        }
    }

    private void OnDestroy()
    {
        if (_targetSyncSource != null)
        {
            _targetSyncSource.OnAudioGenerated -= HandleAudioGenerated;
        }
        
        if (_audioTrack != null)
        {
            _audioTrack.Dispose();
            _audioTrack = null;
        }
    }
    
    private void HandleAudioGenerated(float[] data, int channels)
    {
        if (_audioTrack == null) return;
        _audioTrack.SetData(data, channels, _sampleRate);
    }
}
