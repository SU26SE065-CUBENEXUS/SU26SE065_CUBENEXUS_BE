using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface IStartRoundUseCase
{
    Task<StartRoundResponseDto> ExecuteAsync(Guid eventId, int roundNumber, StartRoundRequestDto dto);
}
