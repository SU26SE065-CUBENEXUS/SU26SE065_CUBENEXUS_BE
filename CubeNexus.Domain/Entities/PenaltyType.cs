namespace CubeNexus.Domain.Entities;

public class PenaltyType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int TimeAdditionMs { get; set; } = 0;
    public bool IsDisqualified { get; set; } = false;
}
