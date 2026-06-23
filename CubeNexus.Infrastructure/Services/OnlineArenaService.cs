using CubeNexus.Application.DTOs.Arena;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

/// <summary>
/// Triển khai Giai đoạn 2 &amp; 3: Placement Phase và Elo ổn định.
///
/// Công thức Elo:
///   E_A = 1 / (1 + 10^((R_B - R_A) / 400))
///   R'_A = R_A + K * (S_A - E_A)
///   S: 1.0 (thắng), 0.0 (thua), 0.5 (hòa)
/// </summary>
public class OnlineArenaService : IOnlineArenaService
{
    private readonly IUnitOfWork _uow;

    public OnlineArenaService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // =========================================================
    // RecordMatchResultAsync
    // =========================================================
    public async Task<MatchResultDto> RecordMatchResultAsync(
        Guid matchId, Guid? winnerId, CancellationToken ct = default)
    {
        var match = await _uow.OnlineMatches.GetByIdAsync(matchId, ct)
            ?? throw new InvalidOperationException($"Không tìm thấy trận đấu {matchId}.");

        if (match.EndedAt != null && match.Player1EloAfter != null)
            throw new InvalidOperationException("Kết quả trận đấu này đã được ghi nhận.");

        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);

        var profile1 = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(match.Player1Id, match.PuzzleTypeId, ct)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy online profile của player1 ({match.Player1Id}).");

        var profile2 = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(match.Player2Id, match.PuzzleTypeId, ct)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy online profile của player2 ({match.Player2Id}).");

        // Xác định kết quả (S)
        (decimal s1, decimal s2) = winnerId switch
        {
            null                            => (0.5m, 0.5m),           // Hòa
            var w when w == match.Player1Id => (1.0m, 0.0m),           // P1 thắng
            _                               => (0.0m, 1.0m)            // P2 thắng
        };

        // Tính Expected Score: E = 1 / (1 + 10^((Rb-Ra)/400))
        decimal e1 = CalculateExpectedScore(profile1.Elo, profile2.Elo);
        decimal e2 = 1.0m - e1;

        int eloBefore1 = profile1.Elo;
        int eloBefore2 = profile2.Elo;
        int k1 = profile1.KFactorCurrent;
        int k2 = profile2.KFactorCurrent;

        // Tính Elo mới: R' = R + K * (S - E), tối thiểu 0
        int eloAfter1 = Math.Max(0, (int)Math.Round(eloBefore1 + k1 * (s1 - e1)));
        int eloAfter2 = Math.Max(0, (int)Math.Round(eloBefore2 + k2 * (s2 - e2)));

        bool wasPlacement1 = !profile1.IsPlacementComplete;
        bool wasPlacement2 = !profile2.IsPlacementComplete;

        var now = DateTime.UtcNow;

        // Cập nhật profile (bao gồm kiểm tra hoàn thành Placement)
        bool p1PlacementJustDone = UpdateProfile(profile1, s1, eloAfter1, config, now);
        bool p2PlacementJustDone = UpdateProfile(profile2, s2, eloAfter2, config, now);

        // Cập nhật thông tin trận
        match.WinnerId       = winnerId;
        match.EndedAt        = now;
        match.Player1EloAfter = eloAfter1;
        match.Player2EloAfter = eloAfter2;
        if (match.Player1EloBefore == null) match.Player1EloBefore = eloBefore1;
        if (match.Player2EloBefore == null) match.Player2EloBefore = eloBefore2;

        _uow.OnlineMatches.Update(match);

        // Ghi EloHistory cho cả 2 người chơi
        _uow.EloHistories.Add(BuildEloHistory(
            profile1.Id, matchId, eloBefore1, eloAfter1, k1, s1, e1, wasPlacement1, now));
        _uow.EloHistories.Add(BuildEloHistory(
            profile2.Id, matchId, eloBefore2, eloAfter2, k2, s2, e2, wasPlacement2, now));

        await _uow.SaveChangesAsync(ct);

