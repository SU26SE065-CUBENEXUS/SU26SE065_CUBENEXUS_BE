using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Reflection;

namespace CubeNexus.API.Services;

public class OnlineArenaRealtimeNotifier : IOnlineArenaRealtimeNotifier
{
    private readonly IHubContext<OnlineArenaHub> _hubContext;

    public OnlineArenaRealtimeNotifier(IHubContext<OnlineArenaHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyMatchmakingQueuedAsync(Guid userId, object payload)
        => _hubContext.Clients.Group($"user:{userId}").SendAsync("MatchmakingQueued", payload);

    public Task NotifyMatchmakingFoundAsync(Guid player1Id, Guid player2Id, object payload)
    {
        var player1Payload = GetPropertyValue(payload, "player1Payload") ?? payload;
        var player2Payload = GetPropertyValue(payload, "player2Payload") ?? payload;

        var t1 = _hubContext.Clients.Group($"user:{player1Id}").SendAsync("MatchmakingFound", player1Payload);
        var t2 = _hubContext.Clients.Group($"user:{player2Id}").SendAsync("MatchmakingFound", player2Payload);
        return Task.WhenAll(t1, t2);
    }

    public Task NotifyMatchmakingCancelledAsync(Guid userId, object payload)
        => _hubContext.Clients.Group($"user:{userId}").SendAsync("MatchmakingCancelled", payload);

    public Task NotifyMatchJoinedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("MatchJoined", payload);

    public Task NotifyCameraReadyUpdatedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("CameraReadyUpdated", payload);

    public Task NotifyWebRtcConnectionUpdatedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("WebRtcConnectionUpdated", payload);

    public Task NotifyVideoRecordingStartedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("VideoRecordingStarted", payload);

    public Task NotifyTimerConnectedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("TimerConnected", payload);

    public Task NotifyTimerDisconnectedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("TimerDisconnected", payload);

    public Task NotifyReadyStateUpdatedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("ReadyStateUpdated", payload);

    public Task NotifyMatchReadyAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("MatchReady", payload);

    public Task NotifyAiCheckStartedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("AiCheckStarted", payload);

    public Task NotifyAiCheckCompletedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("AiCheckCompleted", payload);

    public Task NotifyAiCheckFailedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("AiCheckFailed", payload);

    public Task NotifyScrambleRevealedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("ScrambleRevealed", payload);

    public Task NotifyScrambleCheckUpdatedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("ScrambleCheckUpdated", payload);

    public Task NotifyFinishCheckUpdatedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("FinishCheckUpdated", payload);

    public Task NotifyResultSubmittedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("ResultSubmitted", payload);

    public Task NotifyVideoEvidenceUploadedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("VideoEvidenceUploaded", payload);

    public Task NotifyMatchCompletedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("MatchCompleted", payload);

    public Task NotifyMatchNeedsReviewAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("MatchNeedsReview", payload);

    public Task NotifyMatchCancelledAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("MatchCancelled", payload);

    public Task NotifyFraudReportCreatedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("FraudReportCreated", payload);

    public Task NotifyFraudReportResolvedAsync(Guid matchId, object payload)
        => _hubContext.Clients.Group($"online-match:{matchId}").SendAsync("FraudReportResolved", payload);

    private static object? GetPropertyValue(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return property?.GetValue(source);
    }
}
