using UnityEngine;
using UnityEngine.Events;

// Ensure you have the Meta XR SDK imported so OVRInput is available.

public class TriggerButtonComboDetector : MonoBehaviour
{
    [Header("Input Settings")]
    [Tooltip("The OVRInput Axis1D trigger to monitor (e.g., SecondaryHandTrigger for right index).")]
    public OVRInput.Axis1D targetTrigger = OVRInput.Axis1D.SecondaryHandTrigger; // Left Index Trigger

    [Tooltip("The OVRInput Button to monitor (e.g., Button.One for X/A button).")]
    public OVRInput.Button targetButton = OVRInput.Button.One; // X button on Left Touch

    [Tooltip("The OVRInput Button to monitor (e.g., Button.One for X/A button).")]
    public OVRInput.Button secondTargetButton = OVRInput.Button.Two; // X button on Left Touch

    [Tooltip("The OVRInput Button to monitor (e.g., Button.Three for Y/B button).")]
    public OVRInput.Button thirdTargetButton = OVRInput.Button.Three; // Y button on Left Touch

    [Tooltip("The controller to check the inputs on (e.g., LTouch for left).")]
    public OVRInput.Controller targetController = OVRInput.Controller.LTouch; // Left Controller

    public OVRInput.Axis2D thumbstick;

    [Tooltip("How much the trigger needs to be pressed (0 to 1) to be considered 'held'.")]
    [Range(0.01f, 1.0f)]
    public float triggerPressThreshold = 0.5f; // Trigger needs to be at least half-pressed

    [Header("Event Trigger")]
    [Tooltip("Assign functions here that should run when the combo is detected.")]
    public UnityEvent onComboActionTriggered; // The event that will be invoked
    public UnityEvent onSecondComboActionTriggered; // The second event that will be invoked
    public UnityEvent onThirdComboActionTriggered;

    public UnityEvent onActionTriggered; // The event that will be invoked
    public UnityEvent onSecondActionTriggered; // The second event that will be invoked
    public UnityEvent onThirdActionTriggered;

    public UnityEvent onThumbstickactivated;

    [Header("Right Controller - Scene Switch")]
    [Tooltip("The trigger axis on the right controller for the scene switch combo.")]
    public OVRInput.Axis1D sceneSwitchTrigger = OVRInput.Axis1D.PrimaryHandTrigger;
    [Tooltip("The button to press (while holding trigger) to switch scenes.")]
    public OVRInput.Button sceneSwitchButton = OVRInput.Button.Two; // B button on Right Touch
    [Tooltip("The controller for scene switch input.")]
    public OVRInput.Controller sceneSwitchController = OVRInput.Controller.RTouch;
    public UnityEvent onSceneSwitchTriggered;

    [Header("Thumbstick Rotation")]
    public GameObject virtualScene;

    [Tooltip("Rotation speed in degrees per second.")]
    public float rotationSpeed = 30f;

    [Tooltip("Translation speed in units per second when moving scene towards/away from camera.")]
    public float translationSpeed = 2f;

    public GameObject leftController;
    public GameObject rightController;

    private bool isleftController = false;

    // --- Private Variables ---
    private bool isTriggerHeld = false;

    // public GameObject convUI;
    private Vector3 uiPos;