        return new MatchResultDto
        {
            MatchId                  = matchId,
            IsPlacementMatch         = wasPlacement1 || wasPlacement2,
            Player1PlacementCompleted = p1PlacementJustDone,
            Player2PlacementCompleted = p2PlacementJustDone,
            Player1 = BuildPlayerChange(profile1, eloBefore1, eloAfter1, k1, s1, e1),
            Player2 = BuildPlayerChange(profile2, eloBefore2, eloAfter2, k2, s2, e2)
        };
    }

    // =========================================================
    // GetPlayerProfileAsync
    // =========================================================
    public async Task<OnlineProfileDto?> GetPlayerProfileAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var config  = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var profile = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(userId, puzzleTypeId, ct);

        if (profile is null) return null;

        int total = profile.TotalWins + profile.TotalLosses + profile.TotalDraws;
        double winRate = total > 0
            ? Math.Round((double)profile.TotalWins / total * 100, 1)
            : 0;

        return new OnlineProfileDto
        {
            UserId               = profile.UserId,
            PuzzleTypeId         = profile.PuzzleTypeId,
            PuzzleTypeName       = profile.PuzzleType.Name,
            // Elo ẩn trong Placement Phase
            EloVisible           = profile.IsPlacementComplete ? profile.Elo : null,
            PeakElo              = profile.PeakElo,
            SeedElo              = profile.SeedElo,
            SeedSourceCode       = profile.SeedSourceCode,
            PracticeAo5Ms        = profile.PracticeAo5Ms,
            PlacementMatchesDone = profile.PlacementMatchesDone,
            PlacementMatchCount  = config.PlacementMatchCount,
            IsPlacementComplete  = profile.IsPlacementComplete,
            PlacementCompletedAt = profile.PlacementCompletedAt,
            TotalWins            = profile.TotalWins,
            TotalLosses          = profile.TotalLosses,
            TotalDraws           = profile.TotalDraws,
            WinRate              = winRate,
            CreatedAt            = profile.CreatedAt
        };
    }

    // =========================================================
    // GetLeaderboardAsync
    // =========================================================
    public async Task<LeaderboardResponseDto> GetLeaderboardAsync(
        Guid puzzleTypeId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await _uow.OnlineProfiles
            .GetLeaderboardAsync(puzzleTypeId, page, pageSize, ct);

        var entries = items.Select((p, i) =>
        {
            int total = p.TotalWins + p.TotalLosses + p.TotalDraws;
            double wr = total > 0
                ? Math.Round((double)p.TotalWins / total * 100, 1)
                : 0;

            return new LeaderboardEntryDto
            {
                Rank                 = (page - 1) * pageSize + i + 1,
                UserId               = p.UserId,
                DisplayName          = p.User.DisplayName,
                AvatarUrl            = p.User.AvatarUrl,
                Elo                  = p.Elo,
                PeakElo              = p.PeakElo,
                TotalWins            = p.TotalWins,
                TotalLosses          = p.TotalLosses,
                TotalDraws           = p.TotalDraws,
                WinRate              = wr,
                PlacementCompletedAt = p.PlacementCompletedAt
            };
        }).ToList();

        return new LeaderboardResponseDto
        {
            Entries    = entries,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize
        };
    }

    // =========================================================
    // GetPlayerEligibilityAsync
    // =========================================================
    public async Task<PlayerEligibilityDto> GetPlayerEligibilityAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var config  = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var profile = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(userId, puzzleTypeId, ct);

        // Người chơi chưa seeding → không thể vào PVP
        if (profile is null)
        {
            return new PlayerEligibilityDto
            {
                UserId               = userId,
                PuzzleTypeId         = puzzleTypeId,
                CanJoinPvp           = false,
                BlockReason          = "Bạn chưa hoàn thành Practice Ao5 seeding. " +
                                       "Hãy tập luyện ≥5 lượt giải tại /api/practice, " +
                                       "tính Ao5 tại /api/elo-seeding/calculate-ao5, " +
                                       "rồi khởi tạo profile tại /api/elo-seeding/initialize-profile.",
                HasOnlineProfile     = false,
                IsPlacementComplete  = false,
                PlacementMatchesDone = 0,
                PlacementMatchCount  = config.PlacementMatchCount,
                HiddenElo            = null,
                PublicElo            = null,
                CurrentStage         = "NO_PROFILE",
                NextStepHint         = "Hoàn thành Practice Ao5 seeding để mở khóa Online PVP."
            };
        }

        bool isPlacementComplete = profile.IsPlacementComplete;
        int  placementDone       = profile.PlacementMatchesDone;
        int  remaining           = config.PlacementMatchCount - placementDone;

        string stage;
        string hint;

        if (!isPlacementComplete)
        {
            stage = "PLACEMENT";
            hint  = $"Đang trong giai đoạn Placement (Elo ẩn). " +
                    $"Hoàn thành thêm {remaining} trận PVP nữa để Elo được công khai trên bảng xếp hạng.";
        }
        else
        {
            stage = "STANDARD";
            hint  = "Elo đã được công khai. Tiếp tục thi đấu để leo rank!";
        }

        return new PlayerEligibilityDto
        {
            UserId               = userId,
            PuzzleTypeId         = puzzleTypeId,
            CanJoinPvp           = true,        // Có profile = được vào PVP
            BlockReason          = null,
            HasOnlineProfile     = true,
            IsPlacementComplete  = isPlacementComplete,
            PlacementMatchesDone = placementDone,
            PlacementMatchCount  = config.PlacementMatchCount,
            // Elo ẩn hiển thị trong giai đoạn Placement, công khai sau
            HiddenElo            = !isPlacementComplete ? profile.Elo : null,
            PublicElo            = isPlacementComplete ? profile.Elo : null,
            CurrentStage         = stage,
            NextStepHint         = hint
        };
    }

    // =========================================================
    // Private Helpers (Pure logic – không phụ thuộc DB)
    // =========================================================

    /// <summary>E_A = 1 / (1 + 10^((R_B - R_A) / 400))</summary>
    private static decimal CalculateExpectedScore(int ra, int rb)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10, (rb - ra) / 400.0));
        return (decimal)Math.Round(expected, 4);
    }

    /// <summary>
    /// Cập nhật profile sau trận.
    /// Trả về TRUE nếu Placement vừa hoàn thành trong trận này.
    /// </summary>
    private static bool UpdateProfile(
        OnlineProfile profile,
        decimal actualScore,
        int newElo,
        EloConfig config,
        DateTime now)
    {
        bool wasComplete = profile.IsPlacementComplete;

        profile.Elo = newElo;
        if (newElo > profile.PeakElo) profile.PeakElo = newElo;

        if      (actualScore == 1.0m) profile.TotalWins++;
        else if (actualScore == 0.0m) profile.TotalLosses++;
        else                          profile.TotalDraws++;

        if (!profile.IsPlacementComplete)
        {
            profile.PlacementMatchesDone++;

            if (profile.PlacementMatchesDone >= config.PlacementMatchCount)
            {
                // Giai đoạn 3: Chốt hạng – hạ K về standard
                profile.IsPlacementComplete  = true;
                profile.PlacementCompletedAt = now;
                profile.KFactorCurrent       = config.KFactorStandard;
            }
        }

        profile.UpdatedAt = now;
        return !wasComplete && profile.IsPlacementComplete;
    }

    private static EloHistory BuildEloHistory(
        Guid profileId, Guid matchId,
        int before, int after,
        int k, decimal s, decimal e,
        bool isPlacement, DateTime now)
    {
        return new EloHistory
        {
            Id              = Guid.NewGuid(),
            OnlineProfileId = profileId,
            MatchId         = matchId,
            EloBefore       = before,
            EloAfter        = after,
            Delta           = after - before,
            KFactorUsed     = k,
            ActualScore     = s,
            ExpectedScore   = e,
            IsPlacementMatch = isPlacement,
            ReasonCode      = isPlacement ? "PLACEMENT_MATCH" : "STANDARD_MATCH",
            ChangedAt       = now
        };
    }

    private static PlayerEloChangeDto BuildPlayerChange(
        OnlineProfile profile,
        int before, int after,
        int k, decimal s, decimal e)
    {
        return new PlayerEloChangeDto
        {
            UserId               = profile.UserId,
            DisplayName          = profile.User.DisplayName,
            EloBefore            = before,
            EloAfter             = after,
            Delta                = after - before,
            ActualScore          = s,
            ExpectedScore        = e,
            KFactorUsed          = k,
            PlacementMatchesDone = profile.PlacementMatchesDone,
            IsPlacementComplete  = profile.IsPlacementComplete
        };
    }
}
