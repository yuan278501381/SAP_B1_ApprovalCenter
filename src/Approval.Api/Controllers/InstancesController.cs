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
    private readonly ITraceContext _traceContext;

    public InstancesController(IApprovalDbContext db, ITraceContext traceContext)
    {
        _db = db;
        _traceContext = traceContext;
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
            return new
            {
                d.Id,
                d.Name,
                d.Category,
                d.Description,
                d.CreatedAt,
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
}
