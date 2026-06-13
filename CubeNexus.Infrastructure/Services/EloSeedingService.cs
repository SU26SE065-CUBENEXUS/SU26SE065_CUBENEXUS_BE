using CubeNexus.Application.DTOs.Arena;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Services;

namespace CubeNexus.Infrastructure.Services;

/// <summary>
/// Triển khai Giai đoạn 1: Seeding từ Practice Ao5.
///
/// Luồng:
///   1. Kiểm tra số lượt giải Practice hợp lệ qua IEloSeedingRepository.
///   2. Nếu đủ → tính Ao5 theo chuẩn WCA (loại best + worst, average 3 giữa).
///   3. Tra ngưỡng Elo qua IEloSeedingRepository.
///   4. Lưu PracticeAo5Snapshot.
///   5. Khởi tạo OnlineProfile với seed_elo, k_factor = k_factor_placement.
///   6. Ghi EloHistory với reason_code = 'SEEDING_INIT'.
///   7. Commit qua IUnitOfWork.SaveChangesAsync().
/// </summary>
public class EloSeedingService : IEloSeedingService
{
    private readonly IUnitOfWork _uow;

    public EloSeedingService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // =========================================================
    // GetPracticeStatusAsync
    // =========================================================
    public async Task<PracticeStatusDto> GetPracticeStatusAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var solves = await _uow.EloSeeding.GetValidPracticeSolvesAsync(userId, puzzleTypeId, ct);

        int solvesCount = solves.Count;
        bool isEligible = solvesCount >= config.MinPracticeSolves;

        int? latestAo5Ms = null;
        string? latestAo5Display = null;
        int? expectedSeedElo = null;

        if (isEligible)
        {
            var recent5 = await _uow.EloSeeding.GetRecentPracticeSolvesAsync(
                userId, puzzleTypeId, 5, ct);
            latestAo5Ms = PracticeAo5Calculator.CalculateAo5(
                recent5.OrderBy(s => s.SolvedAt).ToList());

            if (latestAo5Ms.HasValue)
            {
                latestAo5Display = FormatMs(latestAo5Ms.Value);
                var threshold = await _uow.EloSeeding
                    .GetMatchingThresholdAsync(puzzleTypeId, latestAo5Ms.Value, ct);
                expectedSeedElo = threshold?.EloValue ?? config.DefaultElo;
            }
        }

        // Lấy thông tin Online Profile (nếu đã khởi tạo)
        var profile = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(userId, puzzleTypeId, ct);

        bool hasProfile          = profile != null;
        bool canJoinPvp          = hasProfile;          // Chỉ cần có profile là vào được PVP
        bool isPlacementComplete = profile?.IsPlacementComplete ?? false;
        int  placementDone       = profile?.PlacementMatchesDone ?? 0;
        bool canInitProfile      = isEligible && !hasProfile;

        // Xác định stage hiện tại
        string stage;
        string hint;

        if (!hasProfile)
        {
            stage = "PRACTICE";
            if (!isEligible)
                hint = $"Hãy tập luyện thêm. Bạn đang có {solvesCount}/{config.MinPracticeSolves} lượt giải hợp lệ.";
            else
                hint = "Đã đủ điều kiện! Gọi /api/elo-seeding/calculate-ao5 rồi /api/elo-seeding/initialize-profile để mở khóa PVP.";
        }
        else if (!isPlacementComplete)
        {
            stage = "PLACEMENT";
            int remaining = config.PlacementMatchCount - placementDone;
            hint  = $"Đang trong giai đoạn Placement. Hoàn thành thêm {remaining} trận PVP nữa để Elo được công khai.";
        }
        else
        {
            stage = "STANDARD";
            hint  = "Elo đã được công khai. Tiếp tục thi đấu để leo rank!";
        }

