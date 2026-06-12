using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface ILockRoundResultsUseCase
{
    Task<OperationResultDto> ExecuteAsync(Guid eventId, int roundNumber);
}

public interface ICompleteRoundUseCase
{
    Task<OperationResultDto> ExecuteAsync(Guid eventId, int roundNumber);
}
