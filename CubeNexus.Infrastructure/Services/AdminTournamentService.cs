using CubeNexus.Application.DTOs.Admin;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Services;

public class AdminTournamentService : IAdminTournamentService
{
    private readonly ApplicationDbContext _context;

    public AdminTournamentService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AdminTournamentPagedResultDto> GetTournamentsAsync(
        int page = 1,
        int pageSize = 10,
        string? search = null,
        string? status = null,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

        var query = _context.Tournaments
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.Events)
                .ThenInclude(e => e.PuzzleType)
            .Include(t => t.Events)
                .ThenInclude(e => e.MedleyPuzzles)
                    .ThenInclude(mp => mp.PuzzleType)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(t =>
                t.Name.ToLower().Contains(s) ||
                (t.Location != null && t.Location.ToLower().Contains(s)) ||
                (t.CreatedByUser != null && (
                    t.CreatedByUser.DisplayName.ToLower().Contains(s) ||
                    t.CreatedByUser.Email.ToLower().Contains(s) ||
                    t.CreatedByUser.UserCode.ToLower().Contains(s)
                ))
            );
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.StatusCode.ToUpper() == status.Trim().ToUpper());
        }

        var totalCount = await query.CountAsync(ct);

        var tournaments = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var tournamentIds = tournaments.Select(t => t.Id).ToList();
        var regCounts = await _context.Registrations
            .AsNoTracking()
            .Where(r => tournamentIds.Contains(r.TournamentId) && r.StatusCode != "CANCELLED")
            .GroupBy(r => r.TournamentId)
            .Select(g => new { TournamentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TournamentId, x => x.Count, ct);

        var items = tournaments.Select(t => new AdminTournamentDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Location = t.Location,
            MaxParticipants = t.MaxParticipants,
            RegisteredParticipantsCount = regCounts.TryGetValue(t.Id, out var count) ? count : 0,
            BannerUrl = t.BannerUrl,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            RegistrationOpenAt = t.RegistrationOpenAt,
            RegistrationCloseAt = t.RegistrationCloseAt,
            StatusCode = t.StatusCode,
            CreatedByUserId = t.CreatedBy,
            CreatedByName = t.CreatedByUser?.DisplayName ?? "Unknown",
            CreatedByEmail = t.CreatedByUser?.Email ?? string.Empty,
            CreatedByCode = t.CreatedByUser?.UserCode ?? string.Empty,
            CreatedAt = t.CreatedAt,
            EventsCount = t.Events.Count,
            Events = t.Events.Select(e => new AdminTournamentEventDto
            {
                Id = e.Id,
                PuzzleTypeId = e.PuzzleTypeId,
                PuzzleTypeName = e.PuzzleType?.Name ?? string.Empty,
                PuzzleTypeCode = e.PuzzleType?.Code ?? string.Empty,
                EventFormatCode = e.EventFormatCode,
                RegistrationStatusCode = e.RegistrationStatusCode,
                MedleyPuzzles = e.MedleyPuzzles.OrderBy(mp => mp.SortOrder).Select(mp => new AdminMedleyPuzzleDto
                {
                    Id = mp.Id,
                    PuzzleTypeId = mp.PuzzleTypeId,
                    PuzzleTypeName = mp.PuzzleType?.Name ?? string.Empty,
                    PuzzleTypeCode = mp.PuzzleType?.Code ?? string.Empty,
                    SortOrder = mp.SortOrder
                }).ToList()
            }).ToList(),
        }).ToList();

        return new AdminTournamentPagedResultDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<AdminTournamentDto> GetTournamentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _context.Tournaments
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Events)
                .ThenInclude(e => e.PuzzleType)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (t == null)
        {
            throw new KeyNotFoundException("Không tìm thấy giải đấu.");
        }

        var regCount = await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.TournamentId == id && r.StatusCode != "CANCELLED", ct);

        return new AdminTournamentDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Location = t.Location,
            MaxParticipants = t.MaxParticipants,
            RegisteredParticipantsCount = regCount,
            BannerUrl = t.BannerUrl,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            RegistrationOpenAt = t.RegistrationOpenAt,
            RegistrationCloseAt = t.RegistrationCloseAt,
            StatusCode = t.StatusCode,
            CreatedByUserId = t.CreatedBy,
            CreatedByName = t.CreatedByUser?.DisplayName ?? "Unknown",
            CreatedByEmail = t.CreatedByUser?.Email ?? string.Empty,
            CreatedByCode = t.CreatedByUser?.UserCode ?? string.Empty,
            CreatedAt = t.CreatedAt,
            EventsCount = t.Events.Count,
            Events = t.Events.Select(e => new AdminTournamentEventDto
            {
                Id = e.Id,
                PuzzleTypeId = e.PuzzleTypeId,
                PuzzleTypeName = e.PuzzleType?.Name ?? string.Empty,
                PuzzleTypeCode = e.PuzzleType?.Code ?? string.Empty,
                RegistrationStatusCode = e.RegistrationStatusCode,
            }).ToList(),
        };
    }

    public async Task<AdminTournamentDto> UpdateTournamentStatusAsync(Guid id, string statusCode, CancellationToken ct = default)
    {
        var t = await _context.Tournaments
            .Include(x => x.CreatedByUser)
            .Include(x => x.Events)
                .ThenInclude(e => e.PuzzleType)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (t == null)
        {
            throw new KeyNotFoundException("Không tìm thấy giải đấu.");
        }

        var normalizedStatus = (statusCode ?? "").Trim().ToUpper();
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            throw new ArgumentException("Trạng thái không được để trống.");
        }

        t.StatusCode = normalizedStatus;
        t.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var regCount = await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.TournamentId == id && r.StatusCode != "CANCELLED", ct);

        return new AdminTournamentDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Location = t.Location,
            MaxParticipants = t.MaxParticipants,
            RegisteredParticipantsCount = regCount,
            BannerUrl = t.BannerUrl,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            RegistrationOpenAt = t.RegistrationOpenAt,
            RegistrationCloseAt = t.RegistrationCloseAt,
            StatusCode = t.StatusCode,
            CreatedByUserId = t.CreatedBy,
            CreatedByName = t.CreatedByUser?.DisplayName ?? "Unknown",
            CreatedByEmail = t.CreatedByUser?.Email ?? string.Empty,
            CreatedByCode = t.CreatedByUser?.UserCode ?? string.Empty,
            CreatedAt = t.CreatedAt,
            EventsCount = t.Events.Count,
            Events = t.Events.Select(e => new AdminTournamentEventDto
            {
                Id = e.Id,
                PuzzleTypeId = e.PuzzleTypeId,
                PuzzleTypeName = e.PuzzleType?.Name ?? string.Empty,
                PuzzleTypeCode = e.PuzzleType?.Code ?? string.Empty,
                RegistrationStatusCode = e.RegistrationStatusCode,
            }).ToList(),
        };
    }
}
