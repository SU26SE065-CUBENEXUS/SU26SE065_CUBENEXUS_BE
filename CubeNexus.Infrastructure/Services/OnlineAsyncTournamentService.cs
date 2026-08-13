using CubeNexus.Application.DTOs;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.UseCases.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Application.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CubeNexus.Infrastructure.Services;

public class OnlineAsyncTournamentService : IOnlineAsyncTournamentService
{
    private const int PlusTwoThresholdMs = 6_000;
    private const int DnfThresholdMs = 14_000;

    private readonly IUnitOfWork _uow;
    private readonly IScramblePoolService _scramblePool;
    private readonly IAiRubikClient _aiRubikClient;
    private readonly IRecordingStorageService _recordingStorage;

    public OnlineAsyncTournamentService(IUnitOfWork uow, IScramblePoolService scramblePool, IAiRubikClient aiRubikClient, IRecordingStorageService recordingStorage)
    {
        _uow = uow;
        _scramblePool = scramblePool;
        _aiRubikClient = aiRubikClient;
        _recordingStorage = recordingStorage;
    }

    public async Task<OnlineAsyncTournamentDto> CreateTournamentAsync(Guid managerUserId, CreateOnlineAsyncTournamentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new CustomException("INVALID_NAME", "Tournament name is required.", 400);

        if (request.RegistrationOpenAt >= request.RegistrationCloseAt)
            throw new CustomException("INVALID_DATES", "Registration open date must be before registration close date.", 400);

        if (request.StartDate >= request.EndDate)
            throw new CustomException("INVALID_DATES", "Competition start date must be before competition end date.", 400);

        if (request.RegistrationCloseAt > request.StartDate)
            throw new CustomException("INVALID_DATES", "Registration must close on or before the competition start time.", 400);

        if (request.AttemptTimeLimitMs is < 1 or > 3_600_000)
            throw new CustomException("INVALID_TIME_LIMIT", "Attempt time limit must be between 1 ms and 60 minutes.", 400);

        var puzzleType = await _uow.PuzzleTypes.GetByIdAsync(request.PuzzleTypeId, ct);
        if (puzzleType == null)
            throw new CustomException("PUZZLE_NOT_FOUND", "Specified puzzle type was not found.", 404);

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            TournamentType = "ONLINE_ASYNC",
            FormatCode = "AO1",
            PuzzleTypeId = request.PuzzleTypeId,
            // Async competitors receive a private scramble only when starting their attempt.
            ScrambleSequence = null,
            AttemptTimeLimitMs = request.AttemptTimeLimitMs > 0 ? request.AttemptTimeLimitMs : 300000,
            RegistrationOpenAt = request.RegistrationOpenAt,
            RegistrationCloseAt = request.RegistrationCloseAt,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            // PUBLISHED is the persisted equivalent of the pre-registration state.
            // The public DTO derives REGISTRATION_OPEN/CLOSED and ONGOING from dates.
            StatusCode = "PUBLISHED",
            CreatedBy = managerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _uow.Tournaments.Add(tournament);

        // Assign creator as TournamentManager
        var managerRole = new TournamentManager
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            UserId = managerUserId,
            AssignedAt = DateTime.UtcNow
        };
        _uow.TournamentManagers.Add(managerRole);

        await _uow.SaveChangesAsync(ct);

