using System;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface ICompleteEventUseCase
{
    Task<OperationResultDto> ExecuteAsync(Guid eventId);
}
