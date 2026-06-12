using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class CheckInRegistrationByQrUseCase : ICheckInRegistrationByQrUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CheckInRegistrationByQrUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CheckInResponseDto> ExecuteAsync(CheckInRequestDto dto)
    {
        var registration = await _unitOfWork.Registrations.GetByQrTokenAsync(dto.QrToken);
        if (registration == null)
        {
            throw new CustomException("QR_INVALID", "Invalid QR code.", 400);
        }

        if (registration.Tournament.StatusCode == "CANCELLED")
        {
            throw new CustomException("TOURNAMENT_CANCELLED", "The tournament is cancelled.", 400);
        }

        if (registration.StatusCode == "CANCELLED")
        {
            throw new CustomException("REGISTRATION_CANCELLED", "The registration is cancelled.", 400);
        }

        var response = new CheckInResponseDto
        {
            RegistrationId = registration.Id,
            PlayerName = registration.User.DisplayName,
            TournamentName = registration.Tournament.Name,
            Events = registration.OfflineRegistrationEvents.Select(e => e.Event.PuzzleType.Name).ToList()
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
