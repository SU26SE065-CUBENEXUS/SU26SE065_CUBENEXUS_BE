using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class StartOnlineMatchScannerSessionUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IAiRubikClient _aiRubikClient;
    private readonly IUnitOfWork _uow;

    public StartOnlineMatchScannerSessionUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IAiRubikClient aiRubikClient,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _aiRubikClient = aiRubikClient;
        _uow = uow;
    }

    public async Task<OnlineArenaScannerSessionResponseDto> ExecuteAsync(Guid matchId, Guid userId, string validationType)
    {
        var match = await OnlineArenaScannerFlow.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        OnlineArenaScannerFlow.ValidateScannerPhase(match, userId, validationType);

        var started = await _aiRubikClient.StartScannerTestSessionAsync();
        var state = new OnlineArenaPlayerScannerState
        {
            ValidationType = validationType,
            ScanSessionId = Guid.NewGuid().ToString("N"),
            AiSessionId = started.SessionId,
            ScanGeneration = 1,
            ScanStatus = "IN_PROGRESS",
            ScannerState = started.ScannerState,
            RequestedFaceIndex = 1,
            Faces = [],
            UpdatedAt = DateTime.UtcNow
        };

        OnlineArenaScannerFlow.ApplyScannerState(match, userId, validationType, state);
        OnlineArenaScannerFlow.ApplyValidationProgress(match, userId, validationType, "SCANNING");

        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, $"{validationType}_SCANNER_SESSION_STARTED", new
        {
            state.ScanSessionId,
            state.AiSessionId,
            state.ScanGeneration,
            validationType
        }));
        await _uow.SaveChangesAsync();

        var response = OnlineArenaScannerFlow.BuildScannerResponse(match, userId, state, "Scanner session started.");
        await OnlineArenaScannerFlow.NotifyScannerUpdatedAsync(_notifier, match.Id, validationType, response);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, response.Message));
        return response;
    }
}

public class GetOnlineMatchScannerSessionUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;

    public GetOnlineMatchScannerSessionUseCase(IOnlineMatchRepository matchRepo)
    {
        _matchRepo = matchRepo;
    }

    public async Task<OnlineArenaScannerSessionResponseDto> ExecuteAsync(Guid matchId, Guid userId, string validationType)
    {
        var match = await OnlineArenaScannerFlow.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        var state = OnlineArenaScannerFlow.RequireScannerState(match, userId, validationType);
        return OnlineArenaScannerFlow.BuildScannerResponse(match, userId, state, "Scanner session loaded.");
    }
}

public class ObserveOnlineMatchScannerFrameUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IAiRubikClient _aiRubikClient;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeMatchUseCase;

    public ObserveOnlineMatchScannerFrameUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IAiRubikClient aiRubikClient,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeMatchUseCase)
    {
        _matchRepo = matchRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _aiRubikClient = aiRubikClient;
        _uow = uow;
        _completeMatchUseCase = completeMatchUseCase;
    }

    public async Task<object> ExecuteAsync(
        Guid matchId,
        Guid userId,
        string validationType,
        string imageBase64,
        OnlineArenaScannerObserveRequest request)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            throw new ArgumentException("Scanner snapshot is required.");

        var match = await OnlineArenaScannerFlow.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        var state = OnlineArenaScannerFlow.RequireScannerState(match, userId, validationType);
        OnlineArenaScannerFlow.EnsureRequestMatchesState(state, request);

        var metadata = new Dictionary<string, object?>
        {
            ["source"] = "online-arena",
            ["scanSessionId"] = state.AiSessionId,
            ["scanGeneration"] = state.ScanGeneration,
            ["requestId"] = request.RequestId,
            ["targetFaceIndex"] = request.TargetFaceIndex
        };

        var observation = await _aiRubikClient.ObserveScannerTestFrameAsync(state.AiSessionId, imageBase64, metadata);
        if (!string.Equals(observation.ScanSessionId, state.AiSessionId, StringComparison.Ordinal)
            || observation.ScanGeneration != state.ScanGeneration
            || observation.TargetFaceIndex != request.TargetFaceIndex)
        {
            throw new InvalidOperationException("Scanner response identity mismatch.");
        }

        state.RequestId = observation.RequestId ?? request.RequestId;
        state.ScannerState = observation.ScannerState;
        state.LastObservation = OnlineArenaScannerFlow.ToObservationState(observation);
        state.UpdatedAt = DateTime.UtcNow;

        if (observation.ScannerState == "ACCEPTED")
        {
            OnlineArenaScannerFlow.AcceptFace(state, observation);
            state.RequestedFaceIndex = Math.Min(state.Faces.Count + 1, 6);

            if (state.Faces.Count >= 6)
            {
                state.ScanStatus = "COMPLETED";
                var validation = OnlineArenaScannerFlow.CompleteValidation(match, userId, validationType, state);
                OnlineArenaScannerFlow.ApplyScannerState(match, userId, validationType, state);
                _matchRepo.Update(match);
                await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, $"{validationType}_SCANNER_COMPLETED", new
                {
                    state.ScanSessionId,
                    validationType,
                    validation
                }));
                await _uow.SaveChangesAsync();

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
                        var signalRPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Finish check failed. Match moved to review.");
                        await _notifier.NotifyFinishCheckUpdatedAsync(match.Id, signalRPayload);
                        await _notifier.NotifyMatchNeedsReviewAsync(match.Id, signalRPayload);

                        return new ObserveFinishFrameResponseDto
                        {
                            MatchId = match.Id,
                            MeUserId = userId,
                            FinishCheckStatus = "FAILED",
                            WaitingForOpponent = false,
                            OpponentResultStatus = isP1 ? match.Player2ResultStatus : match.Player1ResultStatus,
                            OpponentFinishCheckStatus = isP1 ? match.Player2FinishCheckStatus : match.Player1FinishCheckStatus,
                            NextUiState = "NEEDS_REVIEW",
                            ServerNow = DateTime.UtcNow,
                            MatchStatus = match.StatusCode,
                            Outcome = match.Outcome
                        };
                    }
                }
                else
                {
                    // SCRAMBLE validation completed — trigger event-driven auto-ready
                    // This may transition the match to COUNTDOWN if both players' checklists are now complete
                    await MarkCameraReadyUseCase.AutoReadyIfChecklistPassedAsync(
                        match, userId, _matchRepo, _notifier, _uow);
                }

                return completedResponse;
            }
        }

        OnlineArenaScannerFlow.ApplyScannerState(match, userId, validationType, state);
        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = OnlineArenaScannerFlow.BuildScannerResponse(match, userId, state, observation.Reason ?? "Scanner observation recorded.");
        await OnlineArenaScannerFlow.NotifyScannerUpdatedAsync(_notifier, match.Id, validationType, response);
        return response;
    }
}

public class RetryOnlineMatchScannerFaceUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IAiRubikClient _aiRubikClient;
    private readonly IUnitOfWork _uow;

    public RetryOnlineMatchScannerFaceUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IAiRubikClient aiRubikClient,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _aiRubikClient = aiRubikClient;
        _uow = uow;
    }

    public async Task<OnlineArenaScannerSessionResponseDto> ExecuteAsync(Guid matchId, Guid userId, string validationType)
    {
        var match = await OnlineArenaScannerFlow.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        var state = OnlineArenaScannerFlow.RequireScannerState(match, userId, validationType);

        var updated = await _aiRubikClient.RetryScannerTestFaceAsync(state.AiSessionId);
        state.ScanGeneration++;
        state.ScannerState = updated.ScannerState;
        state.LastObservation = null;
        state.RequestId = null;
        state.UpdatedAt = DateTime.UtcNow;

        OnlineArenaScannerFlow.ApplyValidationProgress(match, userId, validationType, "SCANNING");
        OnlineArenaScannerFlow.ApplyScannerState(match, userId, validationType, state);
        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = OnlineArenaScannerFlow.BuildScannerResponse(match, userId, state, updated.Message ?? "Retry current face.");
        await OnlineArenaScannerFlow.NotifyScannerUpdatedAsync(_notifier, match.Id, validationType, response);
        return response;
    }
}

public class ResetOnlineMatchScannerSessionUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IAiRubikClient _aiRubikClient;
    private readonly IUnitOfWork _uow;

    public ResetOnlineMatchScannerSessionUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IAiRubikClient aiRubikClient,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _aiRubikClient = aiRubikClient;
        _uow = uow;
    }

    public async Task<OnlineArenaScannerSessionResponseDto> ExecuteAsync(Guid matchId, Guid userId, string validationType)
    {
        var match = await OnlineArenaScannerFlow.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        var state = OnlineArenaScannerFlow.RequireScannerState(match, userId, validationType);

        var updated = await _aiRubikClient.ResetScannerTestSessionAsync(state.AiSessionId);
        state.ScanGeneration++;
        state.ScanStatus = "IN_PROGRESS";
        state.ScannerState = updated.ScannerState;
        state.RequestedFaceIndex = 1;
        state.RequestId = null;
        state.LastObservation = null;
        state.Faces.Clear();
        state.UpdatedAt = DateTime.UtcNow;

        OnlineArenaScannerFlow.ApplyValidationProgress(match, userId, validationType, "SCANNING");
        if (validationType == OnlineArenaScannerFlow.ValidationTypeScramble)
        {
            OnlineArenaScannerFlow.SetPlayerReady(match, userId, false);
        }

        OnlineArenaScannerFlow.ApplyScannerState(match, userId, validationType, state);
        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = OnlineArenaScannerFlow.BuildScannerResponse(match, userId, state, updated.Message ?? "Scanner session reset.");
        await OnlineArenaScannerFlow.NotifyScannerUpdatedAsync(_notifier, match.Id, validationType, response);
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, OnlineArenaFlowHelpers.BuildReadinessResponse(match, response.Message));
        return response;
    }
}

internal static class OnlineArenaScannerFlow
{
    internal const string ValidationTypeScramble = "SCRAMBLE";
    internal const string ValidationTypeFinish = "FINISH";

