using UnityEngine;

public class PCRSwitch : MonoBehaviour
{
    public enum RenderMode
    {
        PointCloudRendering,
        NormalMesh
    }

    [Header("Settings")]
    public RenderMode currentMode = RenderMode.PointCloudRendering;

    [Header("References")]
    public MultiCameraManager rgbdCameraManager;

    private Camera mainCamera;
    private int rgbdLayerMask;

    void Start()
    {
        mainCamera = Camera.main;
        rgbdLayerMask = LayerMask.GetMask("RGBD");

        // Apply the initial mode
        ApplyMode(currentMode);
    }

    void Update()
    {
        // Optional: Toggle mode with a key press (e.g., 'M')
        if (Input.GetKeyDown(KeyCode.M))
        {
            ToggleMode();
        }
    }

    public void ToggleMode()
    {
        currentMode = (currentMode == RenderMode.PointCloudRendering)
            ? RenderMode.NormalMesh
            : RenderMode.PointCloudRendering;

        ApplyMode(currentMode);
    }

    public void SetMode(RenderMode mode)
    {
        currentMode = mode;
        ApplyMode(mode);
    }

    private void ApplyMode(RenderMode mode)
    {
        if (mainCamera == null)
        {
            Debug.LogWarning("PCRSwitch: Main camera not found.");
            return;
        }

        switch (mode)
        {
            case RenderMode.PointCloudRendering:
                // Exclude RGBD layer from main camera's culling mask
                mainCamera.cullingMask &= ~rgbdLayerMask;

                // Enable PCR pipeline via the manager
                if (rgbdCameraManager != null)
                {
                    rgbdCameraManager.isPCREnabled = true;
                    
                    // Enable DepthCamera scripts to resume capturing
                    foreach (var cam in rgbdCameraManager.cameras)
                    {
                        if (cam != null && cam.depthCamera != null)
                        {
                            cam.depthCamera.enabled = true;
                        }
                    }
                }
                break;

            case RenderMode.NormalMesh:
                // Include RGBD layer in main camera's culling mask
                mainCamera.cullingMask |= rgbdLayerMask;

                // Disable PCR pipeline via the manager
                if (rgbdCameraManager != null)
                {
                    rgbdCameraManager.isPCREnabled = false;
                    
                    // Disable DepthCamera scripts to stop capturing  
                    foreach (var cam in rgbdCameraManager.cameras)
                    {
                        if (cam != null && cam.depthCamera != null)
                        {
                            cam.depthCamera.enabled = false;
                        }
                    }
                }
                break;
        }

        Debug.Log($"PCRSwitch: Mode changed to {mode}");
    }
}
