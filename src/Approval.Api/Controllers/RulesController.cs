using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Approval.Api.Controllers;

public record RuleDto(
    string? Id,
    string CompanyId,
    string ObjectCode,
    string ObjectType,
    string RuleName,
    string? Description,
    string TriggerMode,
    string? TriggerFieldName,
    UserScopeMode UserScopeMode,
    List<string>? UserScopeList,
    List<string>? DeptScopeList,
    string? ConditionExpr,
    string TargetDefinitionId,
    string? TargetVersionId,
    int Priority,
    bool IsActive
);

public record RuleTestRequest(
    string CompanyId,
    string ObjectCode,
    string CreatorUserCode,
    string? Department,
    decimal DocTotal,
    Dictionary<string, object?>? HeaderFields,
    string? RawJson = null
);

[ApiController]
[Authorize]
[Route("api/v1/rules")]
public class RulesController : ControllerBase
{
    private readonly ApprovalDbContext _db;
    private readonly IWorkflowRuleMatcher _ruleMatcher;
    private readonly ITraceContext _traceContext;
    private readonly ILogger<RulesController> _logger;

    public RulesController(ApprovalDbContext db, IWorkflowRuleMatcher ruleMatcher, ITraceContext traceContext, ILogger<RulesController> logger)
    {
        _db = db;
        _ruleMatcher = ruleMatcher;
        _traceContext = traceContext;
        _logger = logger;
    }

