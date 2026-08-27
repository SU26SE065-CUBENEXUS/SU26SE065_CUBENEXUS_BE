using CubeNexus.Application.DTOs.Arena;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

public class OnlineArenaService : IOnlineArenaService
{
    private readonly IUnitOfWork _uow;

    public OnlineArenaService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<MatchResultDto> RecordMatchResultAsync(
        Guid matchId, Guid? winnerId, CancellationToken ct = default)
    {
        var match = await _uow.OnlineMatches.GetByIdAsync(matchId, ct)
            ?? throw new InvalidOperationException($"Không tìm thấy trận đấu {matchId}.");

        if (match.EndedAt != null && match.Player1EloAfter != null)
            throw new InvalidOperationException("Kết quả trận đấu này đã được ghi nhận.");

        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);

        var profile1 = await _uow.OnlineProfiles.GetByUserIdAsync(match.Player1Id, ct)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy online profile của player1 ({match.Player1Id}).");

        var profile2 = await _uow.OnlineProfiles.GetByUserIdAsync(match.Player2Id, ct)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy online profile của player2 ({match.Player2Id}).");

        (decimal s1, decimal s2) = winnerId switch
        {
            null                            => (0.5m, 0.5m),
            var w when w == match.Player1Id => (1.0m, 0.0m),
            _                               => (0.0m, 1.0m)
        };

        decimal e1 = CalculateExpectedScore(profile1.EloStandard, profile2.EloStandard);
        decimal e2 = 1.0m - e1;

        int eloBefore1 = profile1.EloStandard;
        int eloBefore2 = profile2.EloStandard;
        int k1 = profile1.KFactorCurrentStandard;
        int k2 = profile2.KFactorCurrentStandard;

        int eloAfter1 = Math.Max(0, (int)Math.Round(eloBefore1 + k1 * (s1 - e1)));
        int eloAfter2 = Math.Max(0, (int)Math.Round(eloBefore2 + k2 * (s2 - e2)));

        bool wasPlacement1 = !profile1.IsPlacementCompleteStandard;
        bool wasPlacement2 = !profile2.IsPlacementCompleteStandard;

        var now = DateTime.UtcNow;

        bool p1PlacementJustDone = UpdateStandardProfile(profile1, s1, eloAfter1, config, now);
        bool p2PlacementJustDone = UpdateStandardProfile(profile2, s2, eloAfter2, config, now);

        match.WinnerId = winnerId;
        match.EndedAt = now;
        match.Player1EloAfter = eloAfter1;
        match.Player2EloAfter = eloAfter2;
        if (match.Player1EloBefore == null) match.Player1EloBefore = eloBefore1;
        if (match.Player2EloBefore == null) match.Player2EloBefore = eloBefore2;

        _uow.OnlineMatches.Update(match);

        _uow.EloHistories.Add(BuildEloHistory(
            profile1.Id, matchId, eloBefore1, eloAfter1, k1, s1, e1, wasPlacement1, now));
        _uow.EloHistories.Add(BuildEloHistory(
            profile2.Id, matchId, eloBefore2, eloAfter2, k2, s2, e2, wasPlacement2, now));

        await _uow.SaveChangesAsync(ct);

