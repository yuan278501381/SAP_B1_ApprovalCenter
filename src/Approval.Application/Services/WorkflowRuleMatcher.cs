using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;

namespace Approval.Application.Services;

/// <summary>
/// 多维审批规则矩阵匹配引擎 (Rule Matching Engine)
/// </summary>
public sealed class WorkflowRuleMatcher : IWorkflowRuleMatcher
{
    private readonly IApprovalDbContext _db;

    public WorkflowRuleMatcher(IApprovalDbContext db)
    {
        _db = db;
    }

    public async Task<RuleMatchResult> MatchRuleAsync(
        string companyId,
        string objectCode,
        SapObjectPayload payload,
        CancellationToken ct = default)
    {
        var rules = _db.Rules
            .Where(r => r.CompanyId == companyId && r.ObjectCode == objectCode && r.IsActive)
            .OrderBy(r => r.Priority)
            .ToList();

        var bindings = _db.Bindings
            .Where(b => b.CompanyId == companyId && b.ObjectCode == objectCode && b.IsActive)
            .OrderBy(b => b.Priority)
            .ToList();

        // 1. 若配置了高优先级的绑定 (例如特定测试或动态绑定 Priority >= 100)，优先命中绑定
        var highPriorityBinding = bindings.FirstOrDefault(b => b.Priority >= 100);
        if (highPriorityBinding != null)
        {
            var version = _db.DefinitionVersions.FirstOrDefault(v => v.Id == highPriorityBinding.VersionId);
            return new RuleMatchResult(
                ShouldTrigger: true,
                TriggerReason: $"优先命中对象绑定 [{objectCode}] (Priority: {highPriorityBinding.Priority})",
                MatchedRule: null,
                TargetVersionId: highPriorityBinding.VersionId,
                TargetDefinitionId: version?.DefinitionId);
        }

        // 2. 按规则矩阵逐条评估
        if (rules.Count > 0)
        {
            foreach (var rule in rules)
            {
                if (EvaluateRule(rule, payload, out var reason))
                {
                    var versionId = rule.TargetVersionId;
                    if (string.IsNullOrWhiteSpace(versionId))
                    {
                        var latestPublished = _db.DefinitionVersions
                            .Where(v => v.DefinitionId == rule.TargetDefinitionId && v.Status == "Published")
                            .OrderByDescending(v => v.VersionNum)
                            .FirstOrDefault();
                        versionId = latestPublished?.Id;
                    }

                    if (!string.IsNullOrWhiteSpace(versionId))
                    {
                        return new RuleMatchResult(
                            ShouldTrigger: true,
                            TriggerReason: $"命中规则 [{rule.RuleName}]: {reason}",
                            MatchedRule: rule,
                            TargetVersionId: versionId,
                            TargetDefinitionId: rule.TargetDefinitionId);
                    }
                }
            }

            // 规则均未命中或命中了黑名单免审
            return new RuleMatchResult(
                ShouldTrigger: false,
                TriggerReason: "未命中任何审批触发规则或处于免审黑名单，无需发起审批",
                MatchedRule: null,
                TargetVersionId: null,
                TargetDefinitionId: null);
        }

        // 3. 兜底查询常规绑定
        var defaultBinding = bindings.OrderByDescending(b => b.Priority).FirstOrDefault();
        if (defaultBinding != null)
        {
            var version = _db.DefinitionVersions.FirstOrDefault(v => v.Id == defaultBinding.VersionId);
            return new RuleMatchResult(
                ShouldTrigger: true,
                TriggerReason: $"默认对象绑定 [{objectCode}] -> 流程版本 [{defaultBinding.VersionId}]",
                MatchedRule: null,
                TargetVersionId: defaultBinding.VersionId,
                TargetDefinitionId: version?.DefinitionId);
        }

        return new RuleMatchResult(
            ShouldTrigger: false,
            TriggerReason: $"未找到对象 [{objectCode}] 的审批规则或绑定",
            MatchedRule: null,
            TargetVersionId: null,
            TargetDefinitionId: null);
    }

