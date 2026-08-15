using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Approval.Api.Controllers;

public record SaveLayoutRequest(
    string CompanyId,
    string ObjectCode,
    string LayoutJson
);

/// <summary>
/// 企业级单据 UI 字段可见性与展示顺序配置控制器 (全公司默认与个人偏好两级云端漫游)
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/ui-layouts")]
public class UiLayoutsController : ControllerBase
{
    private readonly ApprovalDbContext _db;
    private readonly ITraceContext _traceContext;

    public UiLayoutsController(ApprovalDbContext db, ITraceContext traceContext)
    {
        _db = db;
        _traceContext = traceContext;
    }

    private string CurrentUserCode =>
        Request.Headers["X-Approval-User"].FirstOrDefault()
        ?? User.FindFirst("UserCode")?.Value
        ?? User.Identity?.Name
        ?? "manager";

    private bool IsAdmin =>
        string.Equals(CurrentUserCode, "admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(CurrentUserCode, "manager", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 获取当前单据对象的生效布局配置 (优先用户个人配置，若无则继承全公司全局默认配置)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetEffectiveLayout(
        [FromQuery] string companyId,
        [FromQuery] string objectCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(objectCode))
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_PARAMS", "CompanyId 和 ObjectCode 不能为空", _traceContext.TraceId));
        }

        var userCode = CurrentUserCode;

        // 1. 查询全公司全局默认配置
        var globalLayout = await _db.UiLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ObjectCode == objectCode && x.UserCode == null, ct);

        // 2. 查询用户个人专属偏好
        var userLayout = await _db.UiLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ObjectCode == objectCode && x.UserCode == userCode, ct);

        var isUserCustomized = userLayout != null;
        var effectiveLayoutJson = userLayout?.LayoutJson ?? globalLayout?.LayoutJson ?? "{}";

        return Ok(ApiResponse<object>.Ok(new
        {
            CompanyId = companyId,
            ObjectCode = objectCode,
            UserCode = userCode,
            IsUserCustomized = isUserCustomized,
            HasGlobalDefault = globalLayout != null,
            EffectiveLayoutJson = effectiveLayoutJson,
            GlobalLayoutJson = globalLayout?.LayoutJson,
            UpdatedAt = (userLayout?.UpdatedAt ?? globalLayout?.UpdatedAt) ?? DateTime.UtcNow,
            UpdatedBy = userLayout?.UpdatedBy ?? globalLayout?.UpdatedBy ?? "system"
        }, _traceContext.TraceId));
    }

    /// <summary>
    /// 保存当前用户的个人专属偏好配置
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> SaveUserLayout(
        [FromBody] SaveLayoutRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyId) || string.IsNullOrWhiteSpace(req.ObjectCode))
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_PARAMS", "CompanyId 和 ObjectCode 不能为空", _traceContext.TraceId));
        }

        var userCode = CurrentUserCode;
        var id = $"user_{userCode}_{req.CompanyId}_{req.ObjectCode}";
        var existing = await _db.UiLayouts.FirstOrDefaultAsync(x => x.Id == id, ct);

        var now = DateTime.UtcNow;
        if (existing == null)
        {
            var entity = new SysUiLayout
            {
                Id = id,
                CompanyId = req.CompanyId,
                ObjectCode = req.ObjectCode,
                UserCode = userCode,
                LayoutJson = req.LayoutJson,
                UpdatedAt = now,
                UpdatedBy = userCode
            };
            await _db.AddAsync(entity, ct);
        }
        else
        {
            existing.LayoutJson = req.LayoutJson;
            existing.UpdatedAt = now;
            existing.UpdatedBy = userCode;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { Success = true, Message = "个人专属偏好已成功保存至服务器！" }, _traceContext.TraceId));
    }

    /// <summary>
    /// 保存为全公司的全局默认配置 (仅限 Admin / Manager 权限)
    /// </summary>
    [HttpPost("global")]
    public async Task<ActionResult<ApiResponse<object>>> SaveGlobalDefaultLayout(
        [FromBody] SaveLayoutRequest req,
        CancellationToken ct = default)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(req.CompanyId) || string.IsNullOrWhiteSpace(req.ObjectCode))
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_PARAMS", "CompanyId 和 ObjectCode 不能为空", _traceContext.TraceId));
        }

        var userCode = CurrentUserCode;
        var id = $"global_{req.CompanyId}_{req.ObjectCode}";
        var existing = await _db.UiLayouts.FirstOrDefaultAsync(x => x.Id == id, ct);

        var now = DateTime.UtcNow;
        if (existing == null)
        {
            var entity = new SysUiLayout
            {
                Id = id,
                CompanyId = req.CompanyId,
                ObjectCode = req.ObjectCode,
                UserCode = null, // 全局默认
                LayoutJson = req.LayoutJson,
                UpdatedAt = now,
                UpdatedBy = userCode
            };
            await _db.AddAsync(entity, ct);
        }
        else
        {
            existing.LayoutJson = req.LayoutJson;
            existing.UpdatedAt = now;
            existing.UpdatedBy = userCode;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { Success = true, Message = "全公司全局默认配置已成功发布！后续所有用户默认继承此配置。" }, _traceContext.TraceId));
    }

    /// <summary>
    /// 重置当前用户的个人专属配置 (恢复为全公司全局默认配置)
    /// </summary>
    [HttpDelete]
    public async Task<ActionResult<ApiResponse<object>>> ResetUserLayout(
        [FromQuery] string companyId,
        [FromQuery] string objectCode,
        CancellationToken ct = default)
    {
        var userCode = CurrentUserCode;
        var id = $"user_{userCode}_{companyId}_{objectCode}";
        var existing = await _db.UiLayouts.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (existing != null)
        {
            _db.UiLayouts.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(ApiResponse<object>.Ok(new { Success = true, Message = "已成功恢复为全公司默认配置！" }, _traceContext.TraceId));
    }
}
