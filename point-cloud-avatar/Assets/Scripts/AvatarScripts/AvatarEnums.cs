/// <summary>
/// A central location for defining all avatar-related enums for type-safe operations.
/// </summary>
public enum AvatarAnimation
{
    None, // Represents no animation
    
    // === LEGACY (kept for backwards compatibility) ===
    ThinkingAnim,
    SittingAnim,
    DoubtAnim,
    RelievedAnim,
    ThankfulAnim,
    DisagreeAnim,
    HandsForwardAnim,
    
    // === VICON SEATED ANIMATIONS ===
    SeatedIdle,           // SittingIdle.fbx
    SeatedIdleVariant,    // SittingIdle001.fbx
    
    // Active Listening (pool for ActiveListen behavior)
    ActiveListening,
    ActiveListening001,
    ActiveListening002,
    ActiveListening003,
    ActiveListening004,
    
    // Talking - Both Hands (pool for Explain behavior)
    TalkingBothHands,
    TalkingBothHands001,
    TalkingBothHandsClosure,
    
    // Talking - One Hand Left (pool for Explain behavior)
    TalkingOneHandLeft,
    TalkingOneHandLeft001,
    TalkingOneHandLeft002,
    TalkingOneHandLeftClosure,
    
    // Talking - One Hand Right (pool for Explain behavior)
    TalkingOneHandRight,
    TalkingOneHandRight001,
    TalkingOneHandRight002,
    TalkingOneHandRightClosure,
    
    // Talking - Special variants
    TalkingDisagree,
    TalkingDisagreeEmpathetic,
    TalkingDisagreeEmpathetic001,
    TalkingDisagreeEmpathetic002,
    TalkingHandsCounting,
    TalkingHandsPointing,
    
    // Listening
    ListeningDisagree,
    
    // Filler Animations (pool for FillerIdle behavior)
    Filler001,
    Filler002,
    Filler003,
    Filler004Watch,
    Filler005WeightShift,
    Filler006WeightShift,
    
    // Gestures
    GestureBeckonSitDown,
    GestureBeckonSitDown001,
    
    // Phone Gestures
    Phone002,
    Phone003,
    Phone004,
    
    // === HMD INTERACTIONS (commented - for future project) ===
    // HMDSittingPutAndAdjust,
    // HMDSittingPutAndAdjust2,
    // HMDSittingPutNoAdjust,
    // HMDSittingTakeOff,
    // HMDSittingTakeOff2,
}

public enum AvatarExpression
{
    None, // Represents no expression
    Focus,
    Scrunch,
    BrowUp,
    Soften,
    UnderstandingSmile,
    Unhappy,
}

public enum AvatarBehavior
{
    None, // Represents no specific behavior
    
    // === EXISTING BEHAVIORS (will be remapped to Vicon animations) ===
    Think,            // Keep - may use filler or specific animation
    Doubt,            // Keep
    Idle,             // RE-MAP → SeatedIdle (Vicon)
    UnderstandingNod, // Keep - uses head nod system
    DisagreeEmpathetic, // Keep - uses head shake system
    Relieved,         // Keep
    Thankful,         // Keep
    Disagree,         // RE-MAP → TalkingDisagree or TalkingDisagreeEmpathetic
    Agree,            // Keep
    Explain,          // RE-MAP → Random from Talking pools
    
    // === NEW BEHAVIORS (Vicon-specific) ===
    ActiveListen,     // Engaged listening with subtle movement
    FillerIdle,       // Subtle filler animation between actions
    Counting,         // Counting gesture (TalkingHandsCounting)
    Pointing,         // Pointing gesture (TalkingHandsPointing)
    BeckonSitDown,    // Gesture to invite someone to sit
    PhoneTalking,
    PhoneClosing,
    
    // === PHYSICAL THERAPY / EXERCISE ===
    Exercise,         // Physical therapy exercise demonstration
}
