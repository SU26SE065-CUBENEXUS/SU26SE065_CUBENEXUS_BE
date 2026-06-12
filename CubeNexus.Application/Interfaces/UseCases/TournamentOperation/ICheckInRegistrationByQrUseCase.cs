using CubeNexus.Application.DTOs.Registration;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface ICheckInRegistrationByQrUseCase
{
    Task<CheckInResponseDto> ExecuteAsync(CheckInRequestDto dto);
}
