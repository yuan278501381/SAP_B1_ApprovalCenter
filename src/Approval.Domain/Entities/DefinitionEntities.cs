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
