using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class MarkWebRtcConnectedUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public MarkWebRtcConnectedUseCase(
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

    public async Task<WebRtcConnectionResponseDto> ExecuteAsync(Guid matchId, Guid userId, MarkWebRtcConnectedRequest request)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        var normalizedState = (request.ConnectionState ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedState is not "connected" and not "completed")
            throw new ArgumentException("connectionState must be connected or completed.");

        if (match.Player1Id == userId)
            match.Player1WebRtcConnected = true;
        else
            match.Player2WebRtcConnected = true;

        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, "WEBRTC_CONNECTED", request));
        await _uow.SaveChangesAsync();

        var response = new WebRtcConnectionResponseDto
        {
            Message = "WebRTC connection marked.",
            MatchId = match.Id,
            PlayerId = userId,
            Player1WebRtcConnected = match.Player1WebRtcConnected,
            Player2WebRtcConnected = match.Player2WebRtcConnected,
            StatusCode = match.StatusCode
        };

        await _notifier.NotifyWebRtcConnectionUpdatedAsync(match.Id, response);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, response.Message));
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

public class MarkVideoRecordingStartedUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public MarkVideoRecordingStartedUseCase(
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

    public async Task<VideoRecordingStartedResponseDto> ExecuteAsync(Guid matchId, Guid userId, MarkVideoRecordingStartedRequest request)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");
        if (match.StatusCode is not nameof(OnlineMatchStatus.CREATED) and not nameof(OnlineMatchStatus.READY))
            throw new InvalidOperationException("Recording can only be marked during preparation.");

        if (match.Player1Id == userId)
        {
            match.Player1RecordingStarted = true;
            match.Player1RecordingStartedAt = request.RecordingStartedAt;
        }
        else
        {
            match.Player2RecordingStarted = true;
            match.Player2RecordingStartedAt = request.RecordingStartedAt;
        }

        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, "VIDEO_RECORDING_STARTED", request));
        await _uow.SaveChangesAsync();

        var response = new VideoRecordingStartedResponseDto
        {
            Message = "Video recording marked as started.",
            MatchId = match.Id,
            PlayerId = userId,
            Player1RecordingStarted = match.Player1RecordingStarted,
            Player2RecordingStarted = match.Player2RecordingStarted,
            RecordingStartedAt = request.RecordingStartedAt,
            StatusCode = match.StatusCode
        };

        await _notifier.NotifyVideoRecordingStartedAsync(match.Id, response);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, response.Message));
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

