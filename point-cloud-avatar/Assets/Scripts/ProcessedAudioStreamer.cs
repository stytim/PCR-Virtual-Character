using UnityEngine;
using Unity.WebRTC;
using System;

/// <summary>
/// Streams AEC-processed microphone audio via WebRTC using the SetData API.
/// Feeds audio directly to the AudioStreamTrack without local playback.
/// </summary>
public class ProcessedAudioStreamer : MonoBehaviour
{
    private const int SAMPLE_RATE = 16000;
    private const int CHANNELS = 1;
    
    private AudioStreamTrack _audioTrack;
    public AudioStreamTrack AudioTrack => _audioTrack;
    
    private void Start()
    {
        _audioTrack = new AudioStreamTrack();
        
        var processor = WebRTCAudioProcessor.Instance;
        if (processor != null)
        {
            processor.OnFrameProcessed += HandleProcessedFrame;
        }
        else
        {
            StartCoroutine(WaitForProcessor());
        }
    }
    
    private System.Collections.IEnumerator WaitForProcessor()
    {
        while (WebRTCAudioProcessor.Instance == null)
        {
            yield return new WaitForSeconds(0.5f);
        }
        WebRTCAudioProcessor.Instance.OnFrameProcessed += HandleProcessedFrame;
    }

    private void OnDestroy()
    {
        if (WebRTCAudioProcessor.Instance != null)
        {
            WebRTCAudioProcessor.Instance.OnFrameProcessed -= HandleProcessedFrame;
        }
        
        if (_audioTrack != null)
        {
            _audioTrack.Dispose();
            _audioTrack = null;
        }
    }

    private void HandleProcessedFrame(short[] data)
    {
        if (_audioTrack == null) return;
        
        float[] floatData = new float[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            floatData[i] = data[i] / 32768f;
        }
        
        _audioTrack.SetData(floatData, CHANNELS, SAMPLE_RATE);
    }
}
