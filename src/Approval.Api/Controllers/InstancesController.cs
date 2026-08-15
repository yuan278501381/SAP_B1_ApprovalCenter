using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Approval.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/instances")]
public class InstancesController : ControllerBase
{
    private readonly IApprovalDbContext _db;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly ITraceContext _traceContext;

    public InstancesController(IApprovalDbContext db, IWorkflowEngine workflowEngine, ITraceContext traceContext)
    {
        _db = db;
        _workflowEngine = workflowEngine;
        _traceContext = traceContext;
    }

    /// <summary>
    /// 发起人主动撤回审批申请
    /// </summary>
    [HttpPost("{instanceId}/revoke")]
    public async Task<ActionResult<ApiResponse<object>>> RevokeInstance(
        string instanceId,
        [FromBody] RevokeRequestDto? dto,
        CancellationToken ct = default)
    {
        var operatorCode = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? Request.Headers["X-Approval-User"].FirstOrDefault()
            ?? _traceContext.CurrentUserCode
            ?? User.Identity?.Name
            ?? "unknown";
        var operatorName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? Request.Headers["X-Approval-User-Name"].FirstOrDefault()
            ?? operatorCode;

        try
        {
            var updatedInstance = await _workflowEngine.RevokeWorkflowAsync(
                instanceId,
                operatorCode,
                operatorName,
                dto?.Reason,
                ct);

            return Ok(ApiResponse<object>.Ok(new
            {
                InstanceId = updatedInstance.Id,
                Status = updatedInstance.Status.ToString(),
                Message = "审批申请已成功撤回，相关待办已作废，已通知所有审批人"
            }, _traceContext.TraceId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("INSTANCE_NOT_FOUND", ex.Message, _traceContext.TraceId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("REVOKE_DENIED", ex.Message, _traceContext.TraceId));
        }
    }

    /// <summary>
    /// 导出实例的完整不可变审计证据链
    /// </summary>
    [HttpGet("{instanceId}/audit")]
    public ActionResult<ApiResponse<object>> GetInstanceAudit(string instanceId)
    {
        var instance = _db.Instances.FirstOrDefault(i => i.Id == instanceId);
        if (instance == null)
        {
            return NotFound(ApiResponse<object>.Fail("INSTANCE_NOT_FOUND", $"未找到实例 {instanceId}", _traceContext.TraceId));
        }

        var snapshot = _db.Snapshots.FirstOrDefault(s => s.InstanceId == instanceId);
        var logs = _db.ActionLogs
            .Where(l => l.InstanceId == instanceId)
            .OrderBy(l => l.ActionTime)
            .ToList();

        var nodes = _db.NodeInstances
            .Where(n => n.InstanceId == instanceId)
            .OrderBy(n => n.StartedAt)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            Instance = new
            {
                instance.Id,
                instance.CompanyId,
                instance.ObjectCode,
                instance.ObjectKey,
                instance.Title,
                instance.SubmitterCode,
                instance.SubmitterName,
                Status = instance.Status.ToString(),
                instance.CreatedAt,
                instance.FinishedAt
            },
            Snapshot = new
            {
                snapshot?.DataSha256,
                snapshot?.CanonicalJson,
                snapshot?.SnapshottedAt
            },
            Nodes = nodes.Select(n => new
            {
                n.NodeKey,
                n.NodeName,
                NodeType = n.NodeType.ToString(),
                Status = n.Status.ToString(),
                n.StartedAt,
                n.CompletedAt
            }),
            AuditLogs = logs.Select(l => new
            {
                l.Id,
                l.TraceId,
                l.Action,
                l.OperatorCode,
                l.OperatorName,
                l.FromStatus,
                l.ToStatus,
                l.Comment,
                l.ClientIp,
                l.ActionTime
            })
        }, _traceContext.TraceId));
    }
}

[ApiController]
[Authorize]
[Route("api/v1/definitions")]
public class DefinitionsController : ControllerBase
{
    private readonly IApprovalDbContext _db;
    private readonly ITraceContext _traceContext;

    public DefinitionsController(IApprovalDbContext db, ITraceContext traceContext)
    {
        _db = db;
        _traceContext = traceContext;
    }

