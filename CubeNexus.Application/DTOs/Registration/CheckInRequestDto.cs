namespace CubeNexus.Application.DTOs.Registration;

public class CheckInRequestDto
{
    public string QrToken { get; set; } = null!;

    /// <summary>
    /// Kept for backward-compatible clients. Competitor Face Verification is completed
    /// before the QR is displayed; Judge Desk scanning does not repeat Face Verification.
    /// </summary>
    public Guid? FaceVerificationSessionId { get; set; }
}
