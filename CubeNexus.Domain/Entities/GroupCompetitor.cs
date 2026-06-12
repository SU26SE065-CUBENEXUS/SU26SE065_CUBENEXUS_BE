using CubeNexus.Domain.Enums;

namespace CubeNexus.Domain.Entities;

public class GroupCompetitor
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid RegistrationEventId { get; set; }
    public int? StationNumber { get; set; }
    public GroupCompetitorStatus StatusCode { get; set; } = GroupCompetitorStatus.PENDING;

    public Group Group { get; set; } = null!;
    public OfflineRegistrationEvent OfflineRegistrationEvent { get; set; } = null!;
}
