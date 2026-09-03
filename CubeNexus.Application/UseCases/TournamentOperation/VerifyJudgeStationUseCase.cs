using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Helpers;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.UseCases.TournamentOperation;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.TournamentOperation;

public class VerifyJudgeStationUseCase : IVerifyJudgeStationByStationUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public VerifyJudgeStationUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // New verify-by-station flow (resolving GroupId automatically)
    public async Task<VerifyJudgeStationResponseDto> ExecuteAsync(VerifyJudgeStationByStationDto dto, Guid? judgeUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.QrToken))
            throw new CustomException("INVALID_QR_TOKEN", "QR code token is empty.", 400);

        CubeNexus.Application.DTOs.Registration.RegistrationQrPayload? payload = null;
        try
        {
            payload = System.Text.Json.JsonSerializer.Deserialize<CubeNexus.Application.DTOs.Registration.RegistrationQrPayload>(dto.QrToken);
        }
        catch
        {
            // Fallback for raw text token
        }

        Domain.Entities.Registration? registration = null;
        if (payload != null && payload.RegistrationId != Guid.Empty && !string.IsNullOrEmpty(payload.Token))
        {
            if (payload.ExpiresAt < DateTime.UtcNow)
                throw new CustomException("QR_EXPIRED", "The competitor's QR ticket has expired.", 400);

            registration = await _unitOfWork.Registrations.GetRegistrationWithDetailsAsync(payload.RegistrationId, ct);
            if (registration == null)
                throw new CustomException("INVALID_QR_TOKEN", "Invalid QR code registration details.", 400);

            // Verify the payload token matches the one stored in the DB (resolves JSON serialization differences)
            try
            {
                var dbPayload = System.Text.Json.JsonSerializer.Deserialize<CubeNexus.Application.DTOs.Registration.RegistrationQrPayload>(registration.QrToken);
                if (dbPayload == null || dbPayload.Token != payload.Token)
                {
                    throw new CustomException("INVALID_QR_TOKEN", "QR code verification failed (token mismatch).", 400);
                }
            }
            catch (CustomException) { throw; }
            catch
            {
                throw new CustomException("INVALID_QR_TOKEN", "QR code validation failed due to invalid database state.", 400);
            }
        }
        else
        {
            // Raw token lookup fallback
            registration = await _unitOfWork.Registrations.GetByQrTokenAsync(dto.QrToken, ct);
            if (registration == null)
                throw new CustomException("INVALID_QR_TOKEN", "Invalid QR code credentials.", 400);
        }

        if (judgeUserId.HasValue)
        {
            var caller = await _unitOfWork.Users.GetByIdAsync(judgeUserId.Value, ct);
            if (caller != null && string.Equals(caller.UserRole, "JUDGE", StringComparison.OrdinalIgnoreCase))
            {
                var isAssigned = await _unitOfWork.TournamentJudges.AnyAsync(
                    tj => tj.TournamentId == registration.TournamentId && tj.UserId == judgeUserId.Value, ct);
                if (!isAssigned)
                {
                    throw new CustomException("JUDGE_NOT_ASSIGNED_TO_TOURNAMENT", "The judge is not authorized to verify competitors for this tournament.", 403);
                }
            }
        }

        if (registration.StatusCode == "CANCELLED")
            throw new CustomException("REGISTRATION_CANCELLED", "This registration is cancelled.", 400);

        if (registration.CheckedInAt == null || registration.StatusCode != "CHECKED_IN")
            throw new CustomException("NOT_CHECKED_IN", "Competitor has not checked in at the reception.", 400);

        var offlineRegEvent = await _unitOfWork.OfflineRegistrationEvents.FirstOrDefaultAsync(re => re.RegistrationId == registration.Id && re.EventId == dto.EventId, ct);
        if (offlineRegEvent == null)
            throw new CustomException("COMPETITOR_NOT_REGISTERED_FOR_EVENT", "Competitor is not registered for this event.", 400);

        var ev = await _unitOfWork.Events.GetByIdAsync(dto.EventId, ct);
        if (ev == null)
            throw new CustomException("EVENT_NOT_FOUND", "Event not found.", 404);

        Group? group = null;
        GroupCompetitor? comp = null;

        if (dto.GroupNumber > 0)
        {
            var expectedGroupName = $"Group {dto.GroupNumber}";
            group = await _unitOfWork.Groups.FirstOrDefaultAsync(
                g => g.EventId == dto.EventId && g.RoundNumber == dto.RoundNumber && g.GroupName != null && g.GroupName.ToLower() == expectedGroupName.ToLower(),
                ct
            );

            if (group == null)
                throw new CustomException("GROUP_NOT_FOUND", $"Group {dto.GroupNumber} is not available for this round.", 404);

            var groupCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => gc.GroupId == group.Id && gc.RegistrationEventId == offlineRegEvent.Id, ct);
            comp = groupCompetitors.FirstOrDefault();
        }
        else
        {
            // Auto-resolve Group from competitor's assignment in this Event & Round
            var allRoundGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == dto.EventId && g.RoundNumber == dto.RoundNumber, ct);
            var roundGroupIds = allRoundGroups.Select(g => g.Id).ToList();

            var groupCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(
                gc => roundGroupIds.Contains(gc.GroupId) && gc.RegistrationEventId == offlineRegEvent.Id,
                ct
            );
            comp = groupCompetitors.FirstOrDefault();
            if (comp != null)
            {
                group = allRoundGroups.FirstOrDefault(g => g.Id == comp.GroupId);
            }
        }

        if (group == null || comp == null)
            throw new CustomException("COMPETITOR_NOT_ASSIGNED_TO_ROUND", "Competitor is not assigned to any group in this round.", 400);

        if (comp.StationNumber != dto.StationNumber)
            throw new CustomException("STATION_MISMATCH", $"Competitor is assigned to station {comp.StationNumber}, but scanned at station {dto.StationNumber}.", 400);

        var user = await _unitOfWork.Users.GetByIdAsync(registration.UserId, ct);
        var competitorName = user?.DisplayName ?? "Competitor";

        return await ValidateAndBuildResponseAsync(comp, group, ev, registration, dto.StationNumber, competitorName, ct);
    }

    // Shared Validation and Response construction helper
    private async Task<VerifyJudgeStationResponseDto> ValidateAndBuildResponseAsync(
        GroupCompetitor comp,
        Group group,
        Event ev,
        Registration registration,
        int requestedStationNumber,
        string competitorName,
        CancellationToken ct)
    {
        if (group.StatusCode != "ONGOING")
        {
            if (group.StatusCode == "PENDING")
            {
                throw new CustomException("GROUP_NOT_ONGOING", $"The group in Round {group.RoundNumber} has not been started by the Organizer. Please open Tournament Management -> Round {group.RoundNumber} -> select 'Start Active Round'.", 400);
            }
            if (group.StatusCode == "COMPLETED" || group.StatusCode == "LOCKED")
            {
                throw new CustomException("GROUP_NOT_ONGOING", $"The group in Round {group.RoundNumber} has already ended or results are locked.", 400);
            }
            throw new CustomException("GROUP_NOT_ONGOING", $"The group in Round {group.RoundNumber} is currently not ongoing (Current status: {group.StatusCode}).", 400);
        }

        if (comp.StatusCode == GroupCompetitorStatus.NO_SHOW)
            throw new CustomException("COMPETITOR_NO_SHOW", "This competitor was marked as NO_SHOW and cannot compete.", 400);

        var results = await _unitOfWork.Results.FindAsync(r => r.GroupCompetitorId == comp.Id, ct);
        var resultsList = results.ToList();

        if (resultsList.Any() && resultsList.All(r => r.IsLocked))
            throw new CustomException("RESULTS_LOCKED", "Results for this competitor are already locked.", 400);

        bool isStopped = CutoffEvaluator.IsStopped(ev.SolveCount, ev.CutoffTimeMs, resultsList);

        if (isStopped || comp.StatusCode == GroupCompetitorStatus.COMPLETED || resultsList.Count >= ev.SolveCount)
        {
            if (isStopped)
            {
                bool isDnfStopped = CutoffEvaluator.IsDnfStopped(ev.SolveCount, resultsList);
                if (isDnfStopped)
                {
                    throw new CustomException("DNF_LIMIT_REACHED", "The competitor was stopped because they reached the maximum DNF count leading to a DNF average.", 400);
                }
                var cutoffDisplay = ev.CutoffTimeMs.HasValue ? $"{ev.CutoffTimeMs.Value / 1000.0:0.##}s" : "required cutoff";
                throw new CustomException("CUTOFF_NOT_PASSED", $"The competitor was stopped because they did not pass the Cutoff Time ({cutoffDisplay}).", 400);
            }
            throw new CustomException("COMPETITOR_COMPLETED", "This competitor has already completed all solves for this round.", 400);
        }

        int nextSolveNumber = resultsList.Count + 1;

        ScrambleInfoDto? currentScramble = null;
        if (ev.EventFormatCode == "TRADITIONAL")
        {
            var scrambleSet = await _unitOfWork.ScrambleSets.FirstOrDefaultAsync(ss => ss.GroupId == group.Id, ct);
            if (scrambleSet == null)
                throw new CustomException("SCRAMBLES_NOT_GENERATED", "Scrambles have not been generated for this group yet.", 400);

            var scrambles = await _unitOfWork.Scrambles.FindAsync(s => s.ScrambleSetId == scrambleSet.Id && s.SolveNumber == nextSolveNumber && s.PuzzleTypeId == ev.PuzzleTypeId, ct);
            var scramble = scrambles.FirstOrDefault();
            if (scramble == null)
                throw new CustomException("SCRAMBLES_NOT_GENERATED", "Scrambles have not been generated for this group yet.", 400);

            currentScramble = new ScrambleInfoDto
            {
                ScrambleId = scramble.Id,
                SolveNumber = scramble.SolveNumber,
                Sequence = scramble.Sequence
            };
        }

        var puzzle = await _unitOfWork.PuzzleTypes.GetByIdAsync(ev.PuzzleTypeId, ct);
        var eventName = puzzle?.Name ?? "Unknown Event";

        return new VerifyJudgeStationResponseDto
        {
            Success = true,
            Message = "Verification successful. Competitor can proceed.",
            GroupCompetitorId = comp.Id,
            EventId = ev.Id,
            EventName = eventName,
            RoundNumber = group.RoundNumber,
            GroupId = group.Id,
            GroupName = group.GroupName ?? string.Empty,
            CompetitorName = competitorName,
            StationNumber = comp.StationNumber,
            NextSolveNumber = nextSolveNumber,
            SolveCount = ev.SolveCount,
            CanSubmit = true,
            CurrentScramble = currentScramble
        };
    }
}
