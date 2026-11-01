using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf;
using Cysharp.Net.Http;

// --- RESOLVING CONFLICTS WITH TYPE ALIASES ---
using AceClientStream = NvidiaAce.Controller.V1.AudioStream;
using AceServerStream = NvidiaAce.Controller.V1.AnimationDataStream;
using AceStreamHeader = NvidiaAce.Controller.V1.AudioStreamHeader;
using AceEndOfAudio = NvidiaAce.Controller.V1.AudioStream.Types.EndOfAudio;

// Include other necessary namespaces
using NvidiaAce.Services.A2FController.V1;
using NvidiaAce.Audio.V1;
using NvidiaAce.A2F.V1;
using NvidiaAce.AnimationData.V1;

public class A2FClient : MonoBehaviour
{
    [Header("Cloud Connection Settings")]
    [Tooltip("The API Key from NVIDIA NGC (start with 'nvapi-')")]
    public string apiKey = ""; // set your NVIDIA NGC key in the Inspector; never commit it

    [Tooltip("The Function ID for the Mark Model or similar")]
    public string functionId = "8efc55f5-6f00-424e-afe9-26212cd2c630";

    [Header("Legacy/Local Settings (Ignored if using Cloud)")]
    public bool useCloud = true;
    public string localHost = "10.0.130.30";
    public int localPort = 52000;

    public Action<float, Dictionary<string, float>> OnAnimationReceived;
    public Action<string> OnStatusMessage;

    private GrpcChannel _channel;
    private A2FControllerService.A2FControllerServiceClient _client;

    private AsyncDuplexStreamingCall<AceClientStream, AceServerStream> _stream;
    private CancellationTokenSource _cancellationTokenSource;
    private List<string> _blendshapeNames = new List<string>();

    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    private int _sendCount = 0; // Diagnostic: track total sends
    private long _totalBytesSent = 0;

    // --- AUTO-RECONNECT ---
    [Header("Auto-Reconnect")]
    [Tooltip("Seconds without receiving frames before triggering a reconnect (while audio is being sent).")]
    public float droughtTimeoutSeconds = 5f;
    
    private float _lastFrameReceivedTime = 0f;
    private float _lastAudioSentTime = 0f;
    private float _droughtReconnectCooldown = 0f; // Prevents rapid reconnection
    private bool _hasEverReceivedFrame = false;

    private void Start()
    {
        InitializeClient();
    }

    private void Update()
    {
        // Auto-reconnect: if we're sending audio but not receiving frames
        if (_droughtReconnectCooldown > 0f)
        {
            _droughtReconnectCooldown -= Time.deltaTime;
            return;
        }

        if (!_hasEverReceivedFrame) return; // Don't reconnect before first frame ever arrives
        
        float timeSinceLastFrame = Time.time - _lastFrameReceivedTime;
        float timeSinceLastSend = Time.time - _lastAudioSentTime;
        
        // Condition: we've sent audio recently (within 3s) but haven't received frames for droughtTimeoutSeconds
        if (timeSinceLastSend < 3f && timeSinceLastFrame > droughtTimeoutSeconds)
        {
            Debug.LogWarning($"\ud83d\udd04 [A2F] Auto-reconnecting! No frames for {timeSinceLastFrame:F1}s while audio was sent {timeSinceLastSend:F1}s ago.");
            _droughtReconnectCooldown = 10f; // Cooldown prevents rapid-fire reconnections
            Reconnect();
        }
    }

    /// <summary>
    /// Tears down the existing gRPC stream and creates a new one.
    /// Also resets the animator session so sync offsets are recalculated.
    /// </summary>
    public async void Reconnect()
    {
        Debug.Log("\ud83d\udd04 [A2F] Reconnecting gRPC stream...");
        
        // 1. Cancel old receive loop
        _cancellationTokenSource?.Cancel();
        
        // 2. Try to cleanly close old stream
        if (_stream != null)
        {
            try
            {
                await _stream.RequestStream.CompleteAsync();
            }
            catch { /* ignore cleanup errors */ }
            _stream.Dispose();
            _stream = null;
        }
        
        // 3. Shutdown old channel
        if (_channel != null)
        {
            try { await _channel.ShutdownAsync(); } catch { }
            _channel = null;
        }
        
        // 4. Reset counters
        _sendCount = 0;
        _totalBytesSent = 0;
        _hasEverReceivedFrame = false;
        
        // 5. Notify the animator to reset (sync offsets will be stale)
        var animator = GetComponent<MultiMeshAvatarAnimator>();
        if (animator != null) animator.ResetSession();
        
        // 6. Create fresh connection
        InitializeClient();
    }

    private async void OnDestroy()
    {
        await DisconnectAsync();
        _writeLock.Dispose();
    }

