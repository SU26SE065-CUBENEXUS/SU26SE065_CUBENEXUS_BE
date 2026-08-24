namespace CubeNexus.Domain.Entities;

public class ScrambleGenerationSetting
{
    public string CompetitionMode { get; set; } = string.Empty;
    public string GenerationMode { get; set; } = "MANUAL";
    public Guid? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? UpdatedByUser { get; set; }
}
