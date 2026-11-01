using UnityEngine;

/// <summary>
/// Attach to a Sub-State Machine. On entry, randomly selects which child state to transition to.
/// Configure the state names in the Inspector - they must match exactly the state names in the SSM.
/// </summary>
public class RandomStateSelector : StateMachineBehaviour
{
    [Tooltip("Names of child states to randomly choose from. Must match state names exactly.")]
    public string[] stateNames;
    
    [Tooltip("Animator parameter name to set for state selection (Integer type).")]
    public string stateIndexParameter = "RandomStateIndex";
    
    private int _lastSelectedIndex = -1;

    public override void OnStateMachineEnter(Animator animator, int stateMachinePathHash)
    {
        if (stateNames == null || stateNames.Length == 0)
        {
            Debug.LogWarning("[RandomStateSelector] No state names configured!");
            return;
        }
        
        // Pick a random index, avoiding the last one if possible
        int selectedIndex;
        if (stateNames.Length == 1)
        {
            selectedIndex = 0;
        }
        else
        {
            int attempts = 0;
            do
            {
                selectedIndex = Random.Range(0, stateNames.Length);
                attempts++;
            } while (selectedIndex == _lastSelectedIndex && attempts < 10);
        }
        
        _lastSelectedIndex = selectedIndex;
        
        // Set the parameter - transitions in Animator should use this to go to correct state
        animator.SetInteger(stateIndexParameter, selectedIndex);
        
        Debug.Log($"<color=cyan>[RandomStateSelector]</color> Selected state index: {selectedIndex} ({stateNames[selectedIndex]})");
    }
    
    public override void OnStateMachineExit(Animator animator, int stateMachinePathHash)
    {
        // Reset the parameter when leaving the SSM
        animator.SetInteger(stateIndexParameter, -1);
    }
}
