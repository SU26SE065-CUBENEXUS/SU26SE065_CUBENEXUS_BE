using System.Text.Json;
using CubeNexus.Application.DTOs.FaceVerification;
using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Options;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CubeNexus.Infrastructure.Services;

public class FaceVerificationService : IFaceVerificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFaceVerificationClient _client;
    private readonly FaceVerificationOptions _options;
    private readonly ILogger<FaceVerificationService> _logger;

    public FaceVerificationService(
        ApplicationDbContext db,
        IUnitOfWork unitOfWork,
        IFaceVerificationClient client,
        IOptions<FaceVerificationOptions> options,
        ILogger<FaceVerificationService> logger)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FaceEnrollmentStatusDto> GetEnrollmentStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var enrollment = await _db.FaceEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Status == "ENROLLED", ct);

        return new FaceEnrollmentStatusDto
        {
            UserId = userId,
            IsEnrolled = enrollment is not null,
            Status = enrollment?.Status,
            ModelVersion = enrollment?.ModelVersion,
            QualityScore = enrollment?.QualityScore,
            TemplatesCount = enrollment?.TemplatesCount ?? 0,
            EnrolledAt = enrollment?.EnrolledAt,
        };
    }

    public async Task<FaceSessionStartResponseDto> StartEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        var callbackUrl = BuildCallbackUrl();
        var ai = await _client.CreateEnrollmentSessionAsync(new FaceAiCreateSessionRequest
        {
            UserId = userId.ToString(),
            CallbackUrl = callbackUrl,
            Metadata = new Dictionary<string, object?>
            {
                ["verificationType"] = "ENROLLMENT",
                ["source"] = "cubenexus-api",
            },
        }, ct);

        var session = await PersistSessionAsync(
            userId: userId,
            purpose: "ENROLLMENT",
            contextType: "PROFILE",
            tournamentId: null,
            registrationId: null,
            initiatedByUserId: userId,
            ai: ai,
            ct);

        return ToStartResponse(session, playerName: null, faceEnrolled: false);
    }

    public async Task<FaceSessionStatusDto> SubmitEnrollmentEvidenceAsync(
        Guid sessionId,
        Guid callerUserId,
        FaceUploadFile? evidenceVideo,
        IReadOnlyList<FaceUploadFile> images,
        string metadataJson,
        CancellationToken ct = default)
    {
        var session = await GetOwnedSessionAsync(sessionId, callerUserId, staffOverride: false, ct);
        if (session.Purpose != "ENROLLMENT")
        {
            throw new CustomException("SESSION_IS_NOT_FOR_ENROLLMENT", "Session is not an enrollment session.", 409);
        }

        EnsureSessionOpen(session);
        if (images.Count is < 3 or > 8)
        {
            throw new CustomException("ENROLLMENT_REQUIRES_3_TO_8_IMAGES", "Enrollment requires 3 to 8 images.", 422);
        }

        var imageParts = images
            .Select(image => (image.Content, image.FileName, image.ContentType))
            .ToList();

        var aiResult = await _client.SubmitEnrollmentEvidenceAsync(
            session.ExternalSessionId,
            session.UploadToken,
            string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson,
            evidenceVideo?.Content,
            evidenceVideo?.FileName,
            evidenceVideo?.ContentType,
            imageParts,
            ct);

        ApplyAiResult(session, aiResult);
        if (session.State == "ENROLLED")
        {
            await UpsertEnrollmentAsync(session, aiResult, ct);
        }

        await _db.SaveChangesAsync(ct);
        return ToStatusDto(session);
    }

    public async Task<FaceSessionStartResponseDto> StartCheckInVerificationAsync(
        string qrToken,
        Guid? judgeUserId,
        CancellationToken ct = default)
    {
        var registration = await ResolveRegistrationAsync(qrToken);
        if (registration is null)
        {
            throw new CustomException("QR_INVALID", "Invalid QR code credentials or token mismatch.", 400);
        }

        // Basic check-in conditions must pass before any Face Verification session is created.
        if (string.Equals(registration.StatusCode, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException("REGISTRATION_CANCELLED", "This registration has been cancelled.", 400);
        }

        if (!string.Equals(registration.StatusCode, "CONFIRMED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(registration.StatusCode, "CHECKED_IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException("REGISTRATION_NOT_CONFIRMED", "Only a confirmed registration can proceed to check-in.", 400);
        }

        if (!string.Equals(registration.Tournament.StatusCode, "CHECKING_IN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(registration.Tournament.StatusCode, "ONGOING", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException("INVALID_TOURNAMENT_STATE", "Face Verification is available only while the tournament is CHECKING_IN or ONGOING.", 400);
        }

        if (judgeUserId.HasValue)
        {
            await EnsureJudgeAssignedAsync(judgeUserId.Value, registration.TournamentId);
        }

        var enrollment = await _db.FaceEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == registration.UserId && e.Status == "ENROLLED", ct);
        if (enrollment is null)
        {
            throw new CustomException(
                "FACE_NOT_ENROLLED",
                "The competitor has not registered Facial Biometrics on their Competitor account (Profile → Facial Biometrics). Judges cannot register on their behalf.",
                409);
        }

        var callbackUrl = BuildCallbackUrl();
        var ai = await _client.CreateVerificationSessionAsync(new FaceAiCreateSessionRequest
        {
            UserId = registration.UserId.ToString(),
            CallbackUrl = callbackUrl,
            Metadata = new Dictionary<string, object?>
            {
                ["verificationType"] = "CHECK_IN",
                ["registrationId"] = registration.Id.ToString(),
                ["tournamentId"] = registration.TournamentId.ToString(),
                ["source"] = "cubenexus-api",
            },
        }, ct);

        var session = await PersistSessionAsync(
            userId: registration.UserId,
            purpose: "VERIFICATION",
            contextType: "CHECK_IN",
            tournamentId: registration.TournamentId,
            registrationId: registration.Id,
            initiatedByUserId: judgeUserId,
            ai: ai,
            ct);

        return ToStartResponse(
            session,
            playerName: registration.User.DisplayName,
            faceEnrolled: true);
    }

    public async Task<FaceSessionStartResponseDto> StartCompetitorCheckInVerificationAsync(
        Guid userId,
        Guid tournamentId,
        CancellationToken ct = default)
    {
        var registration = await _db.Registrations
            .Include(r => r.User)
            .Include(r => r.Tournament)
            .Where(r => r.UserId == userId
                && r.TournamentId == tournamentId
                && r.StatusCode != "CANCELLED")
            .OrderByDescending(r => r.RegisteredAt)
            .FirstOrDefaultAsync(ct);

        if (registration is null)
        {
            throw new CustomException(
                "REGISTRATION_NOT_FOUND",
                "You are not registered for this tournament.",
                404);
        }

        // CANCELLED remains a valid registration state in the system, but it can
        // never pass the competitor QR gate. Competitors do not change this state here.
        if (string.Equals(registration.StatusCode, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException(
                "REGISTRATION_CANCELLED",
                "This registration has been cancelled.",
                400);
        }

        if (!string.Equals(registration.StatusCode, "CONFIRMED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(registration.StatusCode, "CHECKED_IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException(
                "REGISTRATION_NOT_CONFIRMED",
                "Only a confirmed registration can open a check-in QR ticket.",
                400);
        }

        if (!string.Equals(registration.Tournament.StatusCode, "CHECKING_IN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(registration.Tournament.StatusCode, "ONGOING", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException(
                "INVALID_TOURNAMENT_STATE",
                "QR check-in is available only while the tournament is CHECKING_IN or ONGOING.",
                400);
        }

        var enrollment = await _db.FaceEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Status == "ENROLLED", ct);
        if (enrollment is null)
        {
            throw new CustomException(
                "FACE_NOT_ENROLLED",
                "Please register Face ID in Profile before opening the check-in QR.",
                409);
        }

        var ai = await _client.CreateVerificationSessionAsync(new FaceAiCreateSessionRequest
        {
            UserId = userId.ToString(),
            CallbackUrl = BuildCallbackUrl(),
            Metadata = new Dictionary<string, object?>
            {
                ["verificationType"] = "CHECK_IN",
                ["registrationId"] = registration.Id.ToString(),
                ["tournamentId"] = tournamentId.ToString(),
                ["source"] = "cubenexus-api-competitor",
            },
        }, ct);

        var session = await PersistSessionAsync(
            userId: userId,
            purpose: "VERIFICATION",
            contextType: "CHECK_IN",
            tournamentId: tournamentId,
            registrationId: registration.Id,
            initiatedByUserId: userId,
            ai: ai,
            ct);

        return ToStartResponse(
            session,
            playerName: registration.User.DisplayName,
            faceEnrolled: true);
    }

    public async Task<FaceSessionStartResponseDto> StartSelfTestVerificationAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var enrollment = await _db.FaceEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Status == "ENROLLED", ct);
        if (enrollment is null)
        {
            throw new CustomException(
                "FACE_NOT_ENROLLED",
                "You have not registered Facial Biometrics. Please register before trying to verify.",
                409);
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var callbackUrl = BuildCallbackUrl();
        var ai = await _client.CreateVerificationSessionAsync(new FaceAiCreateSessionRequest
        {
            UserId = userId.ToString(),
            CallbackUrl = callbackUrl,
            Metadata = new Dictionary<string, object?>
            {
                ["verificationType"] = "SELF_TEST",
                ["source"] = "cubenexus-api-profile",
            },
        }, ct);

        var session = await PersistSessionAsync(
            userId: userId,
            purpose: "VERIFICATION",
            contextType: "PROFILE", // DB check allows PROFILE|CHECK_IN|STATION|LOGIN (not SELF_TEST)
            tournamentId: null,
            registrationId: null,
            initiatedByUserId: userId,
            ai: ai,
            ct);

        return ToStartResponse(
            session,
            playerName: user?.DisplayName,
            faceEnrolled: true);
    }

    public async Task<FaceSessionStatusDto> SubmitPassiveEvidenceAsync(
        Guid sessionId,
        Guid callerUserId,
        IReadOnlyList<FaceUploadFile> finalFrames,
        CancellationToken ct = default)
    {
        var session = await GetOwnedSessionAsync(sessionId, callerUserId, staffOverride: true, ct);
        if (session.Purpose != "VERIFICATION")
        {
            throw new CustomException("SESSION_IS_NOT_FOR_VERIFICATION", "Session is not a verification session.", 409);
        }

        EnsureSessionOpen(session);
        if (finalFrames.Count is < 3 or > 5)
        {
            throw new CustomException("PASSIVE_REQUIRES_3_TO_5_FINAL_FRAMES", "Passive verification requires 3 to 5 frames.", 422);
        }

        var parts = finalFrames
            .Select(frame => (frame.Content, frame.FileName, frame.ContentType))
            .ToList();

        var aiResult = await _client.SubmitPassiveEvidenceAsync(
            session.ExternalSessionId,
            session.UploadToken,
            parts,
            ct);

        ApplyAiResult(session, aiResult);
        await _db.SaveChangesAsync(ct);
        return ToStatusDto(session);
    }

    public async Task<FaceSessionStatusDto> SubmitActiveEvidenceAsync(
        Guid sessionId,
        Guid callerUserId,
        FaceUploadFile? evidenceVideo,
        IReadOnlyList<FaceUploadFile> finalFrames,
        string metadataJson,
        CancellationToken ct = default)
    {
        var session = await GetOwnedSessionAsync(sessionId, callerUserId, staffOverride: true, ct);
        EnsureSessionOpen(session);
        if (session.Purpose == "VERIFICATION" && session.State is not ("CHALLENGE_REQUIRED" or "CHALLENGE"))
        {
            throw new CustomException(
                "ACTIVE_CHALLENGE_NOT_EXPECTED",
                "Active challenge evidence is accepted only after the verification service requests a challenge.",
                409);
        }
        if (session.Purpose == "VERIFICATION" && evidenceVideo is null)
        {
            throw new CustomException(
                "CHALLENGE_VIDEO_REQUIRED",
                "Challenge verification requires a recorded video evidence file.",
                422);
        }
        if (finalFrames.Count is < 1 or > 5)
        {
            throw new CustomException("FINAL_FRAMES_REQUIRED", "Active verification requires 1 to 5 final frames.", 422);
        }

        var parts = finalFrames
            .Select(frame => (frame.Content, frame.FileName, frame.ContentType))
            .ToList();

        var aiResult = await _client.SubmitActiveEvidenceAsync(
            session.ExternalSessionId,
            session.UploadToken,
            string.IsNullOrWhiteSpace(metadataJson) ? "{\"cameraMirror\":true}" : metadataJson,
            evidenceVideo?.Content,
            evidenceVideo?.FileName,
            evidenceVideo?.ContentType,
            parts,
            ct);

        ApplyAiResult(session, aiResult);
        if (session.Purpose == "ENROLLMENT" && session.State == "ENROLLED")
        {
            await UpsertEnrollmentAsync(session, aiResult, ct);
        }

        await _db.SaveChangesAsync(ct);
        return ToStatusDto(session);
    }

    public async Task<FaceSessionStatusDto> GetSessionAsync(Guid sessionId, Guid callerUserId, bool isStaff, CancellationToken ct = default)
    {
        var session = await GetOwnedSessionAsync(sessionId, callerUserId, staffOverride: isStaff, ct);
        return ToStatusDto(session);
    }

    public Task<object?> AnalyzeFrameAsync(FaceUploadFile frame, CancellationToken ct = default)
    {
        return _client.AnalyzeFrameAsync(frame.Content, frame.FileName, frame.ContentType, ct);
    }

    public async Task HandleCallbackAsync(FaceCallbackRequestDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.SessionId))
        {
            return;
        }

        var session = await _db.FaceVerificationSessions
            .FirstOrDefaultAsync(s => s.ExternalSessionId == dto.SessionId, ct);
        if (session is null)
        {
            _logger.LogWarning("Face callback for unknown external session {ExternalSessionId}", dto.SessionId);
            return;
        }

        session.State = string.IsNullOrWhiteSpace(dto.State) ? session.State : dto.State;
        session.ResultJson = dto.Result is null ? session.ResultJson : JsonSerializer.Serialize(dto.Result, JsonOptions);
        ApplyResultFlags(session, dto.Result);
        if (session.State is "VERIFIED" or "ENROLLED" or "REJECTED")
        {
            session.CompletedAt ??= DateTime.UtcNow;
        }

        if (session.State == "ENROLLED")
        {
            await UpsertEnrollmentFromCallbackAsync(session, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureValidCheckInFaceSessionAsync(Guid faceSessionId, Guid registrationId, CancellationToken ct = default)
    {
        var session = await _db.FaceVerificationSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == faceSessionId, ct);
        if (session is null)
        {
            throw new CustomException("FACE_SESSION_NOT_FOUND", "Face verification session not found.", 404);
        }

        if (session.RegistrationId != registrationId || session.ContextType != "CHECK_IN")
        {
            throw new CustomException("FACE_SESSION_MISMATCH", "Face session does not match this registration.", 400);
        }

        if (!string.Equals(session.State, "VERIFIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomException("FACE_NOT_VERIFIED", "Face verification has not passed for this competitor.", 400);
        }

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            throw new CustomException("FACE_SESSION_EXPIRED", "Face verification session has expired. Please verify again.", 410);
        }

        var validUntil = session.CompletedAt ?? session.CreatedAt;
        if (validUntil.AddMinutes(Math.Max(1, _options.CheckInSessionValidMinutes)) < DateTime.UtcNow)
        {
            throw new CustomException("FACE_SESSION_STALE", "Face verification is too old. Please verify again.", 410);
        }
    }

    public async Task EnsureCheckInFaceGateAsync(Guid? faceSessionId, Guid registrationId, CancellationToken ct = default)
    {
        if (!_options.RequireForCheckIn)
        {
            return;
        }

        if (!faceSessionId.HasValue)
        {
            throw new CustomException(
                "FACE_VERIFICATION_REQUIRED",
                "Check-in requires a verified face session. Scan QR, verify face, then submit check-in.",
                400);
        }

        await EnsureValidCheckInFaceSessionAsync(faceSessionId.Value, registrationId, ct);
    }

    private async Task<FaceVerificationSession> PersistSessionAsync(
        Guid userId,
        string purpose,
        string contextType,
        Guid? tournamentId,
        Guid? registrationId,
        Guid? initiatedByUserId,
        FaceAiCreateSessionResponse ai,
        CancellationToken ct)
    {
        var session = new FaceVerificationSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            ContextType = contextType,
            TournamentId = tournamentId,
            RegistrationId = registrationId,
            InitiatedByUserId = initiatedByUserId,
            ExternalSessionId = ai.SessionId,
            UploadToken = ai.UploadToken,
            ChallengeJson = ai.Challenge is null
                ? null
                : JsonSerializer.Serialize(ai.Challenge, JsonOptions),
            State = string.IsNullOrWhiteSpace(ai.State) ? "POSITIONING" : ai.State,
            ExpiresAt = ai.ExpiresAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(ai.ExpiresAt, DateTimeKind.Utc)
                : ai.ExpiresAt.ToUniversalTime(),
            CreatedAt = DateTime.UtcNow,
        };

        _db.FaceVerificationSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    private async Task UpsertEnrollmentAsync(
        FaceVerificationSession session,
        FaceAiSessionResultResponse aiResult,
        CancellationToken ct)
    {
        var details = ExtractDetails(aiResult.Result);
        var enrollment = await _db.FaceEnrollments.FirstOrDefaultAsync(e => e.UserId == session.UserId, ct);
        var now = DateTime.UtcNow;
        if (enrollment is null)
        {
            enrollment = new FaceEnrollment
            {
                Id = Guid.NewGuid(),
                UserId = session.UserId,
                EnrolledAt = now,
            };
            _db.FaceEnrollments.Add(enrollment);
        }

        enrollment.Status = "ENROLLED";
        enrollment.ModelVersion = details.TryGetValue("modelVersion", out var mv) ? mv?.ToString() : "buffalo_l";
        enrollment.QualityScore = TryDouble(details, "qualityScore");
        enrollment.TemplatesCount = TryInt(details, "templatesStored") ?? 0;
        enrollment.LastExternalSessionId = session.ExternalSessionId;
        enrollment.UpdatedAt = now;
        if (enrollment.EnrolledAt == default)
        {
            enrollment.EnrolledAt = now;
        }
    }

    private async Task UpsertEnrollmentFromCallbackAsync(FaceVerificationSession session, CancellationToken ct)
    {
        var enrollment = await _db.FaceEnrollments.FirstOrDefaultAsync(e => e.UserId == session.UserId, ct);
        var now = DateTime.UtcNow;
        if (enrollment is null)
        {
            enrollment = new FaceEnrollment
            {
                Id = Guid.NewGuid(),
                UserId = session.UserId,
                Status = "ENROLLED",
                EnrolledAt = now,
                UpdatedAt = now,
                LastExternalSessionId = session.ExternalSessionId,
                ModelVersion = "buffalo_l",
            };
            _db.FaceEnrollments.Add(enrollment);
            return;
        }

        enrollment.Status = "ENROLLED";
        enrollment.UpdatedAt = now;
        enrollment.LastExternalSessionId = session.ExternalSessionId;
    }

    private void ApplyAiResult(FaceVerificationSession session, FaceAiSessionResultResponse aiResult)
    {
        session.State = string.IsNullOrWhiteSpace(aiResult.State) ? session.State : aiResult.State;
        session.ResultJson = aiResult.Result is null ? session.ResultJson : JsonSerializer.Serialize(aiResult.Result, JsonOptions);
        ApplyResultFlags(session, aiResult.Result);
        if (session.State is "VERIFIED" or "ENROLLED" or "REJECTED")
        {
            session.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            session.CompletedAt = null;
        }
    }

    private static void ApplyResultFlags(FaceVerificationSession session, object? resultObj)
    {
        if (resultObj is null)
        {
            return;
        }

        Dictionary<string, object?>? result;
        if (resultObj is Dictionary<string, object?> dict)
        {
            result = dict;
        }
        else
        {
            try
            {
                result = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    JsonSerializer.Serialize(resultObj, JsonOptions),
                    JsonOptions);
            }
            catch
            {
                return;
            }
        }

        if (result is null)
        {
            return;
        }

        if (result.TryGetValue("reason", out var reason) && reason is not null)
        {
            session.FailureReason = reason.ToString();
        }

        if (result.TryGetValue("livenessPassed", out var live))
        {
            session.LivenessPassed = TryBool(live);
        }

        if (result.TryGetValue("faceMatched", out var matched))
        {
            session.FaceMatched = TryBool(matched);
        }

        var details = ExtractDetails(result);
        session.Similarity = TryDouble(details, "similarity");
    }

    private static Dictionary<string, object?> ExtractDetails(Dictionary<string, object?>? result)
    {
        if (result is null || !result.TryGetValue("details", out var details) || details is null)
        {
            return new Dictionary<string, object?>();
        }

        if (details is Dictionary<string, object?> typed)
        {
            return typed;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(details, JsonOptions),
                JsonOptions) ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static Dictionary<string, object?> ExtractDetails(object? resultObj)
    {
        if (resultObj is Dictionary<string, object?> dict)
        {
            return ExtractDetails(dict);
        }

        return new Dictionary<string, object?>();
    }

    private async Task<FaceVerificationSession> GetOwnedSessionAsync(
        Guid sessionId,
        Guid callerUserId,
        bool staffOverride,
        CancellationToken ct)
    {
        var session = await _db.FaceVerificationSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            throw new CustomException("FACE_SESSION_NOT_FOUND", "Face verification session not found.", 404);
        }

        if (session.ExpiresAt < DateTime.UtcNow && session.State is not ("VERIFIED" or "ENROLLED" or "REJECTED"))
        {
            session.State = "EXPIRED";
            await _db.SaveChangesAsync(ct);
            throw new CustomException("FACE_SESSION_EXPIRED", "Face verification session has expired.", 410);
        }

        if (staffOverride)
        {
            var caller = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerUserId, ct);
            var isStaff = caller is not null && caller.UserRole is "JUDGE" or "MANAGER" or "ADMIN";
            if (isStaff || session.UserId == callerUserId || session.InitiatedByUserId == callerUserId)
            {
                return session;
            }
        }
        else if (session.UserId == callerUserId)
        {
            return session;
        }

        throw new CustomException("FACE_SESSION_FORBIDDEN", "You cannot access this face verification session.", 403);
    }

    private static void EnsureSessionOpen(FaceVerificationSession session)
    {
        if (session.State is "PROCESSING" or "VERIFIED" or "ENROLLED" or "REJECTED" or "EXPIRED")
        {
            throw new CustomException("SESSION_ALREADY_PROCESSED", "Session has already been processed.", 409);
        }
    }

    private string? BuildCallbackUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.CallbackBaseUrl))
        {
            return null;
        }

        return $"{_options.CallbackBaseUrl.TrimEnd('/')}/internal/face-verification/result";
    }

    private async Task<Registration?> ResolveRegistrationAsync(string qrToken)
    {
        if (string.IsNullOrWhiteSpace(qrToken))
        {
            return null;
        }

        RegistrationQrPayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<RegistrationQrPayload>(qrToken);
        }
        catch
        {
            // raw token
        }

        if (payload != null && payload.RegistrationId != Guid.Empty && !string.IsNullOrEmpty(payload.Token))
        {
            if (payload.ExpiresAt < DateTime.UtcNow)
            {
                throw new CustomException("QR_EXPIRED", "The competitor's QR ticket has expired.", 400);
            }

            var registration = await _unitOfWork.Registrations.GetRegistrationWithDetailsAsync(payload.RegistrationId);
            if (registration is null)
            {
                return null;
            }

            try
            {
                var dbPayload = JsonSerializer.Deserialize<RegistrationQrPayload>(registration.QrToken);
                if (dbPayload == null || dbPayload.Token != payload.Token)
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            return registration;
        }

        return await _unitOfWork.Registrations.GetByQrTokenAsync(qrToken);
    }

    private async Task EnsureJudgeAssignedAsync(Guid judgeUserId, Guid tournamentId)
    {
        var caller = await _unitOfWork.Users.GetByIdAsync(judgeUserId);
        if (caller is null || !string.Equals(caller.UserRole, "JUDGE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isAssigned = await _unitOfWork.TournamentJudges.AnyAsync(
            tj => tj.TournamentId == tournamentId && tj.UserId == judgeUserId);
        if (!isAssigned)
        {
            throw new CustomException(
                "JUDGE_NOT_ASSIGNED_TO_TOURNAMENT",
                "Trọng tài không có quyền điểm danh thí sinh cho giải đấu này.",
                403);
        }
    }

    private static FaceSessionStartResponseDto ToStartResponse(
        FaceVerificationSession session,
        string? playerName,
        bool faceEnrolled)
    {
        return new FaceSessionStartResponseDto
        {
            SessionId = session.Id,
            ExternalSessionId = session.ExternalSessionId,
            UploadToken = session.UploadToken,
            Challenge = ParseChallenge(session.ChallengeJson),
            ExpiresAt = session.ExpiresAt,
            State = session.State,
            Purpose = session.Purpose,
            ContextType = session.ContextType,
            UserId = session.UserId,
            PlayerName = playerName,
            RegistrationId = session.RegistrationId,
            TournamentId = session.TournamentId,
            FaceEnrolled = faceEnrolled,
        };
    }

    private static FaceSessionStatusDto ToStatusDto(FaceVerificationSession session)
    {
        object? result = null;
        if (!string.IsNullOrWhiteSpace(session.ResultJson))
        {
            try
            {
                result = JsonSerializer.Deserialize<object>(session.ResultJson, JsonOptions);
            }
            catch
            {
                result = session.ResultJson;
            }
        }

        return new FaceSessionStatusDto
        {
            SessionId = session.Id,
            ExternalSessionId = session.ExternalSessionId,
            State = session.State,
            Purpose = session.Purpose,
            ContextType = session.ContextType,
            UserId = session.UserId,
            RegistrationId = session.RegistrationId,
            Challenge = ParseChallenge(session.ChallengeJson),
            ExpiresAt = session.ExpiresAt,
            Result = result,
            FailureReason = session.FailureReason,
            LivenessPassed = session.LivenessPassed,
            FaceMatched = session.FaceMatched,
            Similarity = session.Similarity,
        };
    }

    private static FaceChallengeDto ParseChallenge(string? challengeJson)
    {
        if (string.IsNullOrWhiteSpace(challengeJson))
        {
            return new FaceChallengeDto();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<FaceAiChallengeResponse>(challengeJson, JsonOptions);
            return new FaceChallengeDto
            {
                ChallengeId = parsed?.ChallengeId ?? string.Empty,
                Actions = parsed?.Actions ?? [],
            };
        }
        catch
        {
            return new FaceChallengeDto();
        }
    }

    private static bool? TryBool(object? value)
    {
        return value switch
        {
            bool b => b,
            JsonElement el when el.ValueKind == JsonValueKind.True => true,
            JsonElement el when el.ValueKind == JsonValueKind.False => false,
            string s when bool.TryParse(s, out var b) => b,
            _ => null,
        };
    }

    private static double? TryDouble(Dictionary<string, object?> details, string key)
    {
        if (!details.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) => d,
            string s when double.TryParse(s, out var d) => d,
            _ => null,
        };
    }

    private static int? TryInt(Dictionary<string, object?> details, string key)
    {
        if (!details.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) => i,
            string s when int.TryParse(s, out var i) => i,
            _ => null,
        };
    }
}
