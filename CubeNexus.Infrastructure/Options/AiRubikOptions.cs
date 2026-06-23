namespace CubeNexus.Infrastructure.Options;

public class AiRubikOptions
{
    public const string SectionName = "AiRubik";

    public string BaseUrl { get; set; } = "http://localhost:8010";
    public int TimeoutSeconds { get; set; } = 10;
    public string? ApiKey { get; set; }
    public bool EnableUnauthenticatedScannerTest { get; set; }
}
