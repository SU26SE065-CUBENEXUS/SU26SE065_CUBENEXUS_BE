using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class CheckInRegistrationByQrUseCase : ICheckInRegistrationByQrUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    public CheckInRegistrationByQrUseCase(
        IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CheckInResponseDto> ExecuteAsync(CheckInRequestDto dto, Guid? judgeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(dto.QrToken))
        {
            throw new CustomException("QR_INVALID", "Invalid QR code.", 400);
        }

        RegistrationQrPayload? payload = null;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<RegistrationQrPayload>(dto.QrToken);
        }
        catch
        {
            // Fallback for raw text token
        }

        Domain.Entities.Registration? registration = null;
        if (payload != null && payload.RegistrationId != Guid.Empty && !string.IsNullOrEmpty(payload.Token))
        {
            if (payload.ExpiresAt < DateTime.UtcNow)
            {
                throw new CustomException("QR_EXPIRED", "The competitor's QR ticket has expired.", 400);
            }

            registration = await _unitOfWork.Registrations.GetRegistrationWithDetailsAsync(payload.RegistrationId);

            if (registration != null)
            {
                try
                {
                    var dbPayload = System.Text.Json.JsonSerializer.Deserialize<RegistrationQrPayload>(registration.QrToken);
                    if (dbPayload == null || dbPayload.Token != payload.Token)
                    {
                        registration = null;
                    }
                }
                catch
                {
                    registration = null;
                }
            }
        }
        else
        {
            registration = await _unitOfWork.Registrations.GetByQrTokenAsync(dto.QrToken);
        }

        if (registration == null)
        {
            throw new CustomException("QR_INVALID", "Invalid QR code credentials or token mismatch.", 400);
        }

        if (judgeUserId.HasValue)
        {
            var caller = await _unitOfWork.Users.GetByIdAsync(judgeUserId.Value);
            if (caller != null && string.Equals(caller.UserRole, "JUDGE", StringComparison.OrdinalIgnoreCase))
            {
                var isAssigned = await _unitOfWork.TournamentJudges.AnyAsync(
                    tj => tj.TournamentId == registration.TournamentId && tj.UserId == judgeUserId.Value);
                if (!isAssigned)
                {
                    throw new CustomException("JUDGE_NOT_ASSIGNED_TO_TOURNAMENT", "The judge is not authorized to check in competitors for this tournament.", 403);
                }
            }
        }

        if (registration.Tournament.StatusCode != "CHECKING_IN" && registration.Tournament.StatusCode != "ONGOING")
        {
            throw new CustomException("INVALID_TOURNAMENT_STATE", "Check-in is only allowed when tournament status is CHECKING_IN or ONGOING.", 400);
        }

        if (registration.StatusCode == "CANCELLED")
        {
            throw new CustomException("REGISTRATION_CANCELLED", "The registration is cancelled.", 400);
        }

        var regEventIds = registration.OfflineRegistrationEvents.Select(ore => ore.Id).ToList();
        var groupCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => regEventIds.Contains(gc.RegistrationEventId));
        var gcList = groupCompetitors.ToList();

        var assignments = new List<CheckInAssignmentDto>();
        if (gcList.Any())
        {
            var groupIds = gcList.Select(gc => gc.GroupId).Distinct().ToList();
            var groups = await _unitOfWork.Groups.FindAsync(g => groupIds.Contains(g.Id));
            var groupMap = groups.ToDictionary(g => g.Id);

            foreach (var gc in gcList)
            {
                if (groupMap.TryGetValue(gc.GroupId, out var group))
                {
                    var ore = registration.OfflineRegistrationEvents.First(e => e.Id == gc.RegistrationEventId);
                    assignments.Add(new CheckInAssignmentDto
                    {
                        EventId = ore.EventId,
                        EventName = ore.Event.PuzzleType.Name,
                        RoundNumber = group.RoundNumber,
                        GroupId = group.Id,
                        GroupName = group.GroupName ?? string.Empty,
                        GroupStatusCode = group.StatusCode,
                        StationNumber = gc.StationNumber
                    });
                }
            }
        }

        var response = new CheckInResponseDto
        {
            RegistrationId = registration.Id,
            PlayerName = registration.User.DisplayName,
            TournamentName = registration.Tournament.Name,
            Events = registration.OfflineRegistrationEvents.Select(e => e.Event.PuzzleType.Name).ToList(),
            Assignments = assignments
        };

        if (registration.CheckedInAt.HasValue)
        {
            response.Success = true;
            response.AlreadyCheckedIn = true;
            response.Message = "Player is already checked in.";
            response.CheckedInAt = registration.CheckedInAt.Value;
            return response;
        }

        registration.StatusCode = "CHECKED_IN";
        registration.CheckedInAt = DateTime.UtcNow;

        _unitOfWork.Registrations.Update(registration);
        await _unitOfWork.SaveChangesAsync();

        response.Success = true;
        response.AlreadyCheckedIn = false;
        response.Message = "Check-in successful.";
        response.CheckedInAt = registration.CheckedInAt.Value;

        return response;
    }
}
