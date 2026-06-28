using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class CreateMatchRecordingUploadUrlUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchVideoEvidenceRepository _videoRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IRecordingStorageService _storageService;
    private readonly IUnitOfWork _uow;

    public CreateMatchRecordingUploadUrlUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchVideoEvidenceRepository videoRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IRecordingStorageService storageService,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _videoRepo = videoRepo;
        _auditRepo = auditRepo;
        _storageService = storageService;
        _uow = uow;
    }

    public async Task<MatchRecordingUploadUrlResponseDto> ExecuteAsync(
        Guid matchId,
        Guid userId,
        CreateMatchRecordingUploadUrlRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await MatchRecordingPolicy.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        MatchRecordingPolicy.ValidateUploadEligibility(match, userId);

        var normalizedContentType = MatchRecordingPolicy.NormalizeContentType(request.ContentType);
        var extension = MatchRecordingPolicy.NormalizeExtension(request.FileExtension, normalizedContentType);
        var current = await _videoRepo.GetLatestAsync(matchId, userId);

        if (current?.RecordingStatus == nameof(MatchRecordingStatus.Ready) && !string.IsNullOrWhiteSpace(current.ObjectKey))
            throw new ConflictException("Recording is already completed for this player and match.");

        var isNewEvidence = current == null;
        if (isNewEvidence)
        {
            current = new OnlineMatchVideoEvidence
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                PlayerId = userId,
                RecordingStatus = nameof(MatchRecordingStatus.Pending),
                Status = nameof(MatchRecordingStatus.Pending),
                SourceType = "R2_DIRECT_UPLOAD"
            };
            await _videoRepo.AddAsync(current);
        }

        var evidence = current!;
        var durationSeconds = MatchRecordingPolicy.NormalizeDurationSeconds(request.DurationSeconds);
        var objectKey = !string.IsNullOrWhiteSpace(evidence.ObjectKey) && evidence.RecordingStatus == nameof(MatchRecordingStatus.Uploading)
            ? evidence.ObjectKey!
            : MatchRecordingPolicy.BuildObjectKey(matchId, extension);

        evidence.ObjectKey = objectKey;
        evidence.ContentType = normalizedContentType;
        evidence.MimeType = normalizedContentType;
        evidence.FileUrl = objectKey;
        evidence.DurationSeconds = durationSeconds;
        evidence.DurationMs = durationSeconds.HasValue ? (long)Math.Round(durationSeconds.Value * 1000d) : null;
        evidence.RecordedAt = MatchRecordingPolicy.NormalizeRecordedAt(request.RecordedAt, match.StartedAt);
        evidence.RecordingStatus = nameof(MatchRecordingStatus.Uploading);
        evidence.Status = nameof(MatchRecordingStatus.Uploading);
        evidence.UploadedAt = null;
        evidence.FileSizeBytes = null;

        if (!isNewEvidence)
            _videoRepo.Update(evidence);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(matchId, userId, "MATCH_RECORDING_UPLOAD_URL_CREATED", new
        {
            evidence.Id,
            evidence.ObjectKey,
            evidence.ContentType,
            evidence.RecordedAt,
            evidence.DurationSeconds
        }));
        await _uow.SaveChangesAsync();

        var upload = await _storageService.CreateUploadUrlAsync(
            evidence.ObjectKey,
            normalizedContentType,
            cancellationToken);

        return new MatchRecordingUploadUrlResponseDto
        {
            MatchId = matchId,
            PlayerId = userId,
            VideoEvidenceId = evidence.Id,
            RecordingStatus = evidence.RecordingStatus,
            ObjectKey = evidence.ObjectKey,
            ContentType = normalizedContentType,
            UploadUrl = upload.Url.ToString(),
            ExpiresAt = upload.ExpiresAtUtc,
            RequiredHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = normalizedContentType
            }
        };
    }
}

public class CompleteMatchRecordingUploadUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchVideoEvidenceRepository _videoRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IRecordingStorageService _storageService;
    private readonly IUnitOfWork _uow;

    public CompleteMatchRecordingUploadUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchVideoEvidenceRepository videoRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IRecordingStorageService storageService,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _videoRepo = videoRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _storageService = storageService;
        _uow = uow;
    }

    public async Task<MatchRecordingCompleteResponseDto> ExecuteAsync(
        Guid matchId,
        Guid userId,
        CompleteMatchRecordingUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var match = await MatchRecordingPolicy.RequireParticipantMatchAsync(_matchRepo, matchId, userId);
        MatchRecordingPolicy.ValidateCompleteEligibility(match, userId, request.ObjectKey);

        var current = await _videoRepo.GetLatestAsync(matchId, userId)
            ?? throw new KeyNotFoundException("Recording was not initialized for this player.");

        if (current.RecordingStatus == nameof(MatchRecordingStatus.Ready)
            && string.Equals(current.ObjectKey, request.ObjectKey, StringComparison.Ordinal))
        {
            return BuildCompleteResponse(current, "Recording already marked as ready.");
        }

        if (!string.Equals(current.ObjectKey, request.ObjectKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Complete request does not match the active recording object key.");

        var metadata = await _storageService.GetObjectMetadataAsync(request.ObjectKey, cancellationToken);
        if (metadata == null)
        {
            current.RecordingStatus = nameof(MatchRecordingStatus.Failed);
            current.Status = nameof(MatchRecordingStatus.Failed);
            _videoRepo.Update(current);
            await _uow.SaveChangesAsync();
            throw new InvalidOperationException("Uploaded recording was not found in storage.");
        }

        var normalizedContentType = MatchRecordingPolicy.NormalizeContentType(metadata.ContentType);
        if (metadata.FileSizeBytes <= 0)
            throw new InvalidOperationException("Uploaded recording is empty.");

        current.ObjectKey = metadata.ObjectKey;
        current.ContentType = normalizedContentType;
        current.MimeType = normalizedContentType;
        current.FileUrl = metadata.ObjectKey;
        current.FileSizeBytes = metadata.FileSizeBytes;
        current.DurationSeconds = MatchRecordingPolicy.NormalizeDurationSeconds(request.DurationSeconds ?? current.DurationSeconds);
        current.DurationMs = current.DurationSeconds.HasValue ? (long)Math.Round(current.DurationSeconds.Value * 1000d) : current.DurationMs;
        current.RecordedAt ??= match.StartedAt ?? DateTime.UtcNow;
        current.UploadedAt = DateTime.UtcNow;
        current.RecordingStatus = nameof(MatchRecordingStatus.Ready);
        current.Status = nameof(MatchRecordingStatus.Ready);

        _videoRepo.Update(current);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(matchId, userId, "MATCH_RECORDING_UPLOAD_COMPLETED", new
        {
            current.Id,
            current.ObjectKey,
            current.ContentType,
            current.FileSizeBytes,
            current.DurationSeconds,
            current.RecordedAt,
            current.UploadedAt
        }));
        await _uow.SaveChangesAsync();

        var response = BuildCompleteResponse(current, "Recording upload completed.");
        await _notifier.NotifyVideoEvidenceUploadedAsync(matchId, response);
        return response;
    }

    private static MatchRecordingCompleteResponseDto BuildCompleteResponse(OnlineMatchVideoEvidence evidence, string message)
        => new()
        {
            Message = message,
            MatchId = evidence.MatchId,
            PlayerId = evidence.PlayerId,
            VideoEvidenceId = evidence.Id,
            ObjectKey = evidence.ObjectKey ?? string.Empty,
            ContentType = evidence.ContentType ?? evidence.MimeType ?? "application/octet-stream",
            FileSizeBytes = evidence.FileSizeBytes ?? 0,
            DurationSeconds = evidence.DurationSeconds,
            RecordingStatus = evidence.RecordingStatus,
            RecordedAt = evidence.RecordedAt,
            UploadedAt = evidence.UploadedAt
        };
}

public class GetMatchRecordingPlaybackUrlUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchVideoEvidenceRepository _videoRepo;
    private readonly IRecordingStorageService _storageService;

    public GetMatchRecordingPlaybackUrlUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineMatchVideoEvidenceRepository videoRepo,
        IRecordingStorageService storageService)
    {
        _matchRepo = matchRepo;
        _videoRepo = videoRepo;
        _storageService = storageService;
    }

    public async Task<MatchRecordingPlaybackResponseDto> ExecuteAsync(
        Guid matchId,
        Guid userId,
        bool isAdminLike,
        CancellationToken cancellationToken = default)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (!isAdminLike && match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not allowed to view this match recording.");

        var evidences = (await _videoRepo.GetByMatchAsync(matchId))
            .Where(item => item.RecordingStatus == nameof(MatchRecordingStatus.Ready) && !string.IsNullOrWhiteSpace(item.ObjectKey))
            .OrderBy(item => item.PlayerId)
            .ToList();

        if (evidences.Count == 0)
            throw new KeyNotFoundException("No ready recording found for this match.");

        var recordings = new List<MatchRecordingPlaybackItemDto>(evidences.Count);
        foreach (var item in evidences)
        {
            var url = await _storageService.CreatePlaybackUrlAsync(item.ObjectKey!, cancellationToken);

            recordings.Add(new MatchRecordingPlaybackItemDto
            {
                VideoEvidenceId = item.Id,
                PlayerId = item.PlayerId,
                ObjectKey = item.ObjectKey!,
                ContentType = item.ContentType ?? item.MimeType ?? "application/octet-stream",
                FileSizeBytes = item.FileSizeBytes ?? 0,
                DurationSeconds = item.DurationSeconds,
                RecordingStatus = item.RecordingStatus,
                RecordedAt = item.RecordedAt,
                UploadedAt = item.UploadedAt,
                PlaybackUrl = url.Url.ToString(),
                ExpiresAt = url.ExpiresAtUtc
            });
        }

        return new MatchRecordingPlaybackResponseDto
        {
            MatchId = matchId,
            Recordings = recordings
        };
    }
}

internal static class MatchRecordingPolicy
{
    private const double MaxRecordingDurationSeconds = 600d;

    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/webm"] = "webm",
        ["video/mp4"] = "mp4"
    };

    public static async Task<OnlineMatch> RequireParticipantMatchAsync(IOnlineMatchRepository matchRepo, Guid matchId, Guid userId)
    {
        var match = await matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        return match;
    }

    public static void ValidateUploadEligibility(OnlineMatch match, Guid userId)
    {
        if (match.StartedAt == null)
            throw new InvalidOperationException("Recording upload is only available after the match has started.");

        var recordingStarted = match.Player1Id == userId ? match.Player1RecordingStarted : match.Player2RecordingStarted;
        if (!recordingStarted)
            throw new InvalidOperationException("Recording has not been marked as ready for this player.");
    }

    public static void ValidateCompleteEligibility(OnlineMatch match, Guid userId, string objectKey)
    {
        ValidateUploadEligibility(match, userId);
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("objectKey is required.");
        if (!objectKey.StartsWith($"matches/{match.Id}/recording-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("objectKey does not belong to this match.");
    }

    public static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("contentType is required.");

        var normalized = contentType.Trim().ToLowerInvariant();
        if (normalized.Contains(';'))
            normalized = normalized.Split(';', 2, StringSplitOptions.TrimEntries)[0];

        if (!AllowedContentTypes.ContainsKey(normalized))
            throw new ArgumentException("Only video/webm and video/mp4 are supported.");

        return normalized;
    }

    public static string NormalizeExtension(string? extension, string contentType)
    {
        var expected = AllowedContentTypes[contentType];
        if (string.IsNullOrWhiteSpace(extension))
            return expected;

        var normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
        if (normalized != expected)
            throw new ArgumentException($"fileExtension must match contentType {contentType}.");

        return normalized;
    }

    public static string BuildObjectKey(Guid matchId, string extension)
        => $"matches/{matchId}/recording-{Guid.NewGuid():N}.{extension}";

    public static double? NormalizeDurationSeconds(double? durationSeconds)
    {
        if (durationSeconds == null)
            return null;
        if (durationSeconds <= 0)
            throw new ArgumentException("durationSeconds must be positive.");
        if (durationSeconds > MaxRecordingDurationSeconds)
            throw new ArgumentException("durationSeconds exceeds the 10 minute match recording limit.");
        return Math.Round(durationSeconds.Value, 3);
    }

    public static DateTime NormalizeRecordedAt(DateTime? recordedAt, DateTime? matchStartedAt)
    {
        var value = recordedAt?.ToUniversalTime() ?? matchStartedAt ?? DateTime.UtcNow;
        if (value > DateTime.UtcNow.AddMinutes(1))
            throw new ArgumentException("recordedAt cannot be in the far future.");
        return value;
    }
}
