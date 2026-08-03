using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Helpers;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/live-board")]
public class LiveBoardController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public LiveBoardController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Lấy toàn bộ trạng thái của Live Board cho vòng đấu (Public).
    /// </summary>
    [HttpGet("events/{eventId:guid}/rounds/{roundNumber:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLiveBoardState(Guid eventId, int roundNumber)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            return NotFound(new { message = $"Event with ID {eventId} not found." });

        var puzzle = await _unitOfWork.PuzzleTypes.GetByIdAsync(ev.PuzzleTypeId);
        var eventName = puzzle?.Name ?? "Unknown Event";

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == roundNumber);
        
        var groupDtos = groups.Select(g => new LiveBoardGroupDto
        {
            GroupId = g.Id,
            GroupName = g.GroupName ?? string.Empty,
            StatusCode = g.StatusCode
        }).ToList();

        var roundStatus = "PENDING";
        if (groups.Any(g => g.StatusCode == "ONGOING"))
        {
            roundStatus = "ONGOING";
        }
        else if (groups.Any() && groups.All(g => g.StatusCode == "LOCKED"))
        {
            roundStatus = "LOCKED";
        }
        else if (groups.Any() && groups.All(g => g.StatusCode == "COMPLETED"))
        {
            roundStatus = "COMPLETED";
        }

        var groupIds = groups.Select(g => g.Id).ToList();
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId));
        var competitorIds = competitors.Select(c => c.Id).ToList();

        var results = await _unitOfWork.Results.FindAsync(r => competitorIds.Contains(r.GroupCompetitorId));
        var penaltyTypes = await _unitOfWork.PenaltyTypes.GetAllAsync();
        var penaltyTypeMap = penaltyTypes.ToDictionary(pt => pt.Id);

        var regEventIds = competitors.Select(gc => gc.RegistrationEventId).ToList();
        var offlineRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => regEventIds.Contains(ore.Id));
        var regIds = offlineRegEvents.Select(ore => ore.RegistrationId).ToList();
        var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id));
        var userIds = registrations.Select(r => r.UserId).ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));

        var userMap = users.ToDictionary(u => u.Id);
        var regMap = registrations.ToDictionary(r => r.Id);
        var offlineRegEventMap = offlineRegEvents.ToDictionary(ore => ore.Id);

        var competitorDtos = LiveBoardCalculator.CalculateCompetitors(
            ev.SolveCount,
            competitors,
            results,
            userMap,
            regMap,
            offlineRegEventMap,
            penaltyTypeMap,
            ev.CutoffTimeMs
        );

        var progress = LiveBoardCalculator.CalculateProgress(ev.SolveCount, competitorDtos);

        return Ok(new LiveBoardStateDto
        {
            EventId = eventId,
            EventName = eventName,
            RoundNumber = roundNumber,
            RoundStatus = roundStatus,
            SolveCount = ev.SolveCount,
            Progress = progress,
            Groups = groupDtos,
            Competitors = competitorDtos
        });
    }

    /// <summary>
    /// Lấy bảng xếp hạng rút gọn của vòng đấu (Public).
    /// </summary>
    [HttpGet("events/{eventId:guid}/rounds/{roundNumber:int}/rankings")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLiveBoardRankings(Guid eventId, int roundNumber)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId);
        if (ev == null)
            return NotFound(new { message = $"Event with ID {eventId} not found." });

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == roundNumber);
        var groupIds = groups.Select(g => g.Id).ToList();
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId));
        var competitorIds = competitors.Select(c => c.Id).ToList();

        var results = await _unitOfWork.Results.FindAsync(r => competitorIds.Contains(r.GroupCompetitorId));
        var penaltyTypes = await _unitOfWork.PenaltyTypes.GetAllAsync();
        var penaltyTypeMap = penaltyTypes.ToDictionary(pt => pt.Id);

        var regEventIds = competitors.Select(gc => gc.RegistrationEventId).ToList();
        var offlineRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => regEventIds.Contains(ore.Id));
        var regIds = offlineRegEvents.Select(ore => ore.RegistrationId).ToList();
        var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id));
        var userIds = registrations.Select(r => r.UserId).ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));

        var userMap = users.ToDictionary(u => u.Id);
        var regMap = registrations.ToDictionary(r => r.Id);
        var offlineRegEventMap = offlineRegEvents.ToDictionary(ore => ore.Id);

        var competitorDtos = LiveBoardCalculator.CalculateCompetitors(
            ev.SolveCount,
            competitors,
            results,
            userMap,
            regMap,
            offlineRegEventMap,
            penaltyTypeMap,
            ev.CutoffTimeMs
        );

        var rankings = competitorDtos.Select(c => new LiveBoardRankingDto
        {
            Rank = c.Rank,
            GroupCompetitorId = c.GroupCompetitorId,
            CompetitorName = c.CompetitorName,
            BestTimeMs = c.BestTimeMs,
            AverageTimeMs = c.AverageTimeMs,
            CompletedSolves = c.CompletedSolves,
            CompetitorStatus = c.CompetitorStatus
        }).ToList();

        return Ok(rankings);
    }
}
