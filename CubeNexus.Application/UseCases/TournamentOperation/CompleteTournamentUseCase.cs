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
        var eventList = events.ToList();

        if (tournament.TournamentType != "ONLINE_ASYNC")
        {
            if (!eventList.Any())
            {
                throw new CustomException(
                    "TOURNAMENT_NO_EVENTS",
                    "Không thể hoàn thành giải đấu vì chưa có môn thi nào.",
                    400
                );
            }

            var puzzleTypes = await _unitOfWork.PuzzleTypes.GetAllAsync();
            var puzzleTypeMap = puzzleTypes.ToDictionary(p => p.Id, p => p.Name);

            var eventIds = eventList.Select(e => e.Id).ToList();
            var allGroups = await _unitOfWork.Groups.FindAsync(g => eventIds.Contains(g.EventId));
            var groupsList = allGroups.ToList();

            foreach (var ev in eventList)
            {
                var puzzleName = puzzleTypeMap.GetValueOrDefault(ev.PuzzleTypeId, "Môn thi");
                var evGroups = groupsList.Where(g => g.EventId == ev.Id).ToList();

                if (!evGroups.Any())
                {
                    throw new CustomException(
                        "EVENT_NOT_STARTED",
                        $"Không thể hoàn thành giải đấu! Môn thi '{puzzleName}' chưa được khởi tạo nhóm hay tạo vòng thi nào.",
                        400
                    );
                }

                var uncompletedGroups = evGroups.Where(g => g.StatusCode != "COMPLETED").ToList();
                if (uncompletedGroups.Any())
                {
                    throw new CustomException(
                        "TOURNAMENT_NOT_READY_TO_COMPLETE",
                        $"Không thể hoàn thành giải đấu! Môn thi '{puzzleName}' còn nhóm/vòng thi chưa hoàn tất (chưa Complete Round). Vui lòng nhập điểm và hoàn thành các vòng thi trước.",
                        400
                    );
                }

                int configuredRounds = ev.TotalRounds > 0 ? ev.TotalRounds : 1;
                int maxRoundNumber = evGroups.Max(g => g.RoundNumber);
                if (maxRoundNumber < configuredRounds)
                {
                    throw new CustomException(
                        "UNCOMPLETED_ROUNDS_REMAINING",
                        $"Không thể hoàn thành giải đấu! Môn thi '{puzzleName}' được cấu hình {configuredRounds} vòng đấu nhưng hiện tại mới thực hiện xong Vòng {maxRoundNumber}. Vui lòng thực hiện 'Advance Round' để tuyển chọn thí sinh thi tiếp Vòng {maxRoundNumber + 1}.",
                        400
                    );
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
