using System.Collections.Generic;
using System.Linq;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Helpers;

public static class CutoffEvaluator
{
    /// <summary>
    /// Returns the number of initial attempts required to pass cutoff.
    /// Mo3 (solveCount == 3) -> 1 attempt
    /// Ao5 (solveCount == 5) -> 2 attempts
    /// </summary>
    public static int GetRequiredCutoffAttempts(int solveCount)
    {
        return (solveCount == 3) ? 1 : 2;
    }

    /// <summary>
    /// Determines whether a competitor has completed the required initial attempts
    /// AND failed to achieve at least one solve time strictly under cutoffTimeMs.
    /// </summary>
    public static bool IsCutoffStopped(int solveCount, int? cutoffTimeMs, List<Result> results)
    {
        if (!cutoffTimeMs.HasValue || cutoffTimeMs.Value <= 0)
            return false;

        int requiredAttempts = GetRequiredCutoffAttempts(solveCount);

        var orderedResults = results.OrderBy(r => r.SolveNumber).ToList();

        // Must have completed at least the required initial attempts
        if (orderedResults.Count < requiredAttempts)
            return false;

        var initialAttempts = orderedResults.Take(requiredAttempts).ToList();

        // Competitor passes if at least ONE initial solve is valid (!IsDnf) and FinalTimeMs < cutoffTimeMs
        bool passedCutoff = initialAttempts.Any(r => !r.IsDnf && r.FinalTimeMs.HasValue && r.FinalTimeMs.Value < cutoffTimeMs.Value);

        return !passedCutoff;
    }
}
