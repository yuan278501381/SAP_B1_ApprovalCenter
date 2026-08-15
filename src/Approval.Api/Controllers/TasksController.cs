using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Approval.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskStatus = Approval.Domain.Enums.TaskStatus;

namespace Approval.Api.Controllers;

public record DecisionRequest(string Decision, string? Comments);

[ApiController]
[Authorize]
[Route("api/v1/tasks")]
public class TasksController : ControllerBase
{
    private readonly IWorkflowEngine _engine;
    private readonly ApprovalDbContext _db;
    private readonly ITraceContext _traceContext;

    public TasksController(IWorkflowEngine engine, ApprovalDbContext db, ITraceContext traceContext)
    {
        _engine = engine;
        _db = db;
        _traceContext = traceContext;
    }

    /// <summary>
    /// 查询任务列表 (支持待办、已处理、全部)
    /// </summary>
    [HttpGet]
    public ActionResult<ApiResponse<object>> GetTasks(
        [FromQuery] string scope = "mine",
        [FromQuery] string status = "pending",
        [FromQuery] string? companyId = null,
        [FromQuery] string? objectCode = null,
        [FromQuery] string? objectKey = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userCode = CurrentUserCode;
        var query = _db.Tasks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(companyId) || !string.IsNullOrWhiteSpace(objectCode) || !string.IsNullOrWhiteSpace(objectKey))
        {
            var instanceIds = _db.Instances
                .Where(i => (companyId == null || i.CompanyId == companyId)
                    && (objectCode == null || i.ObjectCode == objectCode)
                    && (objectKey == null || i.ObjectKey == objectKey))
                .Select(i => i.Id);
            query = query.Where(t => instanceIds.Contains(t.InstanceId));
        }

