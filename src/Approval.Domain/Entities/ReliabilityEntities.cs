using Approval.Domain.Enums;

namespace Approval.Domain.Entities;

/// <summary>
/// 不可变操作审计日志实体
/// </summary>
public class WorkflowActionLog
{
    public long Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string OperatorCode { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public string Action { get; set; } = string.Empty; // Submit, Approve, Reject, Return, Delegate, Withdraw
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string? ClientIp { get; set; }
    public DateTime ActionTime { get; set; } = DateTime.UtcNow;

    public WorkflowInstance? Instance { get; set; }
}

/// <summary>
/// 发件箱事件实体 (Outbox Pattern)
/// </summary>
public class WorkflowOutbox
{
    public long Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 10;
    public DateTime NextRetryAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMsg { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ProcessingAt { get; set; }
    public string? LockId { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// 收件箱幂等去重实体 (Idempotency)
/// </summary>
public class WorkflowInbox
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// SAP 镜像同步状态跟踪实体
/// </summary>
public class SapSyncState
{
    public long Id { get; set; }
    public string CompanyId { get; set; } = string.Empty;
    public string ObjectCode { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string ExpectedStatus { get; set; } = string.Empty;
    public string? LastSyncedStatus { get; set; }
    public string SyncStatus { get; set; } = "Pending"; // Pending, Synced, Failed
    public DateTime? LastSyncAttempt { get; set; }
    public string? ErrorMessage { get; set; }
}
