using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class CreateFraudReportUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IFraudReportRepository _fraudRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public CreateFraudReportUseCase(
        IOnlineMatchRepository matchRepo,
        IFraudReportRepository fraudRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _fraudRepo = fraudRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<FraudReportDto> ExecuteAsync(Guid userId, Guid matchId, CreateFraudReportRequest req)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a participant in this match.");

        var accusedUserId = match.Player1Id == userId ? match.Player2Id : match.Player1Id;
        var report = new FraudReport
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ReporterUserId = userId,
            ReportedUserId = accusedUserId,
            ReasonCode = "PLAYER_REPORT",
            Description = req.Description,
            EvidenceUrl = req.EvidenceUrl,
            StatusCode = "OPEN",
            ReviewScope = "WHOLE_MATCH",
            CreatedAt = DateTime.UtcNow
        };

        await _fraudRepo.AddAsync(report);
        match.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
        match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
        {
            code = "FRAUD_REPORT_CREATED",
            reportId = report.Id,
            reporterUserId = userId
        });
        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(matchId, userId, "FRAUD_REPORT_CREATED", new
        {
            reportId = report.Id,
            description = req.Description
        }));
        await _uow.SaveChangesAsync();

        await _notifier.NotifyFraudReportCreatedAsync(matchId, new
        {
            reportId = report.Id,
            matchId = report.MatchId,
            reporterUserId = report.ReporterUserId,
            reportedUserId = report.ReportedUserId,
            statusCode = report.StatusCode,
            createdAt = report.CreatedAt
        });

        return FraudReportMapper.ToDto(report);
    }
}

public class GetPendingFraudReportsUseCase
{
    private readonly IFraudReportRepository _fraudRepo;

    public GetPendingFraudReportsUseCase(IFraudReportRepository fraudRepo)
    {
        _fraudRepo = fraudRepo;
    }

    public async Task<List<FraudReportDto>> ExecuteAsync()
        => (await _fraudRepo.GetPendingReportsAsync()).Select(FraudReportMapper.ToDto).ToList();
}

public class ReviewFraudReportUseCase
{
    private readonly IFraudReportRepository _fraudRepo;
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAiCheckRepository _aiCheckRepo;
    private readonly IOnlineMatchVideoEvidenceRepository _videoEvidenceRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public ReviewFraudReportUseCase(
        IFraudReportRepository fraudRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAiCheckRepository aiCheckRepo,
        IOnlineMatchVideoEvidenceRepository videoEvidenceRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _fraudRepo = fraudRepo;
        _matchRepo = matchRepo;
        _aiCheckRepo = aiCheckRepo;
        _videoEvidenceRepo = videoEvidenceRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<FraudReportDto> ExecuteAsync(Guid reviewerId, Guid reportId, ReviewFraudReportRequest request)
    {
        var report = await _fraudRepo.GetByIdAsync(reportId);
        if (report == null)
            throw new KeyNotFoundException("Fraud report not found.");
        if (report.StatusCode is not "OPEN" and not "REVIEWING" and not "PENDING")
            throw new ConflictException("Only OPEN/REVIEWING fraud reports can be resolved.");

        var decision = string.IsNullOrWhiteSpace(request.VerdictCode)
            ? FraudVerdict.INCONCLUSIVE.ToString()
            : request.VerdictCode;
        report.StatusCode = decision == FraudVerdict.GUILTY.ToString()
            ? "RESOLVED_VALID"
            : decision == FraudVerdict.INNOCENT.ToString()
                ? "RESOLVED_INVALID"
                : "INCONCLUSIVE";
        report.VerdictCode = decision;
        report.Decision = decision;
        report.AdminNote = request.AdminNote;
        report.ReviewedBy = reviewerId;
        report.ReviewedAt = DateTime.UtcNow;
        report.ResolvedByAdminId = reviewerId;
        report.ResolvedAt = DateTime.UtcNow;

        var match = await _matchRepo.GetByIdAsync(report.MatchId)
            ?? throw new KeyNotFoundException("Match not found.");
        if (decision == FraudVerdict.GUILTY.ToString())
        {
            match.StatusCode = OnlineMatchStatus.COMPLETED.ToString();
            match.Phase = "COMPLETED";
            match.Outcome = report.ReportedUserId == match.Player1Id
                ? OnlineMatchOutcome.PLAYER2_WIN.ToString()
                : OnlineMatchOutcome.PLAYER1_WIN.ToString();
            match.WinnerId = match.Outcome == OnlineMatchOutcome.PLAYER1_WIN.ToString() ? match.Player1Id : match.Player2Id;
            match.EndedAt = DateTime.UtcNow;
        }
        else if (decision == FraudVerdict.INNOCENT.ToString())
        {
            if (match.StatusCode == OnlineMatchStatus.NEEDS_REVIEW.ToString())
            {
                match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
                {
                    code = "FRAUD_REPORT_RESOLVED_INVALID",
                    reportId = report.Id
                });
            }
        }

        _fraudRepo.Update(report);
        _matchRepo.Update(match);
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, reviewerId, "FRAUD_REPORT_RESOLVED", new
        {
            reportId = report.Id,
            decision
        }));
        await _uow.SaveChangesAsync();

        await _notifier.NotifyFraudReportResolvedAsync(match.Id, new
        {
            reportId = report.Id,
            matchId = match.Id,
            decision,
            statusCode = report.StatusCode,
            resolvedAt = report.ResolvedAt
        });

        return FraudReportMapper.ToDto(report);
    }
}

