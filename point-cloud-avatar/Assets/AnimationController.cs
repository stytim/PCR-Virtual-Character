using UnityEngine;

public class AnimationController : MonoBehaviour
{
    public AnimationTransitionController transitionController;
    
    // Listen to events
    void Start()
    {
        transitionController.OnTransitionComplete += (index) => {
            Debug.Log($"Finished transition to animation {index}");
        };
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void TriggerWalkTransition()
    {
        // Transition to animation at index 0 with "walk forward" prompt
        transitionController.TransitionToAnimation(0, "walk forward");
    }
    void TriggerByName()
    {
        // Transition by animation name (matches the clip name in the list)
        transitionController.TransitionToAnimation("RunCycle", "start running");
    }
}
