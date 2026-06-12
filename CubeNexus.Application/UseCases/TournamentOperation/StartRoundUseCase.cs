using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class StartRoundUseCase : IStartRoundUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;

    public StartRoundUseCase(IUnitOfWork unitOfWork, IRealtimeNotifier realtimeNotifier)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<StartRoundResponseDto> ExecuteAsync(Guid eventId, int roundNumber, StartRoundRequestDto dto)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == roundNumber);
        if (!groups.Any())
            throw new CustomException("GROUPS_NOT_FOUND", "No groups found for this round.", 400);

        var groupIds = groups.Select(g => g.Id).ToList();
        var scramblesExist = await _unitOfWork.ScrambleSets.AnyAsync(ss => groupIds.Contains(ss.GroupId));
        if (!scramblesExist)
            throw new CustomException("SCRAMBLES_NOT_FOUND", "Scrambles must be generated before starting the round.", 400);

        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId));
        var offRegIds = competitors.Select(c => c.RegistrationEventId).ToList();
        var offlineRegs = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => offRegIds.Contains(ore.Id));
        
        var regIds = offlineRegs.Select(o => o.RegistrationId).ToList();
        var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id));

        var userIds = registrations.Select(r => r.UserId).ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));

        var usersMap = users.ToDictionary(u => u.Id);
        var regMap = registrations.ToDictionary(r => r.Id);
        var offRegMap = offlineRegs.ToDictionary(o => o.Id);
        var groupMap = groups.ToDictionary(g => g.Id);

        var missingCompetitors = new List<MissingCompetitorDto>();
        bool hasCheckedInCompetitor = false;

        foreach (var comp in competitors)
        {
            var offReg = offRegMap[comp.RegistrationEventId];
            var reg = regMap[offReg.RegistrationId];
            var user = usersMap[reg.UserId];
            var group = groupMap[comp.GroupId];

            if (reg.CheckedInAt == null)
            {
                missingCompetitors.Add(new MissingCompetitorDto
                {
                    GroupCompetitorId = comp.Id,
                    CompetitorName = user.DisplayName,
                    GroupName = group.GroupName ?? string.Empty,
                    StationNumber = comp.StationNumber
                });
            }
            else
            {
                hasCheckedInCompetitor = true;
            }
        }

        if (!hasCheckedInCompetitor)
            throw new CustomException("NO_CHECKED_IN_COMPETITORS", "Cannot start round without any checked-in competitors.", 400);

        if (missingCompetitors.Any() && !dto.AllowMissingCompetitors)
        {
            return new StartRoundResponseDto
            {
                Success = false,
                Message = "Missing competitors.",
                MissingCompetitors = missingCompetitors
            };
        }

        // Mark missing as NO_SHOW
        if (dto.AllowMissingCompetitors && missingCompetitors.Any())
        {
            var missingIds = missingCompetitors.Select(m => m.GroupCompetitorId).ToList();
            foreach (var comp in competitors.Where(c => missingIds.Contains(c.Id)))
            {
                comp.StatusCode = GroupCompetitorStatus.NO_SHOW;
                _unitOfWork.GroupCompetitors.Update(comp);
            }
        }

        // Set groups ONGOING
        foreach (var group in groups)
        {
            group.StatusCode = "ONGOING";
            _unitOfWork.Groups.Update(group);
        }

        // Set tournament ONGOING if needed
        var tournament = await _unitOfWork.Tournaments.GetByIdAsync(ev.TournamentId);
        if (tournament != null && tournament.StatusCode != "ONGOING")
        {
            tournament.StatusCode = "ONGOING";
            _unitOfWork.Tournaments.Update(tournament);
        }

        await _unitOfWork.SaveChangesAsync();

        // Broadcast Realtime Event
        var noShowCompetitorIds = competitors
            .Where(c => c.StatusCode == GroupCompetitorStatus.NO_SHOW)
            .Select(c => c.Id)
            .ToList();

        var eventDto = new RoundStartedEventDto
        {
            EventId = eventId,
            RoundNumber = roundNumber,
            RoundStatus = "ONGOING",
            StartedAt = DateTime.UtcNow,
            NoShowCompetitorIds = noShowCompetitorIds
        };

        try
        {
            await _realtimeNotifier.BroadcastRoundStartedAsync(eventDto);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast Stage ERROR] Failed to broadcast round started realtime event: {ex.Message}. EventId={eventId}, RoundNumber={roundNumber}");
        }

        return new StartRoundResponseDto
        {
            Success = true,
            Message = "Round started successfully."
        };
    }
}
