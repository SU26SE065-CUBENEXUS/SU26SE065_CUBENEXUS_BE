namespace CubeNexus.Application.Interfaces.OnlineArena;

public interface IOnlineArenaRealtimeNotifier
{
    Task NotifyMatchmakingQueuedAsync(Guid userId, object payload);
    Task NotifyMatchmakingFoundAsync(Guid player1Id, Guid player2Id, object payload);
    Task NotifyMatchmakingCancelledAsync(Guid userId, object payload);
    Task NotifyMatchJoinedAsync(Guid matchId, object payload);
    Task NotifyCameraReadyUpdatedAsync(Guid matchId, object payload);
    Task NotifyWebRtcConnectionUpdatedAsync(Guid matchId, object payload);
    Task NotifyVideoRecordingStartedAsync(Guid matchId, object payload);
    Task NotifyTimerConnectedAsync(Guid matchId, object payload);
    Task NotifyTimerDisconnectedAsync(Guid matchId, object payload);
    Task NotifyReadyStateUpdatedAsync(Guid matchId, object payload);
    Task NotifyMatchReadyAsync(Guid matchId, object payload);
    Task NotifyAiCheckStartedAsync(Guid matchId, object payload);
    Task NotifyAiCheckCompletedAsync(Guid matchId, object payload);
    Task NotifyAiCheckFailedAsync(Guid matchId, object payload);
    Task NotifyScrambleRevealedAsync(Guid matchId, object payload);
    Task NotifyScrambleCheckUpdatedAsync(Guid matchId, object payload);
    Task NotifyFinishCheckUpdatedAsync(Guid matchId, object payload);
    Task NotifyResultSubmittedAsync(Guid matchId, object payload);
    Task NotifyVideoEvidenceUploadedAsync(Guid matchId, object payload);
    Task NotifyMatchCompletedAsync(Guid matchId, object payload);
    Task NotifyMatchNeedsReviewAsync(Guid matchId, object payload);
    Task NotifyMatchCancelledAsync(Guid matchId, object payload);
    Task NotifyFraudReportCreatedAsync(Guid matchId, object payload);
    Task NotifyFraudReportResolvedAsync(Guid matchId, object payload);
}
