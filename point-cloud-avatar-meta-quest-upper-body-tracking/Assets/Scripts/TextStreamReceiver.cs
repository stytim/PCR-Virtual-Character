using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using TMPro;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Events;

namespace HandTrackingStreaming
{
    /// <summary>
    /// WebRTC Text Stream Receiver for Meta Quest project.
    /// Receives STT transcription and LLM response text from PCR Avatar.
    /// Uses UDP-based signaling for automatic peer discovery.
    /// </summary>
    public class TextStreamReceiver : MonoBehaviour
    {
        [Header("Network Settings")]
        [Tooltip("UDP port for signaling. Must match sender.")]
        public int signalingPort = 9000;

        [Tooltip("Optional: Direct IP of sender. Leave empty for broadcast discovery.")]
        public string senderIP = "";

        [Tooltip("Seconds between discovery broadcasts.")]
        public float discoveryInterval = 2.0f;

        [Tooltip("Seconds before considering connection lost.")]
        public float heartbeatTimeout = 5.0f;

        [Header("UI Display")]
        [Tooltip("UI element to display STT transcription.")]
        public TMP_Text sttDisplayText;

        [Tooltip("UI element to display LLM response.")]
        public TMP_Text llmDisplayText;

        [Header("Status")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private string connectionStatus = "Discovering...";

        [Header("Events")]
        public UnityEvent OnConnected;
        public UnityEvent OnDisconnected;

        [Serializable]
        public class StringEvent : UnityEvent<string> { }
        public StringEvent OnSTTReceived;
        public StringEvent OnLLMReceived;

        // WebRTC
        private RTCPeerConnection peerConnection;
        private RTCDataChannel remoteDataChannel;
        private bool webrtcInitialized = false;

        // UDP Signaling
        private UdpClient udpClient;
        private Thread receiveThread;
        private bool isRunning = false;
        private IPEndPoint senderEndPoint;

        // Signaling state
        private Queue<SignalingMessage> pendingMessages = new Queue<SignalingMessage>();
        private object messageLock = new object();
        private float lastHeartbeatReceived;
        private bool waitingForOffer = false;

        // Message types for signaling
        [Serializable]
        private class SignalingMessage
        {
            public string type;
            public string sdp;
            public string candidate;
            public string sdpMid;
            public int sdpMLineIndex;
            public string senderIP;
            public int senderPort; // Added to track actual port
        }

        // Message types for data channel
        [Serializable]
        private class TextMessage
        {
            public string type;
            public string text;
            public long timestamp;
        }

        void Awake()
        {
            if (OnConnected == null) OnConnected = new UnityEvent();
            if (OnDisconnected == null) OnDisconnected = new UnityEvent();
            if (OnSTTReceived == null) OnSTTReceived = new StringEvent();
            if (OnLLMReceived == null) OnLLMReceived = new StringEvent();
        }

        void Start()
        {
            InitializeWebRTC();
            StartDebugStats();
        }

        void InitializeWebRTC()
        {
            // Start WebRTC update loop (do not yield return, as it loops indefinitely)
            StartCoroutine(WebRTC.Update());
            webrtcInitialized = true;

            StartSignaling();
            StartCoroutine(DiscoveryLoop());
            StartCoroutine(ConnectionMonitor());

            Debug.Log("[TextStreamReceiver] Initialized and looking for sender.");
        }

        void Update()
        {
            // Process pending signaling messages on main thread
            lock (messageLock)
            {
                while (pendingMessages.Count > 0)
                {
                    var msg = pendingMessages.Dequeue();
                    ProcessSignalingMessage(msg);
                }
            }
        }

        void OnDestroy()
        {
            Cleanup();
        }

        void OnApplicationQuit()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            isRunning = false;

            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(500);
            }

            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }

            if (remoteDataChannel != null)
            {
                remoteDataChannel.Close();
                remoteDataChannel = null;
            }

