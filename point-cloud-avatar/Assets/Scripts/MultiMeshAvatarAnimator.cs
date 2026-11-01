using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Utilities.Audio;
using App.Audio; // Add this namespace

public class MultiMeshAvatarAnimator : MonoBehaviour
{
    [Header("References")]
    public A2FClient client;
    // public StreamAudioSource streamSource;
    public SyncAudioSource streamSource;
    public SkinnedMeshRenderer[] targetMeshes;

    [Header("Configuration")]
    public float weightMultiplier = 100.0f;

    [Header("Sync Settings")]
    public bool useAudioSync = true;

    [Header("Debug")]
    public bool showDebugGUI = true;

    // --- SYNC DATA ---
    private struct AnimationFrame
    {
        public float time;
        public Dictionary<string, float> weights;
    }
    private List<AnimationFrame> _frameBuffer = new List<AnimationFrame>();

    // The timestamp of the very first frame received from A2F for the current sentence
    private float _firstFrameA2FTime = -1f;

    // The Audio Clock time when that first frame arrived
    private float _audioTimeAtFirstFrame = -1f;

    // --- CACHING ---
    private Dictionary<string, List<BlendShapeTarget>> _globalBlendShapeMap;
    private struct BlendShapeTarget { public SkinnedMeshRenderer mesh; public int index; }

    // --- LIVE DEBUGGING VARS ---
    private float _currentAudioTime;
    private float _currentAnimTime;
    private float _currentDelta; // (AudioTime - AnimTime)
    private float _avgDelta;
    private int _frameCount;
    private int _debugFrameCounter = 0; // For periodic logging
    private float _lastFrameReceivedTime = 0f; // Watchdog: when did we last get an A2F frame?
    private int _totalFramesReceived = 0;
    private bool _loggedBufferDrought = false; // Prevent spam when buffer stays empty

    private void OnEnable()
    {
        SceneSessionManager.Instance.OnSceneChanged.AddListener(SetMultiMeshAvatarAnimator);
    }

    private void OnDisable()
    {
        SceneSessionManager.Instance.OnSceneChanged.RemoveListener(SetMultiMeshAvatarAnimator);
    }

    private void SetMultiMeshAvatarAnimator()
    {
        targetMeshes[0] = SceneSessionManager.Instance.currentHeadSMR;
        targetMeshes[1] = SceneSessionManager.Instance.currentTeethSMR;
        targetMeshes[2] = SceneSessionManager.Instance.currentTongueSMR;
        BuildBlendShapeMap();
        ResetSession();
    }    

    void Start()
    {
        BuildBlendShapeMap();

        if (client != null) client.OnAnimationReceived += OnFrameReceived;
        if (streamSource == null) streamSource = GetComponent<SyncAudioSource>();
        if (streamSource == null && client != null) streamSource = client.GetComponent<SyncAudioSource>();
    }

    public void ResetSession()
    {
        _frameBuffer.Clear();
        _firstFrameA2FTime = -1f;
        _audioTimeAtFirstFrame = -1f;
        _avgDelta = 0f;
        _frameCount = 0;
        _totalFramesReceived = 0;
        _loggedBufferDrought = false;
        _lastFrameReceivedTime = Time.time;
        Debug.Log("[Animator] Session Reset.");
    }

