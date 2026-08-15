using Approval.Domain.Enums;

namespace Approval.Domain.Entities;

/// <summary>
/// 流程定义主表实体
/// </summary>
public class WorkflowDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public bool AllowSubmitterRevoke { get; set; } = true; // 默认允许发起人撤销
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkflowDefinitionVersion> Versions { get; set; } = new();
}

/// <summary>
/// 流程定义版本实体 (发布后不可变)
/// </summary>
public class WorkflowDefinitionVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DefinitionId { get; set; } = string.Empty;
    public int VersionNum { get; set; }
    public string GraphJson { get; set; } = "{}";
    public string Status { get; set; } = "Draft"; // Draft, Published, Deprecated
    public DateTime? PublishedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WorkflowDefinition? Definition { get; set; }
}

/// <summary>
/// 业务对象与流程版本绑定
/// </summary>
public class WorkflowBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public string ObjectCode { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public int Priority { get; set; } = 0;
    public string? ConditionExpr { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WorkflowDefinitionVersion? Version { get; set; }
}

/// <summary>
/// 审批触发与多维路由规则实体
/// </summary>
public class WorkflowRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = "DB_KCC";
    
    /// <summary>
    /// 业务对象编码 (单据如 CHORDR, CHOQUT, ORDR, OPOR; 主数据如 OCRD, OITM)
    /// </summary>
    public string ObjectCode { get; set; } = string.Empty;
    public string ObjectType { get; set; } = "Document"; // Document, MasterData
    public string RuleName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 触发方式: AutoAlways(无字段或默认总是触发), ExplicitCheckbox(仅当勾选字段为Y时触发)
    /// </summary>
    public string TriggerMode { get; set; } = "AutoAlways";

    /// <summary>
    /// 勾选字段名 (当 TriggerMode 为 ExplicitCheckbox 时有效，如 U_APSubmit)
    /// </summary>
    public string? TriggerFieldName { get; set; } = "U_APSubmit";

    /// <summary>
    /// 人员范围模式: All(全部用户), Whitelist(白名单), Blacklist(黑名单)
    /// </summary>
    public UserScopeMode UserScopeMode { get; set; } = UserScopeMode.All;
    
    /// <summary>
    /// 人员名单列表 JSON (例如 ["manager", "sales01"])
    /// </summary>
    public string UserScopeListJson { get; set; } = "[]";

    /// <summary>
    /// 部门范围名单 JSON (例如 ["Sales1", "Sales2"])
    /// </summary>
    public string DeptScopeListJson { get; set; } = "[]";

    /// <summary>
    /// 条件表达式 (例如 "DocTotal <= 50000", "DocTotal > 50000")
    /// </summary>
    public string? ConditionExpr { get; set; }

    /// <summary>
    /// 目标流程定义 ID
    /// </summary>
    public string TargetDefinitionId { get; set; } = string.Empty;

    /// <summary>
    /// 目标流程版本 ID (若为空则自动路由到该定义下最新已发布的版本)
    /// </summary>
    public string? TargetVersionId { get; set; }

    /// <summary>
    /// 优先级序号 (越小优先级越高，匹配到第一条后停止)
    /// </summary>
    public int Priority { get; set; } = 10;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public WorkflowDefinition? TargetDefinition { get; set; }
}
