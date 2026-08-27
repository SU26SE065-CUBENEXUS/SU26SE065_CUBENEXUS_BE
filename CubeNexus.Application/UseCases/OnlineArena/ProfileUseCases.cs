using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class InitOnlineProfileUseCase
{
    private readonly IOnlineProfileInitService _profileInitService;
    private readonly IUnitOfWork _uow;

    public InitOnlineProfileUseCase(
        IOnlineProfileInitService profileInitService,
        IUnitOfWork uow)
    {
        _profileInitService = profileInitService;
        _uow = uow;
    }

    public async Task<OnlineProfileDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
        var config = await _uow.EloConfigs.GetActiveConfigAsync();
        int reqCount = config?.PlacementMatchCount ?? 5;
        var profile = await _profileInitService.EnsureStandardProfileAsync(userId);
        await _uow.SaveChangesAsync();
        return MapToDto(profile, puzzleTypeId, reqCount);
    }

    internal static OnlineProfileDto MapToDto(OnlineProfile profile, Guid puzzleTypeId, int placementMatchCount = 5) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        PuzzleTypeId = puzzleTypeId,
        DisplayName = profile.User?.DisplayName ?? string.Empty,
        Elo = profile.EloStandard,
        PeakElo = profile.PeakEloStandard,
        PlacementMatchesDone = profile.PlacementMatchesDoneStandard,
        PlacementMatchCount = placementMatchCount,
        IsPlacementComplete = profile.IsPlacementCompleteStandard || profile.PlacementMatchesDoneStandard >= placementMatchCount,
        TotalWins = profile.TotalWinsStandard,
        TotalLosses = profile.TotalLossesStandard,
        TotalDraws = profile.TotalDrawsStandard
    };
}

public class GetMyOnlineProfilesUseCase
{
    private readonly CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository _repo;
    private readonly IEloConfigRepository _eloConfigRepo;

    public GetMyOnlineProfilesUseCase(
        CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository repo,
        IEloConfigRepository eloConfigRepo)
    {
        _repo = repo;
        _eloConfigRepo = eloConfigRepo;
    }

    public async Task<List<OnlineProfileDto>> ExecuteAsync(Guid userId)
    {
        var config = await _eloConfigRepo.GetActiveConfigAsync();
        int reqCount = config?.PlacementMatchCount ?? 5;
        var profiles = await _repo.GetUserProfilesAsync(userId);
        return profiles
            .Select(p => InitOnlineProfileUseCase.MapToDto(p, Guid.Empty, reqCount))
            .ToList();
    }
}

public class GetOnlineLeaderboardUseCase
{
    private readonly CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository _repo;

    public GetOnlineLeaderboardUseCase(CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository repo) => _repo = repo;

    public async Task<List<LeaderboardEntryDto>> ExecuteAsync(Guid puzzleTypeId)
    {
        var leaderboard = await _repo.GetLeaderboardAsync(puzzleTypeId);
        var res = new List<LeaderboardEntryDto>();
        for (int i = 0; i < leaderboard.Count; i++)
        {
            var p = leaderboard[i];
            res.Add(new LeaderboardEntryDto
            {
                Rank = i + 1,
                UserId = p.UserId,
                DisplayName = p.User?.DisplayName ?? "",
                AvatarUrl = p.User?.AvatarUrl,
                Elo = p.EloStandard,
                TotalWins = p.TotalWinsStandard
            });
        }
        return res;
    }
}
