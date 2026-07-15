using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;

namespace CubeNexus.Application.Interfaces.Services;

public interface ITournamentOperationService
{
    Task<OperationResultDto> CloseEventRegistrationAsync(Guid eventId, CancellationToken ct = default);
    Task<List<GroupDto>> GenerateEventGroupsAsync(Guid eventId, GenerateGroupsDto dto, CancellationToken ct = default);
    Task<OperationResultDto> GenerateGroupScramblesAsync(Guid eventId, GenerateScramblesDto dto, Guid userId, CancellationToken ct = default);
    Task<SubmitResultResponseDto> SubmitTraditionalResultAsync(SubmitTraditionalResultDto dto, Guid userId, CancellationToken ct = default);
    Task<SubmitResultResponseDto> SubmitMedleyResultAsync(SubmitMedleyResultDto dto, Guid userId, CancellationToken ct = default);
    Task<SolveProgressDto> GetSolveProgressAsync(Guid groupCompetitorId, CancellationToken ct = default);
    Task<List<GroupScrambleDetailDto>> GetGroupScramblesAsync(Guid groupId, CancellationToken ct = default);
    Task<List<PenaltyTypeDto>> GetPenaltyTypesAsync(CancellationToken ct = default);
    Task<JudgeStationRosterResponseDto> GetJudgeStationRosterAsync(Guid eventId, int roundNumber, int groupNumber, int stationNumber, CancellationToken ct = default);
}