        if (status.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Status == TaskStatus.Pending);
        }
        else if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Status == TaskStatus.Completed);
        }

        // 关联候选人过滤
        if (scope.Equals("mine", StringComparison.OrdinalIgnoreCase))
        {
            var taskIds = _db.TaskCandidates
                .Where(c => c.UserCode == userCode)
                .Select(c => c.TaskId);

            query = query.Where(t => taskIds.Contains(t.Id) || t.CompletedBy == userCode);
        }

        var total = query.Count();
        var list = query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var resultItems = list.Select(t =>
        {
            var inst = _db.Instances.FirstOrDefault(i => i.Id == t.InstanceId);
            var node = _db.NodeInstances.FirstOrDefault(n => n.Id == t.NodeInstanceId);
            return new
            {
                TaskId = t.Id,
                InstanceId = t.InstanceId,
                ObjectCode = inst?.ObjectCode,
                ObjectKey = inst?.ObjectKey,
                Title = inst?.Title,
                Submitter = inst?.SubmitterName ?? inst?.SubmitterCode,
                NodeName = node?.NodeName,
                TaskType = t.TaskType.ToString(),
                Status = t.Status.ToString(),
                t.Decision,
                t.CreatedAt,
                t.DueAt,
                t.CompletedAt
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = resultItems
        }, _traceContext.TraceId));
    }

    /// <summary>
    /// 获取任务详情 (包含单据明细、快照及审批轨迹)
    /// </summary>
    [HttpGet("{taskId}")]
    public ActionResult<ApiResponse<object>> GetTaskDetail(string taskId)
    {
        var task = _db.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
        {
            return NotFound(ApiResponse<object>.Fail("TASK_NOT_FOUND", $"未找到任务 {taskId}", _traceContext.TraceId));
        }

        var canRead = _db.TaskCandidates.Any(c => c.TaskId == taskId && c.UserCode == CurrentUserCode)
            || task.CompletedBy == CurrentUserCode;
        if (!canRead)
            return Forbid();

        var instance = _db.Instances.FirstOrDefault(i => i.Id == task.InstanceId);
        var snapshot = _db.Snapshots.FirstOrDefault(s => s.InstanceId == task.InstanceId);
        var node = _db.NodeInstances.FirstOrDefault(n => n.Id == task.NodeInstanceId);
        var logs = _db.ActionLogs.Where(l => l.InstanceId == task.InstanceId).OrderBy(l => l.ActionTime).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            Task = new
            {
                task.Id,
                task.InstanceId,
                task.NodeInstanceId,
                NodeName = node?.NodeName,
                TaskType = task.TaskType.ToString(),
                Status = task.Status.ToString(),
                task.Decision,
                task.Comments,
                task.CreatedAt,
                task.DueAt
            },
            Instance = new
            {
                instance?.Id,
                instance?.CompanyId,
                instance?.ObjectCode,
                instance?.ObjectKey,
                instance?.Title,
                instance?.SubmitterCode,
                instance?.SubmitterName,
                Status = instance?.Status.ToString(),
                instance?.CreatedAt,
                instance?.FinishedAt
            },
            Snapshot = new
            {
                DataSha256 = snapshot?.DataSha256,
                RawJson = snapshot?.RawJson,
                CanonicalJson = snapshot?.CanonicalJson,
                SnapshottedAt = snapshot?.SnapshottedAt
            },
            AuditLogs = logs.Select(l => new
            {
                l.Id,
                l.Action,
                l.OperatorCode,
                l.OperatorName,
                l.FromStatus,
                l.ToStatus,
                l.Comment,
                l.ActionTime
            }).ToList()
        }, _traceContext.TraceId));
    }

    /// <summary>
    /// 提交任务审批决定 (同意 / 拒绝 / 退回)
    /// </summary>
    [HttpPost("{taskId}/decisions")]
    public async Task<ActionResult<ApiResponse<object>>> MakeDecision(
        string taskId,
        [FromBody] DecisionRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(ApiResponse<object>.Fail("IDEMPOTENCY_KEY_REQUIRED", "审批决定必须提供 Idempotency-Key", _traceContext.TraceId));

        var userCode = CurrentUserCode;
        var userName = User.Identity?.Name;

        // 1. 幂等性校验
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingInbox = _db.Inboxes.FirstOrDefault(i => i.HandlerName == nameof(MakeDecision) && i.IdempotencyKey == idempotencyKey);
            if (existingInbox != null)
            {
                var cached = JsonSerializer.Deserialize<object>(existingInbox.ResponseJson ?? "{}");
                return Ok(ApiResponse<object>.Ok(cached!, _traceContext.TraceId));
            }
        }

        if (!Enum.TryParse<TaskDecision>(req.Decision, true, out var decisionEnum))
        {
            return BadRequest(ApiResponse<object>.Fail("INVALID_DECISION", "无效的审批决定，支持: Approve, Reject, Return", _traceContext.TraceId));
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (_db.Database.IsRelational())
                transaction = await _db.Database.BeginTransactionAsync(ct);

            var task = await _engine.ProcessDecisionAsync(
                taskId,
                userCode,
                userName,
                decisionEnum,
                req.Comments,
                ct);

            var inst = _db.Instances.FirstOrDefault(i => i.Id == task.InstanceId);
            var result = new
            {
                TaskId = task.Id,
                TaskStatus = task.Status.ToString(),
                Decision = task.Decision.ToString(),
                InstanceStatus = inst?.Status.ToString(),
                task.CompletedAt
            };

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _db.AddAsync(new Domain.Entities.WorkflowInbox
                {
                    IdempotencyKey = idempotencyKey,
                    HandlerName = nameof(MakeDecision),
                    ResponseJson = JsonSerializer.Serialize(result),
                    ProcessedAt = DateTime.UtcNow
                }, ct);
                await _db.SaveChangesAsync(ct);
            }

            if (transaction != null) await transaction.CommitAsync(ct);

            return Ok(ApiResponse<object>.Ok(result, _traceContext.TraceId));
        }
        catch (UnauthorizedAccessException ex)
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail("DECISION_FORBIDDEN", ex.Message, _traceContext.TraceId));
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var existing = _db.Inboxes.FirstOrDefault(i => i.HandlerName == nameof(MakeDecision) && i.IdempotencyKey == idempotencyKey);
            if (existing != null)
            {
                var cached = JsonSerializer.Deserialize<object>(existing.ResponseJson ?? "{}");
                return Ok(ApiResponse<object>.Ok(cached!, _traceContext.TraceId));
            }
            return Conflict(ApiResponse<object>.Fail("CONCURRENT_DECISION", "任务已被其他请求处理，请刷新", _traceContext.TraceId));
        }
        catch (Exception ex)
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            return BadRequest(ApiResponse<object>.Fail("DECISION_FAILED", ex.Message, _traceContext.TraceId));
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    private string CurrentUserCode => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("缺少用户身份");
}
