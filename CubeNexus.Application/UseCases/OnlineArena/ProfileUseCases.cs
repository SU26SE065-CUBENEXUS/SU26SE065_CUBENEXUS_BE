using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Application.Interfaces;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class InitOnlineProfileUseCase
{
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IUnitOfWork _uow;

    public InitOnlineProfileUseCase(IOnlineProfileRepository profileRepo, IUnitOfWork uow)
    {
        _profileRepo = profileRepo;
        _uow = uow;
    }

    public async Task<OnlineProfileDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
        var existing = await _profileRepo.GetProfileAsync(userId, puzzleTypeId);
        if (existing != null)
            throw new Exception("Profile already exists.");

        var profile = new OnlineProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PuzzleTypeId = puzzleTypeId,
            Elo = 1000,
            PeakElo = 1000,
            PlacementMatchesDone = 0,
            IsPlacementComplete = false,
            TotalWins = 0,
            TotalLosses = 0,
            TotalDraws = 0,
            KFactorCurrent = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _profileRepo.AddAsync(profile);
        await _uow.SaveChangesAsync();

        return new OnlineProfileDto
        {
            Id = profile.Id,
            UserId = profile.UserId,
            PuzzleTypeId = profile.PuzzleTypeId,
            Elo = profile.Elo,
            PeakElo = profile.PeakElo,
            PlacementMatchesDone = profile.PlacementMatchesDone,
            IsPlacementComplete = profile.IsPlacementComplete,
            TotalWins = profile.TotalWins,
            TotalLosses = profile.TotalLosses,
            TotalDraws = profile.TotalDraws
        };
    }
}

public class GetMyOnlineProfilesUseCase
{
    private readonly IOnlineProfileRepository _repo;
    public GetMyOnlineProfilesUseCase(IOnlineProfileRepository repo) => _repo = repo;

    public async Task<List<OnlineProfileDto>> ExecuteAsync(Guid userId)
    {
        var profiles = await _repo.GetUserProfilesAsync(userId);
        return profiles.Select(p => new OnlineProfileDto
        {
            Id = p.Id,
            UserId = p.UserId,
            PuzzleTypeId = p.PuzzleTypeId,
            Elo = p.Elo,
            PeakElo = p.PeakElo,
            PlacementMatchesDone = p.PlacementMatchesDone,
            IsPlacementComplete = p.IsPlacementComplete,
            TotalWins = p.TotalWins,
            TotalLosses = p.TotalLosses,
            TotalDraws = p.TotalDraws
        }).ToList();
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
            res.Add(new LeaderboardEntryDto { Rank = i + 1, UserId = p.UserId, DisplayName = p.User?.DisplayName ?? "", AvatarUrl = p.User?.AvatarUrl, Elo = p.Elo, TotalWins = p.TotalWins });
        }
        return res;
    }
}