    /// <summary>
    /// 查询审批触发与多维路由规则列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> GetRules(
        [FromQuery] string? companyId = "DB_KCC",
        [FromQuery] string? objectCode = null,
        CancellationToken ct = default)
    {
        var query = _db.Rules.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(companyId))
            query = query.Where(r => r.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(objectCode))
            query = query.Where(r => r.ObjectCode == objectCode);

        var list = await query.OrderBy(r => r.Priority).ToListAsync(ct);
        var defs = await _db.Definitions.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var result = list.Select(r => new
        {
            r.Id,
            r.CompanyId,
            r.ObjectCode,
            r.ObjectType,
            r.RuleName,
            r.Description,
            r.TriggerMode,
            r.TriggerFieldName,
            UserScopeMode = r.UserScopeMode.ToString(),
            UserScopeList = ParseList(r.UserScopeListJson),
            DeptScopeList = ParseList(r.DeptScopeListJson),
            r.ConditionExpr,
            r.TargetDefinitionId,
            TargetDefinitionName = defs.TryGetValue(r.TargetDefinitionId, out var name) ? name : r.TargetDefinitionId,
            r.TargetVersionId,
            r.Priority,
            r.IsActive,
            r.CreatedAt,
            r.UpdatedAt
        });

        return Ok(ApiResponse<object>.Ok(result, _traceContext.TraceId));
    }

    /// <summary>
    /// 创建新规则
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<object>>> CreateRule([FromBody] RuleDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.RuleName))
            return BadRequest(ApiResponse<object>.Fail("RULE_NAME_REQUIRED", "规则名称不能为空", _traceContext.TraceId));
        if (string.IsNullOrWhiteSpace(dto.ObjectCode))
            return BadRequest(ApiResponse<object>.Fail("OBJECT_CODE_REQUIRED", "单据/主数据编码不能为空", _traceContext.TraceId));
        if (string.IsNullOrWhiteSpace(dto.TargetDefinitionId))
            return BadRequest(ApiResponse<object>.Fail("TARGET_DEF_REQUIRED", "必须选择目标流程定义", _traceContext.TraceId));

        var rule = new WorkflowRule
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id,
            CompanyId = string.IsNullOrWhiteSpace(dto.CompanyId) ? "DB_KCC" : dto.CompanyId,
            ObjectCode = dto.ObjectCode.Trim().ToUpperInvariant(),
            ObjectType = string.IsNullOrWhiteSpace(dto.ObjectType) ? "Document" : dto.ObjectType,
            RuleName = dto.RuleName.Trim(),
            Description = dto.Description,
            TriggerMode = string.IsNullOrWhiteSpace(dto.TriggerMode) ? "AutoAlways" : dto.TriggerMode,
            TriggerFieldName = dto.TriggerFieldName,
            UserScopeMode = dto.UserScopeMode,
            UserScopeListJson = JsonSerializer.Serialize(dto.UserScopeList ?? new List<string>()),
            DeptScopeListJson = JsonSerializer.Serialize(dto.DeptScopeList ?? new List<string>()),
            ConditionExpr = dto.ConditionExpr?.Trim(),
            TargetDefinitionId = dto.TargetDefinitionId,
            TargetVersionId = dto.TargetVersionId,
            Priority = dto.Priority,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _db.Rules.AddAsync(rule, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { rule.Id, rule.RuleName }, _traceContext.TraceId));
    }

    /// <summary>
    /// 更新规则
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateRule(string id, [FromBody] RuleDto dto, CancellationToken ct = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null)
            return NotFound(ApiResponse<object>.Fail("RULE_NOT_FOUND", $"未找到规则 {id}", _traceContext.TraceId));

        rule.RuleName = dto.RuleName.Trim();
        rule.ObjectCode = dto.ObjectCode.Trim().ToUpperInvariant();
        rule.ObjectType = dto.ObjectType;
        rule.Description = dto.Description;
        rule.TriggerMode = dto.TriggerMode;
        rule.TriggerFieldName = dto.TriggerFieldName;
        rule.UserScopeMode = dto.UserScopeMode;
        rule.UserScopeListJson = JsonSerializer.Serialize(dto.UserScopeList ?? new List<string>());
        rule.DeptScopeListJson = JsonSerializer.Serialize(dto.DeptScopeList ?? new List<string>());
        rule.ConditionExpr = dto.ConditionExpr?.Trim();
        rule.TargetDefinitionId = dto.TargetDefinitionId;
        rule.TargetVersionId = dto.TargetVersionId;
        rule.Priority = dto.Priority;
        rule.IsActive = dto.IsActive;
        rule.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { rule.Id, rule.RuleName, Status = "Updated" }, _traceContext.TraceId));
    }

    /// <summary>
    /// 删除规则
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteRule(string id, CancellationToken ct = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null)
            return NotFound(ApiResponse<object>.Fail("RULE_NOT_FOUND", $"未找到规则 {id}", _traceContext.TraceId));

        _db.Rules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { id, Status = "Deleted" }, _traceContext.TraceId));
    }

    /// <summary>
    /// 在线模拟单据测试规则命中与路由结果
    /// </summary>
    [HttpPost("test-match")]
    public async Task<ActionResult<ApiResponse<object>>> TestMatch([FromBody] RuleTestRequest req, CancellationToken ct = default)
    {
        var headers = req.HeaderFields ?? new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(req.Department))
            headers["Department"] = req.Department;

        var payload = new SapObjectPayload
        {
            CompanyId = req.CompanyId,
            ObjectCode = req.ObjectCode,
            ObjectKey = "TEST_SIMULATION",
            Title = $"{req.ObjectCode} 模拟单据",
            CreatorUserCode = req.CreatorUserCode,
            DocTotal = req.DocTotal,
            RawJson = req.RawJson ?? "{}",
            HeaderFields = headers
        };

        var matchResult = await _ruleMatcher.MatchRuleAsync(req.CompanyId, req.ObjectCode, payload, ct);
        return Ok(ApiResponse<object>.Ok(new
        {
            matchResult.ShouldTrigger,
            matchResult.TriggerReason,
            MatchedRuleId = matchResult.MatchedRule?.Id,
            MatchedRuleName = matchResult.MatchedRule?.RuleName,
            matchResult.TargetDefinitionId,
            matchResult.TargetVersionId
        }, _traceContext.TraceId));
    }

    private List<string> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<string>();
        try 
        { 
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); 
        }
        catch (Exception ex)
        { 
            _logger.LogWarning(ex, "JSON 反序列化候选列表失败，采用安全降级策略返回空集合: {Json}", json);
            return new List<string>(); 
        }
    }
}
