using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Helpers;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Services;
using CubeNexus.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace CubeNexus.Infrastructure.Services;

public class TournamentOperationService : ITournamentOperationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IScrambleGeneratorService _scrambleGenerator;
    private readonly GroupAssignmentDomainService _groupAssignmentService;
    private readonly PenaltyCalculationDomainService _penaltyCalculationService;
    private readonly IRecordingStorageService? _storageService;
    private readonly R2Options? _r2Options;

    public TournamentOperationService(
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtimeNotifier,
        IScrambleGeneratorService scrambleGenerator,
        IRecordingStorageService? storageService = null,
        IOptions<R2Options>? r2Options = null)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
        _scrambleGenerator = scrambleGenerator;
        _storageService = storageService;
        _r2Options = r2Options?.Value;
        _groupAssignmentService = new GroupAssignmentDomainService();
        _penaltyCalculationService = new PenaltyCalculationDomainService();
    }

    private async Task<string?> UploadEvidencePhotoAsync(Guid groupCompetitorId, int solveNumber, string? photoData, string? photoUrl, CancellationToken ct)
    {
        Console.WriteLine($"[Evidence Upload Debug] GroupCompetitorId={groupCompetitorId}, Solve={solveNumber}, photoData len={photoData?.Length ?? 0}, photoUrl={photoUrl ?? "null"}, StorageService={(_storageService != null ? "Available" : "NULL")}, R2PublicUrl={_r2Options?.PublicUrl ?? "NULL"}");

        if (!string.IsNullOrWhiteSpace(photoUrl))
        {
            // Reject device-local paths that cannot be accessed from the web
            if (photoUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                photoUrl.StartsWith("ph://", StringComparison.OrdinalIgnoreCase) ||
                photoUrl.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[Evidence Upload] Ignoring local device path (not accessible from web): {photoUrl.Substring(0, Math.Min(80, photoUrl.Length))}");
                // Don't return it — fall through to try photoData instead
            }
            else
            {
                return photoUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(photoData))
        {
            Console.WriteLine("[Evidence Upload Debug] photoData is null or whitespace. Skipping upload.");
            return null;
        }

        // Reject device-local paths in photoData as well (safety guard)
        if (photoData.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            photoData.StartsWith("ph://", StringComparison.OrdinalIgnoreCase) ||
            photoData.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Evidence Upload] Rejecting local device path in photoData: {photoData.Substring(0, Math.Min(80, photoData.Length))}");
            return null;
        }

        if (_storageService == null)
        {
            Console.WriteLine("[Evidence Upload Warning] StorageService is NULL! Cannot upload to R2.");
            return null; // No storage service — don't save unverifiable data
        }

        try
        {
            var base64Parts = photoData.Split(',');
            var header = base64Parts.Length > 1 ? base64Parts[0].ToLowerInvariant() : string.Empty;
            var base64String = base64Parts.Length > 1 ? base64Parts[1] : base64Parts[0];

            string contentType = "image/jpeg";
            string extension = "jpg";

            if (header.Contains("image/png"))
            {
                contentType = "image/png";
                extension = "png";
            }
            else if (header.Contains("image/webp"))
            {
                contentType = "image/webp";
                extension = "webp";
            }
            else if (header.Contains("image/gif"))
            {
                contentType = "image/gif";
                extension = "gif";
            }

            var bytes = Convert.FromBase64String(base64String.Trim());
            var objectKey = $"evidence/tournaments/gc_{groupCompetitorId}_solve_{solveNumber}_{Guid.NewGuid():N}.{extension}";

            using var ms = new System.IO.MemoryStream(bytes);
            await _storageService.UploadStreamAsync(objectKey, ms, contentType, ct);

            // If a public CDN URL is configured, return the full public URL (accessible from Web browsers).
            // Otherwise fall back to returning the objectKey (web will try to resolve it).
            var publicUrl = _r2Options?.GetPublicUrl(objectKey);
            Console.WriteLine($"[Evidence Upload] Uploaded to R2. Key={objectKey}, PublicUrl={publicUrl ?? "(no public URL configured)"}" );
            return publicUrl ?? objectKey;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Evidence Upload Warning] Failed to upload evidence to R2: {ex.Message}");
            // Return null instead of bad data — better to show 'no photo' than a broken path
            return null;
        }
    }

    public async Task<OperationResultDto> CloseEventRegistrationAsync(Guid eventId, CancellationToken ct = default)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {eventId} not found.");

        ev.RegistrationStatusCode = "CLOSED";
        _unitOfWork.Events.Update(ev);
        await _unitOfWork.SaveChangesAsync(ct);

        return new OperationResultDto
        {
            Success = true,
            Message = "Registration closed successfully."
        };
    }

    public async Task<List<GroupDto>> GenerateEventGroupsAsync(Guid eventId, GenerateGroupsDto dto, CancellationToken ct = default)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {eventId} not found.");

        // Check if groups already exist for this event and round number
        var existingGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == dto.RoundNumber, ct);
        if (existingGroups.Any())
        {
            var groupIds = existingGroups.Select(g => g.Id).ToList();
            var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId), ct);
            if (competitors.Any())
            {
                var compIds = competitors.Select(c => c.Id).ToList();
                var hasResults = await _unitOfWork.Results.AnyAsync(r => compIds.Contains(r.GroupCompetitorId), ct);
                if (hasResults)
                {
                    throw new InvalidOperationException("Cannot regenerate groups when results already exist.");
                }
            }

            if (existingGroups.All(g => g.StatusCode == "PENDING"))
            {
                var scrambleSets = await _unitOfWork.ScrambleSets.FindAsync(ss => groupIds.Contains(ss.GroupId), ct);
                if (scrambleSets.Any())
                {
                    var scrambleSetIds = scrambleSets.Select(ss => ss.Id).ToList();
                    var scrambles = await _unitOfWork.Scrambles.FindAsync(s => scrambleSetIds.Contains(s.ScrambleSetId), ct);
                    _unitOfWork.Scrambles.RemoveRange(scrambles);
                    _unitOfWork.ScrambleSets.RemoveRange(scrambleSets);
                }
                _unitOfWork.GroupCompetitors.RemoveRange(competitors);
                _unitOfWork.Groups.RemoveRange(existingGroups);
                await _unitOfWork.SaveChangesAsync(ct);
            }
            else
            {
                throw new InvalidOperationException($"Groups for event {eventId} round {dto.RoundNumber} already exist.");
            }
        }

        // Get registered events: Round 1 uses all registered competitors. Round > 1 uses ONLY the advanced competitors.
        List<OfflineRegistrationEvent> registeredEvents;
        if (dto.RoundNumber == 1)
        {
            var allRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(
                re => re.EventId == eventId && re.StatusCode == "REGISTERED",
                ct
            );
            registeredEvents = allRegEvents.ToList();
        }
        else
        {
            // For Round > 1: preserve the qualified competitors assigned to this round
            if (existingGroups.Any())
            {
                var existingGroupIds = existingGroups.Select(g => g.Id).ToList();
                var existingCompList = await _unitOfWork.GroupCompetitors.FindAsync(gc => existingGroupIds.Contains(gc.GroupId), ct);
                var existingRegEventIds = existingCompList.Select(gc => gc.RegistrationEventId).Distinct().ToList();

                var regEventsForRound = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => existingRegEventIds.Contains(ore.Id), ct);
                registeredEvents = regEventsForRound.ToList();
            }
            else
            {
                throw new InvalidOperationException($"Cannot generate groups for Round {dto.RoundNumber} directly. Please use 'Advance Round' to select qualified competitors from Round {dto.RoundNumber - 1}.");
            }
        }

        if (!registeredEvents.Any())
            throw new InvalidOperationException("No eligible competitors found to generate groups for this round.");

        // Fetch display names for competitors
        var regIds = registeredEvents.Select(re => re.RegistrationId).Distinct().ToList();
        var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id), ct);
        var userIds = registrations.Select(r => r.UserId).Distinct().ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id), ct);

        var userMap = users.ToDictionary(u => u.Id, u => u);
        var regMap = registrations.ToDictionary(r => r.Id, r => r);

        // Run Domain Service
        var assignments = _groupAssignmentService.AssignGroups(
            eventId,
            dto.RoundNumber,
            registeredEvents,
            dto.CompetitorsPerGroup,
            dto.StationCount
        );

        var groupMap = new Dictionary<int, Group>();
        var newGroups = new List<Group>();
        var newGroupCompetitors = new List<GroupCompetitor>();

        foreach (var assignment in assignments)
        {
            if (!groupMap.TryGetValue(assignment.GroupNumber, out var group))
            {
                group = new Group
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    RoundNumber = dto.RoundNumber,
                    GroupName = assignment.GroupName,
                    StatusCode = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };
                groupMap[assignment.GroupNumber] = group;
                newGroups.Add(group);
            }

            var gc = new GroupCompetitor
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                RegistrationEventId = assignment.RegistrationEvent.Id,
                StationNumber = assignment.StationNumber
            };
            newGroupCompetitors.Add(gc);
        }

        _unitOfWork.Groups.AddRange(newGroups);
        _unitOfWork.GroupCompetitors.AddRange(newGroupCompetitors);
        await _unitOfWork.SaveChangesAsync(ct);

        // Map and Return DTOs
        var resultList = new List<GroupDto>();
        foreach (var group in newGroups)
        {
            var competitorsInGroup = newGroupCompetitors.Where(gc => gc.GroupId == group.Id).Select(gc =>
            {
                var regEvent = registeredEvents.First(re => re.Id == gc.RegistrationEventId);
                var reg = regMap.TryGetValue(regEvent.RegistrationId, out var r) ? r : null;
                var user = reg != null && userMap.TryGetValue(reg.UserId, out var u) ? u : null;

                return new GroupCompetitorDto
                {
                    Id = gc.Id,
                    RegistrationEventId = gc.RegistrationEventId,
                    CompetitorName = user?.DisplayName ?? "Unknown Competitor",
                    StationNumber = gc.StationNumber
                };
            }).ToList();

            resultList.Add(new GroupDto
            {
                Id = group.Id,
                EventId = group.EventId,
                RoundNumber = group.RoundNumber,
                GroupName = group.GroupName ?? string.Empty,
                StatusCode = group.StatusCode,
                Competitors = competitorsInGroup
            });
        }

        return resultList;
    }

    public async Task<OperationResultDto> GenerateGroupScramblesAsync(Guid eventId, GenerateScramblesDto dto, Guid userId, CancellationToken ct = default)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {eventId} not found.");

        var groups = await _unitOfWork.Groups.FindAsync(g => g.EventId == eventId && g.RoundNumber == dto.RoundNumber, ct);
        if (!groups.Any())
            throw new InvalidOperationException("Groups must be generated before generating scrambles.");

        // Strict regeneration rule 0: check if any group in this round is not PENDING
        if (groups.Any(g => g.StatusCode != "PENDING"))
            throw new InvalidOperationException("Cannot generate scrambles after groups have started.");

        var groupIds = groups.Select(g => g.Id).ToList();

        // Strict regeneration rule 1: scramble sets already exist for these groups
        var scrambleSetsExist = await _unitOfWork.ScrambleSets.AnyAsync(ss => groupIds.Contains(ss.GroupId), ct);
        if (scrambleSetsExist)
            throw new InvalidOperationException("Scramble sets already exist for these groups.");

        // Strict regeneration rule 2: results already exist for these groups
        var competitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => groupIds.Contains(gc.GroupId), ct);
        if (competitors.Any())
        {
            var compIds = competitors.Select(c => c.Id).ToList();
            var hasResults = await _unitOfWork.Results.AnyAsync(r => compIds.Contains(r.GroupCompetitorId), ct);
            if (hasResults)
                throw new InvalidOperationException("Cannot regenerate scrambles when results already exist.");
        }

        var puzzleTypes = await _unitOfWork.PuzzleTypes.GetAllAsync(ct);
        var puzzleTypeMap = puzzleTypes.ToDictionary(p => p.Id, p => p);

        var scrambleSets = new List<ScrambleSet>();
        var scrambles = new List<Scramble>();

        foreach (var group in groups)
        {
            var ss = new ScrambleSet
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                PdfUrl = null,
                PdfPasswordHash = null,
                GeneratedAt = DateTime.UtcNow,
                GeneratedBy = userId
            };
            scrambleSets.Add(ss);

            if (ev.EventFormatCode == "TRADITIONAL")
            {
                if (!puzzleTypeMap.TryGetValue(ev.PuzzleTypeId, out var pt))
                    throw new InvalidOperationException($"Puzzle type {ev.PuzzleTypeId} not found.");

                for (int solveNum = 1; solveNum <= ev.SolveCount; solveNum++)
                {
                    scrambles.Add(new Scramble
                    {
                        Id = Guid.NewGuid(),
                        ScrambleSetId = ss.Id,
                        PuzzleTypeId = ev.PuzzleTypeId,
                        SolveNumber = solveNum,
                        Sequence = _scrambleGenerator.GenerateScramble(pt.Code, pt.ScrambleLength),
                        SortOrder = solveNum
                    });
                }
            }
            else if (ev.EventFormatCode == "MEDLEY")
            {
                var medleyPuzzles = await _unitOfWork.MedleyEventPuzzles.FindAsync(mp => mp.EventId == ev.Id, ct);
                medleyPuzzles = medleyPuzzles.OrderBy(mp => mp.SortOrder).ToList();

                for (int solveNum = 1; solveNum <= ev.SolveCount; solveNum++)
                {
                    for (int i = 0; i < medleyPuzzles.Count; i++)
                    {
                        var mp = medleyPuzzles[i];
                        if (!puzzleTypeMap.TryGetValue(mp.PuzzleTypeId, out var pt))
                            throw new InvalidOperationException($"Puzzle type {mp.PuzzleTypeId} not found.");

                        scrambles.Add(new Scramble
                        {
                            Id = Guid.NewGuid(),
                            ScrambleSetId = ss.Id,
                            PuzzleTypeId = mp.PuzzleTypeId,
                            SolveNumber = solveNum,
                            Sequence = _scrambleGenerator.GenerateScramble(pt.Code, pt.ScrambleLength),
                            SortOrder = (solveNum - 1) * medleyPuzzles.Count + (i + 1)
                        });
                    }
                }
            }
        }

        _unitOfWork.ScrambleSets.AddRange(scrambleSets);
        _unitOfWork.Scrambles.AddRange(scrambles);
        await _unitOfWork.SaveChangesAsync(ct);

        return new OperationResultDto
        {
            Success = true,
            Message = $"Scrambles generated successfully for {groups.Count} groups."
        };
    }

    public async Task<SubmitResultResponseDto> SubmitTraditionalResultAsync(SubmitTraditionalResultDto dto, Guid userId, CancellationToken ct = default)
    {
        Console.WriteLine($"[Validation Stage] SubmitTraditionalResultAsync started. GroupCompetitorId={dto.GroupCompetitorId}, ScrambleId={dto.ScrambleId}, SolveNumber={dto.SolveNumber}");

        var groupCompetitor = await _unitOfWork.GroupCompetitors.GetByIdAsync(dto.GroupCompetitorId, ct);
        if (groupCompetitor == null)
            throw new Application.Exceptions.CustomException("GROUP_COMPETITOR_NOT_FOUND", $"Group competitor {dto.GroupCompetitorId} not found.", 404);

        if (groupCompetitor.StatusCode == CubeNexus.Domain.Enums.GroupCompetitorStatus.NO_SHOW)
            throw new Application.Exceptions.CustomException("COMPETITOR_NO_SHOW", "Competitor is marked as NO_SHOW.", 400);

        var group = await _unitOfWork.Groups.GetByIdAsync(groupCompetitor.GroupId, ct);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {groupCompetitor.GroupId} not found.");

        if (group.StatusCode != "ONGOING")
            throw new Application.Exceptions.CustomException("GROUP_NOT_ONGOING", "Group is not ongoing.", 400);

        var offlineRegEvent = await _unitOfWork.OfflineRegistrationEvents.GetByIdAsync(groupCompetitor.RegistrationEventId, ct);
        if (offlineRegEvent == null)
            throw new KeyNotFoundException("Registration event not found.");

        var registration = await _unitOfWork.Registrations.GetByIdAsync(offlineRegEvent.RegistrationId, ct);
        if (registration == null || registration.CheckedInAt == null)
            throw new Application.Exceptions.CustomException("PLAYER_NOT_CHECKED_IN", "Player has not checked in.", 400);

        var user = await _unitOfWork.Users.GetByIdAsync(registration.UserId, ct);
        if (user == null)
            throw new Application.Exceptions.CustomException("USER_NOT_FOUND_FOR_REGISTRATION", $"User {registration.UserId} not found for registration.", 404);

        var ev = await _unitOfWork.Events.GetByIdAsync(group.EventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {group.EventId} not found.");

        if (ev.EventFormatCode != "TRADITIONAL")
            throw new InvalidOperationException("Event format must be TRADITIONAL.");

        var existingResults = await _unitOfWork.Results.FindAsync(r => r.GroupCompetitorId == dto.GroupCompetitorId, ct);
        var existingResultsList = existingResults.ToList();

        if (existingResultsList.Count >= ev.SolveCount)
            throw new Application.Exceptions.CustomException("SOLVE_COUNT_EXCEEDED", $"Competitor has already completed all {ev.SolveCount} solves.", 400);

        int expectedSolveNumber = existingResultsList.Count + 1;
        if (dto.SolveNumber != expectedSolveNumber)
            throw new Application.Exceptions.CustomException("INVALID_SOLVE_NUMBER", $"Expected solve number {expectedSolveNumber}, but got {dto.SolveNumber}.", 400);

        // Check if scramble belongs to the group's scramble set and match solve number and puzzle type
        var scrambleSet = await _unitOfWork.ScrambleSets.FirstOrDefaultAsync(ss => ss.GroupId == group.Id, ct);
        if (scrambleSet == null)
            throw new InvalidOperationException("Scramble set not found for this group.");

        var scramble = await _unitOfWork.Scrambles.GetByIdAsync(dto.ScrambleId, ct);
        if (scramble == null || scramble.ScrambleSetId != scrambleSet.Id || scramble.SolveNumber != dto.SolveNumber || scramble.PuzzleTypeId != ev.PuzzleTypeId)
        {
            throw new Application.Exceptions.CustomException("INVALID_SCRAMBLE", $"Scramble does not match solve number {dto.SolveNumber} for this competitor/group.", 400);
        }

        PenaltyType? penaltyType = null;
        if (dto.PenaltyTypeId.HasValue)
        {
            var p = await _unitOfWork.PenaltyTypes.GetByIdAsync(dto.PenaltyTypeId.Value, ct);
            if (p == null)
                throw new Application.Exceptions.CustomException("PENALTY_TYPE_NOT_FOUND", $"Penalty type {dto.PenaltyTypeId} not found.", 404);
            penaltyType = p;
        }

        // Check for existing result
        var duplicateResult = existingResultsList.FirstOrDefault(r => r.SolveNumber == dto.SolveNumber);
        if (duplicateResult != null)
            throw new Application.Exceptions.CustomException("DUPLICATE_RESULT", $"Result already exists for group competitor {dto.GroupCompetitorId} and solve number {dto.SolveNumber}.", 409);

        var result = new Result
        {
            Id = Guid.NewGuid(),
            GroupCompetitorId = dto.GroupCompetitorId,
            SolveNumber = dto.SolveNumber
        };

        result.ScrambleId = dto.ScrambleId;
        result.JudgedBy = userId;
        result.RawTimeMs = dto.RawTimeMs;
        result.PenaltyTypeId = dto.PenaltyTypeId;
        result.EsignatureData = dto.EsignatureData;
        result.EvidencePhotoUrl = await UploadEvidencePhotoAsync(dto.GroupCompetitorId, dto.SolveNumber, dto.EvidencePhotoData, dto.EvidencePhotoUrl, ct);
        result.SignedAt = dto.EsignatureData != null ? DateTime.UtcNow : null;
        result.SubmittedAt = DateTime.UtcNow;

        _penaltyCalculationService.CalculateTraditionalResult(result, penaltyType);

        _unitOfWork.Results.Add(result);

        // Include current result in list to evaluate cutoff
        existingResultsList.Add(result);
        bool isCutoffStopped = CutoffEvaluator.IsCutoffStopped(ev.SolveCount, ev.CutoffTimeMs, existingResultsList);

        // Check if completed
        var resultCount = existingResultsList.Count;

        if ((resultCount >= ev.SolveCount || isCutoffStopped) && groupCompetitor.StatusCode != CubeNexus.Domain.Enums.GroupCompetitorStatus.COMPLETED)
        {
            groupCompetitor.StatusCode = CubeNexus.Domain.Enums.GroupCompetitorStatus.COMPLETED;
            _unitOfWork.GroupCompetitors.Update(groupCompetitor);
        }

        Console.WriteLine($"[Save Stage] Saving result to DB. GroupCompetitorId={dto.GroupCompetitorId}, RegistrationId={registration.Id}, UserId={user.Id}, ScrambleId={dto.ScrambleId}, SolveNumber={dto.SolveNumber}");
        await _unitOfWork.SaveChangesAsync(ct);

        Console.WriteLine($"[Build-payload Stage] Building realtime payload. GroupCompetitorId={dto.GroupCompetitorId}, RegistrationId={registration.Id}, UserId={user.Id}, ScrambleId={dto.ScrambleId}, SolveNumber={dto.SolveNumber}");
        try
        {
            // Fetch group/round competitors and results for ranking
            var roundGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == ev.Id && g.RoundNumber == group.RoundNumber, ct);
            var roundGroupIds = roundGroups.Select(rg => rg.Id).ToList();
            var roundCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => roundGroupIds.Contains(gc.GroupId), ct);
            var roundCompetitorIds = roundCompetitors.Select(rc => rc.Id).ToList();
            var roundResults = await _unitOfWork.Results.FindAsync(r => roundCompetitorIds.Contains(r.GroupCompetitorId), ct);

            // Fetch registration maps
            var regEventIds = roundCompetitors.Select(gc => gc.RegistrationEventId).ToList();
            var offlineRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => regEventIds.Contains(ore.Id), ct);
            var regIds = offlineRegEvents.Select(ore => ore.RegistrationId).ToList();
            var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id), ct);
            var userIds = registrations.Select(r => r.UserId).ToList();
            var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id), ct);

            var userMap = users.ToDictionary(u => u.Id);
            var regMap = registrations.ToDictionary(r => r.Id);
            var offlineRegEventMap = offlineRegEvents.ToDictionary(ore => ore.Id);

            var penaltyTypes = await _unitOfWork.PenaltyTypes.GetAllAsync(ct);
            var penaltyTypeMap = penaltyTypes.ToDictionary(pt => pt.Id);

            var calculatedCompetitors = LiveBoardCalculator.CalculateCompetitors(
                ev.SolveCount,
                roundCompetitors,
                roundResults,
                userMap,
                regMap,
                offlineRegEventMap,
                penaltyTypeMap,
                ev.CutoffTimeMs
            );

            var calculatedComp = calculatedCompetitors.FirstOrDefault(cc => cc.GroupCompetitorId == groupCompetitor.Id);
            var penaltyCode = result.PenaltyTypeId.HasValue && penaltyTypeMap.TryGetValue(result.PenaltyTypeId.Value, out var pt) ? pt.Code : "NONE";

            var submittedEvent = new ResultSubmittedEventDto
            {
                EventId = ev.Id,
                RoundNumber = group.RoundNumber,
                GroupId = group.Id,
                GroupName = group.GroupName ?? string.Empty,
                GroupCompetitorId = groupCompetitor.Id,
                CompetitorName = user.DisplayName,
                StationNumber = groupCompetitor.StationNumber,
                CompetitorStatus = groupCompetitor.StatusCode.ToString(),
                Result = new SubmittedResultDto
                {
                    ResultId = result.Id,
                    SolveNumber = result.SolveNumber,
                    RawTimeMs = result.RawTimeMs,
                    FinalTimeMs = result.FinalTimeMs,
                    PenaltyCode = penaltyCode,
                    IsDnf = result.IsDnf,
                    IsLocked = result.IsLocked,
                    SubmittedAt = result.SubmittedAt
                },
                Summary = new SubmittedResultSummaryDto
                {
                    CompletedSolves = calculatedComp?.CompletedSolves ?? 0,
                    SolveCount = ev.SolveCount,
                    BestTimeMs = calculatedComp?.BestTimeMs,
                    AverageTimeMs = calculatedComp?.AverageTimeMs,
                    Rank = calculatedComp?.Rank,
                    IsCutoffReached = isCutoffStopped
                }
            };

            Console.WriteLine($"[Broadcast Stage] Broadcasting ResultSubmittedEvent. GroupCompetitorId={dto.GroupCompetitorId}");
            await _realtimeNotifier.BroadcastResultSubmittedAsync(submittedEvent, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast Stage ERROR] Failed to broadcast realtime event: {ex.Message}. GroupCompetitorId={dto.GroupCompetitorId}, RegistrationId={registration.Id}, UserId={user.Id}, ScrambleId={dto.ScrambleId}, SolveNumber={dto.SolveNumber}");
        }

        int submittedCount = resultCount;
        int? nextSolveNumber = (!isCutoffStopped && submittedCount < ev.SolveCount) ? submittedCount + 1 : null;
        bool canSubmitNext = !isCutoffStopped && nextSolveNumber.HasValue && groupCompetitor.StatusCode != CubeNexus.Domain.Enums.GroupCompetitorStatus.NO_SHOW && group.StatusCode == "ONGOING";

        ScrambleInfoDto? nextScramble = null;
        if (canSubmitNext && nextSolveNumber.HasValue)
        {
            var nextScrambles = await _unitOfWork.Scrambles.FindAsync(s => s.ScrambleSetId == scrambleSet.Id && s.SolveNumber == nextSolveNumber.Value && s.PuzzleTypeId == ev.PuzzleTypeId, ct);
            var nextS = nextScrambles.FirstOrDefault();
            if (nextS != null)
            {
                nextScramble = new ScrambleInfoDto
                {
                    ScrambleId = nextS.Id,
                    SolveNumber = nextS.SolveNumber,
                    Sequence = nextS.Sequence
                };
            }
        }

        return new SubmitResultResponseDto
        {
            ResultId = result.Id,
            FinalTimeMs = result.FinalTimeMs,
            IsDnf = result.IsDnf,
            SubmittedSolveNumber = result.SolveNumber,
            Progress = new SubmitProgressDto
            {
                SubmittedCount = submittedCount,
                SolveCount = ev.SolveCount,
                NextSolveNumber = nextSolveNumber,
                CanSubmitNext = canSubmitNext,
                IsCutoffReached = isCutoffStopped
            },
            NextScramble = nextScramble
        };
    }

    public async Task<SubmitResultResponseDto> SubmitMedleyResultAsync(SubmitMedleyResultDto dto, Guid userId, CancellationToken ct = default)
    {
        Console.WriteLine($"[Validation Stage] SubmitMedleyResultAsync started. GroupCompetitorId={dto.GroupCompetitorId}, SolveNumber={dto.SolveNumber}");

        var groupCompetitor = await _unitOfWork.GroupCompetitors.GetByIdAsync(dto.GroupCompetitorId, ct);
        if (groupCompetitor == null)
            throw new Application.Exceptions.CustomException("GROUP_COMPETITOR_NOT_FOUND", $"Group competitor {dto.GroupCompetitorId} not found.", 404);

        if (groupCompetitor.StatusCode == CubeNexus.Domain.Enums.GroupCompetitorStatus.NO_SHOW)
            throw new Application.Exceptions.CustomException("COMPETITOR_NO_SHOW", "Competitor is marked as NO_SHOW.", 400);

        var group = await _unitOfWork.Groups.GetByIdAsync(groupCompetitor.GroupId, ct);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {groupCompetitor.GroupId} not found.");

        if (group.StatusCode != "ONGOING")
            throw new Application.Exceptions.CustomException("GROUP_NOT_ONGOING", "Group is not ongoing.", 400);

        var offlineRegEvent = await _unitOfWork.OfflineRegistrationEvents.GetByIdAsync(groupCompetitor.RegistrationEventId, ct);
        if (offlineRegEvent == null)
            throw new KeyNotFoundException("Registration event not found.");

        var registration = await _unitOfWork.Registrations.GetByIdAsync(offlineRegEvent.RegistrationId, ct);
        if (registration == null || registration.CheckedInAt == null)
            throw new Application.Exceptions.CustomException("PLAYER_NOT_CHECKED_IN", "Player has not checked in.", 400);

        var user = await _unitOfWork.Users.GetByIdAsync(registration.UserId, ct);
        if (user == null)
            throw new Application.Exceptions.CustomException("USER_NOT_FOUND_FOR_REGISTRATION", $"User {registration.UserId} not found for registration.", 404);

        var ev = await _unitOfWork.Events.GetByIdAsync(group.EventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {group.EventId} not found.");

        if (ev.EventFormatCode != "MEDLEY")
            throw new InvalidOperationException("Event format must be MEDLEY.");

        var scrambleSet = await _unitOfWork.ScrambleSets.FirstOrDefaultAsync(ss => ss.GroupId == group.Id, ct);
        if (scrambleSet == null)
            throw new InvalidOperationException("Scramble set not found for this group.");

        var medleyPuzzlesList = await _unitOfWork.MedleyEventPuzzles.FindAsync(mp => mp.EventId == ev.Id, ct);
        var medleyPuzzles = medleyPuzzlesList.OrderBy(mp => mp.SortOrder).ToList();
        var medleyPuzzleMap = medleyPuzzles.ToDictionary(mp => mp.Id, mp => mp);

        var existingResults = await _unitOfWork.Results.FindAsync(r => r.GroupCompetitorId == dto.GroupCompetitorId, ct);
        var existingResultsList = existingResults.ToList();

        if (existingResultsList.Count >= ev.SolveCount)
            throw new Application.Exceptions.CustomException("SOLVE_COUNT_EXCEEDED", $"Competitor has already completed all {ev.SolveCount} solves.", 400);

        int expectedSolveNumber = existingResultsList.Count + 1;
        if (dto.SolveNumber != expectedSolveNumber)
            throw new Application.Exceptions.CustomException("INVALID_SOLVE_NUMBER", $"Expected solve number {expectedSolveNumber}, but got {dto.SolveNumber}.", 400);

        // Check for existing parent result
        var duplicateResult = existingResultsList.FirstOrDefault(r => r.SolveNumber == dto.SolveNumber);
        if (duplicateResult != null)
            throw new Application.Exceptions.CustomException("DUPLICATE_RESULT", $"Result already exists for group competitor {dto.GroupCompetitorId} and solve number {dto.SolveNumber}.", 409);

        var result = new Result
        {
            Id = Guid.NewGuid(),
            GroupCompetitorId = dto.GroupCompetitorId,
            SolveNumber = dto.SolveNumber
        };

        result.ScrambleId = null; // Medley parent result HAS scramble_id = NULL
        result.JudgedBy = userId;
        result.RawTimeMs = null; // RawTimeMs on parent is null, computed from details
        result.PenaltyTypeId = null;
        result.EsignatureData = dto.EsignatureData;
        result.EvidencePhotoUrl = await UploadEvidencePhotoAsync(dto.GroupCompetitorId, dto.SolveNumber, dto.EvidencePhotoData, dto.EvidencePhotoUrl, ct);
        result.SignedAt = dto.EsignatureData != null ? DateTime.UtcNow : null;
        result.SubmittedAt = DateTime.UtcNow;

        var detailList = new List<(MedleyResultDetail detail, PenaltyType? penalty)>();
        var detailsToInsert = new List<MedleyResultDetail>();

        if (dto.Details.Count != medleyPuzzles.Count)
            throw new Application.Exceptions.CustomException("INVALID_MEDLEY_PUZZLE_ORDER", "Details count does not match expected medley puzzles count.", 400);

        int index = 0;
        foreach (var detailDto in dto.Details)
        {
            var expectedMp = medleyPuzzles[index];
            if (detailDto.MedleyPuzzleId != expectedMp.Id)
                throw new Application.Exceptions.CustomException("INVALID_MEDLEY_PUZZLE_ORDER", $"Medley puzzle at index {index} is incorrect. Expected {expectedMp.Id}.", 400);

            if (!medleyPuzzleMap.TryGetValue(detailDto.MedleyPuzzleId, out var mp))
                throw new InvalidOperationException($"Medley puzzle {detailDto.MedleyPuzzleId} not found in this event.");

            // Validate Scramble
            var scramble = await _unitOfWork.Scrambles.GetByIdAsync(detailDto.ScrambleId, ct);
            if (scramble == null || scramble.ScrambleSetId != scrambleSet.Id || scramble.SolveNumber != dto.SolveNumber || scramble.PuzzleTypeId != mp.PuzzleTypeId)
            {
                throw new Application.Exceptions.CustomException("INVALID_SCRAMBLE", $"Scramble does not match solve number {dto.SolveNumber} for this competitor/group.", 400);
            }

            PenaltyType? penaltyType = null;
            if (detailDto.PenaltyTypeId.HasValue)
            {
                var p = await _unitOfWork.PenaltyTypes.GetByIdAsync(detailDto.PenaltyTypeId.Value, ct);
                if (p == null)
                    throw new Application.Exceptions.CustomException("PENALTY_TYPE_NOT_FOUND", $"Penalty type {detailDto.PenaltyTypeId} not found.", 404);
                penaltyType = p;
            }

            var detail = new MedleyResultDetail
            {
                Id = Guid.NewGuid(),
                ResultId = result.Id,
                MedleyPuzzleId = detailDto.MedleyPuzzleId,
                ScrambleId = detailDto.ScrambleId,
                RawTimeMs = detailDto.RawTimeMs,
                PenaltyTypeId = detailDto.PenaltyTypeId,
                SortOrder = index + 1
            };

            index++;

            detailList.Add((detail, penaltyType));
            detailsToInsert.Add(detail);
        }

        // Apply Penalty rules via Domain Service
        _penaltyCalculationService.CalculateMedleyResult(result, detailList);

        _unitOfWork.Results.Add(result);
        _unitOfWork.MedleyResultDetails.AddRange(detailsToInsert);

        // Include current result in list to evaluate cutoff
        existingResultsList.Add(result);
        bool isCutoffStopped = CutoffEvaluator.IsCutoffStopped(ev.SolveCount, ev.CutoffTimeMs, existingResultsList);

        // Check if completed
        var resultCount = existingResultsList.Count;

        if ((resultCount >= ev.SolveCount || isCutoffStopped) && groupCompetitor.StatusCode != CubeNexus.Domain.Enums.GroupCompetitorStatus.COMPLETED)
        {
            groupCompetitor.StatusCode = CubeNexus.Domain.Enums.GroupCompetitorStatus.COMPLETED;
            _unitOfWork.GroupCompetitors.Update(groupCompetitor);
        }

        Console.WriteLine($"[Save Stage] Saving medley result to DB. GroupCompetitorId={dto.GroupCompetitorId}, RegistrationId={registration.Id}, UserId={user.Id}, SolveNumber={dto.SolveNumber}");
        await _unitOfWork.SaveChangesAsync(ct);

        Console.WriteLine($"[Build-payload Stage] Building medley realtime payload. GroupCompetitorId={dto.GroupCompetitorId}, RegistrationId={registration.Id}, UserId={user.Id}, SolveNumber={dto.SolveNumber}");
        try
        {
            // Fetch group/round competitors and results for ranking
            var roundGroups = await _unitOfWork.Groups.FindAsync(g => g.EventId == ev.Id && g.RoundNumber == group.RoundNumber, ct);
            var roundGroupIds = roundGroups.Select(rg => rg.Id).ToList();
            var roundCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(gc => roundGroupIds.Contains(gc.GroupId), ct);
            var roundCompetitorIds = roundCompetitors.Select(rc => rc.Id).ToList();
            var roundResults = await _unitOfWork.Results.FindAsync(r => roundCompetitorIds.Contains(r.GroupCompetitorId), ct);

            // Fetch registration maps
            var regEventIds = roundCompetitors.Select(gc => gc.RegistrationEventId).ToList();
            var offlineRegEvents = await _unitOfWork.OfflineRegistrationEvents.FindAsync(ore => regEventIds.Contains(ore.Id), ct);
            var regIds = offlineRegEvents.Select(ore => ore.RegistrationId).ToList();
            var registrations = await _unitOfWork.Registrations.FindAsync(r => regIds.Contains(r.Id), ct);
            var userIds = registrations.Select(r => r.UserId).ToList();
            var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id), ct);

            var userMap = users.ToDictionary(u => u.Id);
            var regMap = registrations.ToDictionary(r => r.Id);
            var offlineRegEventMap = offlineRegEvents.ToDictionary(ore => ore.Id);

            var penaltyTypes = await _unitOfWork.PenaltyTypes.GetAllAsync(ct);
            var penaltyTypeMap = penaltyTypes.ToDictionary(pt => pt.Id);

            var calculatedCompetitors = LiveBoardCalculator.CalculateCompetitors(
                ev.SolveCount,
                roundCompetitors,
                roundResults,
                userMap,
                regMap,
                offlineRegEventMap,
                penaltyTypeMap,
                ev.CutoffTimeMs
            );

            var calculatedComp = calculatedCompetitors.FirstOrDefault(cc => cc.GroupCompetitorId == groupCompetitor.Id);
            var penaltyCode = "NONE"; // Medley parent result doesn't have a direct penalty type

            var submittedEvent = new ResultSubmittedEventDto
            {
                EventId = ev.Id,
                RoundNumber = group.RoundNumber,
                GroupId = group.Id,
                GroupName = group.GroupName ?? string.Empty,
                GroupCompetitorId = groupCompetitor.Id,
                CompetitorName = user.DisplayName,
                StationNumber = groupCompetitor.StationNumber,
                CompetitorStatus = groupCompetitor.StatusCode.ToString(),
                Result = new SubmittedResultDto
                {
                    ResultId = result.Id,
                    SolveNumber = result.SolveNumber,
                    RawTimeMs = result.RawTimeMs,
                    FinalTimeMs = result.FinalTimeMs,
                    PenaltyCode = penaltyCode,
                    IsDnf = result.IsDnf,
                    IsLocked = result.IsLocked,
                    SubmittedAt = result.SubmittedAt
                },
                Summary = new SubmittedResultSummaryDto
                {
                    CompletedSolves = calculatedComp?.CompletedSolves ?? 0,
                    SolveCount = ev.SolveCount,
                    BestTimeMs = calculatedComp?.BestTimeMs,
                    AverageTimeMs = calculatedComp?.AverageTimeMs,
                    Rank = calculatedComp?.Rank,
                    IsCutoffReached = isCutoffStopped
                }
            };

            Console.WriteLine($"[Broadcast Stage] Broadcasting ResultSubmittedEvent (Medley). GroupCompetitorId={dto.GroupCompetitorId}");
            await _realtimeNotifier.BroadcastResultSubmittedAsync(submittedEvent, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broadcast Stage ERROR] Failed to broadcast medley realtime event: {ex.Message}. GroupCompetitorId={dto.GroupCompetitorId}, RegistrationId={registration.Id}, UserId={user.Id}, SolveNumber={dto.SolveNumber}");
        }

        int submittedCount = resultCount;
        int? nextSolveNumber = (!isCutoffStopped && submittedCount < ev.SolveCount) ? submittedCount + 1 : null;
        bool canSubmitNext = !isCutoffStopped && nextSolveNumber.HasValue && groupCompetitor.StatusCode != CubeNexus.Domain.Enums.GroupCompetitorStatus.NO_SHOW && group.StatusCode == "ONGOING";

        return new SubmitResultResponseDto
        {
            ResultId = result.Id,
            FinalTimeMs = result.FinalTimeMs,
            IsDnf = result.IsDnf,
            SubmittedSolveNumber = result.SolveNumber,
            Progress = new SubmitProgressDto
            {
                SubmittedCount = submittedCount,
                SolveCount = ev.SolveCount,
                NextSolveNumber = nextSolveNumber,
                CanSubmitNext = canSubmitNext,
                IsCutoffReached = isCutoffStopped
            },
            NextScramble = null
        };
    }

    public async Task<SolveProgressDto> GetSolveProgressAsync(Guid groupCompetitorId, CancellationToken ct = default)
    {
        var groupCompetitor = await _unitOfWork.GroupCompetitors.GetByIdAsync(groupCompetitorId, ct);
        if (groupCompetitor == null)
            throw new Application.Exceptions.CustomException("GROUP_COMPETITOR_NOT_FOUND", $"Group competitor {groupCompetitorId} not found.", 404);

        var group = await _unitOfWork.Groups.GetByIdAsync(groupCompetitor.GroupId, ct);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {groupCompetitor.GroupId} not found.");

        var ev = await _unitOfWork.Events.GetByIdAsync(group.EventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {group.EventId} not found.");

        var puzzle = await _unitOfWork.PuzzleTypes.GetByIdAsync(ev.PuzzleTypeId, ct);
        var eventName = puzzle?.Name ?? "Unknown Event";

        var results = await _unitOfWork.Results.FindAsync(r => r.GroupCompetitorId == groupCompetitorId, ct);
        var resultsList = results.ToList();

        var submittedSolveNumbers = resultsList.Select(r => r.SolveNumber).OrderBy(n => n).ToList();
        var submittedCount = resultsList.Count;
        var solveCount = ev.SolveCount;

        int? nextSolveNumber = submittedCount < solveCount ? submittedCount + 1 : null;
        bool canSubmit = true;
        string? reason = null;

        if (groupCompetitor.StatusCode == CubeNexus.Domain.Enums.GroupCompetitorStatus.NO_SHOW)
        {
            canSubmit = false;
            reason = "COMPETITOR_NO_SHOW";
        }
        else if (group.StatusCode != "ONGOING")
        {
            canSubmit = false;
            reason = "GROUP_NOT_ONGOING";
        }
        else
        {
            var offlineRegEvent = await _unitOfWork.OfflineRegistrationEvents.GetByIdAsync(groupCompetitor.RegistrationEventId, ct);
            var registration = offlineRegEvent != null ? await _unitOfWork.Registrations.GetByIdAsync(offlineRegEvent.RegistrationId, ct) : null;
            if (registration == null || registration.CheckedInAt == null)
            {
                canSubmit = false;
                reason = "PLAYER_NOT_CHECKED_IN";
            }
            else if (submittedCount >= solveCount)
            {
                canSubmit = false;
                reason = "ALL_SOLVES_COMPLETED";
            }
        }

        ScrambleInfoDto? currentScramble = null;
        if (canSubmit && nextSolveNumber.HasValue && ev.EventFormatCode == "TRADITIONAL")
        {
            var scrambleSet = await _unitOfWork.ScrambleSets.FirstOrDefaultAsync(ss => ss.GroupId == group.Id, ct);
            if (scrambleSet != null)
            {
                var scrambles = await _unitOfWork.Scrambles.FindAsync(s => s.ScrambleSetId == scrambleSet.Id && s.SolveNumber == nextSolveNumber.Value && s.PuzzleTypeId == ev.PuzzleTypeId, ct);
                var scramble = scrambles.FirstOrDefault();
                if (scramble != null)
                {
                    currentScramble = new ScrambleInfoDto
                    {
                        ScrambleId = scramble.Id,
                        SolveNumber = scramble.SolveNumber,
                        Sequence = scramble.Sequence
                    };
                }
            }
        }

        return new SolveProgressDto
        {
            GroupCompetitorId = groupCompetitorId,
            EventId = ev.Id,
            EventName = eventName,
            RoundNumber = group.RoundNumber,
            GroupId = group.Id,
            GroupName = group.GroupName ?? string.Empty,
            StationNumber = groupCompetitor.StationNumber,
            SolveCount = solveCount,
            SubmittedSolveNumbers = submittedSolveNumbers,
            SubmittedCount = submittedCount,
            NextSolveNumber = nextSolveNumber,
            CanSubmit = canSubmit,
            Reason = reason,
            CurrentScramble = currentScramble
        };
    }

    public async Task<JudgeStationRosterResponseDto> GetJudgeStationRosterAsync(Guid eventId, int roundNumber, int groupNumber, int stationNumber, CancellationToken ct = default)
    {
        var ev = await _unitOfWork.Events.GetByIdAsync(eventId, ct);
        if (ev == null)
            throw new KeyNotFoundException($"Event with ID {eventId} not found.");

        var puzzle = await _unitOfWork.PuzzleTypes.GetByIdAsync(ev.PuzzleTypeId, ct);
        var eventName = puzzle?.Name ?? "Unknown Event";

        List<CubeNexus.Domain.Entities.Group> groups;
        if (groupNumber > 0)
        {
            var expectedGroupName = $"Group {groupNumber}";
            var singleGroup = await _unitOfWork.Groups.FirstOrDefaultAsync(
                g => g.EventId == eventId && g.RoundNumber == roundNumber && g.GroupName != null && g.GroupName.ToLower() == expectedGroupName.ToLower(),
                ct
            );
            groups = singleGroup != null ? new List<CubeNexus.Domain.Entities.Group> { singleGroup } : new List<CubeNexus.Domain.Entities.Group>();
        }
        else
        {
            var allGroups = await _unitOfWork.Groups.FindAsync(
                g => g.EventId == eventId && g.RoundNumber == roundNumber,
                ct
            );
            groups = allGroups.ToList();
        }

        if (!groups.Any())
        {
            return new JudgeStationRosterResponseDto
            {
                Success = true,
                Message = $"No groups found for event {eventId} and round {roundNumber}.",
                Competitors = new List<JudgeStationRosterItemDto>()
            };
        }

        var groupMap = groups.ToDictionary(g => g.Id, g => g);
        var groupIds = groups.Select(g => g.Id).ToList();

        var groupCompetitors = await _unitOfWork.GroupCompetitors.FindAsync(
            gc => groupIds.Contains(gc.GroupId) && gc.StationNumber == stationNumber,
            ct
        );

        var list = new List<JudgeStationRosterItemDto>();

        foreach (var gc in groupCompetitors)
        {
            var grp = groupMap.GetValueOrDefault(gc.GroupId);
            if (grp == null) continue;

            var offlineRegEvent = await _unitOfWork.OfflineRegistrationEvents.GetByIdAsync(gc.RegistrationEventId, ct);
            if (offlineRegEvent == null) continue;

            var registration = await _unitOfWork.Registrations.GetByIdAsync(offlineRegEvent.RegistrationId, ct);
            if (registration == null) continue;

            var user = await _unitOfWork.Users.GetByIdAsync(registration.UserId, ct);
            var competitorName = user?.DisplayName ?? "Unknown Competitor";

            var results = await _unitOfWork.Results.FindAsync(r => r.GroupCompetitorId == gc.Id, ct);
            var resultsList = results.ToList();
            var submittedCount = resultsList.Count;

            int? nextSolveNumber = submittedCount < ev.SolveCount ? submittedCount + 1 : null;
            bool canSubmit = gc.StatusCode != CubeNexus.Domain.Enums.GroupCompetitorStatus.NO_SHOW &&
                             grp.StatusCode == "ONGOING" &&
                             registration.CheckedInAt != null &&
                             registration.StatusCode == "CHECKED_IN" &&
                             submittedCount < ev.SolveCount;

            list.Add(new JudgeStationRosterItemDto
            {
                GroupCompetitorId = gc.Id,
                GroupId = grp.Id,
                GroupName = grp.GroupName ?? string.Empty,
                CompetitorName = competitorName,
                EventId = ev.Id,
                EventName = eventName,
                RoundNumber = roundNumber,
                StationNumber = stationNumber,
                SolveCount = ev.SolveCount,
                SubmittedCount = submittedCount,
                NextSolveNumber = nextSolveNumber,
                CanSubmit = canSubmit,
                Status = gc.StatusCode.ToString()
            });
        }

        return new JudgeStationRosterResponseDto
        {
            Success = true,
            Message = "Roster retrieved successfully.",
            Competitors = list
        };
    }

}
