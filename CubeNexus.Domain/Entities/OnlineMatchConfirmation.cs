namespace CubeNexus.Domain.Entities;

/// <summary>
/// Tracks the 60-second confirmation window between two matched players.
/// Status flow: PENDING → CONFIRMED (both confirm) | EXPIRED (timeout) | CANCELLED (one player cancels).
/// OnlineMatch is only created once status reaches CONFIRMED.
/// </summary>
public class OnlineMatchConfirmation
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }

    public Guid Player1UserId { get; set; }
    public Guid Player2UserId { get; set; }

    public bool Player1Confirmed { get; set; } = false;
    public bool Player2Confirmed { get; set; } = false;

    public DateTime ConfirmDeadlineAt { get; set; }

    /// <summary>PENDING | CONFIRMED | EXPIRED | CANCELLED</summary>
    public string Status { get; set; } = "PENDING";

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when both players confirmed and the official OnlineMatch was created.</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Set only after CONFIRMED — the id of the OnlineMatch created for this confirmation.
    /// Null while PENDING. Used as idempotency guard to prevent duplicate match creation.
    /// </summary>
    public Guid? MatchId { get; set; }

    // === Navigation ===
    public PuzzleType PuzzleType { get; set; } = null!;
    public User Player1 { get; set; } = null!;
    public User Player2 { get; set; } = null!;
    public OnlineMatch? Match { get; set; }
}
