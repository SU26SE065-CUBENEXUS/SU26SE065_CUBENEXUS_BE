using CubeNexus.Application.DTOs.Practice;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using CubeNexus.Domain.Services;

namespace CubeNexus.Infrastructure.Services;

/// <summary>
/// Luồng tập luyện WCA Stackmat:
///   session → attempt (scramble) → hands-on → ready → hands-off → solving → hands-on stop → finalize
/// </summary>
public class PracticeService : IPracticeService
{
    private const int MaxSolveTimeMs = 600_000;

    private static readonly HashSet<string> ValidPenalties =
        new(StringComparer.OrdinalIgnoreCase) { "OK", "PLUS_2", "DNF" };

    private readonly IUnitOfWork _uow;
    private readonly IScrambleGeneratorService _scrambleGenerator;
    private readonly IPracticeRealtimeNotifier _notifier;

    public PracticeService(
        IUnitOfWork uow,
        IScrambleGeneratorService scrambleGenerator,
        IPracticeRealtimeNotifier notifier)
    {
        _uow = uow;
        _scrambleGenerator = scrambleGenerator;
        _notifier = notifier;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Session
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PracticeSessionResponseDto> StartSessionAsync(
        Guid userId, StartPracticeSessionDto dto)
    {
        var puzzleType = await _uow.PuzzleTypes.GetByIdAsync(dto.PuzzleTypeId)
            ?? throw new KeyNotFoundException("Không tìm thấy loại rubik này.");

        if (!puzzleType.IsActive)
            throw new InvalidOperationException("Loại rubik này hiện không hoạt động.");

        var existing = await _uow.Practice.GetActiveSessionAsync(userId, dto.PuzzleTypeId);
        if (existing != null)
            throw new CustomException(
                "SESSION_ALREADY_ACTIVE",
                "Bạn đã có session đang mở cho loại rubik này. Hãy kết thúc session cũ trước.",
                400);

        var session = new PracticeSession
        {
            Id           = Guid.NewGuid(),
            UserId       = userId,
            PuzzleTypeId = dto.PuzzleTypeId,
            StartedAt    = DateTime.UtcNow,
            EndedAt      = null
        };

        await _uow.Practice.AddSessionAsync(session);
        await _uow.SaveChangesAsync();

        return MapSession(session, puzzleType.Name, puzzleType.Code, 0);
    }

    [Obsolete("Use WCA attempt flow instead.")]
    public Task<PracticeSolveResponseDto> SubmitSolveAsync(Guid userId, SubmitSolveDto dto)
        => throw new CustomException(
            "SOLVE_ENDPOINT_DEPRECATED",
            "POST /api/practice/solves đã ngừng hỗ trợ. Hãy dùng luồng attempt WCA Stackmat.",
            410);

    public async Task<PracticeSessionSummaryDto> EndSessionAsync(
        Guid userId, Guid sessionId)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);

        var activeAttempt = await _uow.Practice.GetActiveAttemptAsync(sessionId);
        if (activeAttempt != null)
            throw new CustomException(
                "ATTEMPT_IN_PROGRESS",
                "Session còn attempt chưa hoàn thành. Hãy finalize hoặc abort trước khi kết thúc session.",
                400);

        if (!session.EndedAt.HasValue)
        {
            session.EndedAt = DateTime.UtcNow;
            await _uow.SaveChangesAsync();
        }

        await _notifier.NotifyPracticeSessionEndedAsync(userId, sessionId);

