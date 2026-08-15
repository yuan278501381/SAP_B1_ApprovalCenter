using Approval.Domain.Enums;

namespace Approval.Application.Common.Models;

/// <summary>
/// 流程定义图拓扑模型
/// </summary>
public class WorkflowGraphDefinition
{
    public bool AllowSubmitterRevoke { get; set; } = true;
    public List<WorkflowGraphNode> Nodes { get; set; } = new();
    public List<WorkflowGraphEdge> Edges { get; set; } = new();
}

public class WorkflowGraphNode
{
    public string NodeKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public NodeType NodeType { get; set; } = NodeType.Approval;
    public TaskType TaskType { get; set; } = TaskType.Approve;
    public CandidateType CandidateType { get; set; } = CandidateType.Direct;
    public List<string> CandidateValues { get; set; } = new(); // 用户工号或角色标识
    public string? ConditionExpression { get; set; } // 针对 Condition 节点，如 "DocTotal > 50000"
}

public class WorkflowGraphEdge
{
    public string FromNodeKey { get; set; } = string.Empty;
    public string ToNodeKey { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? ConditionValue { get; set; } // 审批结果或条件判断结果: "Approve", "Reject", "True", "False"
}
