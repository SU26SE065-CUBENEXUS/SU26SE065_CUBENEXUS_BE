using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class ValidateScrambleCubeStateUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public ValidateScrambleCubeStateUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<CubeScanValidationResponseDto> ExecuteAsync(Guid matchId, Guid userId, CubeScanValidationRequest request)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");
        if (match.StatusCode is not nameof(OnlineMatchStatus.CREATED) and not nameof(OnlineMatchStatus.READY))
            throw new InvalidOperationException("Scramble validation is only allowed before match start.");

        var validation = RubikCubeStateValidator.ValidateBasicCubeState(request.CubeState);
        var matched = validation.IsValid && RubikCubeStateValidator.MatchesScramble(request.CubeState, match.ScrambleSequence);
        var status = validation.IsValid && matched ? "PASSED" : "FAILED";

        CubeStateValidationShared.ApplyPlayerStatus(match, userId, status, isFinish: false);
        if (status == "PASSED")
        {
            CubeStateValidationShared.ApplyLegacyPreCheckCompatibility(match, userId);
            if (MarkPlayerReadyUseCase.AllReady(match))
                match.StatusCode = OnlineMatchStatus.READY.ToString();
        }
        else
        {
            match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
            {
                code = "SCRAMBLE_VALIDATION_FAILED",
                playerId = userId,
                reason = validation.Reason
            });
        }

        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, "SCRAMBLE_VALIDATION_COMPLETED", new
        {
            status,
            matched,
            validation,
            request.ScanMetadata
        }));
        await _uow.SaveChangesAsync();

        var response = CubeStateValidationShared.BuildResponse(match, userId, "SCRAMBLE_CHECK", status, validation, matched, null);
        await _notifier.NotifyScrambleCheckUpdatedAsync(match.Id, response);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, response.Message));
        if (match.StatusCode == OnlineMatchStatus.READY.ToString())
            await _notifier.NotifyMatchReadyAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, "Match ready."));

        return response;
    }

    private async Task<OnlineMatch> RequireParticipantMatchAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        return match;
    }
}

public class ValidateFinishCubeStateUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeMatchUseCase;

    public ValidateFinishCubeStateUseCase(
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

    public async Task<CubeScanValidationResponseDto> ExecuteAsync(Guid matchId, Guid userId, CubeScanValidationRequest request)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");
        if (match.StatusCode is not nameof(OnlineMatchStatus.ONGOING) and not nameof(OnlineMatchStatus.PENDING_EVIDENCE))
            throw new InvalidOperationException("Finish validation is only allowed after match start.");

        var isPlayer1 = match.Player1Id == userId;
        if (isPlayer1 && match.Player1ResultStatus == PlayerResultStatus.PENDING.ToString())
            throw new InvalidOperationException("Submit result before finish validation.");
        if (!isPlayer1 && match.Player2ResultStatus == PlayerResultStatus.PENDING.ToString())
            throw new InvalidOperationException("Submit result before finish validation.");

        var validation = RubikCubeStateValidator.ValidateBasicCubeState(request.CubeState);
        var solved = validation.IsValid && RubikCubeStateValidator.IsSolved(request.CubeState);
        var status = validation.IsValid && solved ? "PASSED" : "FAILED";

        CubeStateValidationShared.ApplyPlayerStatus(match, userId, status, isFinish: true);
        if (status != "PASSED")
        {
            match.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
            match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
            {
                code = "FINISH_VALIDATION_FAILED",
                playerId = userId,
                reason = validation.Reason
            });
        }

        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, "FINISH_VALIDATION_COMPLETED", new
        {
            status,
            solved,
            validation,
            request.ScanMetadata
        }));
        await _uow.SaveChangesAsync();

        var response = CubeStateValidationShared.BuildResponse(match, userId, "FINISH_CHECK", status, validation, null, solved);
        await _notifier.NotifyFinishCheckUpdatedAsync(match.Id, response);

        if (status == "PASSED"
            && match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString()
            && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString()
            && match.Player1FinishCheckStatus == "PASSED"
            && match.Player2FinishCheckStatus == "PASSED")
        {
            await _completeMatchUseCase.ExecuteAsync(match.Id);
        }

        return response;
    }

    private async Task<OnlineMatch> RequireParticipantMatchAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        return match;
    }
}

internal static class CubeScanValidationResponseFactory
{
    public static CubeScanValidationResponseDto BuildResponse(
        OnlineMatch match,
        Guid userId,
        string validationType,
        string status,
        CubeStateBasicValidation validation,
        bool? scrambleMatched,
        bool? solved)
        => new()
        {
            Message = status == "PASSED" ? $"{validationType} passed." : $"{validationType} failed.",
            MatchId = match.Id,
            PlayerId = userId,
            ValidationType = validationType,
            Status = status,
            MatchStatus = match.StatusCode,
            IsValidCubeState = validation.IsValid,
            IsScrambleMatched = scrambleMatched,
            IsSolved = solved,
            Reason = validation.Reason,
            Missing = validation.Missing,
            ColorCounts = validation.ColorCounts,
            CreatedAt = DateTime.UtcNow
        };
}

internal static partial class CubeStateValidationShared
{
    public static void ApplyPlayerStatus(OnlineMatch match, Guid userId, string status, bool isFinish)
    {
        var isPlayer1 = match.Player1Id == userId;
        if (isFinish)
        {
            if (isPlayer1) match.Player1FinishCheckStatus = status;
            else match.Player2FinishCheckStatus = status;
            return;
        }

        if (isPlayer1) match.Player1ScrambleCheckStatus = status;
        else match.Player2ScrambleCheckStatus = status;
    }

    public static void ApplyLegacyPreCheckCompatibility(OnlineMatch match, Guid userId)
    {
    }

    public static CubeScanValidationResponseDto BuildResponse(
        OnlineMatch match,
        Guid userId,
        string validationType,
        string status,
        CubeStateBasicValidation validation,
        bool? scrambleMatched,
        bool? solved)
        => CubeScanValidationResponseFactory.BuildResponse(match, userId, validationType, status, validation, scrambleMatched, solved);
}
