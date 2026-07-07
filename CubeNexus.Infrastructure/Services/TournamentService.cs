using CubeNexus.Application.DTOs.Tournament;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

public class TournamentService : ITournamentService
{
    private readonly IUnitOfWork _unitOfWork;

    public TournamentService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TournamentDetailDto> CreateTournamentAsync(CreateTournamentDto dto, Guid managerId, CancellationToken ct = default)
    {
        // 1. Validate Dates
        if (dto.StartDate >= dto.EndDate)
        {
            throw new InvalidOperationException("StartDate must be earlier than EndDate.");
        }
        
        if (dto.RegistrationOpenAt >= dto.RegistrationCloseAt)
        {
            throw new InvalidOperationException("RegistrationOpenAt must be earlier than RegistrationCloseAt.");
        }
        
        if (dto.RegistrationCloseAt >= dto.StartDate)
        {
            throw new InvalidOperationException("RegistrationCloseAt must be earlier than tournament StartDate.");
        }

        // 2. Validate Events & Puzzles
        var allActivePuzzles = await _unitOfWork.PuzzleTypes.GetAllActiveAsync();
        var activePuzzleIds = allActivePuzzles.Select(p => p.Id).ToHashSet();

        foreach (var ev in dto.Events)
        {
            if (!activePuzzleIds.Contains(ev.PuzzleTypeId))
            {
                throw new InvalidOperationException($"PuzzleTypeId {ev.PuzzleTypeId} is invalid or inactive.");
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
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            RegistrationOpenAt = dto.RegistrationOpenAt,
            RegistrationCloseAt = dto.RegistrationCloseAt,
            StatusCode = "DRAFT", // Always draft
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
                SortOrder = evDto.SortOrder,
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
        var tournaments = await _unitOfWork.Tournaments.GetPublicTournamentsAsync(ct);
        return tournaments.Select(MapToDetailDto).ToList();
    }

    public async Task<TournamentDetailDto> GetTournamentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await _unitOfWork.Tournaments.GetTournamentWithEventsAndPuzzlesAsync(id, ct);
        if (tournament == null)
        {
            throw new KeyNotFoundException($"Tournament with ID {id} not found.");
        }
        return MapToDetailDto(tournament);
    }

    private TournamentDetailDto MapToDetailDto(Tournament t)
    {
        return new TournamentDetailDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Location = t.Location,
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
                TimeLimitMs = e.TimeLimitMs,
                CutoffTimeMs = e.CutoffTimeMs,
                SolveCount = e.SolveCount,
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
}
