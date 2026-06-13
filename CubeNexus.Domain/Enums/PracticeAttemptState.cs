namespace CubeNexus.Domain.Enums;

/// <summary>Trạng thái một lượt giải practice theo luồng Stackmat WCA.</summary>
public enum PracticeAttemptState
{
    Scrambled,
    HoldingHands,
    Ready,
    Solving,
    Stopped,
    Completed,
    Aborted
}
