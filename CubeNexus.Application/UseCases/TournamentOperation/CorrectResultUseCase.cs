using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Helpers;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Services;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class CorrectResultUseCase : ICorrectResultUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;

    public CorrectResultUseCase(IUnitOfWork unitOfWork, IRealtimeNotifier realtimeNotifier)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<ResultCorrectionResponseDto> ExecuteAsync(Guid resultId, ResultCorrectionDto dto, Guid managerId)
    {
        var result = await _unitOfWork.Results.GetByIdAsync(resultId);
        if (result == null)
            throw new CustomException("RESULT_NOT_FOUND", "Result not found.", 404);

        if (result.IsLocked)
            throw new CustomException("RESULT_LOCKED", "Cannot correct result because it is locked.", 409);

        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new CustomException("CORRECTION_REASON_REQUIRED", "Correction reason is required.", 400);

        PenaltyType? penaltyType = null;
        if (dto.PenaltyTypeId.HasValue)
        {
            penaltyType = await _unitOfWork.PenaltyTypes.GetByIdAsync(dto.PenaltyTypeId.Value);
            if (penaltyType == null)
                throw new CustomException("PENALTY_TYPE_NOT_FOUND", "Penalty type not found.", 404);
        }

        // Validate raw time requirements
        bool isDnf = penaltyType != null && (penaltyType.IsDisqualified || penaltyType.Code == "DNF");
        if (!isDnf)
        {
            if (!dto.RawTimeMs.HasValue || dto.RawTimeMs.Value <= 0)
            {
                throw new CustomException("INVALID_RESULT_CORRECTION", "Raw time is required and must be greater than 0.", 400);
            }
        }

        var oldRawTime = result.RawTimeMs;
        var oldFinalTime = result.FinalTimeMs;
        var oldPenaltyTypeId = result.PenaltyTypeId;
        var oldIsDnf = result.IsDnf;

        result.RawTimeMs = dto.RawTimeMs;
        result.PenaltyTypeId = dto.PenaltyTypeId;

        var calculator = new PenaltyCalculationDomainService();
        calculator.CalculateTraditionalResult(result, penaltyType);

        _unitOfWork.Results.Update(result);

        var auditLog = new ResultAuditLog
        {
            Id = Guid.NewGuid(),
            ResultId = result.Id,
            ChangedBy = managerId,
            OldRawTimeMs = oldRawTime,
            NewRawTimeMs = result.RawTimeMs,
            OldFinalTimeMs = oldFinalTime,
            NewFinalTimeMs = result.FinalTimeMs,
            OldPenaltyTypeId = oldPenaltyTypeId,
            NewPenaltyTypeId = result.PenaltyTypeId,
            OldIsDnf = oldIsDnf,
            NewIsDnf = result.IsDnf,
            Reason = dto.Reason,
            ChangedAt = DateTime.UtcNow
        };
        _unitOfWork.ResultAuditLogs.Add(auditLog);

        await _unitOfWork.SaveChangesAsync();

        // Recalculate LiveBoard & Rank
        var groupCompetitor = await _unitOfWork.GroupCompetitors.GetByIdAsync(result.GroupCompetitorId);
        var group = await _unitOfWork.Groups.GetByIdAsync(groupCompetitor.GroupId);
        var ev = await _unitOfWork.Events.GetByIdAsync(group.EventId);

        var roundGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == ev.Id && g.RoundNumber == group.RoundNumber);
        var roundGroupIds = roundGroups.Select(rg => rg.Id).ToList();
        var roundCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => roundGroupIds.Contains(gc.GroupId));
        var roundCompetitorIds = roundCompetitors.Select(rc => rc.Id).ToList();
        var roundResults = await _unitOfWork.Results.FindAsync(r => roundCompetitorIds.Contains(r.GroupCompetitorId));

        var regEventIds = roundCompetitors.Select(gc => gc.RegistrationEventId).ToList();
        var offlineRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => regEventIds.Contains(ore.Id));
        var regIds = offlineRegEvents.Select(ore => ore.RegistrationId).ToList();
        var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id));
        var userIds = registrations.Select(r => r.UserId).ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));

        var userMap = users.ToDictionary(u => u.Id);
        var regMap = registrations.ToDictionary(r => r.Id);
        var offlineRegEventMap = offlineRegEvents.ToDictionary(ore => ore.Id);

        var penaltyTypesList = await _unitOfWork.PenaltyTypes.GetAllAsync();
        var penaltyTypeMap = penaltyTypesList.ToDictionary(pt => pt.Id);

        var calculatedCompetitors = LiveBoardCalculator.CalculateCompetitors(
            ev.SolveCount,
            roundCompetitors,
            roundResults,
            userMap,
            regMap,
            offlineRegEventMap,
            penaltyTypeMap,
            ev.CutoffTimeMs
        );

        var calculatedComp = calculatedCompetitors.FirstOrDefault(cc => cc.GroupCompetitorId == groupCompetitor.Id);
        var competitorName = userMap.TryGetValue(regMap[offlineRegEventMap[groupCompetitor.RegistrationEventId].RegistrationId].UserId, out var compUser) ? compUser.DisplayName : "Unknown";

        var response = new ResultCorrectionResponseDto
        {
            ResultId = result.Id,
            RawTimeMs = result.RawTimeMs,
            FinalTimeMs = result.FinalTimeMs,
            PenaltyCode = result.PenaltyTypeId.HasValue && penaltyTypeMap.TryGetValue(result.PenaltyTypeId.Value, out var ptCode) ? ptCode.Code : "NONE",
            IsDnf = result.IsDnf,
            IsLocked = result.IsLocked,
            CorrectedAt = auditLog.ChangedAt,
            CorrectedBy = managerId,
            CorrectionReason = auditLog.Reason,
            CompletedSolves = calculatedComp?.CompletedSolves ?? 0,
            SolveCount = ev.SolveCount,
            BestTimeMs = calculatedComp?.BestTimeMs,
            AverageTimeMs = calculatedComp?.AverageTimeMs,
            Rank = calculatedComp?.Rank ?? 0,
            CompetitorStatus = groupCompetitor.StatusCode.ToString()
        };

        try
        {
            var correctedEvent = new ResultCorrectedEventDto
            {
                EventId = ev.Id,
                RoundNumber = group.RoundNumber,
                GroupId = group.Id,
                GroupCompetitorId = groupCompetitor.Id,
                CompetitorName = competitorName,
                Result = new CorrectedResultDto
                {
                    ResultId = result.Id,
                    SolveNumber = result.SolveNumber,
                    RawTimeMs = result.RawTimeMs,
                    FinalTimeMs = result.FinalTimeMs,
                    PenaltyCode = response.PenaltyCode,
                    IsDnf = result.IsDnf,
                    IsLocked = result.IsLocked,
                    CorrectedAt = auditLog.ChangedAt
                },
                Summary = new CorrectedResultSummaryDto
                {
                    CompletedSolves = response.CompletedSolves,
                    SolveCount = response.SolveCount,
                    BestTimeMs = response.BestTimeMs,
                    AverageTimeMs = response.AverageTimeMs,
                    Rank = response.Rank
                }
            };
            await _realtimeNotifier.BroadcastResultCorrectedAsync(correctedEvent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast Stage ERROR] Failed to broadcast ResultCorrected: {ex.Message}");
        }

        return response;
    }
}
