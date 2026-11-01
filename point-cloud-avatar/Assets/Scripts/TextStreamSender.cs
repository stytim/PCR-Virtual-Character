using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// WebRTC Text Stream Sender for PCR Avatar project.
/// Streams STT transcription and LLM response text to a remote spectator.
/// Uses UDP-based signaling for automatic peer discovery.
/// </summary>
public class TextStreamSender : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("UDP port for signaling. Must match receiver.")]
    public int signalingPort = 9000;
    
    [Tooltip("Optional: Direct IP of receiver. Leave empty for broadcast discovery.")]
    public string remoteIP = "";
    
    [Tooltip("Seconds between reconnection attempts.")]
    public float reconnectInterval = 3.0f;
    
    [Tooltip("Seconds before considering connection lost.")]
    public float heartbeatTimeout = 5.0f;

    [Header("Status")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private string connectionStatus = "Disconnected";

    [Header("Events")]
    public UnityEvent OnConnected;
    public UnityEvent OnDisconnected;

    // WebRTC
    private RTCPeerConnection peerConnection;
    private RTCDataChannel dataChannel;
    private bool webrtcInitialized = false;

    // UDP Signaling
    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;
    private IPEndPoint remoteEndPoint;

    // Signaling state
    private Queue<SignalingMessage> pendingMessages = new Queue<SignalingMessage>();
    private object messageLock = new object();
    private float lastHeartbeatReceived;
    private bool pendingOffer = false;

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

    public static TextStreamSender Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("[TextStreamSender] Multiple instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        if (OnConnected == null) OnConnected = new UnityEvent();
        if (OnDisconnected == null) OnDisconnected = new UnityEvent();
    }

    // Audio Streaming
    public ProcessedAudioStreamer micStreamer;
    public TTSAudioStreamer ttsStreamer; // CHANGED: Use Bridge
    public AudioSource ttsOriginalSource; // Renamed from ttsAudioSource for clarity
    private AudioStreamTrack micTrack;
    private AudioStreamTrack ttsTrack;

    void Start()
    {
        InitializeWebRTC();
        
        // Auto-find references if missing
        if (micStreamer == null) 
        {
            micStreamer = FindFirstObjectByType<ProcessedAudioStreamer>();
            if (micStreamer == null)
            {
                // Auto-create on a separate child object to avoid AudioSource conflicts
                var go = new GameObject("ProcessedAudioStreamer_Auto");
                go.transform.SetParent(transform);
                micStreamer = go.AddComponent<ProcessedAudioStreamer>();
                Debug.Log("[TextStreamSender] Auto-created ProcessedAudioStreamer on child object.");
            }
        }

        // Initialize TTS Original Source Reference
        if (ttsOriginalSource == null)
        {
            var tts = FindFirstObjectByType<ElevenLabsTTS>();
            if (tts != null) 
            {
                // Try to get SyncAudioSource component from the same object
                var syncSource = tts.GetComponent<App.Audio.SyncAudioSource>();
                if (syncSource != null) ttsOriginalSource = syncSource;
            }
        }
        
        // CRITICAL: Ensure bypassEffects is FALSE so that OnAudioFilterRead is called.
        // This is required for SyncAudioSource to process data
        if (ttsOriginalSource != null)
        {
            ttsOriginalSource.bypassEffects = false;
        }

        // Initialize TTS Streamer (Bridge)
        if (ttsStreamer == null)
        {
            ttsStreamer = FindFirstObjectByType<TTSAudioStreamer>();
            if (ttsStreamer == null)
            {
                var go = new GameObject("TTSAudioStreamer_Auto");
                go.transform.SetParent(transform);
                ttsStreamer = go.AddComponent<TTSAudioStreamer>();
                Debug.Log("[TextStreamSender] Auto-created TTSAudioStreamer.");
            }
        }
    }

    void InitializeWebRTC()
    {
        // Start WebRTC update loop (do not yield return, as it loops indefinitely)
        StartCoroutine(WebRTC.Update());
        webrtcInitialized = true;
        
        StartSignaling();
        StartCoroutine(ConnectionMonitor());
        
        Debug.Log("[TextStreamSender] Initialized and listening for receivers.");
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

        if (dataChannel != null)
        {
            dataChannel.Close();
            dataChannel = null;
        }

        // Cleanup Tracks
        if (micTrack != null) { micTrack.Dispose(); micTrack = null; }
        if (ttsTrack != null) { ttsTrack.Dispose(); ttsTrack = null; }

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
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, signalingPort));
            udpClient.EnableBroadcast = true;
            
            // Fix for Windows UDP socket exception on close
            try { udpClient.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { }

            isRunning = true;
            receiveThread = new Thread(ReceiveSignalingLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log($"[TextStreamSender] Signaling started on port {signalingPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TextStreamSender] Failed to start signaling: {e.Message}");
        }
    }

    private void ReceiveSignalingLoop()
    {
        Debug.Log($"[TextStreamSender] Receive thread started. Listneing for UDP packets on {signalingPort}...");
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        
        while (isRunning)
        {
            try
            {
                // Blocking receive
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                
                if (data != null && data.Length > 0)
                {
                    string json = Encoding.UTF8.GetString(data);
                    
                    // Verbose logging for debugging
                    // Debug.Log($"[TextStreamSender] Received {data.Length} bytes from {remoteEndPoint}: {json}");
                    
                    var msg = JsonConvert.DeserializeObject<SignalingMessage>(json);
                    if (msg != null)
                    {
                        // Store sender's endpoint for responses (use actual received endpoint)
                        if (string.IsNullOrEmpty(msg.senderIP))
                        {
                            msg.senderIP = remoteEndPoint.Address.ToString();
                        }
                        // Always use the actual port from the received packet
                        msg.senderPort = remoteEndPoint.Port;
                        
                        lock (messageLock)
                        {
                            pendingMessages.Enqueue(msg);
                        }
                    }
                }
            }
            catch (SocketException e) 
            { 
                 // Ignore interruption error which happens on close
                 if (e.SocketErrorCode != SocketError.Interrupted && isRunning)
                 {
                     Debug.LogError($"[TextStreamSender] Socket receive error: {e.Message}");
                 }
            }
            catch (Exception e)
            {
                if (isRunning)
                    Debug.LogError($"[TextStreamSender] Receive thread exception: {e.Message}");
            }
        }
        Debug.Log("[TextStreamSender] Receive thread stopped.");
    }

    private void ProcessSignalingMessage(SignalingMessage msg)
    {
        // Debug.Log($"[TextStreamSender] Received signaling: {msg.type} from {msg.senderIP}");

        switch (msg.type)
        {
            case "DISCOVER":
                HandleDiscover(msg.senderIP, msg.senderPort);
                break;
                
            case "ANSWER":
                HandleAnswer(msg);
                break;
                
            case "ICE":
                HandleRemoteIceCandidate(msg);
                break;
                
            case "HEARTBEAT":
                lastHeartbeatReceived = Time.time;
                break;
        }
    }

    private void HandleDiscover(string receiverIP, int receiverPort)
    {
        if (pendingOffer || isConnected)
        {
            Debug.Log("[TextStreamSender] Ignoring DISCOVER - already connected or pending.");
            return;
        }

        // Use the actual port the receiver is listening on
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(receiverIP), receiverPort);
        Debug.Log($"[TextStreamSender] Receiver discovered at {receiverIP}:{receiverPort}. Creating offer...");
        
        StartCoroutine(CreateAndSendOffer());
    }

    private IEnumerator CreateAndSendOffer()
    {
        pendingOffer = true;

        // Create peer connection
        var config = GetRTCConfiguration();
        peerConnection = new RTCPeerConnection(ref config);
        
        // Add Audio Tracks
        if (manualTestTrack != null)
        {
            peerConnection.AddTrack(manualTestTrack);
            Debug.Log("[TextStreamSender] Added MANUAL TEST Audio Track.");
        }
        else
        {
            // Normal Operation
            if (micStreamer != null)
            {
                // NEW: Use the AudioTrack created by ProcessedAudioStreamer with SetData API
                // This track receives audio via SetData and does NOT play locally
                var track = micStreamer.AudioTrack;
                if (track != null)
                {
                    micTrack = track;
                    peerConnection.AddTrack(micTrack);
                    Debug.Log("[TextStreamSender] Added Mic Track (SetData API, no local echo).");
                }
                else
                {
                    Debug.LogWarning("[TextStreamSender] ProcessedAudioStreamer.AudioTrack is null!");
                }
            }

            if (ttsStreamer != null)
            {
                // NEW: Use the AudioTrack created by TTSAudioStreamer with SetData API
                // This track receives audio via SetData and does NOT play locally
                var track = ttsStreamer.AudioTrack;
                if (track != null)
                {
                    ttsTrack = track;
                    peerConnection.AddTrack(ttsTrack);
                    Debug.Log("[TextStreamSender] Added TTS Track (SetData API, no double playback).");
                }
                else
                {
                    Debug.LogWarning("[TextStreamSender] TTSAudioStreamer.AudioTrack is null!");
                }
            }
        }

        peerConnection.OnIceCandidate = candidate =>
        {
            SendIceCandidate(candidate);
        };


        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log($"[TextStreamSender] ICE state: {state}");
            
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

        // Create data channel
        var dcConfig = new RTCDataChannelInit();
        dataChannel = peerConnection.CreateDataChannel("textStream", dcConfig);
        
        dataChannel.OnOpen = () =>
        {
            Debug.Log("[TextStreamSender] DataChannel opened!");
            connectionStatus = "DataChannel Open";
        };

        dataChannel.OnClose = () =>
        {
            Debug.Log("[TextStreamSender] DataChannel closed.");
            HandleDisconnection();
        };

        // Create offer
        var offerOp = peerConnection.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError($"[TextStreamSender] CreateOffer error: {offerOp.Error.message}");
            pendingOffer = false;
            yield break;
        }

        var desc = offerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref desc);
        yield return setLocalOp;

        if (setLocalOp.IsError)
        {
            Debug.LogError($"[TextStreamSender] SetLocalDescription error: {setLocalOp.Error.message}");
            pendingOffer = false;
            yield break;
        }

        // Send offer via UDP
        SendSignalingMessage(new SignalingMessage
        {
            type = "OFFER",
            sdp = desc.sdp,
            senderIP = GetLocalIPAddress()
        });

        Debug.Log("[TextStreamSender] Offer sent.");
    }

    private void HandleAnswer(SignalingMessage msg)
    {
        if (peerConnection == null) return;

        StartCoroutine(SetRemoteAnswer(msg.sdp));
    }

    private IEnumerator SetRemoteAnswer(string sdp)
    {
        var desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
        var op = peerConnection.SetRemoteDescription(ref desc);
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"[TextStreamSender] SetRemoteDescription error: {op.Error.message}");
        }
        else
        {
            Debug.Log("[TextStreamSender] Remote answer set successfully.");
            pendingOffer = false;
            lastHeartbeatReceived = Time.time;
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
        Debug.Log($"[TextStreamSender] Added remote ICE candidate.");
    }

    private void SendIceCandidate(RTCIceCandidate candidate)
    {
        if (remoteEndPoint == null) return;

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
        if (remoteEndPoint == null) return;

        try
        {
            string json = JsonConvert.SerializeObject(msg);
            byte[] data = Encoding.UTF8.GetBytes(json);
            udpClient.Send(data, data.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TextStreamSender] Failed to send signaling: {e.Message}");
        }
    }

    private void SendHeartbeat()
    {
        if (remoteEndPoint == null) return;

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
                    Debug.LogWarning("[TextStreamSender] Heartbeat timeout. Reconnecting...");
                    HandleDisconnection();
                }
            }
        }
    }

    private void HandleDisconnection()
    {
        if (!isConnected && !pendingOffer) return;

        connectionStatus = "Disconnected";
        isConnected = false;
        pendingOffer = false;
        
        OnDisconnected?.Invoke();

        // Cleanup WebRTC
        if (dataChannel != null)
        {
            dataChannel.Close();
            dataChannel = null;
        }

        if (peerConnection != null)
        {
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        Debug.Log("[TextStreamSender] Disconnected. Waiting for new DISCOVER...");
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

    #region Public API

    /// <summary>
    /// Send STT transcription text to the remote receiver.
    /// </summary>
    public void SendSTTText(string text)
    {
        SendText("stt", text);
    }

    /// <summary>
    /// Send LLM response text to the remote receiver.
    /// </summary>
    public void SendLLMText(string text)
    {
        SendText("llm", text);
    }

    private void SendText(string type, string text)
    {
        if (dataChannel == null || dataChannel.ReadyState != RTCDataChannelState.Open)
        {
            Debug.LogWarning($"[TextStreamSender] Cannot send {type} text - DataChannel not open.");
            return;
        }

        var msg = new TextMessage
        {
            type = type,
            text = text,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        string json = JsonConvert.SerializeObject(msg);
        dataChannel.Send(json);
        Debug.Log($"[TextStreamSender] Sent {type}: {text.Substring(0, Math.Min(50, text.Length))}...");
    }

    /// <summary>
    /// Check if the sender is connected to a receiver.
    /// </summary>
    public bool IsConnected => isConnected;

    #endregion

    #region Test Methods

    [ContextMenu("Test Send STT Text")]
    private void TestSendSTTText()
    {
        SendSTTText("STT Test - This is a test transcription message.");
    }

    [ContextMenu("Test Send LLM Text")]
    private void TestSendLLMText()
    {
        SendLLMText("LLM Test - This is a test LLM response message.");
    }

    // Test Track Reference
    private AudioStreamTrack manualTestTrack;
    private GameObject manualTestObject;

    [ContextMenu("Test Stream Audio File (ref.wav)")]
    private void TestStreamAudioFile()
    {
        StartCoroutine(LoadAndSetupTestAudio());
    }

    private IEnumerator LoadAndSetupTestAudio()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "ref.wav");
        string url = "file://" + path;
        
        Debug.Log($"[TextStreamSender] Loading test audio for stream: {url}");

        using (var uwr = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(uwr);
                
                // Cleanup previous test if any
                if (manualTestTrack != null) { manualTestTrack.Dispose(); manualTestTrack = null; }
                if (manualTestObject != null) { Destroy(manualTestObject); manualTestObject = null; }

                // Create Clean Source
                manualTestObject = new GameObject("TestStream_AudioSource_Clean");
                manualTestObject.transform.SetParent(transform);
                var tempSource = manualTestObject.AddComponent<AudioSource>();
                
                tempSource.loop = true;
                tempSource.clip = clip;
                tempSource.volume = 1.0f;
                tempSource.spatialBlend = 0f; 
                tempSource.bypassEffects = false; 

                Debug.Log($"[TextStreamSender] Playing test audio ({clip.length}s) locally.");
                tempSource.Play();

                // Attach Local Debugger
                var debugger = manualTestObject.AddComponent<AudioStreamDebugger>();
                debugger.sourceName = "Sender_Local_Clean";

                // Create Track
                manualTestTrack = new AudioStreamTrack(tempSource);
                Debug.Log("[TextStreamSender] Test Audio Track created. Restarting connection to include it...");

                // Force Reconnection to send new Offer with this track
                // We assume we are connected or will be discovered.
                // If connected, this offer will trigger a renegotiation (or reconnection logic on receiver).
                StartCoroutine(CreateAndSendOffer());
            }
            else
            {
                Debug.LogError($"[TextStreamSender] Failed to load test audio: {uwr.error}");
            }
        }
    }

    // Helper class to monitor audio levels and record WAV (Duplicated from Receiver for debugging)
    public class AudioStreamDebugger : MonoBehaviour
    {
        public string sourceName;
        
        private float _lastRms;
        private float _timer;

        void OnAudioFilterRead(float[] data, int channels)
        {
            float sum = 0;
            for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
            _lastRms = Mathf.Sqrt(sum / data.Length);
        }

        void Update()
        {
            _timer += Time.deltaTime;
            if (_timer > 1.0f)
            {
                _timer = 0;
                float db = 20 * Mathf.Log10(_lastRms + 1e-6f);
                if (_lastRms > 0.001f)
                    Debug.Log($"[SenderDebugger] {sourceName}: RMS={_lastRms:F4} ({db:F1} dB) - PLAYING LOCAL");
                else
                    Debug.Log($"[SenderDebugger] {sourceName}: RMS={_lastRms:F4} ({db:F1} dB) - SILENT LOCAL");
            }
        }
    }

    [ContextMenu("Show Connection Status")]
    private void ShowConnectionStatus()
    {
        Debug.Log($"[TextStreamSender] Status: {connectionStatus}, Connected: {isConnected}, " +
                  $"DataChannel: {(dataChannel != null ? dataChannel.ReadyState.ToString() : "null")}");
    }

    #endregion
}
