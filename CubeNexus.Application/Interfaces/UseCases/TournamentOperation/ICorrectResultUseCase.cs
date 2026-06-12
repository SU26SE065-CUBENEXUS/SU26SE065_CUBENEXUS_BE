using System;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface ICorrectResultUseCase
{
    Task<ResultCorrectionResponseDto> ExecuteAsync(Guid resultId, ResultCorrectionDto dto, Guid managerId);
}