public class GetFraudReportDetailUseCase
{
    private readonly IFraudReportRepository _fraudRepo;
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineMatchAiCheckRepository _aiCheckRepo;
    private readonly IOnlineMatchVideoEvidenceRepository _videoEvidenceRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;

    public GetFraudReportDetailUseCase(
        IFraudReportRepository fraudRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAiCheckRepository aiCheckRepo,
        IOnlineMatchVideoEvidenceRepository videoEvidenceRepo,
        IOnlineMatchAuditLogRepository auditRepo)
    {
        _fraudRepo = fraudRepo;
        _matchRepo = matchRepo;
        _aiCheckRepo = aiCheckRepo;
        _videoEvidenceRepo = videoEvidenceRepo;
        _auditRepo = auditRepo;
    }

    public async Task<FraudReportDetailDto> ExecuteAsync(Guid reportId)
    {
        var report = await _fraudRepo.GetByIdAsync(reportId)
            ?? throw new KeyNotFoundException("Fraud report not found.");
        var match = await _matchRepo.GetByIdAsync(report.MatchId)
            ?? throw new KeyNotFoundException("Match not found.");

        var aiChecks = await _aiCheckRepo.GetByMatchAsync(match.Id);
        var evidences = await _videoEvidenceRepo.GetByMatchAsync(match.Id);
        var auditLogs = await _auditRepo.GetByMatchAsync(match.Id);

        return new FraudReportDetailDto
        {
            Report = FraudReportMapper.ToDto(report),
            Match = OnlineArenaFlowHelpers.BuildMatchDetail(match, match.Player1Id, true),
            AiChecks = aiChecks.Select(item => new OnlineMatchAiCheckDto
            {
                Id = item.Id,
                MatchId = item.MatchId,
                PlayerId = item.PlayerId,
                CheckType = item.CheckType,
                Status = item.Status,
                Confidence = item.Confidence,
                EvidenceImageUrl = item.EvidenceImageUrl,
                VideoEvidenceId = item.VideoEvidenceId,
                ModelVersion = item.ModelVersion,
                ResultJson = item.ResultJson,
                FailureReason = item.FailureReason,
                CreatedAt = item.CreatedAt
            }).ToList(),
            VideoEvidences = evidences.Select(item => new OnlineMatchVideoEvidenceDto
            {
                Id = item.Id,
                MatchId = item.MatchId,
                PlayerId = item.PlayerId,
                ObjectKey = item.ObjectKey,
                ContentType = item.ContentType,
                FileSizeBytes = item.FileSizeBytes,
                DurationSeconds = item.DurationSeconds,
                RecordingStatus = item.RecordingStatus,
                RecordedAt = item.RecordedAt,
                FileUrl = item.FileUrl,
                ThumbnailUrl = item.ThumbnailUrl,
                DurationMs = item.DurationMs,
                RecordingStartedAt = item.RecordingStartedAt,
                RecordingEndedAt = item.RecordingEndedAt,
                UploadedAt = item.UploadedAt,
                Status = item.Status,
                Checksum = item.Checksum,
                SourceType = item.SourceType,
                MimeType = item.MimeType
            }).ToList(),
            AuditLogs = auditLogs.Select(item => new OnlineMatchAuditLogDto
            {
                Id = item.Id,
                MatchId = item.MatchId,
                PlayerId = item.PlayerId,
                EventType = item.EventType,
                PayloadJson = item.PayloadJson,
                CreatedAt = item.CreatedAt
            }).ToList()
        };
    }
}

internal static class FraudReportMapper
{
    public static FraudReportDto ToDto(FraudReport report)
        => new()
        {
            Id = report.Id,
            MatchId = report.MatchId,
            ReporterUserId = report.ReporterUserId,
            ReportedUserId = report.ReportedUserId,
            ReasonCode = report.ReasonCode,
            Description = report.Description,
            EvidenceUrl = report.EvidenceUrl,
            StatusCode = report.StatusCode,
            ReviewScope = report.ReviewScope,
            Decision = report.Decision,
            PenaltyAction = report.PenaltyAction,
            ResolvedByAdminId = report.ResolvedByAdminId,
            ResolvedAt = report.ResolvedAt,
            ReviewedBy = report.ReviewedBy,
            VerdictCode = report.VerdictCode,
            AdminNote = report.AdminNote,
            CreatedAt = report.CreatedAt,
            ReviewedAt = report.ReviewedAt
        };
}
