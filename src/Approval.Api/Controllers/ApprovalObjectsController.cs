using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Approval.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Approval.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/objects")]
public class ApprovalObjectsController : ControllerBase
{
    private readonly IWorkflowEngine _engine;
    private readonly ISapAdapterRegistry _adapterRegistry;
    private readonly ApprovalDbContext _db;
    private readonly ITraceContext _traceContext;

    public ApprovalObjectsController(
        IWorkflowEngine engine,
        ISapAdapterRegistry adapterRegistry,
        ApprovalDbContext db,
        ITraceContext traceContext)
    {
        _engine = engine;
        _adapterRegistry = adapterRegistry;
        _db = db;
        _traceContext = traceContext;
    }

    /// <summary>
    /// 显式发起单据审批
    /// </summary>
    [HttpPost("{objectCode}/{objectKey}/submit")]
    public async Task<ActionResult<ApiResponse<object>>> SubmitApproval(
        string objectCode,
        string objectKey,
        [FromQuery] string companyId = "DB_KCC",
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(ApiResponse<object>.Fail("IDEMPOTENCY_KEY_REQUIRED", "发起审批必须提供 Idempotency-Key", _traceContext.TraceId));

        // 1. 幂等性校验 (Inbox)
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingInbox = _db.Inboxes.FirstOrDefault(i => i.HandlerName == nameof(SubmitApproval) && i.IdempotencyKey == idempotencyKey);
            if (existingInbox != null)
            {
                var cachedResult = JsonSerializer.Deserialize<object>(existingInbox.ResponseJson ?? "{}");
                return Ok(ApiResponse<object>.Ok(cachedResult!, _traceContext.TraceId));
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (_db.Database.IsRelational())
                transaction = await _db.Database.BeginTransactionAsync(ct);

            // 2. 通过 Adapter 抓取单据完整数据
            var adapter = _adapterRegistry.GetAdapter(objectCode);
            var payload = await adapter.FetchObjectAsync(companyId, objectKey, ct);

            // 3. 驱动流程引擎启动
            var instance = await _engine.StartWorkflowAsync(
                companyId,
                objectCode,
                objectKey,
                User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                User.Identity?.Name,
                payload,
                ct);

            var result = new
            {
                InstanceId = instance.Id,
                Status = instance.Status.ToString(),
                instance.Title,
                instance.CreatedAt,
                Tasks = instance.Tasks.Select(t => new { t.Id, t.TaskType, t.Status }).ToList()
            };

            // 4. 记录幂等结果
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _db.AddAsync(new Domain.Entities.WorkflowInbox
                {
                    IdempotencyKey = idempotencyKey,
                    HandlerName = nameof(SubmitApproval),
                    ResponseJson = JsonSerializer.Serialize(result),
                    ProcessedAt = DateTime.UtcNow
                }, ct);
                await _db.SaveChangesAsync(ct);
            }

            if (transaction != null) await transaction.CommitAsync(ct);

            return Ok(ApiResponse<object>.Ok(result, _traceContext.TraceId));
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var existing = _db.Inboxes.FirstOrDefault(i => i.HandlerName == nameof(SubmitApproval) && i.IdempotencyKey == idempotencyKey);
            if (existing != null)
            {
                var cached = JsonSerializer.Deserialize<object>(existing.ResponseJson ?? "{}");
                return Ok(ApiResponse<object>.Ok(cached!, _traceContext.TraceId));
            }
            return Conflict(ApiResponse<object>.Fail("CONCURRENT_SUBMIT", "并发提交冲突，请查询单据审批状态", _traceContext.TraceId));
        }
        catch (Exception ex)
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            return BadRequest(ApiResponse<object>.Fail("SUBMIT_FAILED", ex.Message, _traceContext.TraceId));
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// 获取单据当前审批状态与快照摘要 (供 SAP 内嵌 Web 控件加载)
    /// </summary>
    [HttpGet("{objectCode}/{objectKey}/approval")]
    public ActionResult<ApiResponse<object>> GetApprovalStatus(
        string objectCode,
        string objectKey,
        [FromQuery] string companyId = "DB_KCC")
    {
        var instance = _db.Instances
            .Where(i => i.CompanyId == companyId && i.ObjectCode == objectCode && i.ObjectKey == objectKey)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefault();

        if (instance == null)
        {
            return Ok(ApiResponse<object>.Ok(new
            {
                HasApproval = false,
                Status = "Draft",
                Message = "尚未发起审批"
            }, _traceContext.TraceId));
        }

        var snapshot = _db.Snapshots.FirstOrDefault(s => s.InstanceId == instance.Id);
        var tasks = _db.Tasks.Where(t => t.InstanceId == instance.Id).ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            HasApproval = true,
            InstanceId = instance.Id,
            Status = instance.Status.ToString(),
            instance.Title,
            instance.CreatedAt,
            instance.FinishedAt,
            DataSha256 = snapshot?.DataSha256,
            PendingTasks = tasks.Where(t => t.Status == Domain.Enums.TaskStatus.Pending)
                .Select(t => new { t.Id, t.TaskType, t.CreatedAt, t.DueAt }).ToList()
        }, _traceContext.TraceId));
    }
}
