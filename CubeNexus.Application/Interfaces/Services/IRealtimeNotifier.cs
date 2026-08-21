using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.Services;

public interface IRealtimeNotifier
{
    Task BroadcastRoundStartedAsync(RoundStartedEventDto payload, CancellationToken ct = default);
    Task BroadcastResultSubmittedAsync(ResultSubmittedEventDto payload, CancellationToken ct = default);
    Task BroadcastResultsLockedAsync(ResultsLockedEventDto payload, CancellationToken ct = default);
    Task BroadcastRoundCompletedAsync(RoundCompletedEventDto payload, CancellationToken ct = default);
    Task BroadcastResultCorrectedAsync(ResultCorrectedEventDto payload, CancellationToken ct = default);
    Task SendStationCommandAsync(Guid eventId, int roundNumber, int stationNumber, string command, object? data = null, CancellationToken ct = default);
    Task BroadcastScramblePoolDepletedAsync(object payload, CancellationToken ct = default);
    Task BroadcastAdminNotificationAsync(object payload, CancellationToken ct = default);
}