    /// <summary>
    /// 获取当前所有启用的流程定义及版本
    /// </summary>
    [HttpGet]
    public ActionResult<ApiResponse<object>> GetDefinitions()
    {
        var defs = _db.Definitions.Where(d => d.IsActive).ToList();
        var result = defs.Select(d =>
        {
            var versions = _db.DefinitionVersions.Where(v => v.DefinitionId == d.Id).ToList();
            var bindings = _db.Bindings.Where(b => versions.Select(v => v.Id).Contains(b.VersionId)).ToList();
            var latestVersion = versions.OrderByDescending(v => v.VersionNum).FirstOrDefault();
            return new
            {
                d.Id,
                d.Name,
                d.Category,
                d.Description,
                d.CreatedAt,
                LatestVersion = latestVersion == null ? null : new
                {
                    latestVersion.Id,
                    latestVersion.VersionNum,
                    latestVersion.Status,
                    latestVersion.GraphJson,
                    latestVersion.PublishedAt
                },
                Versions = versions.Select(v => new
                {
                    v.Id,
                    v.VersionNum,
                    v.Status,
                    v.PublishedAt,
                    BoundObjects = bindings.Where(b => b.VersionId == v.Id).Select(b => new { b.CompanyId, b.ObjectCode, b.Priority })
                })
            };
        });

        return Ok(ApiResponse<object>.Ok(result, _traceContext.TraceId));
    }

    /// <summary>
    /// 获取指定流程详情及完整节点图
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<ApiResponse<object>> GetDefinitionDetail(string id)
    {
        var def = _db.Definitions.FirstOrDefault(d => d.Id == id);
        if (def == null)
            return NotFound(ApiResponse<object>.Fail("DEF_NOT_FOUND", $"未找到流程定义 {id}", _traceContext.TraceId));

        var versions = _db.DefinitionVersions.Where(v => v.DefinitionId == id).OrderByDescending(v => v.VersionNum).ToList();
        var latest = versions.FirstOrDefault();

        return Ok(ApiResponse<object>.Ok(new
        {
            def.Id,
            def.Name,
            def.Category,
            def.Description,
            def.CreatedAt,
            LatestVersion = latest == null ? null : new
            {
                latest.Id,
                latest.VersionNum,
                latest.Status,
                latest.GraphJson,
                latest.PublishedAt
            },
            Versions = versions.Select(v => new
            {
                v.Id,
                v.VersionNum,
                v.Status,
                v.PublishedAt
            })
        }, _traceContext.TraceId));
    }

    /// <summary>
    /// 创建新流程定义
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> CreateDefinition([FromBody] CreateDefinitionDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(ApiResponse<object>.Fail("NAME_REQUIRED", "流程名称不能为空", _traceContext.TraceId));

        var defId = string.IsNullOrWhiteSpace(dto.Id) ? $"DEF_{Guid.NewGuid():N}".Substring(0, 16) : dto.Id.Trim();
        var def = new Domain.Entities.WorkflowDefinition
        {
            Id = defId,
            Name = dto.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category,
            Description = dto.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var ver = new Domain.Entities.WorkflowDefinitionVersion
        {
            Id = $"VER_{def.Id}_V1",
            DefinitionId = def.Id,
            VersionNum = 1,
            GraphJson = string.IsNullOrWhiteSpace(dto.GraphJson) ? "{}" : dto.GraphJson,
            Status = "Published",
            PublishedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        await _db.AddAsync(def, ct);
        await _db.AddAsync(ver, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { def.Id, def.Name, VersionId = ver.Id }, _traceContext.TraceId));
    }

    /// <summary>
    /// 保存并发布新版本流程图
    /// </summary>
    [HttpPost("{id}/versions")]
    public async Task<ActionResult<ApiResponse<object>>> PublishNewVersion(string id, [FromBody] PublishVersionDto dto, CancellationToken ct = default)
    {
        var def = _db.Definitions.FirstOrDefault(d => d.Id == id);
        if (def == null)
            return NotFound(ApiResponse<object>.Fail("DEF_NOT_FOUND", $"未找到流程定义 {id}", _traceContext.TraceId));

        var maxVer = _db.DefinitionVersions.Where(v => v.DefinitionId == id).Select(v => (int?)v.VersionNum).Max() ?? 0;
        var nextNum = maxVer + 1;

        var ver = new Domain.Entities.WorkflowDefinitionVersion
        {
            Id = $"VER_{def.Id}_V{nextNum}",
            DefinitionId = def.Id,
            VersionNum = nextNum,
            GraphJson = dto.GraphJson,
            Status = "Published",
            PublishedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        await _db.AddAsync(ver, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { def.Id, VersionId = ver.Id, VersionNum = ver.VersionNum, Status = "Published" }, _traceContext.TraceId));
    }
}

public record CreateDefinitionDto(string? Id, string Name, string? Category, string? Description, string? GraphJson);
public record PublishVersionDto(string GraphJson);
public record RevokeRequestDto(string? Reason);
