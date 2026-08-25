namespace CubeNexus.Application.Interfaces.Services;

public interface IAdminNotificationService
{
    /// <summary>
    /// Creates a Notification row for every active ADMIN and returns the shared broadcast DTO fields.
    /// </summary>
    Task<AdminNotificationCreatedDto?> NotifyAdminsAsync(
        string typeCode,
        string title,
        string body,
        string? payloadJson,
        CancellationToken ct = default);
}

public sealed class AdminNotificationCreatedDto
{
    public Guid Id { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? Payload { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
