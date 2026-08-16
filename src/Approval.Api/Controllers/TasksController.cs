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
    private readonly ILogger<TasksController> _logger;

    public TasksController(IWorkflowEngine engine, ApprovalDbContext db, ITraceContext traceContext, ILogger<TasksController> logger = null!)
    {
        _engine = engine;
        _db = db;
        _traceContext = traceContext;
        _logger = logger;
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
        var joinedQuery = from t in _db.Tasks
                          join i in _db.Instances on t.InstanceId equals i.Id
                          join n in _db.NodeInstances on t.NodeInstanceId equals n.Id
                          select new
                          {
                              Task = t,
                              Instance = i,
                              Node = n
                          };

        if (!string.IsNullOrWhiteSpace(companyId))
            joinedQuery = joinedQuery.Where(x => x.Instance.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(objectCode))
            joinedQuery = joinedQuery.Where(x => x.Instance.ObjectCode == objectCode);
        if (!string.IsNullOrWhiteSpace(objectKey))
            joinedQuery = joinedQuery.Where(x => x.Instance.ObjectKey == objectKey);

        if (status.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            joinedQuery = joinedQuery.Where(x => x.Task.Status == TaskStatus.Pending);
        }
        else if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            joinedQuery = joinedQuery.Where(x => x.Task.Status == TaskStatus.Completed);
        }

        // 关联候选人过滤
        if (scope.Equals("mine", StringComparison.OrdinalIgnoreCase))
        {
            var taskIds = _db.TaskCandidates
                .Where(c => c.UserCode == userCode)
                .Select(c => c.TaskId);

            joinedQuery = joinedQuery.Where(x => taskIds.Contains(x.Task.Id) || x.Task.CompletedBy == userCode);
        }

        var total = joinedQuery.Count();
        var resultItems = joinedQuery
            .OrderByDescending(x => x.Task.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                TaskId = x.Task.Id,
                InstanceId = x.Task.InstanceId,
                ObjectCode = x.Instance.ObjectCode,
                ObjectKey = x.Instance.ObjectKey,
                Title = x.Instance.Title,
                Submitter = x.Instance.SubmitterName ?? x.Instance.SubmitterCode,
                NodeName = x.Node.NodeName,
                TaskType = x.Task.TaskType.ToString(),
                Status = x.Task.Status.ToString(),
                x.Task.Decision,
                x.Task.CreatedAt,
                x.Task.DueAt,
                x.Task.CompletedAt
            })
            .ToList();

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
        var task = _db.Tasks.AsNoTracking().Include(t => t.Candidates).FirstOrDefault(t => t.Id == taskId);
        if (task == null)
        {
            return NotFound(ApiResponse<object>.Fail("TASK_NOT_FOUND", $"未找到任务 {taskId}", _traceContext.TraceId));
        }

        var isAdmin = string.Equals(CurrentUserCode, "admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(CurrentUserCode, "manager", StringComparison.OrdinalIgnoreCase);

        // 权限校验
        if (!isAdmin && !task.Candidates.Any(c => c.UserCode == CurrentUserCode) && task.CompletedBy != CurrentUserCode)
        {
            return Forbid();
        }

        var instance = _db.Instances.AsNoTracking().FirstOrDefault(i => i.Id == task.InstanceId);
        var snapshot = _db.Snapshots.AsNoTracking().FirstOrDefault(s => s.InstanceId == task.InstanceId);
        var node = _db.NodeInstances.AsNoTracking().FirstOrDefault(n => n.Id == task.NodeInstanceId);
        var logs = _db.ActionLogs.AsNoTracking().Where(l => l.InstanceId == task.InstanceId).OrderBy(l => l.ActionTime).ToList();

        // 千万级快照透明解压
        var rawJsonDecompressed = Approval.Application.Common.Helpers.SnapshotCompressionHelper.DecompressJson(snapshot?.RawJson);

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
                RawJson = rawJsonDecompressed,
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
        var userCode = CurrentUserCode;
        var userName = User.Identity?.Name;

        // 1. 幂等性校验 (若客户端提供了 Idempotency-Key 则进行严格幂等拦截)
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
            _logger.LogError(ex, "操作失败: {Message}", ex.Message);
            return StatusCode(500, ApiResponse<object>.Fail("SERVER_ERROR", "服务器内部错误", _traceContext.TraceId));
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// 转交任务给他人处理
    /// </summary>
    [HttpPost("{taskId}/forward")]
    public async Task<ActionResult<ApiResponse<object>>> ForwardTask(
        string taskId,
        [FromBody] ForwardRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        try
        {
            await _engine.ForwardTaskAsync(
                taskId,
                CurrentUserCode,
                CurrentUserName,
                request.TargetUserCode,
                request.TargetUserName,
                request.Comments,
                ct);

            return Ok(ApiResponse<object>.Ok(new { TaskId = taskId, Status = "Forwarded", request.TargetUserCode }, _traceContext.TraceId));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("FORWARD_FORBIDDEN", ex.Message, _traceContext.TraceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "操作失败: {Message}", ex.Message);
            return StatusCode(500, ApiResponse<object>.Fail("SERVER_ERROR", "服务器内部错误", _traceContext.TraceId));
        }
    }

    private string CurrentUserCode => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("缺少用户身份");

    private string? CurrentUserName => User.FindFirstValue(ClaimTypes.Name);
}

public record ForwardRequest(string TargetUserCode, string? TargetUserName, string? Comments);