    private void OnFrameReceived(float rawTimeStamp, Dictionary<string, float> weights)
    {
        if (useAudioSync)
        {
            _lastFrameReceivedTime = Time.time;
            _totalFramesReceived++;
            float currentAudioClock = (streamSource != null) ? streamSource.PlaybackTime : 0f;

            // 1. Initialize Zero Point on first frame
            if (_firstFrameA2FTime < 0)
            {
                _firstFrameA2FTime = rawTimeStamp;
                _audioTimeAtFirstFrame = currentAudioClock;

                // Log the initial latency hit
            //     Debug.Log($"🟢 [Sync Start] First A2F Frame Arrived! " +
            //               $"A2F_T0={rawTimeStamp:F3}s | AudioClock={currentAudioClock:F3}s | " +
            //               $"Offset={currentAudioClock:F3}s");
            }
            else
            {
                // DIAGNOSTIC: Detect stale frames from previous sentence (Root Cause #5)
                if (rawTimeStamp < _firstFrameA2FTime)
                {
                    Debug.LogError($"🔴 [A2F STALE] Received timestamp {rawTimeStamp:F3} < start {_firstFrameA2FTime:F3}. Stale packet from previous sentence!");
                    return; // Skip this stale frame
                }
            }

            // 2. Normalize A2F time to start at 0.0
            float adjustedTime = rawTimeStamp - _firstFrameA2FTime;

            _frameBuffer.Add(new AnimationFrame { time = adjustedTime, weights = weights });

            // 3. Cleanup old frames to save memory
            float cleanupThreshold = currentAudioClock - _audioTimeAtFirstFrame - 2.0f;
            if (_frameBuffer.Count > 0 && _frameBuffer[0].time < cleanupThreshold)
            {
                int removedCount = 0;
                while (_frameBuffer.Count > 0 && _frameBuffer[0].time < cleanupThreshold)
                {
                    _frameBuffer.RemoveAt(0);
                    removedCount++;
                }
                if (removedCount > 5)
                {
                    Debug.LogWarning($"🟡 [A2F Cleanup] Removed {removedCount} old frames. Remaining: {_frameBuffer.Count}");
                }
            }
        }
        else
        {
            ApplyInterpolatedFrame(weights, weights, 0f);
        }
    }

    void Update()
    {
        if (useAudioSync) ProcessSyncLogic();
    }

    private void ProcessSyncLogic()
    {
        if (_frameBuffer.Count == 0)
        {
            // DIAGNOSTIC: Detect when buffer stays empty for too long
            float timeSinceLastFrame = Time.time - _lastFrameReceivedTime;
            if (timeSinceLastFrame > 2.0f && !_loggedBufferDrought)
            {
                _loggedBufferDrought = true;
                float playbackTime = (streamSource != null) ? streamSource.PlaybackTime : -1f;
                // Debug.LogError($"🔴 [A2F DROUGHT] No A2F frames for {timeSinceLastFrame:F1}s! " +
                //                $"Total frames ever received this session: {_totalFramesReceived} | " +
                //                $"PlaybackTime={playbackTime:F3}s | " +
                //                $"firstFrameA2FTime={_firstFrameA2FTime:F3}");
            }
            return;
        }
        _loggedBufferDrought = false; // Reset if we have frames again
        if (streamSource == null) return;

        // --- 1. CALCULATE TIME ---
        float rawAudioTime = streamSource.PlaybackTime;
        float targetAnimTime = rawAudioTime - _audioTimeAtFirstFrame;

        // DIAGNOSTIC: Detect negative targetAnimTime (Root Cause #1)
        if (targetAnimTime < 0)
        {
            _debugFrameCounter++;
            if (_debugFrameCounter % 30 == 1) // Log once per ~0.5s, not every frame
            {
                Debug.LogWarning($"🔴 [A2F NEGATIVE] targetAnimTime={targetAnimTime:F3} (Audio={rawAudioTime:F3} - Offset={_audioTimeAtFirstFrame:F3}). Face frozen!");
            }
            return; // Don't apply animation with negative time
        }

        // DEBUG LOGGING
        _currentAudioTime = rawAudioTime;
        _currentAnimTime = targetAnimTime;

        // --- 2. FETCH FRAMES ---
        AnimationFrame frameA = _frameBuffer.LastOrDefault(f => f.time <= targetAnimTime);
        AnimationFrame frameB = _frameBuffer.FirstOrDefault(f => f.time > targetAnimTime);

        // --- 3. INTERPOLATE ---
        if (frameA.weights == null)
        {
            // DIAGNOSTIC: No frame at or before targetAnimTime
            _debugFrameCounter++;
            if (_debugFrameCounter % 60 == 1)
            {
                Debug.LogWarning($"🔴 [A2F NO_FRAME_A] targetAnimTime={targetAnimTime:F3} but earliest buffered frame={_frameBuffer[0].time:F3}. " +
                                 $"Buffer range: [{_frameBuffer[0].time:F3}..{_frameBuffer[_frameBuffer.Count-1].time:F3}] count={_frameBuffer.Count}");
            }
            frameA = _frameBuffer.FirstOrDefault();
        }
        if (frameB.weights == null) frameB = frameA;

        float t = 0f;
        if (frameB.time > frameA.time)
        {
            t = (targetAnimTime - frameA.time) / (frameB.time - frameA.time);
        }

        // Calculate drift for debug
        _currentDelta = targetAnimTime - frameA.time;

        // DIAGNOSTIC: Periodic state snapshot (every ~1 second)
        // _debugFrameCounter++;
        // if (_debugFrameCounter % 60 == 0)
        // {
        //     Debug.Log($"📊 [A2F Sync] Audio={rawAudioTime:F3} | Target={targetAnimTime:F3} | " +
        //               $"FrameA.t={frameA.time:F3} | Delta={_currentDelta:F4} | " +
        //               $"Buffer=[{_frameBuffer[0].time:F3}..{_frameBuffer[_frameBuffer.Count-1].time:F3}] x{_frameBuffer.Count}");
        // }

        ApplyInterpolatedFrame(frameA.weights, frameB.weights, t);
    }

