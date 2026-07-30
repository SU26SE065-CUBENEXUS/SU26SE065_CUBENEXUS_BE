using System;
using System.Linq;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class CompleteTournamentUseCase : ICompleteTournamentUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CompleteTournamentUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationResultDto> ExecuteAsync(Guid tournamentId)
    {
        var tournament = await _unitOfWork.Tournaments.GetByIdAsync(tournamentId);
        if (tournament == null)
        {
            throw new CustomException("TOURNAMENT_NOT_FOUND", "Tournament not found.", 404);
        }

        if (tournament.StatusCode == "COMPLETED")
        {
            return new OperationResultDto
            {
                Success = true,
                Message = "This tournament is already completed."
            };
        }

        var events = await _unitOfWork.Events.FindAsync(e => e.TournamentId == tournamentId);
        foreach (var ev in events)
        {
            ev.RegistrationStatusCode = "CLOSED";
            _unitOfWork.Events.Update(ev);
        }

        tournament.StatusCode = "COMPLETED";
        _unitOfWork.Tournaments.Update(tournament);

        await _unitOfWork.SaveChangesAsync();

        return new OperationResultDto
        {
            Success = true,
            Message = "Tournament completed successfully."
        };
    }
}
