using CubeNexus.Infrastructure.Persistence;
using CubeNexus.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        var items = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.TypeCode == "SCRAMBLE_POOL_EMPTY")
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
