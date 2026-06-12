using System.Text.Json;
using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

public class TournamentRegistrationService : ITournamentRegistrationService
{
    private readonly IUnitOfWork _unitOfWork;

    public TournamentRegistrationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

        if (tournament.StatusCode != "PUBLISHED" && tournament.StatusCode != "REGISTRATION_OPEN" && tournament.StatusCode != "ONGOING")
            throw new InvalidOperationException("This tournament is not open for registration.");

        if (await _unitOfWork.Registrations.HasUserRegisteredAsync(tournamentId, userId, ct))
            throw new InvalidOperationException("User is already registered for this tournament.");

        if (dto.Events == null || !dto.Events.Any())
            throw new InvalidOperationException("You must register for at least one event.");

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
            ExpiresAt = tournament.EndDate.AddDays(7) // Example expiration
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

            var puzzleTypeId = eventEntity.PuzzleTypeId;

            // 1. Calculate seed from OFFICIAL_RESULT
            var officialResults = await _unitOfWork.Registrations.GetLatestOfficialResultsAsync(userId, puzzleTypeId, ct);
            var seedTime = CalculateOfficialSeedTime(officialResults);
            string? seedSource = null;
            DateTime? seedGeneratedAt = null;

            if (seedTime.HasValue)
            {
                seedSource = "OFFICIAL_RESULT";
                seedGeneratedAt = DateTime.UtcNow;
            }
            else
            {
                // 2. Fallback to PRACTICE_AO5
                var snapshots = await _unitOfWork.PracticeAo5Snapshots.FindAsync(
                    s => s.UserId == userId && s.PuzzleTypeId == puzzleTypeId,
                    ct
                );
                var latestPracticeSnapshot = snapshots.OrderByDescending(s => s.CalculatedAt).FirstOrDefault();
                if (latestPracticeSnapshot != null)
                {
                    seedTime = latestPracticeSnapshot.Ao5TimeMs;
                    seedSource = "PRACTICE_AO5";
                    seedGeneratedAt = DateTime.UtcNow;
                }
                else
                {
                    // 3. Fallback to DEFAULT
                    seedTime = null;
                    seedSource = null;
                    seedGeneratedAt = null;
                }
            }

            registration.OfflineRegistrationEvents.Add(new OfflineRegistrationEvent
            {
                Id = Guid.NewGuid(),
                RegistrationId = registration.Id,
                EventId = evDto.EventId,
                StatusCode = "REGISTERED",
                SeedTimeMs = seedTime,
                SeedSourceCode = seedSource,
                SeedGeneratedAt = seedGeneratedAt,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _unitOfWork.Registrations.Add(registration);
        await _unitOfWork.SaveChangesAsync(ct);

        return await GetUserRegistrationByIdAsync(registrationId, userId, ct);
    }

    public async Task<List<RegistrationResultDto>> GetUserRegistrationsAsync(Guid userId, CancellationToken ct = default)
    {
        var registrations = await _unitOfWork.Registrations.GetUserRegistrationsAsync(userId, ct);
        return registrations.Select(MapToDto).ToList();
    }

    public async Task<RegistrationResultDto> GetUserRegistrationByIdAsync(Guid registrationId, Guid userId, CancellationToken ct = default)
    {
        var reg = await _unitOfWork.Registrations.GetRegistrationWithEventsAsync(registrationId, userId, ct);
        if (reg == null)
            throw new KeyNotFoundException($"Registration with ID {registrationId} not found.");

        return MapToDto(reg);
    }

    private RegistrationResultDto MapToDto(Registration r)
    {
        return new RegistrationResultDto
        {
            RegistrationId = r.Id,
            TournamentId = r.TournamentId,
            TournamentName = r.Tournament?.Name ?? string.Empty,
            UserId = r.UserId,
            StatusCode = r.StatusCode,
            RegisteredAt = r.RegisteredAt,
            QrToken = r.QrToken,
            RegisteredEvents = r.OfflineRegistrationEvents.Select(ore => new RegisteredEventDetailDto
            {
                RegistrationEventId = ore.Id,
                EventId = ore.EventId,
                PuzzleTypeName = ore.Event?.PuzzleType?.Name ?? string.Empty,
                EventFormatCode = ore.Event?.EventFormatCode ?? string.Empty,
                StatusCode = ore.StatusCode,
                SeedTimeMs = ore.SeedTimeMs,
                SeedSourceCode = ore.SeedSourceCode,
                SeedGeneratedAt = ore.SeedGeneratedAt
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
            .OrderBy(x => x.SeedTimeMs.HasValue ? 0 : 1)
            .ThenBy(x => x.SeedTimeMs)
            .ToList();
    }
}
