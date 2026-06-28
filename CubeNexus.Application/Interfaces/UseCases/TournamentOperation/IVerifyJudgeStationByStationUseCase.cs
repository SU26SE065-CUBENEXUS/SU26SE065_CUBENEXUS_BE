using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface IVerifyJudgeStationByStationUseCase
{
    Task<VerifyJudgeStationResponseDto> ExecuteAsync(VerifyJudgeStationByStationDto dto, CancellationToken ct = default);
}
