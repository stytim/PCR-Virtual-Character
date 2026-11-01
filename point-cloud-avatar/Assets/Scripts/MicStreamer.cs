using UnityEngine;
using System;

public class MicStreamer : MonoBehaviour
{
    [Header("References")]
    public A2FClient client;

    [Header("Settings")]
    [Range(16000, 48000)]
    public int sampleRate = 16000; 
    
    [Range(1f, 10f)]
    public float gain = 1.0f;

    // Internal State
    private AudioClip _micClip;
    private string _device;
    private int _lastPosition = 0;
    private bool _isInitialized = false;

    void Start()
    {
        if (client == null)
        {
            Debug.LogError("MicStreamer: No A2FClient assigned!");
            return;
        }

        // 1. Start Microphone
        // We use a shorter buffer (1s) to reduce memory, looping handles the rest.
        if (Microphone.devices.Length > 0)
        {
            _device = Microphone.devices[0];
            _micClip = Microphone.Start(_device, true, 1, sampleRate); 
            _isInitialized = true;
            Debug.Log($"MicStreamer: Recording on '{_device}' @ {sampleRate}Hz");
        }
        else
        {
            Debug.LogError("MicStreamer: No Microphone found!");
        }
    }

    void Update()
    {
        if (!_isInitialized || client == null) return;

        // 2. Where is the driver writing now?
        int currentPosition = Microphone.GetPosition(_device);
        
        // Safety check: GetPosition can return -1 or stay static if the mic disconnects
        if (currentPosition < 0 || currentPosition == _lastPosition) return;

        int samplesToRead = 0;

        if (currentPosition > _lastPosition)
        {
            // Simple case: No wrap around
            samplesToRead = currentPosition - _lastPosition;
        }
        else
        {
            // Wrap around: (End of Buffer - Last Pos) + (Current Pos)
            samplesToRead = (_micClip.samples - _lastPosition) + currentPosition;
        }

        // If nothing new, stop
        if (samplesToRead <= 0) return;

        // 3. Create a buffer for ALL the new float data
        float[] fullFloatBuffer = new float[samplesToRead];

        if (currentPosition > _lastPosition)
        {
            // --- Standard Read ---
            // Verify bounds to prevent FMOD crash
            if (_lastPosition + samplesToRead <= _micClip.samples)
            {
                _micClip.GetData(fullFloatBuffer, _lastPosition);
            }
        }
        else
        {
            // --- Wrapped Read ---
            // Part A: Read from _lastPosition to the End of the Clip
            int samplesToEnd = _micClip.samples - _lastPosition;
            if (samplesToEnd > 0)
            {
                float[] endPart = new float[samplesToEnd];
                _micClip.GetData(endPart, _lastPosition);
                // Copy to the start of our full buffer
                Array.Copy(endPart, 0, fullFloatBuffer, 0, samplesToEnd);
            }

            // Part B: Read from 0 to currentPosition
            if (currentPosition > 0)
            {
                float[] startPart = new float[currentPosition];
                _micClip.GetData(startPart, 0);
                // Copy to the rest of our full buffer
                Array.Copy(startPart, 0, fullFloatBuffer, samplesToEnd, currentPosition);
            }
        }

        // 4. Convert to PCM and Send
        ProcessAndSend(fullFloatBuffer);

        // 5. Update Head
        _lastPosition = currentPosition;
    }

    private void ProcessAndSend(float[] floatData)
    {
        byte[] pcmBytes = new byte[floatData.Length * 2];

        for (int i = 0; i < floatData.Length; i++)
        {
            // Apply gain
            float val = floatData[i] * gain;
            
            // Hard Clamp to prevent overflow noise
            if (val > 1.0f) val = 1.0f;
            if (val < -1.0f) val = -1.0f;

            // Convert to Int16
            short shortVal = (short)(val * 32767f);

            // Little Endian Byte layout
            pcmBytes[i * 2] = (byte)(shortVal & 0xff);
            pcmBytes[i * 2 + 1] = (byte)((shortVal >> 8) & 0xff);
        }

        client.SendAudioData(pcmBytes);
    }
}