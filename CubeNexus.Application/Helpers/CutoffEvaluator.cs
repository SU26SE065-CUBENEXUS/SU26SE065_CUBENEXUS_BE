using System.Collections.Generic;
using System.Linq;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Helpers;

public static class CutoffEvaluator
{
    /// <summary>
    /// Returns the number of initial attempts required to evaluate cutoff.
    /// For events with 3 or more solves (Ao3, Mo3, Ao5), cutoff is evaluated across the first 2 solves (S1, S2).
    /// For events with 1 or 2 solves (Bo1, Bo2), cutoff is evaluated on the 1st solve.
    /// </summary>
    public static int GetRequiredCutoffAttempts(int solveCount)
    {
        if (solveCount <= 2)
            return 1;
        return 2;
    }

    /// <summary>
    /// Determines whether a competitor has completed the required initial attempts
    /// AND failed to achieve at least one solve time <= cutoffTimeMs.
    /// </summary>
    public static bool IsCutoffStopped(int solveCount, int? cutoffTimeMs, List<Result> results)
    {
        if (!cutoffTimeMs.HasValue || cutoffTimeMs.Value <= 0)
            return false;

        int requiredAttempts = GetRequiredCutoffAttempts(solveCount);

        var orderedResults = results.OrderBy(r => r.SolveNumber).ToList();

        // Must have completed at least the required initial attempts (e.g. 2 solves for solveCount >= 3)
        if (orderedResults.Count < requiredAttempts)
            return false;

        var initialAttempts = orderedResults.Take(requiredAttempts).ToList();

        // Competitor passes cutoff if at least ONE initial solve is valid (!IsDnf) AND FinalTimeMs <= cutoffTimeMs
        bool passedCutoff = initialAttempts.Any(r => !r.IsDnf && r.FinalTimeMs.HasValue && r.FinalTimeMs.Value <= cutoffTimeMs.Value);

        return !passedCutoff;
    }
}
