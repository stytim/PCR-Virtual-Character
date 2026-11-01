using UnityEngine;

public class TransitionToTalking : StateMachineBehaviour
{
    [Tooltip("At what normalized time (0-1) should we switch to normal talking?")]
    public float switchTime = 0.9f;
    
    private bool _hasTriggered = false;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _hasTriggered = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_hasTriggered) return;
    
        // When Disagree animation is 90% done...
        if (stateInfo.normalizedTime >= switchTime)
        {
            // Tell the Controller to force-switch to the "Explain" behavior
            AvatarBehaviorController.Instance.SwitchToExplainLoop();
            _hasTriggered = true;
        }
    }
}