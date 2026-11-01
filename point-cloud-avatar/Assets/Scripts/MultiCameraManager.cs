using System.Collections.Generic;
using UnityEngine;

public class MultiCameraManager : MonoBehaviour
{
    public enum UpdateMode
    {
        EveryFrame,
        Every2Frames,
        Every4Frames,
        RoundRobin,
        Manual
    }

    [Header("Settings")]
    public float pointSize = 5f;
    public float maxDepthDistance = 10f;
    [Range(0.5f, 3f)] public float brightness = 1.0f;
    [Range(0f, 1f)] public float opacity = 1.0f;
    public bool freezeCapture = false;
    public bool showPointCloud = true;
    public bool showMesh = false;
    
    [Header("Performance")]
    public UpdateMode updateMode = UpdateMode.Every2Frames;
    [Range(1, 4)] public int globalDownsampleFactor = 2;
    
    [Header("Visual Optimization")]
    [Range(1, 4)] public int interleaveFactor = 2; // Reduce vertex load by drawing 1/N points per frame
    
    [Header("PCR Toggle")]
    public bool isPCREnabled = true;

    [Header("Cameras")]
    public List<PointCloudGPU> cameras = new List<PointCloudGPU>();

    private int frameCount = 0;
    private int currentCameraIndex = 0;
    private int interleaveFrameIndex = 0;

    void Start()
    {
        if (cameras.Count == 0)
        {
            cameras.AddRange(GetComponentsInChildren<PointCloudGPU>());
        }
        ApplySettings();
    }

    void Update()
    {
        frameCount++;
        ApplySettings();

        if (!isPCREnabled || freezeCapture) return;
        
        interleaveFrameIndex++;

        switch (updateMode)
        {
            case UpdateMode.EveryFrame:
                ProcessAllCameras();
                break;
            case UpdateMode.Every2Frames:
                if (frameCount % 2 == 0) ProcessAllCameras();
                break;
            case UpdateMode.Every4Frames:
                if (frameCount % 4 == 0) ProcessAllCameras();
                break;
            case UpdateMode.RoundRobin:
                if (cameras.Count > 0)
                {
                    // RoundRobin: Only ONE camera updates its compute buffer per frame
                    var activeCam = cameras[currentCameraIndex];
                    if (activeCam != null && activeCam.isActiveAndEnabled)
                    {
                        activeCam.ForceRender();
                    }
                    currentCameraIndex = (currentCameraIndex + 1) % cameras.Count;
                    
                    // Render visuals for ALL cameras using interleave (always visible, lower load)
                    int offset = interleaveFrameIndex % interleaveFactor;
                    foreach (var cam in cameras)
                    {
                        if (cam != null && cam.isActiveAndEnabled) cam.ForceRenderVisuals(interleaveFactor, offset);
                    }
                }
                break;
        }
    }
    
    void ProcessAllCameras()
    {
        int offset = interleaveFrameIndex % interleaveFactor;
        foreach (var cam in cameras)
        {
            if (cam != null && cam.isActiveAndEnabled)
            {
                cam.ForceRender();
                cam.ForceRenderVisuals(interleaveFactor, offset);
            }
        }
    }

    void ApplySettings()
    {
        foreach (var cam in cameras)
        {
            if (cam != null && cam.isActiveAndEnabled)
            {
                cam.managedByManager = true;
                cam.pointSize = pointSize;
                cam.maxDepthDistance = maxDepthDistance;
                cam.brightness = brightness;
                cam.opacity = opacity;
                cam.showPointCloud = showPointCloud;
                cam.showMesh = showMesh;
                
                if (cam.depthCamera != null)
                {
                    cam.depthCamera.downsampleFactor = globalDownsampleFactor;
                }
            }
        }
    }
    
    [ContextMenu("Refresh Cameras")]
    public void RefreshCameras()
    {
        cameras.Clear();
        cameras.AddRange(GetComponentsInChildren<PointCloudGPU>());
    }
}