    private bool EvaluateRule(WorkflowRule rule, SapObjectPayload payload, out string reason)
    {
        reason = string.Empty;

        // A. 触发方式检查 (TriggerMode)
        if (rule.TriggerMode.Equals("ExplicitCheckbox", StringComparison.OrdinalIgnoreCase))
        {
            var fieldName = string.IsNullOrWhiteSpace(rule.TriggerFieldName) ? "U_APSubmit" : rule.TriggerFieldName;
            if (TryGetFieldValue(payload, fieldName, out var checkVal) && checkVal != null)
            {
                var strVal = checkVal.ToString()?.Trim();
                var isChecked = "Y".Equals(strVal, StringComparison.OrdinalIgnoreCase) ||
                                "true".Equals(strVal, StringComparison.OrdinalIgnoreCase) ||
                                "1".Equals(strVal);
                if (!isChecked)
                {
                    return false; // 勾选字段存在且未勾选
                }
            }
            // 若字段不存在于单据中，系统智能自适应：默认视为触发
        }

        // B. 人员范围模式检查 (UserScopeMode: All, Whitelist, Blacklist)
        var submitter = payload.CreatorUserCode?.Trim() ?? string.Empty;
        var users = ParseStringList(rule.UserScopeListJson);

        if (rule.UserScopeMode == UserScopeMode.Whitelist)
        {
            if (!users.Contains(submitter, StringComparer.OrdinalIgnoreCase))
                return false; // 不在白名单中
        }
        else if (rule.UserScopeMode == UserScopeMode.Blacklist)
        {
            if (users.Contains(submitter, StringComparer.OrdinalIgnoreCase))
                return false; // 处于免审黑名单中
        }

        // C. 部门范围检查 (DeptScopeListJson)
        var depts = ParseStringList(rule.DeptScopeListJson);
        if (depts.Count > 0)
        {
            var submitterDept = ResolveSubmitterDepartment(submitter, payload);
            if (string.IsNullOrWhiteSpace(submitterDept) || !depts.Contains(submitterDept, StringComparer.OrdinalIgnoreCase))
                return false; // 部门不匹配
        }

        // D. 业务条件表达式检查 (支持传统单表达式与高级复合结构化 JSON: 表头组合 + 行表明细扫描)
        if (!string.IsNullOrWhiteSpace(rule.ConditionExpr))
        {
            if (!EvaluateAdvancedCondition(rule.ConditionExpr, payload, out var condReason))
                return false;

            if (!string.IsNullOrEmpty(condReason))
                reason = condReason;
        }

        if (string.IsNullOrEmpty(reason))
            reason = $"制单人: {submitter}, 金额: {payload.DocTotal:N2}";
        return true;
    }

    private string? ResolveSubmitterDepartment(string userCode, SapObjectPayload payload)
    {
        // 优先从单据表头查找常见部门字段
        string[] deptFieldNames = ["Department", "U_Department", "U_Dept", "Dept", "U_DEPT_CODE"];
        foreach (var name in deptFieldNames)
        {
            if (TryGetFieldValue(payload, name, out var val) && val != null)
                return val.ToString()?.Trim();
        }

        // 其次从用户映射表查询
        var mapping = _db.UserMappings.FirstOrDefault(m => m.SapUserCode == userCode || m.AdUserCode == userCode);
        return mapping?.Department?.Trim();
    }

