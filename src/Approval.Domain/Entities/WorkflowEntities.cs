using Approval.Domain.Enums;
using System.ComponentModel.DataAnnotations;

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
    public string? TargetDocType { get; set; }     // 如果是草稿/凭证批，记录目标单据类型 (如 '13' 应收发票, '20' 采购收货)
    public string? PostedDocEntry { get; set; }    // 审批通过并成功过账后的正式单据 DocEntry
    public string? PostedDocNum { get; set; }      // 审批通过并成功过账后的正式单据 DocNum
    /// <summary>乐观并发控制令牌</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public WorkflowDefinitionVersion? CurrentVersion { get; set; }
    public WorkflowSnapshot? Snapshot { get; set; }
    public List<WorkflowNodeInstance> NodeInstances { get; set; } = new();
    public List<WorkflowTask> Tasks { get; set; } = new();
    public List<WorkflowActionLog> ActionLogs { get; set; } = new();

    public WorkflowInstance() { } // For EF Core

    public static WorkflowInstance Create(string companyId, string objectCode, string objectKey, string title, string submitterCode, string? submitterName, string currentVersionId, DateTime createdAt)
    {
        return new WorkflowInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = companyId,
            ObjectCode = objectCode,
            ObjectKey = objectKey,
            Title = title,
            SubmitterCode = submitterCode,
            SubmitterName = submitterName,
            Status = WorkflowStatus.Running,
            CurrentVersionId = currentVersionId,
            CreatedAt = createdAt
        };
    }

    public void AddSnapshot(WorkflowSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public void MarkSuperseded(DateTime finishedAt)
    {
        if (Status != WorkflowStatus.Running) throw new InvalidOperationException($"实例状态 {Status} 不允许此操作");
        Status = WorkflowStatus.Superceded;
        FinishedAt = finishedAt;
    }

    public void MarkRejected(DateTime finishedAt)
    {
        if (Status != WorkflowStatus.Running) throw new InvalidOperationException($"实例状态 {Status} 不允许此操作");
        Status = WorkflowStatus.Rejected;
        FinishedAt = finishedAt;
    }

    public void MarkReturned(DateTime finishedAt)
    {
        if (Status != WorkflowStatus.Running) throw new InvalidOperationException($"实例状态 {Status} 不允许此操作");
        Status = WorkflowStatus.Returned;
        FinishedAt = finishedAt;
    }

    public void MarkApproved(DateTime finishedAt)
    {
        if (Status != WorkflowStatus.Running) throw new InvalidOperationException($"实例状态 {Status} 不允许此操作");
        Status = WorkflowStatus.Approved;
        FinishedAt = finishedAt;
    }

    public void MarkCancelled(DateTime finishedAt)
    {
        if (Status != WorkflowStatus.Running) throw new InvalidOperationException($"实例状态 {Status} 不允许此操作");
        Status = WorkflowStatus.Cancelled;
        FinishedAt = finishedAt;
    }

    public void SetPostedDocument(string? docEntry, string? docNum)
    {
        PostedDocEntry = docEntry;
        PostedDocNum = docNum;
    }
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

    public WorkflowNodeInstance() { }

    public static WorkflowNodeInstance Create(string instanceId, string nodeKey, string nodeName, NodeType nodeType, DateTime startedAt)
    {
        return new WorkflowNodeInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            NodeKey = nodeKey,
            NodeName = nodeName,
            NodeType = nodeType,
            Status = NodeStatus.Active,
            StartedAt = startedAt
        };
    }

    public void Complete(DateTime completedAt)
    {
        Status = NodeStatus.Completed;
        CompletedAt = completedAt;
    }
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

    public WorkflowTask() { } // For EF Core

    public static WorkflowTask Create(string instanceId, string nodeInstanceId, TaskType taskType, DateTime createdAt, DateTime? dueAt)
    {
        return new WorkflowTask
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            NodeInstanceId = nodeInstanceId,
            TaskType = taskType,
            Status = Approval.Domain.Enums.TaskStatus.Pending,
            CreatedAt = createdAt,
            DueAt = dueAt
        };
    }

    public void Approve(string operatorCode, string? comments, DateTime completedAt)
    {
        if (Status != Approval.Domain.Enums.TaskStatus.Pending)
            throw new InvalidOperationException($"任务状态 {Status} 不允许审批");
        Status = Approval.Domain.Enums.TaskStatus.Completed;
        Decision = TaskDecision.Approve;
        CompletedBy = operatorCode;
        Comments = comments;
        CompletedAt = completedAt;
    }

    public void Reject(string operatorCode, string? comments, DateTime completedAt)
    {
        if (Status != Approval.Domain.Enums.TaskStatus.Pending)
            throw new InvalidOperationException($"任务状态 {Status} 不允许驳回");
        Status = Approval.Domain.Enums.TaskStatus.Completed;
        Decision = TaskDecision.Reject;
        CompletedBy = operatorCode;
        Comments = comments;
        CompletedAt = completedAt;
    }

    public void Return(string operatorCode, string? comments, DateTime completedAt)
    {
        if (Status != Approval.Domain.Enums.TaskStatus.Pending)
            throw new InvalidOperationException($"任务状态 {Status} 不允许退回");
        Status = Approval.Domain.Enums.TaskStatus.Completed;
        Decision = TaskDecision.Return;
        CompletedBy = operatorCode;
        Comments = comments;
        CompletedAt = completedAt;
    }

    public void Cancel(string? comments)
    {
        if (Status != Approval.Domain.Enums.TaskStatus.Pending)
            throw new InvalidOperationException($"任务状态 {Status} 不允许作废");
        Status = Approval.Domain.Enums.TaskStatus.Cancelled;
        Comments = comments;
    }
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
