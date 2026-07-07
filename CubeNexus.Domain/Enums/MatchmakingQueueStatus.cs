namespace CubeNexus.Domain.Enums;

public enum MatchmakingQueueStatus
{
    QUEUED,
    /// <summary>Match found, waiting for both players to confirm. Player is not searchable in the pool.</summary>
    CONFIRMING,
    MATCHED,
    CANCELLED
}
