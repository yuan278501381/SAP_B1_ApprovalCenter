namespace Approval.Domain.Enums;

/// <summary>
/// 审批流程实例整体状态
/// </summary>
public enum WorkflowStatus
{
    /// <summary>
    /// 运行流转中
    /// </summary>
    Running = 1,

    /// <summary>
    /// 审批通过并放行
    /// </summary>
    Approved = 2,

    /// <summary>
    /// 审批被拒绝
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// 发起人主动撤回 / 取消
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// 审批人退回给发起人；当前实例终止，可修改后重新提交新实例
    /// </summary>
    Returned = 5
}

/// <summary>
/// 节点类型
/// </summary>
public enum NodeType
{
    Start = 1,
    Approval = 2,
    Condition = 3,
    ParallelSplit = 4,
    ParallelJoin = 5,
    CC = 6,
    End = 7
}

/// <summary>
/// 节点流转状态
/// </summary>
public enum NodeStatus
{
    Pending = 0,
    Active = 1,
    Completed = 2,
    Skipped = 3
}

/// <summary>
/// 任务类型
/// </summary>
public enum TaskType
{
    /// <summary>
    /// 人工审批 (或签/排他)
    /// </summary>
    Approve = 1,

    /// <summary>
    /// 会签 (全部或N/M通过)
    /// </summary>
    Sign = 2,

    /// <summary>
    /// 抄送知会
    /// </summary>
    CC = 3
}

/// <summary>
/// 任务状态
/// </summary>
public enum TaskStatus
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3,
    Delegated = 4
}

/// <summary>
/// 审批决定
/// </summary>
public enum TaskDecision
{
    Approve = 1,
    Reject = 2,
    Return = 3
}

/// <summary>
/// 候选人类型
/// </summary>
public enum CandidateType
{
    Direct = 1,
    Role = 2,
    Manager = 3,
    Delegate = 4
}

/// <summary>
/// 发件箱消息状态
/// </summary>
public enum OutboxStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4
}
