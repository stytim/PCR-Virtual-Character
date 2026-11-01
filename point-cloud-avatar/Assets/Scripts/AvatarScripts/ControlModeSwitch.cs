using UnityEngine;
using com.rfilkov.kinect;
using com.rfilkov.components;
using RootMotion.FinalIK;


/// <summary>
/// Switches the avatar between AI-controlled (prerecorded animations) and Human-controlled 
/// (live tracking from Kinect + Meta Quest) modes by enabling/disabling the respective GameObjects.
/// </summary>
public class ControlModeSwitch : MonoBehaviour
{

    /// <summary>
    /// Control mode for avatar animation source.
    /// </summary>
    public enum ControlMode
    {
        AI,     // Prerecorded animations via AvatarBehaviorController
        Human   // Live tracking via Kinect + Meta Quest
    }
    [Header("Current Mode")]
    [Tooltip("The current control mode. Change this in the Inspector or call ToggleMode() at runtime.")]
    public ControlMode currentMode = ControlMode.AI;

    [Header("AI Mode (Prerecorded Animations)")]
    [Tooltip("The GameObject containing the AvatarBehaviorController script.")]
    public GameObject aiControllerObject;

    [Header("Human Mode (Live Tracking)")]
    [Tooltip("The GameObject containing UserAvatarMatcher and BodyDataReceiver scripts.")]
    public GameObject humanControllerObject;

    [Tooltip("The GameObject containing KinectManager (with NetClientInterface as child). " +
             "This will be disabled in AI mode to stop Kinect tracking.")]
    public GameObject kinectManagerObject;

    [Tooltip("The avatar model GameObject. The AvatarController component will be searched on this " +
             "at runtime (since UserAvatarMatcher adds it dynamically).")]
    public GameObject avatarModel;
    public Camera dummyKinect;

    // Cached reference to the AvatarController (found at runtime)
    private com.rfilkov.components.AvatarController _avatarControllerComponent;

    private void Start()
    {
        if (aiControllerObject == null)
        {
            aiControllerObject = FindFirstObjectByType<AvatarBehaviorController>().gameObject;
        }
        if (humanControllerObject == null)
        {
            humanControllerObject = FindFirstObjectByType<UserAvatarMatcher>().gameObject;
        }
        if (kinectManagerObject == null)
        {
            kinectManagerObject = FindFirstObjectByType<KinectManager>().gameObject;
        }
        // Apply the initial mode on start
        ApplyMode(currentMode);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleMode();
        }
    }

    /// <summary>
    /// Tries to find the AvatarController component on the avatar model.
    /// This is needed because UserAvatarMatcher adds it dynamically at runtime.
    /// </summary>
    private void TryFindAvatarController()
    {
        humanControllerObject.GetComponent<com.rfilkov.components.UserAvatarMatcher>().InitializeAvatarController();
        _avatarControllerComponent = avatarModel.GetComponent<com.rfilkov.components.AvatarController>();
        _avatarControllerComponent.posRelativeToCamera = dummyKinect;
        
    }

    /// <summary>
    /// Toggles between AI and Human control modes.
    /// </summary>
    [ContextMenu("Toggle Control Mode")]
    public void ToggleMode()
    {
        currentMode = (currentMode == ControlMode.AI) ? ControlMode.Human : ControlMode.AI;
        ApplyMode(currentMode);
        Debug.Log($"<color=magenta>[ControlModeSwitch]</color> Toggled to: <b>{currentMode}</b>");
    }

    public void SetControlMode(ControlMode mode)
    {
        currentMode = mode;
        ApplyMode(mode);
        Debug.Log($"<color=magenta>[ControlModeSwitch]</color> Set to: <b>{currentMode}</b>");
    }

    /// <summary>
    /// Applies the specified control mode by enabling/disabling the appropriate GameObjects.
    /// </summary>
    private void ApplyMode(ControlMode mode)
    {
        switch (mode)
        {
            case ControlMode.AI:
                EnableAIMode();
                break;
            case ControlMode.Human:
                EnableHumanMode();
                break;
        }
    }

    private void EnableAIMode()
    {
        // Enable AI controller
        if (aiControllerObject != null)
        {
            aiControllerObject.SetActive(true);
            
            // Reset animator to idle state when switching to AI mode
            if (AvatarBehaviorController.Instance != null)
            {
                AvatarBehaviorController.Instance.EnterIdleMode();
            }
        }

        // Disable Human controller
        if (humanControllerObject != null)
        {
            humanControllerObject.SetActive(false);
        }

        // Disable KinectManager (and its NetClientInterface child)
        if (kinectManagerObject != null)
        {
            kinectManagerObject.SetActive(false);
        }

        // Try to find and disable AvatarController component on avatar model

        TryFindAvatarController();
        if (_avatarControllerComponent != null)
        {
            _avatarControllerComponent.enabled = false;
        }
        avatarModel.transform.localPosition = new Vector3(0, -0.986f, 0);
        avatarModel.transform.localRotation = Quaternion.identity;
        avatarModel.GetComponent<Animator>().enabled = true;
        avatarModel.GetComponent<BipedIK>().enabled = false;
    }

    private void EnableHumanMode()
    {
        // Disable AI controller
        if (aiControllerObject != null)
        {
            aiControllerObject.SetActive(false);
        }

        
        // Enable Human controller
        if (humanControllerObject != null)
        {
            humanControllerObject.GetComponent<com.rfilkov.components.UserAvatarMatcher>().posRelativeToCamera = dummyKinect;
            humanControllerObject.SetActive(true);
        }

        // Enable KinectManager (and its NetClientInterface child)
        if (kinectManagerObject != null)
        {
            kinectManagerObject.SetActive(true);
        }

        // Try to find and enable AvatarController component on avatar model
        TryFindAvatarController();
        if (_avatarControllerComponent != null)
        {
            _avatarControllerComponent.enabled = true;
        }
        avatarModel.GetComponent<Animator>().enabled = false;
        avatarModel.GetComponent<BipedIK>().enabled = true;
    }

    /// <summary>
    /// Gets the current control mode.
    /// </summary>
    public ControlMode GetCurrentMode()
    {
        return currentMode;
    }

    /// <summary>
    /// Checks if currently in AI mode.
    /// </summary>
    public bool IsAIMode => currentMode == ControlMode.AI;

    /// <summary>
    /// Checks if currently in Human mode.
    /// </summary>
    public bool IsHumanMode => currentMode == ControlMode.Human;
}
