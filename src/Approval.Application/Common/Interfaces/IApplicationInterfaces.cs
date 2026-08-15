using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;

namespace Approval.Application.Common.Interfaces;

public interface IApprovalDbContext
{
    IQueryable<WorkflowDefinition> Definitions { get; }
    IQueryable<WorkflowDefinitionVersion> DefinitionVersions { get; }
    IQueryable<WorkflowBinding> Bindings { get; }
    IQueryable<WorkflowRule> Rules { get; }
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
    IQueryable<SysNotification> Notifications { get; }
    IQueryable<SysUiLayout> UiLayouts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class;
}

public interface IWorkflowRuleMatcher
{
    /// <summary>
    /// 根据单据/主数据属性（制单人、部门、金额、自定义字段）匹配命中最高优先级的审批规则与流程版本
    /// </summary>
    Task<RuleMatchResult> MatchRuleAsync(
        string companyId,
        string objectCode,
        SapObjectPayload payload,
        CancellationToken ct = default);
}

public record RuleMatchResult(
    bool ShouldTrigger,
    string? TriggerReason,
    WorkflowRule? MatchedRule,
    string? TargetVersionId,
    string? TargetDefinitionId
);

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

public record FieldMetaInfo(
    string FieldName,
    string Description,
    string DataType,
    Dictionary<string, string>? ValidValues
);

public record ObjectMetadataResult(
    string ObjectCode,
    string TableName,
    Dictionary<string, FieldMetaInfo> HeaderFields,
    Dictionary<string, Dictionary<string, FieldMetaInfo>> ChildTableFields,
    Dictionary<string, string>? ChildTableDescriptions = null,
    string? ObjectDescription = null
);

public record CompanyInfoResult(
    string CompanyId,
    string CompanyName,
    string? Address,
    string? Phone
);

public interface ISapMetadataService
{
    /// <summary>
    /// 获取 SAP 公司真实全称/描述与基本信息 (从 OADM / Service Layer 提取)
    /// </summary>
    Task<CompanyInfoResult> GetCompanyInfoAsync(string companyId, CancellationToken ct = default);

    /// <summary>
    /// 从 SAP 数据库 (CUFD/UFD1/无对象表/系统字典) 获取单据及子表的所有动态字段中英文定义与下拉有效值描述字典 (多级缓存: 内存 -> 磁盘文件 -> 数据库)
    /// </summary>
    Task<ObjectMetadataResult> GetObjectMetadataAsync(string companyId, string objectCode, bool forceRefresh = false, CancellationToken ct = default);

    /// <summary>
    /// 全量拉取 SAP 元数据与全系统字典 (OEXD, OSLP, OCTG, OHEM, OSTC, CUFD, UFD1) 并持久化序列化落盘 (供定时调度器与手动即时刷新调用)
    /// </summary>
    Task RefreshAllMetadataAndSaveToDiskAsync(string companyId, CancellationToken ct = default);
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

    /// <summary>
    /// 发起人撤回/取消审批申请 (Revoke): 将处于审批中的流程终止并作废未决任务，向所有历史审批人精准发送撤销通知（排除发起人自己）
    /// </summary>
    Task<WorkflowInstance> RevokeWorkflowAsync(
        string instanceId,
        string operatorCode,
        string? operatorName,
        string? reason,
        CancellationToken ct = default);
}
