using UnityEngine;

/// <summary>
/// Attach to a Blend Tree state. Sets a random INTEGER parameter on state enter,
/// causing the blend tree to select a specific animation (no blending).
/// Thresholds in blend tree should be 0, 1, 2, 3, etc.
/// </summary>
public class AnimatorRandomizer : StateMachineBehaviour
{
    [Tooltip("Float parameter name that controls the blend tree. Will be set to an integer value.")]
    public string blendParameter = "IdleVariant";
    
    [Tooltip("Number of animations in the blend tree (e.g., 7 for 7 idle variants).")]
    public int animationCount = 7;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // Pick a random integer from 0 to (animationCount - 1)
        int randomIndex = Random.Range(0, animationCount);
        
        // Set as float but with integer value - no blending occurs at exact threshold
        animator.SetFloat(blendParameter, randomIndex);
        
        Debug.Log($"<color=magenta>[AnimatorRandomizer]</color> Set {blendParameter} = {randomIndex}");
    }
}
