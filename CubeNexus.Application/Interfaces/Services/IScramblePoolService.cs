using CubeNexus.Application.DTOs.Admin;

namespace CubeNexus.Application.Interfaces.Services;

public interface IScramblePoolService
{
    Task<ScrambleReservationDto> ReserveAsync(string competitionMode, Guid puzzleTypeId,
        string targetType, Guid targetId, Guid? actorUserId = null, CancellationToken ct = default);
    Task MarkUsedAsync(Guid scramblePoolItemId, Guid? actorUserId = null, CancellationToken ct = default);
}

public interface IAdminScrambleService
{
    Task<IReadOnlyList<ScramblePoolSummaryDto>> GetSummaryAsync(CancellationToken ct = default);
    Task<ScramblePoolPageDto> GetItemsAsync(string? mode, string? status, Guid? puzzleTypeId,
        int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<ScramblePoolItemDto>> GenerateAsync(GenerateScramblesRequestDto request,
        Guid actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<ScramblePoolItemDto>> ImportAsync(ImportScramblesRequestDto request,
        Guid actorUserId, CancellationToken ct = default);
    Task<ScramblePoolItemDto> ApproveAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
    Task<ScramblePoolItemDto> RetireAsync(Guid id, Guid actorUserId, CancellationToken ct = default);
}
