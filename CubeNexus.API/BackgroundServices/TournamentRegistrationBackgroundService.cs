using CubeNexus.Application.Interfaces.Repositories;

namespace CubeNexus.API.BackgroundServices;

/// <summary>
/// Opens registration for published tournaments once their configured UTC opening time is reached.
/// The conditional database update makes each run idempotent and safe when multiple API instances run.
/// </summary>
public sealed class TournamentRegistrationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TournamentRegistrationBackgroundService> _logger;

    public TournamentRegistrationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<TournamentRegistrationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TournamentRegistrationBackgroundService started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var openedCount = await unitOfWork.Tournaments
                        .OpenDueRegistrationsAsync(DateTime.UtcNow, stoppingToken);

                    if (openedCount > 0)
                    {
                        _logger.LogInformation(
                            "Automatically opened registration for {TournamentCount} tournament(s).",
                            openedCount);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while opening due tournament registrations.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }
}
