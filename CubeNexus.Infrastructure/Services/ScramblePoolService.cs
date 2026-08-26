using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CubeNexus.Application.DTOs.Admin;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.UseCases.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Services;

public sealed class ScramblePoolService : IScramblePoolService, IAdminScrambleService
{
    private static readonly SemaphoreSlim NotificationGate = new(1, 1);
    private sealed record ScramblePoolStatusRow(
        string CompetitionMode,
        Guid PuzzleTypeId,
        string PuzzleCode,
        string Status);

    private static readonly HashSet<string> Modes = ["ONLINE_MATCH", "OFFLINE", "ONLINE_ASYNC"];
    private static readonly HashSet<string> TournamentStatusesRequiringScrambles =
        ["REGISTRATION_CLOSED", "CHECKING_IN", "ONGOING"];
    private static readonly Regex MovePattern = new("^[2-9]?[RLUDFB](?:w)?(?:2|')?$", RegexOptions.Compiled);
    private readonly ApplicationDbContext _db;
    private readonly IScrambleGeneratorService _generator;
    private readonly IRealtimeNotifier? _realtimeNotifier;

    public ScramblePoolService(ApplicationDbContext db, IScrambleGeneratorService generator, IRealtimeNotifier? realtimeNotifier = null)
    {
        _db = db;
        _generator = generator;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<ScrambleReservationDto> ReserveAsync(string competitionMode, Guid puzzleTypeId,
        string targetType, Guid targetId, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var mode = NormalizeMode(competitionMode);
        for (var retry = 0; retry < 8; retry++)
        {
            var id = await _db.ScramblePoolItems.AsNoTracking()
                .Where(x => x.CompetitionMode == mode && x.PuzzleTypeId == puzzleTypeId &&
                            x.Status == "AVAILABLE" && x.IsValidated)
                .OrderBy(x => x.ApprovedAt ?? x.CreatedAt)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (!id.HasValue)
            {
                var puzzle = await _db.PuzzleTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == puzzleTypeId, ct);
                var puzzleCode = puzzle?.Code ?? "UNKNOWN";
                var puzzleName = puzzle?.Name ?? "Rubik";

                var generationMode = await _db.ScrambleGenerationSettings.AsNoTracking()
                    .Where(x => x.CompetitionMode == mode)
                    .Select(x => x.GenerationMode)
                    .SingleOrDefaultAsync(ct) ?? "MANUAL";

                if (generationMode == "AUTO")
                {
                    if (puzzle != null)
                    {
                        var autoSequence = NormalizeSequence(_generator.GenerateScramble(puzzle.Code, puzzle.ScrambleLength));
                        var autoHash = Hash(autoSequence);
                        var newItem = CreateItem(mode, puzzle, autoSequence, autoHash, "CUBENEXUS_AUTO_ON_DEMAND",
                            "Auto-generated on demand", actorUserId ?? Guid.Empty, approved: true);

                        newItem.Status = "RESERVED";
                        newItem.AssignedTargetType = targetType;
                        newItem.AssignedTargetId = targetId;
                        newItem.AssignedAt = DateTime.UtcNow;

                        _db.ScramblePoolItems.Add(newItem);
                        AddAudit(newItem.Id, "AUTO_GENERATED_AND_RESERVED", actorUserId, targetType, targetId);
                        await _db.SaveChangesAsync(ct);

                        return new ScrambleReservationDto(newItem.Id, newItem.Sequence, newItem.ExpectedStateJson);
                    }
                }

                var emptyMessage = $"Scramble pool for {mode} ({puzzleCode}) is empty! Please generate scrambles or enable AUTO mode.";
                var payload = JsonSerializer.Serialize(new
                {
                    competitionMode = mode,
                    puzzleTypeId,
                    puzzleCode,
                    puzzleName
                });
                if (mode != "ONLINE_ASYNC")
                    await EnsureDepletedNotificationAsync(mode, puzzleTypeId, puzzleCode, puzzleName,
                        emptyMessage, payload, ct);

                throw new InvalidOperationException($"SCRAMBLE_POOL_EMPTY: Scramble pool for {mode} ({puzzleCode}) has run out! Please generate scrambles or switch to AUTO mode.");
            }

            var now = DateTime.UtcNow;
            var changed = await _db.ScramblePoolItems
                .Where(x => x.Id == id && x.Status == "AVAILABLE")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "RESERVED")
                    .SetProperty(x => x.AssignedTargetType, targetType)
                    .SetProperty(x => x.AssignedTargetId, targetId)
                    .SetProperty(x => x.AssignedAt, now), ct);
            if (changed == 0) continue;

