using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class CompleteOnlineMatchScannerUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeMatchUseCase;

    public CompleteOnlineMatchScannerUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeMatchUseCase)
    {
        _matchRepo = matchRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _uow = uow;
        _completeMatchUseCase = completeMatchUseCase;
    }

    public async Task<object> ExecuteAsync(
        Guid matchId,
        Guid userId,
        string validationType,
        OnlineArenaScannerCompleteRequest request)
    {
        if (request.Observations == null || request.Observations.Count < 5)
        {
            throw new ArgumentException("Request must contain at least 5 observations.");
        }

        var match = await OnlineArenaScannerFlow.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        OnlineArenaScannerFlow.ValidateScannerPhase(match, userId, validationType);
        
        var state = OnlineArenaScannerFlow.RequireScannerState(match, userId, validationType);
        
        if (!string.Equals(state.ScanSessionId, request.ScanSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Scan session mismatch.");
        }

        // 1. Clear existing faces
        state.Faces.Clear();
        state.ScanGeneration = request.ScanGeneration;
        state.RequestId = request.RequestId;
        state.UpdatedAt = DateTime.UtcNow;

        // 2. Loop through all observations and add them to session state
        foreach (var observation in request.Observations)
        {
            if (observation.ScannerState != "ACCEPTED")
            {
                throw new InvalidOperationException("All submitted observations must be in ACCEPTED state.");
            }
            OnlineArenaScannerFlow.AcceptFace(state, observation);
        }

        if (state.Faces.Count < 5)
        {
            throw new InvalidOperationException("Failed to register at least 5 distinct faces.");
        }

        state.ScanStatus = "COMPLETED";
        state.ScannerState = "ACCEPTED";
        state.RequestedFaceIndex = Math.Min(state.Faces.Count, 6);
        state.LastObservation = OnlineArenaScannerFlow.ToObservationState(request.Observations.Last());

        // 3. Complete validation checks
        var validation = OnlineArenaScannerFlow.CompleteValidation(match, userId, validationType, state);
        OnlineArenaScannerFlow.ApplyScannerState(match, userId, validationType, state);
        _matchRepo.Update(match);

        // 4. Audit log entry
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, $"{validationType}_SCANNER_COMPLETED", new
        {
            state.ScanSessionId,
            validationType,
            validation
        }));

        // 5. Save changes atomically
        await _uow.SaveChangesAsync();

        // 6. Broadcast completion update
        var completedResponse = OnlineArenaScannerFlow.BuildScannerResponse(match, userId, state, validation.Matched
            ? $"{validationType} scanner validation passed."
            : $"{validationType} scanner validation requires retry.");

        await OnlineArenaScannerFlow.NotifyScannerUpdatedAsync(_notifier, match.Id, validationType, completedResponse);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, completedResponse.Message));

        var isP1 = match.Player1Id == userId;
        var normValidationType = OnlineArenaScannerFlow.NormalizeValidationType(validationType);

        if (normValidationType == OnlineArenaScannerFlow.ValidationTypeFinish)
        {
            if (validation.Status == "PASS")
            {
                var bothSubmitted = match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString()
                    && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString();

                var p1Done = match.Player1ResultStatus == PlayerResultStatus.DNF.ToString() || match.Player1FinishCheckStatus == "PASSED";
                var p2Done = match.Player2ResultStatus == PlayerResultStatus.DNF.ToString() || match.Player2FinishCheckStatus == "PASSED";

                if (bothSubmitted && p1Done && p2Done)
                {
                    await _completeMatchUseCase.ExecuteAsync(match.Id);
                    var reloaded = await _matchRepo.GetByIdAsync(match.Id) ?? match;
                    return new ObserveFinishFrameResponseDto
                    {
                        MatchId = reloaded.Id,
                        MeUserId = userId,
                        FinishCheckStatus = "PASSED",
                        WaitingForOpponent = false,
                        OpponentResultStatus = isP1 ? reloaded.Player2ResultStatus : reloaded.Player1ResultStatus,
                        OpponentFinishCheckStatus = isP1 ? reloaded.Player2FinishCheckStatus : reloaded.Player1FinishCheckStatus,
                        NextUiState = "COMPLETED",
                        ServerNow = DateTime.UtcNow,
                        MatchStatus = reloaded.StatusCode,
                        Outcome = reloaded.Outcome,
                        WinnerId = reloaded.WinnerId
                    };
                }
                else
                {
                    var signalRPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Player waiting for opponent.");
                    await _notifier.NotifyPlayerWaitingOpponentAsync(match.Id, signalRPayload);
                    await _notifier.NotifyFinishCheckUpdatedAsync(match.Id, signalRPayload);

                    return new ObserveFinishFrameResponseDto
                    {
                        MatchId = match.Id,
                        MeUserId = userId,
                        FinishCheckStatus = "PASSED",
                        WaitingForOpponent = true,
                        OpponentResultStatus = isP1 ? match.Player2ResultStatus : match.Player1ResultStatus,
                        OpponentFinishCheckStatus = isP1 ? match.Player2FinishCheckStatus : match.Player1FinishCheckStatus,
                        NextUiState = "WAITING_OPPONENT",
                        ServerNow = DateTime.UtcNow
                    };
                }
            }
            else
            {
                // Finish scan FAILED (sai màu/ánh sáng) — cho phép scan lại từ đầu
                // KHÔNG chuyển sang NEEDS_REVIEW, match vẫn tiếp tục bình thường.
                // CompleteValidation đã reset FinishCheckStatus về NOT_STARTED và xóa ScannerStateJson.
                var signalRPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Finish check failed. Please re-scan your Rubik's cube from the beginning.");
                await _notifier.NotifyFinishCheckUpdatedAsync(match.Id, signalRPayload);

                return new ObserveFinishFrameResponseDto
                {
                    MatchId = match.Id,
                    MeUserId = userId,
                    FinishCheckStatus = "NOT_STARTED",
                    WaitingForOpponent = false,
                    OpponentResultStatus = isP1 ? match.Player2ResultStatus : match.Player1ResultStatus,
                    OpponentFinishCheckStatus = isP1 ? match.Player2FinishCheckStatus : match.Player1FinishCheckStatus,
                    NextUiState = "RETRY_SCAN",
                    Message = "Colors did not match a solved Rubik's cube. Please re-scan all faces from the beginning.",
                    ServerNow = DateTime.UtcNow,
                    MatchStatus = match.StatusCode,
                    Outcome = match.Outcome
                };
            }
        }
        else
        {
            // SCRAMBLE validation completed — trigger event-driven auto-ready
            await MarkCameraReadyUseCase.AutoReadyIfChecklistPassedAsync(
                match, userId, _matchRepo, _notifier, _uow);
        }

        return completedResponse;
    }
}

public class OnlineArenaScannerCompleteRequest
{
    public string ScanSessionId { get; set; } = string.Empty;
    public int ScanGeneration { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public List<AiRubikScannerPreviewDto> Observations { get; set; } = [];
}
