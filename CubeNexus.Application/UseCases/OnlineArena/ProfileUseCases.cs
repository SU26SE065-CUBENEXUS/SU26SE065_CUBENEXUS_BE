using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class InitOnlineProfileUseCase
{
    private readonly IOnlineProfileInitService _profileInitService;
    private readonly CubeNexus.Application.Interfaces.IUnitOfWork _uow;

    public InitOnlineProfileUseCase(
        IOnlineProfileInitService profileInitService,
        CubeNexus.Application.Interfaces.IUnitOfWork uow)
    {
        _profileInitService = profileInitService;
        _uow = uow;
    }

    public async Task<OnlineProfileDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
        var profile = await _profileInitService.EnsureStandardProfileAsync(userId);
        await _uow.SaveChangesAsync();
        return MapToDto(profile, puzzleTypeId);
    }

    internal static OnlineProfileDto MapToDto(OnlineProfile profile, Guid puzzleTypeId) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        PuzzleTypeId = puzzleTypeId,
        Elo = profile.EloStandard,
        PeakElo = profile.PeakEloStandard,
        PlacementMatchesDone = profile.PlacementMatchesDoneStandard,
        IsPlacementComplete = profile.IsPlacementCompleteStandard,
        TotalWins = profile.TotalWinsStandard,
        TotalLosses = profile.TotalLossesStandard,
        TotalDraws = profile.TotalDrawsStandard
    };
}

public class GetMyOnlineProfilesUseCase
{
    private readonly IOnlineProfileRepository _repo;

    public GetMyOnlineProfilesUseCase(IOnlineProfileRepository repo) => _repo = repo;

    public async Task<List<OnlineProfileDto>> ExecuteAsync(Guid userId)
    {
        var profiles = await _repo.GetUserProfilesAsync(userId);
        return profiles
            .Select(p => InitOnlineProfileUseCase.MapToDto(p, Guid.Empty))
            .ToList();
    }
}

public class GetOnlineLeaderboardUseCase
{
    private readonly IOnlineProfileRepository _repo;

    public GetOnlineLeaderboardUseCase(IOnlineProfileRepository repo) => _repo = repo;

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