    private void ApplyInterpolatedFrame(Dictionary<string, float> weightsA, Dictionary<string, float> weightsB, float t)
    {
        foreach (var kvp in weightsA)
        {
            if (_globalBlendShapeMap.TryGetValue(kvp.Key.ToLowerInvariant(), out List<BlendShapeTarget> targets))
            {
                float valA = kvp.Value;
                float valB = weightsB.ContainsKey(kvp.Key) ? weightsB[kvp.Key] : valA;
                float finalWeight = Mathf.Lerp(valA, valB, t) * weightMultiplier;

                for (int i = 0; i < targets.Count; i++)
                    targets[i].mesh.SetBlendShapeWeight(targets[i].index, finalWeight);
            }
        }
    }

    private void BuildBlendShapeMap()
    {
        _globalBlendShapeMap = new Dictionary<string, List<BlendShapeTarget>>();
        foreach (var meshRenderer in targetMeshes)
        {
            if (meshRenderer == null) continue;
            Mesh m = meshRenderer.sharedMesh;
            for (int i = 0; i < m.blendShapeCount; i++)
            {
                string cleanName = m.GetBlendShapeName(i).ToLowerInvariant();
                if (cleanName.Contains(".")) cleanName = cleanName.Split('.').Last();
                if (!_globalBlendShapeMap.ContainsKey(cleanName))
                    _globalBlendShapeMap[cleanName] = new List<BlendShapeTarget>();
                _globalBlendShapeMap[cleanName].Add(new BlendShapeTarget { mesh = meshRenderer, index = i });
            }
        }
    }

    private void OnGUI()
    {
        if (!showDebugGUI) return;

        GUI.skin.label.fontSize = 20;
        GUILayout.BeginArea(new Rect(10, 10, 600, 300), "A2F Sync Debugger", GUI.skin.window);

        GUILayout.Label($"Audio Clock (Raw): {_currentAudioTime:F3}s");
        GUILayout.Label($"Latency Offset: {_audioTimeAtFirstFrame:F3}s (Subtracted)");
        GUILayout.Label($"Target Anim Time: {_currentAnimTime:F3}s");

        GUI.color = (_currentDelta > 0.1f) ? Color.red : Color.green;
        GUILayout.Label($"Frame Buffer Delta: {_currentDelta:F4}s");
        GUILayout.Label(_currentDelta > 0.1f ? "LAGGING / BUFFER EMPTY" : "SYNCED");
        GUI.color = Color.white;

        if (_frameBuffer.Count > 0)
        {
            GUILayout.Label($"Buffered Range: {_frameBuffer[0].time:F2}s -> {_frameBuffer.Last().time:F2}s");
            GUILayout.Label($"Buffer Count: {_frameBuffer.Count}");
        }

        GUILayout.EndArea();
    }
}