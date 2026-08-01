using Microsoft.AspNetCore.SignalR;

namespace CubeNexus.API.Hubs;

public class TournamentHub : Hub
{
    // Clients can join specific tournament or event groups to receive targeted updates
    public async Task JoinTournamentGroup(string tournamentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Tournament_{tournamentId}");
    }

    public async Task LeaveTournamentGroup(string tournamentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Tournament_{tournamentId}");
    }

    public async Task JoinEventRound(string eventId, int roundNumber)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"event:{eventId}:round:{roundNumber}");
    }

    public async Task LeaveEventRound(string eventId, int roundNumber)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"event:{eventId}:round:{roundNumber}");
    }

    public async Task RegisterJudgeStation(string eventId, int roundNumber, int stationNumber)
    {
        var groupName = $"event:{eventId}:round:{roundNumber}:station:{stationNumber}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        await Clients.Group($"event:{eventId}:round:{roundNumber}").SendAsync("StationOnlineStatusChanged", stationNumber, true);
    }

    public async Task UpdateStationState(string eventId, int roundNumber, int stationNumber, string state, string? competitorName)
    {
        var roundGroupName = $"event:{eventId}:round:{roundNumber}";
        await Clients.Group(roundGroupName).SendAsync("StationStateChanged", new {
            StationNumber = stationNumber,
            State = state,
            CompetitorName = competitorName
        });
    }

    // Register a judge station to its specific station group for commands (e.g. LOCK_STATION)
    public async Task RegisterJudgeStation(string eventId, int roundNumber, int stationNumber)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"station:{eventId}:round:{roundNumber}:station:{stationNumber}");
    }

    // Broadcast station state updates to the event group (Live Board, Manager, Admin)
    public async Task UpdateStationState(string eventId, int roundNumber, int stationNumber, string state, string competitorName)
    {
        await Clients.Group($"event:{eventId}:round:{roundNumber}")
            .SendAsync("ReceiveStationStateUpdate", new
            {
                EventId = eventId,
                RoundNumber = roundNumber,
                StationNumber = stationNumber,
                State = state,
                CompetitorName = competitorName
            });
    }

    // Competitor subscribes to their own personal channel to receive check-in notification
    // Group key: "competitor:{registrationId}"
    public async Task RegisterCompetitor(string registrationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"competitor:{registrationId}");
    }

    public async Task LeaveCompetitorGroup(string registrationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"competitor:{registrationId}");
    }
}
