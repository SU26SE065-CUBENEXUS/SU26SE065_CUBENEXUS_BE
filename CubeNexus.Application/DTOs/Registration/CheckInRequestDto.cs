namespace CubeNexus.Application.DTOs.Registration;

public class CheckInRequestDto
{
    public string QrToken { get; set; } = null!;

    /// <summary>
    /// Required when FaceVerification:RequireForCheckIn is true (unless already checked in).
    /// Must be a VERIFIED CHECK_IN face session for this registration.
    /// </summary>
    public Guid? FaceVerificationSessionId { get; set; }
}