    private static readonly string[] FaceOrder = ["U", "R", "F", "D", "L", "B"];
    private static readonly Dictionary<string, string> FaceCenters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["U"] = "white",
        ["R"] = "red",
        ["F"] = "green",
        ["D"] = "yellow",
        ["L"] = "orange",
        ["B"] = "blue"
    };

    public static async Task<OnlineMatch> RequireParticipantMatchAsync(IOnlineMatchRepository matchRepo, Guid matchId, Guid userId)
    {
        var match = await matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");
        return match;
    }

    public static void ValidateScannerPhase(OnlineMatch match, Guid userId, string validationType)
    {
        validationType = NormalizeValidationType(validationType);
        if (validationType == ValidationTypeScramble)
        {
            if (match.StatusCode is not nameof(OnlineMatchStatus.CREATED) and not nameof(OnlineMatchStatus.READY))
                throw new InvalidOperationException("Scramble scanner is only allowed before match start.");
            EnsurePlayerAssignment(match, userId);
            return;
        }

        if (match.StatusCode is not nameof(OnlineMatchStatus.ONGOING) and not nameof(OnlineMatchStatus.PENDING_EVIDENCE))
            throw new InvalidOperationException("Finish scanner is only allowed after the match has started.");

        var isPlayer1 = match.Player1Id == userId;
        var resultStatus = isPlayer1 ? match.Player1ResultStatus : match.Player2ResultStatus;

        if (resultStatus == PlayerResultStatus.PENDING.ToString())
            throw new InvalidOperationException("Submit result before finish validation.");
        if (resultStatus == PlayerResultStatus.DNF.ToString())
            throw new InvalidOperationException("Finish validation is not allowed for DNF results.");
    }

    public static string NormalizeValidationType(string validationType)
    {
        validationType = validationType.Trim().ToUpperInvariant();
        return validationType switch
        {
            "SCRAMBLE" => ValidationTypeScramble,
            "FINISH" => ValidationTypeFinish,
            _ => throw new ArgumentException("validationType must be 'scramble' or 'finish'.")
        };
    }

    public static void EnsureRequestMatchesState(OnlineArenaPlayerScannerState state, OnlineArenaScannerObserveRequest request)
    {
        if (!string.Equals(state.ScanSessionId, request.ScanSessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Scan session mismatch.");
        if (state.ScanGeneration != request.ScanGeneration)
            throw new InvalidOperationException("Stale scan generation.");
        if (state.RequestedFaceIndex != request.TargetFaceIndex)
            throw new InvalidOperationException("Scanner request does not target the expected face.");
        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new ArgumentException("requestId is required.");
    }

    public static OnlineArenaPlayerScannerState RequireScannerState(OnlineMatch match, Guid userId, string validationType)
    {
        var json = GetScannerStateJson(match, userId);
        if (string.IsNullOrWhiteSpace(json))
            throw new KeyNotFoundException("Scanner session was not started.");

        var state = JsonSerializer.Deserialize<OnlineArenaPlayerScannerState>(json)
            ?? throw new InvalidOperationException("Stored scanner state is invalid.");
        if (!string.Equals(state.ValidationType, NormalizeValidationType(validationType), StringComparison.Ordinal))
            throw new InvalidOperationException("Scanner session type mismatch.");
        return state;
    }

    public static void ApplyScannerState(OnlineMatch match, Guid userId, string validationType, OnlineArenaPlayerScannerState state)
    {
        validationType = NormalizeValidationType(validationType);
        state.ValidationType = validationType;
        var json = JsonSerializer.Serialize(state);
        if (match.Player1Id == userId) match.Player1ScannerStateJson = json;
        else match.Player2ScannerStateJson = json;
    }

    public static void ApplyValidationProgress(OnlineMatch match, Guid userId, string validationType, string status)
    {
        if (NormalizeValidationType(validationType) == ValidationTypeScramble)
        {
            if (match.Player1Id == userId) match.Player1ScrambleCheckStatus = status;
            else match.Player2ScrambleCheckStatus = status;
            return;
        }

        if (match.Player1Id == userId) match.Player1FinishCheckStatus = status;
        else match.Player2FinishCheckStatus = status;
    }

    public static void SetPlayerReady(OnlineMatch match, Guid userId, bool ready)
    {
        if (match.Player1Id == userId) match.Player1Ready = ready;
        else match.Player2Ready = ready;
    }

    public static OnlineArenaScannerObservationState ToObservationState(AiRubikScannerPreviewDto observation)
        => new()
        {
            RequestId = observation.RequestId,
            StableObservationCount = observation.StableObservationCount,
            RequiredStableObservations = observation.RequiredStableObservations,
            DetectedStickers = observation.DetectedStickers,
            Confidence = observation.Confidence,
            InferMs = observation.InferMs,
            DecodeMs = observation.DecodeMs,
            PreprocessMs = observation.PreprocessMs,
            PostprocessMs = observation.PostprocessMs,
            TotalMs = observation.TotalMs,
            Reason = observation.Reason,
            ObservedCenterColor = observation.CenterColor,
            Grid3x3 = observation.Grid3x3
        };

    public static void AcceptFace(OnlineArenaPlayerScannerState state, AiRubikScannerPreviewDto observation)
    {
        if (observation.Grid3x3 == null || string.IsNullOrWhiteSpace(observation.CenterColor))
            throw new InvalidOperationException("Accepted scanner observation is missing face data.");

        var observedCenterColor = observation.CenterColor.Trim().ToLowerInvariant();
        var faceCode = FaceCodeForCenterColor(observedCenterColor);
        var canonicalFaceIndex = FaceIndexForCode(faceCode);

        var acceptedFace = new OnlineArenaAcceptedFaceState
        {
            FaceIndex = canonicalFaceIndex,
            FaceCode = faceCode,
            ExpectedCenterColor = observedCenterColor,
            ObservedCenterColor = observedCenterColor,
            Grid3x3 = NormalizeCubeGrid(observation.Grid3x3),
            AcceptedAt = DateTime.UtcNow
        };

        state.Faces.RemoveAll(face => string.Equals(face.FaceCode, faceCode, StringComparison.OrdinalIgnoreCase));
        state.Faces.Add(acceptedFace);
        state.Faces = state.Faces.OrderBy(face => face.FaceIndex).ToList();
    }

    public static OnlineArenaScannerValidationDto CompleteValidation(
        OnlineMatch match,
        Guid userId,
        string validationType,
        OnlineArenaPlayerScannerState state)
    {
        var observedState = state.Faces.OrderBy(face => face.FaceIndex)
            .ToDictionary(face => face.FaceCode, face => NormalizeCubeGrid(face.Grid3x3), StringComparer.OrdinalIgnoreCase);
        var basicValidation = RubikCubeStateValidator.ValidateBasicCubeState(observedState);
        if (!basicValidation.IsValid)
        {
            if (match.Player1Id == userId) match.Player1ObservedStateJson = JsonSerializer.Serialize(observedState);
            else match.Player2ObservedStateJson = JsonSerializer.Serialize(observedState);
            ApplyFailure(match, userId, validationType, "FAILED");
            return new OnlineArenaScannerValidationDto
            {
                Status = "RETRY",
                Matched = false,
                MatchedStickerCount = 0,
                MismatchedStickerCount = 54,
                PlayerStatus = validationType == ValidationTypeScramble ? "SCRAMBLE_NOT_VERIFIED" : "FINISH_NOT_VERIFIED"
            };
        }

        Dictionary<string, List<List<string>>> expectedState;
        if (NormalizeValidationType(validationType) == ValidationTypeScramble)
        {
            expectedState = DeserializeCubeState(match.Player1Id == userId ? match.Player1ExpectedStateJson : match.Player2ExpectedStateJson);
        }
        else
        {
            expectedState = RubikCubeStateValidator.BuildSolvedCubeState();
        }

        var comparison = RubikCubeStateValidator.CompareCubeStates(expectedState, observedState);
        var passed = comparison.Matched;
        var status = passed ? "PASS" : "RETRY";

        if (NormalizeValidationType(validationType) == ValidationTypeScramble)
        {
            if (match.Player1Id == userId)
            {
                match.Player1ObservedStateJson = JsonSerializer.Serialize(observedState);
                match.Player1ScrambleCheckStatus = passed ? "PASSED" : "FAILED";
            }
            else
            {
                match.Player2ObservedStateJson = JsonSerializer.Serialize(observedState);
                match.Player2ScrambleCheckStatus = passed ? "PASSED" : "FAILED";
            }

            SetPlayerReady(match, userId, passed);
            CubeStateValidationShared.ApplyLegacyPreCheckCompatibility(match, userId);
            // Auto-ready is handled by the caller (ObserveOnlineMatchScannerFrameUseCase) via AutoReadyIfChecklistPassedAsync
        }
        else
        {
            if (match.Player1Id == userId)
            {
                match.Player1ObservedStateJson = JsonSerializer.Serialize(observedState);
                match.Player1FinishCheckStatus = passed ? "PASSED" : "FAILED";
            }
            else
            {
                match.Player2ObservedStateJson = JsonSerializer.Serialize(observedState);
                match.Player2FinishCheckStatus = passed ? "PASSED" : "FAILED";
            }

            if (!passed)
                match.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
        }

        return new OnlineArenaScannerValidationDto
        {
            Status = status,
            Matched = comparison.Matched,
            MatchedStickerCount = comparison.MatchedStickerCount,
            MismatchedStickerCount = comparison.MismatchedStickerCount,
            PlayerStatus = NormalizeValidationType(validationType) == ValidationTypeScramble
                ? (passed ? "VERIFIED_READY" : "SCRAMBLE_NOT_VERIFIED")
                : (passed ? "FINISH_VERIFIED" : "FINISH_NOT_VERIFIED"),
            Mismatches = comparison.Mismatches
        };
    }

    public static OnlineArenaScannerSessionResponseDto BuildScannerResponse(
        OnlineMatch match,
        Guid userId,
        OnlineArenaPlayerScannerState state,
        string message)
    {
        var requestedFaceCode = string.Empty;
        var requestedCenterColor = state.Faces.Count >= 6 ? string.Empty : "any unscanned center";
        var requestedFaceLabel = state.Faces.Count >= 6
            ? "Completed"
            : $"Scan any remaining face ({state.Faces.Count + 1}/6)";
        var validation = state.ScanStatus == "COMPLETED"
            ? CompleteValidationSnapshot(match, userId, state.ValidationType)
            : null;
        return new OnlineArenaScannerSessionResponseDto
        {
            Message = message,
            MatchId = match.Id,
            PlayerId = userId,
            ValidationType = state.ValidationType,
            ScanSessionId = state.ScanSessionId,
            AiSessionId = state.AiSessionId,
            ScanGeneration = state.ScanGeneration,
            ScanStatus = state.ScanStatus,
            ScannerState = state.ScannerState,
            MatchStatus = match.StatusCode,
            RequestedFaceIndex = state.Faces.Count >= 6 ? 6 : state.RequestedFaceIndex,
            RequestedFaceCode = requestedFaceCode,
            RequestedFaceLabel = requestedFaceLabel,
            RequestedCenterColor = requestedCenterColor,
            CapturedFaceCount = state.Faces.Count,
            RequestId = state.RequestId,
            StableObservationCount = state.LastObservation?.StableObservationCount ?? 0,
            RequiredStableObservations = state.LastObservation?.RequiredStableObservations ?? 3,
            DetectedStickers = state.LastObservation?.DetectedStickers ?? 0,
            Confidence = state.LastObservation?.Confidence ?? 0,
            InferMs = state.LastObservation?.InferMs ?? 0,
            DecodeMs = state.LastObservation?.DecodeMs ?? 0,
            PreprocessMs = state.LastObservation?.PreprocessMs ?? 0,
            PostprocessMs = state.LastObservation?.PostprocessMs ?? 0,
            TotalMs = state.LastObservation?.TotalMs ?? 0,
            Reason = state.LastObservation?.Reason,
            ObservedCenterColor = state.LastObservation?.ObservedCenterColor,
            Grid3x3 = state.LastObservation?.Grid3x3,
            Faces = state.Faces.Select(face => new OnlineArenaScannerAcceptedFaceDto
            {
                FaceIndex = face.FaceIndex,
                FaceCode = face.FaceCode,
                ExpectedCenterColor = face.ExpectedCenterColor,
                ObservedCenterColor = face.ObservedCenterColor,
                Grid3x3 = face.Grid3x3,
                AcceptedAt = face.AcceptedAt
            }).ToList(),
            Validation = validation
        };
    }

    public static async Task NotifyScannerUpdatedAsync(IOnlineArenaRealtimeNotifier notifier, Guid matchId, string validationType, object payload)
    {
        if (NormalizeValidationType(validationType) == ValidationTypeScramble)
            await notifier.NotifyScrambleCheckUpdatedAsync(matchId, payload);
        else
            await notifier.NotifyFinishCheckUpdatedAsync(matchId, payload);
    }

    private static OnlineArenaScannerValidationDto? CompleteValidationSnapshot(OnlineMatch match, Guid userId, string validationType)
    {
        var scrambleStatus = match.Player1Id == userId ? match.Player1ScrambleCheckStatus : match.Player2ScrambleCheckStatus;
        var finishStatus = match.Player1Id == userId ? match.Player1FinishCheckStatus : match.Player2FinishCheckStatus;
        var status = NormalizeValidationType(validationType) == ValidationTypeScramble ? scrambleStatus : finishStatus;
        if (status is not "PASSED" and not "FAILED")
            return null;

        var observed = DeserializeCubeState(match.Player1Id == userId ? match.Player1ObservedStateJson : match.Player2ObservedStateJson);
        var expected = NormalizeValidationType(validationType) == ValidationTypeScramble
            ? DeserializeCubeState(match.Player1Id == userId ? match.Player1ExpectedStateJson : match.Player2ExpectedStateJson)
            : RubikCubeStateValidator.BuildSolvedCubeState();
        var comparison = RubikCubeStateValidator.CompareCubeStates(expected, observed);
        return new OnlineArenaScannerValidationDto
        {
            Status = status == "PASSED" ? "PASS" : "RETRY",
            Matched = comparison.Matched,
            MatchedStickerCount = comparison.MatchedStickerCount,
            MismatchedStickerCount = comparison.MismatchedStickerCount,
            PlayerStatus = NormalizeValidationType(validationType) == ValidationTypeScramble
                ? (status == "PASSED" ? "VERIFIED_READY" : "SCRAMBLE_NOT_VERIFIED")
                : (status == "PASSED" ? "FINISH_VERIFIED" : "FINISH_NOT_VERIFIED"),
            Mismatches = comparison.Mismatches
        };
    }

    private static void EnsurePlayerAssignment(OnlineMatch match, Guid userId)
    {
        var scramble = match.Player1Id == userId ? match.Player1ScrambleSequence : match.Player2ScrambleSequence;
        var expectedStateJson = match.Player1Id == userId ? match.Player1ExpectedStateJson : match.Player2ExpectedStateJson;
        if (string.IsNullOrWhiteSpace(scramble) || string.IsNullOrWhiteSpace(expectedStateJson))
            throw new InvalidOperationException("Player scramble assignment is missing.");
    }

    private static string GetScannerStateJson(OnlineMatch match, Guid userId)
        => match.Player1Id == userId ? match.Player1ScannerStateJson ?? string.Empty : match.Player2ScannerStateJson ?? string.Empty;

    private static string FaceCodeForIndex(int faceIndex)
    {
        if (faceIndex < 1 || faceIndex > FaceOrder.Length)
            throw new InvalidOperationException("Invalid face index.");
        return FaceOrder[faceIndex - 1];
    }

    private static string FaceCodeForCenterColor(string centerColor)
    {
        foreach (var pair in FaceCenters)
        {
            if (string.Equals(pair.Value, centerColor, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }

        throw new InvalidOperationException($"Unsupported center color '{centerColor}'.");
    }

    private static int FaceIndexForCode(string faceCode)
    {
        for (var i = 0; i < FaceOrder.Length; i++)
        {
            if (string.Equals(FaceOrder[i], faceCode, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }

        throw new InvalidOperationException($"Unsupported face code '{faceCode}'.");
    }

    private static Dictionary<string, List<List<string>>> DeserializeCubeState(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, List<List<string>>>>(json)
              ?? [];

    private static List<List<string>> NormalizeCubeGrid(List<List<string>> grid)
        => grid.Select(row => row.Select(color => color.Trim().ToLowerInvariant()).ToList()).ToList();

    private static void ApplyFailure(OnlineMatch match, Guid userId, string validationType, string status)
    {
        if (NormalizeValidationType(validationType) == ValidationTypeScramble)
        {
            if (match.Player1Id == userId) match.Player1ScrambleCheckStatus = status;
            else match.Player2ScrambleCheckStatus = status;
            SetPlayerReady(match, userId, false);
            return;
        }

        if (match.Player1Id == userId) match.Player1FinishCheckStatus = status;
        else match.Player2FinishCheckStatus = status;
    }
}

internal sealed class OnlineArenaPlayerScannerState
{
    public string ValidationType { get; set; } = string.Empty;
    public string ScanSessionId { get; set; } = string.Empty;
    public string AiSessionId { get; set; } = string.Empty;
    public int ScanGeneration { get; set; }
    public string ScanStatus { get; set; } = "IN_PROGRESS";
    public string ScannerState { get; set; } = "POSITION_FACE";
    public int RequestedFaceIndex { get; set; } = 1;
    public string? RequestId { get; set; }
    public OnlineArenaScannerObservationState? LastObservation { get; set; }
    public List<OnlineArenaAcceptedFaceState> Faces { get; set; } = [];
    public DateTime UpdatedAt { get; set; }
}

internal sealed class OnlineArenaScannerObservationState
{
    public string? RequestId { get; set; }
    public int StableObservationCount { get; set; }
    public int RequiredStableObservations { get; set; }
    public int DetectedStickers { get; set; }
    public double Confidence { get; set; }
    public double InferMs { get; set; }
    public double DecodeMs { get; set; }
    public double PreprocessMs { get; set; }
    public double PostprocessMs { get; set; }
    public double TotalMs { get; set; }
    public string? Reason { get; set; }
    public string? ObservedCenterColor { get; set; }
    public List<List<string>>? Grid3x3 { get; set; }
}

internal sealed class OnlineArenaAcceptedFaceState
{
    public int FaceIndex { get; set; }
    public string FaceCode { get; set; } = string.Empty;
    public string ExpectedCenterColor { get; set; } = string.Empty;
    public string? ObservedCenterColor { get; set; }
    public List<List<string>> Grid3x3 { get; set; } = [];
    public DateTime AcceptedAt { get; set; }
}
