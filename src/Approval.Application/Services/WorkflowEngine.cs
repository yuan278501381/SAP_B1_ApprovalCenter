using System.Globalization;
using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Domain.Services;
using TaskStatus = Approval.Domain.Enums.TaskStatus;

namespace Approval.Application.Services;

/// <summary>
/// 轻量审批状态机。当前可靠支持：条件分支、串行审批、同意、拒绝、退回。
/// 并行、会签、角色解析尚未实现，流程发布前会被明确拒绝，避免静默走错流程。
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private static readonly JsonSerializerOptions GraphJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IApprovalDbContext _db;
    private readonly ITraceContext _traceContext;

    public WorkflowEngine(IApprovalDbContext db, ITraceContext traceContext)
    {
        _db = db;
        _traceContext = traceContext;
    }

    public async Task<WorkflowInstance> StartWorkflowAsync(
        string companyId,
        string objectCode,
        string objectKey,
        string submitterCode,
        string? submitterName,
        SapObjectPayload payload,
        CancellationToken ct = default)
    {
        var existing = _db.Instances.FirstOrDefault(i =>
            i.CompanyId == companyId && i.ObjectCode == objectCode && i.ObjectKey == objectKey &&
            i.Status == WorkflowStatus.Running);
        if (existing != null)
            throw new InvalidOperationException($"该单据 [{objectCode}-{objectKey}] 已有运行中的审批实例 {existing.Id}");

        var binding = _db.Bindings
            .Where(b => b.CompanyId == companyId && b.ObjectCode == objectCode && b.IsActive)
            .OrderByDescending(b => b.Priority)
            .ToList()
            .FirstOrDefault(b => EvaluateExpression(b.ConditionExpr, payload.DocTotal));
        if (binding == null)
            throw new InvalidOperationException($"公司 {companyId} 的对象 {objectCode} 未配置可用审批流程");

        var version = _db.DefinitionVersions.FirstOrDefault(v =>
            v.Id == binding.VersionId && v.Status == "Published")
            ?? throw new InvalidOperationException($"绑定的流程版本 {binding.VersionId} 不存在或未发布");
        var graph = ParseAndValidateGraph(version.GraphJson);
        var start = graph.Nodes.Single(n => n.NodeType == NodeType.Start);
        var firstNode = ResolveNextExecutableNode(graph, start.NodeKey, payload.DocTotal, null);
        if (firstNode.NodeType != NodeType.Approval)
            throw new InvalidOperationException("流程必须至少包含一个人工审批节点");

        var (canonicalJson, sha256) = CanonicalSnapshotBuilder.Build(payload.RawJson);
        var now = DateTime.UtcNow;
        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = companyId,
            ObjectCode = objectCode,
            ObjectKey = objectKey,
            Title = string.IsNullOrWhiteSpace(payload.Title) ? $"{objectCode} #{objectKey}" : payload.Title,
            SubmitterCode = submitterCode,
            SubmitterName = submitterName,
            Status = WorkflowStatus.Running,
            CurrentVersionId = version.Id,
            CreatedAt = now
        };
        instance.Snapshot = new WorkflowSnapshot
        {
            InstanceId = instance.Id,
            RawJson = payload.RawJson,
            CanonicalJson = canonicalJson,
            DataSha256 = sha256,
            SnapshottedAt = now
        };

        var firstTask = AddApprovalTask(instance, firstNode, now);
        instance.ActionLogs.Add(new WorkflowActionLog
        {
            TraceId = _traceContext.TraceId,
            InstanceId = instance.Id,
            TaskId = firstTask.Id,
            OperatorCode = submitterCode,
            OperatorName = submitterName,
            Action = "Submit",
            FromStatus = "Draft",
            ToStatus = WorkflowStatus.Running.ToString(),
            Comment = "提交审批申请",
            ClientIp = _traceContext.ClientIp,
            ActionTime = now
        });

        await _db.AddAsync(instance, ct);
        await AddOutboxAsync(instance, "WorkflowStarted", "Pending", sha256, ct);
        await _db.SaveChangesAsync(ct);
        return instance;
    }

    public async Task<WorkflowTask> ProcessDecisionAsync(
        string taskId,
        string operatorCode,
        string? operatorName,
        TaskDecision decision,
        string? comments,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operatorCode))
            throw new UnauthorizedAccessException("审批操作缺少可信用户身份");

        var task = _db.Tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"未找到审批任务 {taskId}");
        if (task.Status != TaskStatus.Pending)
            throw new InvalidOperationException($"任务 {taskId} 已处于 {task.Status} 状态，无法重复审批");
        if (!_db.TaskCandidates.Any(c => c.TaskId == taskId && c.UserCode == operatorCode))
            throw new UnauthorizedAccessException($"用户 {operatorCode} 不是任务 {taskId} 的候选审批人");

        var instance = _db.Instances.FirstOrDefault(i => i.Id == task.InstanceId)
            ?? throw new InvalidOperationException($"未找到任务关联的流程实例 {task.InstanceId}");
        if (instance.Status != WorkflowStatus.Running)
            throw new InvalidOperationException($"流程实例已处于 {instance.Status} 状态");
        var node = _db.NodeInstances.FirstOrDefault(n => n.Id == task.NodeInstanceId)
            ?? throw new InvalidOperationException($"未找到节点实例 {task.NodeInstanceId}");
        var snapshot = _db.Snapshots.FirstOrDefault(s => s.InstanceId == instance.Id)
            ?? throw new InvalidOperationException("审批快照缺失，禁止继续流转");

        WorkflowGraphNode? nextNode = null;
        if (decision == TaskDecision.Approve)
        {
            var version = _db.DefinitionVersions.FirstOrDefault(v => v.Id == instance.CurrentVersionId)
                ?? throw new InvalidOperationException($"实例引用的流程版本 {instance.CurrentVersionId} 不存在");
            var graph = ParseAndValidateGraph(version.GraphJson);
            nextNode = ResolveNextExecutableNode(graph, node.NodeKey, ExtractDocTotal(snapshot.RawJson), "Approve");
        }

        var now = DateTime.UtcNow;
        task.Decision = decision;
        task.Comments = comments;
        task.CompletedBy = operatorCode;
        task.CompletedAt = now;
        task.Status = TaskStatus.Completed;
        node.Status = NodeStatus.Completed;
        node.CompletedAt = now;

        string toStatus;
        if (decision == TaskDecision.Reject)
        {
            instance.Status = WorkflowStatus.Rejected;
            instance.FinishedAt = now;
            toStatus = WorkflowStatus.Rejected.ToString();
            await AddOutboxAsync(instance, "InstanceRejected", toStatus, snapshot.DataSha256, ct);
        }
        else if (decision == TaskDecision.Return)
        {
            instance.Status = WorkflowStatus.Returned;
            instance.FinishedAt = now;
            toStatus = WorkflowStatus.Returned.ToString();
            await AddOutboxAsync(instance, "InstanceReturned", toStatus, snapshot.DataSha256, ct);
        }
        else if (nextNode!.NodeType == NodeType.End)
        {
            instance.Status = WorkflowStatus.Approved;
            instance.FinishedAt = now;
            toStatus = WorkflowStatus.Approved.ToString();
            await AddOutboxAsync(instance, "InstanceApproved", toStatus, snapshot.DataSha256, ct);
        }
        else
        {
            AddApprovalTask(instance, nextNode, now);
            toStatus = "Progressing";
        }

        await _db.AddAsync(new WorkflowActionLog
        {
            TraceId = _traceContext.TraceId,
            InstanceId = instance.Id,
            TaskId = task.Id,
            OperatorCode = operatorCode,
            OperatorName = operatorName,
            Action = decision.ToString(),
            FromStatus = WorkflowStatus.Running.ToString(),
            ToStatus = toStatus,
            Comment = comments,
            ClientIp = _traceContext.ClientIp,
            ActionTime = now
        }, ct);

        await _db.SaveChangesAsync(ct);
        return task;
    }

    private static WorkflowTask AddApprovalTask(WorkflowInstance instance, WorkflowGraphNode node, DateTime now)
    {
        if (node.NodeType != NodeType.Approval)
            throw new InvalidOperationException($"节点 {node.NodeKey} 不是人工审批节点");
        if (node.CandidateType != CandidateType.Direct)
            throw new NotSupportedException($"节点 {node.NodeKey} 使用了尚未实现的选人类型 {node.CandidateType}");
        var candidates = node.CandidateValues.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException($"审批节点 {node.NodeKey} 未配置审批人");

        var nodeInstance = new WorkflowNodeInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instance.Id,
            NodeKey = node.NodeKey,
            NodeName = node.Name,
            NodeType = node.NodeType,
            Status = NodeStatus.Active,
            StartedAt = now
        };
        var task = new WorkflowTask
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instance.Id,
            NodeInstanceId = nodeInstance.Id,
            TaskType = node.TaskType,
            Status = TaskStatus.Pending,
            CreatedAt = now,
            DueAt = now.AddDays(3)
        };
        foreach (var userCode in candidates)
            task.Candidates.Add(new WorkflowTaskCandidate { TaskId = task.Id, UserCode = userCode, UserName = userCode, CandidateType = CandidateType.Direct });

        nodeInstance.Tasks.Add(task);
        instance.NodeInstances.Add(nodeInstance);
        instance.Tasks.Add(task);
        return task;
    }

    private async Task AddOutboxAsync(WorkflowInstance instance, string eventType, string status, string hash, CancellationToken ct)
    {
        await _db.AddAsync(new WorkflowOutbox
        {
            TraceId = _traceContext.TraceId,
            EventType = eventType,
            AggregateId = instance.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                InstanceId = instance.Id,
                instance.CompanyId,
                instance.ObjectCode,
                instance.ObjectKey,
                Status = status,
                DataSha256 = hash
            }),
            Status = OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            NextRetryAt = DateTime.UtcNow
        }, ct);
    }

    private static WorkflowGraphDefinition ParseAndValidateGraph(string json)
    {
        var graph = JsonSerializer.Deserialize<WorkflowGraphDefinition>(json, GraphJsonOptions)
            ?? throw new InvalidOperationException("流程图 JSON 解析失败");
        if (graph.Nodes.Count == 0) throw new InvalidOperationException("流程图没有节点");
        if (graph.Nodes.GroupBy(n => n.NodeKey).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1))
            throw new InvalidOperationException("流程节点标识为空或重复");
        if (graph.Nodes.Count(n => n.NodeType == NodeType.Start) != 1)
            throw new InvalidOperationException("流程必须且只能有一个 Start 节点");
        var supported = new[] { NodeType.Start, NodeType.Condition, NodeType.Approval, NodeType.End };
        var unsupported = graph.Nodes.FirstOrDefault(n => !supported.Contains(n.NodeType));
        if (unsupported != null)
            throw new NotSupportedException($"节点 {unsupported.NodeKey} 的类型 {unsupported.NodeType} 尚未实现");
        if (graph.Edges.Any(e => !graph.Nodes.Any(n => n.NodeKey == e.FromNodeKey) || !graph.Nodes.Any(n => n.NodeKey == e.ToNodeKey)))
            throw new InvalidOperationException("流程连线引用了不存在的节点");
        return graph;
    }

    private static WorkflowGraphNode ResolveNextExecutableNode(
        WorkflowGraphDefinition graph,
        string fromNodeKey,
        decimal docTotal,
        string? decision)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentKey = fromNodeKey;
        var branch = decision;
        while (true)
        {
            if (!visited.Add(currentKey)) throw new InvalidOperationException("流程图存在循环，已停止流转");
            var edges = graph.Edges.Where(e => e.FromNodeKey == currentKey).ToList();
            var edge = edges.FirstOrDefault(e => string.Equals(e.ConditionValue, branch, StringComparison.OrdinalIgnoreCase))
                ?? edges.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.ConditionValue));
            if (edge == null) throw new InvalidOperationException($"节点 {currentKey} 没有匹配的后续连线");
            var node = graph.Nodes.Single(n => n.NodeKey == edge.ToNodeKey);
            if (node.NodeType is NodeType.Approval or NodeType.End) return node;
            if (node.NodeType == NodeType.Condition)
                branch = EvaluateExpression(node.ConditionExpression, docTotal) ? "True" : "False";
            else
                branch = null;
            currentKey = node.NodeKey;
        }
    }

    private static bool EvaluateExpression(string? expression, decimal docTotal)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        var normalized = expression.Replace(" ", string.Empty, StringComparison.Ordinal);
        const string field = "DocTotal";
        if (!normalized.StartsWith(field, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"当前条件表达式仅支持 DocTotal，实际为: {expression}");
        var rest = normalized[field.Length..];
        var op = new[] { ">=", "<=", "==", ">", "<" }.FirstOrDefault(rest.StartsWith)
            ?? throw new InvalidOperationException($"无法解析条件表达式: {expression}");
        if (!decimal.TryParse(rest[op.Length..], NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
            throw new InvalidOperationException($"无法解析条件金额: {expression}");
        return op switch
        {
            ">=" => docTotal >= expected,
            "<=" => docTotal <= expected,
            "==" => docTotal == expected,
            ">" => docTotal > expected,
            "<" => docTotal < expected,
            _ => false
        };
    }

    private static decimal ExtractDocTotal(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return 0m;
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Name.Equals("DocTotal", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetDecimal(out var value))
                return value;
        return 0m;
    }
}
