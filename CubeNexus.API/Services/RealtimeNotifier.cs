using System;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.API.Hubs;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace CubeNexus.API.Services;

public class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<TournamentHub> _hubContext;

    public RealtimeNotifier(IHubContext<TournamentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastRoundStartedAsync(RoundStartedEventDto payload, CancellationToken ct = default)
    {
        try
        {
            var groupName = $"event:{payload.EventId}:round:{payload.RoundNumber}";
            Console.WriteLine($"[Realtime Hub] Broadcasting RoundStarted to {groupName}");
            await _hubContext.Clients.Group(groupName).SendAsync("RoundStarted", payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to broadcast RoundStarted: {ex.Message}");
        }
    }

    public async Task BroadcastResultSubmittedAsync(ResultSubmittedEventDto payload, CancellationToken ct = default)
    {
        try
        {
            var groupName = $"event:{payload.EventId}:round:{payload.RoundNumber}";
            Console.WriteLine($"[Realtime Hub] Broadcasting ResultSubmitted to {groupName}");
            await _hubContext.Clients.Group(groupName).SendAsync("ResultSubmitted", payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to broadcast ResultSubmitted: {ex.Message}");
        }
    }

    public async Task BroadcastResultsLockedAsync(ResultsLockedEventDto payload, CancellationToken ct = default)
    {
        try
        {
            var groupName = $"event:{payload.EventId}:round:{payload.RoundNumber}";
            Console.WriteLine($"[Realtime Hub] Broadcasting ResultsLocked to {groupName}");
            await _hubContext.Clients.Group(groupName).SendAsync("ResultsLocked", payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to broadcast ResultsLocked: {ex.Message}");
        }
    }

    public async Task BroadcastRoundCompletedAsync(RoundCompletedEventDto payload, CancellationToken ct = default)
    {
        try
        {
            var groupName = $"event:{payload.EventId}:round:{payload.RoundNumber}";
            Console.WriteLine($"[Realtime Hub] Broadcasting RoundCompleted to {groupName}");
            await _hubContext.Clients.Group(groupName).SendAsync("RoundCompleted", payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to broadcast RoundCompleted: {ex.Message}");
        }
    }

    public async Task BroadcastResultCorrectedAsync(ResultCorrectedEventDto payload, CancellationToken ct = default)
    {
        try
        {
            var groupName = $"event:{payload.EventId}:round:{payload.RoundNumber}";
            Console.WriteLine($"[Realtime Hub] Broadcasting ResultCorrected to {groupName}");
            await _hubContext.Clients.Group(groupName).SendAsync("ResultCorrected", payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to broadcast ResultCorrected: {ex.Message}");
        }
    }

    public async Task SendStationCommandAsync(Guid eventId, int roundNumber, int stationNumber, string command, object? data = null, CancellationToken ct = default)
    {
        try
        {
            var stationGroup = $"event:{eventId}:round:{roundNumber}:station:{stationNumber}";
            Console.WriteLine($"[Realtime Hub] Sending station command '{command}' to {stationGroup}");
            await _hubContext.Clients.Group(stationGroup).SendAsync("ReceiveStationCommand", new
            {
                Command = command,
                Data = data
            }, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to send station command: {ex.Message}");
        }
    }

    public async Task BroadcastScramblePoolDepletedAsync(object payload, CancellationToken ct = default)
    {
        try
        {
            Console.WriteLine("[Realtime Hub] Broadcasting ScramblePoolDepleted to all clients");
            await _hubContext.Clients.All.SendAsync("ScramblePoolDepleted", payload, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Realtime Hub ERROR] Failed to broadcast ScramblePoolDepleted: {ex.Message}");
        }
    }
}
