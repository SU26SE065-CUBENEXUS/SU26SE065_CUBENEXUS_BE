using System.Text.Json;
using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Services;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Services;

public class TournamentRegistrationService : ITournamentRegistrationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApplicationDbContext _context;

    public TournamentRegistrationService(IUnitOfWork unitOfWork, ApplicationDbContext context)
    {
        _unitOfWork = unitOfWork;
        _context = context;
    }

    public async Task<DemoParticipantGenerationResultDto> GenerateDemoParticipantsAsync(
        Guid tournamentId,
        Guid managerId,
        int count = 20,
        CancellationToken ct = default)
    {
        if (count < 1)
            throw new InvalidOperationException("Demo participant count must be greater than zero.");

        var hasAccess = await _context.Set<Tournament>()
            .AnyAsync(t => t.Id == tournamentId &&
                (t.CreatedBy == managerId || _context.Set<TournamentManager>()
                    .Any(tm => tm.TournamentId == tournamentId && tm.UserId == managerId)), ct);
        if (!hasAccess)
            throw new UnauthorizedAccessException("You do not have access to this tournament.");

        var tournament = await _unitOfWork.Tournaments.GetTournamentWithEventsAndPuzzlesAsync(tournamentId, ct);
        if (tournament == null)
            throw new KeyNotFoundException($"Tournament with ID {tournamentId} not found.");
        if (tournament.Events.Count == 0)
            throw new InvalidOperationException("The tournament must have at least one event before generating demo participants.");

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var result = new DemoParticipantGenerationResultDto
        {
            TournamentId = tournamentId,
            RequestedCount = count
        };

        try
        {
            var eventId = tournament.Events.OrderBy(e => e.SortOrder ?? int.MaxValue).First().Id;
            var demoPasswordHash = "100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=";

            for (var index = 1; index <= count; index++)
            {
                var code = $"DEMO-COMP-{index:000}";
                var email = $"{code.ToLowerInvariant()}@demo.cubenexus.com";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserCode == code, ct);

                if (user == null)
                {
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        UserCode = code,
                        Email = email,
                        PasswordHash = demoPasswordHash,
                        DisplayName = $"Demo Competitor {index:00}",
                        UserRole = "COMPETITOR",
                        IsActive = true,
                        IsBanned = false,
                        EmailConfirmed = true,
                        EmailConfirmedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync(ct);
                }

                var enrollment = await _context.FaceEnrollments
                    .FirstOrDefaultAsync(f => f.UserId == user.Id, ct);
                if (enrollment == null)
                {
                    _context.FaceEnrollments.Add(new FaceEnrollment
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        Status = "ENROLLED",
                        ModelVersion = "demo",
                        QualityScore = 1.0,
                        TemplatesCount = 3,
                        LastExternalSessionId = $"demo-face-{code}",
                        EnrolledAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }

                var existing = await _context.Registrations
                    .FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.UserId == user.Id && r.StatusCode != "CANCELLED", ct);
                if (existing != null)
                {
                    result.ExistingRegistrations++;
                    result.ParticipantCodes.Add(code);
                    continue;
                }

                // Reuse the real registration workflow so capacity, status, date,
                // duplicate, Face Enrollment, event and schedule validations remain active.
                await RegisterCompetitorAsync(tournamentId, user.Id, new RegisterTournamentDto
                {
                    Events = [new RegisterEventDto { EventId = eventId }]
                }, ct);

                result.NewRegistrations++;
                result.ParticipantCodes.Add(code);
            }

            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<RegistrationResultDto> RegisterCompetitorAsync(Guid tournamentId, Guid userId, RegisterTournamentDto dto, CancellationToken ct = default)
    {
        var tournament = await _unitOfWork.Tournaments.GetTournamentWithEventsAndPuzzlesAsync(tournamentId, ct);
        if (tournament == null)
            throw new KeyNotFoundException($"Tournament with ID {tournamentId} not found.");

        // Validations
        var now = DateTime.UtcNow;
        if (now < tournament.RegistrationOpenAt || now > tournament.RegistrationCloseAt)
            throw new InvalidOperationException("Registration is currently closed for this tournament.");

        if (tournament.StatusCode != "REGISTRATION_OPEN")
            throw new InvalidOperationException("Tournament registration is not currently open.");

        if (tournament.MaxParticipants.HasValue && tournament.MaxParticipants.Value > 0)
        {
            var currentCount = await _unitOfWork.Registrations.CountAsync(r => r.TournamentId == tournamentId && r.StatusCode != "CANCELLED", ct);
            if (currentCount >= tournament.MaxParticipants.Value)
            {
                throw new InvalidOperationException($"Tournament registration is full (Limit: {tournament.MaxParticipants.Value} competitors).");
            }
        }

        if (await _unitOfWork.Registrations.AnyAsync(
                r => r.TournamentId == tournamentId && r.UserId == userId && r.StatusCode == "CANCELLED", ct))
        {
            throw new InvalidOperationException("You cancelled your registration for this tournament and cannot register again.");
        }

        if (await _unitOfWork.Registrations.HasUserRegisteredAsync(tournamentId, userId, ct))
            throw new InvalidOperationException("User is already registered for this tournament.");

        // Check Face ID enrollment requirement
        var hasFaceEnrolled = await _unitOfWork.FaceEnrollments.AnyAsync(
            f => f.UserId == userId && f.Status == "ENROLLED",
            ct
        );
        if (!hasFaceEnrolled)
        {
            throw new InvalidOperationException(
                "Bạn cần phải hoàn tất đăng ký Face ID trong Hồ sơ cá nhân (Profile -> Face ID) trước khi đăng ký tham gia giải đấu."
            );
        }

        // Check for schedule overlap with other registered active tournaments
        var userRegs = await _unitOfWork.Registrations.FindAsync(
            r => r.UserId == userId && r.TournamentId != tournamentId && r.StatusCode != "CANCELLED",
            ct
        );
        var registeredTourIds = userRegs.Select(r => r.TournamentId).Distinct().ToList();
        if (registeredTourIds.Any())
        {
            var conflictingTournaments = await _unitOfWork.Tournaments.FindAsync(
                t => registeredTourIds.Contains(t.Id) &&
                     t.StatusCode != "CANCELLED" &&
                     t.StatusCode != "DISABLED" &&
                     t.StatusCode != "COMPLETED" &&
                     tournament.StartDate < t.EndDate &&
                     tournament.EndDate > t.StartDate,
                ct
            );

            var conflict = conflictingTournaments.FirstOrDefault();
            if (conflict != null)
            {
                var fmtStart = conflict.StartDate.AddHours(7).ToString("dd/MM/yyyy HH:mm");
                var fmtEnd = conflict.EndDate.AddHours(7).ToString("dd/MM/yyyy HH:mm");
                throw new InvalidOperationException(
                    $"Cannot register: You are already registered for tournament '{conflict.Name}' which takes place during the same timeframe (from {fmtStart} to {fmtEnd}). Each competitor can only participate in 1 tournament during overlapping competition times."
                );
            }
        }

        if (dto.Events == null || !dto.Events.Any())
        {
            if (dto.SelectedEventIds != null && dto.SelectedEventIds.Any())
            {
                dto.Events = dto.SelectedEventIds.Select(id => new RegisterEventDto { EventId = id }).ToList();
            }
            else
            {
                throw new InvalidOperationException("You must register for at least one event.");
            }
        }

        var validEventIds = tournament.Events.Select(e => e.Id).ToHashSet();
        foreach (var ev in dto.Events)
        {
            if (!validEventIds.Contains(ev.EventId))
                throw new InvalidOperationException($"Event ID {ev.EventId} is not part of this tournament.");
        }

        // Create Registration Entity
        var registrationId = Guid.NewGuid();
        
        var qrPayload = new RegistrationQrPayload
        {
            RegistrationId = registrationId,
            Token = Guid.NewGuid().ToString("N"), // Random token
            ExpiresAt = tournament.EndDate
        };

        var registration = new Registration
        {
            Id = registrationId,
            TournamentId = tournamentId,
            UserId = userId,
            StatusCode = "CONFIRMED",
            QrToken = JsonSerializer.Serialize(qrPayload),
            RegisteredAt = DateTime.UtcNow
        };

        foreach (var evDto in dto.Events)
        {
            var eventEntity = tournament.Events.FirstOrDefault(e => e.Id == evDto.EventId);
            if (eventEntity == null)
                throw new InvalidOperationException($"Event ID {evDto.EventId} is not part of this tournament.");

            // Validate per-event MaxCapacity if configured
            if (eventEntity.MaxCapacity.HasValue && eventEntity.MaxCapacity.Value > 0)
            {
                var eventRegCount = await _unitOfWork.OfflineRegistrationEvents.CountAsync(
                    ore => ore.EventId == evDto.EventId && ore.StatusCode != "WITHDRAWN",
                    ct
                );
                if (eventRegCount >= eventEntity.MaxCapacity.Value)
                {
                    var puzzleName = eventEntity.PuzzleType?.Name ?? "Event";
                    throw new InvalidOperationException($"Event '{puzzleName}' has reached maximum registration capacity ({eventEntity.MaxCapacity.Value} competitors).");
                }
            }

            var puzzleTypeId = eventEntity.PuzzleTypeId;

            registration.OfflineRegistrationEvents.Add(new OfflineRegistrationEvent
            {
                Id = Guid.NewGuid(),
                RegistrationId = registration.Id,
                EventId = evDto.EventId,
                StatusCode = "REGISTERED",
                SeedTimeMs = null,
                SeedSourceCode = null,
                SeedGeneratedAt = null,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _unitOfWork.Registrations.Add(registration);
        await _unitOfWork.SaveChangesAsync(ct);

        return await GetUserRegistrationByIdAsync(registrationId, userId, ct);
    }

    public async Task<RegistrationResultDto> CancelUserRegistrationAsync(
        Guid registrationId,
        Guid userId,
        CancellationToken ct = default)
    {
        var registration = await _unitOfWork.Registrations
            .GetRegistrationWithEventsAsync(registrationId, userId, ct);
        if (registration == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        if (registration.StatusCode == "CANCELLED")
            throw new InvalidOperationException("This registration has already been cancelled.");

        if (registration.StatusCode == "CHECKED_IN")
            throw new InvalidOperationException("A checked-in registration cannot be cancelled by the competitor.");

        registration.StatusCode = "CANCELLED";
        foreach (var ore in registration.OfflineRegistrationEvents)
        {
            ore.StatusCode = "WITHDRAWN";
        }

        _unitOfWork.Registrations.Update(registration);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(registration);
    }

    public async Task<List<RegistrationResultDto>> GetUserRegistrationsAsync(Guid userId, CancellationToken ct = default)
    {
        var registrations = await _unitOfWork.Registrations.GetUserRegistrationsAsync(userId, ct);
        
        var regEventIds = registrations.SelectMany(r => r.OfflineRegistrationEvents).Select(ore => ore.Id).ToList();
        Console.WriteLine($"[DEBUG] GetUserRegistrationsAsync: userId={userId}, registrations={registrations.Count}, regEventIds={regEventIds.Count} -> [{string.Join(", ", regEventIds.Take(5))}]");
        
        var groupCompetitors = regEventIds.Any()
            ? await _unitOfWork.GroupCompetitors.FindAsync(gc => regEventIds.Contains(gc.RegistrationEventId), ct)
            : new List<GroupCompetitor>();
        var gcList = groupCompetitors.ToList();
        Console.WriteLine($"[DEBUG] GroupCompetitors found: {gcList.Count} -> [{string.Join(", ", gcList.Select(gc => $"gc.RegEventId={gc.RegistrationEventId},gc.GroupId={gc.GroupId}").Take(5))}]");

        var gcGroupIds = gcList.Select(gc => gc.GroupId).Distinct().ToList();
        var groups = gcGroupIds.Any()
            ? await _unitOfWork.Groups.FindAsync(g => gcGroupIds.Contains(g.Id), ct)
            : new List<Group>();
        var groupMap = groups.ToDictionary(g => g.Id);

        var gcIds = gcList.Select(gc => gc.Id).ToList();
        var results = gcIds.Any()
            ? await _unitOfWork.Results.FindAsync(r => gcIds.Contains(r.GroupCompetitorId), ct)
            : new List<Result>();
        var resultMap = results
            .GroupBy(r => r.GroupCompetitorId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.SolveNumber).ToList());

        return registrations.Select(r => MapToDto(r, gcList, groupMap, resultMap, includeQrToken: false)).ToList();
    }

    public async Task<RegistrationResultDto> GetUserRegistrationByIdAsync(Guid registrationId, Guid userId, CancellationToken ct = default)
    {
        var reg = await _unitOfWork.Registrations.GetRegistrationWithEventsAsync(registrationId, userId, ct);
        if (reg == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        var regEventIds = reg.OfflineRegistrationEvents.Select(ore => ore.Id).ToList();
        var groupCompetitors = regEventIds.Any()
            ? await _unitOfWork.GroupCompetitors.FindAsync(gc => regEventIds.Contains(gc.RegistrationEventId), ct)
            : new List<GroupCompetitor>();
        var gcList = groupCompetitors.ToList();

        var gcGroupIds = gcList.Select(gc => gc.GroupId).Distinct().ToList();
        var groups = gcGroupIds.Any()
            ? await _unitOfWork.Groups.FindAsync(g => gcGroupIds.Contains(g.Id), ct)
            : new List<Group>();
        var groupMap = groups.ToDictionary(g => g.Id);

        var gcIds = gcList.Select(gc => gc.Id).ToList();
        var results = gcIds.Any()
            ? await _unitOfWork.Results.FindAsync(r => gcIds.Contains(r.GroupCompetitorId), ct)
            : new List<Result>();
        var resultMap = results
            .GroupBy(r => r.GroupCompetitorId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.SolveNumber).ToList());

        return MapToDto(reg, gcList, groupMap, resultMap, includeQrToken: false);
    }

    private RegistrationResultDto MapToDto(Registration r)
    {
        return MapToDto(r, new List<GroupCompetitor>(), new Dictionary<Guid, Group>(), new Dictionary<Guid, List<Result>>());
    }

    private RegistrationResultDto MapToDto(
        Registration r, 
        List<GroupCompetitor> gcList, 
        Dictionary<Guid, Group> groupMap,
        Dictionary<Guid, List<Result>> resultMap,
        bool includeQrToken = true
    )
    {
        return new RegistrationResultDto
        {
            RegistrationId = r.Id,
            TournamentId = r.TournamentId,
            TournamentName = r.Tournament?.Name ?? string.Empty,
            UserId = r.UserId,
            StatusCode = r.StatusCode,
            RegisteredAt = r.RegisteredAt,
            QrToken = includeQrToken ? r.QrToken : null,
            TournamentStartDate = r.Tournament?.StartDate,
            TournamentEndDate = r.Tournament?.EndDate,
            TournamentStatusCode = r.Tournament?.StatusCode ?? string.Empty,
            RegisteredEvents = r.OfflineRegistrationEvents.Select(ore => {
                var gcsForOre = gcList
                    .Where(c => c.RegistrationEventId == ore.Id)
                    .ToList();

                var assignments = gcsForOre
                    .Where(gc => groupMap.TryGetValue(gc.GroupId, out _))
                    .Select(gc => {
                        groupMap.TryGetValue(gc.GroupId, out var grp);
                        resultMap.TryGetValue(gc.Id, out var solvesList);
                        var solves = solvesList?.Select(res => new SolveDetailDto
                        {
                            SolveNumber = res.SolveNumber,
                            RawTimeMs = res.RawTimeMs,
                            PenaltyTypeId = res.PenaltyTypeId,
                            FinalTimeMs = res.FinalTimeMs,
                            IsDnf = res.IsDnf,
                            EvidencePhotoUrl = res.EvidencePhotoUrl
                        }).ToList() ?? new List<SolveDetailDto>();

                        return new CompetitorAssignmentDto
                        {
                            RoundNumber = grp!.RoundNumber,
                            GroupId = grp.Id,
                            GroupName = grp.GroupName ?? string.Empty,
                            StationNumber = gc.StationNumber,
                            GroupStatusCode = grp.StatusCode,
                            CompetitorStatusCode = gc.StatusCode.ToString(),
                            IsPublished = grp.StatusCode != "PENDING",
                            Solves = solves
                        };
                    })
                    .OrderBy(a => a.RoundNumber)
                    .ToList();

                return new RegisteredEventDetailDto
                {
                    RegistrationEventId = ore.Id,
                    EventId = ore.EventId,
                    PuzzleTypeName = ore.Event?.PuzzleType?.Name ?? string.Empty,
                    EventFormatCode = ore.Event?.EventFormatCode ?? string.Empty,
                    MedleyPuzzles = ore.Event?.MedleyPuzzles?.OrderBy(mp => mp.SortOrder).Select(mp => new CubeNexus.Application.DTOs.Tournament.MedleyPuzzleDetailDto
                    {
                        Id = mp.Id,
                        PuzzleTypeId = mp.PuzzleTypeId,
                        PuzzleTypeName = mp.PuzzleType?.Name ?? string.Empty,
                        PuzzleTypeCode = mp.PuzzleType?.Code ?? string.Empty,
                        SortOrder = mp.SortOrder
                    }).ToList() ?? new List<CubeNexus.Application.DTOs.Tournament.MedleyPuzzleDetailDto>(),
                    StatusCode = ore.StatusCode,
                    SeedTimeMs = ore.SeedTimeMs,
                    SeedSourceCode = ore.SeedSourceCode,
                    SeedGeneratedAt = ore.SeedGeneratedAt,
                    Assignments = assignments
                };
            }).ToList()
        };
    }

    private static int? CalculateOfficialSeedTime(List<Result> results)
    {
        if (results == null || !results.Any())
            return null;

        var validSolves = results.Where(r => r.FinalTimeMs.HasValue).ToList();
        if (!validSolves.Any())
            return null;

        if (validSolves.Count == 5)
            return CalculateAo5(validSolves);

        var nonDnfSolves = validSolves.Where(r => !r.IsDnf).ToList();
        if (!nonDnfSolves.Any())
            return null;

        return (int)Math.Round(nonDnfSolves.Average(r => r.FinalTimeMs!.Value));
    }

    private static int? CalculateAo5(List<Result> solves)
    {
        int dnfCount = solves.Count(r => r.IsDnf);
        if (dnfCount >= 2)
            return null;

        var times = solves.Select(r => r.IsDnf ? int.MaxValue : r.FinalTimeMs!.Value).OrderBy(t => t).ToList();
        var middle3 = times.Skip(1).Take(3).ToList();
        return (int)Math.Round(middle3.Average());
    }

    public async Task<RegisteredEventDetailDto> OverrideSeedAsync(Guid registrationEventId, OverrideSeedDto dto, CancellationToken ct = default)
    {
        var ore = await _unitOfWork.OfflineRegistrationEvents.FirstOrDefaultAsync(
            x => x.Id == registrationEventId,
            ct
        );
        if (ore == null)
            throw new KeyNotFoundException($"Offline registration event with ID {registrationEventId} not found.");

        if (dto.SeedTimeMs.HasValue)
        {
            if (dto.SeedTimeMs.Value <= 0)
                throw new InvalidOperationException("Seed time must be greater than 0.");

            ore.SeedTimeMs = dto.SeedTimeMs.Value;
            ore.SeedSourceCode = "MANUAL_OVERRIDE";
            ore.SeedGeneratedAt = DateTime.UtcNow;
        }
        else
        {
            ore.SeedTimeMs = null;
            ore.SeedSourceCode = null;
            ore.SeedGeneratedAt = null;
        }

        ore.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.OfflineRegistrationEvents.Update(ore);
        await _unitOfWork.SaveChangesAsync(ct);

        var ev = await _unitOfWork.Events.FirstOrDefaultAsync(e => e.Id == ore.EventId, ct);
        PuzzleType? pt = null;
        if (ev != null)
        {
            pt = await _unitOfWork.PuzzleTypes.GetByIdAsync(ev.PuzzleTypeId, ct);
        }

        return new RegisteredEventDetailDto
        {
            RegistrationEventId = ore.Id,
            EventId = ore.EventId,
            PuzzleTypeName = pt?.Name ?? string.Empty,
            EventFormatCode = ev?.EventFormatCode ?? string.Empty,
            StatusCode = ore.StatusCode,
            SeedTimeMs = ore.SeedTimeMs,
            SeedSourceCode = ore.SeedSourceCode,
            SeedGeneratedAt = ore.SeedGeneratedAt
        };
    }

    public async Task<List<EventCompetitorSeedDto>> GetEventCompetitorsSortedAsync(Guid eventId, CancellationToken ct = default)
    {
        var list = await _unitOfWork.OfflineRegistrationEvents.FindAsync(
            ore => ore.EventId == eventId && ore.StatusCode == "REGISTERED",
            ct
        );

        var result = new List<EventCompetitorSeedDto>();

        foreach (var ore in list)
        {
            var reg = await _unitOfWork.Registrations.FirstOrDefaultAsync(r => r.Id == ore.RegistrationId, ct);
            string displayName = string.Empty;
            Guid userId = Guid.Empty;
            if (reg != null)
            {
                userId = reg.UserId;
                var user = await _unitOfWork.Users.FirstOrDefaultAsync(u => u.Id == reg.UserId, ct);
                if (user != null)
                {
                    displayName = user.DisplayName;
                }
            }

            result.Add(new EventCompetitorSeedDto
            {
                RegistrationEventId = ore.Id,
                UserId = userId,
                DisplayName = displayName,
                SeedTimeMs = ore.SeedTimeMs,
                SeedSourceCode = ore.SeedSourceCode,
                SeedGeneratedAt = ore.SeedGeneratedAt
            });
        }

        return result
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    public async Task<List<TournamentRegistrationDetailDto>> GetTournamentRegistrationsAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var list = await _unitOfWork.Registrations.GetTournamentRegistrationsAsync(tournamentId, ct);
        return list.Select(r => new TournamentRegistrationDetailDto
        {
            RegistrationId = r.Id,
            TournamentId = r.TournamentId,
            TournamentName = r.Tournament?.Name ?? string.Empty,
            UserId = r.UserId,
            CompetitorName = r.User?.DisplayName ?? string.Empty,
            Email = r.User?.Email ?? string.Empty,
            CompetitorUserCode = r.User?.UserCode ?? string.Empty,
            CompetitorAvatarUrl = r.User?.AvatarUrl,
            StatusCode = r.StatusCode,
            RegisteredAt = r.RegisteredAt,
            CheckedInAt = r.CheckedInAt,
            QrToken = r.QrToken,
            RegisteredEvents = r.OfflineRegistrationEvents.Select(ore => new RegisteredEventDetailDto
            {
                RegistrationEventId = ore.Id,
                EventId = ore.EventId,
                PuzzleTypeName = ore.Event?.PuzzleType?.Name ?? string.Empty,
                EventFormatCode = ore.Event?.EventFormatCode ?? string.Empty,
                MedleyPuzzles = ore.Event?.MedleyPuzzles?.OrderBy(mp => mp.SortOrder).Select(mp => new CubeNexus.Application.DTOs.Tournament.MedleyPuzzleDetailDto
                {
                    Id = mp.Id,
                    PuzzleTypeId = mp.PuzzleTypeId,
                    PuzzleTypeName = mp.PuzzleType?.Name ?? string.Empty,
                    PuzzleTypeCode = mp.PuzzleType?.Code ?? string.Empty,
                    SortOrder = mp.SortOrder
                }).ToList() ?? new List<CubeNexus.Application.DTOs.Tournament.MedleyPuzzleDetailDto>(),
                StatusCode = ore.StatusCode,
                SeedTimeMs = ore.SeedTimeMs,
                SeedSourceCode = ore.SeedSourceCode,
                SeedGeneratedAt = ore.SeedGeneratedAt,
                Assignments = ore.GroupCompetitors.Select(gc => new CompetitorAssignmentDto
                {
                    RoundNumber = gc.Group?.RoundNumber ?? 0,
                    GroupId = gc.GroupId,
                    GroupName = gc.Group?.GroupName ?? string.Empty,
                    StationNumber = gc.StationNumber,
                    GroupStatusCode = gc.Group?.StatusCode ?? string.Empty,
                    CompetitorStatusCode = gc.StatusCode.ToString(),
                    IsPublished = gc.Group?.StatusCode != "PENDING"
                }).OrderBy(a => a.RoundNumber).ToList()
            }).ToList()
        }).ToList();
    }

    public async Task<RegistrationResultDto> UpdateRegistrationStatusAsync(Guid registrationId, string status, CancellationToken ct = default)
    {
        var temp = await _unitOfWork.Registrations.GetByIdAsync(registrationId, ct);
        if (temp == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        var registration = await _unitOfWork.Registrations.GetRegistrationWithEventsAsync(registrationId, temp.UserId, ct);
        if (registration == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        var validStatuses = new[] { "PENDING", "CONFIRMED", "CANCELLED", "CHECKED_IN" };
        var normalizedStatus = status.ToUpperInvariant();
        if (!validStatuses.Contains(normalizedStatus))
            throw new InvalidOperationException($"Invalid registration status: {status}");

        if (registration.StatusCode == "CANCELLED")
            throw new InvalidOperationException("A cancelled registration is final and cannot be reactivated or changed.");

        registration.StatusCode = normalizedStatus;
        if (normalizedStatus == "CANCELLED")
        {
            foreach (var ore in registration.OfflineRegistrationEvents)
            {
                ore.StatusCode = "WITHDRAWN";
            }
        }
        else if (normalizedStatus == "CONFIRMED" || normalizedStatus == "CHECKED_IN")
        {
            foreach (var ore in registration.OfflineRegistrationEvents)
            {
                if (ore.StatusCode == "WITHDRAWN")
                {
                    ore.StatusCode = "REGISTERED";
                }
            }
        }

        _unitOfWork.Registrations.Update(registration);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(registration);
    }

    public async Task<RegistrationResultDto> ManuallyCheckInAsync(Guid registrationId, CancellationToken ct = default)
    {
        var temp = await _unitOfWork.Registrations.GetByIdAsync(registrationId, ct);
        if (temp == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        var registration = await _unitOfWork.Registrations.GetRegistrationWithEventsAsync(registrationId, temp.UserId, ct);
        if (registration == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        if (string.Equals(registration.StatusCode, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A cancelled registration cannot be checked in.");

        if (!string.Equals(registration.StatusCode, "CONFIRMED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(registration.StatusCode, "CHECKED_IN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a confirmed registration can be checked in.");

        var tournamentStatus = registration.Tournament?.StatusCode;
        if (!string.Equals(tournamentStatus, "CHECKING_IN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tournamentStatus, "ONGOING", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Check-in is available only while the tournament is CHECKING_IN or ONGOING.");

        if (string.Equals(registration.StatusCode, "CHECKED_IN", StringComparison.OrdinalIgnoreCase))
            return MapToDto(registration);

        registration.StatusCode = "CHECKED_IN";
        registration.CheckedInAt = DateTime.UtcNow;

        foreach (var ore in registration.OfflineRegistrationEvents)
        {
            if (ore.StatusCode == "WITHDRAWN")
            {
                ore.StatusCode = "REGISTERED";
            }
        }

        _unitOfWork.Registrations.Update(registration);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(registration);
    }
}
