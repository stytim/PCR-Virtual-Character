using UnityEngine;
using System.Collections.Generic; // Required for List

[CreateAssetMenu(fileName = "NewAvatarBehavior", menuName = "Avatar/Behavior Data")]
public class AvatarBehaviorSO : ScriptableObject
{
    [Header("General Settings")]
    public bool lookAtUser = true;
    public bool disableProceduralIK = true; // Check this for Vicon animations
    
    [Header("Animator Control")]
    public int animatorLayer = 0;
    [Tooltip("The main trigger to enter the SSM (e.g., 'EnterTalking').")]
    public string animatorTrigger;
    
    [Header("Blend Tree Randomizer")]
    [Tooltip("The Float parameter in the Blend Tree (e.g., 'GestureVariant').")]
    public string blendParameter;
    
    [Tooltip("Define the Closure Type for each animation in your Blend Tree.\nElement 0 corresponds to Threshold 0, Element 1 to Threshold 1, etc.")]
    public List<AnimationClosureType> blendTreeMapping; 

    [Header("Expressions")]
    public List<AvatarExpression> expressionsToSet;
    
    [Header("Head Movement")]
    public bool headNod;
    public bool headShake;
}