using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using CubeNexus.Domain.Services;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class CreateFraudReportUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IFraudReportRepository _fraudRepo;
    private readonly IOnlineMatchAuditLogRepository _auditRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IAdminNotificationService _adminNotifications;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IUnitOfWork _uow;

    public CreateFraudReportUseCase(
        IOnlineMatchRepository matchRepo,
        IFraudReportRepository fraudRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IAdminNotificationService adminNotifications,
        IRealtimeNotifier realtimeNotifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _fraudRepo = fraudRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _adminNotifications = adminNotifications;
        _realtimeNotifier = realtimeNotifier;
        _uow = uow;
    }

    public async Task<FraudReportDto> ExecuteAsync(Guid userId, Guid matchId, CreateFraudReportRequest req)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        // 3.1 Kiểm tra Match đã hoàn thành
        if (match.StatusCode != OnlineMatchStatus.COMPLETED.ToString())
            throw new InvalidOperationException("Only completed matches can be reported.");

        // 3.2 Kiểm tra quyền (phải là người chơi trong trận đấu)
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("You are not a participant in this match.");

        // 3.3 Kiểm tra Report Deadline (Trong vòng 24 giờ sau khi trận đấu kết thúc)
        var matchCompletedAt = match.EndedAt ?? match.CreatedAt;
        if (DateTime.UtcNow > matchCompletedAt.AddHours(24))
            throw new InvalidOperationException("Fraud report deadline (24 hours after match completion) has expired.");

        // 3.4 Kiểm tra chống Spam (Mỗi người chơi chỉ được gửi 1 report cho mỗi trận đấu)
        var existingReports = await _fraudRepo.GetByMatchAsync(matchId);
        if (existingReports.Any(r => r.ReporterUserId == userId))
            throw new ConflictException("You have already submitted a fraud report for this match.");

        var accusedUserId = match.Player1Id == userId ? match.Player2Id : match.Player1Id;
        var report = new FraudReport
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ReporterUserId = userId,
            ReportedUserId = accusedUserId,
            ReasonCode = req.FraudType,
            FraudType = string.IsNullOrWhiteSpace(req.FraudType) ? "OTHER" : req.FraudType,
            TimestampText = string.IsNullOrWhiteSpace(req.TimestampText) ? "00:00" : req.TimestampText,
            TimestampSeconds = req.TimestampSeconds < 0 ? 0 : req.TimestampSeconds,
            Description = req.Description,
            EvidenceUrl = req.EvidenceUrl,
            EvidenceScreenshotUrl = req.EvidenceScreenshotUrl,
            StatusCode = "OPEN",
            ReviewScope = "WHOLE_MATCH",
            CreatedAt = DateTime.UtcNow
        };

        await _fraudRepo.AddAsync(report);

        // Giai đoạn 4: Match giữ nguyên trạng thái COMPLETED, lưu audit log
        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(matchId, userId, "FRAUD_REPORT_CREATED", new
        {
            reportId = report.Id,
            fraudType = report.FraudType,
            timestampText = report.TimestampText,
            timestampSeconds = report.TimestampSeconds,
            description = req.Description
        }));

        await _uow.SaveChangesAsync();

        // Match-room SignalR (players still in lobby)
        await _notifier.NotifyFraudReportCreatedAsync(matchId, new
        {
            reportId = report.Id,
            matchId = report.MatchId,
            reporterUserId = report.ReporterUserId,
            reportedUserId = report.ReportedUserId,
            fraudType = report.FraudType,
            timestampText = report.TimestampText,
            timestampSeconds = report.TimestampSeconds,
            statusCode = report.StatusCode,
            createdAt = report.CreatedAt
        });

        // Persist + broadcast admin inbox notifications (TournamentHub)
        var payloadJson = JsonSerializer.Serialize(new
        {
            reportId = report.Id,
            matchId = report.MatchId,
            reporterUserId = report.ReporterUserId,
            reportedUserId = report.ReportedUserId,
            fraudType = report.FraudType,
            timestampText = report.TimestampText,
            timestampSeconds = report.TimestampSeconds,
            statusCode = report.StatusCode
        });
        var title = "New online fraud report";
        var body =
            $"A player filed a PvP 1v1 fraud report ({report.FraudType}) at {report.TimestampText}. Match {report.MatchId.ToString()[..8]}…";
        var adminDto = await _adminNotifications.NotifyAdminsAsync(
            "FRAUD_REPORT_CREATED",
            title,
            body,
            payloadJson);
        if (adminDto != null)
        {
            await _realtimeNotifier.BroadcastAdminNotificationAsync(new
            {
                id = adminDto.Id,
                typeCode = adminDto.TypeCode,
                title = adminDto.Title,
                body = adminDto.Body,
                payload = adminDto.Payload,
                isRead = adminDto.IsRead,
                createdAt = adminDto.CreatedAt
            });
        }

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
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IEloHistoryRepository _eloHistoryRepo;
    private readonly IEloCalculator _eloCalc;
    private readonly IAdminNotificationService _adminNotifications;
    private readonly IUnitOfWork _uow;

    public ReviewFraudReportUseCase(
        IFraudReportRepository fraudRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineMatchAiCheckRepository aiCheckRepo,
        IOnlineMatchVideoEvidenceRepository videoEvidenceRepo,
        IOnlineMatchAuditLogRepository auditRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IOnlineProfileRepository profileRepo,
        IEloHistoryRepository eloHistoryRepo,
        IEloCalculator eloCalc,
        IAdminNotificationService adminNotifications,
        IUnitOfWork uow)
    {
        _fraudRepo = fraudRepo;
        _matchRepo = matchRepo;
        _aiCheckRepo = aiCheckRepo;
        _videoEvidenceRepo = videoEvidenceRepo;
        _auditRepo = auditRepo;
        _notifier = notifier;
        _profileRepo = profileRepo;
        _eloHistoryRepo = eloHistoryRepo;
        _eloCalc = eloCalc;
        _adminNotifications = adminNotifications;
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
            : request.VerdictCode.ToUpperInvariant();

        report.StatusCode = "RESOLVED";
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
            var cheaterId = report.ReportedUserId;
            var victimId = report.ReporterUserId;

            // Cheater -> DNF, Victim -> Winner
            if (cheaterId == match.Player1Id)
            {
                match.Player1IsDnf = true;
                match.Player1ResultStatus = "DNF";
                match.Player2ResultStatus = "VALID";
            }
            else
            {
                match.Player2IsDnf = true;
                match.Player2ResultStatus = "DNF";
                match.Player1ResultStatus = "VALID";
            }

            match.Outcome = victimId == match.Player1Id
                ? OnlineMatchOutcome.PLAYER1_WIN.ToString()
                : OnlineMatchOutcome.PLAYER2_WIN.ToString();
            match.WinnerId = victimId;
            match.StatusCode = OnlineMatchStatus.COMPLETED.ToString();
        }
        else if (decision == FraudVerdict.INCONCLUSIVE.ToString())
        {
            match.Outcome = OnlineMatchOutcome.DRAW.ToString();
            match.WinnerId = null;
            match.StatusCode = OnlineMatchStatus.COMPLETED.ToString();
        }

        // Recalculate ELO and update player profiles
        var p1Profile = await _profileRepo.GetProfileAsync(match.Player1Id, match.PuzzleTypeId);
        var p2Profile = await _profileRepo.GetProfileAsync(match.Player2Id, match.PuzzleTypeId);

        if (p1Profile != null && p2Profile != null)
        {
            // Revert old ELO delta if match was already scored previously
            if (match.Player1EloBefore.HasValue && match.Player1EloAfter.HasValue)
            {
                var oldP1Delta = match.Player1EloAfter.Value - match.Player1EloBefore.Value;
                p1Profile.EloStandard -= oldP1Delta;
            }
            if (match.Player2EloBefore.HasValue && match.Player2EloAfter.HasValue)
            {
                var oldP2Delta = match.Player2EloAfter.Value - match.Player2EloBefore.Value;
                p2Profile.EloStandard -= oldP2Delta;
            }

            var p1Score = match.Outcome == nameof(OnlineMatchOutcome.PLAYER1_WIN) ? 1.0m : match.Outcome == nameof(OnlineMatchOutcome.DRAW) ? 0.5m : 0.0m;
            var p2Score = match.Outcome == nameof(OnlineMatchOutcome.PLAYER2_WIN) ? 1.0m : match.Outcome == nameof(OnlineMatchOutcome.DRAW) ? 0.5m : 0.0m;

            var (p1EloAfter, p2EloAfter, p1Exp, p2Exp) = _eloCalc.Calculate(
                p1Profile.EloStandard,
                p1Profile.KFactorCurrentStandard,
                p1Score,
                p2Profile.EloStandard,
                p2Profile.KFactorCurrentStandard,
                p2Score
            );

            match.Player1EloBefore = p1Profile.EloStandard;
            match.Player2EloBefore = p2Profile.EloStandard;
            match.Player1EloAfter = p1EloAfter;
            match.Player2EloAfter = p2EloAfter;

            UpdateProfile(p1Profile, p1EloAfter, p1Score);
            UpdateProfile(p2Profile, p2EloAfter, p2Score);

            _profileRepo.Update(p1Profile);
            _profileRepo.Update(p2Profile);

            await _eloHistoryRepo.AddAsync(new EloHistory
            {
                Id = Guid.NewGuid(),
                OnlineProfileId = p1Profile.Id,
                MatchId = match.Id,
                EloBefore = p1Profile.EloStandard,
                EloAfter = p1EloAfter,
                Delta = p1EloAfter - p1Profile.EloStandard,
                KFactorUsed = p1Profile.KFactorCurrentStandard,
                ActualScore = p1Score,
                ExpectedScore = p1Exp,
                ReasonCode = $"FRAUD_VERDICT_{decision}",
                EloModeCode = "STANDARD",
                ChangedAt = DateTime.UtcNow
            });

            await _eloHistoryRepo.AddAsync(new EloHistory
            {
                Id = Guid.NewGuid(),
                OnlineProfileId = p2Profile.Id,
                MatchId = match.Id,
                EloBefore = p2Profile.EloStandard,
                EloAfter = p2EloAfter,
                Delta = p2EloAfter - p2Profile.EloStandard,
                KFactorUsed = p2Profile.KFactorCurrentStandard,
                ActualScore = p2Score,
                ExpectedScore = p2Exp,
                ReasonCode = $"FRAUD_VERDICT_{decision}",
                EloModeCode = "STANDARD",
                ChangedAt = DateTime.UtcNow
            });
        }

        _fraudRepo.Update(report);
        _matchRepo.Update(match);

        await _auditRepo.AddAsync(OnlineArenaAuditFactory.BuildAudit(match.Id, reviewerId, "FRAUD_REPORT_RESOLVED", new
        {
            reportId = report.Id,
            verdict = decision,
            adminNote = request.AdminNote
        }));

        await _uow.SaveChangesAsync();
        await _adminNotifications.MarkFraudReportResolvedAsync(report.Id);

        // Giai đoạn 10: Thông báo cho cả 2 người chơi qua SignalR
        await _notifier.NotifyFraudReportResolvedAsync(match.Id, new
        {
            reportId = report.Id,
            matchId = match.Id,
            verdict = decision,
            reporterId = report.ReporterUserId,
            cheaterId = report.ReportedUserId,
            adminNote = request.AdminNote,
            resolvedAt = report.ResolvedAt
        });

        return FraudReportMapper.ToDto(report);
    }

    private static void UpdateProfile(OnlineProfile profile, int newElo, decimal score)
    {
        profile.EloStandard = newElo;
        profile.PeakEloStandard = Math.Max(profile.PeakEloStandard, newElo);

        if (score == 1.0m) profile.TotalWinsStandard++;
        else if (score == 0.0m) profile.TotalLossesStandard++;
        else profile.TotalDrawsStandard++;

        profile.UpdatedAt = DateTime.UtcNow;
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
        var match = await _matchRepo.GetByIdWithPlayersAsync(report.MatchId)
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
            ReporterUserCode = report.ReportedByUser?.UserCode,
            ReporterDisplayName = report.ReportedByUser?.DisplayName,
            ReportedUserCode = report.AccusedUser?.UserCode,
            ReportedDisplayName = report.AccusedUser?.DisplayName,
            ReasonCode = report.ReasonCode,
            FraudType = report.FraudType ?? "OTHER",
            TimestampText = report.TimestampText ?? "00:00",
            TimestampSeconds = report.TimestampSeconds,
            Description = report.Description,
            EvidenceUrl = report.EvidenceUrl,
            EvidenceScreenshotUrl = report.EvidenceScreenshotUrl,
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
