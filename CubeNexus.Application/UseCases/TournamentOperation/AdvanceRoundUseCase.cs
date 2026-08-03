using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Helpers;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using CubeNexus.Domain.Services;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class AdvanceRoundUseCase : IAdvanceRoundUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly GroupAssignmentDomainService _groupAssignmentService;

    public AdvanceRoundUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        _groupAssignmentService = new GroupAssignmentDomainService();
    }

    public async Task<OperationResultDto> ExecuteAsync(Guid eventId, int currentRoundNumber, AdvanceRoundRequestDto dto)
    {
        if (dto.TopN <= 0 || dto.CompetitorsPerGroup <= 0 || dto.StationCount <= 0)
            throw new CustomException("INVALID_INPUT", "TopN, CompetitorsPerGroup, and StationCount must be greater than 0.", 400);

        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        if (ev.RegistrationStatusCode == "COMPLETED")
            throw new CustomException("EVENT_ALREADY_COMPLETED", "Cannot advance round because the event is already completed.", 409);

        var nextRoundGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == dto.NextRoundNumber);
        if (nextRoundGroups.Any())
            throw new CustomException("NEXT_ROUND_ALREADY_EXISTS", $"Round {dto.NextRoundNumber} already exists.", 409);

        var currentGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == currentRoundNumber);
        if (!currentGroups.Any())
            throw new CustomException("CURRENT_ROUND_NOT_FOUND", "Current round groups not found.", 404);

        var statuses = currentGroups.Select(g => g.StatusCode).Distinct().ToList();
        if (statuses.Count > 1 || statuses[0] != "COMPLETED")
            throw new CustomException("INVALID_ROUND_STATE", "Current round must be completely COMPLETED.", 409);

        var groupIds = currentGroups.Select(g => g.Id).ToList();
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId));
        var competitorIds = competitors.Select(c => c.Id).ToList();

        var results = await _unitOfWork.Results.FindAsync(r => competitorIds.Contains(r.GroupCompetitorId));
        if (results.Any() && !results.All(r => r.IsLocked))
            throw new CustomException("RESULTS_NOT_LOCKED", "All results in the current round must be locked before advancing.", 409);

        var penaltyTypes = await _unitOfWork.PenaltyTypes.GetAllAsync();
        var penaltyTypeMap = penaltyTypes.ToDictionary(pt => pt.Id);

        var regEventIds = competitors.Select(gc => gc.RegistrationEventId).ToList();
        var offlineRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => regEventIds.Contains(ore.Id));
        var regIds = offlineRegEvents.Select(ore => ore.RegistrationId).ToList();
        var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id));
        var userIds = registrations.Select(r => r.UserId).ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));

        var userMap = users.ToDictionary(u => u.Id);
        var regMap = registrations.ToDictionary(r => r.Id);
        var offlineRegEventMap = offlineRegEvents.ToDictionary(ore => ore.Id);

        // Filter and compute base rankings
        var calculatedCompetitors = LiveBoardCalculator.CalculateCompetitors(
            ev.SolveCount,
            competitors,
            results.ToList(),
            userMap,
            regMap,
            offlineRegEventMap,
            penaltyTypeMap,
            ev.CutoffTimeMs
        );

        // Exclude NO_SHOW, non-COMPLETED, and cutoff-stopped competitors
        // IsCutoffReached = true means the competitor FAILED to pass cutoff (was stopped early)
        // → they must NOT advance to the next round
        var eligibleCompetitors = calculatedCompetitors
            .Where(c => c.CompetitorStatus == "COMPLETED"
                     && c.CompletedSolves >= ev.SolveCount
                     && !c.IsCutoffReached)
            .ToList();

        // Advanced Tie-Break Logic
        // Sort by Average ASC, then Best ASC, then SeedTimeMs ASC, then SubmittedAt ASC, then Id
        var competitorEntities = competitors.ToDictionary(c => c.Id);
        var resultListByComp = results.GroupBy(r => r.GroupCompetitorId).ToDictionary(g => g.Key, g => g.ToList());

        var sortedEligible = eligibleCompetitors
            .OrderBy(c => c.AverageTimeMs ?? int.MaxValue)
            .ThenBy(c => c.BestTimeMs ?? int.MaxValue)
            .ThenBy(c => {
                var compEntity = competitorEntities[c.GroupCompetitorId];
                var offReg = offlineRegEventMap[compEntity.RegistrationEventId];
                return offReg.SeedTimeMs ?? int.MaxValue;
            })
            .ThenBy(c => {
                var compResults = resultListByComp.TryGetValue(c.GroupCompetitorId, out var rList) ? rList : new List<Result>();
                return compResults.Any() ? compResults.Max(r => r.SubmittedAt) : DateTime.MaxValue;
            })
            .ThenBy(c => c.GroupCompetitorId)
            .ToList();

        var advancedCompetitors = sortedEligible.Take(dto.TopN).ToList();

        if (!advancedCompetitors.Any())
            throw new CustomException("NO_ELIGIBLE_COMPETITORS", "No eligible competitors found to advance.", 400);

        var advancedOffRegs = advancedCompetitors.Select(c => {
            var compEntity = competitorEntities[c.GroupCompetitorId];
            return offlineRegEventMap[compEntity.RegistrationEventId];
        }).ToList();

        // Generate Groups
        var assignments = _groupAssignmentService.AssignGroups(
            eventId,
            dto.NextRoundNumber,
            advancedOffRegs,
            dto.CompetitorsPerGroup,
            dto.StationCount
        );

        var groupMap = new Dictionary<int, Group>();
        var newGroups = new List<Group>();
        var newGroupCompetitors = new List<GroupCompetitor>();

        foreach (var assignment in assignments)
        {
            if (!groupMap.TryGetValue(assignment.GroupNumber, out var group))
            {
                group = new Group
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    RoundNumber = dto.NextRoundNumber,
                    GroupName = assignment.GroupName,
                    StatusCode = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };
                groupMap[assignment.GroupNumber] = group;
                newGroups.Add(group);
            }

            var gc = new GroupCompetitor
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                RegistrationEventId = assignment.RegistrationEvent.Id,
                StationNumber = assignment.StationNumber
            };
            newGroupCompetitors.Add(gc);
        }

        _unitOfWork.Groups.AddRange(newGroups);
        _unitOfWork.GroupCompetitors.AddRange(newGroupCompetitors);

        await _unitOfWork.SaveChangesAsync();

        return new OperationResultDto
        {
            Success = true,
            Message = $"Successfully advanced {advancedCompetitors.Count} competitors to round {dto.NextRoundNumber}."
        };
    }
}
