using CubeNexus.Application.DTOs.Tournament;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Services;

public class TournamentService : ITournamentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApplicationDbContext _context;
    private readonly IRecordingStorageService? _storageService;

    public TournamentService(IUnitOfWork unitOfWork, ApplicationDbContext context, IRecordingStorageService? storageService = null)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _storageService = storageService;
    }

    private async Task<string?> UploadBannerPhotoAsync(Guid tournamentId, string? photoData, string? photoUrl, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(photoUrl))
            return photoUrl;

        if (string.IsNullOrWhiteSpace(photoData))
            return null;

        if (_storageService == null)
            return photoData;

        try
        {
            var base64Parts = photoData.Split(',');
            var header = base64Parts.Length > 1 ? base64Parts[0].ToLowerInvariant() : string.Empty;
            var base64String = base64Parts.Length > 1 ? base64Parts[1] : base64Parts[0];

            string contentType = "image/jpeg";
            string extension = "jpg";

            if (header.Contains("image/png"))
            {
                contentType = "image/png";
                extension = "png";
            }
            else if (header.Contains("image/webp"))
            {
                contentType = "image/webp";
                extension = "webp";
            }

            var bytes = Convert.FromBase64String(base64String.Trim());
            var objectKey = $"banner/tournaments/{tournamentId}_{Guid.NewGuid():N}.{extension}";

            using var ms = new System.IO.MemoryStream(bytes);
            await _storageService.UploadStreamAsync(objectKey, ms, contentType, ct);
            return objectKey;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Banner Upload Warning] Failed to upload banner to R2: {ex.Message}");
            return photoData;
        }
    }

    public async Task<TournamentDetailDto> CreateTournamentAsync(CreateTournamentDto dto, Guid managerId, CancellationToken ct = default)
    {
        // 1. Validate Dates
        var now = DateTime.UtcNow;
        if (dto.RegistrationOpenAt < now.AddMinutes(-5))
        {
            throw new InvalidOperationException("Thời gian mở đăng ký không được ở trong quá khứ.");
        }

        if (dto.RegistrationOpenAt >= dto.RegistrationCloseAt)
        {
            throw new InvalidOperationException("Thời gian mở đăng ký phải trước thời gian đóng đăng ký.");
        }

        if (dto.StartDate < dto.RegistrationCloseAt)
        {
            throw new InvalidOperationException("Thời gian bắt đầu giải đấu phải sau hoặc cùng thời gian đóng đăng ký.");
        }

        if (dto.StartDate >= dto.EndDate)
        {
            throw new InvalidOperationException("Thời gian bắt đầu giải đấu phải trước thời gian kết thúc giải đấu.");
        }

        // Validate creator user exists in database (handle stale tokens after DB reset)
        var creatorUser = await _unitOfWork.Users.GetByIdAsync(managerId, ct);
        if (creatorUser == null)
        {
            var allUsers = await _unitOfWork.Users.GetAllAsync(ct);
            creatorUser = allUsers.FirstOrDefault(u => u.UserRole == "ADMIN" || u.UserRole == "MANAGER")
                       ?? allUsers.FirstOrDefault();

            if (creatorUser == null)
            {
                throw new InvalidOperationException("No valid manager user found in database. Please log in again.");
            }
            managerId = creatorUser.Id;
        }
        var allActivePuzzles = await _unitOfWork.PuzzleTypes.GetAllActiveAsync();
        var activePuzzleIds = allActivePuzzles.Select(p => p.Id).ToHashSet();

        foreach (var ev in dto.Events)
        {
            if (!activePuzzleIds.Contains(ev.PuzzleTypeId))
            {
                throw new InvalidOperationException($"PuzzleTypeId {ev.PuzzleTypeId} is invalid or inactive.");
            }

            // Validate Event MaxCapacity vs Tournament MaxParticipants
            if (dto.MaxParticipants.HasValue && dto.MaxParticipants.Value > 0)
            {
                if (ev.MaxCapacity.HasValue && ev.MaxCapacity.Value > dto.MaxParticipants.Value)
                {
                    var puzzleName = allActivePuzzles.FirstOrDefault(p => p.Id == ev.PuzzleTypeId)?.Name ?? "Hạng mục";
                    throw new InvalidOperationException($"Giới hạn thí sinh môn '{puzzleName}' ({ev.MaxCapacity.Value}) không được lớn hơn tổng giới hạn thí sinh của toàn giải đấu ({dto.MaxParticipants.Value}).");
                }
            }

            if (ev.EventFormatCode == "MEDLEY")
            {
                if (ev.MedleyPuzzles == null || ev.MedleyPuzzles.Count < 2)
                {
                    throw new InvalidOperationException("Medley events must contain at least 2 puzzles.");
                }

                var medleyPuzzleTypes = new HashSet<Guid>();
                foreach (var mp in ev.MedleyPuzzles)
                {
                    if (!activePuzzleIds.Contains(mp.PuzzleTypeId))
                    {
                        throw new InvalidOperationException($"Medley PuzzleTypeId {mp.PuzzleTypeId} is invalid or inactive.");
                    }
                    if (!medleyPuzzleTypes.Add(mp.PuzzleTypeId))
                    {
                        throw new InvalidOperationException($"Medley event cannot contain duplicate puzzle types (Id: {mp.PuzzleTypeId}).");
                    }
                }
            }
            else if (ev.EventFormatCode == "TRADITIONAL")
            {
                if (ev.MedleyPuzzles != null && ev.MedleyPuzzles.Any())
                {
                    throw new InvalidOperationException("Traditional events cannot contain Medley puzzles.");
                }
            }
            else
            {
                throw new InvalidOperationException($"Unsupported EventFormatCode: {ev.EventFormatCode}");
            }
        }

        // 3. Create Entities
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            Location = dto.Location,
            MaxParticipants = dto.MaxParticipants,
            BannerUrl = dto.BannerUrl ?? dto.BannerPhotoData,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            RegistrationOpenAt = dto.RegistrationOpenAt,
            RegistrationCloseAt = dto.RegistrationCloseAt,
            StatusCode = "DRAFT", // Start as DRAFT until Manager clicks Publish
            CreatedBy = managerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var evDto in dto.Events)
        {
            var eventEntity = new Event
            {
                Id = Guid.NewGuid(),
                TournamentId = tournament.Id,
                PuzzleTypeId = evDto.PuzzleTypeId,
                EventFormatCode = evDto.EventFormatCode,
                TimeLimitMs = evDto.TimeLimitMs,
                CutoffTimeMs = evDto.CutoffTimeMs,
                SolveCount = evDto.SolveCount,
                TotalRounds = evDto.TotalRounds > 0 ? evDto.TotalRounds : 1,
                AdvanceTopN = evDto.AdvanceTopN > 0 ? evDto.AdvanceTopN : 16,
                SortOrder = evDto.SortOrder,
                MaxCapacity = evDto.MaxCapacity,
                CreatedAt = DateTime.UtcNow
            };

            if (evDto.EventFormatCode == "MEDLEY")
            {
                foreach (var mpDto in evDto.MedleyPuzzles)
                {
                    eventEntity.MedleyPuzzles.Add(new MedleyEventPuzzle
                    {
                        Id = Guid.NewGuid(),
                        EventId = eventEntity.Id,
                        PuzzleTypeId = mpDto.PuzzleTypeId,
                        SortOrder = mpDto.SortOrder
                    });
                }
            }

            tournament.Events.Add(eventEntity);
        }

        _unitOfWork.Tournaments.Add(tournament);
        await _unitOfWork.SaveChangesAsync(ct);

        return await GetTournamentByIdAsync(tournament.Id, ct);
    }

    public async Task<List<TournamentDetailDto>> GetPublicTournamentsAsync(CancellationToken ct = default)
    {
        var publicStatuses = new[] { "REGISTRATION_OPEN", "REGISTRATION_CLOSED", "PUBLISHED", "ONGOING", "COMPLETED" };

        // Single SQL round-trip: load tournaments with events + eager-count registrations via navigation
        var tournaments = await _context.Set<Tournament>()
            .Where(t => publicStatuses.Contains(t.StatusCode))
            .Include(t => t.CreatedByUser)
            .Include(t => t.Events)
                .ThenInclude(e => e.PuzzleType)
            .Include(t => t.Events)
                .ThenInclude(e => e.MedleyPuzzles)
                    .ThenInclude(mp => mp.PuzzleType)
            .OrderByDescending(t => t.StartDate)
            .AsSplitQuery()
            .ToListAsync(ct);

        // Count registrations per tournament with a second efficient batched query (indexed)
        var ids = tournaments.Select(t => t.Id).ToList();
        var regCounts = await _context.Set<Registration>()
            .Where(r => ids.Contains(r.TournamentId) && r.StatusCode != "CANCELLED")
            .GroupBy(r => r.TournamentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        return tournaments.Select(t => MapToDetailDto(t, regCounts.TryGetValue(t.Id, out var c) ? c : 0)).ToList();
    }

    public async Task<List<TournamentDetailDto>> GetManagerTournamentsAsync(Guid managerId, CancellationToken ct = default)
    {
        var assignedTournamentIds = _context.Set<TournamentManager>()
            .Where(tm => tm.UserId == managerId)
            .Select(tm => tm.TournamentId);

        // Managers see only tournaments they created or were explicitly assigned.
        // This intentionally includes DRAFT and other private statuses.
        var tournaments = await _context.Set<Tournament>()
            .Where(t => t.CreatedBy == managerId || assignedTournamentIds.Contains(t.Id))
            .Include(t => t.CreatedByUser)
            .Include(t => t.Events)
                .ThenInclude(e => e.PuzzleType)
            .Include(t => t.Events)
                .ThenInclude(e => e.MedleyPuzzles)
            .OrderByDescending(t => t.StartDate)
            .AsNoTracking()
            .ToListAsync(ct);

        var ids = tournaments.Select(t => t.Id).ToList();
        var regCounts = await _context.Set<Registration>()
            .Where(r => ids.Contains(r.TournamentId) && r.StatusCode != "CANCELLED")
            .GroupBy(r => r.TournamentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        return tournaments
            .Select(t => MapToDetailDto(t, regCounts.TryGetValue(t.Id, out var c) ? c : 0))
            .ToList();
    }

    public async Task<TournamentDetailDto> GetTournamentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _unitOfWork.Tournaments.GetTournamentWithEventsAndPuzzlesAsync(id, ct);
        if (tournament == null)
        {
            throw new KeyNotFoundException($"Tournament with ID {id} not found.");
        }
        var currentParticipants = await _context.Set<Registration>()
            .CountAsync(r => r.TournamentId == id && r.StatusCode != "CANCELLED", ct);
        return MapToDetailDto(tournament, currentParticipants);
    }

    public async Task<TournamentDetailDto> CloseRegistrationAsync(Guid tournamentId, Guid managerId, CancellationToken ct = default)
    {
        var tournament = await _unitOfWork.Tournaments.GetTournamentWithEventsAndPuzzlesAsync(tournamentId, ct);
        if (tournament == null)
        {
            throw new KeyNotFoundException($"Tournament with ID {tournamentId} not found.");
        }

        tournament.RegistrationCloseAt = DateTime.UtcNow;
        tournament.StatusCode = "REGISTRATION_CLOSED";
        tournament.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Tournaments.Update(tournament);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDetailDto(tournament);
    }

    private TournamentDetailDto MapToDetailDto(Tournament t, int currentParticipants = 0)
    {
        return new TournamentDetailDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Location = t.Location,
            MaxParticipants = t.MaxParticipants,
            CurrentParticipants = currentParticipants,
            BannerUrl = t.BannerUrl,
            StartDate = t.StartDate,
            EndDate = t.EndDate,
            RegistrationOpenAt = t.RegistrationOpenAt,
            RegistrationCloseAt = t.RegistrationCloseAt,
            StatusCode = t.StatusCode,
            CreatedBy = t.CreatedBy,
            CreatedByUserName = t.CreatedByUser?.DisplayName ?? string.Empty,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            Events = t.Events.Select(e => new EventDetailDto
            {
                Id = e.Id,
                PuzzleTypeId = e.PuzzleTypeId,
                PuzzleTypeName = e.PuzzleType?.Name ?? string.Empty,
                PuzzleTypeCode = e.PuzzleType?.Code ?? string.Empty,
                EventFormatCode = e.EventFormatCode,
                RegistrationStatusCode = e.RegistrationStatusCode ?? "OPEN",
                TimeLimitMs = e.TimeLimitMs,
                CutoffTimeMs = e.CutoffTimeMs,
                SolveCount = e.SolveCount,
                TotalRounds = e.TotalRounds > 0 ? e.TotalRounds : 1,
                AdvanceTopN = e.AdvanceTopN > 0 ? e.AdvanceTopN : 16,
                SortOrder = e.SortOrder,
                MaxCapacity = e.MaxCapacity,
                MedleyPuzzles = e.MedleyPuzzles.OrderBy(mp => mp.SortOrder).Select(mp => new MedleyPuzzleDetailDto
                {
                    Id = mp.Id,
                    PuzzleTypeId = mp.PuzzleTypeId,
                    PuzzleTypeName = mp.PuzzleType?.Name ?? string.Empty,
                    PuzzleTypeCode = mp.PuzzleType?.Code ?? string.Empty,
                    SortOrder = mp.SortOrder
                }).ToList()
            }).OrderBy(e => e.SortOrder).ToList()
        };
    }

    // =========================================================
    // Tournament-Scoped Judge Management CRUD
    // =========================================================

    // =========================================================
    // Tournament-Scoped Judge Management CRUD
    // =========================================================

    public async Task<List<TournamentJudgeDto>> GetTournamentJudgesAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var judges = await _context.TournamentJudges
            .Include(tj => tj.User)
            .Where(tj => tj.TournamentId == tournamentId)
            .OrderBy(tj => tj.RoleCode == "CHECKIN_JUDGE" ? 0 : 1)
            .ThenBy(tj => tj.AssignedStationNumber ?? 999)
            .ThenBy(tj => tj.AssignedAt)
            .ToListAsync(ct);

        return judges.Select(tj => new TournamentJudgeDto
        {
            Id = tj.Id,
            UserId = tj.UserId,
            TournamentId = tj.TournamentId,
            DisplayName = tj.User?.DisplayName ?? "Judge",
            Username = GetUsernameFromUser(tj.User),
            Email = tj.User?.Email ?? string.Empty,
            UserCode = tj.User?.UserCode ?? string.Empty,
            RoleCode = tj.RoleCode ?? "STATION_JUDGE",
            AssignedStationNumber = tj.AssignedStationNumber,
            AssignedAt = tj.AssignedAt,
            IsActive = tj.User?.IsActive ?? true,
            RawPassword = null
        }).ToList();
    }

    public async Task<TournamentJudgeDto> CreateTournamentJudgeAsync(Guid tournamentId, CreateTournamentJudgeDto dto, Guid managerId, CancellationToken ct = default)
    {
        var tournament = await _unitOfWork.Tournaments.GetByIdAsync(tournamentId, ct);
        if (tournament == null)
            throw new KeyNotFoundException($"Tournament with ID {tournamentId} not found.");

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
            throw new InvalidOperationException("DisplayName is required for Judge.");

        string username;
        string email;
        string userCode;

        if (!string.IsNullOrWhiteSpace(dto.Username))
        {
            username = dto.Username.Trim();
            email = username.Contains('@') ? username : $"{username}@cubenexus.com";
            userCode = username.Contains('@') ? username.Split('@')[0] : username;

            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() || (u.UserCode != null && u.UserCode.ToLower() == userCode.ToLower()), ct);
            if (existingUser != null)
                throw new InvalidOperationException($"Username/Email '{username}' already exists. Please choose a different username.");
        }
        else
        {
            var existingJudgeUsers = await _context.Users
                .Where(u => u.Email.StartsWith("judge") || (u.UserCode != null && u.UserCode.StartsWith("judge")))
                .Select(u => new { u.Email, u.UserCode })
                .ToListAsync(ct);

            var usedNumbers = new HashSet<int>();
            var regex = new System.Text.RegularExpressions.Regex(@"^judge(\d+)(@|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var item in existingJudgeUsers)
            {
                var m1 = regex.Match(item.Email ?? "");
                if (m1.Success && int.TryParse(m1.Groups[1].Value, out int n1))
                    usedNumbers.Add(n1);

                var m2 = regex.Match(item.UserCode ?? "");
                if (m2.Success && int.TryParse(m2.Groups[1].Value, out int n2))
                    usedNumbers.Add(n2);
            }

            int nextSeq = 1;
            while (usedNumbers.Contains(nextSeq))
            {
                nextSeq++;
            }

            username = $"judge{nextSeq}";
            email = $"judge{nextSeq}@cubenexus.com";
            userCode = $"judge{nextSeq}";
        }

        var rawPassword = !string.IsNullOrWhiteSpace(dto.Password) 
            ? dto.Password.Trim() 
            : "1";

        var now = DateTime.UtcNow;
        bool isTournamentLive = tournament.StatusCode == "CHECKING_IN" || tournament.StatusCode == "ONGOING";

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserCode = userCode,
            Email = email,
            PasswordHash = CubeNexus.Infrastructure.Identity.AuthService.HashPassword(rawPassword),
            DisplayName = dto.DisplayName.Trim(),
            UserRole = "JUDGE",
            IsActive = isTournamentLive,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        var tournamentJudge = new TournamentJudge
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            UserId = user.Id,
            RoleCode = string.IsNullOrWhiteSpace(dto.RoleCode) ? "STATION_JUDGE" : dto.RoleCode.Trim(),
            AssignedStationNumber = dto.AssignedStationNumber,
            AssignedAt = now
        };

        await _context.Users.AddAsync(user, ct);
        await _context.TournamentJudges.AddAsync(tournamentJudge, ct);
        await _context.SaveChangesAsync(ct);

        return new TournamentJudgeDto
        {
            Id = tournamentJudge.Id,
            UserId = user.Id,
            TournamentId = tournamentId,
            DisplayName = user.DisplayName,
            Username = username,
            Email = user.Email,
            UserCode = user.UserCode,
            RoleCode = tournamentJudge.RoleCode,
            AssignedStationNumber = tournamentJudge.AssignedStationNumber,
            AssignedAt = tournamentJudge.AssignedAt,
            IsActive = user.IsActive,
            RawPassword = rawPassword
        };
    }

    public async Task<List<TournamentJudgeDto>> BatchCreateTournamentJudgesAsync(Guid tournamentId, BatchCreateTournamentJudgeDto dto, Guid managerId, CancellationToken ct = default)
    {
        var tournament = await _unitOfWork.Tournaments.GetByIdAsync(tournamentId, ct);
        if (tournament == null)
            throw new KeyNotFoundException($"Tournament with ID {tournamentId} not found.");

        int checkInCount = Math.Max(0, dto.CheckInCount);
        int stationCount = Math.Max(0, dto.StationCount);
        int judgesPerStation = Math.Max(1, dto.JudgesPerStation);

        // Prepare assignment list
        var assignments = new List<(string RoleCode, int? StationNumber, string DefaultName)>();

        for (int i = 1; i <= checkInCount; i++)
        {
            assignments.Add(("CHECKIN_JUDGE", null, checkInCount == 1 ? "Trọng tài Check-in" : $"Trọng tài Check-in {i}"));
        }

        for (int s = 1; s <= stationCount; s++)
        {
            for (int j = 1; j <= judgesPerStation; j++)
            {
                string name = judgesPerStation > 1
                    ? $"Trọng tài Bàn {s} ({j})"
                    : $"Trọng tài Bàn {s}";
                assignments.Add(("STATION_JUDGE", s, name));
            }
        }

        // Legacy fallback if no structured inputs provided
        if (!assignments.Any())
        {
            int legacyCount = Math.Clamp(dto.Count > 0 ? dto.Count : 5, 1, 50);
            var prefix = string.IsNullOrWhiteSpace(dto.NamePrefix) ? "Trọng tài" : dto.NamePrefix.Trim();
            for (int i = 1; i <= legacyCount; i++)
            {
                assignments.Add(("STATION_JUDGE", i <= 5 ? i : null, $"{prefix} {i}"));
            }
        }

        var existingJudgeUsers = await _context.Users
            .Where(u => u.Email.StartsWith("judge") || (u.UserCode != null && u.UserCode.StartsWith("judge")))
            .Select(u => new { u.Email, u.UserCode })
            .ToListAsync(ct);

        var usedNumbers = new HashSet<int>();
        var regex = new System.Text.RegularExpressions.Regex(@"^judge(\d+)(@|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var item in existingJudgeUsers)
        {
            var m1 = regex.Match(item.Email ?? "");
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out int n1))
                usedNumbers.Add(n1);

            var m2 = regex.Match(item.UserCode ?? "");
            if (m2.Success && int.TryParse(m2.Groups[1].Value, out int n2))
                usedNumbers.Add(n2);
        }

        int nextJudgeSeq = 1;
        var now = DateTime.UtcNow;
        var resultList = new List<TournamentJudgeDto>();
        bool isTournamentLive = tournament.StatusCode == "CHECKING_IN" || tournament.StatusCode == "ONGOING";
        const string defaultPassword = "1";

        for (int i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            string displayName = (dto.CustomNames != null && dto.CustomNames.Count > i && !string.IsNullOrWhiteSpace(dto.CustomNames[i]))
                ? dto.CustomNames[i].Trim()
                : assignment.DefaultName;

            while (usedNumbers.Contains(nextJudgeSeq))
            {
                nextJudgeSeq++;
            }
            usedNumbers.Add(nextJudgeSeq);

            var username = $"judge{nextJudgeSeq}";
            var email = $"judge{nextJudgeSeq}@cubenexus.com";
            var userCode = $"judge{nextJudgeSeq}";
            var rawPassword = defaultPassword;

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserCode = userCode,
                Email = email,
                PasswordHash = CubeNexus.Infrastructure.Identity.AuthService.HashPassword(rawPassword),
                DisplayName = displayName,
                UserRole = "JUDGE",
                IsActive = isTournamentLive,
                EmailConfirmed = true,
                EmailConfirmedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            var tournamentJudge = new TournamentJudge
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                UserId = user.Id,
                RoleCode = assignment.RoleCode,
                AssignedStationNumber = assignment.StationNumber,
                AssignedAt = now
            };

            await _context.Users.AddAsync(user, ct);
            await _context.TournamentJudges.AddAsync(tournamentJudge, ct);

            resultList.Add(new TournamentJudgeDto
            {
                Id = tournamentJudge.Id,
                UserId = user.Id,
                TournamentId = tournamentId,
                DisplayName = user.DisplayName,
                Username = username,
                Email = user.Email,
                UserCode = user.UserCode,
                RoleCode = tournamentJudge.RoleCode,
                AssignedStationNumber = tournamentJudge.AssignedStationNumber,
                AssignedAt = now,
                IsActive = user.IsActive,
                RawPassword = rawPassword
            });
        }

        await _context.SaveChangesAsync(ct);
        return resultList;
    }

    public async Task<TournamentJudgeDto> UpdateTournamentJudgeAsync(Guid tournamentId, Guid judgeUserId, UpdateTournamentJudgeDto dto, Guid managerId, CancellationToken ct = default)
    {
        var tj = await _context.TournamentJudges
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.UserId == judgeUserId, ct);

        if (tj == null)
            throw new KeyNotFoundException("Judge assignment not found in this tournament.");

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
            throw new InvalidOperationException("DisplayName cannot be empty.");

        tj.User.DisplayName = dto.DisplayName.Trim();
        tj.User.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.RoleCode))
        {
            tj.RoleCode = dto.RoleCode.Trim();
        }

        tj.AssignedStationNumber = dto.AssignedStationNumber;

        await _context.SaveChangesAsync(ct);

        return new TournamentJudgeDto
        {
            Id = tj.Id,
            UserId = tj.UserId,
            TournamentId = tj.TournamentId,
            DisplayName = tj.User.DisplayName,
            Username = GetUsernameFromUser(tj.User),
            Email = tj.User.Email,
            UserCode = tj.User.UserCode,
            RoleCode = tj.RoleCode,
            AssignedStationNumber = tj.AssignedStationNumber,
            AssignedAt = tj.AssignedAt,
            IsActive = tj.User.IsActive,
            RawPassword = null
        };
    }

    public async Task<List<TournamentJudgeDto>> ShuffleTournamentJudgesAsync(Guid tournamentId, ShuffleTournamentJudgesDto dto, Guid managerId, CancellationToken ct = default)
    {
        var judges = await _context.TournamentJudges
            .Include(tj => tj.User)
            .Where(tj => tj.TournamentId == tournamentId)
            .ToListAsync(ct);

        if (!judges.Any())
            return new List<TournamentJudgeDto>();

        int checkInCount = Math.Max(0, dto.CheckInCount);
        int stationCount = Math.Max(0, dto.StationCount);
        int judgesPerStation = Math.Max(1, dto.JudgesPerStation);

        int totalRequiredSlots = checkInCount + (stationCount * judgesPerStation);
        if (totalRequiredSlots > judges.Count)
        {
            throw new InvalidOperationException($"Tổng số vị trí cần phân công ({totalRequiredSlots}) vượt quá số lượng Trọng tài hiện có trong giải đấu ({judges.Count}). Vui lòng tạo thêm trọng tài hoặc giảm số vị trí.");
        }

        // Prepare assignment target pool
        var pool = new List<(string RoleCode, int? StationNumber, string BaseName)>();

        for (int i = 1; i <= checkInCount; i++)
        {
            pool.Add(("CHECKIN_JUDGE", null, checkInCount == 1 ? "Trọng tài Check-in" : $"Trọng tài Check-in {i}"));
        }

        for (int s = 1; s <= stationCount; s++)
        {
            for (int j = 1; j <= judgesPerStation; j++)
            {
                string name = judgesPerStation > 1 ? $"Trọng tài Bàn {s} ({j})" : $"Trọng tài Bàn {s}";
                pool.Add(("STATION_JUDGE", s, name));
            }
        }

        // Randomize judges order
        var random = new Random();
        var shuffledJudges = judges.OrderBy(_ => random.Next()).ToList();

        for (int i = 0; i < shuffledJudges.Count; i++)
        {
            var judge = shuffledJudges[i];
            if (i < pool.Count)
            {
                var target = pool[i];
                judge.RoleCode = target.RoleCode;
                judge.AssignedStationNumber = target.StationNumber;
                judge.User.DisplayName = target.BaseName;
            }
            else
            {
                judge.RoleCode = "GENERAL_JUDGE";
                judge.AssignedStationNumber = null;
                judge.User.DisplayName = $"Trọng tài Dự phòng {i - pool.Count + 1}";
            }
            judge.User.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);

        return judges.Select(tj => new TournamentJudgeDto
        {
            Id = tj.Id,
            UserId = tj.UserId,
            TournamentId = tj.TournamentId,
            DisplayName = tj.User.DisplayName,
            Username = GetUsernameFromUser(tj.User),
            Email = tj.User.Email,
            UserCode = tj.User.UserCode,
            RoleCode = tj.RoleCode,
            AssignedStationNumber = tj.AssignedStationNumber,
            AssignedAt = tj.AssignedAt,
            IsActive = tj.User.IsActive,
            RawPassword = null
        }).ToList();
    }

    public async Task<TournamentJudgeDto> ResetTournamentJudgePasswordAsync(Guid tournamentId, Guid judgeUserId, ResetJudgePasswordDto dto, Guid managerId, CancellationToken ct = default)
    {
        var tj = await _context.TournamentJudges
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.UserId == judgeUserId, ct);

        if (tj == null)
            throw new KeyNotFoundException("Judge assignment not found in this tournament.");

        var rawPassword = !string.IsNullOrWhiteSpace(dto.NewPassword)
            ? dto.NewPassword.Trim()
            : "1";

        tj.User.PasswordHash = CubeNexus.Infrastructure.Identity.AuthService.HashPassword(rawPassword);
        tj.User.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new TournamentJudgeDto
        {
            Id = tj.Id,
            UserId = tj.UserId,
            TournamentId = tj.TournamentId,
            DisplayName = tj.User.DisplayName,
            Username = GetUsernameFromUser(tj.User),
            Email = tj.User.Email,
            UserCode = tj.User.UserCode,
            RoleCode = tj.RoleCode,
            AssignedStationNumber = tj.AssignedStationNumber,
            AssignedAt = tj.AssignedAt,
            IsActive = tj.User.IsActive,
            RawPassword = rawPassword
        };
    }

    public async Task<TournamentJudgeDto> ToggleJudgeStatusAsync(Guid tournamentId, Guid judgeUserId, bool isActive, Guid managerId, CancellationToken ct = default)
    {
        var tj = await _context.TournamentJudges
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.UserId == judgeUserId, ct);

        if (tj == null)
            throw new KeyNotFoundException("Trọng tài không thuộc giải đấu này.");

        if (tj.User != null)
        {
            tj.User.IsActive = isActive;
            tj.User.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        return new TournamentJudgeDto
        {
            Id = tj.Id,
            UserId = tj.UserId,
            TournamentId = tj.TournamentId,
            DisplayName = tj.User?.DisplayName ?? "Judge",
            Username = GetUsernameFromUser(tj.User),
            Email = tj.User?.Email ?? string.Empty,
            UserCode = tj.User?.UserCode ?? string.Empty,
            RoleCode = tj.RoleCode ?? "STATION_JUDGE",
            AssignedStationNumber = tj.AssignedStationNumber,
            AssignedAt = tj.AssignedAt,
            IsActive = tj.User?.IsActive ?? true
        };
    }

    public async Task<List<TournamentJudgeDto>> DeactivateAllJudgesAsync(Guid tournamentId, Guid managerId, CancellationToken ct = default)
    {
        var judges = await _context.TournamentJudges
            .Include(x => x.User)
            .Where(x => x.TournamentId == tournamentId)
            .ToListAsync(ct);

        foreach (var tj in judges)
        {
            if (tj.User != null)
            {
                tj.User.IsActive = false;
                tj.User.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(ct);
        return await GetTournamentJudgesAsync(tournamentId, ct);
    }

    public async Task<List<TournamentJudgeDto>> ActivateAllJudgesAsync(Guid tournamentId, Guid managerId, CancellationToken ct = default)
    {
        var judges = await _context.TournamentJudges
            .Include(x => x.User)
            .Where(x => x.TournamentId == tournamentId)
            .ToListAsync(ct);

        foreach (var tj in judges)
        {
            if (tj.User != null)
            {
                tj.User.IsActive = true;
                tj.User.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(ct);
        return await GetTournamentJudgesAsync(tournamentId, ct);
    }

    public async Task DeleteTournamentJudgeAsync(Guid tournamentId, Guid judgeUserId, Guid managerId, CancellationToken ct = default)
    {
        var tj = await _context.TournamentJudges
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.UserId == judgeUserId, ct);

        if (tj == null)
            throw new KeyNotFoundException("Judge assignment not found in this tournament.");

        _context.TournamentJudges.Remove(tj);
        await _context.SaveChangesAsync(ct);
    }

    private static string GetUsernameFromUser(User? user)
    {
        if (user == null) return string.Empty;
        if (!string.IsNullOrEmpty(user.Email) && user.Email.Contains('@'))
        {
            var prefix = user.Email.Split('@')[0];
            if (!prefix.StartsWith("P") && !prefix.StartsWith("J"))
                return prefix;
        }
        return !string.IsNullOrEmpty(user.UserCode) ? user.UserCode : user.Email;
    }
}

