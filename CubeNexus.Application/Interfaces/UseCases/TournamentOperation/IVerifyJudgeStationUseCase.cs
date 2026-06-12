using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

public interface IVerifyJudgeStationUseCase
{
    Task<VerifyJudgeStationResponseDto> ExecuteAsync(VerifyJudgeStationDto dto);
}