            var item = await _db.ScramblePoolItems.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            _db.ScramblePoolAuditLogs.Add(new ScramblePoolAuditLog
            {
                Id = Guid.NewGuid(), ScramblePoolItemId = item.Id, Action = "ASSIGNED",
                ActorUserId = actorUserId, TargetType = targetType, TargetId = targetId, CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);
            return new ScrambleReservationDto(item.Id, item.Sequence, item.ExpectedStateJson);
        }
        throw new InvalidOperationException("SCRAMBLE_POOL_BUSY: The scramble pool is being allocated concurrently. Please try again.");
    }

    public async Task MarkUsedAsync(Guid scramblePoolItemId, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var item = await _db.ScramblePoolItems.SingleOrDefaultAsync(x => x.Id == scramblePoolItemId, ct)
            ?? throw new KeyNotFoundException("Scramble not found.");
        if (item.Status == "USED") return;
        if (item.Status != "RESERVED") throw new InvalidOperationException("Only reserved scrambles can be marked as used.");
        item.Status = "USED";
        item.UsedAt = DateTime.UtcNow;
        AddAudit(item.Id, "USED", actorUserId, item.AssignedTargetType, item.AssignedTargetId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OnlineMatchAvailabilityDto> GetOnlineMatchAvailabilityAsync(
        Guid puzzleTypeId, CancellationToken ct = default)
    {
        var puzzle = await _db.PuzzleTypes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == puzzleTypeId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Active puzzle type not found.");
        if (puzzle.Code is not ("333" or "3x3x3"))
            throw new InvalidOperationException("ONLINE_MATCH currently supports 3x3x3 only.");

        var mode = await _db.ScrambleGenerationSettings.AsNoTracking()
            .Where(x => x.CompetitionMode == "ONLINE_MATCH")
            .Select(x => x.GenerationMode)
            .SingleOrDefaultAsync(ct) ?? "MANUAL";
        var availableCount = await _db.ScramblePoolItems.AsNoTracking()
            .CountAsync(x => x.CompetitionMode == "ONLINE_MATCH" && x.PuzzleTypeId == puzzleTypeId &&
                             x.Status == "AVAILABLE" && x.IsValidated, ct);
        var isAvailable = mode.Equals("AUTO", StringComparison.OrdinalIgnoreCase) || availableCount > 0;
        var message = isAvailable ? null : "Online matches are temporarily unavailable because the 3x3x3 scramble pool is empty.";

        if (!isAvailable)
        {
            var payload = JsonSerializer.Serialize(new
            {
                competitionMode = "ONLINE_MATCH",
                puzzleTypeId,
                puzzleCode = puzzle.Code,
                puzzleName = puzzle.Name
            });
            await EnsureDepletedNotificationAsync("ONLINE_MATCH", puzzleTypeId, puzzle.Code, puzzle.Name,
                message!, payload, ct);
        }

        return new OnlineMatchAvailabilityDto(isAvailable, mode.ToUpperInvariant(), availableCount, message);
    }

    public async Task<IReadOnlyList<ScramblePoolSummaryDto>> GetSummaryAsync(CancellationToken ct = default)
    {
        // 1. Active OFFLINE Tournament puzzle types
        var activeOfflinePuzzleTypeIds = await _db.Events.AsNoTracking()
            .Where(e => e.Tournament != null && 
                (e.Tournament.TournamentType == "OFFLINE" || string.IsNullOrEmpty(e.Tournament.TournamentType)) &&
                TournamentStatusesRequiringScrambles.Contains(e.Tournament.StatusCode.ToUpper()))
            .Select(e => e.PuzzleTypeId)
            .Distinct()
            .ToListAsync(ct);

        // 2. Active ONLINE_ASYNC Tournament puzzle types
        var activeOnlineAsyncPuzzleTypeIds = Array.Empty<Guid>();

        // 3. Standard active puzzle types used for Online Arena Matches (PvP)
        var onlineMatchPuzzleTypeIds = await _db.PuzzleTypes.AsNoTracking()
            .Where(pt => pt.IsActive && (pt.Code == "333" || pt.Code == "3x3x3"))
            .Select(pt => pt.Id)
            .ToListAsync(ct);

        var usedPuzzleTypeIds = activeOfflinePuzzleTypeIds
            .Concat(activeOnlineAsyncPuzzleTypeIds)
            .Concat(onlineMatchPuzzleTypeIds)
            .Distinct()
            .ToHashSet();

        var puzzleTypes = await _db.PuzzleTypes.AsNoTracking()
            .Where(pt => pt.IsActive && usedPuzzleTypeIds.Contains(pt.Id))
            .Select(pt => new { pt.Id, pt.Code })
            .ToListAsync(ct);

        var rows = await _db.ScramblePoolItems.AsNoTracking()
            .Select(x => new ScramblePoolStatusRow(
                x.CompetitionMode,
                x.PuzzleTypeId,
                x.PuzzleType.Code,
                x.Status))
            .ToListAsync(ct);

        var summaryList = rows
            .GroupBy(x => new { x.CompetitionMode, x.PuzzleTypeId, x.PuzzleCode, x.Status })
            .Select(g => new ScramblePoolSummaryDto(g.Key.CompetitionMode, g.Key.PuzzleTypeId,
                g.Key.PuzzleCode, g.Key.Status, g.Count()))
            .ToList();

        var resultList = new List<ScramblePoolSummaryDto>(summaryList);

        // Inject AVAILABLE = 0 warnings ONLY if the puzzle type is used in THAT specific active competition mode
        foreach (var pt in puzzleTypes)
        {
            // For OFFLINE: only if used in an active OFFLINE tournament
            if (activeOfflinePuzzleTypeIds.Contains(pt.Id))
            {
                var hasAvailable = resultList.Any(s => s.CompetitionMode == "OFFLINE" && s.PuzzleTypeId == pt.Id && s.Status == "AVAILABLE");
                if (!hasAvailable)
                {
                    resultList.Add(new ScramblePoolSummaryDto("OFFLINE", pt.Id, pt.Code, "AVAILABLE", 0));
                }
            }

            // For ONLINE_MATCH: only if used in online arena matches
            if (onlineMatchPuzzleTypeIds.Contains(pt.Id))
            {
                var hasAvailable = resultList.Any(s => s.CompetitionMode == "ONLINE_MATCH" && s.PuzzleTypeId == pt.Id && s.Status == "AVAILABLE");
                if (!hasAvailable)
                {
                    resultList.Add(new ScramblePoolSummaryDto("ONLINE_MATCH", pt.Id, pt.Code, "AVAILABLE", 0));
                }
            }
        }

        await ResolveInactiveScrambleNotificationsAsync(
            activeOfflinePuzzleTypeIds,
            activeOnlineAsyncPuzzleTypeIds,
            onlineMatchPuzzleTypeIds,
            rows,
            ct);

        return resultList
            .OrderBy(x => x.CompetitionMode)
            .ThenBy(x => x.PuzzleCode)
            .ThenBy(x => x.Status)
            .ToList();
    }

    private async Task EnsureDepletedNotificationAsync(string mode, Guid puzzleTypeId, string puzzleCode,
        string puzzleName, string message, string payload, CancellationToken ct)
    {
        await NotificationGate.WaitAsync(ct);
        try
        {
            var adminIds = await _db.Users.AsNoTracking()
                .Where(u => u.UserRole == "ADMIN" && u.IsActive && !u.IsBanned)
                .Select(u => u.Id)
                .ToListAsync(ct);
            var existingPayloads = await _db.Notifications.AsNoTracking()
                .Where(n => n.TypeCode == "SCRAMBLE_POOL_EMPTY" && !n.IsRead && adminIds.Contains(n.UserId))
                .Select(n => new { n.UserId, n.Payload })
                .ToListAsync(ct);
            var notifications = adminIds
                .Where(adminId => !existingPayloads.Any(existing => existing.UserId == adminId &&
                    existing.Payload != null &&
                    existing.Payload.Contains($"\"competitionMode\":\"{mode}\"") &&
                    existing.Payload.Contains($"\"puzzleTypeId\":\"{puzzleTypeId}\"")))
                .Select(adminId => new Notification
                {
                    Id = Guid.NewGuid(), UserId = adminId, TypeCode = "SCRAMBLE_POOL_EMPTY",
                    Title = "Scramble pool empty", Body = message, Payload = payload,
                    IsRead = false, CreatedAt = DateTime.UtcNow
                })
                .ToList();
            if (notifications.Count > 0)
            {
                _db.Notifications.AddRange(notifications);
                await _db.SaveChangesAsync(ct);
                if (_realtimeNotifier != null)
                    await _realtimeNotifier.BroadcastScramblePoolDepletedAsync(new
                    {
                        CompetitionMode = mode, PuzzleTypeId = puzzleTypeId, PuzzleCode = puzzleCode,
                        PuzzleName = puzzleName, Message = message, Timestamp = DateTime.UtcNow
                    }, ct);
            }
        }
        finally
        {
            NotificationGate.Release();
        }
    }

    private async Task ResolveInactiveScrambleNotificationsAsync(
        IReadOnlyCollection<Guid> activeOfflinePuzzleTypeIds,
        IReadOnlyCollection<Guid> activeOnlineAsyncPuzzleTypeIds,
        IReadOnlyCollection<Guid> onlineMatchPuzzleTypeIds,
        IReadOnlyCollection<ScramblePoolStatusRow> rows,
        CancellationToken ct)
    {
        var modeSettings = await _db.ScrambleGenerationSettings.AsNoTracking()
            .Select(x => new { x.CompetitionMode, x.GenerationMode })
            .ToListAsync(ct);
        var manualModes = modeSettings
            .Where(x => x.GenerationMode.Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.CompetitionMode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Missing settings use MANUAL by default.
        foreach (var mode in Modes)
        {
            if (!modeSettings.Any(x => x.CompetitionMode.Equals(mode, StringComparison.OrdinalIgnoreCase)))
                manualModes.Add(mode);
        }

        var availableKeys = rows
            .Where(x => x.Status == "AVAILABLE")
            .Select(x => $"{x.CompetitionMode}:{x.PuzzleTypeId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddDepletedKeys(string mode, IEnumerable<Guid> puzzleTypeIds)
        {
            if (!manualModes.Contains(mode)) return;
            foreach (var puzzleTypeId in puzzleTypeIds)
            {
                var key = $"{mode}:{puzzleTypeId}";
                if (!availableKeys.Contains(key)) activeKeys.Add(key);
            }
        }

        AddDepletedKeys("OFFLINE", activeOfflinePuzzleTypeIds);
        AddDepletedKeys("ONLINE_ASYNC", activeOnlineAsyncPuzzleTypeIds);
        AddDepletedKeys("ONLINE_MATCH", onlineMatchPuzzleTypeIds);

        var unread = await _db.Notifications
            .Where(n => n.TypeCode == "SCRAMBLE_POOL_EMPTY" && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        var changed = false;
        var retainedActiveNotifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var notification in unread)
        {
            try
            {
                using var payload = JsonDocument.Parse(notification.Payload ?? "{}");
                var root = payload.RootElement;
                var mode = root.TryGetProperty("competitionMode", out var modeValue) ? modeValue.GetString() : null;
                var puzzleTypeIdText = root.TryGetProperty("puzzleTypeId", out var puzzleValue) ? puzzleValue.GetString() : null;
                if (string.IsNullOrWhiteSpace(mode) || !Guid.TryParse(puzzleTypeIdText, out var puzzleTypeId)) continue;
                var alertKey = $"{mode}:{puzzleTypeId}";
                var perAdminKey = $"{notification.UserId}:{alertKey}";
                if (activeKeys.Contains(alertKey) && retainedActiveNotifications.Add(perAdminKey)) continue;

                notification.IsRead = true;
                notification.ReadAt = now;
                changed = true;
            }
            catch (JsonException)
            {
                // Preserve malformed legacy notifications for manual review.
            }
        }

        if (changed) await _db.SaveChangesAsync(ct);
    }

    public async Task<ScramblePoolPageDto> GetItemsAsync(string? mode, string? status, Guid? puzzleTypeId,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.ScramblePoolItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(mode)) query = query.Where(x => x.CompetitionMode == NormalizeMode(mode));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim().ToUpperInvariant());
        if (puzzleTypeId.HasValue) query = query.Where(x => x.PuzzleTypeId == puzzleTypeId.Value);
        var total = await query.CountAsync(ct);
        var entities = await query.Include(x => x.PuzzleType).OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var availableQueue = await _db.ScramblePoolItems.AsNoTracking()
            .Where(x => x.Status == "AVAILABLE" && x.IsValidated)
            .Select(x => new { x.Id, x.CompetitionMode, x.PuzzleTypeId, x.ApprovedAt, x.CreatedAt })
            .ToListAsync(ct);
        var queuePositions = availableQueue
            .GroupBy(x => new { x.CompetitionMode, x.PuzzleTypeId })
            .SelectMany(group => group
                .OrderBy(x => x.ApprovedAt ?? x.CreatedAt)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Select((item, index) => new { item.Id, Position = index + 1 }))
            .ToDictionary(x => x.Id, x => x.Position);

        return new ScramblePoolPageDto(
            entities.Select(x => ToDto(x, queuePositions.GetValueOrDefault(x.Id))).ToList(),
            total, page, pageSize);
    }

    public async Task<IReadOnlyList<ScramblePoolItemDto>> GenerateAsync(GenerateScramblesRequestDto request,
        Guid actorUserId, CancellationToken ct = default)
    {
        if (request.Count is < 1 or > 500) throw new ArgumentException("Scramble count must be between 1 and 500.");
        var mode = NormalizeMode(request.CompetitionMode);
        var puzzle = await _db.PuzzleTypes.SingleOrDefaultAsync(x => x.Id == request.PuzzleTypeId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Active puzzle type not found.");
        var created = new List<ScramblePoolItem>();
        var knownHashes = (await _db.ScramblePoolItems.AsNoTracking()
            .Where(x => x.CompetitionMode == mode && x.PuzzleTypeId == puzzle.Id)
            .Select(x => x.SequenceHash).ToListAsync(ct)).ToHashSet();
        var generationAttempts = 0;
        var maxGenerationAttempts = Math.Max(2_000, request.Count * 100);
        while (created.Count < request.Count)
        {
            if (++generationAttempts > maxGenerationAttempts)
                throw new InvalidOperationException(
                    $"Unable to generate {request.Count} unique two-move scrambles. " +
                    "This competition mode and puzzle type may be near its combination limit; reduce the requested count.");
            var sequence = NormalizeSequence(_generator.GenerateScramble(puzzle.Code, puzzle.ScrambleLength));
            var hash = Hash(sequence);
            if (!knownHashes.Add(hash)) continue;
            created.Add(CreateItem(mode, puzzle, sequence, hash, "CUBENEXUS_CRYPTO_V1", request.Notes,
                actorUserId, request.AutoApprove));
        }
        _db.ScramblePoolItems.AddRange(created);
        foreach (var item in created) AddAudit(item.Id, request.AutoApprove ? "GENERATED_AND_APPROVED" : "GENERATED", actorUserId);
        await _db.SaveChangesAsync(ct);
        return created.Select(x => ToDto(x, puzzle)).ToList();
    }

    public async Task<IReadOnlyList<ScramblePoolItemDto>> ImportAsync(ImportScramblesRequestDto request,
        Guid actorUserId, CancellationToken ct = default)
    {
        var mode = NormalizeMode(request.CompetitionMode);
        var puzzle = await _db.PuzzleTypes.SingleOrDefaultAsync(x => x.Id == request.PuzzleTypeId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Active puzzle type not found.");
        if (request.Sequences.Count is < 1 or > 1000) throw new ArgumentException("The import must contain between 1 and 1,000 scrambles.");
        var rows = request.Sequences.Select(NormalizeSequence).Distinct().ToList();
        var hashes = rows.Select(Hash).ToList();
        var existing = (await _db.ScramblePoolItems.AsNoTracking().Where(x => x.CompetitionMode == mode &&
            x.PuzzleTypeId == puzzle.Id && hashes.Contains(x.SequenceHash)).Select(x => x.SequenceHash).ToListAsync(ct)).ToHashSet();
        if (existing.Count > 0) throw new InvalidOperationException("At least one imported scramble already exists in this pool.");
        var created = rows.Select(sequence => CreateItem(mode, puzzle, sequence, Hash(sequence), "ADMIN_IMPORT",
            request.Notes, actorUserId, false)).ToList();
        _db.ScramblePoolItems.AddRange(created);
        foreach (var item in created) AddAudit(item.Id, "IMPORTED", actorUserId);
        await _db.SaveChangesAsync(ct);
        return created.Select(x => ToDto(x, puzzle)).ToList();
    }

    public async Task<ScramblePoolItemDto> ApproveAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await FindItemAsync(id, ct);
        if (item.Status != "DRAFT") throw new InvalidOperationException("Only draft scrambles can be approved.");
        ValidateSequence(item.Sequence, item.PuzzleType.Code is not ("222" or "333"));
        item.IsValidated = true; item.Status = "AVAILABLE"; item.ApprovedBy = actorUserId; item.ApprovedAt = DateTime.UtcNow;
        AddAudit(item.Id, "APPROVED", actorUserId);
        await _db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<ScramblePoolItemDto> RetireAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await FindItemAsync(id, ct);
        if (item.Status is not ("DRAFT" or "AVAILABLE"))
            throw new InvalidOperationException("Reserved or used scrambles cannot be retired.");
        item.Status = "RETIRED"; AddAudit(item.Id, "RETIRED", actorUserId);
        await _db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<ScrambleGenerationModeDto> GetScrambleGenerationModeAsync(string competitionMode,
        CancellationToken ct = default)
    {
        var normalizedMode = NormalizeMode(competitionMode);
        var setting = await _db.ScrambleGenerationSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompetitionMode == normalizedMode, ct);

        return setting == null
            ? new ScrambleGenerationModeDto(normalizedMode, "MANUAL", null, null)
            : new ScrambleGenerationModeDto(setting.CompetitionMode, setting.GenerationMode,
                setting.UpdatedBy, setting.UpdatedAt);
    }

    public async Task<ScrambleGenerationModeDto> SetScrambleGenerationModeAsync(string competitionMode,
        string mode, Guid actorUserId, CancellationToken ct = default)
    {
        var normalizedMode = NormalizeMode(competitionMode);
        var upper = (mode ?? string.Empty).Trim().ToUpperInvariant();
        if (upper != "MANUAL" && upper != "AUTO")
        {
            throw new ArgumentException("Generation mode must be MANUAL or AUTO.");
        }

        var setting = await _db.ScrambleGenerationSettings
            .SingleOrDefaultAsync(x => x.CompetitionMode == normalizedMode, ct);
        if (setting == null)
        {
            setting = new ScrambleGenerationSetting { CompetitionMode = normalizedMode };
            _db.ScrambleGenerationSettings.Add(setting);
        }

        setting.GenerationMode = upper;
        setting.UpdatedBy = actorUserId;
        setting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new ScrambleGenerationModeDto(setting.CompetitionMode, setting.GenerationMode,
            setting.UpdatedBy, setting.UpdatedAt);
    }

    private async Task<ScramblePoolItem> FindItemAsync(Guid id, CancellationToken ct) =>
        await _db.ScramblePoolItems.Include(x => x.PuzzleType).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new KeyNotFoundException("Scramble not found.");

    private ScramblePoolItem CreateItem(string mode, PuzzleType puzzle, string sequence, string hash,
        string generator, string? notes, Guid actor, bool approved)
    {
        ValidateSequence(sequence, puzzle.Code is not ("222" or "333"));
        var now = DateTime.UtcNow;
        return new ScramblePoolItem
        {
            // Set only the FK. The PuzzleType instance may already be tracked by
            // this DbContext; assigning this detached navigation would make EF
            // attach a second instance with the same key during AUTO generation.
            Id = Guid.NewGuid(), CompetitionMode = mode, PuzzleTypeId = puzzle.Id,
            Sequence = sequence, SequenceHash = hash,
            ExpectedStateJson = puzzle.Code == "333" ? JsonSerializer.Serialize(RubikCubeStateValidator.BuildExpectedCubeStateForScramble(sequence)) : null,
            Status = approved ? "AVAILABLE" : "DRAFT", IsValidated = true, GeneratorName = generator,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), CreatedBy = actor, CreatedAt = now,
            ApprovedBy = approved ? actor : null, ApprovedAt = approved ? now : null
        };
    }

    private void AddAudit(Guid itemId, string action, Guid? actor, string? targetType = null, Guid? targetId = null) =>
        _db.ScramblePoolAuditLogs.Add(new ScramblePoolAuditLog { Id = Guid.NewGuid(), ScramblePoolItemId = itemId,
            Action = action, ActorUserId = actor, TargetType = targetType, TargetId = targetId, CreatedAt = DateTime.UtcNow });

    private static string NormalizeMode(string mode)
    {
        var value = mode.Trim().ToUpperInvariant();
        if (!Modes.Contains(value)) throw new ArgumentException("CompetitionMode must be ONLINE_MATCH, OFFLINE, or ONLINE_ASYNC.");
        return value;
    }

    private static string NormalizeSequence(string sequence)
    {
        var value = string.Join(' ', (sequence ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        ValidateSequence(value);
        return value;
    }

    private static void ValidateSequence(string sequence, bool allowWideMoves = true)
    {
        var moves = sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (moves.Length == 0 || moves.Any(move => !MovePattern.IsMatch(move)))
            throw new ArgumentException($"Invalid scramble: '{sequence}'. Use standard cube notation such as R, U2, Rw, or 3Rw'.");
        if (moves.Length > 2)
            throw new ArgumentException("Invalid scramble: each scramble may contain at most two moves.");
        if (!allowWideMoves && moves.Any(move => move.Contains('w') || char.IsDigit(move[0]) && move.Length > 1 && char.IsLetter(move[1])))
            throw new ArgumentException("2x2 and 3x3 scrambles do not support wide or multi-layer moves.");
        for (var i = 1; i < moves.Length; i++)
            if (moves[i].First(char.IsLetter) == moves[i - 1].First(char.IsLetter))
                throw new ArgumentException("Invalid scramble: consecutive moves cannot use the same face.");
    }

    private static string Hash(string sequence) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sequence))).ToLowerInvariant();
    private static ScramblePoolItemDto ToDto(ScramblePoolItem x, PuzzleType puzzle, int? queuePosition = null) => new(x.Id, x.CompetitionMode, x.PuzzleTypeId,
        puzzle.Code, puzzle.Name, x.Sequence, x.Status, x.IsValidated, x.GeneratorName, x.Notes,
        x.CreatedAt, x.ApprovedAt, x.AssignedTargetType, x.AssignedTargetId, x.AssignedAt, queuePosition);

    private static ScramblePoolItemDto ToDto(ScramblePoolItem x, int? queuePosition = null) => new(x.Id, x.CompetitionMode, x.PuzzleTypeId,
        x.PuzzleType.Code, x.PuzzleType.Name, x.Sequence, x.Status, x.IsValidated, x.GeneratorName, x.Notes,
        x.CreatedAt, x.ApprovedAt, x.AssignedTargetType, x.AssignedTargetId, x.AssignedAt, queuePosition);
}
