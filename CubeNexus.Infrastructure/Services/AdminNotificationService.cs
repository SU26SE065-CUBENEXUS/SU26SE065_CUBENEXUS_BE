using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CubeNexus.Infrastructure.Services;

public sealed class AdminNotificationService : IAdminNotificationService
{
    private readonly ApplicationDbContext _db;

    public AdminNotificationService(ApplicationDbContext db) => _db = db;

    public async Task<AdminNotificationCreatedDto?> NotifyAdminsAsync(
        string typeCode,
        string title,
        string body,
        string? payloadJson,
        CancellationToken ct = default)
    {
        var adminIds = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserRole == "ADMIN" && u.IsActive && !u.IsBanned)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (adminIds.Count == 0)
            return null;

        var now = DateTime.UtcNow;
        var notifications = adminIds.Select(adminId => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = adminId,
            TypeCode = typeCode,
            Title = title,
            Body = body,
            Payload = payloadJson,
            IsRead = false,
            CreatedAt = now
        }).ToList();

        _db.Notifications.AddRange(notifications);
        await _db.SaveChangesAsync(ct);

        var first = notifications[0];
        return new AdminNotificationCreatedDto
        {
            Id = first.Id,
            TypeCode = first.TypeCode,
            Title = first.Title,
            Body = first.Body,
            Payload = first.Payload,
            IsRead = false,
            CreatedAt = first.CreatedAt
        };
    }

    public async Task MarkFraudReportResolvedAsync(Guid reportId, CancellationToken ct = default)
    {
        var unreadNotifications = await _db.Notifications
            .Where(n => n.TypeCode == "FRAUD_REPORT_CREATED" && !n.IsRead)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var notification in unreadNotifications)
        {
            try
            {
                using var payload = JsonDocument.Parse(notification.Payload ?? "{}");
                if (!payload.RootElement.TryGetProperty("reportId", out var reportIdValue) ||
                    !Guid.TryParse(reportIdValue.GetString(), out var notificationReportId) ||
                    notificationReportId != reportId)
                    continue;

                notification.IsRead = true;
                notification.ReadAt = now;
                changed = true;
            }
            catch (JsonException)
            {
                // Leave malformed legacy notifications untouched.
            }
        }

        if (changed) await _db.SaveChangesAsync(ct);
    }
}
