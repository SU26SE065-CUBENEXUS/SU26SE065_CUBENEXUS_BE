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
}
