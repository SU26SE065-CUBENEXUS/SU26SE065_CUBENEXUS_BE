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
            .Include(t => t.PuzzleType)
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
            TournamentType = t.TournamentType,
            FormatCode = t.FormatCode,
            PuzzleTypeId = t.PuzzleTypeId,
            PuzzleTypeName = t.PuzzleType?.Name,
            PuzzleTypeCode = t.PuzzleType?.Code,
            AttemptTimeLimitMs = t.AttemptTimeLimitMs,
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
            .Include(x => x.PuzzleType)
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
            TournamentType = t.TournamentType,
            FormatCode = t.FormatCode,
            PuzzleTypeId = t.PuzzleTypeId,
            PuzzleTypeName = t.PuzzleType?.Name,
            PuzzleTypeCode = t.PuzzleType?.Code,
            AttemptTimeLimitMs = t.AttemptTimeLimitMs,
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
            .Include(x => x.PuzzleType)
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

        var currentStatus = (t.StatusCode ?? "").Trim().ToUpper();
        if (currentStatus != normalizedStatus)
        {
            bool isValidTransition = false;
            if (normalizedStatus == "PUBLISHED")
            {
                isValidTransition = currentStatus == "DRAFT" || currentStatus == "DISABLED";
            }
            else if (normalizedStatus == "REGISTRATION_OPEN")
            {
                isValidTransition = currentStatus == "PUBLISHED" || currentStatus == "DRAFT" || currentStatus == "DISABLED";
            }
            else if (normalizedStatus == "REGISTRATION_CLOSED")
            {
                isValidTransition = currentStatus == "REGISTRATION_OPEN" || currentStatus == "PUBLISHED";
            }
            else if (normalizedStatus == "CHECKING_IN")
            {
                isValidTransition = currentStatus == "REGISTRATION_CLOSED";
            }
            else if (normalizedStatus == "ONGOING")
            {
                isValidTransition = currentStatus == "CHECKING_IN";
            }
            else if (normalizedStatus == "COMPLETED")
            {
                isValidTransition = currentStatus == "ONGOING";
            }
            else if (normalizedStatus == "CANCELLED")
            {
                isValidTransition = currentStatus == "DRAFT" || currentStatus == "PUBLISHED" || currentStatus == "REGISTRATION_OPEN" || currentStatus == "REGISTRATION_CLOSED" || currentStatus == "CHECKING_IN";
            }
            else if (normalizedStatus == "DISABLED")
            {
                isValidTransition = currentStatus == "DRAFT" || currentStatus == "PUBLISHED" || currentStatus == "REGISTRATION_OPEN" || currentStatus == "REGISTRATION_CLOSED";
            }

            if (!isValidTransition)
            {
                throw new InvalidOperationException($"Không thể chuyển trạng thái giải đấu từ {currentStatus} sang {normalizedStatus}.");
            }
        }

        t.StatusCode = normalizedStatus;
        t.UpdatedAt = DateTime.UtcNow;

        // Auto-activate judge accounts if tournament is CHECKING_IN
        if (normalizedStatus == "CHECKING_IN")
        {
            var judgeUserIds = await _context.TournamentJudges
                .Where(tj => tj.TournamentId == id)
                .Select(tj => tj.UserId)
                .ToListAsync(ct);

            if (judgeUserIds.Any())
            {
                var judgeUsers = await _context.Users
                    .Where(u => judgeUserIds.Contains(u.Id))
                    .ToListAsync(ct);

                foreach (var judgeUser in judgeUsers)
                {
                    judgeUser.IsActive = true;
                }
            }
        }

        // Auto-deactivate judge accounts if tournament is COMPLETED or CANCELLED
        if (normalizedStatus == "COMPLETED" || normalizedStatus == "CANCELLED")
        {
            if (normalizedStatus == "COMPLETED" && t.TournamentType != "ONLINE_ASYNC")
            {
                var eventIds = t.Events.Select(e => e.Id).ToList();
                if (eventIds.Any())
                {
                    var hasUncompletedGroups = await _context.Groups
                        .AnyAsync(g => eventIds.Contains(g.EventId) && g.StatusCode != "COMPLETED", ct);

                    if (hasUncompletedGroups)
                    {
                        throw new InvalidOperationException("Không thể hoàn thành giải đấu vì còn nhóm hoặc vòng thi chưa hoàn tất (chưa Complete Round).");
                    }
                }
            }

            var judgeUserIds = await _context.TournamentJudges
                .Where(tj => tj.TournamentId == id)
                .Select(tj => tj.UserId)
                .ToListAsync(ct);

            if (judgeUserIds.Any())
            {
                var judgeUsers = await _context.Users
                    .Where(u => judgeUserIds.Contains(u.Id))
                    .ToListAsync(ct);

                foreach (var judgeUser in judgeUsers)
                {
                    judgeUser.IsActive = false;
                }
            }
        }

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
            TournamentType = t.TournamentType,
            FormatCode = t.FormatCode,
            PuzzleTypeId = t.PuzzleTypeId,
            PuzzleTypeName = t.PuzzleType?.Name,
            PuzzleTypeCode = t.PuzzleType?.Code,
            AttemptTimeLimitMs = t.AttemptTimeLimitMs,
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

    public async Task<AdminTournamentDto> ForceStartOnlineAsyncTournamentAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.CreatedByUser)
            .Include(t => t.PuzzleType)
            .Include(t => t.Events).ThenInclude(e => e.PuzzleType)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tournament was not found.");

        if (tournament.TournamentType != "ONLINE_ASYNC")
            throw new InvalidOperationException("This development action is available only for online asynchronous tournaments.");
        if (tournament.StatusCode is "DISABLED" or "CANCELLED" or "COMPLETED")
            throw new InvalidOperationException("A disabled, cancelled, or completed tournament cannot be started.");

        var now = DateTime.UtcNow;
        tournament.RegistrationCloseAt = now;
        tournament.StartDate = now;
        if (tournament.EndDate <= now)
            tournament.EndDate = now.AddHours(1);
        tournament.StatusCode = "ONGOING";
        tournament.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
        return await GetTournamentByIdAsync(id, ct);
    }

    public async Task<AdminTournamentDto> CloseOnlineAsyncRegistrationAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _context.Tournaments.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tournament was not found.");

        if (tournament.TournamentType != "ONLINE_ASYNC")
            throw new InvalidOperationException("This development action is available only for online asynchronous tournaments.");
        if (tournament.StatusCode is "DISABLED" or "CANCELLED" or "COMPLETED")
            throw new InvalidOperationException("Registration cannot be changed for this tournament.");

        var now = DateTime.UtcNow;
        if (now >= tournament.StartDate)
            throw new InvalidOperationException("Registration is already closed because the competition has started.");

        tournament.RegistrationCloseAt = now;
        tournament.StatusCode = "REGISTRATION_CLOSED";
        tournament.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
        return await GetTournamentByIdAsync(id, ct);
    }
}