        return await BuildSummaryAsync(session);
    }

    public async Task<IReadOnlyList<PracticeSessionResponseDto>> GetMySessionsAsync(
        Guid userId, Guid? puzzleTypeId = null, int page = 1, int pageSize = 20)
    {
        var skip = (page - 1) * pageSize;
        var sessions = await _uow.Practice.GetSessionsByUserAsync(
            userId, puzzleTypeId, skip, pageSize);

        var result = new List<PracticeSessionResponseDto>();
        foreach (var s in sessions)
        {
            var totalSolves = await _uow.Practice.CountSolvesAsync(s.Id);
            result.Add(MapSession(s, s.PuzzleType.Name, s.PuzzleType.Code, totalSolves));
        }

        return result;
    }

    public async Task<PracticeSessionSummaryDto> GetSessionDetailAsync(
        Guid userId, Guid sessionId)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);
        return await BuildSummaryAsync(session);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Attempt flow
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PracticeAttemptResponseDto> CreateAttemptAsync(Guid userId, Guid sessionId)
    {
        var session = await GetOwnedOpenSessionAsync(userId, sessionId);

        var active = await _uow.Practice.GetActiveAttemptAsync(sessionId);
        if (active != null)
            throw new CustomException(
                "ATTEMPT_ALREADY_ACTIVE",
                "Session đã có attempt đang diễn ra. Hãy hoàn thành hoặc abort attempt hiện tại.",
                400);

        var scramble = _scrambleGenerator.GenerateScramble(
            session.PuzzleType.Code,
            session.PuzzleType.ScrambleLength);

        var attempt = new PracticeAttempt
        {
            Id               = Guid.NewGuid(),
            SessionId        = sessionId,
            ScrambleSequence = scramble,
            State            = PracticeAttemptState.Scrambled,
            CreatedAt        = DateTime.UtcNow
        };

        await _uow.Practice.AddAttemptAsync(attempt);
        await _uow.SaveChangesAsync();

        var result = MapAttempt(attempt);
        await _notifier.NotifyPracticeAttemptUpdatedAsync(userId, result);
        return result;
    }

    public async Task<PracticeAttemptResponseDto?> GetCurrentAttemptAsync(
        Guid userId, Guid sessionId)
    {
        await GetOwnedSessionAsync(userId, sessionId);
        var attempt = await _uow.Practice.GetActiveAttemptAsync(sessionId);
        return attempt == null ? null : MapAttempt(attempt);
    }

    public async Task<PracticeAttemptResponseDto> GetAttemptAsync(Guid userId, Guid attemptId)
    {
        var attempt = await GetOwnedAttemptAsync(userId, attemptId);
        return MapAttempt(attempt);
    }

    public async Task<PracticeAttemptResponseDto> HandsOnAsync(Guid userId, Guid attemptId)
    {
        var attempt = await GetOwnedAttemptAsync(userId, attemptId);
        await GetOwnedOpenSessionAsync(userId, attempt.SessionId);

        switch (attempt.State)
        {
            case PracticeAttemptState.Scrambled:
            case PracticeAttemptState.HoldingHands:
            case PracticeAttemptState.Ready:
                attempt.State = PracticeAttemptState.HoldingHands;
                attempt.HandsOnAt = DateTime.UtcNow;
                break;

            case PracticeAttemptState.Solving:
            case PracticeAttemptState.Stopped:
                attempt.State = PracticeAttemptState.Stopped;
                attempt.StoppedAt = DateTime.UtcNow;
                break;

            default:
                throw InvalidTransition(attempt.State, "hands-on");
        }

        await _uow.SaveChangesAsync();
        var result = MapAttempt(attempt);
        await _notifier.NotifyPracticeAttemptUpdatedAsync(userId, result);
        return result;
    }

    public async Task<PracticeAttemptResponseDto> ReadyAsync(Guid userId, Guid attemptId)
    {
        var attempt = await GetOwnedAttemptAsync(userId, attemptId);
        await GetOwnedOpenSessionAsync(userId, attempt.SessionId);

        if (attempt.State != PracticeAttemptState.HoldingHands && attempt.State != PracticeAttemptState.Ready)
            throw InvalidTransition(attempt.State, "ready");

        attempt.State = PracticeAttemptState.Ready;
        attempt.ReadyAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync();

        var result = MapAttempt(attempt);
        await _notifier.NotifyPracticeAttemptUpdatedAsync(userId, result);
        return result;
    }

    public async Task<PracticeAttemptResponseDto> HandsOffAsync(Guid userId, Guid attemptId)
    {
        var attempt = await GetOwnedAttemptAsync(userId, attemptId);
        await GetOwnedOpenSessionAsync(userId, attempt.SessionId);

        if (attempt.State != PracticeAttemptState.Ready)
        {
            if (attempt.State == PracticeAttemptState.HoldingHands)
                throw new CustomException(
                    "HANDS_OFF_TOO_EARLY",
                    "Chưa sẵn sàng (đèn xanh). Hãy giữ tay đủ lâu rồi gọi ready trước khi nhấc tay.",
                    400);

            throw InvalidTransition(attempt.State, "hands-off");
        }

        attempt.State = PracticeAttemptState.Solving;
        attempt.StartedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync();

        var result = MapAttempt(attempt);
        await _notifier.NotifyPracticeAttemptUpdatedAsync(userId, result);
        return result;
    }

    public async Task<PracticeAttemptResponseDto> FinalizeAttemptAsync(
        Guid userId, Guid attemptId, FinalizeAttemptDto dto)
    {
        var attempt = await GetOwnedAttemptAsync(userId, attemptId);
        var session = await GetOwnedOpenSessionAsync(userId, attempt.SessionId);

        if (attempt.State != PracticeAttemptState.Stopped && attempt.State != PracticeAttemptState.Solving)
            throw InvalidTransition(attempt.State, "finalize");

        if (attempt.State == PracticeAttemptState.Solving)
        {
            attempt.StoppedAt = DateTime.UtcNow;
        }

        var penaltyCode = NormalizePenalty(dto.Penalty);
        var penaltyType = await ResolvePenaltyTypeAsync(penaltyCode);
        var isDnf = penaltyCode == "DNF";

        if (!isDnf)
        {
            if (dto.TimeMs <= 0 || dto.TimeMs > MaxSolveTimeMs)
                throw new CustomException(
                    "INVALID_TIME",
                    $"Thời gian không hợp lệ. Phải từ 1 đến {MaxSolveTimeMs} ms.",
                    400);
        }

        var rawTimeMs = isDnf ? 0 : dto.TimeMs;
        var displayTimeMs = PracticeAo5Calculator.GetDisplayTimeMs(rawTimeMs, isDnf, penaltyType);

        var solve = new PracticeSolve
        {
            Id               = Guid.NewGuid(),
            SessionId        = attempt.SessionId,
            AttemptId        = attempt.Id,
            ScrambleSequence = attempt.ScrambleSequence,
            TimeMs           = rawTimeMs,
            PenaltyTypeId    = penaltyType?.Id,
            IsDnf            = isDnf,
            SolvedAt         = DateTime.UtcNow
        };

        attempt.State = PracticeAttemptState.Completed;
        attempt.TimeMs = rawTimeMs;
        attempt.PenaltyTypeId = penaltyType?.Id;
        attempt.IsDnf = isDnf;
        attempt.CompletedAt = DateTime.UtcNow;
        attempt.PenaltyType = penaltyType;
        attempt.Solve = solve;

        await _uow.Practice.AddSolveAsync(solve);
        await _uow.SaveChangesAsync();

        int? ao5 = null;
        var recentSolves = await _uow.Practice.GetLatestSolvesAsync(attempt.SessionId, 5);
        if (recentSolves.Count == 5)
            ao5 = PracticeAo5Calculator.CalculateAo5(recentSolves.OrderBy(s => s.SolvedAt).ToList());

        var result = MapAttempt(attempt, solve.Id, displayTimeMs, ao5);
        await _notifier.NotifyPracticeAttemptUpdatedAsync(userId, result);
        return result;
    }

    public async Task<PracticeAttemptResponseDto> AbortAttemptAsync(
        Guid userId, Guid attemptId, AbortAttemptDto? dto)
    {
        var attempt = await GetOwnedAttemptAsync(userId, attemptId);
        await GetOwnedOpenSessionAsync(userId, attempt.SessionId);

        if (attempt.State is PracticeAttemptState.Completed or PracticeAttemptState.Aborted)
            throw InvalidTransition(attempt.State, "abort");

        attempt.State = PracticeAttemptState.Aborted;
        attempt.AbortReason = dto?.Reason;
        attempt.CompletedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync();

        var result = MapAttempt(attempt);
        await _notifier.NotifyPracticeAttemptUpdatedAsync(userId, result);
        return result;
    }

    public async Task ConnectSessionAsync(Guid userId, Guid sessionId)
    {
        await GetOwnedOpenSessionAsync(userId, sessionId);
        await _notifier.NotifyPracticeMobileConnectedAsync(userId, sessionId);
    }

    public async Task DisconnectSessionAsync(Guid userId, Guid sessionId)
    {
        await GetOwnedSessionAsync(userId, sessionId);
        await _notifier.NotifyPracticeMobileDisconnectedAsync(userId, sessionId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PracticeSession> GetOwnedSessionAsync(Guid userId, Guid sessionId)
    {
        var session = await _uow.Practice.GetSessionByIdAsync(sessionId)
            ?? throw new KeyNotFoundException("Không tìm thấy session.");

        if (session.UserId != userId)
            throw new UnauthorizedAccessException("Session không thuộc về bạn.");

        return session;
    }

    private async Task<PracticeSession> GetOwnedOpenSessionAsync(Guid userId, Guid sessionId)
    {
        var session = await GetOwnedSessionAsync(userId, sessionId);

        if (session.EndedAt.HasValue)
            throw new CustomException("SESSION_ENDED", "Session đã kết thúc.", 400);

        return session;
    }

    private async Task<PracticeAttempt> GetOwnedAttemptAsync(Guid userId, Guid attemptId)
    {
        var attempt = await _uow.Practice.GetAttemptByIdAsync(attemptId)
            ?? throw new KeyNotFoundException("Không tìm thấy attempt.");

        var session = await _uow.Practice.GetSessionByIdAsync(attempt.SessionId)
            ?? throw new KeyNotFoundException("Không tìm thấy session.");

        if (session.UserId != userId)
            throw new UnauthorizedAccessException("Attempt không thuộc về bạn.");

        return attempt;
    }

    private static CustomException InvalidTransition(PracticeAttemptState state, string action)
        => new(
            "INVALID_STATE_TRANSITION",
            $"Không thể thực hiện '{action}' khi attempt đang ở trạng thái '{state}'.",
            400);

    private async Task<PenaltyType?> ResolvePenaltyTypeAsync(string penaltyCode)
    {
        if (penaltyCode == "OK")
            return null;

        return await _uow.Practice.GetPenaltyTypeByCodeAsync(penaltyCode)
            ?? throw new CustomException(
                "INVALID_PENALTY",
                $"Penalty '{penaltyCode}' không tồn tại trong hệ thống.",
                400);
    }

    private async Task<PracticeSessionSummaryDto> BuildSummaryAsync(PracticeSession session)
    {
        var solves = await _uow.Practice.GetAllSolvesAsync(session.Id);

        var solveDtos = new List<PracticeSolveResponseDto>();
        for (int i = 0; i < solves.Count; i++)
        {
            var s = solves[i];
            var display = PracticeAo5Calculator.GetDisplayTimeMs(s);

            int? ao5 = null;
            if (i >= 4)
            {
                var window = solves.Skip(i - 4).Take(5).ToList();
                ao5 = PracticeAo5Calculator.CalculateAo5(window);
            }

            solveDtos.Add(new PracticeSolveResponseDto
            {
                Id               = s.Id,
                SessionId        = s.SessionId,
                AttemptId        = s.AttemptId,
                ScrambleSequence = s.ScrambleSequence,
                TimeMs           = s.TimeMs,
                PenaltyCode      = s.PenaltyType?.Code,
                DisplayTimeMs    = PracticeAo5Calculator.ToUiDisplayTimeMs(display),
                SolvedAt         = s.SolvedAt,
                CurrentAo5Ms     = ao5
            });
        }

        var validSolves = solves.Where(s => !s.IsDnf).ToList();
        int? meanMs = validSolves.Count > 0
            ? (int)validSolves.Average(s =>
                PracticeAo5Calculator.ToUiDisplayTimeMs(PracticeAo5Calculator.GetDisplayTimeMs(s)))
            : null;
        int? bestMs = validSolves.Count > 0
            ? validSolves.Min(s =>
                PracticeAo5Calculator.ToUiDisplayTimeMs(PracticeAo5Calculator.GetDisplayTimeMs(s)))
            : null;

        int? bestAo5 = null;
        for (int i = 4; i < solves.Count; i++)
        {
            var window = solves.Skip(i - 4).Take(5).ToList();
            var ao5 = PracticeAo5Calculator.CalculateAo5(window);
            if (ao5.HasValue && (!bestAo5.HasValue || ao5.Value < bestAo5.Value))
                bestAo5 = ao5;
        }

        return new PracticeSessionSummaryDto
        {
            SessionId      = session.Id,
            PuzzleTypeCode = session.PuzzleType.Code,
            StartedAt      = session.StartedAt,
            EndedAt        = session.EndedAt ?? DateTime.UtcNow,
            TotalSolves    = solves.Count,
            DnfCount       = solves.Count(s => s.IsDnf),
            MeanMs         = meanMs,
            BestMs         = bestMs,
            BestAo5Ms      = bestAo5,
            Solves         = solveDtos
        };
    }

    private static PracticeSessionResponseDto MapSession(
        PracticeSession session, string puzzleName, string puzzleCode, int totalSolves)
        => new()
        {
            Id             = session.Id,
            UserId         = session.UserId,
            PuzzleTypeId   = session.PuzzleTypeId,
            PuzzleTypeName = puzzleName,
            PuzzleTypeCode = puzzleCode,
            StartedAt      = session.StartedAt,
            EndedAt        = session.EndedAt,
            TotalSolves    = totalSolves
        };

    private static PracticeAttemptResponseDto MapAttempt(
        PracticeAttempt attempt,
        Guid? solveId = null,
        int? displayTimeMs = null,
        int? currentAo5Ms = null)
    {
        var display = displayTimeMs
            ?? (attempt.PenaltyType != null || attempt.IsDnf
                ? PracticeAo5Calculator.GetDisplayTimeMs(
                    attempt.TimeMs ?? 0, attempt.IsDnf, attempt.PenaltyType)
                : (int?)null);

        return new PracticeAttemptResponseDto
        {
            Id               = attempt.Id,
            SessionId        = attempt.SessionId,
            State            = attempt.State.ToString(),
            ScrambleSequence = attempt.ScrambleSequence,
            HandsOnAt        = attempt.HandsOnAt,
            ReadyAt          = attempt.ReadyAt,
            StartedAt        = attempt.StartedAt,
            StoppedAt        = attempt.StoppedAt,
            AllowedActions   = GetAllowedActions(attempt.State),
            SolveId          = solveId ?? attempt.Solve?.Id,
            TimeMs           = attempt.TimeMs,
            PenaltyCode      = attempt.PenaltyType?.Code,
            DisplayTimeMs    = display.HasValue
                ? PracticeAo5Calculator.ToUiDisplayTimeMs(display.Value)
                : null,
            CurrentAo5Ms     = currentAo5Ms,
            AbortReason      = attempt.AbortReason
        };
    }

    private static IReadOnlyList<string> GetAllowedActions(PracticeAttemptState state)
        => state switch
        {
            PracticeAttemptState.Scrambled     => ["hands-on", "abort"],
            PracticeAttemptState.HoldingHands  => ["ready", "abort"],
            PracticeAttemptState.Ready         => ["hands-off", "abort"],
            PracticeAttemptState.Solving       => ["hands-on", "abort"],
            PracticeAttemptState.Stopped       => ["finalize", "abort"],
            _                                  => []
        };

    private static string NormalizePenalty(string? penalty)
    {
        if (string.IsNullOrWhiteSpace(penalty))
            return "OK";

        var upper = penalty.Trim().ToUpperInvariant();
        if (!ValidPenalties.Contains(upper))
            throw new CustomException(
                "INVALID_PENALTY",
                $"Penalty không hợp lệ: '{penalty}'. Chỉ chấp nhận: OK, PLUS_2, DNF.",
                400);

        return upper;
    }
}
