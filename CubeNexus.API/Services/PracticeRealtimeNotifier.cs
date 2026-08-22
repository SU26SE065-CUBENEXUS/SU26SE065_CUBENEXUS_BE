using CubeNexus.Application.DTOs.Practice;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CubeNexus.API.Services;

public class PracticeRealtimeNotifier : IPracticeRealtimeNotifier
{
    private readonly IHubContext<OnlineArenaHub> _hubContext;

    public PracticeRealtimeNotifier(IHubContext<OnlineArenaHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyPracticeAttemptUpdatedAsync(Guid userId, PracticeAttemptResponseDto payload)
    {
        return _hubContext.Clients.Group($"user:{userId}").SendAsync("PracticeAttemptUpdated", payload);
    }

    public Task NotifyPracticeMobileConnectedAsync(Guid userId, Guid sessionId)
    {
        return _hubContext.Clients.Group($"user:{userId}").SendAsync("PracticeMobileConnected", new
        {
            sessionId,
            connectedAt = DateTime.UtcNow
        });
    }

    public Task NotifyPracticeMobileDisconnectedAsync(Guid userId, Guid sessionId)
    {
        return _hubContext.Clients.Group($"user:{userId}").SendAsync("PracticeMobileDisconnected", new
        {
            sessionId,
            disconnectedAt = DateTime.UtcNow
        });
    }

    public Task NotifyPracticeSessionEndedAsync(Guid userId, Guid sessionId)
    {
        return _hubContext.Clients.Group($"user:{userId}").SendAsync("PracticeSessionEnded", new
        {
            sessionId,
            endedAt = DateTime.UtcNow
        });
    }
}