        return new MatchResultDto
        {
            MatchId = matchId,
            IsPlacementMatch = wasPlacement1 || wasPlacement2,
            Player1PlacementCompleted = p1PlacementJustDone,
            Player2PlacementCompleted = p2PlacementJustDone,
            Player1 = BuildPlayerChange(profile1, eloBefore1, eloAfter1, k1, s1, e1),
            Player2 = BuildPlayerChange(profile2, eloBefore2, eloAfter2, k2, s2, e2)
        };
    }

    public async Task<OnlineProfileDto?> GetPlayerProfileAsync(
        Guid userId, CancellationToken ct = default)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var profile = await _uow.OnlineProfiles.GetByUserIdAsync(userId, ct);

        if (profile is null) return null;

        int total = profile.TotalWinsStandard + profile.TotalLossesStandard + profile.TotalDrawsStandard;
        double winRate = total > 0
            ? Math.Round((double)profile.TotalWinsStandard / total * 100, 1)
            : 0;

        bool isPlacementComplete = profile.IsPlacementCompleteStandard || profile.PlacementMatchesDoneStandard >= config.PlacementMatchCount;

        return new OnlineProfileDto
        {
            UserId = profile.UserId,
            EloStandardVisible = isPlacementComplete ? profile.EloStandard : null,
            PeakEloStandard = profile.PeakEloStandard,
            EloMedley = profile.EloMedley,
            PlacementMatchesDoneStandard = profile.PlacementMatchesDoneStandard,
            PlacementMatchCount = config.PlacementMatchCount,
            IsPlacementCompleteStandard = isPlacementComplete,
            PlacementCompletedAtStandard = profile.PlacementCompletedAtStandard,
            TotalWinsStandard = profile.TotalWinsStandard,
            TotalLossesStandard = profile.TotalLossesStandard,
            TotalDrawsStandard = profile.TotalDrawsStandard,
            WinRate = winRate,
            CreatedAt = profile.CreatedAt
        };
    }

    public async Task<LeaderboardResponseDto> GetLeaderboardAsync(
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, totalCount) = await _uow.OnlineProfiles
            .GetLeaderboardAsync(page, pageSize, ct);

        var entries = items.Select((p, i) =>
        {
            int total = p.TotalWinsStandard + p.TotalLossesStandard + p.TotalDrawsStandard;
            double wr = total > 0
                ? Math.Round((double)p.TotalWinsStandard / total * 100, 1)
                : 0;

            return new LeaderboardEntryDto
            {
                Rank = (page - 1) * pageSize + i + 1,
                UserId = p.UserId,
                DisplayName = p.User.DisplayName,
                AvatarUrl = p.User.AvatarUrl,
                Elo = p.EloStandard,
                PeakElo = p.PeakEloStandard,
                TotalWins = p.TotalWinsStandard,
                TotalLosses = p.TotalLossesStandard,
                TotalDraws = p.TotalDrawsStandard,
                WinRate = wr,
                PlacementCompletedAt = p.PlacementCompletedAtStandard
            };
        }).ToList();

        return new LeaderboardResponseDto
        {
            Entries = entries,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PlayerEligibilityDto> GetPlayerEligibilityAsync(
        Guid userId, CancellationToken ct = default)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var profile = await _uow.OnlineProfiles.GetByUserIdAsync(userId, ct);

        if (profile is null)
        {
            return new PlayerEligibilityDto
            {
                UserId = userId,
                CanJoinPvp = false,
                BlockReason = "Chưa có Online Profile. Vui lòng liên hệ hỗ trợ.",
                HasOnlineProfile = false,
                PlacementMatchCount = config.PlacementMatchCount,
                CurrentStage = "NO_PROFILE",
                NextStepHint = "Profile sẽ được tạo tự động khi đăng ký."
            };
        }

        bool isPlacementComplete = profile.IsPlacementCompleteStandard;
        int placementDone = profile.PlacementMatchesDoneStandard;
        int remaining = config.PlacementMatchCount - placementDone;

        string stage;
        string hint;

        if (!isPlacementComplete)
        {
            stage = "PLACEMENT";
            hint = $"Đang trong giai đoạn Placement (Elo Standard ẩn). " +
                   $"Hoàn thành thêm {remaining} trận PVP nữa để Elo được công khai trên bảng xếp hạng.";
        }
        else
        {
            stage = "STANDARD";
            hint = "Elo Standard đã được công khai. Tiếp tục thi đấu để leo rank!";
        }

        return new PlayerEligibilityDto
        {
            UserId = userId,
            CanJoinPvp = true,
            BlockReason = null,
            HasOnlineProfile = true,
            IsPlacementCompleteStandard = isPlacementComplete,
            PlacementMatchesDoneStandard = placementDone,
            PlacementMatchCount = config.PlacementMatchCount,
            HiddenEloStandard = !isPlacementComplete ? profile.EloStandard : null,
            PublicEloStandard = isPlacementComplete ? profile.EloStandard : null,
            CurrentStage = stage,
            NextStepHint = hint
        };
    }

    private static decimal CalculateExpectedScore(int ra, int rb)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10, (rb - ra) / 400.0));
        return (decimal)Math.Round(expected, 4);
    }

    private static bool UpdateStandardProfile(
        OnlineProfile profile,
        decimal actualScore,
        int newElo,
        EloConfig config,
        DateTime now)
    {
        bool wasComplete = profile.IsPlacementCompleteStandard;

        profile.EloStandard = newElo;
        if (newElo > profile.PeakEloStandard) profile.PeakEloStandard = newElo;

        if (actualScore == 1.0m) profile.TotalWinsStandard++;
        else if (actualScore == 0.0m) profile.TotalLossesStandard++;
        else profile.TotalDrawsStandard++;

        if (!profile.IsPlacementCompleteStandard)
        {
            profile.PlacementMatchesDoneStandard++;

            if (profile.PlacementMatchesDoneStandard >= config.PlacementMatchCount)
            {
                profile.IsPlacementCompleteStandard = true;
                profile.PlacementCompletedAtStandard = now;
                profile.KFactorCurrentStandard = config.KFactorStandard;
            }
        }

        profile.UpdatedAt = now;
        return !wasComplete && profile.IsPlacementCompleteStandard;
    }

    private static EloHistory BuildEloHistory(
        Guid profileId, Guid matchId,
        int before, int after,
        int k, decimal s, decimal e,
        bool isPlacement, DateTime now)
    {
        return new EloHistory
        {
            Id = Guid.NewGuid(),
            OnlineProfileId = profileId,
            MatchId = matchId,
            EloBefore = before,
            EloAfter = after,
            Delta = after - before,
            KFactorUsed = k,
            ActualScore = s,
            ExpectedScore = e,
            IsPlacementMatch = isPlacement,
            ReasonCode = isPlacement ? "PLACEMENT_MATCH" : "STANDARD_MATCH",
            EloModeCode = "STANDARD",
            ChangedAt = now
        };
    }

    private static PlayerEloChangeDto BuildPlayerChange(
        OnlineProfile profile,
        int before, int after,
        int k, decimal s, decimal e)
    {
        return new PlayerEloChangeDto
        {
            UserId = profile.UserId,
            DisplayName = profile.User.DisplayName,
            EloBefore = before,
            EloAfter = after,
            Delta = after - before,
            ActualScore = s,
            ExpectedScore = e,
            KFactorUsed = k,
            PlacementMatchesDone = profile.PlacementMatchesDoneStandard,
            IsPlacementComplete = profile.IsPlacementCompleteStandard
        };
    }
}
