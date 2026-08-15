using Approval.Domain.Enums;

namespace Approval.Domain.Entities;

/// <summary>
/// 流程审批实例聚合根
/// </summary>
public class WorkflowInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public string ObjectCode { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SubmitterCode { get; set; } = string.Empty;
    public string? SubmitterName { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Running;
    public string CurrentVersionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public byte[]? RowVersion { get; set; }

    public WorkflowDefinitionVersion? CurrentVersion { get; set; }
    public WorkflowSnapshot? Snapshot { get; set; }
    public List<WorkflowNodeInstance> NodeInstances { get; set; } = new();
    public List<WorkflowTask> Tasks { get; set; } = new();
    public List<WorkflowActionLog> ActionLogs { get; set; } = new();
}

/// <summary>
/// 规范化快照与防篡改指纹
/// </summary>
public class WorkflowSnapshot
{
    public string InstanceId { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public string CanonicalJson { get; set; } = string.Empty;
    public string DataSha256 { get; set; } = string.Empty;
    public DateTime SnapshottedAt { get; set; } = DateTime.UtcNow;

    public WorkflowInstance? Instance { get; set; }
}

/// <summary>
/// 节点实例
/// </summary>
public class WorkflowNodeInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string InstanceId { get; set; } = string.Empty;
    public string NodeKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public NodeType NodeType { get; set; } = NodeType.Approval;
    public NodeStatus Status { get; set; } = NodeStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public WorkflowInstance? Instance { get; set; }
    public List<WorkflowTask> Tasks { get; set; } = new();
}

/// <summary>
/// 审批任务实体
/// </summary>
public class WorkflowTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string InstanceId { get; set; } = string.Empty;
    public string NodeInstanceId { get; set; } = string.Empty;
    public TaskType TaskType { get; set; } = TaskType.Approve;
    public Approval.Domain.Enums.TaskStatus Status { get; set; } = Approval.Domain.Enums.TaskStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public TaskDecision? Decision { get; set; }
    public string? Comments { get; set; }
    public byte[]? RowVersion { get; set; }

    public WorkflowInstance? Instance { get; set; }
    public WorkflowNodeInstance? NodeInstance { get; set; }
    public List<WorkflowTaskCandidate> Candidates { get; set; } = new();
}

/// <summary>
/// 任务候选人/角色
/// </summary>
public class WorkflowTaskCandidate
{
    public long Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public CandidateType CandidateType { get; set; } = CandidateType.Direct;

    public WorkflowTask? Task { get; set; }
}
