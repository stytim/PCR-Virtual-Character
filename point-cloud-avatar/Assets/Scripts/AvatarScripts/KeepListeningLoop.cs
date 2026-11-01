using UnityEngine;

public class KeepListeningLoop : StateMachineBehaviour
{
    [Tooltip("At what point (0-1) should we pick a new listening pose?")]
    public float restartTime = 0.9f;
    
    private bool _hasTriggered = false;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _hasTriggered = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (_hasTriggered) return;

        if (stateInfo.normalizedTime >= restartTime)
        {
            AvatarBehaviorController.Instance.LoopListeningBehavior();
            _hasTriggered = true;
        }
    }
}