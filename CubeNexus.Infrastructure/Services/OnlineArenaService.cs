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
            ?? throw new InvalidOperationException($"Khong tim thay tran dau {matchId}.");

        if (match.EndedAt != null && match.Player1EloAfter != null)
            throw new InvalidOperationException("Ket qua tran dau nay da duoc ghi nhan.");

        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);

        var profile1 = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(match.Player1Id, match.PuzzleTypeId, ct)
            ?? throw new InvalidOperationException($"Khong tim thay online profile cua player1 ({match.Player1Id}).");

        var profile2 = await _uow.OnlineProfiles
            .GetByUserAndPuzzleTypeAsync(match.Player2Id, match.PuzzleTypeId, ct)
            ?? throw new InvalidOperationException($"Khong tim thay online profile cua player2 ({match.Player2Id}).");

        (decimal s1, decimal s2) = winnerId switch
        {
            null => (0.5m, 0.5m),
            var w when w == match.Player1Id => (1.0m, 0.0m),
            _ => (0.0m, 1.0m)
        };

        decimal e1 = CalculateExpectedScore(profile1.Elo, profile2.Elo);
        decimal e2 = 1.0m - e1;

        int eloBefore1 = profile1.Elo;
        int eloBefore2 = profile2.Elo;
        int k1 = profile1.KFactorCurrent;
        int k2 = profile2.KFactorCurrent;

        int eloAfter1 = Math.Max(0, (int)Math.Round(eloBefore1 + k1 * (s1 - e1)));
        int eloAfter2 = Math.Max(0, (int)Math.Round(eloBefore2 + k2 * (s2 - e2)));

        bool wasPlacement1 = !profile1.IsPlacementComplete;
        bool wasPlacement2 = !profile2.IsPlacementComplete;

        var now = DateTime.UtcNow;

        bool p1PlacementJustDone = UpdateProfile(profile1, s1, eloAfter1, config, now);
        bool p2PlacementJustDone = UpdateProfile(profile2, s2, eloAfter2, config, now);

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
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var profile = await _uow.OnlineProfiles.GetByUserAndPuzzleTypeAsync(userId, puzzleTypeId, ct);

        if (profile is null) return null;

        int total = profile.TotalWins + profile.TotalLosses + profile.TotalDraws;
        double winRate = total > 0
            ? Math.Round((double)profile.TotalWins / total * 100, 1)
            : 0;

        return new OnlineProfileDto
        {
            UserId = profile.UserId,
            EloStandardVisible = profile.IsPlacementComplete ? profile.Elo : null,
            PeakEloStandard = profile.PeakElo,
            EloMedley = null,
            PlacementMatchesDoneStandard = profile.PlacementMatchesDone,
            PlacementMatchCount = config.PlacementMatchCount,
            IsPlacementCompleteStandard = profile.IsPlacementComplete,
            PlacementCompletedAtStandard = profile.PlacementCompletedAt,
            TotalWinsStandard = profile.TotalWins,
            TotalLossesStandard = profile.TotalLosses,
            TotalDrawsStandard = profile.TotalDraws,
            WinRate = winRate,
            CreatedAt = profile.CreatedAt
        };
    }

    public async Task<LeaderboardResponseDto> GetLeaderboardAsync(
        Guid puzzleTypeId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
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
                Rank = (page - 1) * pageSize + i + 1,
                UserId = p.UserId,
                DisplayName = p.User.DisplayName,
                AvatarUrl = p.User.AvatarUrl,
                Elo = p.Elo,
                PeakElo = p.PeakElo,
                TotalWins = p.TotalWins,
                TotalLosses = p.TotalLosses,
                TotalDraws = p.TotalDraws,
                WinRate = wr,
                PlacementCompletedAt = p.PlacementCompletedAt
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
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var profile = await _uow.OnlineProfiles.GetByUserAndPuzzleTypeAsync(userId, puzzleTypeId, ct);

        if (profile is null)
        {
            return new PlayerEligibilityDto
            {
                UserId = userId,
                CanJoinPvp = false,
                BlockReason = "Ban chua hoan thanh Practice Ao5 seeding.",
                HasOnlineProfile = false,
                IsPlacementCompleteStandard = false,
                PlacementMatchesDoneStandard = 0,
                PlacementMatchCount = config.PlacementMatchCount,
                HiddenEloStandard = null,
                PublicEloStandard = null,
                CurrentStage = "NO_PROFILE",
                NextStepHint = "Hoan thanh Practice Ao5 seeding de mo khoa Online PVP."
            };
        }

        bool isPlacementComplete = profile.IsPlacementComplete;
        int placementDone = profile.PlacementMatchesDone;
        int remaining = config.PlacementMatchCount - placementDone;

        string stage;
        string hint;

        if (!isPlacementComplete)
        {
            stage = "PLACEMENT";
            hint = $"Dang trong giai doan Placement. Hoan thanh them {remaining} tran PVP nua de Elo duoc cong khai.";
        }
        else
        {
            stage = "STANDARD";
            hint = "Elo da duoc cong khai. Tiep tuc thi dau de leo rank.";
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
            HiddenEloStandard = !isPlacementComplete ? profile.Elo : null,
            PublicEloStandard = isPlacementComplete ? profile.Elo : null,
            CurrentStage = stage,
            NextStepHint = hint
        };
    }

    private static decimal CalculateExpectedScore(int ra, int rb)
    {
        double expected = 1.0 / (1.0 + Math.Pow(10, (rb - ra) / 400.0));
        return (decimal)Math.Round(expected, 4);
    }

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

        if (actualScore == 1.0m) profile.TotalWins++;
        else if (actualScore == 0.0m) profile.TotalLosses++;
        else profile.TotalDraws++;

        if (!profile.IsPlacementComplete)
        {
            profile.PlacementMatchesDone++;

            if (profile.PlacementMatchesDone >= config.PlacementMatchCount)
            {
                profile.IsPlacementComplete = true;
                profile.PlacementCompletedAt = now;
                profile.KFactorCurrent = config.KFactorStandard;
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
            PlacementMatchesDone = profile.PlacementMatchesDone,
            IsPlacementComplete = profile.IsPlacementComplete
        };
    }
}