    private static bool TryGetFieldValue(SapObjectPayload payload, string fieldName, out object? val)
    {
        val = null;
        if (string.Equals(fieldName, "DocTotal", StringComparison.OrdinalIgnoreCase))
        {
            val = payload.DocTotal;
            return true;
        }
        if (string.Equals(fieldName, "Creator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fieldName, "UserCode", StringComparison.OrdinalIgnoreCase))
        {
            val = payload.CreatorUserCode;
            return true;
        }

        if (payload.HeaderFields != null)
        {
            foreach (var kv in payload.HeaderFields)
            {
                if (kv.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    val = kv.Value;
                    return true;
                }
            }
        }
        return false;
    }

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool EvaluateAdvancedCondition(string expr, SapObjectPayload payload, out string reason)
    {
        reason = string.Empty;
        var trimmed = expr.Trim();

        // 1. 结构化 JSON 复合条件 (表头多字段组合 + 子表明细行扫描)
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var combine = root.TryGetProperty("combine", out var cProp) ? cProp.GetString()?.ToUpperInvariant() : "AND";
                var isOr = combine == "OR";

                var matchedReasons = new List<string>();
                var allPass = !isOr; // AND 默认为 true，遇到任一失败则 false；OR 默认为 false，遇到任一成功则 true

                // A. 评估表头多字段条件
                if (root.TryGetProperty("headerConditions", out var headers) && headers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var h in headers.EnumerateArray())
                    {
                        var field = h.TryGetProperty("field", out var f) ? f.GetString() : "";
                        var op = h.TryGetProperty("op", out var o) ? o.GetString() : "==";
                        var targetVal = h.TryGetProperty("value", out var v) ? v.ToString() : "";

                        if (string.IsNullOrWhiteSpace(field)) continue;

                        TryGetFieldValue(payload, field, out var actualVal);
                        var pass = EvaluateComparison(actualVal, op, targetVal);

                        if (pass)
                            matchedReasons.Add($"表头[{field}]满足{op}{targetVal}");

                        if (isOr && pass) { allPass = true; break; }
                        if (!isOr && !pass) { allPass = false; break; }
                    }
                }

                if (isOr && allPass)
                {
                    reason = string.Join(", ", matchedReasons);
                    return true;
                }
                if (!isOr && !allPass)
                {
                    return false;
                }

                // B. 评估子表明细行条件 (Line Item Collections)
                if (root.TryGetProperty("lineConditions", out var lines) && lines.ValueKind == JsonValueKind.Array)
                {
                    using var rawDoc = !string.IsNullOrWhiteSpace(payload.RawJson) ? JsonDocument.Parse(payload.RawJson) : null;
                    var rawRoot = rawDoc?.RootElement;

                    foreach (var l in lines.EnumerateArray())
                    {
                        var coll = l.TryGetProperty("collection", out var colProp) ? colProp.GetString() : "";
                        var mode = l.TryGetProperty("mode", out var mProp) ? mProp.GetString()?.ToUpperInvariant() : "ANY";
                        var field = l.TryGetProperty("field", out var lf) ? lf.GetString() : "";
                        var op = l.TryGetProperty("op", out var lo) ? lo.GetString() : "==";
                        var targetVal = l.TryGetProperty("value", out var lv) ? lv.ToString() : "";

                        if (string.IsNullOrWhiteSpace(field) || rawRoot == null) continue;

                        var pass = EvaluateLineCondition(rawRoot.Value, coll, mode, field, op, targetVal, out var lineHitMsg);
                        if (pass)
                            matchedReasons.Add(lineHitMsg);

                        if (isOr && pass) { allPass = true; break; }
                        if (!isOr && !pass) { allPass = false; break; }
                    }
                }

                if (allPass)
                {
                    reason = matchedReasons.Count > 0 ? string.Join("; ", matchedReasons) : $"符合复合规则";
                    return true;
                }
                return false;
            }
            catch
            {
                // 解析失败降级为简单金额判断
            }
        }

