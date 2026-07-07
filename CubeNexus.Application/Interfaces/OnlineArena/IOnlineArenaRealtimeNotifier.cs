namespace CubeNexus.Application.Interfaces.OnlineArena;

public interface IOnlineArenaRealtimeNotifier
{
    // === Matchmaking ===
    Task NotifyMatchmakingQueuedAsync(Guid userId, object payload);
    Task NotifyMatchmakingFoundAsync(Guid player1Id, Guid player2Id, object payload);
    Task NotifyMatchmakingCancelledAsync(Guid userId, object payload);
    /// <summary>Gửi cho user khi bị apply cooldown.</summary>
    Task NotifyMatchmakingCooldownAppliedAsync(Guid userId, object payload);

    // === Match Confirmation (60s window) ===
    /// <summary>
    /// Notify both players when a match is found and confirmation window begins.
    /// Each player receives a separate, personalized payload (with correct opponent info).
    /// SignalR event name: "MatchFound"
    /// </summary>
    Task NotifyMatchFoundAsync(Guid player1Id, Guid player2Id, object player1Payload, object player2Payload);

    /// <summary>
    /// Notify both players when one player confirms (partial confirmation).
    /// Shared payload — both players see the same updated confirmed flags.
    /// SignalR event name: "MatchConfirmationUpdated"
    /// </summary>
    Task NotifyMatchConfirmationUpdatedAsync(Guid player1Id, Guid player2Id, object payload);

    /// <summary>
    /// Notify both players when both have confirmed and an official match is created.
    /// Each player receives a separate payload (meUserId / opponentUserId are swapped).
    /// SignalR event name: "MatchConfirmed"
    /// </summary>
    Task NotifyMatchConfirmedAsync(Guid player1Id, Guid player2Id, object player1Payload, object player2Payload);

    /// <summary>
    /// Notify both players when the confirmation window expired before both confirmed.
    /// Shared payload — includes requeueAvailable flag.
    /// SignalR event name: "MatchConfirmationExpired"
    /// </summary>
    Task NotifyMatchConfirmationExpiredAsync(Guid player1Id, Guid player2Id, object payload);

    /// <summary>
    /// Notify both players when one player actively cancelled during the confirmation window.
    /// Shared payload — includes who cancelled.
    /// SignalR event name: "MatchConfirmationCancelled"
    /// </summary>
    Task NotifyMatchConfirmationCancelledAsync(Guid player1Id, Guid player2Id, object payload);

    // === Room / Setup ===
    Task NotifyMatchJoinedAsync(Guid matchId, object payload);
    Task NotifyCameraReadyUpdatedAsync(Guid matchId, object payload);
    Task NotifyWebRtcConnectionUpdatedAsync(Guid matchId, object payload);
    Task NotifyVideoRecordingStartedAsync(Guid matchId, object payload);
    Task NotifyTimerConnectedAsync(Guid matchId, object payload);
    Task NotifyTimerDisconnectedAsync(Guid matchId, object payload);

    // === Checklist / Ready phase ===
    /// <summary>Gửi khi bất kỳ checklist item nào thay đổi (camera/webrtc/timer/recording/scrambleCheck).</summary>
    Task NotifyChecklistUpdatedAsync(Guid matchId, object payload);
    /// <summary>Gửi khi cả 2 checklistPassed → phase WAITING_READY.</summary>
    Task NotifyMatchWaitingReadyAsync(Guid matchId, object payload);
    Task NotifyReadyStateUpdatedAsync(Guid matchId, object payload);
    Task NotifyMatchReadyAsync(Guid matchId, object payload);

    // === Phase transitions ===
    /// <summary>Phase đã thay đổi — generic event cho frontend re-poll state.</summary>
    Task NotifyMatchPhaseUpdatedAsync(Guid matchId, object payload);
    /// <summary>Cả 2 playerReady → countdown bắt đầu.</summary>
    Task NotifyCountdownStartedAsync(Guid matchId, object payload);
    /// <summary>Countdown xong → match ONGOING + INSPECTION phase.</summary>
    Task NotifyInspectionStartedAsync(Guid matchId, object payload);
    /// <summary>Inspection xong → SOLVING phase.</summary>
    Task NotifySolveStartedAsync(Guid matchId, object payload);

    // === AI Checks ===
    Task NotifyAiCheckStartedAsync(Guid matchId, object payload);
    Task NotifyAiCheckCompletedAsync(Guid matchId, object payload);
    Task NotifyAiCheckFailedAsync(Guid matchId, object payload);
    Task NotifyScrambleRevealedAsync(Guid matchId, object payload);
    Task NotifyScrambleCheckUpdatedAsync(Guid matchId, object payload);
    Task NotifyFinishCheckUpdatedAsync(Guid matchId, object payload);

    // === Results ===
    /// <summary>Một player stop timer và submit result.</summary>
    Task NotifyTimerResultSubmittedAsync(Guid matchId, object payload);
    Task NotifyResultSubmittedAsync(Guid matchId, object payload);
    Task NotifyVideoEvidenceUploadedAsync(Guid matchId, object payload);
    Task NotifyPlayerWaitingOpponentAsync(Guid matchId, object payload);
    Task NotifyMatchStateChangedAsync(Guid matchId, object payload);

    // === Terminal ===
    Task NotifyMatchCompletedAsync(Guid matchId, object payload);
    Task NotifyMatchNeedsReviewAsync(Guid matchId, object payload);
    Task NotifyMatchCancelledAsync(Guid matchId, object payload);

    // === Timeout ===
    /// <summary>Setup timeout → match CANCELLED. Gửi cho cả 2 player + user group của timeout player.</summary>
    Task NotifySetupTimeoutAsync(Guid matchId, object payload);
    /// <summary>Ready timeout → match CANCELLED.</summary>
    Task NotifyReadyTimeoutAsync(Guid matchId, object payload);
    /// <summary>Solve timeout → player bị DNF.</summary>
    Task NotifySolveTimeoutAsync(Guid matchId, object payload);

    // === Fraud ===
    Task NotifyFraudReportCreatedAsync(Guid matchId, object payload);
    Task NotifyFraudReportResolvedAsync(Guid matchId, object payload);
}
