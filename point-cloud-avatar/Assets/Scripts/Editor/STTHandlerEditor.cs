using UnityEngine;
using UnityEditor;
using System.Collections.Generic; // Required for HashSet

[CustomEditor(typeof(STTHandler))]
public class STTHandlerEditor : Editor
{
    private STTHandler sttHandler;
    private SerializedProperty activeMicrophoneDeviceForInfoProperty;
    // We don't need to store every property here if we iterate,
    // but it's good to get specific ones if you need special handling.

    private void OnEnable()
    {
        sttHandler = (STTHandler)target; // Get the instance of STTHandler being inspected
        // Find the specific property for our custom read-only display
        activeMicrophoneDeviceForInfoProperty = serializedObject.FindProperty("activeMicrophoneDeviceForInfo");

        if (activeMicrophoneDeviceForInfoProperty == null)
        {
            // The handlers possess this property, but not the manager. We'll find it manually if needed.
        }
    }

    public override void OnInspectorGUI()
    {
        // Always call this at the beginning
        serializedObject.Update();

        // 1. First, define the custom read-only fields/groups we want
        EditorGUILayout.LabelField("Server Setup", EditorStyles.boldLabel);
        
        // Let the default property iterator draw serverType
        SerializedProperty activeServerTypeProp = serializedObject.FindProperty("activeServerType");
        if (activeServerTypeProp != null)
        {
            EditorGUILayout.PropertyField(activeServerTypeProp);
        }

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Microphone Configuration", EditorStyles.boldLabel);

        // --- Custom Microphone Selection Dropdown ---
        string[] microphoneDevices = Microphone.devices;
        if (microphoneDevices.Length == 0)
        {
            EditorGUILayout.HelpBox("No microphone devices found. Please ensure a microphone is connected.", MessageType.Warning);
        }
        else
        {
            int currentDeviceIndex = -1;
            
            // Get the proper currently stored microphone name
            string currentDeviceName = "";
            
            // Awake() might not run in edit mode, so we ensure the references are populated for the editor script
            var pHandler = sttHandler.GetComponent<ParakeetSTTHandler>();
            var wHandler = sttHandler.GetComponent<WhisperHandler>();

            if (sttHandler.activeServerType == STTServerType.Parakeet && pHandler != null)
            {
                currentDeviceName = pHandler.selectedMicrophoneDeviceName;
            }
            else if (sttHandler.activeServerType == STTServerType.Whisper && wHandler != null)
            {
                currentDeviceName = wHandler.selectedMicrophoneDeviceName;
            }

            if (!string.IsNullOrEmpty(currentDeviceName))
            {
                for (int i = 0; i < microphoneDevices.Length; i++)
                {
                    if (microphoneDevices[i] == currentDeviceName)
                    {
                        currentDeviceIndex = i;
                        break;
                    }
                }
            }

            if (currentDeviceIndex == -1 && !string.IsNullOrEmpty(currentDeviceName))
            {
                EditorGUILayout.HelpBox($"Previously selected: '{currentDeviceName}' (now disconnected or not found). Please select an available device.", MessageType.Warning);
            }

            int newSelectedDeviceIndex = EditorGUILayout.Popup("Select Microphone", currentDeviceIndex, microphoneDevices);

            if (newSelectedDeviceIndex != currentDeviceIndex)
            {
                string newDevice = string.Empty;
                if (newSelectedDeviceIndex >= 0 && newSelectedDeviceIndex < microphoneDevices.Length)
                {
                    newDevice = microphoneDevices[newSelectedDeviceIndex];
                }

                if (pHandler != null) 
                {
                    Undo.RecordObject(pHandler, "Select Microphone Device"); 
                    pHandler.OnMicrophoneSelectedInEditor(newDevice);
                    EditorUtility.SetDirty(pHandler);
                }

                if (wHandler != null)
                {
                    Undo.RecordObject(wHandler, "Select Microphone Device"); 
                    wHandler.OnMicrophoneSelectedInEditor(newDevice);
                    EditorUtility.SetDirty(wHandler);
                }
            }
        }

        EditorGUILayout.Space(); // Adds a little visual separation

        // --- Draw all other properties ---
        EditorGUILayout.LabelField("Other Settings & Events", EditorStyles.boldLabel);

        // Define a list of property names that we have already handled above
        // or that are handled by specific attributes like [HideInInspector]
        var manuallyHandledProperties = new HashSet<string>
        {
            "m_Script",                         // Default script field, always good to skip explicitly
            "selectedMicrophoneDeviceName",     // We handle this with the dropdown (it's also [HideInInspector])
            "activeMicrophoneDeviceForInfo",     // We draw this manually as read-only info
            "activeServerType"                  // Drawn custom under Server Setup
        };

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true; // To ensure the first property is entered
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false; // Subsequent calls should not re-enter children of the same top-level property

            if (!manuallyHandledProperties.Contains(property.name))
            {
                EditorGUILayout.PropertyField(property, true); // 'true' ensures that children (like lists, UnityEvents) are drawn
            }
        }

        // Always call this at the end
        serializedObject.ApplyModifiedProperties();
    }
}