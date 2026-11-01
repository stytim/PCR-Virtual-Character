using UnityEngine;

/// <summary>
/// Attach this StateMachineBehaviour to each exercise animation state in the Exercise SSM.
/// 
/// Simplified design: Each animation state is independently reachable via ExerciseIndex.
/// OnStateEnter sets the current index, OnStateExit signals completion.
/// No chaining, no phases, no ProceedToNext.
/// </summary>
public class ExerciseStateBehaviour : StateMachineBehaviour
{
    [Header("Exercise Index")]
    [Tooltip("The animation index of this exercise (1-5).")]
    public int exerciseIndex = 1;
    
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var controller = ExerciseController.Instance;
        if (controller == null || !controller.IsExercising) return;
        
        controller.SetCurrentExerciseIndex(exerciseIndex);
        Debug.Log($"<color=green>[Exercise] Animation {exerciseIndex} STARTED</color>");
    }
    
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // Ensure animator speed is normal
        animator.speed = 1f;
        
        var controller = ExerciseController.Instance;
        if (controller == null || !controller.IsExercising) return;
        
        // Only signal completion if the animation actually played to near-completion.
        // This prevents false triggers from quick transitions or misconfigured exit times.
        if (stateInfo.normalizedTime < 0.8f)
        {
            Debug.LogWarning($"<color=yellow>[Exercise] State {exerciseIndex} exited early (normalizedTime={stateInfo.normalizedTime:F2}), ignoring.</color>");
            return;
        }
        
        Debug.Log($"<color=cyan>[Exercise] Animation {exerciseIndex} FINISHED (normalizedTime={stateInfo.normalizedTime:F2})</color>");
        controller.OnExerciseAnimationCompleted(exerciseIndex);
    }
}
