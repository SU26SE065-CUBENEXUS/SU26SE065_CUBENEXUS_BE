using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class CompleteEventUseCase : ICompleteEventUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CompleteEventUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationResultDto> ExecuteAsync(Guid eventId)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        // Check if already completed (using CLOSED registration status code as event-completion indicator)
        if (ev.RegistrationStatusCode == "CLOSED")
        {
            throw new CustomException("EVENT_ALREADY_COMPLETED", "This event is already completed.", 409);
        }

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId);
        if (!groups.Any())
        {
            throw new CustomException("GROUPS_NOT_FOUND", "No groups or rounds found for this event.", 400);
        }

        // Validate that all groups/rounds are COMPLETED
        var incompleteGroups = groups.Where(g => g.StatusCode != "COMPLETED").ToList();
        if (incompleteGroups.Any())
        {
            var incompleteRoundList = incompleteGroups.Select(g => new {
                roundNumber = g.RoundNumber,
                groupId = g.Id,
                groupName = g.GroupName ?? string.Empty,
                statusCode = g.StatusCode
            }).ToList();

            var extraData = new Dictionary<string, object>
            {
                { "incompleteRounds", incompleteRoundList }
            };

            throw new CustomException(
                "EVENT_NOT_READY_TO_COMPLETE", 
                "Cannot complete event because some rounds are not completed yet.", 
                409, 
                extraData
            );
        }

        // Validate that all results for the final/current active round are locked
        var finalRoundNumber = groups.Max(g => g.RoundNumber);
        var finalRoundGroups = groups.Where(g => g.RoundNumber == finalRoundNumber).Select(g => g.Id).ToList();
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => finalRoundGroups.Contains(gc.GroupId));
        var competitorIds = competitors.Select(c => c.Id).ToList();
        var results = await _unitOfWork.Results.FindAsync(r => competitorIds.Contains(r.GroupCompetitorId));
        if (results.Any(r => !r.IsLocked))
        {
            throw new CustomException("RESULTS_NOT_LOCKED", "Cannot complete event because some results in the final round are not locked.", 409);
        }

        // Mark event as closed/completed
        ev.RegistrationStatusCode = "CLOSED";
        _unitOfWork.Events.Update(ev);

        await _unitOfWork.SaveChangesAsync();

        return new OperationResultDto
        {
            Success = true,
            Message = "Event completed successfully."
        };
    }
}
