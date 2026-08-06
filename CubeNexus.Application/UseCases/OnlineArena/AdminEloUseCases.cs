using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using IUnitOfWork = CubeNexus.Application.Interfaces.IUnitOfWork;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class GetEloConfigUseCase
{
    private readonly IEloConfigRepository _eloConfigRepo;

    public GetEloConfigUseCase(IEloConfigRepository eloConfigRepo)
    {
        _eloConfigRepo = eloConfigRepo;
    }

    public async Task<EloConfigDto> ExecuteAsync(CancellationToken ct = default)
    {
        EloConfig config;
        try
        {
            config = await _eloConfigRepo.GetActiveConfigAsync(ct);
        }
        catch
        {
            config = new EloConfig
            {
                Id = Guid.NewGuid(),
                KFactorPlacement = 100,
                KFactorStandard = 20,
                PlacementMatchCount = 5,
                DefaultElo = 1000,
                UpdatedAt = DateTime.UtcNow
            };
        }

        return new EloConfigDto
        {
            Id = config.Id,
            KFactorPlacement = config.KFactorPlacement,
            KFactorStandard = config.KFactorStandard,
            PlacementMatchCount = config.PlacementMatchCount,
            DefaultElo = config.DefaultElo,
            UpdatedAt = config.UpdatedAt,
            UpdatedBy = config.UpdatedBy
        };
    }
}

public class UpdateEloConfigUseCase
{
    private readonly IEloConfigRepository _eloConfigRepo;
    private readonly IUnitOfWork _uow;

    public UpdateEloConfigUseCase(IEloConfigRepository eloConfigRepo, IUnitOfWork uow)
    {
        _eloConfigRepo = eloConfigRepo;
        _uow = uow;
    }

    public async Task<EloConfigDto> ExecuteAsync(Guid adminId, UpdateEloConfigRequest req, CancellationToken ct = default)
    {
        if (req.KFactorPlacement <= 0 || req.KFactorStandard <= 0)
            throw new ArgumentException("K-Factor must be greater than 0.");
        if (req.PlacementMatchCount < 0)
            throw new ArgumentException("Placement match count cannot be negative.");
        if (req.DefaultElo < 100)
            throw new ArgumentException("Default ELO must be at least 100.");

        EloConfig config;
        try
        {
            config = await _eloConfigRepo.GetActiveConfigAsync(ct);
            config.KFactorPlacement = req.KFactorPlacement;
            config.KFactorStandard = req.KFactorStandard;
            config.PlacementMatchCount = req.PlacementMatchCount;
            config.DefaultElo = req.DefaultElo;
            config.UpdatedBy = adminId;
            config.UpdatedAt = DateTime.UtcNow;
            _eloConfigRepo.Update(config);
        }
        catch
        {
            config = new EloConfig
            {
                Id = Guid.NewGuid(),
                KFactorPlacement = req.KFactorPlacement,
                KFactorStandard = req.KFactorStandard,
                PlacementMatchCount = req.PlacementMatchCount,
                DefaultElo = req.DefaultElo,
                UpdatedBy = adminId,
                UpdatedAt = DateTime.UtcNow
            };
            _eloConfigRepo.Add(config);
        }

        await _uow.SaveChangesAsync(ct);

        return new EloConfigDto
        {
            Id = config.Id,
            KFactorPlacement = config.KFactorPlacement,
            KFactorStandard = config.KFactorStandard,
            PlacementMatchCount = config.PlacementMatchCount,
            DefaultElo = config.DefaultElo,
            UpdatedAt = config.UpdatedAt,
            UpdatedBy = config.UpdatedBy
        };
    }
}

public class GetAdminPlayerEloListUseCase
{
    private readonly CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository _profileRepo;

    public GetAdminPlayerEloListUseCase(CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository profileRepo)
    {
        _profileRepo = profileRepo;
    }

    public async Task<List<AdminPlayerEloDto>> ExecuteAsync(Guid? puzzleTypeId = null, string? search = null)
    {
        var targetPuzzleId = puzzleTypeId ?? Guid.Empty;
        var profiles = await _profileRepo.GetLeaderboardAsync(targetPuzzleId, 200);

        var items = profiles.Select(p => new AdminPlayerEloDto
        {
            UserId = p.UserId,
            Username = p.User?.DisplayName ?? p.User?.Email ?? "Player",
            AvatarUrl = p.User?.AvatarUrl,
            PuzzleTypeId = targetPuzzleId,
            PuzzleTypeName = "3x3x3 Ranked",
            EloStandard = p.EloStandard,
            PeakEloStandard = p.PeakEloStandard,
            TotalWinsStandard = p.TotalWinsStandard,
            TotalLossesStandard = p.TotalLossesStandard,
            TotalDrawsStandard = p.TotalDrawsStandard,
            IsPlacementCompleteStandard = p.IsPlacementCompleteStandard,
            PlacementMatchesDoneStandard = p.PlacementMatchesDoneStandard,
            UpdatedAt = p.UpdatedAt
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            items = items.Where(x => x.Username.ToLowerInvariant().Contains(term) || x.UserId.ToString().Contains(term));
        }

        return items.ToList();
    }
}

public class AdjustPlayerEloUseCase
{
    private readonly CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository _profileRepo;
    private readonly IEloHistoryRepository _eloHistoryRepo;
    private readonly IUnitOfWork _uow;

    public AdjustPlayerEloUseCase(
        CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository profileRepo,
        IEloHistoryRepository eloHistoryRepo,
        IUnitOfWork uow)
    {
        _profileRepo = profileRepo;
        _eloHistoryRepo = eloHistoryRepo;
        _uow = uow;
    }

    public async Task<AdjustPlayerEloResponseDto> ExecuteAsync(Guid adminId, Guid targetUserId, AdjustPlayerEloRequest req)
    {
        if (req.EloDelta == 0)
            throw new ArgumentException("Elo delta must not be zero.");

        var profile = await _profileRepo.GetByUserIdAsync(targetUserId);
        if (profile == null)
            throw new KeyNotFoundException("Online profile for target user not found.");

        int eloBefore = profile.EloStandard;
        int eloAfter = Math.Max(0, eloBefore + req.EloDelta);

        profile.EloStandard = eloAfter;
        profile.PeakEloStandard = Math.Max(profile.PeakEloStandard, eloAfter);
        profile.UpdatedAt = DateTime.UtcNow;

        _profileRepo.Update(profile);

        var reasonText = string.IsNullOrWhiteSpace(req.Reason) ? "ADMIN_MANUAL_ADJUST" : req.Reason;
        await _eloHistoryRepo.AddAsync(new EloHistory
        {
            Id = Guid.NewGuid(),
            OnlineProfileId = profile.Id,
            MatchId = null,
            EloBefore = eloBefore,
            EloAfter = eloAfter,
            Delta = req.EloDelta,
            KFactorUsed = profile.KFactorCurrentStandard,
            ReasonCode = "ADMIN_ADJUST",
            EloModeCode = "STANDARD",
            ChangedAt = DateTime.UtcNow
        });

        await _uow.SaveChangesAsync();

        return new AdjustPlayerEloResponseDto
        {
            UserId = targetUserId,
            Username = profile.User?.DisplayName ?? profile.User?.Email ?? "Player",
            EloBefore = eloBefore,
            EloAfter = eloAfter,
            Delta = req.EloDelta,
            Reason = reasonText,
            AdjustedAt = DateTime.UtcNow
        };
    }
}
