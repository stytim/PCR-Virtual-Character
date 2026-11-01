using UnityEngine;

public class KeepTalkingLoop : StateMachineBehaviour
{
    [Tooltip("At what normalized time (0-1) should we pick a new random gesture?")]
    public float restartTime = 0.9f;
    
    private bool _hasTriggered = false;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _hasTriggered = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_hasTriggered) return;

        // When the current gesture is ending...
        if (stateInfo.normalizedTime >= restartTime)
        {
            // Ask Controller to spin the wheel and restart THIS state
            AvatarBehaviorController.Instance.LoopTalkingBehavior();
            _hasTriggered = true;
        }
    }
}