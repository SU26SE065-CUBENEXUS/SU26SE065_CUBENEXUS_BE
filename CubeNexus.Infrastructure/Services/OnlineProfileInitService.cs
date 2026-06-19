using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

public class OnlineProfileInitService : IOnlineProfileInitService
{
    private readonly IUnitOfWork _uow;

    public OnlineProfileInitService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<OnlineProfile> EnsureStandardProfileAsync(
        Guid userId, CancellationToken ct = default)
    {
        var existing = await _uow.OnlineProfiles.GetByUserIdAsync(userId, ct);
        if (existing is not null)
            return existing;

        var config = await _uow.EloConfigs.GetActiveConfigAsync(ct);
        var now = DateTime.UtcNow;
        var defaultElo = config.DefaultElo;

        var profile = new OnlineProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EloStandard = defaultElo,
            PeakEloStandard = defaultElo,
            PlacementMatchesDoneStandard = 0,
            IsPlacementCompleteStandard = false,
            KFactorCurrentStandard = config.KFactorPlacement,
            CreatedAt = now,
            UpdatedAt = now
        };

        _uow.OnlineProfiles.Add(profile);

        _uow.EloHistories.Add(new EloHistory
        {
            Id = Guid.NewGuid(),
            OnlineProfileId = profile.Id,
            MatchId = null,
            EloBefore = 0,
            EloAfter = defaultElo,
            Delta = defaultElo,
            KFactorUsed = config.KFactorPlacement,
            IsPlacementMatch = false,
            ReasonCode = "DEFAULT_INIT",
            EloModeCode = "STANDARD",
            ChangedAt = now
        });

        return profile;
    }
}
