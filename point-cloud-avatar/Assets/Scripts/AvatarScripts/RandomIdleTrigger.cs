using UnityEngine;

public class RandomIdleTrigger : StateMachineBehaviour
{
    [Header("Settings")]
    public float minWait = 3f;
    public float maxWait = 8f;
    public string parameterName = "IdleIndex"; // Int parameter
    public int fillerCount = 7; // How many fillers you have

    private float _timer;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // Reset timer and ensure we are "clean" (Index 0)
        _timer = Random.Range(minWait, maxWait);
        animator.SetInteger(parameterName, 0);
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // If we are transitioning OUT, do nothing (prevent double-firing)
        if (animator.IsInTransition(layerIndex)) return;

        _timer -= Time.deltaTime;

        if (_timer <= 0f)
        {
            // Pick a random filler (1 to 7)
            int randomFiller = Random.Range(1, fillerCount + 1);
            animator.SetInteger(parameterName, randomFiller);
        }
    }
}