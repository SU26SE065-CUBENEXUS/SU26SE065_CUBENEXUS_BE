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

        if (tournament.TournamentType != "ONLINE_ASYNC")
        {
            var eventIds = events.Select(e => e.Id).ToList();
            if (eventIds.Any())
            {
                var groups = await _unitOfWork.Groups.FindAsync(g => eventIds.Contains(g.EventId));
                if (groups.Any())
                {
                    var uncompletedGroups = groups.Where(g => g.StatusCode != "COMPLETED").ToList();
                    if (uncompletedGroups.Any())
                    {
                        throw new CustomException(
                            "TOURNAMENT_NOT_READY_TO_COMPLETE",
                            "Không thể hoàn thành giải đấu vì còn vòng thi hoặc nhóm thi đấu chưa hoàn tất (chưa Complete Round). Vui lòng nhập điểm và hoàn thành các vòng thi trước.",
                            400
                        );
                    }
                }
            }
        }

        foreach (var ev in events)
        {
            ev.RegistrationStatusCode = "CLOSED";
            _unitOfWork.Events.Update(ev);
        }

        tournament.StatusCode = "COMPLETED";
        _unitOfWork.Tournaments.Update(tournament);

        // Auto-deactivate all judge user accounts for this tournament
        var judges = await _unitOfWork.TournamentJudges.FindAsync(tj => tj.TournamentId == tournamentId);
        var judgeUserIds = judges.Select(tj => tj.UserId).Distinct().ToList();
        if (judgeUserIds.Any())
        {
            var judgeUsers = await _unitOfWork.Users.FindAsync(u => judgeUserIds.Contains(u.Id));
            foreach (var judgeUser in judgeUsers)
            {
                judgeUser.IsActive = false;
                _unitOfWork.Users.Update(judgeUser);
            }
        }

        await _unitOfWork.SaveChangesAsync();

        return new OperationResultDto
        {
            Success = true,
            Message = "Tournament completed successfully."
        };
    }
}