            if (peerConnection != null)
            {
                peerConnection.Close();
                peerConnection.Dispose();
                peerConnection = null;
            }
        }

        #region Signaling

        private void StartSignaling()
        {
            try
            {
                udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, signalingPort + 1)); // Use different port for receiving

                // Fix for Windows UDP socket exception on close
                try { udpClient.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { }

                isRunning = true;
                receiveThread = new Thread(ReceiveSignalingLoop);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                Debug.Log($"[TextStreamReceiver] Signaling started, will broadcast to port {signalingPort}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TextStreamReceiver] Failed to start signaling: {e.Message}");
            }
        }

        private void ReceiveSignalingLoop()
        {
            IPEndPoint anyEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                try
                {
                    if (udpClient.Available > 0)
                    {
                        byte[] data = udpClient.Receive(ref anyEndPoint);
                        string json = Encoding.UTF8.GetString(data);

                        var msg = JsonConvert.DeserializeObject<SignalingMessage>(json);
                        if (msg != null)
                        {
                            if (string.IsNullOrEmpty(msg.senderIP))
                            {
                                msg.senderIP = anyEndPoint.Address.ToString();
                            }
                            // Always use the actual port from the received packet
                            msg.senderPort = anyEndPoint.Port;

                            lock (messageLock)
                            {
                                pendingMessages.Enqueue(msg);
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
                catch (SocketException) { }
                catch (Exception e)
                {
                    if (isRunning)
                        Debug.LogError($"[TextStreamReceiver] Signaling receive error: {e.Message}");
                }
            }
        }

        private IEnumerator DiscoveryLoop()
        {
            while (true)
            {
                if (!isConnected && !waitingForOffer)
                {
                    SendDiscovery();
                }

                yield return new WaitForSeconds(discoveryInterval);
            }
        }

        private void SendDiscovery()
        {
            try
            {
                var msg = new SignalingMessage
                {
                    type = "DISCOVER",
                    senderIP = GetLocalIPAddress()
                };

                string json = JsonConvert.SerializeObject(msg);
                byte[] data = Encoding.UTF8.GetBytes(json);

                // Send to specific IP or broadcast + localhost
                if (!string.IsNullOrEmpty(senderIP))
                {
                    IPEndPoint targetEndPoint = new IPEndPoint(IPAddress.Parse(senderIP), signalingPort);
                    udpClient.Send(data, data.Length, targetEndPoint);
                    Debug.Log($"[TextStreamReceiver] Sent DISCOVER to {targetEndPoint}");
                }
                else
                {
                    // Broadcast
                    IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, signalingPort);
                    udpClient.Send(data, data.Length, broadcastEndPoint);
                    
                    // Also try localhost explicitly for local testing
                    IPEndPoint localhostEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), signalingPort);
                    udpClient.Send(data, data.Length, localhostEndPoint);
                    
                    Debug.Log($"[TextStreamReceiver] Sent DISCOVER to Broadcast and Localhost");
                }
                
                connectionStatus = "Discovering...";
            }
            catch (Exception e)
            {
                Debug.LogError($"[TextStreamReceiver] Failed to send discovery: {e.Message}");
            }
        }

        private void ProcessSignalingMessage(SignalingMessage msg)
        {
            // Debug.Log($"[TextStreamReceiver] Received signaling: {msg.type} from {msg.senderIP}");

            switch (msg.type)
            {
                case "OFFER":
                    HandleOffer(msg);
                    break;

                case "ICE":
                    HandleRemoteIceCandidate(msg);
                    break;

                case "HEARTBEAT":
                    lastHeartbeatReceived = Time.time;
                    break;
            }
        }

        private void HandleOffer(SignalingMessage msg)
        {
            // Use the actual port the sender is listening on
            senderEndPoint = new IPEndPoint(IPAddress.Parse(msg.senderIP), msg.senderPort);
            waitingForOffer = false;

            Debug.Log($"[TextStreamReceiver] Received offer from {msg.senderIP}:{msg.senderPort}. Creating answer...");

            StartCoroutine(CreateAndSendAnswer(msg.sdp));
        }

    [Header("Audio Output")]
    public AudioSource voiceAudioSource;
    public AudioSource ttsAudioSource;
    private int _receivedTrackCount = 0;

    // ...

        private IEnumerator CreateAndSendAnswer(string offerSdp)
        {
            // Reset track count for new connection
            _receivedTrackCount = 0;

            // Create peer connection
            var config = GetRTCConfiguration();
            peerConnection = new RTCPeerConnection(ref config);

            // Handle incoming tracks
            peerConnection.OnTrack = e =>
            {
                if (e.Track.Kind == TrackKind.Audio)
                {
                    var audioTrack = e.Track as AudioStreamTrack;
                    AudioSource targetSource = null;
                    string label = "Unknown";

                    // Simple ordering assumption: 1st = Mic, 2nd = TTS
                    if (_receivedTrackCount == 0) 
                    {
                        targetSource = voiceAudioSource;
                        label = "Voice (Mic)";
                    }
                    else if (_receivedTrackCount == 1) 
                    {
                        targetSource = ttsAudioSource;
                         label = "TTS";
                    }

                    // Fallback creation
                    if (targetSource == null)
                    {
                        var go = new GameObject($"WebRTCAudio_{_receivedTrackCount}");
                        go.transform.SetParent(transform);
                        targetSource = go.AddComponent<AudioSource>();
                        
                        // Critical Audio Settings for robustness
                        targetSource.loop = true;
                        targetSource.volume = 1.0f;
                        targetSource.spatialBlend = 0f; // 2D Sound (hear everywhere)
                        targetSource.mute = false;
                        targetSource.bypassEffects = false; // Critical for OnAudioFilterRead / Debuggers
                        targetSource.bypassListenerEffects = true;
                        targetSource.bypassReverbZones = true;
                        
                        if (_receivedTrackCount == 0) voiceAudioSource = targetSource;
                        else if (_receivedTrackCount == 1) ttsAudioSource = targetSource;
                    }

                    targetSource.SetTrack(audioTrack);
                    targetSource.loop = true;
                    targetSource.Play();
                    
                    // Attach Debug Volume Monitor & Recorder AFTER Play/SetTrack to ensure filter order
                    var debugger = targetSource.gameObject.GetComponent<AudioStreamDebugger>();
                    if (debugger == null) debugger = targetSource.gameObject.AddComponent<AudioStreamDebugger>();
                    debugger.sourceName = label;

                    Debug.Log($"[TextStreamReceiver] Received Audio Track #{_receivedTrackCount} ({label}). Playing on {targetSource.name}");
                    
                    _receivedTrackCount++;
                }
            };

            peerConnection.OnIceCandidate = candidate =>
            {
                SendIceCandidate(candidate);
            };

            peerConnection.OnIceConnectionChange = state =>
            {
                Debug.Log($"[TextStreamReceiver] ICE state: {state}");

                if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
                {
                    connectionStatus = "Connected";
                    if (!isConnected)
                    {
                        isConnected = true;
                        OnConnected?.Invoke();
                    }
                }
                else if (state == RTCIceConnectionState.Disconnected || state == RTCIceConnectionState.Failed)
                {
                    HandleDisconnection();
                }
            };

            peerConnection.OnDataChannel = channel =>
            {
                Debug.Log($"[TextStreamReceiver] DataChannel received: {channel.Label}");
                remoteDataChannel = channel;

                remoteDataChannel.OnMessage = bytes =>
                {
                    string json = Encoding.UTF8.GetString(bytes);
                    HandleDataChannelMessage(json);
                };

                remoteDataChannel.OnOpen = () =>
                {
                    Debug.Log("[TextStreamReceiver] DataChannel opened!");
                    connectionStatus = "DataChannel Open";
                };

                remoteDataChannel.OnClose = () =>
                {
                    Debug.Log("[TextStreamReceiver] DataChannel closed.");
                    HandleDisconnection();
                };
            };

            // Set remote description (offer)
            var offerDesc = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = offerSdp };
            var setRemoteOp = peerConnection.SetRemoteDescription(ref offerDesc);
            yield return setRemoteOp;

            if (setRemoteOp.IsError)
            {
                Debug.LogError($"[TextStreamReceiver] SetRemoteDescription error: {setRemoteOp.Error.message}");
                yield break;
            }

            // Create answer
            var answerOp = peerConnection.CreateAnswer();
            yield return answerOp;

            if (answerOp.IsError)
            {
                Debug.LogError($"[TextStreamReceiver] CreateAnswer error: {answerOp.Error.message}");
                yield break;
            }

            var answerDesc = answerOp.Desc;
            var setLocalOp = peerConnection.SetLocalDescription(ref answerDesc);
            yield return setLocalOp;

            if (setLocalOp.IsError)
            {
                Debug.LogError($"[TextStreamReceiver] SetLocalDescription error: {setLocalOp.Error.message}");
                yield break;
            }

            // Send answer via UDP
            SendSignalingMessage(new SignalingMessage
            {
                type = "ANSWER",
                sdp = answerDesc.sdp,
                senderIP = GetLocalIPAddress()
            });

            Debug.Log("[TextStreamReceiver] Answer sent.");
            lastHeartbeatReceived = Time.time;
        }

    [ContextMenu("Test Local Audio (ref.wav)")]
    public void TestLocalAudio()
    {
        StartCoroutine(LoadAndPlayLocalTestAudio());
    }

    private IEnumerator LoadAndPlayLocalTestAudio()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "ref.wav");
        string url = "file://" + path;
        
        Debug.Log($"[TextStreamReceiver] Loading local test audio from: {url}");

        using (var uwr = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(uwr);
                
                // Create a temporary audio source to play it
                var go = new GameObject("AudioTest_RefWav");
                go.transform.SetParent(transform);
                var source = go.AddComponent<AudioSource>();
                
                // Use robust settings
                source.volume = 1.0f;
                source.spatialBlend = 0f; // 2D
                source.loop = true;
                source.clip = clip;
                source.Play();

                Debug.Log($"[TextStreamReceiver] Playing local test audio ({clip.length}s)...");
                
                // Destroy after 10 seconds or clip length (if looping, maybe just let it run for a bit)
                Destroy(go, Mathf.Max(10f, clip.length)); 
            }
            else
            {
                Debug.LogError($"[TextStreamReceiver] Failed to load local test audio: {uwr.error}");
            }
        }
    }

        private void HandleRemoteIceCandidate(SignalingMessage msg)
        {
            if (peerConnection == null) return;

            var candidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = msg.candidate,
                sdpMid = msg.sdpMid,
                sdpMLineIndex = msg.sdpMLineIndex
            });

            peerConnection.AddIceCandidate(candidate);
            Debug.Log($"[TextStreamReceiver] Added remote ICE candidate.");
        }

        private void SendIceCandidate(RTCIceCandidate candidate)
        {
            if (senderEndPoint == null) return;

            SendSignalingMessage(new SignalingMessage
            {
                type = "ICE",
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex ?? 0,
                senderIP = GetLocalIPAddress()
            });
        }

        private void SendSignalingMessage(SignalingMessage msg)
        {
            if (senderEndPoint == null) return;

            try
            {
                string json = JsonConvert.SerializeObject(msg);
                byte[] data = Encoding.UTF8.GetBytes(json);
                udpClient.Send(data, data.Length, senderEndPoint);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TextStreamReceiver] Failed to send signaling: {e.Message}");
            }
        }

        private void SendHeartbeat()
        {
            if (senderEndPoint == null) return;

            SendSignalingMessage(new SignalingMessage
            {
                type = "HEARTBEAT",
                senderIP = GetLocalIPAddress()
            });
        }

        #endregion

        #region Connection Management

        private IEnumerator ConnectionMonitor()
        {
            while (true)
            {
                yield return new WaitForSeconds(1.0f);

                if (isConnected)
                {
                    // Send heartbeat
                    SendHeartbeat();

                    // Check for timeout
                    if (Time.time - lastHeartbeatReceived > heartbeatTimeout)
                    {
                        Debug.LogWarning("[TextStreamReceiver] Heartbeat timeout. Reconnecting...");
                        HandleDisconnection();
                    }
                }
            }
        }

        private void HandleDisconnection()
        {
            if (!isConnected) return;

            connectionStatus = "Disconnected";
            isConnected = false;

            OnDisconnected?.Invoke();

            // Cleanup WebRTC
            if (remoteDataChannel != null)
            {
                remoteDataChannel.Close();
                remoteDataChannel = null;
            }

            if (peerConnection != null)
            {
                peerConnection.Close();
                peerConnection.Dispose();
                peerConnection = null;
            }

            Debug.Log("[TextStreamReceiver] Disconnected. Restarting discovery...");
        }

        private RTCConfiguration GetRTCConfiguration()
        {
            RTCConfiguration config = default;
            config.iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
            };
            return config;
        }

        private string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        #endregion

        #region Data Channel Handling

        private void HandleDataChannelMessage(string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<TextMessage>(json);
                if (msg == null) return;

                Debug.Log($"[TextStreamReceiver] Received {msg.type}: {msg.text.Substring(0, Math.Min(50, msg.text.Length))}...");

                switch (msg.type)
                {
                    case "stt":
                        if (sttDisplayText != null)
                            sttDisplayText.text = msg.text;
                        OnSTTReceived?.Invoke(msg.text);
                        break;

                    case "llm":
                        if (llmDisplayText != null)
                            llmDisplayText.text = msg.text;
                        OnLLMReceived?.Invoke(msg.text);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[TextStreamReceiver] Failed to parse message: {e.Message}");
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Check if the receiver is connected to a sender.
        /// </summary>
        public bool IsConnected => isConnected;

        /// <summary>
        /// Get the current connection status string.
        /// </summary>
        public string ConnectionStatus => connectionStatus;

        #endregion

        #region Debug Methods

        [ContextMenu("Show Connection Status")]
        private void ShowConnectionStatus()
        {
            Debug.Log($"[TextStreamReceiver] Status: {connectionStatus}, Connected: {isConnected}, " +
                      $"DataChannel: {(remoteDataChannel != null ? remoteDataChannel.ReadyState.ToString() : "null")}");
        }

        [ContextMenu("Start Debug Stats")]
        public void StartDebugStats()
        {
            StartCoroutine(DebugStatsCoroutine());
        }

        private IEnumerator DebugStatsCoroutine()
        {
            Debug.Log("[TextStreamReceiver] Starting Debug Stats Loop...");
            while (isConnected && peerConnection != null)
            {
                var op = peerConnection.GetStats();
                yield return op;

                if (!op.IsError)
                {
                    var report = op.Value;
                    foreach (var stat in report.Stats.Values)
                    {
                        if (stat is RTCInboundRTPStreamStats inboundStats)
                        {
                            if (inboundStats.kind == "audio")
                            {
                                Debug.Log($"[WebRTC Stats] Audio Inbound: " +
                                          $"Bytes: {inboundStats.bytesReceived}, " +
                                          $"Packets: {inboundStats.packetsReceived}, " +
                                          $"Lost: {inboundStats.packetsLost}");
                            }
                        }
                    }
                }

                yield return new WaitForSeconds(2.0f);
            }
        }

        [ContextMenu("Force Discovery")]
        private void ForceDiscovery()
        {
            SendDiscovery();
        }

        #endregion
    }

    // Helper class to monitor audio levels and record WAV
    public class AudioStreamDebugger : MonoBehaviour
    {
        public string sourceName;
        private AudioSource _audioSource;
        private float _timer;
        
        // Recording
        private List<float> _recordingBuffer = new List<float>();
        private bool _isRecording = false;
        private int _channels = 2;
        private int _sampleRate = 48000;

        void Start()
        {
            _audioSource = GetComponent<AudioSource>();
            _sampleRate = AudioSettings.outputSampleRate;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;
            
            // 1. RMS Calculation (Thread-safe-ish for simple monitoring)
            float sum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i] * data[i];
            }
            float rms = Mathf.Sqrt(sum / data.Length);
            
            // Store RMS for Update loop to read (atomic float write is safe enough for debug UI)
            _lastRms = rms;

            // 2. Recording
            if (_isRecording)
            {
                lock (_recordingBuffer)
                {
                    _recordingBuffer.AddRange(data);
                }
            }
        }

        private float _lastRms;

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer > 1.0f)
            {
                _timer = 0;
                float db = 20 * Mathf.Log10(_lastRms + 1e-6f);
                
                string status = _isRecording ? "[RECORDING] " : "";
                
                if (_lastRms > 0.001f) // Silence threshold ~ -60dB
                {
                    Debug.Log($"[AudioDebugger] {status}{sourceName}: RMS={_lastRms:F4} ({db:F1} dB) - PLAYING");
                }
                else
                {
                   // Debug.Log($"[AudioDebugger] {status}{sourceName}: SILENT ({db:F1} dB)");
                }
            }
        }

        [ContextMenu("Start Recording")]
        public void StartRecording()
        {
            lock (_recordingBuffer)
            {
                _recordingBuffer.Clear();
            }
            _isRecording = true;
            Debug.Log($"[AudioDebugger] Started recording {sourceName}...");
        }

        [ContextMenu("Stop and Save WAV")]
        public void StopAndSaveWav()
        {
            _isRecording = false;
            StartCoroutine(SaveWavCoroutine());
        }

        private System.Collections.IEnumerator SaveWavCoroutine()
        {
            yield return null; // Wait a frame to ensure thread catch-up

            string filename = $"{sourceName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
            
            Debug.Log($"[AudioDebugger] Saving {path}...");

            float[] samples;
            lock (_recordingBuffer)
            {
                samples = _recordingBuffer.ToArray();
            }

            if (samples.Length == 0)
            {
                Debug.LogWarning("[AudioDebugger] No samples recorded!");
                yield break;
            }

            if (WriteWavFile(path, samples, _channels, _sampleRate))
            {
                Debug.Log($"[AudioDebugger] Saved {samples.Length} samples to {path}");
            }
            else
            {
                Debug.LogError("[AudioDebugger] Failed to save WAV file.");
            }
        }

        private bool WriteWavFile(string filepath, float[] samples, int channels, int sampleRate)
        {
            try
            {
                using (var fileStream = new System.IO.FileStream(filepath, System.IO.FileMode.Create))
                using (var writer = new System.IO.BinaryWriter(fileStream))
                {
                    int samplesCount = samples.Length;
                    int bytesPerSample = 2; // 16-bit PCM
                    int byteRate = sampleRate * channels * bytesPerSample;
                    int blockAlign = channels * bytesPerSample;

                    // RIFF
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
                    writer.Write(36 + samplesCount * bytesPerSample);
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));

                    // fmt
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
                    writer.Write(16); // Subchunk1Size
                    writer.Write((short)1); // AudioFormat (1=PCM)
                    writer.Write((short)channels);
                    writer.Write(sampleRate);
                    writer.Write(byteRate);
                    writer.Write((short)blockAlign);
                    writer.Write((short)16); // BitsPerSample

                    // data
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
                    writer.Write(samplesCount * bytesPerSample);

                    // Write data (convert float to short)
                    float rescale = 32767.0f;
                    for (int i = 0; i < samplesCount; i++)
                    {
                        float val = samples[i] * rescale;
                        short sample = (short)Mathf.Clamp(val, -32768f, 32767f);
                        writer.Write(sample);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error writing WAV: {e.Message}");
                return false;
            }
        }
    }
}

