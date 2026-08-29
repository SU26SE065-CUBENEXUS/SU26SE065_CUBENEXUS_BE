using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using System.Security.Authentication;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class SubmitMobileTimerSolveTimeUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IMobileTimerSessionRepository _sessionRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeUseCase;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;

    public SubmitMobileTimerSolveTimeUseCase(
        IOnlineMatchRepository matchRepo,
        IMobileTimerSessionRepository sessionRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeUseCase,
        IOnlineMatchAuditLogRepository auditRepo)
    {
        _matchRepo = matchRepo;
        _sessionRepo = sessionRepo;
        _notifier = notifier;
        _uow = uow;
        _completeUseCase = completeUseCase;
        _auditRepo = auditRepo;
    }

    public async Task<SubmitSolveTimeResponseDto> ExecuteAsync(Guid userId, SubmitSolveTimeRequest request)
    {
        var match = await _matchRepo.GetByIdAsync(request.MatchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");

        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        if (match.StatusCode != OnlineMatchStatus.ONGOING.ToString() || 
            (match.Phase != "SOLVING" && match.Phase != "INSPECTION"))
        {
            throw new InvalidOperationException("Match must be ONGOING and in SOLVING or INSPECTION phase to submit time.");
        }

        // If a player starts/finishes their solve during the inspection phase, transition the match phase to SOLVING
        if (match.Phase == "INSPECTION")
        {
            match.Phase = "SOLVING";
            match.TimeLimitMs = OnlineMatch.DefaultSolveTimeLimitMs;
            match.SolveDeadlineAt = DateTime.UtcNow.AddMilliseconds(OnlineMatch.DefaultSolveTimeLimitMs);
        }

        // Verify mobile timer session
        var session = await _sessionRepo.GetSessionAsync(request.MatchId, userId);
        if (session == null || !session.IsActive || session.Id != request.MobileTimerSessionId)
            throw new InvalidOperationException("No active paired mobile timer session found.");

        // Strip the :P1/:P2 role suffix appended by the frontend QR code generator.
        // The mobile app stores the full scanned string (e.g. "ABC123:P1") but DB stores only base code ("ABC123").
        var submittedToken = request.DeviceSessionToken?.Split(':')[0].Trim() ?? string.Empty;
        if (session.QrSessionCode != submittedToken)
            throw new AuthenticationException("Invalid device session token.");

        var isPlayer1 = match.Player1Id == userId;

        // Idempotency: check if already submitted
        var existingStatus = isPlayer1 ? match.Player1ResultStatus : match.Player2ResultStatus;
        if (existingStatus != PlayerResultStatus.PENDING.ToString())
        {
            var existingTime = isPlayer1 ? match.Player1TimeMs : match.Player2TimeMs;
            var existingDnf = isPlayer1 ? match.Player1IsDnf : match.Player2IsDnf;
            if (existingDnf == request.IsDnf && existingTime == request.TimeMs)
            {
                return BuildResponse(match, userId);
            }
            throw new ConflictException("Result already submitted with different data.");
        }

        // Calculate inspection penalty using server-side timestamps only.
        // We compare InspectionDeadlineAt (when inspection officially ended on server)
        // against the server's current time (when the submission arrived).
        // This is 100% immune to mobile clock drift/timezone issues.
        string penaltyDescription = "";
        if (!request.IsDnf && match.InspectionDeadlineAt.HasValue && request.TimeMs.HasValue)
        {
            var serverReceiveTime = DateTime.UtcNow;
            var inspectionDeadline = match.InspectionDeadlineAt.Value;

            // How many seconds AFTER the inspection deadline did this submission arrive?
            // If the player started solving before inspection ended, this will be negative or small.
            // The solve start time from server's perspective:
            // solveStartedAt = serverReceiveTime - TimeMs (time they were solving)
            // elapsedAfterDeadline = solveStartedAt - inspectionDeadline
            var solveStartedAtServer = serverReceiveTime - TimeSpan.FromMilliseconds(request.TimeMs.Value);
            var elapsedAfterDeadlineSecs = (solveStartedAtServer - inspectionDeadline).TotalSeconds;

            // WCA Rule: 15s inspection.
            // We allow a generous 6.0-second buffer for page rendering, client transition, and network lag.
            // If player started solving >= 2s after deadline + buffer: +2s penalty (total 23s).
            // If player started solving >= 4s after deadline + buffer: DNF (total 25s).
            var lagBufferSecs = 6.0;
            var adjustedElapsedSecs = elapsedAfterDeadlineSecs - lagBufferSecs;

            if (adjustedElapsedSecs >= 2.0)
            {
                if (adjustedElapsedSecs >= 4.0)
                {
                    request.IsDnf = true;
                    request.TimeMs = null;
                    penaltyDescription = " (Inspection timeout DNF)";

                    await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, "INSPECTION_TIMEOUT_DNF", new
                    {
                        elapsedAfterDeadlineSecs,
                        adjustedElapsedSecs,
                        lagBufferSecs,
                        solveStartedAtServer,
                        inspectionDeadline,
                        serverReceiveTime
                    }));
                }
                else
                {
                    request.TimeMs += 2000;
                    penaltyDescription = " (+2s inspection penalty)";

                    await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, userId, "INSPECTION_PENALTY_ADDED", new
                    {
                        elapsedAfterDeadlineSecs,
                        adjustedElapsedSecs,
                        lagBufferSecs,
                        originalTimeMs = request.TimeMs - 2000,
                        penalizedTimeMs = request.TimeMs,
                        solveStartedAtServer,
                        inspectionDeadline,
                        serverReceiveTime
                    }));
                }
            }
        }

        // Verify scramble check was PASSED if solving successfully
        if (!request.IsDnf)
        {
            var scrambleStatus = isPlayer1 ? match.Player1ScrambleCheckStatus : match.Player2ScrambleCheckStatus;
            if (scrambleStatus != "PASSED")
                throw new InvalidOperationException("Cannot submit VALID result when scramble check was not PASSED.");

            if (request.TimeMs == null || request.TimeMs <= 0)
                throw new ArgumentException("timeMs must be > 0 when isDnf is false.");

            if (request.TimeMs > match.TimeLimitMs)
                throw new ArgumentException("timeMs exceeds match time limit. Submit as DNF.");
        }

        // Update player-level fields
        if (isPlayer1)
        {
            match.Player1IsDnf = request.IsDnf;
            match.Player1TimeMs = request.IsDnf ? null : request.TimeMs;
            match.Player1ResultStatus = request.IsDnf ? PlayerResultStatus.DNF.ToString() : PlayerResultStatus.VALID.ToString();
            match.Player1FinishedAt = request.StoppedAt.ToUniversalTime();
            match.Player1FinishCheckStatus = request.IsDnf ? "NOT_REQUIRED" : "NOT_STARTED";
        }
        else
        {
            match.Player2IsDnf = request.IsDnf;
            match.Player2TimeMs = request.IsDnf ? null : request.TimeMs;
            match.Player2ResultStatus = request.IsDnf ? PlayerResultStatus.DNF.ToString() : PlayerResultStatus.VALID.ToString();
            match.Player2FinishedAt = request.StoppedAt.ToUniversalTime();
            match.Player2FinishCheckStatus = request.IsDnf ? "NOT_REQUIRED" : "NOT_STARTED";
        }

        // Check phase transitions
        var bothSubmitted = match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString()
            && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString();

        if (bothSubmitted)
        {
            var p1Needs = match.Player1ResultStatus == PlayerResultStatus.VALID.ToString() && match.Player1FinishCheckStatus != "PASSED";
            var p2Needs = match.Player2ResultStatus == PlayerResultStatus.VALID.ToString() && match.Player2FinishCheckStatus != "PASSED";

            if (p1Needs || p2Needs)
            {
                match.Phase = "PENDING_EVIDENCE";
                match.VideoEvidenceUploadDeadlineAt = DateTime.UtcNow.AddMinutes(2);
            }
        }

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        // Notify result submitted
        var signalRPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, $"Result submitted{penaltyDescription}.");
        await _notifier.NotifyResultSubmittedAsync(match.Id, signalRPayload);

        // If both submitted and all required finish checks are completed (e.g. both DNF), complete match immediately
        if (bothSubmitted)
        {
            var p1Done = match.Player1ResultStatus == PlayerResultStatus.DNF.ToString() || match.Player1FinishCheckStatus == "PASSED";
            var p2Done = match.Player2ResultStatus == PlayerResultStatus.DNF.ToString() || match.Player2FinishCheckStatus == "PASSED";
            if (p1Done && p2Done)
            {
                await _completeUseCase.ExecuteAsync(match.Id);
                // Reload match state
                var reloaded = await _matchRepo.GetByIdAsync(match.Id) ?? match;
                return BuildResponse(reloaded, userId);
            }
        }

        return BuildResponse(match, userId);
    }

    private static SubmitSolveTimeResponseDto BuildResponse(CubeNexus.Domain.Entities.OnlineMatch match, Guid userId)
    {
        var isPlayer1 = match.Player1Id == userId;
        var myResultStatus = isPlayer1 ? match.Player1ResultStatus : match.Player2ResultStatus;
        var myTimeMs = isPlayer1 ? match.Player1TimeMs : match.Player2TimeMs;
        var myFinishStatus = isPlayer1 ? match.Player1FinishCheckStatus : match.Player2FinishCheckStatus;

        var oppResultStatus = isPlayer1 ? match.Player2ResultStatus : match.Player1ResultStatus;
        var oppFinishStatus = isPlayer1 ? match.Player2FinishCheckStatus : match.Player1FinishCheckStatus;

        var canStartFinish = myResultStatus == PlayerResultStatus.VALID.ToString() && myFinishStatus != "PASSED";

        return new SubmitSolveTimeResponseDto
        {
            MatchId = match.Id,
            MeUserId = userId,
            MyResultStatus = myResultStatus,
            MyTimeMs = myTimeMs,
            MyFinishCheckStatus = myFinishStatus,
            OpponentResultStatus = oppResultStatus,
            OpponentFinishCheckStatus = oppFinishStatus,
            CanStartFinishCheck = canStartFinish,
            MatchPhase = match.Phase,
            ServerNow = DateTime.UtcNow
        };
    }
}
