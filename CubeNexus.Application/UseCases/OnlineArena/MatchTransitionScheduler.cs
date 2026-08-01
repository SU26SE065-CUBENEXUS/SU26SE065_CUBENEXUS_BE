using System;

namespace CubeNexus.Application.UseCases.OnlineArena;

public interface IMatchTransitionScheduler
{
    void ScheduleInspectionTransition(Guid matchId, TimeSpan delay);
    void ScheduleSolvingTransition(Guid matchId, TimeSpan delay);
}

public static class MatchTransitionScheduler
{
    public static IMatchTransitionScheduler? Instance { get; set; }

    public static void ScheduleInspectionTransition(Guid matchId, TimeSpan delay)
    {
        Instance?.ScheduleInspectionTransition(matchId, delay);
    }

    public static void ScheduleSolvingTransition(Guid matchId, TimeSpan delay)
    {
        Instance?.ScheduleSolvingTransition(matchId, delay);
    }
}
