using System;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class VerifyJudgeStationUseCase : IVerifyJudgeStationUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public VerifyJudgeStationUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Judge Station Verify is optional UX verification and does not replace submit result validation.
    public async Task<VerifyJudgeStationResponseDto> ExecuteAsync(VerifyJudgeStationDto dto)
    {
        var registration = await _unitOfWork.Registrations.GetByQrTokenAsync(dto.QrToken);
        if (registration == null)
            throw new CustomException("INVALID_QR_TOKEN", "Invalid QR code.", 400);

        if (registration.StatusCode == "CANCELLED")
            throw new CustomException("REGISTRATION_CANCELLED", "This registration is cancelled.", 400);

        if (registration.CheckedInAt == null || registration.StatusCode != "CHECKED_IN")
            throw new CustomException("NOT_CHECKED_IN", "Competitor has not checked in at the reception.", 400);

        var group = await _unitOfWork.Groups.GetByIdAsync(dto.GroupId);
        if (group == null || group.EventId != dto.EventId || group.RoundNumber != dto.RoundNumber)
            throw new CustomException("INVALID_GROUP", "Group does not match the specified event or round.", 400);

        if (group.StatusCode != "ONGOING")
            throw new CustomException("GROUP_NOT_ONGOING", "This group is not currently ongoing.", 400);

        var ev = await _unitOfWork.Events.GetByIdAsync(dto.EventId);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        var puzzle = await _unitOfWork.PuzzleTypes.GetByIdAsync(ev.PuzzleTypeId);
        var eventName = puzzle?.Name ?? "Unknown Event";

        var groupCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => gc.GroupId == group.Id && gc.StationNumber == dto.StationNumber);
        var comp = groupCompetitors.FirstOrDefault();
        
        if (comp == null)
            throw new CustomException("COMPETITOR_NOT_FOUND", "No competitor assigned to this station for this group.", 404);

        var offlineRegEvent = await _unitOfWork.OfflineRegistrationEvents.GetByIdAsync(comp.RegistrationEventId);
        if (offlineRegEvent == null || offlineRegEvent.RegistrationId != registration.Id)
            throw new CustomException("MISMATCHED_COMPETITOR", "The scanned QR code does not belong to the competitor assigned to this station.", 400);

        if (comp.StatusCode == GroupCompetitorStatus.NO_SHOW)
            throw new CustomException("COMPETITOR_NO_SHOW", "This competitor was marked as NO_SHOW and cannot compete.", 400);

        var results = await _unitOfWork.Results.FindAsync(r => r.GroupCompetitorId == comp.Id);
        var resultsList = results.ToList();

        if (resultsList.Any() && resultsList.All(r => r.IsLocked))
            throw new CustomException("RESULTS_LOCKED", "Results for this competitor are already locked.", 400);

        if (comp.StatusCode == GroupCompetitorStatus.COMPLETED || resultsList.Count >= ev.SolveCount)
            throw new CustomException("COMPETITOR_COMPLETED", "This competitor has already completed all solves for this round.", 400);

        int nextSolveNumber = resultsList.Count + 1;

        return new VerifyJudgeStationResponseDto
        {
            Success = true,
            Message = "Verification successful. Competitor can proceed.",
            GroupCompetitorId = comp.Id,
            EventId = ev.Id,
            EventName = eventName,
            RoundNumber = group.RoundNumber,
            GroupId = group.Id,
            GroupName = group.GroupName ?? string.Empty,
            StationNumber = comp.StationNumber,
            NextSolveNumber = nextSolveNumber,
            SolveCount = ev.SolveCount,
            CanSubmit = true
        };
    }
}