        return new PracticeStatusDto
        {
            SolvesCount           = solvesCount,
            RequiredSolves        = config.MinPracticeSolves,
            IsEligibleForSeeding  = isEligible,
            CanInitializeProfile  = canInitProfile,
            LatestAo5Ms           = latestAo5Ms,
            LatestAo5Display      = latestAo5Display,
            ExpectedSeedElo       = expectedSeedElo,
            HasOnlineProfile      = hasProfile,
            CanJoinPvp            = canJoinPvp,
            PlacementMatchesDone  = placementDone,
            PlacementMatchCount   = config.PlacementMatchCount,
            IsPlacementComplete   = isPlacementComplete,
            CurrentStage          = stage,
            NextStepHint          = hint
        };
    }

    // =========================================================
    // CalculateAndSaveAo5Async
    // =========================================================
    public async Task<PracticeAo5Snapshot?> CalculateAndSaveAo5Async(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var solves = await _uow.EloSeeding.GetValidPracticeSolvesAsync(userId, puzzleTypeId, ct);

        if (solves.Count < config.MinPracticeSolves)
            return null;

        var recent5 = await _uow.EloSeeding.GetRecentPracticeSolvesAsync(
            userId, puzzleTypeId, 5, ct);
        if (recent5.Count < 5)
            return null;

        int? ao5Ms = PracticeAo5Calculator.CalculateAo5(
            recent5.OrderBy(s => s.SolvedAt).ToList());
        if (!ao5Ms.HasValue) return null;

        var threshold = await _uow.EloSeeding
            .GetMatchingThresholdAsync(puzzleTypeId, ao5Ms.Value, ct);
        int assignedElo = threshold?.EloValue ?? config.DefaultElo;

        var snapshot = new PracticeAo5Snapshot
        {
            Id               = Guid.NewGuid(),
            UserId           = userId,
            PuzzleTypeId     = puzzleTypeId,
            Ao5TimeMs        = ao5Ms.Value,
            AssignedElo      = assignedElo,
            SeedThresholdId  = threshold?.Id,
            CalculatedAt     = DateTime.UtcNow,
            IsUsedForSeeding = false
        };

        _uow.EloSeeding.AddSnapshot(snapshot);
        await _uow.SaveChangesAsync(ct);

        return snapshot;
    }

    // =========================================================
    // InitializeOnlineProfileAsync
    // =========================================================
    public async Task<OnlineProfile> InitializeOnlineProfileAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        bool exists = await _uow.OnlineProfiles
            .AnyAsync(p => p.UserId == userId && p.PuzzleTypeId == puzzleTypeId, ct);

        if (exists)
            throw new InvalidOperationException(
                "Online Profile đã tồn tại. Bạn đã sẵn sàng tham gia PVP.");

        var config   = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var snapshot = await _uow.EloSeeding.GetUnusedAo5SnapshotAsync(userId, puzzleTypeId, ct);

        // ═══════════════════════════════════════════════════════════════════
        // ENFORCEMENT: Người chơi BẮT BUỘC phải hoàn thành Practice Ao5
        // trước khi được phép tham gia PVP Online Arena.
        //
        // Nếu không có Ao5 Snapshot hợp lệ → từ chối khởi tạo profile.
        // (Trước đây có thể dùng DEFAULT seed – đã bỏ để đảm bảo tính
        //  cạnh tranh công bằng trong giai đoạn Placement.)
        // ═══════════════════════════════════════════════════════════════════
        if (snapshot == null)
        {
            var solves = await _uow.EloSeeding.GetValidPracticeSolvesAsync(userId, puzzleTypeId, ct);
            int solvesCount = solves.Count;

            if (solvesCount < config.MinPracticeSolves)
                throw new InvalidOperationException(
                    $"Bạn cần hoàn thành ít nhất {config.MinPracticeSolves} lượt giải Practice hợp lệ " +
                    $"để seeding Elo trước khi tham gia PVP. Hiện tại bạn có {solvesCount} lượt. " +
                    $"Hãy tập luyện thêm tại /api/practice.");

            throw new InvalidOperationException(
                $"Bạn đã có {solvesCount} lượt giải nhưng chưa tính Ao5 seeding. " +
                $"Hãy gọi POST /api/elo-seeding/calculate-ao5 trước để tạo snapshot Elo.");
        }

        int    seedElo        = snapshot.AssignedElo;
        string seedSource     = "PRACTICE";
        int?   practiceAo5Ms  = snapshot.Ao5TimeMs;
        Guid?  snapshotId     = snapshot.Id;
        snapshot.IsUsedForSeeding = true;  // Đánh dấu đã dùng

        var now = DateTime.UtcNow;

        var profile = new OnlineProfile
        {
            Id                    = Guid.NewGuid(),
            UserId                = userId,
            PuzzleTypeId          = puzzleTypeId,
            Elo                   = seedElo,
            PeakElo               = seedElo,
            SeedElo               = seedElo,
            SeedSourceCode        = seedSource,
            PracticeAo5Ms         = practiceAo5Ms,
            PracticeAo5SnapshotId = snapshotId,
            PlacementMatchesDone  = 0,
            IsPlacementComplete   = false,
            PlacementCompletedAt  = null,
            KFactorCurrent        = config.KFactorPlacement,
            TotalWins             = 0,
            TotalLosses           = 0,
            TotalDraws            = 0,
            CreatedAt             = now,
            UpdatedAt             = now
        };

        _uow.OnlineProfiles.Add(profile);

        // Ghi lịch sử khởi tạo Elo (EloBefore = 0 vì chưa có profile)
        _uow.EloHistories.Add(new EloHistory
        {
            Id              = Guid.NewGuid(),
            OnlineProfileId = profile.Id,
            MatchId         = null,
            EloBefore       = 0,
            EloAfter        = seedElo,
            Delta           = seedElo,
            KFactorUsed     = null,
            ActualScore     = null,
            ExpectedScore   = null,
            IsPlacementMatch = false,
            ReasonCode      = "SEEDING_INIT",
            ChangedAt       = now
        });

        await _uow.SaveChangesAsync(ct);

        return profile;
    }

    // =========================================================
    // Private Helpers (Pure business logic – không phụ thuộc DB)
    // =========================================================

    private static string FormatMs(int ms) => $"{ms / 1000.0:F2}s";
}
