using CubeNexus.Application.DTOs.FaceVerification;

namespace CubeNexus.Application.Interfaces.Services;

public interface IFaceVerificationService
{
    Task<FaceEnrollmentStatusDto> GetEnrollmentStatusAsync(Guid userId, CancellationToken ct = default);

    Task<FaceSessionStartResponseDto> StartEnrollmentAsync(Guid userId, CancellationToken ct = default);

    Task<FaceSessionStatusDto> SubmitEnrollmentEvidenceAsync(
        Guid sessionId,
        Guid callerUserId,
        FaceUploadFile? evidenceVideo,
        IReadOnlyList<FaceUploadFile> images,
        string metadataJson,
        CancellationToken ct = default);

    Task<FaceSessionStartResponseDto> StartCheckInVerificationAsync(
        string qrToken,
        Guid? judgeUserId,
        CancellationToken ct = default);

    /// <summary>Competitor: validate own registration and start a face gate before displaying its QR ticket.</summary>
    Task<FaceSessionStartResponseDto> StartCompetitorCheckInVerificationAsync(
        Guid userId,
        Guid tournamentId,
        CancellationToken ct = default);

    /// <summary>Competitor self-test: verify live face against their enrolled Face ID template.</summary>
    Task<FaceSessionStartResponseDto> StartSelfTestVerificationAsync(Guid userId, CancellationToken ct = default);

    Task<FaceSessionStatusDto> SubmitPassiveEvidenceAsync(
        Guid sessionId,
        Guid callerUserId,
        IReadOnlyList<FaceUploadFile> finalFrames,
        CancellationToken ct = default);

    Task<FaceSessionStatusDto> SubmitActiveEvidenceAsync(
        Guid sessionId,
        Guid callerUserId,
        FaceUploadFile? evidenceVideo,
        IReadOnlyList<FaceUploadFile> finalFrames,
        string metadataJson,
        CancellationToken ct = default);

    Task<FaceSessionStatusDto> GetSessionAsync(Guid sessionId, Guid callerUserId, bool isStaff, CancellationToken ct = default);

    Task<object?> AnalyzeFrameAsync(FaceUploadFile frame, CancellationToken ct = default);

    Task HandleCallbackAsync(FaceCallbackRequestDto dto, CancellationToken ct = default);

    Task EnsureValidCheckInFaceSessionAsync(Guid faceSessionId, Guid registrationId, CancellationToken ct = default);

    Task EnsureCheckInFaceGateAsync(Guid? faceSessionId, Guid registrationId, CancellationToken ct = default);
}
