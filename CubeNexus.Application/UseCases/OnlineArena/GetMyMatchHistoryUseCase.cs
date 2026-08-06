using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.OnlineArena;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class GetMyMatchHistoryUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IFraudReportRepository _fraudRepo;

    public GetMyMatchHistoryUseCase(
        IOnlineMatchRepository matchRepo,
        IFraudReportRepository fraudRepo)
    {
        _matchRepo = matchRepo;
        _fraudRepo = fraudRepo;
    }

    public async Task<OnlineMatchHistoryResponseDto> ExecuteAsync(
        Guid userId,
        Guid? puzzleTypeId = null,
        int page = 1,
        int pageSize = 15)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 50) pageSize = 15;

        var (matches, totalCount) = await _matchRepo.GetUserMatchHistoryAsync(userId, puzzleTypeId, page, pageSize);

        var items = new List<OnlineMatchHistoryItemDto>();

        foreach (var m in matches)
        {
            var reports = await _fraudRepo.GetByMatchAsync(m.Id);
            var latestReport = reports?.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
            var isP1 = m.Player1Id == userId;
            var me = isP1 ? m.Player1 : m.Player2;
            var opponent = isP1 ? m.Player2 : m.Player1;

            var myTimeMs = isP1 ? m.Player1TimeMs : m.Player2TimeMs;
            var opponentTimeMs = isP1 ? m.Player2TimeMs : m.Player1TimeMs;

            var myIsDnf = isP1 ? m.Player1IsDnf : m.Player2IsDnf;
            var opponentIsDnf = isP1 ? m.Player2IsDnf : m.Player1IsDnf;

            var myEloBefore = isP1 ? m.Player1EloBefore : m.Player2EloBefore;
            var myEloAfter = isP1 ? m.Player1EloAfter : m.Player2EloAfter;

            var oppEloBefore = isP1 ? m.Player2EloBefore : m.Player1EloBefore;
            var oppEloAfter = isP1 ? m.Player2EloAfter : m.Player1EloAfter;

            int eloChange = 0;
            if (myEloBefore.HasValue && myEloAfter.HasValue)
            {
                eloChange = myEloAfter.Value - myEloBefore.Value;
            }

            bool isWinner = m.WinnerId.HasValue && m.WinnerId.Value == userId;
            bool isDraw = m.Outcome == "DRAW" || (m.StatusCode == "COMPLETED" && !m.WinnerId.HasValue);

            bool hasVideoReplay = m.VideoEvidences != null && m.VideoEvidences.Any(v => v.Status == "READY" || !string.IsNullOrEmpty(v.ObjectKey));

            items.Add(new OnlineMatchHistoryItemDto
            {
                MatchId = m.Id,
                PuzzleTypeId = m.PuzzleTypeId,
                PuzzleTypeName = m.PuzzleType?.Name ?? "3x3x3",
                ScrambleSequence = m.ScrambleSequence,
                ModeName = "Ranked 1v1",

                MeUserId = me?.Id ?? userId,
                MeUsername = me?.DisplayName ?? me?.Email ?? "Player",
                MeAvatarUrl = me?.AvatarUrl,
                MeTimeMs = myTimeMs,
                MeIsDnf = myIsDnf,
                MeEloBefore = myEloBefore,
                MeEloAfter = myEloAfter,
                EloChange = eloChange,

                OpponentUserId = opponent?.Id ?? Guid.Empty,
                OpponentUsername = opponent?.DisplayName ?? opponent?.Email ?? "Opponent",
                OpponentAvatarUrl = opponent?.AvatarUrl,
                OpponentTimeMs = opponentTimeMs,
                OpponentIsDnf = opponentIsDnf,
                OpponentEloBefore = oppEloBefore,
                OpponentEloAfter = oppEloAfter,

                IsWinner = isWinner,
                IsDraw = isDraw,
                StatusCode = m.StatusCode,
                Outcome = m.Outcome,
                CreatedAt = m.CreatedAt,
                EndedAt = m.EndedAt,
                HasVideoReplay = hasVideoReplay,
                ReportStatus = latestReport?.StatusCode,
                ReportVerdictCode = latestReport?.VerdictCode,
                ReportAdminNote = latestReport?.AdminNote,
                ReportedByUserId = latestReport?.ReporterUserId.ToString(),
            });
        }

        return new OnlineMatchHistoryResponseDto
        {
            Matches = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }
}
