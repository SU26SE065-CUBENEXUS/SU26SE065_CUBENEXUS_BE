using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class LockRoundResultsUseCase : ILockRoundResultsUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;

    public LockRoundResultsUseCase(IUnitOfWork unitOfWork, IRealtimeNotifier realtimeNotifier)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<OperationResultDto> ExecuteAsync(Guid eventId, int roundNumber)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == roundNumber);
        if (!groups.Any())
            throw new CustomException("GROUPS_NOT_FOUND", "No groups found for this round.", 400);

        // Check for mismatched statuses within the same round
        var statuses = groups.Select(g => g.StatusCode).Distinct().ToList();
        if (statuses.Count > 1)
        {
            throw new CustomException("INVALID_ROUND_STATE", "The round is in an invalid state because groups have mismatched statuses.", 400);
        }

        var currentStatus = statuses[0];

        // Validate round status
        if (currentStatus == "COMPLETED")
        {
            throw new CustomException("ROUND_ALREADY_COMPLETED", "Cannot lock results because the round has already been completed.", 409);
        }
        if (currentStatus == "LOCKED")
        {
            throw new CustomException("RESULTS_ALREADY_LOCKED", "Results for this round are already locked.", 409);
        }
        if (currentStatus != "ONGOING")
        {
            throw new CustomException("INVALID_ROUND_STATE", $"Cannot lock results in current round state: {currentStatus}.", 400);
        }

        // Validate competitors completeness
        var groupIds = groups.Select(g => g.Id).ToList();
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId));

        if (!competitors.Any())
        {
            throw new CustomException("ROUND_NOT_READY_TO_LOCK", "No competitors found in this round.", 409);
        }

        var competitorIds = competitors.Select(c => c.Id).ToList();
        var results = await _unitOfWork.Results.FindAsync(r => competitorIds.Contains(r.GroupCompetitorId));
        var resultsByCompetitor = results.GroupBy(r => r.GroupCompetitorId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var comp in competitors)
        {
            if (comp.StatusCode != GroupCompetitorStatus.COMPLETED && comp.StatusCode != GroupCompetitorStatus.NO_SHOW)
            {
                throw new CustomException("ROUND_NOT_READY_TO_LOCK", "Cannot lock results because some competitors have not completed their solves yet.", 409);
            }

            if (comp.StatusCode == GroupCompetitorStatus.COMPLETED)
            {
                var compResults = resultsByCompetitor.TryGetValue(comp.Id, out var rList) ? rList : new List<Result>();
                if (compResults.Count < ev.SolveCount)
                {
                    throw new CustomException("ROUND_NOT_READY_TO_LOCK", "Cannot lock results because some competitors have not completed their solves yet.", 409);
                }
            }
        }

        // Lock all results
        foreach (var result in results)
        {
            if (!result.IsLocked)
            {
                result.IsLocked = true;
                _unitOfWork.Results.Update(result);
            }
        }

        // Set all groups status to LOCKED
        foreach (var group in groups)
        {
            group.StatusCode = "LOCKED";
            _unitOfWork.Groups.Update(group);
        }

        await _unitOfWork.SaveChangesAsync();

        // Broadcast Realtime Event
        try
        {
            await _realtimeNotifier.BroadcastResultsLockedAsync(new ResultsLockedEventDto
            {
                EventId = eventId,
                RoundNumber = roundNumber,
                LockedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast Stage ERROR] Failed to broadcast ResultsLocked: {ex.Message}");
        }

        return new OperationResultDto
        {
            Success = true,
            Message = "All results for the round have been locked successfully."
        };
    }
}

public class CompleteRoundUseCase : ICompleteRoundUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;

    public CompleteRoundUseCase(IUnitOfWork unitOfWork, IRealtimeNotifier realtimeNotifier)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<OperationResultDto> ExecuteAsync(Guid eventId, int roundNumber)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == roundNumber);
        if (!groups.Any())
            throw new CustomException("GROUPS_NOT_FOUND", "No groups found for this round.", 400);

        // Check for mismatched statuses within the same round
        var statuses = groups.Select(g => g.StatusCode).Distinct().ToList();
        if (statuses.Count > 1)
        {
            throw new CustomException("INVALID_ROUND_STATE", "The round is in an invalid state because groups have mismatched statuses.", 400);
        }

        var currentStatus = statuses[0];

        // Idempotency: ROUND_ALREADY_COMPLETED first
        if (currentStatus == "COMPLETED")
        {
            throw new CustomException("ROUND_ALREADY_COMPLETED", "This round is already completed.", 409);
        }

        // Must be LOCKED to complete
        if (currentStatus != "LOCKED")
        {
            throw new CustomException("ROUND_NOT_LOCKED", "Cannot complete round before locking results.", 409);
        }

        // Check if all competitors are COMPLETED or NO_SHOW
        var groupIds = groups.Select(g => g.Id).ToList();
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId));
        var incompleteCompetitors = competitors.Where(c => c.StatusCode != GroupCompetitorStatus.COMPLETED && c.StatusCode != GroupCompetitorStatus.NO_SHOW).ToList();
        if (incompleteCompetitors.Any())
            throw new CustomException("ROUND_NOT_READY_TO_COMPLETE", "All competitors must be completed or marked as no-show before completing the round.", 409);

        foreach (var group in groups)
        {
            group.StatusCode = "COMPLETED";
            _unitOfWork.Groups.Update(group);
        }

        await _unitOfWork.SaveChangesAsync();

        // Broadcast Realtime Event
        try
        {
            await _realtimeNotifier.BroadcastRoundCompletedAsync(new RoundCompletedEventDto
            {
                EventId = eventId,
                RoundNumber = roundNumber,
                RoundStatus = "COMPLETED",
                CompletedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast Stage ERROR] Failed to broadcast RoundCompleted: {ex.Message}");
        }

        return new OperationResultDto
        {
            Success = true,
            Message = "Round completed successfully."
        };
    }
}
