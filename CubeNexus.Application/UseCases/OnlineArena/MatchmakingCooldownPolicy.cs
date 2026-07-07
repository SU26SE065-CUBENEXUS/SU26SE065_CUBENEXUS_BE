namespace CubeNexus.Application.UseCases.OnlineArena;

/// <summary>
/// Centralized cooldown duration constants for all matchmaking-related penalties.
/// Never hardcode these values in controllers or use cases — always reference this class.
/// </summary>
public static class MatchmakingCooldownPolicy
{
    /// <summary>
    /// Cooldown applied to a player who actively cancels after receiving MATCH_FOUND.
    /// (They declined the match, so a short penalty discourages abuse.)
    /// </summary>
    public static readonly TimeSpan CancelAfterMatchFound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cooldown applied to a player who fails to confirm within the confirmation window.
    /// (They didn't respond — penalize lightly to avoid queue griefing.)
    /// </summary>
    public static readonly TimeSpan FailedToConfirm = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Duration of the confirmation window. Both players must confirm within this window.
    /// </summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(60);
}