    private void Start()
    {
        // uiPos = convUI.transform.localPosition;
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            RotateAroundPivot(virtualScene, -rotationSpeed * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.RightArrow))
        {
            RotateAroundPivot(virtualScene, rotationSpeed * Time.deltaTime);
        }
        else if (Input.GetKey(KeyCode.UpArrow))
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 camForward = mainCam.transform.forward;
                camForward.y = 0f;
                camForward.Normalize();
                virtualScene.transform.position += camForward * translationSpeed * Time.deltaTime;
            }
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 camForward = mainCam.transform.forward;
                camForward.y = 0f;
                camForward.Normalize();
                virtualScene.transform.position -= camForward * translationSpeed * Time.deltaTime;
            }
        }

        // --- Check Trigger State ---
        // Get the current value of the specified trigger axis
        float triggerValue = OVRInput.Get(targetTrigger, targetController);

        // Determine if the trigger is currently considered held down
        isTriggerHeld = triggerValue >= triggerPressThreshold;

        // --- Check for Combo Condition ---
        // We only proceed if the trigger is currently held down
        if (isTriggerHeld)
        {
            // Check if the target button was *just pressed down* in this frame
            // OVRInput.GetDown() returns true only for the single frame the button is initially pressed.
            if (OVRInput.GetDown(targetButton, targetController))
            {
                // Both conditions met: Trigger is held AND button was just pressed.
                Debug.Log($"Combo Detected! Invoking assigned actions for {targetTrigger} + {targetButton} on {targetController}.");
                onComboActionTriggered.Invoke();
            }

            if (OVRInput.GetDown(secondTargetButton, targetController))
            {
                // Both conditions met: Trigger is held AND button was just pressed.
                Debug.Log($"Second Combo Detected! Invoking assigned actions for {targetTrigger} + {targetButton} on {targetController}.");
                onSecondComboActionTriggered.Invoke();
            }

            if (OVRInput.GetDown(thirdTargetButton, targetController))
            {
                // Both conditions met: Trigger is held AND button was just pressed.
                Debug.Log($"Third Combo Detected! Invoking assigned actions for {targetTrigger} + {targetButton} on {targetController}.");
                onThirdComboActionTriggered.Invoke();
            }

            Vector2 thumbstickValue = OVRInput.Get(thumbstick);
            if (virtualScene != null && Mathf.Abs(thumbstickValue.x) > 0.1f)
            {
                RotateAroundPivot(virtualScene, thumbstickValue.x * rotationSpeed * Time.deltaTime);
            }
            if (thumbstickValue.x > 0.7f)
            {
                onThumbstickactivated.Invoke();
            }

            // Thumbstick Y-axis: move scene towards/away from main camera's facing direction
            if (virtualScene != null && Mathf.Abs(thumbstickValue.y) > 0.1f)
            {
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    // Use the camera's forward direction (horizontal only)
                    Vector3 camForward = mainCam.transform.forward;
                    camForward.y = 0f;
                    camForward.Normalize();

                    float moveAmount = thumbstickValue.y * translationSpeed * Time.deltaTime;
                    virtualScene.transform.position -= camForward * moveAmount;
                }
            }

        }
        else
        {
             if (OVRInput.GetDown(targetButton, targetController))
            {
                // Both conditions met: Trigger is held AND button was just pressed.
                Debug.Log($"Action Detected! Invoking assigned actions for {targetTrigger} + {targetButton} on {targetController}.");
                onActionTriggered.Invoke();
            }

            if (OVRInput.GetDown(secondTargetButton, targetController))
            {
                // Both conditions met: Trigger is held AND button was just pressed.
                Debug.Log($"Second Action Detected! Invoking assigned actions for {targetTrigger} + {targetButton} on {targetController}.");
                onSecondActionTriggered.Invoke();
            }

            if (OVRInput.GetDown(thirdTargetButton, targetController))
            {
                // Both conditions met: Trigger is held AND button was just pressed.
                Debug.Log($"Third Action Detected! Invoking assigned actions for {targetTrigger} + {targetButton} on {targetController}.");
                onThirdActionTriggered.Invoke();
            }
        }

        // --- Right Controller Scene Switch Combo ---
        float sceneSwitchTriggerValue = OVRInput.Get(sceneSwitchTrigger, sceneSwitchController);
        bool isSceneSwitchTriggerHeld = sceneSwitchTriggerValue >= triggerPressThreshold;

        if (isSceneSwitchTriggerHeld && OVRInput.GetDown(sceneSwitchButton, sceneSwitchController))
        {
            Debug.Log($"Scene Switch Combo Detected! Right trigger + {sceneSwitchButton} on {sceneSwitchController}.");
            onSceneSwitchTriggered?.Invoke();
        }



        // // Check left or right controller trgger
        // if (OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) >= triggerPressThreshold)
        // {
        //     isleftController = true;
        //     convUI.transform.parent = leftController.transform;
        //     Debug.Log("Left Controller Trigger Pressed");
        // }
        // else if (OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch) >= triggerPressThreshold)
        // {
        //     isleftController = false;
        //     convUI.transform.parent = rightController.transform;
        //     Debug.Log("Right Controller Trigger Pressed");
        // }

        // convUI.transform.localPosition = uiPos;
        // convUI.transform.localRotation = Quaternion.identity;

        // if (isTriggerHeld) 
        // {
        //     convUI.SetActive(true); 
        // }
        // else
        // { 
        //     convUI.SetActive(false); 
        // }
    }

    /// <summary>
    /// Rotates a GameObject around its pivot.
    /// </summary>
    private void RotateAroundPivot(GameObject obj, float angleDegrees)
    {
        if (obj == null) return;

        obj.transform.Rotate(0f, angleDegrees, 0f, Space.World);
    }
}
