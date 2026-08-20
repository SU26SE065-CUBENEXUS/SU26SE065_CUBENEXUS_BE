namespace CubeNexus.Infrastructure.Options;

public class FaceVerificationOptions
{
    public const string SectionName = "FaceVerification";

    /// <summary>FastAPI face_verification_service base URL (port 8020).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8020";

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Public base URL of this .NET API so FastAPI can callback.
    /// Example: http://10.10.89.95:5212
    /// </summary>
    public string? CallbackBaseUrl { get; set; }

    /// <summary>Optional shared secret for /internal/face-verification/result.</summary>
    public string? CallbackApiKey { get; set; }

    /// <summary>Offline check-in requires a VERIFIED face session.</summary>
    public bool RequireForCheckIn { get; set; } = true;

    /// <summary>How long a VERIFIED check-in face session remains valid (minutes).</summary>
    public int CheckInSessionValidMinutes { get; set; } = 10;
}
