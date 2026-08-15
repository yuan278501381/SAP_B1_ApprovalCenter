using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Approval.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly IApprovalDbContext _db;
    private readonly ITraceContext _traceContext;

    public NotificationsController(IApprovalDbContext db, ITraceContext traceContext)
    {
        _db = db;
        _traceContext = traceContext;
    }

    /// <summary>
    /// 获取当前用户的站内通知列表与未读数
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetNotifications(
        [FromQuery] bool? unreadOnly = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var currentUser = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? Request.Headers["X-Approval-User"].FirstOrDefault()
            ?? _traceContext.CurrentUserCode
            ?? User.Identity?.Name
            ?? "unknown";

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserCode == currentUser);

        var unreadCount = await _db.Notifications
            .CountAsync(n => n.RecipientUserCode == currentUser && !n.IsRead, ct);

        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }

        var list = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new
            {
                n.Id,
                n.RecipientUserCode,
                n.SenderUserCode,
                n.InstanceId,
                n.ObjectCode,
                n.ObjectKey,
                n.Title,
                n.Content,
                n.Type,
                n.IsRead,
                n.CreatedAt,
                n.ReadAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            UnreadCount = unreadCount,
            Items = list
        }, _traceContext.TraceId));
    }

    /// <summary>
    /// 标记单条通知为已读
    /// </summary>
    [HttpPost("{id}/read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAsRead(string id, CancellationToken ct = default)
    {
        var currentUser = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? Request.Headers["X-Approval-User"].FirstOrDefault()
            ?? _traceContext.CurrentUserCode
            ?? User.Identity?.Name
            ?? "unknown";

        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserCode == currentUser, ct);

        if (notif == null)
            return NotFound(ApiResponse<object>.Fail("NOTIF_NOT_FOUND", $"未找到通知 {id}", _traceContext.TraceId));

        if (!notif.IsRead)
        {
            notif.IsRead = true;
            notif.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(ApiResponse<object>.Ok(new { id, Status = "Read" }, _traceContext.TraceId));
    }

    /// <summary>
    /// 一键将所有未读通知标记为已读
    /// </summary>
    [HttpPost("read-all")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAllAsRead(CancellationToken ct = default)
    {
        var currentUser = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? Request.Headers["X-Approval-User"].FirstOrDefault()
            ?? _traceContext.CurrentUserCode
            ?? User.Identity?.Name
            ?? "unknown";

        var unreadList = await _db.Notifications
            .Where(n => n.RecipientUserCode == currentUser && !n.IsRead)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var notif in unreadList)
        {
            notif.IsRead = true;
            notif.ReadAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { Count = unreadList.Count, Status = "AllRead" }, _traceContext.TraceId));
    }
}
