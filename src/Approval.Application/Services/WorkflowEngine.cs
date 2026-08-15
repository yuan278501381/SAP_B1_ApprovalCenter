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
/// 企业级审批工作流引擎。
/// 支持：条件分支、动态多选人(Direct/Manager/Role/Delegate)、串行与会签、同意/拒绝/退回/转交。
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    private static readonly JsonSerializerOptions GraphJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IApprovalDbContext _db;
    private readonly ITraceContext _traceContext;
    private readonly IUserDirectoryService _userDirectoryService;
    private readonly IWorkflowRuleMatcher _ruleMatcher;

    public WorkflowEngine(
        IApprovalDbContext db,
        ITraceContext traceContext,
        IUserDirectoryService userDirectoryService,
        IWorkflowRuleMatcher ruleMatcher)
    {
        _db = db;
        _traceContext = traceContext;
        _userDirectoryService = userDirectoryService;
        _ruleMatcher = ruleMatcher;
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
        var (canonicalJson, sha256) = CanonicalSnapshotBuilder.Build(payload.RawJson);
        var now = DateTime.UtcNow;

        // 1. 检查是否存在正在流转中的审批实例
        var existing = _db.Instances.FirstOrDefault(i =>
            i.CompanyId == companyId && i.ObjectCode == objectCode && i.ObjectKey == objectKey &&
            i.Status == WorkflowStatus.Running);

        if (existing != null)
        {
            var oldSnapshot = _db.Snapshots.FirstOrDefault(s => s.InstanceId == existing.Id);
            // 若单据内容未变，拒绝重复提交
            if (oldSnapshot != null && oldSnapshot.DataSha256 == sha256)
            {
                throw new InvalidOperationException($"该单据 [{objectCode}-{objectKey}] 已处于审批流转中 (实例ID: {existing.Id})，且内容无变化，无需重复提交");
            }

            // 若单据在审批中被修改 (如金额/行项目变更) -> 触发【改单重路由 (Dynamic Re-routing)】
            existing.Status = WorkflowStatus.Superceded;
            existing.FinishedAt = now;

            // 取消旧实例下未完成的任务
            var pendingTasks = _db.Tasks.Where(t => t.InstanceId == existing.Id && t.Status == TaskStatus.Pending).ToList();
            foreach (var pt in pendingTasks)
            {
                pt.Status = TaskStatus.Cancelled;
                pt.Comments = "因单据数据变更作废";
            }

            existing.ActionLogs.Add(new WorkflowActionLog
            {
                TraceId = _traceContext.TraceId,
                InstanceId = existing.Id,
                OperatorCode = submitterCode,
                OperatorName = submitterName,
                Action = "Superceded",
                FromStatus = WorkflowStatus.Running.ToString(),
                ToStatus = WorkflowStatus.Superceded.ToString(),
                Comment = $"单据数据被修改（最新金额: {payload.DocTotal:N2}），原审批流自动作废，系统重新评估规则矩阵并启动新审批流",
                ClientIp = _traceContext.ClientIp,
                ActionTime = now
            });
        }

        // 2. 驱动规则匹配矩阵引擎 (Rule Matcher)，确定目标流程版本
        var matchResult = await _ruleMatcher.MatchRuleAsync(companyId, objectCode, payload, ct);
        if (!matchResult.ShouldTrigger || string.IsNullOrWhiteSpace(matchResult.TargetVersionId))
        {
            throw new InvalidOperationException(matchResult.TriggerReason ?? $"对象 {objectCode} 未满足审批触发条件或处于免审状态");
        }

        var version = _db.DefinitionVersions.FirstOrDefault(v =>
            v.Id == matchResult.TargetVersionId && v.Status == "Published")
            ?? throw new InvalidOperationException($"匹配的流程版本 {matchResult.TargetVersionId} 不存在或未发布");

        var graph = ParseAndValidateGraph(version.GraphJson);
        var start = graph.Nodes.Single(n => n.NodeType == NodeType.Start);
        var firstNode = ResolveNextExecutableNode(graph, start.NodeKey, payload.DocTotal, null);
        if (firstNode.NodeType != NodeType.Approval)
            throw new InvalidOperationException("流程必须至少包含一个人工审批节点");

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
            RawJson = Approval.Application.Common.Helpers.SnapshotCompressionHelper.CompressJson(payload.RawJson),
            CanonicalJson = canonicalJson,
            DataSha256 = sha256,
            SnapshottedAt = now
        };

        var firstTask = await AddApprovalTaskAsync(instance, firstNode, now, ct);
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
            Comment = $"提交审批申请 ({matchResult.TriggerReason})",
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

        var isSuperAdmin = string.Equals(operatorCode, "admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(operatorCode, "manager", StringComparison.OrdinalIgnoreCase);

        if (!isSuperAdmin && !_db.TaskCandidates.Any(c => c.TaskId == taskId && c.UserCode == operatorCode))
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
            await AddApprovalTaskAsync(instance, nextNode, now, ct);
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

    public async Task ForwardTaskAsync(
        string taskId,
        string operatorCode,
        string? operatorName,
        string targetUserCode,
        string? targetUserName,
        string? comments,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operatorCode))
            throw new UnauthorizedAccessException("操作缺少可信用户身份");
        if (string.IsNullOrWhiteSpace(targetUserCode))
            throw new ArgumentException("转交目标用户不能为空");

        var task = _db.Tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"未找到审批任务 {taskId}");
        if (task.Status != TaskStatus.Pending)
            throw new InvalidOperationException($"任务 {taskId} 已处于 {task.Status} 状态，无法转交");

        var isCandidate = _db.TaskCandidates.Any(c => c.TaskId == taskId && c.UserCode == operatorCode);
        if (!isCandidate)
            throw new UnauthorizedAccessException($"用户 {operatorCode} 不是任务 {taskId} 的当前处理人，无权转交");

        var instance = _db.Instances.FirstOrDefault(i => i.Id == task.InstanceId)
            ?? throw new InvalidOperationException($"未找到任务关联的流程实例 {task.InstanceId}");

        var now = DateTime.UtcNow;

        // 添加目标用户为候选人
        if (!_db.TaskCandidates.Any(c => c.TaskId == taskId && c.UserCode == targetUserCode))
        {
            await _db.AddAsync(new WorkflowTaskCandidate
            {
                TaskId = task.Id,
                UserCode = targetUserCode,
                UserName = targetUserName ?? targetUserCode,
                CandidateType = CandidateType.Delegate
            }, ct);
        }

        // 记录转交审计日志
        await _db.AddAsync(new WorkflowActionLog
        {
            TraceId = _traceContext.TraceId,
            InstanceId = instance.Id,
            TaskId = task.Id,
            OperatorCode = operatorCode,
            OperatorName = operatorName,
            Action = "Forward",
            FromStatus = "Pending",
            ToStatus = "Pending",
            Comment = $"由 {operatorName ?? operatorCode} 转交给 {targetUserName ?? targetUserCode} 处理：{comments}",
            ClientIp = _traceContext.ClientIp,
            ActionTime = now
        }, ct);

        await _db.SaveChangesAsync(ct);
    }

    public async Task<WorkflowInstance> RevokeWorkflowAsync(
        string instanceId,
        string operatorCode,
        string? operatorName,
        string? reason,
        CancellationToken ct = default)
    {
        var instance = _db.Instances.FirstOrDefault(i => i.Id == instanceId)
            ?? throw new KeyNotFoundException($"审批实例 {instanceId} 不存在");

        if (instance.Status != WorkflowStatus.Running)
        {
            throw new InvalidOperationException($"当前单据处于 [{instance.Status}] 状态，仅允许撤回流转中 (Running) 的审批申请");
        }

        // 1. 发起人权限校验 (仅允许制单人本人或具有超级管理权限的账号撤销)
        var isSubmitter = string.Equals(instance.SubmitterCode, operatorCode, StringComparison.OrdinalIgnoreCase);
        var isAdmin = string.Equals(operatorCode, "admin", StringComparison.OrdinalIgnoreCase);
        if (!isSubmitter && !isAdmin)
        {
            throw new InvalidOperationException($"无权撤销：您不是该单据的发起人 ({instance.SubmitterCode})，禁止撤销他人的审批申请");
        }

        // 2. 流程模型撤回策略校验 (AllowSubmitterRevoke)
        var version = _db.DefinitionVersions.FirstOrDefault(v => v.Id == instance.CurrentVersionId);
        if (version != null)
        {
            var graph = ParseAndValidateGraph(version.GraphJson);
            if (!graph.AllowSubmitterRevoke && !isAdmin)
            {
                throw new InvalidOperationException("该审批流程已由系统管理员配置禁止发起人主动撤销申请");
            }
        }

        var now = DateTime.UtcNow;

        // 3. 状态机流转与任务作废
        instance.Status = WorkflowStatus.Cancelled;
        instance.FinishedAt = now;

        var pendingTasks = _db.Tasks.Where(t => t.InstanceId == instance.Id && t.Status == TaskStatus.Pending).ToList();
        var pendingTaskIds = pendingTasks.Select(t => t.Id).ToList();
        foreach (var task in pendingTasks)
        {
            task.Status = TaskStatus.Cancelled;
            task.Comments = $"发起人撤回: {reason ?? "无"}";
        }

        // 4. 全链路审计日志
        instance.ActionLogs.Add(new WorkflowActionLog
        {
            TraceId = _traceContext.TraceId,
            InstanceId = instance.Id,
            OperatorCode = operatorCode,
            OperatorName = operatorName,
            Action = "Revoke",
            FromStatus = WorkflowStatus.Running.ToString(),
            ToStatus = WorkflowStatus.Cancelled.ToString(),
            Comment = string.IsNullOrWhiteSpace(reason) ? "发起人主动撤回审批申请" : $"发起人主动撤回: {reason}",
            ClientIp = _traceContext.ClientIp,
            ActionTime = now
        });

        // 5. 精准收集所有“走过的人”并执行核心排除过滤器 (Exclusion Filter: 排除取消人自己)
        var historicalOperators = _db.ActionLogs
            .Where(l => l.InstanceId == instance.Id && !string.IsNullOrWhiteSpace(l.OperatorCode))
            .Select(l => l.OperatorCode)
            .ToList();

        var pendingCandidateCodes = _db.TaskCandidates
            .Where(c => pendingTaskIds.Contains(c.TaskId) && !string.IsNullOrWhiteSpace(c.UserCode))
            .Select(c => c.UserCode)
            .ToList();

        var allInvolvedUsers = historicalOperators
            .Concat(pendingCandidateCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 核心排除过滤：绝对不通知发起人（取消人本人）
        var notifiedRecipients = allInvolvedUsers
            .Where(u => !string.Equals(u, operatorCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var submitterDisplayName = string.IsNullOrWhiteSpace(operatorName) ? operatorCode : $"{operatorName} ({operatorCode})";
        var notifTitle = $"【撤回通知】{instance.Title} 审批申请已被发起人撤回";
        var notifContent = $"单据 [{instance.ObjectCode} #{instance.ObjectKey}] 发起人 {submitterDisplayName} 已主动撤销审批申请。\n撤销原因：{reason ?? "未填写"}\n相关的待办审批任务已自动关闭，特此知会。";

        foreach (var recipient in notifiedRecipients)
        {
            await _db.AddAsync(new SysNotification
            {
                Id = Guid.NewGuid().ToString("N"),
                RecipientUserCode = recipient,
                SenderUserCode = operatorCode,
                InstanceId = instance.Id,
                ObjectCode = instance.ObjectCode,
                ObjectKey = instance.ObjectKey,
                Title = notifTitle,
                Content = notifContent,
                Type = "Revocation",
                IsRead = false,
                CreatedAt = now
            }, ct);
        }

        // 6. Outbox 异步解锁 SAP 单据状态
        var snapshot = _db.Snapshots.FirstOrDefault(s => s.InstanceId == instance.Id);
        await AddOutboxAsync(instance, "WorkflowRevoked", "Revoked", snapshot?.DataSha256 ?? string.Empty, ct);

        await _db.SaveChangesAsync(ct);
        return instance;
    }

    private async Task<WorkflowTask> AddApprovalTaskAsync(WorkflowInstance instance, WorkflowGraphNode node, DateTime now, CancellationToken ct)
    {
        if (node.NodeType != NodeType.Approval)
            throw new InvalidOperationException($"节点 {node.NodeKey} 不是人工审批节点");

        var candidates = await _userDirectoryService.ResolveCandidatesAsync(
            node.CandidateType,
            node.CandidateValues,
            instance.SubmitterCode,
            ct);

        if (candidates.Count == 0)
            throw new InvalidOperationException($"审批节点 {node.NodeKey} 无法解析出有效审批人");

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
        {
            task.Candidates.Add(new WorkflowTaskCandidate
            {
                TaskId = task.Id,
                UserCode = userCode,
                UserName = userCode,
                CandidateType = node.CandidateType
            });
        }

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
        try
        {
            var decompressed = Approval.Application.Common.Helpers.SnapshotCompressionHelper.DecompressJson(rawJson);
            using var document = JsonDocument.Parse(decompressed);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return 0m;
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name.Equals("DocTotal", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetDecimal(out var value))
                    return value;
            return 0m;
        }
        catch
        {
            return 0m;
        }
    }
}
