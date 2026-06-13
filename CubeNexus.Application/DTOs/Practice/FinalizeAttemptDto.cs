namespace CubeNexus.Application.DTOs.Practice;

public class FinalizeAttemptDto
{
    public int TimeMs { get; set; }

    /// <summary>OK | PLUS_2 | DNF (mặc định OK)</summary>
    public string? Penalty { get; set; }
}
