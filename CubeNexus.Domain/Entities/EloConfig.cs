namespace CubeNexus.Domain.Entities;

public class EloConfig
{
    public Guid Id { get; set; }

    public int KFactorPlacement { get; set; } = 100;
    public int KFactorStandard { get; set; } = 20;
    public int PlacementMatchCount { get; set; } = 5;
    public int DefaultElo { get; set; } = 1000;

    public Guid? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User? UpdatedByUser { get; set; }
}