public class RunAiRubikCheckUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAiCheckRepository _aiCheckRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IAiRubikClient _aiClient;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeMatchUseCase;

    public RunAiRubikCheckUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAiCheckRepository aiCheckRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IAiRubikClient aiClient,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeMatchUseCase)
    {
        _matchRepo = matchRepo;
        _aiCheckRepo = aiCheckRepo;
        _auditRepo = auditRepo;
        _aiClient = aiClient;
        _notifier = notifier;
        _uow = uow;
        _completeMatchUseCase = completeMatchUseCase;
    }

    public async Task<AiRubikCheckResponseDto> ExecuteAsync(
        Guid matchId,
        Guid userId,
        string checkType,
        string imageBase64,
        string? evidenceImageUrl,
        string? scrambleSequence = null,
        CancellationToken cancellationToken = default)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        ValidateCheckPreconditions(match, userId, checkType);

        var startPayload = new { matchId, playerId = userId, checkType, startedAt = DateTime.UtcNow };
        await _notifier.NotifyAiCheckStartedAsync(matchId, startPayload);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(matchId, userId, $"AI_{checkType}_STARTED", startPayload));
        await _uow.SaveChangesAsync();

        var request = new AiRubikCheckRequestDto
        {
            MatchId = matchId,
            PlayerId = userId,
            CheckType = checkType,
            ScrambleSequence = scrambleSequence,
            ImageBase64 = imageBase64,
            ImageUrl = evidenceImageUrl
        };

        AiRubikCheckResultDto aiResult = checkType switch
        {
            "PRE_CHECK" => await _aiClient.PreCheckAsync(request, cancellationToken),
            "SCRAMBLE_CHECK" => await _aiClient.ScrambleCheckAsync(request, cancellationToken),
            "FINISH_CHECK" => await _aiClient.FinishCheckAsync(request, cancellationToken),
            _ => throw new ArgumentException("Unsupported AI check type.")
        };

        var aiCheck = new OnlineMatchAiCheck
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerId = userId,
            CheckType = checkType,
            Status = aiResult.Status,
            Confidence = aiResult.Confidence,
            EvidenceImageUrl = evidenceImageUrl,
            ModelVersion = aiResult.ModelVersion,
            ResultJson = JsonSerializer.Serialize(aiResult),
            FailureReason = aiResult.Reason,
            CreatedAt = DateTime.UtcNow
        };

        await _aiCheckRepo.AddAsync(aiCheck);
        ApplyMatchAiStatus(match, userId, checkType, aiResult.Status);

        if (checkType == "FINISH_CHECK" && aiResult.Status is "FAILED" or "NEEDS_REVIEW" or "AI_CHECK_UNAVAILABLE")
        {
            match.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
            match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
            {
                code = "AI_FINISH_CHECK_REVIEW",
                playerId = userId,
                status = aiResult.Status
            });
        }

        if (checkType == "SCRAMBLE_CHECK" && aiResult.Status is "FAILED" or "NEEDS_REVIEW" or "AI_CHECK_UNAVAILABLE")
        {
            match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
            {
                code = "AI_SCRAMBLE_CHECK_REVIEW",
                playerId = userId,
                status = aiResult.Status
            });
        }

        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(matchId, userId, $"AI_{checkType}_COMPLETED", aiResult));
        await _uow.SaveChangesAsync();

        var response = new AiRubikCheckResponseDto
        {
            MatchId = matchId,
            PlayerId = userId,
            CheckType = aiResult.CheckType,
            Status = aiResult.Status,
            Confidence = aiResult.Confidence,
            DetectedCube = aiResult.DetectedCube,
            DetectedStickers = aiResult.DetectedStickers,
            Grid3x3 = aiResult.Grid3x3,
            Reason = aiResult.Reason,
            ModelVersion = aiResult.ModelVersion,
            ModelLoaded = aiResult.ModelLoaded,
            EvidenceImageUrl = evidenceImageUrl,
            ExpectedScramble = aiResult.ExpectedScramble,
            DetectedState = aiResult.DetectedState,
            IsScrambleMatched = aiResult.IsScrambleMatched,
            IsSolved = aiResult.IsSolved,
            CreatedAt = aiResult.CreatedAt
        };

        await _notifier.NotifyAiCheckCompletedAsync(matchId, response);
        if (checkType == "SCRAMBLE_CHECK")
            await _notifier.NotifyScrambleCheckUpdatedAsync(matchId, response);
        if (checkType == "FINISH_CHECK")
            await _notifier.NotifyFinishCheckUpdatedAsync(matchId, response);

        if (checkType == "FINISH_CHECK"
            && match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString()
            && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString())
        {
            await _completeMatchUseCase.ExecuteAsync(matchId);
        }

        return response;
    }

    private static void ApplyMatchAiStatus(OnlineMatch match, Guid userId, string checkType, string status)
    {
        var isPlayer1 = match.Player1Id == userId;
        if (checkType == "PRE_CHECK")
        {
            if (isPlayer1) match.Player1AiPreCheckStatus = status;
            else match.Player2AiPreCheckStatus = status;
            return;
        }

        if (checkType == "SCRAMBLE_CHECK")
        {
            if (isPlayer1) match.Player1ScrambleCheckStatus = status;
            else match.Player2ScrambleCheckStatus = status;
            return;
        }

        if (isPlayer1) match.Player1FinishCheckStatus = status;
        else match.Player2FinishCheckStatus = status;
    }

    private static void ValidateCheckPreconditions(OnlineMatch match, Guid userId, string checkType)
    {
        var isPlayer1 = match.Player1Id == userId;
        if (checkType == "PRE_CHECK")
        {
            if (match.StatusCode != nameof(OnlineMatchStatus.CREATED))
                throw new InvalidOperationException("AI pre-check is only allowed while match is CREATED.");
            if (!(isPlayer1 ? match.Player1RecordingStarted : match.Player2RecordingStarted))
                throw new InvalidOperationException("Recording must be started before AI pre-check.");
            return;
        }

        if (checkType == "SCRAMBLE_CHECK")
        {
            if (match.StatusCode != nameof(OnlineMatchStatus.ONGOING))
                throw new InvalidOperationException("AI scramble-check is only allowed while match is ONGOING.");
            return;
        }

        if (match.StatusCode is not nameof(OnlineMatchStatus.ONGOING) and not nameof(OnlineMatchStatus.PENDING_EVIDENCE))
            throw new InvalidOperationException("AI finish-check is only allowed after the match has started.");

        if (isPlayer1 && match.Player1ResultStatus == PlayerResultStatus.PENDING.ToString())
            throw new InvalidOperationException("Submit result before AI finish-check.");
        if (!isPlayer1 && match.Player2ResultStatus == PlayerResultStatus.PENDING.ToString())
            throw new InvalidOperationException("Submit result before AI finish-check.");
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

internal static class OnlineArenaAuditFactory
{
    public static OnlineMatchAuditLog BuildAudit(Guid matchId, Guid? playerId, string eventType, object payload)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerId = playerId,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow
        };
}
