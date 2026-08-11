using System;
using System.Collections.Generic;
using System.Linq;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Helpers;

public static class LiveBoardCalculator
{
    public static List<LiveBoardCompetitorDto> CalculateCompetitors(
        int solveCount,
        List<GroupCompetitor> competitors,
        List<Result> results,
        Dictionary<Guid, User> userMap,
        Dictionary<Guid, Registration> regMap,
        Dictionary<Guid, OfflineRegistrationEvent> offlineRegEventMap,
        Dictionary<Guid, PenaltyType> penaltyTypeMap,
        int? cutoffTimeMs = null)
    {
        var resultsByCompetitor = results
            .GroupBy(r => r.GroupCompetitorId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var competitorDtos = new List<LiveBoardCompetitorDto>();

        foreach (var c in competitors)
        {
            var compResults = resultsByCompetitor.TryGetValue(c.Id, out var resList) ? resList : new List<Result>();
            
            var offReg = offlineRegEventMap.TryGetValue(c.RegistrationEventId, out var o) ? o : null;
            var reg = offReg != null && regMap.TryGetValue(offReg.RegistrationId, out var r) ? r : null;
            var user = reg != null && userMap.TryGetValue(reg.UserId, out var u) ? u : null;

            int completedSolves = compResults.Count;
            bool isCutoffReached = CutoffEvaluator.IsCutoffStopped(solveCount, cutoffTimeMs, compResults);

            var resultDtos = compResults.OrderBy(res => res.SolveNumber).Select(res => new LiveBoardResultDto
            {
                ResultId = res.Id,
                SolveNumber = res.SolveNumber,
                RawTimeMs = res.RawTimeMs,
                FinalTimeMs = res.FinalTimeMs,
                PenaltyCode = res.PenaltyTypeId.HasValue && penaltyTypeMap.TryGetValue(res.PenaltyTypeId.Value, out var pt) ? pt.Code : "NONE",
                IsDnf = res.IsDnf,
                IsLocked = res.IsLocked,
                SubmittedAt = res.SubmittedAt,
                EsignatureData = res.EsignatureData,
                EvidencePhotoUrl = res.EvidencePhotoUrl
            }).ToList();

            // Per WCA Regulation 9g: If a competitor fails cutoff in the initial solves, remaining attempts are recorded/calculated as DNF
            if (isCutoffReached && resultDtos.Count < solveCount)
            {
                for (int solveNum = resultDtos.Count + 1; solveNum <= solveCount; solveNum++)
                {
                    resultDtos.Add(new LiveBoardResultDto
                    {
                        ResultId = Guid.Empty,
                        SolveNumber = solveNum,
                        RawTimeMs = null,
                        FinalTimeMs = null,
                        PenaltyCode = "DNF",
                        IsDnf = true,
                        IsLocked = true,
                        SubmittedAt = DateTime.UtcNow
                    });
                }
            }

            // Calculate Best Time
            int? bestTimeMs = null;
            var nonDnfResults = compResults.Where(res => !res.IsDnf && res.FinalTimeMs.HasValue).ToList();
            if (nonDnfResults.Any())
            {
                bestTimeMs = nonDnfResults.Min(res => res.FinalTimeMs!.Value);
            }
            else if (compResults.Any(res => res.IsDnf))
            {
                bestTimeMs = int.MaxValue; // All solves are DNF
            }

            // Calculate Average Time
            int? averageTimeMs = null;
            int effectiveCompletedSolves = isCutoffReached ? solveCount : completedSolves;

            if (effectiveCompletedSolves >= solveCount)
            {
                if (isCutoffReached)
                {
                    // Failed cutoff => remaining solves recorded as DNF => Average is DNF
                    averageTimeMs = int.MaxValue;
                }
                else if (solveCount == 5)
                {
                    int dnfCount = compResults.Count(res => res.IsDnf);
                    if (dnfCount >= 2)
                    {
                        averageTimeMs = int.MaxValue; // DNF average
                    }
                    else
                    {
                        var times = compResults.Select(res => res.IsDnf ? int.MaxValue : res.FinalTimeMs!.Value).OrderBy(t => t).ToList();
                        var middle3 = times.Skip(1).Take(3).ToList();
                        averageTimeMs = (int)Math.Round(middle3.Average());
                    }
                }
                else
                {
                    int dnfCount = compResults.Count(res => res.IsDnf);
                    if (dnfCount > 0)
                    {
                        averageTimeMs = int.MaxValue; // DNF average (e.g. Mo3)
                    }
                    else
                    {
                        averageTimeMs = (int)Math.Round(compResults.Average(res => res.FinalTimeMs!.Value));
                    }
                }
            }

            competitorDtos.Add(new LiveBoardCompetitorDto
            {
                GroupCompetitorId = c.Id,
                CompetitorName = user?.DisplayName ?? "Unknown Competitor",
                CompetitorUserCode = user?.UserCode ?? string.Empty,
                CompetitorAvatarUrl = user?.AvatarUrl,
                StationNumber = c.StationNumber,
                CompetitorStatus = c.StatusCode.ToString(),
                GroupId = c.GroupId,
                Results = resultDtos,
                BestTimeMs = bestTimeMs == int.MaxValue ? null : bestTimeMs,
                AverageTimeMs = averageTimeMs,
                CompletedSolves = effectiveCompletedSolves,
                IsCutoffReached = isCutoffReached
            });
        }

        // Sort for rankings
        var sorted = competitorDtos
            .OrderBy(x => x.CompetitorStatus == "NO_SHOW" ? 1 : 0)
            .ThenBy(x => {
                if (x.CompletedSolves >= solveCount && x.AverageTimeMs.HasValue)
                    return 0; // Category A
                if (x.CompletedSolves >= solveCount && !x.AverageTimeMs.HasValue)
                    return 1; // Category B
                if (x.CompletedSolves > 0)
                    return 2; // Category C
                return 3; // Category D
            })
            .ThenByDescending(x => {
                if (x.CompletedSolves > 0 && x.CompletedSolves < solveCount)
                    return x.CompletedSolves;
                return 0;
            })
            .ThenBy(x => {
                if (x.CompletedSolves >= solveCount && x.AverageTimeMs.HasValue)
                    return x.AverageTimeMs.Value;
                return 0;
            })
            .ThenBy(x => x.BestTimeMs ?? int.MaxValue)
            .ThenBy(x => x.CompetitorName)
            .ToList();

        // Assign ranks
        int currentRank = 1;
        for (int i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            if (item.CompetitorStatus == "NO_SHOW")
            {
                item.Rank = null;
                continue;
            }

            if (i > 0)
            {
                var prev = sorted[i - 1];
                bool isTie = false;
                if (item.CompletedSolves == solveCount && prev.CompletedSolves == solveCount)
                {
                    if (item.AverageTimeMs == prev.AverageTimeMs && item.BestTimeMs == prev.BestTimeMs)
                    {
                        isTie = true;
                    }
                }

                if (!isTie)
                {
                    currentRank = i + 1;
                }
            }
            else
            {
                currentRank = 1;
            }

            item.Rank = currentRank;
        }

        return sorted;
    }

    public static LiveBoardProgressDto CalculateProgress(int solveCount, List<LiveBoardCompetitorDto> competitors)
    {
        int totalCompetitors = competitors.Count;
        int completedCompetitors = competitors.Count(c => c.CompetitorStatus == "COMPLETED");
        int noShowCompetitors = competitors.Count(c => c.CompetitorStatus == "NO_SHOW");
        int pendingCompetitors = totalCompetitors - completedCompetitors - noShowCompetitors;

        int totalExpectedSolves = totalCompetitors * solveCount;
        int submittedSolves = competitors.Sum(c => c.CompletedSolves);

        return new LiveBoardProgressDto
        {
            TotalCompetitors = totalCompetitors,
            CompletedCompetitors = completedCompetitors,
            NoShowCompetitors = noShowCompetitors,
            PendingCompetitors = pendingCompetitors,
            TotalExpectedSolves = totalExpectedSolves,
            SubmittedSolves = submittedSolves
        };
    }
}
