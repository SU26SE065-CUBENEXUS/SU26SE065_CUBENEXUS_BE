using CubeNexus.Infrastructure.Persistence;
using CubeNexus.API.Security;
using CubeNexus.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/admin/notifications")]
[Authorize(Roles = "ADMIN")]
public sealed class AdminNotificationController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AdminNotificationController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = GetUserId();
        await EnsureActiveTournamentNotifications(userId, ct);
        var items = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new
            {
                id = n.Id,
                typeCode = n.TypeCode,
                title = n.Title,
                body = n.Body,
                payload = n.Payload,
                isRead = n.IsRead,
                createdAt = n.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    private async Task EnsureActiveTournamentNotifications(Guid userId, CancellationToken ct)
    {
        var activeTournaments = await _db.Tournaments.AsNoTracking()
            .Where(t => t.StatusCode == "CHECKING_IN" || t.StatusCode == "ONGOING")
            .Select(t => new { t.Id, t.Name, t.StatusCode })
            .ToListAsync(ct);

        if (activeTournaments.Count == 0)
            return;

        var existing = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.TypeCode == "TOURNAMENT_STATUS_CHANGED")
            .Select(n => n.Payload)
            .ToListAsync(ct);

        var newNotifications = activeTournaments
            .Where(t => !existing.Any(payload =>
                payload != null &&
                payload.Contains($"\"tournamentId\":\"{t.Id}\"") &&
                payload.Contains($"\"statusCode\":\"{t.StatusCode}\"")))
            .Select(t => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TypeCode = "TOURNAMENT_STATUS_CHANGED",
                Title = $"Tournament is {t.StatusCode.Replace('_', ' ')}",
                Body = $"{t.Name} is currently in {t.StatusCode} status.",
                Payload = JsonSerializer.Serialize(new
                {
                    tournamentId = t.Id,
                    tournamentName = t.Name,
                    previousStatus = (string?)null,
                    statusCode = t.StatusCode
                }),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (newNotifications.Count > 0)
        {
            _db.Notifications.AddRange(newNotifications);
            await _db.SaveChangesAsync(ct);
        }
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct = default)
    {
        var userId = GetUserId();
        var now = DateTime.UtcNow;
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        await _db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        return UserClaimsHelper.TryGetUserId(User, out var id)
            ? id
            : throw new UnauthorizedAccessException("Invalid user identity.");
    }
}