        var dto = MapToDto(tournament, puzzleType.Name, isRegistered: false, userAttempt: null);
        dto.StatusCode = GetComputedStatus(tournament, DateTime.UtcNow);
        return dto;
    }

    public async Task<OnlineAsyncTournamentDto> GetTournamentByIdAsync(Guid tournamentId, Guid? userId = null, CancellationToken ct = default)
    {
        var tournament = await _uow.Tournaments.GetByIdAsync(tournamentId, ct);
        if (tournament == null || tournament.TournamentType != "ONLINE_ASYNC")
            throw new CustomException("TOURNAMENT_NOT_FOUND", "Online async tournament not found.", 404);

        var puzzleType = tournament.PuzzleTypeId.HasValue
            ? await _uow.PuzzleTypes.GetByIdAsync(tournament.PuzzleTypeId.Value, ct)
            : null;

        bool isRegistered = false;
        OnlineAsyncAttempt? userAttempt = null;

        if (userId.HasValue)
        {
            var reg = await _uow.Registrations.FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == userId.Value, ct);
            isRegistered = reg != null;
            userAttempt = await _uow.OnlineAsyncAttempts.GetByTournamentAndUserAsync(tournamentId, userId.Value, ct);
        }

        var dto = MapToDto(tournament, puzzleType?.Name ?? "3x3", isRegistered, userAttempt);
        dto.StatusCode = GetComputedStatus(tournament, DateTime.UtcNow);
        return dto;
    }

    public async Task<List<OnlineAsyncTournamentDto>> ListTournamentsAsync(string? status = null, Guid? userId = null, CancellationToken ct = default)
    {
        var tournaments = await _uow.Tournaments.FindAsync(t => t.TournamentType == "ONLINE_ASYNC", ct);
        var now = DateTime.UtcNow;

        var result = new List<OnlineAsyncTournamentDto>();
        foreach (var t in tournaments.OrderByDescending(t => t.CreatedAt))
        {
            // Dynamically evaluate status code
            var computedStatus = GetComputedStatus(t, now);

            if (!string.IsNullOrEmpty(status) && !computedStatus.Equals(status, StringComparison.OrdinalIgnoreCase))
                continue;

            bool isRegistered = false;
            OnlineAsyncAttempt? attempt = null;
            if (userId.HasValue)
            {
                var reg = await _uow.Registrations.FirstOrDefaultAsync(r => r.TournamentId == t.Id && r.UserId == userId.Value, ct);
                isRegistered = reg != null;
                attempt = await _uow.OnlineAsyncAttempts.GetByTournamentAndUserAsync(t.Id, userId.Value, ct);
            }

            var puzzleName = t.PuzzleTypeId.HasValue
                ? (await _uow.PuzzleTypes.GetByIdAsync(t.PuzzleTypeId.Value, ct))?.Name
                : "3x3";

            var dto = MapToDto(t, puzzleName ?? "3x3", isRegistered, attempt);
            dto.StatusCode = computedStatus;
            result.Add(dto);
        }

        return result;
    }

    public async Task<bool> RegisterCompetitorAsync(Guid tournamentId, Guid userId, CancellationToken ct = default)
    {
        var tournament = await _uow.Tournaments.GetByIdAsync(tournamentId, ct);
        if (tournament == null || tournament.TournamentType != "ONLINE_ASYNC")
            throw new CustomException("TOURNAMENT_NOT_FOUND", "Online async tournament not found.", 404);

        var now = DateTime.UtcNow;
        if (now < tournament.RegistrationOpenAt)
            throw new CustomException("REGISTRATION_NOT_OPEN", "Registration is not open yet.", 400);

        if (now > tournament.RegistrationCloseAt)
            throw new CustomException("REGISTRATION_CLOSED", "Registration period has ended.", 400);

        var existingReg = await _uow.Registrations.FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == userId, ct);
        if (existingReg != null)
            return true; // Already registered

        var reg = new Registration
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = userId,
            StatusCode = "CONFIRMED",
            QrToken = Guid.NewGuid().ToString("N"),
            RegisteredAt = DateTime.UtcNow
        };

        _uow.Registrations.Add(reg);
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StartOnlineAsyncAttemptResponse> StartAttemptAsync(Guid tournamentId, Guid userId, CancellationToken ct = default)
    {
        var tournament = await _uow.Tournaments.GetByIdAsync(tournamentId, ct);
        if (tournament == null || tournament.TournamentType != "ONLINE_ASYNC")
            throw new CustomException("TOURNAMENT_NOT_FOUND", "Online async tournament not found.", 404);

        // Step 2 & 9 Enforcements: Must be registered
        var registration = await _uow.Registrations.FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == userId, ct);
        if (registration == null)
            throw new CustomException("NOT_REGISTERED", "Competitor must register before starting an attempt.", 403);

        // Step 3 & 9 Enforcements: Must be within Competition Start Time and Competition End Time
        var now = DateTime.UtcNow;
        if (now < tournament.StartDate)
            throw new CustomException("COMPETITION_NOT_STARTED", "Competition has not started yet.", 400);

        if (now > tournament.EndDate)
            throw new CustomException("COMPETITION_ENDED", "Competition has ended. New attempts are not allowed.", 400);

        await _uow.BeginTransactionAsync();
        try
        {
        // Step 12 Enforcement: Format AO1 -> Single attempt constraint
        var existingAttempt = await _uow.OnlineAsyncAttempts.GetByTournamentAndUserAsync(tournamentId, userId, ct);
        if (existingAttempt != null)
            throw new CustomException("ATTEMPT_ALREADY_EXISTS", "You have already started or completed your attempt for this AO1 tournament.", 400);

        var attemptId = Guid.NewGuid();
        var reservation = await _scramblePool.ReserveAsync("ONLINE_ASYNC", tournament.PuzzleTypeId!.Value,
            "ONLINE_ASYNC_ATTEMPT", attemptId, userId, ct);
        var attempt = new OnlineAsyncAttempt
        {
            Id = attemptId,
            TournamentId = tournamentId,
            UserId = userId,
            ScrambleSequence = reservation.Sequence,
            ScramblePoolItemId = reservation.Id,
            Status = "INITIALIZED",
            ReviewStatus = "PENDING_REVIEW",
            // The total attempt budget starts as soon as the competitor enters
            // the attempt, so it includes scramble scan, solve and finish scan.
            AttemptDeadlineAt = now.AddMilliseconds(tournament.AttemptTimeLimitMs),
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _uow.OnlineAsyncAttempts.Add(attempt);
        await _uow.SaveChangesAsync(ct);
        await _scramblePool.MarkUsedAsync(reservation.Id, userId, ct);
        await _uow.CommitTransactionAsync();

        return new StartOnlineAsyncAttemptResponse
        {
            AttemptId = attempt.Id,
            TournamentId = tournamentId,
            ScrambleSequence = attempt.ScrambleSequence,
            StartedAt = attempt.StartedAt,
            TimeLimitMs = tournament.AttemptTimeLimitMs,
            Status = attempt.Status
        };
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<VerifyAsyncScrambleResponse> VerifyScrambleAsync(Guid attemptId, Guid userId, VerifyAsyncScrambleRequest request, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null || attempt.UserId != userId)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);

        await EnsureAttemptWithinDeadlineAsync(attempt, ct);
        EnsureAttemptStatus(attempt, "INITIALIZED");
        var tournament = await _uow.Tournaments.GetByIdAsync(attempt.TournamentId, ct)
            ?? throw new CustomException("TOURNAMENT_NOT_FOUND", "Online async tournament not found.", 404);

        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            var aiResult = await _aiRubikClient.ScrambleCheckAsync(new AiRubikCheckRequestDto
            {
                PlayerId = userId,
                CheckType = "SCRAMBLE",
                ScrambleSequence = attempt.ScrambleSequence,
                ImageBase64 = request.ImageBase64,
                Metadata = new Dictionary<string, object?> { ["source"] = "online-async", ["attemptId"] = attempt.Id }
            }, ct);
            attempt.ScrambleEvidenceJson = JsonSerializer.Serialize(aiResult);
            attempt.ScrambleCheckStatus = aiResult.IsScrambleMatched == true ? "PASSED" : "FAILED";
            if (attempt.ScrambleCheckStatus == "FAILED")
            {
                await _uow.SaveChangesAsync(ct);
                return new VerifyAsyncScrambleResponse { AttemptId = attemptId, Passed = false, Status = "FAILED", Reason = aiResult.Reason ?? "AI could not verify the scramble. Please rescramble and scan again." };
            }
        }
        else if (request.Faces != null && request.Faces.Count == 5)
        {
            // Run state validation using RubikCubeStateValidator
            var faceDtos = request.Faces.Select(f => new ScrambleCheckBatchFaceDto
            {
                CenterColor = f.Face,
                Grid3x3 = f.Grid
            }).ToList();

            var validation = RubikCubeStateValidator.ValidateScrambleBatch(attempt.ScrambleSequence, faceDtos);
            attempt.ScrambleCheckStatus = validation.Passed ? "PASSED" : "FAILED";
            attempt.ScrambleEvidenceJson = JsonSerializer.Serialize(request.Faces);

            if (!validation.Passed)
            {
                var failReason = validation.MismatchedCenterColors.Count > 0
                    ? $"Scramble không khớp tại các mặt có tâm màu: {string.Join(", ", validation.MismatchedCenterColors)}."
                    : validation.Reason ?? "Cube state does not match tournament scramble. Please rescramble.";

                await _uow.SaveChangesAsync(ct);
                return new VerifyAsyncScrambleResponse
                {
                    AttemptId = attemptId,
                    Passed = false,
                    Status = "FAILED",
                    Reason = failReason
                };
            }
        }
        else throw new CustomException("AI_SCAN_REQUIRED", "A camera scan is required before starting the solve.", 400);

        var now = DateTime.UtcNow;
        attempt.Status = "SCRAMBLE_VERIFIED";
        attempt.HandTimerStartedAt = now;
        // AttemptDeadlineAt intentionally starts in StartAttemptAsync so the total
        // time remain includes the initial scramble scan.
        attempt.AttemptDeadlineAt ??= attempt.StartedAt.AddMilliseconds(tournament.AttemptTimeLimitMs);
        attempt.UpdatedAt = now;
        await _uow.SaveChangesAsync(ct);

        return new VerifyAsyncScrambleResponse
        {
            AttemptId = attemptId,
            Passed = true,
            Status = "PASSED",
            Reason = "Scramble verified successfully.",
            AttemptDeadlineAt = attempt.AttemptDeadlineAt,
            HandTimerStartedAt = attempt.HandTimerStartedAt
        };
    }

    public async Task<OnlineAsyncAttemptStateDto> GetAttemptStateAsync(Guid attemptId, Guid userId, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null || attempt.UserId != userId)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);

        await ExpireAttemptIfNeededAsync(attempt, ct);

        var result = MapToFinishResponse(attempt);
        return new OnlineAsyncAttemptStateDto
        {
            AttemptId = result.AttemptId,
            TournamentId = attempt.TournamentId,
            AttemptStatus = attempt.Status,
            ScrambleCheckStatus = attempt.ScrambleCheckStatus,
            FinishCheckStatus = attempt.FinishCheckStatus,
            AttemptDeadlineAt = attempt.AttemptDeadlineAt,
            HandTimerStartedAt = attempt.HandTimerStartedAt,
            SolveStartedAt = attempt.SolveStartedAt,
            ScrambleSequence = attempt.ScrambleSequence,
            RawTimeMs = result.RawTimeMs,
            PenaltyTimeMs = result.PenaltyTimeMs,
            PenaltyCode = result.PenaltyCode,
            IsDnf = result.IsDnf,
            FinalTimeMs = result.FinalTimeMs,
            Status = result.Status,
            ReviewStatus = result.ReviewStatus,
            DisplayResult = result.DisplayResult
        };
    }

    public async Task<StartAsyncSolveTimerResponse> StartSolveTimerAsync(Guid attemptId, Guid userId, StartAsyncSolveTimerRequest request, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null || attempt.UserId != userId)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);

        await EnsureAttemptWithinDeadlineAsync(attempt, ct);
        EnsureAttemptStatus(attempt, "SCRAMBLE_VERIFIED");

        var now = DateTime.UtcNow;
        if (!attempt.HandTimerStartedAt.HasValue)
            throw new CustomException("HAND_TIMER_NOT_STARTED", "Penalty timer has not started.", 400);

        // Penalty is authoritative on the server. The client value is display-only
        // and must not be trusted to decide NONE / PLUS2 / DNF.
        var elapsedDouble = Math.Max(0, (now - attempt.HandTimerStartedAt.Value).TotalMilliseconds);
        var handTimerMs = (int)Math.Min(int.MaxValue, Math.Round(elapsedDouble));

        attempt.UpdatedAt = now;
        if (handTimerMs > DnfThresholdMs)
        {
            attempt.Status = "COMPLETED";
            attempt.SolveFinishedAt = now;
            attempt.PenaltyCode = "DNF";
            attempt.PenaltyTimeMs = 0;
            attempt.IsDnf = true;
            attempt.FinalTimeMs = null;
            attempt.ReviewStatus = "PENDING_REVIEW";

            await _uow.SaveChangesAsync(ct);
            return new StartAsyncSolveTimerResponse
            {
                AttemptId = attemptId,
                Status = attempt.Status,
                SolveStartedAt = now,
                HandTimerMs = handTimerMs,
                PenaltyCode = "DNF",
                PenaltyTimeMs = 0,
                IsDnf = true,
                Message = "Start delay exceeded 14 seconds. Result is DNF."
            };
        }

        attempt.SolveStartedAt = now;
        attempt.Status = "SOLVING";
        attempt.PenaltyCode = handTimerMs > PlusTwoThresholdMs ? "PLUS2" : "NONE";
        attempt.PenaltyTimeMs = handTimerMs > PlusTwoThresholdMs ? 2_000 : 0;
        attempt.IsDnf = false;

        await _uow.SaveChangesAsync(ct);

        return new StartAsyncSolveTimerResponse
        {
            AttemptId = attemptId,
            Status = attempt.Status,
            SolveStartedAt = now,
            HandTimerMs = handTimerMs,
            PenaltyCode = attempt.PenaltyCode,
            PenaltyTimeMs = attempt.PenaltyTimeMs,
            IsDnf = false,
            Message = attempt.PenaltyCode == "PLUS2" ? "Timer started with a +2 second penalty." : "Timer started normally."
        };
    }

    public async Task<FinishAsyncSolveTimerResponse> FinishSolveTimerAsync(Guid attemptId, Guid userId, FinishAsyncSolveTimerRequest request, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null || attempt.UserId != userId)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);

        await EnsureAttemptWithinDeadlineAsync(attempt, ct);
        EnsureAttemptStatus(attempt, "SOLVING");

        var now = DateTime.UtcNow;
        if (!attempt.SolveStartedAt.HasValue)
            throw new CustomException("SOLVE_TIMER_NOT_STARTED", "Solve timer has not started.", 400);

        var elapsedDouble = Math.Max(1, (now - attempt.SolveStartedAt.Value).TotalMilliseconds);
        var serverRawTimeMs = (int)Math.Min(int.MaxValue, Math.Round(elapsedDouble));
        attempt.SolveFinishedAt = now;
        attempt.RawTimeMs = serverRawTimeMs;
        attempt.Status = "FINISH_PENDING";
        attempt.ReviewStatus = "PENDING_REVIEW";
        attempt.UpdatedAt = now;
        attempt.FinalTimeMs = serverRawTimeMs + attempt.PenaltyTimeMs;

        await _uow.SaveChangesAsync(ct);

        return MapToFinishResponse(attempt);
    }

    public async Task<FinishAsyncSolveTimerResponse> VerifyFinishAsync(Guid attemptId, Guid userId, VerifyAsyncFinishRequest request, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null || attempt.UserId != userId)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);

        await EnsureAttemptWithinDeadlineAsync(attempt, ct);
        // A successful finish verification is idempotent. This lets the client
        // retry a failed evidence upload without changing a finalized result.
        if (attempt.Status == "COMPLETED" && attempt.FinishCheckStatus == "PASSED" && !attempt.IsDnf)
            return MapToFinishResponse(attempt);

        // FINISH_PENDING is the current lifecycle state. COMPLETED + PENDING is
        // accepted for attempts created by the previous implementation.
        if (attempt.Status != "FINISH_PENDING" && !(attempt.Status == "COMPLETED" && attempt.FinishCheckStatus == "PENDING" && !attempt.IsDnf))
            throw new CustomException("INVALID_ATTEMPT_STATE", $"This action requires attempt status FINISH_PENDING; current status is {attempt.Status}.", 400);

        if (request.Faces == null || request.Faces.Count != 5)
            throw new CustomException("AI_SCAN_REQUIRED", "Five scanned cube faces are required for finish verification.", 400);

        var faceDtos = request.Faces.Select(f => new ScrambleCheckBatchFaceDto
        {
            CenterColor = f.Face,
            Grid3x3 = f.Grid
        }).ToList();
        var solvedValidation = RubikCubeStateValidator.ValidateSolvedBatch(faceDtos);
        if (!solvedValidation.Passed)
        {
            attempt.FinishEvidenceJson = JsonSerializer.Serialize(request.Faces);
            attempt.FinishCheckStatus = "FAILED";
            attempt.Status = "COMPLETED";
            attempt.IsDnf = true;
            attempt.PenaltyCode = "DNF";
            attempt.PenaltyTimeMs = 0;
            attempt.FinalTimeMs = null;
            attempt.UpdatedAt = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return MapToFinishResponse(attempt);
        }

        attempt.FinishCheckStatus = "PASSED";
        attempt.Status = "COMPLETED";
        attempt.FinishEvidenceJson = JsonSerializer.Serialize(request.Faces);
        attempt.IsDnf = false;
        // Preserve the penalty decided when solving started.
        attempt.FinalTimeMs = (attempt.RawTimeMs ?? 0) + attempt.PenaltyTimeMs;
        attempt.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            try
            {
                var aiResult = await _aiRubikClient.FinishCheckAsync(new AiRubikCheckRequestDto
                {
                    PlayerId = userId,
                    CheckType = "FINISH",
                    ImageBase64 = request.ImageBase64,
                    Metadata = new Dictionary<string, object?> { ["source"] = "online-async", ["attemptId"] = attempt.Id }
                }, ct);
                if (aiResult != null)
                {
                    attempt.FinishEvidenceJson = JsonSerializer.Serialize(aiResult);
                }
            }
            catch
            {
                // AI service snapshot failure does not override 5-face solved validation
            }
        }

        await _uow.SaveChangesAsync(ct);
        return MapToFinishResponse(attempt);
    }

    public async Task<AsyncAttemptVideoUploadResponse> UploadVideoEvidenceAsync(Guid attemptId, Guid userId, Stream content, string contentType, CancellationToken ct = default)
    {
        var attempt = await GetValidAttemptForVideoAsync(attemptId, userId, ct);
        if (content is null || !content.CanRead)
            throw new CustomException("INVALID_VIDEO", "A video file is required.", 400);

        var normalizedType = NormalizeVideoContentType(contentType);
        var extension = normalizedType == "video/mp4" ? "mp4" : "webm";
        var objectKey = $"online-async/{attempt.TournamentId:N}/{attempt.Id:N}/evidence.{extension}";
        await _recordingStorage.UploadStreamAsync(objectKey, content, normalizedType, ct);
        attempt.VideoEvidenceUrl = objectKey;
        attempt.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return new AsyncAttemptVideoUploadResponse { AttemptId = attempt.Id, ObjectKey = objectKey };
    }

    public async Task<AsyncAttemptVideoUploadUrlResponse> CreateVideoUploadUrlAsync(Guid attemptId, Guid userId, CreateAsyncAttemptVideoUploadUrlRequest request, CancellationToken ct = default)
    {
        var attempt = await GetValidAttemptForVideoAsync(attemptId, userId, ct);
        var contentType = NormalizeVideoContentType(request.ContentType);
        var extension = contentType == "video/mp4" ? "mp4" : "webm";
        if (!string.IsNullOrWhiteSpace(request.FileExtension)
            && !string.Equals(request.FileExtension.Trim().TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase))
            throw new CustomException("INVALID_VIDEO_EXTENSION", "Video extension does not match its content type.", 400);

        var objectKey = $"online-async/{attempt.TournamentId:N}/{attempt.Id:N}/evidence-{Guid.NewGuid():N}.{extension}";
        var ticket = await _recordingStorage.CreateUploadUrlAsync(objectKey, contentType, ct);
        return new AsyncAttemptVideoUploadUrlResponse
        {
            AttemptId = attempt.Id,
            UploadUrl = ticket.Url.ToString(),
            ObjectKey = objectKey,
            ContentType = contentType,
            ExpiresAt = ticket.ExpiresAtUtc
        };
    }

    public async Task<AsyncAttemptVideoUploadResponse> CompleteVideoUploadAsync(Guid attemptId, Guid userId, CompleteAsyncAttemptVideoUploadRequest request, CancellationToken ct = default)
    {
        var attempt = await GetValidAttemptForVideoAsync(attemptId, userId, ct);
        var expectedPrefix = $"online-async/{attempt.TournamentId:N}/{attempt.Id:N}/";
        if (string.IsNullOrWhiteSpace(request.ObjectKey) || !request.ObjectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
            throw new CustomException("INVALID_VIDEO_OBJECT_KEY", "Video object key does not belong to this attempt.", 400);

        var metadata = await _recordingStorage.GetObjectMetadataAsync(request.ObjectKey, ct);
        if (metadata == null || metadata.FileSizeBytes <= 0)
            throw new CustomException("VIDEO_UPLOAD_NOT_FOUND", "Uploaded recording is missing or empty in storage.", 400);

        attempt.VideoEvidenceUrl = metadata.ObjectKey;
        attempt.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return new AsyncAttemptVideoUploadResponse { AttemptId = attempt.Id, ObjectKey = metadata.ObjectKey };
    }

    private async Task<OnlineAsyncAttempt> GetValidAttemptForVideoAsync(Guid attemptId, Guid userId, CancellationToken ct)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null || attempt.UserId != userId)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);
        if (attempt.Status != "COMPLETED" || attempt.IsDnf || attempt.FinishCheckStatus != "PASSED")
            throw new CustomException("INVALID_ATTEMPT_STATE", "Only a valid completed attempt can upload video evidence.", 400);
        return attempt;
    }

    private static string NormalizeVideoContentType(string contentType)
    {
        var normalized = (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();
        if (normalized is not ("video/webm" or "video/mp4"))
            throw new CustomException("INVALID_VIDEO_TYPE", "Only video/webm and video/mp4 are supported.", 400);
        return normalized;
    }

    public async Task<string> GetVideoPlaybackUrlAsync(Guid attemptId, Guid reviewerUserId, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct)
            ?? throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);
        await EnsureCanReviewAsync(attempt.TournamentId, reviewerUserId, ct);
        if (attempt.IsDnf || attempt.FinishCheckStatus != "PASSED" || string.IsNullOrWhiteSpace(attempt.VideoEvidenceUrl))
            throw new CustomException("VIDEO_NOT_AVAILABLE", "A valid recording is not available for this attempt.", 404);
        var playback = await _recordingStorage.CreatePlaybackUrlAsync(attempt.VideoEvidenceUrl, ct);
        return playback.Url.ToString();
    }

    public async Task<List<AsyncLeaderboardEntryDto>> GetAttemptsForReviewAsync(Guid tournamentId, Guid reviewerUserId, CancellationToken ct = default)
    {
        await EnsureCanReviewAsync(tournamentId, reviewerUserId, ct);
        var attempts = await _uow.OnlineAsyncAttempts.GetAttemptsByTournamentAsync(tournamentId, ct);
        // Only completed, valid attempts that have their evidence uploaded are reviewable.
        return MapToLeaderboardList(attempts.Where(a =>
            a.Status == "COMPLETED"
            && !a.IsDnf
            && a.FinishCheckStatus == "PASSED"
            && !string.IsNullOrWhiteSpace(a.VideoEvidenceUrl)).ToList());
    }

    public async Task<AsyncLeaderboardEntryDto> ReviewAttemptAsync(Guid attemptId, Guid reviewerUserId, ReviewAsyncAttemptRequest request, CancellationToken ct = default)
    {
        var attempt = await _uow.OnlineAsyncAttempts.GetByIdAsync(attemptId, ct);
        if (attempt == null)
            throw new CustomException("ATTEMPT_NOT_FOUND", "Attempt not found.", 404);

        await EnsureCanReviewAsync(attempt.TournamentId, reviewerUserId, ct);

        if (attempt.Status != "COMPLETED")
            throw new CustomException("ATTEMPT_NOT_COMPLETED", "Only completed attempts can be reviewed.", 400);

        if (request.ReviewStatus is not ("APPROVED" or "REJECTED"))
            throw new CustomException("INVALID_REVIEW_STATUS", "Review status must be APPROVED or REJECTED.", 400);

        if (request.PenaltyCode is not ("NONE" or "PLUS2" or "DNF"))
            throw new CustomException("INVALID_PENALTY", "Penalty code must be NONE, PLUS2, or DNF.", 400);

        attempt.ReviewStatus = request.ReviewStatus; // APPROVED | REJECTED
        attempt.ReviewedBy = reviewerUserId;
        attempt.ReviewedAt = DateTime.UtcNow;
        attempt.ReviewNote = request.ReviewNote;

        if (!string.IsNullOrEmpty(request.PenaltyCode))
        {
            attempt.PenaltyCode = request.PenaltyCode;
            if (request.PenaltyCode == "DNF" || request.ReviewStatus == "REJECTED")
            {
                attempt.IsDnf = true;
                attempt.PenaltyTimeMs = 0;
                attempt.FinalTimeMs = null;
            }
            else if (request.PenaltyCode == "PLUS2")
            {
                attempt.IsDnf = false;
                attempt.PenaltyTimeMs = 2000;
                attempt.FinalTimeMs = (attempt.RawTimeMs ?? 0) + 2000;
            }
            else // NONE
            {
                attempt.IsDnf = false;
                attempt.PenaltyTimeMs = 0;
                attempt.FinalTimeMs = attempt.RawTimeMs;
            }
        }

        attempt.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        var user = await _uow.Users.GetByIdAsync(attempt.UserId, ct);
        return MapToLeaderboardEntry(attempt, user, rank: 0);
    }

    public async Task<List<AsyncLeaderboardEntryDto>> GetLeaderboardAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var attempts = await _uow.OnlineAsyncAttempts.GetLeaderboardAsync(tournamentId, ct);
        return MapToLeaderboardList(attempts);
    }

    // Helper mappers
    private static string GetComputedStatus(Tournament tournament, DateTime now)
    {
        if (tournament.StatusCode is "DRAFT" or "CANCELLED" or "DISABLED" or "COMPLETED")
            return tournament.StatusCode;
        if (now < tournament.RegistrationOpenAt) return "PUBLISHED";
        if (now <= tournament.RegistrationCloseAt) return "REGISTRATION_OPEN";
        if (now < tournament.StartDate) return "REGISTRATION_CLOSED";
        if (now <= tournament.EndDate) return "ONGOING";
        return "COMPLETED";
    }

    private static void EnsureAttemptStatus(OnlineAsyncAttempt attempt, string expectedStatus)
    {
        if (attempt.Status != expectedStatus)
            throw new CustomException("INVALID_ATTEMPT_STATE", $"This action requires attempt status {expectedStatus}; current status is {attempt.Status}.", 400);
    }

    private async Task EnsureAttemptWithinDeadlineAsync(OnlineAsyncAttempt attempt, CancellationToken ct)
    {
        if (await ExpireAttemptIfNeededAsync(attempt, ct))
            throw new CustomException("ATTEMPT_TIME_EXPIRED", "Attempt time limit has expired. Result is DNF.", 400);
    }

    private async Task<bool> ExpireAttemptIfNeededAsync(OnlineAsyncAttempt attempt, CancellationToken ct)
    {
        if (!attempt.AttemptDeadlineAt.HasValue
            || DateTime.UtcNow < attempt.AttemptDeadlineAt.Value
            || (attempt.Status == "COMPLETED" && (attempt.FinishCheckStatus != "PENDING" || attempt.IsDnf)))
            return false;

        attempt.Status = "COMPLETED";
        attempt.SolveFinishedAt = DateTime.UtcNow;
        attempt.IsDnf = true;
        attempt.PenaltyCode = "DNF";
        attempt.PenaltyTimeMs = 0;
        attempt.FinalTimeMs = null;
        attempt.ReviewStatus = "PENDING_REVIEW";
        attempt.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task EnsureCanReviewAsync(Guid tournamentId, Guid userId, CancellationToken ct)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct);
        if (string.Equals(user?.UserRole, "ADMIN", StringComparison.OrdinalIgnoreCase)) return;
        var isManager = await _uow.TournamentManagers.AnyAsync(m => m.TournamentId == tournamentId && m.UserId == userId, ct);
        var isJudge = await _uow.TournamentJudges.AnyAsync(j => j.TournamentId == tournamentId && j.UserId == userId, ct);
        if (!isManager && !isJudge)
            throw new CustomException("FORBIDDEN", "Only a manager or judge assigned to this tournament can review attempts.", 403);
    }

    private static OnlineAsyncTournamentDto MapToDto(Tournament t, string? puzzleName, bool isRegistered, OnlineAsyncAttempt? userAttempt)
    {
        return new OnlineAsyncTournamentDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            TournamentType = t.TournamentType,
            FormatCode = t.FormatCode,
            PuzzleTypeId = t.PuzzleTypeId,
            PuzzleTypeName = puzzleName ?? "3x3",
            ScrambleSequence = t.ScrambleSequence,
            AttemptTimeLimitMs = t.AttemptTimeLimitMs,
            RegistrationOpenAt = t.RegistrationOpenAt,
            RegistrationCloseAt = t.RegistrationCloseAt,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            StatusCode = t.StatusCode,
            CreatedBy = t.CreatedBy,
            CreatedAt = t.CreatedAt,
            IsRegistered = isRegistered,
            UserAttemptStatus = userAttempt?.Status,
            UserAttemptId = userAttempt?.Id
        };
    }

    private static FinishAsyncSolveTimerResponse MapToFinishResponse(OnlineAsyncAttempt attempt)
    {
        string displayResult = attempt.IsDnf
            ? "DNF"
            : attempt.PenaltyCode == "PLUS2"
                ? $"{((attempt.RawTimeMs ?? 0) / 1000.0):F2}s +2 = {((attempt.FinalTimeMs ?? 0) / 1000.0):F2}s"
                : $"{((attempt.FinalTimeMs ?? 0) / 1000.0):F2}s";

        return new FinishAsyncSolveTimerResponse
        {
            AttemptId = attempt.Id,
            RawTimeMs = attempt.RawTimeMs ?? 0,
            PenaltyTimeMs = attempt.PenaltyTimeMs,
            PenaltyCode = attempt.PenaltyCode,
            IsDnf = attempt.IsDnf,
            FinalTimeMs = attempt.FinalTimeMs,
            Status = attempt.Status,
            ReviewStatus = attempt.ReviewStatus,
            DisplayResult = displayResult
        };
    }

    private static List<AsyncLeaderboardEntryDto> MapToLeaderboardList(List<OnlineAsyncAttempt> attempts)
    {
        var list = new List<AsyncLeaderboardEntryDto>();
        int rank = 1;
        foreach (var a in attempts)
        {
            list.Add(MapToLeaderboardEntry(a, a.User, a.IsDnf ? 9999 : rank++));
        }
        return list;
    }

    private static AsyncLeaderboardEntryDto MapToLeaderboardEntry(OnlineAsyncAttempt a, User? user, int rank)
    {
        string display = a.IsDnf
            ? "DNF"
            : a.PenaltyCode == "PLUS2"
                ? $"{((a.RawTimeMs ?? 0) / 1000.0):F2}s +2 = {((a.FinalTimeMs ?? 0) / 1000.0):F2}s"
                : $"{((a.FinalTimeMs ?? 0) / 1000.0):F2}s";

        return new AsyncLeaderboardEntryDto
        {
            Rank = rank,
            AttemptId = a.Id,
            UserId = a.UserId,
            UserFullName = user != null && !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : "Competitor",
            UserAvatarUrl = user?.AvatarUrl,
            RawTimeMs = a.RawTimeMs,
            PenaltyTimeMs = a.PenaltyTimeMs,
            PenaltyCode = a.PenaltyCode,
            IsDnf = a.IsDnf,
            FinalTimeMs = a.FinalTimeMs,
            DisplayResult = display,
            ReviewStatus = a.ReviewStatus,
            VideoEvidenceUrl = a.VideoEvidenceUrl,
            SolveFinishedAt = a.SolveFinishedAt ?? a.CreatedAt
        };
    }
}
