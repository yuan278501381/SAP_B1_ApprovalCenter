using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;

namespace Approval.Application.Common.Interfaces;

public interface IApprovalDbContext
{
    IQueryable<WorkflowDefinition> Definitions { get; }
    IQueryable<WorkflowDefinitionVersion> DefinitionVersions { get; }
    IQueryable<WorkflowBinding> Bindings { get; }
    IQueryable<WorkflowInstance> Instances { get; }
    IQueryable<WorkflowSnapshot> Snapshots { get; }
    IQueryable<WorkflowNodeInstance> NodeInstances { get; }
    IQueryable<WorkflowTask> Tasks { get; }
    IQueryable<WorkflowTaskCandidate> TaskCandidates { get; }
    IQueryable<WorkflowActionLog> ActionLogs { get; }
    IQueryable<WorkflowOutbox> Outboxes { get; }
    IQueryable<WorkflowInbox> Inboxes { get; }
    IQueryable<SapSyncState> SapSyncStates { get; }
    IQueryable<SysUserMapping> UserMappings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class;
}

public interface IUserDirectoryService
{
    /// <summary>
    /// 解析审批候选人 (支持直接指定、直属主管追溯、岗位角色过滤、委托代理人)
    /// </summary>
    Task<List<string>> ResolveCandidatesAsync(
        CandidateType type,
        IEnumerable<string> candidateValues,
        string submitterCode,
        CancellationToken ct = default);
}

public interface ITraceContext
{
    string TraceId { get; }
    string? ClientIp { get; }
    string? CurrentUserCode { get; }
}

public interface IWorkflowEngine
{
    /// <summary>
    /// 提交审批实例并推进至首个待办节点
    /// </summary>
    Task<WorkflowInstance> StartWorkflowAsync(
        string companyId,
        string objectCode,
        string objectKey,
        string submitterCode,
        string? submitterName,
        SapObjectPayload payload,
        CancellationToken ct = default);

    /// <summary>
    /// 处理审批任务决定（同意/拒绝/退回）并驱动后续节点或结束
    /// </summary>
    Task<WorkflowTask> ProcessDecisionAsync(
        string taskId,
        string operatorCode,
        string? operatorName,
        TaskDecision decision,
        string? comments,
        CancellationToken ct = default);

    /// <summary>
    /// 任务转交 (Forward): 将当前任务直接移交给指定人
    /// </summary>
    Task ForwardTaskAsync(
        string taskId,
        string operatorCode,
        string? operatorName,
        string targetUserCode,
        string? targetUserName,
        string? comments,
        CancellationToken ct = default);
}
