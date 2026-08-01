using System;
using System.Threading.Tasks;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.UseCases.OnlineArena;
using CubeNexus.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace CubeNexus.API.Services;

public class MatchTransitionSchedulerImpl : IMatchTransitionScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MatchTransitionSchedulerImpl(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void ScheduleInspectionTransition(Guid matchId, TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                using var scope = _scopeFactory.CreateScope();
                var matchRepo = scope.ServiceProvider.GetRequiredService<IOnlineMatchRepository>();
                var startMatchUseCase = scope.ServiceProvider.GetRequiredService<StartOnlineMatchUseCase>();

                var match = await matchRepo.GetByIdAsync(matchId);
                if (match != null && match.Phase == "COUNTDOWN" && match.StatusCode == nameof(OnlineMatchStatus.READY))
                {
                    await startMatchUseCase.TransitionToInspectionAsync(match);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchTransitionScheduler] Error in ScheduleInspectionTransition: {ex.Message}");
            }
        });
    }

    public void ScheduleSolvingTransition(Guid matchId, TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                using var scope = _scopeFactory.CreateScope();
                var matchRepo = scope.ServiceProvider.GetRequiredService<IOnlineMatchRepository>();
                var transitionUseCase = scope.ServiceProvider.GetRequiredService<TransitionToSolvingUseCase>();

                var match = await matchRepo.GetByIdAsync(matchId);
                if (match != null && match.Phase == "INSPECTION")
                {
                    await transitionUseCase.ExecuteAsync(match);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchTransitionScheduler] Error in ScheduleSolvingTransition: {ex.Message}");
            }
        });
    }
}
