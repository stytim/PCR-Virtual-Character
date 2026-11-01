using UnityEngine;

/// <summary>
/// Attach to looping idle states. After a random delay, triggers transition to next state.
/// This creates natural variation in how long the character stays in each idle pose.
/// </summary>
public class IdleTimeVariation : StateMachineBehaviour
{
    [Tooltip("Minimum time to stay in this state before transitioning.")]
    public float minDuration = 3f;
    
    [Tooltip("Maximum time to stay in this state before transitioning.")]
    public float maxDuration = 8f;
    
    [Tooltip("Trigger to set when it's time to transition.")]
    public string transitionTrigger = "NextIdle";
    
    private float _timeInState;
    private float _targetDuration;
    private bool _hasTriggered;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _timeInState = 0f;
        _targetDuration = Random.Range(minDuration, maxDuration);
        _hasTriggered = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_hasTriggered) return;
        
        _timeInState += Time.deltaTime;
        
        if (_timeInState >= _targetDuration)
        {
            animator.SetTrigger(transitionTrigger);
            _hasTriggered = true;
            Debug.Log($"<color=green>[IdleTimeVariation]</color> Duration {_targetDuration:F1}s reached, triggering: {transitionTrigger}");
        }
    }
}