        // 2. 传统简单表达式兼容 (如 DocTotal > 50000)
        return EvaluateSimpleCondition(trimmed, payload.DocTotal);
    }

    private static bool EvaluateLineCondition(JsonElement rawRoot, string? collectionKey, string mode, string field, string op, string targetVal, out string hitMsg)
    {
        hitMsg = string.Empty;
        var collectionsToScan = new List<JsonElement>();

        if (!string.IsNullOrWhiteSpace(collectionKey) && rawRoot.TryGetProperty(collectionKey, out var specificColl) && specificColl.ValueKind == JsonValueKind.Array)
        {
            collectionsToScan.Add(specificColl);
        }
        else
        {
            // 扫描所有以 Collection 结尾或名为 DocumentLines 的数组
            foreach (var prop in rawRoot.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array && (prop.Name.EndsWith("Collection", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("DocumentLines", StringComparison.OrdinalIgnoreCase)))
                {
                    collectionsToScan.Add(prop.Value);
                }
            }
        }

        if (collectionsToScan.Count == 0) return false;

        var isAny = mode != "ALL";
        var totalRows = 0;
        var matchedRows = 0;

        foreach (var coll in collectionsToScan)
        {
            foreach (var row in coll.EnumerateArray())
            {
                totalRows++;
                object? rowVal = null;
                if (row.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in row.EnumerateObject())
                    {
                        if (p.Name.Equals(field, StringComparison.OrdinalIgnoreCase))
                        {
                            rowVal = p.Value.ToString();
                            break;
                        }
                    }
                }

                if (EvaluateComparison(rowVal, op, targetVal))
                {
                    matchedRows++;
                    if (isAny)
                    {
                        hitMsg = $"行表命中[{field} {op} {targetVal}] (第{totalRows}行)";
                        return true;
                    }
                }
                else
                {
                    if (!isAny) return false; // ALL 模式下只要有一行不匹配即失败
                }
            }
        }

        if (!isAny && totalRows > 0 && matchedRows == totalRows)
        {
            hitMsg = $"行表全量满足[{field} {op} {targetVal}] (共{totalRows}行)";
            return true;
        }

        return false;
    }

    private static bool EvaluateComparison(object? actualVal, string op, string targetVal)
    {
        if (actualVal == null) return false;
        var actualStr = actualVal.ToString()?.Trim() ?? string.Empty;
        var targetStr = targetVal.Trim();

        // 数值比较
        if (decimal.TryParse(actualStr, out var actualNum) && decimal.TryParse(targetStr, out var targetNum))
        {
            return op switch
            {
                ">" => actualNum > targetNum,
                ">=" => actualNum >= targetNum,
                "<" => actualNum < targetNum,
                "<=" => actualNum <= targetNum,
                "==" or "=" => actualNum == targetNum,
                "!=" or "<>" => actualNum != targetNum,
                _ => false
            };
        }

        // 列表包含比较 (IN / NOT_IN)
        if (op.Equals("IN", StringComparison.OrdinalIgnoreCase) || op.Equals("NOT_IN", StringComparison.OrdinalIgnoreCase))
        {
            var list = targetStr.Split([',', ';', '|', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var inList = list.Any(item => string.Equals(item, actualStr, StringComparison.OrdinalIgnoreCase));
            return op.Equals("IN", StringComparison.OrdinalIgnoreCase) ? inList : !inList;
        }

        // 字符串包含比较 (CONTAINS)
        if (op.Equals("CONTAINS", StringComparison.OrdinalIgnoreCase))
        {
            return actualStr.Contains(targetStr, StringComparison.OrdinalIgnoreCase);
        }

        // 字符串全等比较
        return op switch
        {
            "==" or "=" => string.Equals(actualStr, targetStr, StringComparison.OrdinalIgnoreCase),
            "!=" or "<>" => !string.Equals(actualStr, targetStr, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool EvaluateSimpleCondition(string expr, decimal docTotal)
    {
        var trimmed = expr.Trim();
        if (trimmed.Contains('>'))
        {
            var parts = trimmed.Split('>');
            if (parts.Length == 2 && decimal.TryParse(parts[1].Trim().Trim('='), out var threshold))
                return parts[1].Contains('=') ? docTotal >= threshold : docTotal > threshold;
        }
        if (trimmed.Contains('<'))
        {
            var parts = trimmed.Split('<');
            if (parts.Length == 2 && decimal.TryParse(parts[1].Trim().Trim('='), out var threshold))
                return parts[1].Contains('=') ? docTotal <= threshold : docTotal < threshold;
        }
        return true;
    }
}
