using CubeNexus.Application.DTOs.Practice;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

/// <summary>
/// Xử lý luồng tập luyện của Competitor:
///   1. Bắt đầu session → ghi nhận puzzle type
///   2. Submit từng solve → tính Ao5 rolling
///   3. Kết thúc session → tổng kết stats
/// </summary>
public class PracticeService : IPracticeService
{
    private readonly IUnitOfWork _uow;

    // Các mã penalty hợp lệ (không phân biệt hoa/thường khi nhận vào)
    private static readonly HashSet<string> ValidPenalties =
        new(StringComparer.OrdinalIgnoreCase) { "OK", "PLUS_2", "DNF" };

    public PracticeService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Bắt đầu session
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PracticeSessionResponseDto> StartSessionAsync(
        Guid userId, StartPracticeSessionDto dto)
    {
        // Kiểm tra puzzle type tồn tại và đang active
        var puzzleType = await _uow.PuzzleTypes.GetByIdAsync(dto.PuzzleTypeId)
            ?? throw new KeyNotFoundException("Không tìm thấy loại rubik này.");

        if (!puzzleType.IsActive)
            throw new InvalidOperationException("Loại rubik này hiện không hoạt động.");

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

        return new PracticeSessionResponseDto
        {
            Id              = session.Id,
            UserId          = session.UserId,
            PuzzleTypeId    = session.PuzzleTypeId,
            PuzzleTypeName  = puzzleType.Name,
            PuzzleTypeCode  = puzzleType.Code,
            StartedAt       = session.StartedAt,
            EndedAt         = session.EndedAt,
            TotalSolves     = 0
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Submit một lần giải
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PracticeSolveResponseDto> SubmitSolveAsync(
        Guid userId, SubmitSolveDto dto)
    {
        // Validate session
        var session = await _uow.Practice.GetSessionByIdAsync(dto.SessionId)
            ?? throw new KeyNotFoundException("Không tìm thấy session.");

        if (session.UserId != userId)
            throw new UnauthorizedAccessException("Session không thuộc về bạn.");

        if (session.EndedAt.HasValue)
            throw new InvalidOperationException("Session đã kết thúc, không thể ghi thêm solve.");

        // Validate & normalize penalty
        var penaltyCode = NormalizePenalty(dto.Penalty);

        // Tính thời gian hiển thị
        var displayTimeMs = CalculateDisplayTime(dto.TimeMs, penaltyCode);

        var solve = new PracticeSolve
        {
            Id               = Guid.NewGuid(),
            SessionId        = dto.SessionId,
            ScrambleSequence = dto.ScrambleSequence,
            TimeMs           = dto.TimeMs,
            PenaltyTypeId    = null,      // Không còn dùng FK sang penalty_types
            IsDnf            = penaltyCode == "DNF",
            SolvedAt         = DateTime.UtcNow
        };

        await _uow.Practice.AddSolveAsync(solve);
        await _uow.SaveChangesAsync();

        // Tính Ao5 rolling từ 5 solve gần nhất
        var recentSolves = await _uow.Practice.GetLatestSolvesAsync(dto.SessionId, 5);
        int? ao5 = null;

        if (recentSolves.Count == 5)
        {
            ao5 = CalculateAo5(recentSolves);
        }

        return new PracticeSolveResponseDto
        {
            Id               = solve.Id,
            SessionId        = solve.SessionId,
            ScrambleSequence = solve.ScrambleSequence,
            TimeMs           = solve.TimeMs,
            PenaltyCode      = penaltyCode == "OK" ? null : penaltyCode,
            DisplayTimeMs    = displayTimeMs,
            SolvedAt         = solve.SolvedAt,
            CurrentAo5Ms     = ao5
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Kết thúc session
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PracticeSessionSummaryDto> EndSessionAsync(
        Guid userId, Guid sessionId)
    {
        var session = await _uow.Practice.GetSessionByIdAsync(sessionId)
            ?? throw new KeyNotFoundException("Không tìm thấy session.");

        if (session.UserId != userId)
            throw new UnauthorizedAccessException("Session không thuộc về bạn.");

        if (!session.EndedAt.HasValue)
        {
            session.EndedAt = DateTime.UtcNow;
            await _uow.SaveChangesAsync();
        }

        return await BuildSummaryAsync(session);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Lịch sử session
    // ─────────────────────────────────────────────────────────────────────────

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
            result.Add(new PracticeSessionResponseDto
            {
                Id             = s.Id,
                UserId         = s.UserId,
                PuzzleTypeId   = s.PuzzleTypeId,
                PuzzleTypeName = s.PuzzleType.Name,
                PuzzleTypeCode = s.PuzzleType.Code,
                StartedAt      = s.StartedAt,
                EndedAt        = s.EndedAt,
                TotalSolves    = totalSolves
            });
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Chi tiết session
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PracticeSessionSummaryDto> GetSessionDetailAsync(
        Guid userId, Guid sessionId)
    {
        var session = await _uow.Practice.GetSessionByIdAsync(sessionId)
            ?? throw new KeyNotFoundException("Không tìm thấy session.");

        if (session.UserId != userId)
            throw new UnauthorizedAccessException("Session không thuộc về bạn.");

        return await BuildSummaryAsync(session);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PracticeSessionSummaryDto> BuildSummaryAsync(PracticeSession session)
    {
        var solves = await _uow.Practice.GetAllSolvesAsync(session.Id);

        // Map solves với rolling Ao5
        var solveDtos = new List<PracticeSolveResponseDto>();
        for (int i = 0; i < solves.Count; i++)
        {
            var s = solves[i];
            var penaltyCode = s.IsDnf ? "DNF" : null; // simplified (stored as bool)

            int? ao5 = null;
            if (i >= 4)
            {
                var window = solves.Skip(i - 4).Take(5).ToList();
                ao5 = CalculateAo5(window);
            }

            solveDtos.Add(new PracticeSolveResponseDto
            {
                Id               = s.Id,
                SessionId        = s.SessionId,
                ScrambleSequence = s.ScrambleSequence,
                TimeMs           = s.TimeMs,
                PenaltyCode      = penaltyCode,
                DisplayTimeMs    = s.IsDnf ? -1 : s.TimeMs,
                SolvedAt         = s.SolvedAt,
                CurrentAo5Ms     = ao5
            });
        }

        // Stats (exclude DNF)
        var validSolves = solves.Where(s => !s.IsDnf).ToList();
        int? meanMs = validSolves.Count > 0
            ? (int)validSolves.Average(s => s.TimeMs)
            : null;
        int? bestMs = validSolves.Count > 0
            ? validSolves.Min(s => s.TimeMs)
            : null;

        // Best Ao5 across all windows
        int? bestAo5 = null;
        for (int i = 4; i < solves.Count; i++)
        {
            var window = solves.Skip(i - 4).Take(5).ToList();
            var ao5 = CalculateAo5(window);
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

    /// <summary>
    /// Tính Ao5 theo chuẩn WCA:
    /// - Loại 1 solve tốt nhất và 1 solve tệ nhất
    /// - DNF = thời gian vô cực; nếu có 2+ DNF thì Ao5 = null (DNF)
    /// - Trung bình 3 solve còn lại
    /// </summary>
    private static int? CalculateAo5(IReadOnlyList<PracticeSolve> window)
    {
        if (window.Count != 5) return null;

        // Thời gian hiển thị: DNF = int.MaxValue
        var times = window.Select(s => s.IsDnf ? int.MaxValue : s.TimeMs).ToList();

        var dnfCount = times.Count(t => t == int.MaxValue);
        if (dnfCount >= 2) return null; // Ao5 = DNF

        times.Sort();
        // Bỏ best (index 0) và worst (index 4)
        var middle3 = times.Skip(1).Take(3).ToList();

        return (int)middle3.Average();
    }

    /// <summary>Normalize penalty về UPPERCASE; mặc định "OK" nếu null/rỗng</summary>
    private static string NormalizePenalty(string? penalty)
    {
        if (string.IsNullOrWhiteSpace(penalty))
            return "OK";

        var upper = penalty.Trim().ToUpperInvariant();

        if (!ValidPenalties.Contains(upper))
            throw new ArgumentException(
                $"Penalty không hợp lệ: '{penalty}'. Chỉ chấp nhận: OK, PLUS_2, DNF.");

        return upper;
    }

    /// <summary>Tính thời gian hiển thị dựa trên penalty. -1 nếu DNF.</summary>
    private static int CalculateDisplayTime(int timeMs, string penaltyCode)
        => penaltyCode switch
        {
            "PLUS_2" => timeMs + 2000,
            "DNF"    => -1,
            _        => timeMs
        };
}
