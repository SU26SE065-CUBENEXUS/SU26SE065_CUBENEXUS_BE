using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.PublicLive;
using CubeNexus.Application.DTOs.Tournament;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/public/live")]
[AllowAnonymous]
public class PublicLiveController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public PublicLiveController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// GET /api/public/live/tournaments
    /// Lấy danh sách các giải đấu offline có thể xem public.
    /// Sắp xếp: các giải đang live lên đầu, rồi đến các giải sắp diễn ra (StartDate ASC), rồi đến các giải đã hoàn thành.
    /// </summary>
    [HttpGet("tournaments")]
    public async Task<IActionResult> GetPublicLiveTournaments(CancellationToken ct)
    {
        try
        {
            var publicStatuses = new[] { "REGISTRATION_OPEN", "PUBLISHED", "ONGOING", "COMPLETED" };
            
            // 1. Fetch public tournaments
            var tournaments = await _unitOfWork.Tournaments.FindAsync(t => publicStatuses.Contains(t.StatusCode), ct);
            if (!tournaments.Any())
            {
                return Ok(new List<PublicLiveTournamentDto>());
            }

            var tournamentIds = tournaments.Select(t => t.Id).ToList();

            // 2. Fetch events of these tournaments
            var events = await _unitOfWork.Events.FindAsync(e => tournamentIds.Contains(e.TournamentId), ct);
            var eventIds = events.Select(e => e.Id).ToList();

            // 3. Fetch ongoing groups to compute isLive
            var ongoingGroups = await _unitOfWork.Groups.FindAsync(g => g.StatusCode == "ONGOING", ct);
            var ongoingEventIds = ongoingGroups.Select(g => g.EventId).ToHashSet();

            // 4. Map to DTOs
            var dtos = tournaments.Select(t =>
            {
                var tourEvents = events.Where(e => e.TournamentId == t.Id).ToList();
                var hasActiveGroups = tourEvents.Any(e => ongoingEventIds.Contains(e.Id));
                var isLive = t.StatusCode == "ONGOING" && hasActiveGroups;

                return new PublicLiveTournamentDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Description = t.Description,
                    Location = t.Location,
                    StartTime = t.StartDate,
                    EndTime = t.EndDate,
                    Status = t.StatusCode,
                    EventsCount = tourEvents.Count,
                    IsLive = isLive
                };
            }).ToList();

            // 5. Prioritize Sorting:
            // - Ongoing & IsLive = true first
            // - Nearest upcoming tournaments (StartDate ASC) next
            // - Completed tournaments last
            var sortedDtos = dtos
                .OrderByDescending(d => d.IsLive)
                .ThenBy(d => d.Status == "COMPLETED")
                .ThenBy(d => d.StartTime)
                .ToList();

            return Ok(sortedDtos);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching public tournaments.", detail = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/public/live/tournaments/{tournamentId}
    /// Lấy chi tiết Live Board của một giải đấu, bao gồm danh sách các event, thông tin round/group active và kết quả.
    /// </summary>
    [HttpGet("tournaments/{tournamentId:guid}")]
    public async Task<IActionResult> GetPublicLiveTournamentDetail(Guid tournamentId, CancellationToken ct)
    {
        try
        {
            // 1. Fetch tournament details with events and puzzles
            var tournament = await _unitOfWork.Tournaments.GetTournamentWithEventsAndPuzzlesAsync(tournamentId, ct);
            if (tournament == null)
            {
                return NotFound(new { message = $"Tournament with ID {tournamentId} not found." });
            }

            var publicStatuses = new[] { "REGISTRATION_OPEN", "PUBLISHED", "ONGOING", "COMPLETED" };
            if (!publicStatuses.Contains(tournament.StatusCode))
            {
                return NotFound(new { message = "Tournament is not public." });
            }

            // 2. Fetch groups for all events in this tournament to compute active/latest round details
            var eventIds = tournament.Events.Select(e => e.Id).ToList();
            var groups = await _unitOfWork.Groups.FindAsync(g => eventIds.Contains(g.EventId), ct);
            var ongoingGroups = groups.Where(g => g.StatusCode == "ONGOING").ToList();
            var ongoingEventIds = ongoingGroups.Select(g => g.EventId).ToHashSet();

            var eventDtos = new List<PublicLiveEventDto>();
            Guid? activeEventId = null;
            int? activeRoundNumber = null;

            foreach (var ev in tournament.Events)
            {
                var evGroups = groups.Where(g => g.EventId == ev.Id).ToList();
                int? currentRoundNumber = null;
                string? roundStatus = null;

                if (evGroups.Any())
                {
                    // Find if there is an ongoing round
                    var ongoingGroup = evGroups.FirstOrDefault(g => g.StatusCode == "ONGOING");
                    if (ongoingGroup != null)
                    {
                        currentRoundNumber = ongoingGroup.RoundNumber;
                        roundStatus = "ONGOING";
                        
                        // Set active tournament-level event/round (first ongoing one found)
                        if (activeEventId == null)
                        {
                            activeEventId = ev.Id;
                            activeRoundNumber = ongoingGroup.RoundNumber;
                        }
                    }
                    else
                    {
                        // Otherwise, get the highest round number
                        var maxRound = evGroups.Max(g => g.RoundNumber);
                        var roundGroups = evGroups.Where(g => g.RoundNumber == maxRound).ToList();
                        currentRoundNumber = maxRound;
                        
                        if (roundGroups.All(g => g.StatusCode == "COMPLETED"))
                        {
                            roundStatus = "COMPLETED";
                        }
                        else if (roundGroups.Any(g => g.StatusCode == "LOCKED"))
                        {
                            roundStatus = "LOCKED";
                        }
                        else
                        {
                            roundStatus = "PENDING";
                        }
                    }
                }

                eventDtos.Add(new PublicLiveEventDto
                {
                    Id = ev.Id,
                    PuzzleTypeId = ev.PuzzleTypeId,
                    PuzzleTypeName = ev.PuzzleType?.Name ?? string.Empty,
                    PuzzleTypeCode = ev.PuzzleType?.Code ?? string.Empty,
                    EventFormatCode = ev.EventFormatCode,
                    SolveCount = ev.SolveCount,
                    SortOrder = ev.SortOrder,
                    TimeLimitMs = ev.TimeLimitMs,
                    CutoffTimeMs = ev.CutoffTimeMs,
                    CurrentRoundNumber = currentRoundNumber,
                    RoundStatus = roundStatus,
                    MedleyPuzzles = ev.MedleyPuzzles.OrderBy(mp => mp.SortOrder).Select(mp => new MedleyPuzzleDetailDto
                    {
                        Id = mp.Id,
                        PuzzleTypeId = mp.PuzzleTypeId,
                        PuzzleTypeName = mp.PuzzleType?.Name ?? string.Empty,
                        PuzzleTypeCode = mp.PuzzleType?.Code ?? string.Empty,
                        SortOrder = mp.SortOrder
                    }).ToList()
                });
            }

            // If no ongoing event round was found, default active event/round to the first event's latest round (if any)
            if (activeEventId == null && eventDtos.Any(e => e.CurrentRoundNumber != null))
            {
                var firstEventWithRound = eventDtos.FirstOrDefault(e => e.CurrentRoundNumber != null);
                if (firstEventWithRound != null)
                {
                    activeEventId = firstEventWithRound.Id;
                    activeRoundNumber = firstEventWithRound.CurrentRoundNumber;
                }
            }

            var hasActiveGroups = tournament.Events.Any(e => ongoingEventIds.Contains(e.Id));
            var isLive = tournament.StatusCode == "ONGOING" && hasActiveGroups;

            var dto = new PublicLiveTournamentDetailDto
            {
                Id = tournament.Id,
                Name = tournament.Name,
                Description = tournament.Description,
                Location = tournament.Location,
                StartTime = tournament.StartDate,
                EndTime = tournament.EndDate,
                Status = tournament.StatusCode,
                IsLive = isLive,
                Events = eventDtos.OrderBy(e => e.SortOrder).ToList(),
                ActiveEventId = activeEventId,
                ActiveRoundNumber = activeRoundNumber
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching tournament details.", detail = ex.Message });
        }
    }
}