    /// <summary>
    /// Signals A2F that all audio for the current sentence has been sent.
    /// The service will finish processing and return remaining animation frames.
    /// </summary>
    public async Task SendEndOfAudio()
    {
        if (_stream == null) return;
        
        await _writeLock.WaitAsync();
        try
        {
            var endRequest = new AceClientStream { EndOfAudio = new AceEndOfAudio() };
            await _stream.RequestStream.WriteAsync(endRequest);
            // Debug.Log("\ud83d\udfe2 [A2F] Sent EndOfAudio for current sentence.");
        }
        catch (Exception e)
        {
            Debug.LogError($"\ud83d\udd34 [A2F] Error sending EndOfAudio: {e.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Opens a fresh gRPC stream for the next sentence.
    /// Call this after the previous sentence's animation frames have been received.
    /// </summary>
    public async void StartNewStream()
    {
        // Debug.Log("\ud83d\udd04 [A2F] Starting new stream for next sentence...");
        
        // 1. Cancel old receive loop
        _cancellationTokenSource?.Cancel();
        
        // 2. Dispose old stream (it should already be done after EndOfAudio + server status)
        if (_stream != null)
        {
            try { await _stream.RequestStream.CompleteAsync(); } catch { }
            _stream.Dispose();
            _stream = null;
        }
        
        // 3. Shutdown old channel
        if (_channel != null)
        {
            try { await _channel.ShutdownAsync(); } catch { }
            _channel = null;
        }
        
        // 4. Reset counters
        _sendCount = 0;
        _totalBytesSent = 0;
        _hasEverReceivedFrame = false;
        
        // 5. Create fresh connection
        InitializeClient();
    }

    private void InitializeClient()
    {
        try
        {
            string targetAddress = useCloud ? "https://grpc.nvcf.nvidia.com:443" : $"http://{localHost}:{localPort}";
            Debug.Log($"Connecting to A2F Service at {targetAddress}...");

            // 1. Create the high-performance HTTP/2 Handler
            var httpHandler = new YetAnotherHttpHandler
            {
                Http2Only = true,
                // For public cloud (https), we generally DO NOT skip verification unless debugging proxy issues.
                // However, if you have issues on Quest, you can try setting this back to true.
                SkipCertificateVerification = false
            };

            // 2. Pass the handler into the gRPC Channel Options
            var options = new GrpcChannelOptions
            {
                HttpHandler = httpHandler,
                DisposeHttpClient = true
            };

            // 3. Create the Channel
            _channel = GrpcChannel.ForAddress(targetAddress, options);
            _client = new A2FControllerService.A2FControllerServiceClient(_channel);

            // 4. PREPARE HEADERS (Metadata)
            // This is the CRITICAL part for Cloud Connection
            Metadata headers = new Metadata();
            if (useCloud)
            {
                // Note: The key usually needs "Bearer " prefix if the documentation says so. 
                // Based on your snippet: "authorization: Bearer nvapi-..."
                headers.Add("authorization", "Bearer " + apiKey);
                headers.Add("function-id", functionId);
            }

            // 5. Start the Stream WITH Headers
            _cancellationTokenSource = new CancellationTokenSource();

            // We pass 'headers' as the first argument here
            _stream = _client.ProcessAudioStream(headers, cancellationToken: _cancellationTokenSource.Token);

            SendHeader();

            _ = Task.Run(ReceiveLoop);

            if (useCloud)
                Debug.Log("A2F Connected to Cloud.");
            else 
                Debug.Log("A2F Connected to Local Server.");
        }
        catch (Exception e)
        {
            Debug.LogError($"A2F Connection Error: {e.Message}");
            Debug.LogError($"Inner Exception: {e.InnerException?.Message}");
        }
    }

    private async void SendHeader()
    {
        // 16000 is standard for A2F cloud models
        int a2fSampleRate = 16000;

        var headerRequest = new AceClientStream
        {
            AudioStreamHeader = new AceStreamHeader
            {
                AudioHeader = new AudioHeader
                {
                    AudioFormat = AudioHeader.Types.AudioFormat.Pcm,
                    ChannelCount = 1,
                    BitsPerSample = 16,
                    SamplesPerSecond = (uint)a2fSampleRate
                },
                FaceParams = new FaceParameters()
            }
        };

        if (_stream != null)
        {
            await _writeLock.WaitAsync();
            try
            {
                await _stream.RequestStream.WriteAsync(headerRequest);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    // ... (Rest of your SendAudioData overloads remain exactly the same) ...

    // 1. NativeArray Overload
    public void SendAudioData(Unity.Collections.NativeArray<float> floatSamples)
    {
        if (_stream == null)
        {
            Debug.LogWarning("🔴 [A2F Send] _stream is NULL! Audio dropped (NativeArray).");
            return;
        }
        byte[] pcmData = new byte[floatSamples.Length * 2];
        int offset = 0;
        for (int i = 0; i < floatSamples.Length; i++)
        {
            float clamped = Mathf.Clamp(floatSamples[i], -1.0f, 1.0f);
            short pcmSample = (short)(clamped * short.MaxValue);
            pcmData[offset] = (byte)(pcmSample & 0xFF);
            pcmData[offset + 1] = (byte)((pcmSample >> 8) & 0xFF);
            offset += 2;
        }
        SendAudioData(pcmData);
    }

    // 2. Float Array Overload
    public void SendAudioData(float[] floatSamples)
    {
        if (_stream == null)
        {
            Debug.LogWarning("🔴 [A2F Send] _stream is NULL! Audio dropped (float[]).");
            return;
        }
        byte[] pcmData = new byte[floatSamples.Length * 2];
        int offset = 0;
        foreach (var sample in floatSamples)
        {
            float clamped = Mathf.Clamp(sample, -1.0f, 1.0f);
            short pcmSample = (short)(clamped * short.MaxValue);
            byte[] byteSample = BitConverter.GetBytes(pcmSample);
            pcmData[offset] = byteSample[0];
            pcmData[offset + 1] = byteSample[1];
            offset += 2;
        }
        SendAudioData(pcmData);
    }

    // 3. Chunking Logic
    public async void SendAudioData(byte[] pcmData)
    {
        if (_stream == null)
        {
            Debug.LogWarning("🔴 [A2F Send] _stream is NULL! Audio dropped (byte[]).");
            return;
        }
        const int CHUNK_SIZE = 4096;
        int offset = 0;
        try
        {
            await _writeLock.WaitAsync();
            try
            {
                while (offset < pcmData.Length)
                {
                    int count = Mathf.Min(CHUNK_SIZE, pcmData.Length - offset);
                    ByteString chunk = ByteString.CopyFrom(pcmData, offset, count);
                    var audioRequest = new AceClientStream
                    {
                        AudioWithEmotion = new AudioWithEmotion { AudioBuffer = chunk }
                    };
                    await _stream.RequestStream.WriteAsync(audioRequest);
                    offset += count;
                }
                _sendCount++;
                _totalBytesSent += pcmData.Length;
                _lastAudioSentTime = Time.time;
                // if (_sendCount % 10 == 0)
                // {
                //     Debug.Log($"📤 [A2F Send] #{_sendCount} | This={pcmData.Length}B | Total={_totalBytesSent/1024}KB");
                // }
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"🔴 [A2F RECONNECT] Send Error: {e.Message}. Reconnecting stream...");
            _cancellationTokenSource?.Cancel();
            InitializeClient();
        }
    }

    private async Task ReceiveLoop()
    {
        try
        {
            while (await _stream.ResponseStream.MoveNext(_cancellationTokenSource.Token))
            {
                AceServerStream response = _stream.ResponseStream.Current;
                switch (response.StreamPartCase)
                {
                    case AceServerStream.StreamPartOneofCase.AnimationDataStreamHeader:
                        var header = response.AnimationDataStreamHeader;
                        if (header.SkelAnimationHeader != null)
                        {
                            _blendshapeNames.Clear();
                            _blendshapeNames.AddRange(header.SkelAnimationHeader.BlendShapes);
                        }
                        break;

                    case AceServerStream.StreamPartOneofCase.AnimationData:
                        ProcessAnimationData(response.AnimationData);
                        break;

                    case AceServerStream.StreamPartOneofCase.Status:
                        MainThreadDispatcher.Enqueue(() =>
                            Debug.Log($"A2F Status: {response.Status.Message}"));
                        break;
                }
            }
            // Stream ended normally — server closed the connection
            MainThreadDispatcher.Enqueue(() =>
                Debug.LogError($"🔴 [A2F] Receive loop ended — server closed the gRPC stream! No more animation frames will arrive."));
        }
        catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled)
        {
            MainThreadDispatcher.Enqueue(() =>
                Debug.LogWarning($"🟡 [A2F] Receive loop cancelled (shutdown)."));
        }
        catch (RpcException rpcEx)
        {
            Debug.LogError($"🔴 [A2F] gRPC Error: {rpcEx.Status.StatusCode} - {rpcEx.Status.Detail}");
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException) { /* Warning suppressed */ }
            else Debug.LogError($"🔴 [A2F] Receive Loop Error: {e}");
        }
    }

    private void ProcessAnimationData(AnimationData data)
    {
        if (data.SkelAnimation == null || _blendshapeNames.Count == 0) return;
        if (data.SkelAnimation.BlendShapeWeights.Count > 0)
        {
            var blendShapePacket = data.SkelAnimation.BlendShapeWeights[0];
            float timeStamp = (float)blendShapePacket.TimeCode;
            var weights = blendShapePacket.Values;
            var shapeDict = new Dictionary<string, float>();

            for (int i = 0; i < weights.Count && i < _blendshapeNames.Count; i++)
            {
                shapeDict[_blendshapeNames[i]] = weights[i];
            }
            MainThreadDispatcher.Enqueue(() =>
            {
                _lastFrameReceivedTime = Time.time;
                _hasEverReceivedFrame = true;
                OnAnimationReceived?.Invoke(timeStamp, shapeDict);
            });
        }
    }

    private async Task DisconnectAsync()
    {
        if (_stream != null)
        {
            try
            {
                await _writeLock.WaitAsync();
                try
                {
                    var endRequest = new AceClientStream { EndOfAudio = new AceEndOfAudio() };
                    await _stream.RequestStream.WriteAsync(endRequest);
                    await _stream.RequestStream.CompleteAsync();
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch { }
        }
        _cancellationTokenSource?.Cancel();
        if (_channel != null) await _channel.ShutdownAsync();
    }
}