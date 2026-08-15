using System.Text.Json;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Infrastructure.Services;

/// <summary>
/// 数据库初始化与基础流程定义 Seed 服务
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(ApprovalDbContext db)
    {
        if (await db.Definitions.AnyAsync()) return;

        // 1. 型号订单流程定义 (CHORDR_WORKFLOW)
        var chordrDef = new WorkflowDefinition
        {
            Id = "DEF_CHORDR",
            Name = "型号订单标准审批流程",
            Category = "Sales",
            Description = "适用于 SAP B1 型号订单 UDO 的双级/条件审批流程",
            IsActive = true
        };

        var chordrGraph = new WorkflowGraphDefinition
        {
            Nodes = new List<WorkflowGraphNode>
            {
                new() { NodeKey = "start", Name = "开始", NodeType = NodeType.Start },
                new() { NodeKey = "cond_amount", Name = "金额条件判定", NodeType = NodeType.Condition, ConditionExpression = "DocTotal > 50000" },
                new() { NodeKey = "appr_mgr", Name = "部门主管审批", NodeType = NodeType.Approval, TaskType = TaskType.Approve, CandidateValues = new List<string> { "manager" } },
                new() { NodeKey = "appr_dir", Name = "业务总监终审", NodeType = NodeType.Approval, TaskType = TaskType.Approve, CandidateValues = new List<string> { "director", "admin" } },
                new() { NodeKey = "end", Name = "结束放行", NodeType = NodeType.End }
            },
            Edges = new List<WorkflowGraphEdge>
            {
                new() { FromNodeKey = "start", ToNodeKey = "cond_amount", Label = "提交流程" },
                new() { FromNodeKey = "cond_amount", ToNodeKey = "appr_dir", Label = "大额单据 (>5万)", ConditionValue = "True" },
                new() { FromNodeKey = "cond_amount", ToNodeKey = "appr_mgr", Label = "普通单据 (<=5万)", ConditionValue = "False" },
                new() { FromNodeKey = "appr_mgr", ToNodeKey = "end", Label = "同意" },
                new() { FromNodeKey = "appr_dir", ToNodeKey = "end", Label = "同意" }
            }
        };

        var chordrVer = new WorkflowDefinitionVersion
        {
            Id = "VER_CHORDR_V1",
            DefinitionId = chordrDef.Id,
            VersionNum = 1,
            GraphJson = JsonSerializer.Serialize(chordrGraph),
            Status = "Published",
            PublishedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };

        var chordrBinding = new WorkflowBinding
        {
            Id = "BIND_CHORDR",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            VersionId = chordrVer.Id,
            Priority = 10,
            IsActive = true
        };

        // 2. 型号报价单流程定义 (CHOQUT_WORKFLOW - 验证通用性)
        var choqutDef = new WorkflowDefinition
        {
            Id = "DEF_CHOQUT",
            Name = "型号报价单快速审批流程",
            Category = "Sales",
            Description = "适用于型号报价单 UDO 的审批流程",
            IsActive = true
        };

        var choqutGraph = new WorkflowGraphDefinition
        {
            Nodes = new List<WorkflowGraphNode>
            {
                new() { NodeKey = "start", Name = "开始", NodeType = NodeType.Start },
                new() { NodeKey = "appr_sales", Name = "销售主管审批", NodeType = NodeType.Approval, TaskType = TaskType.Approve, CandidateValues = new List<string> { "sales_mgr", "manager" } },
                new() { NodeKey = "end", Name = "结束放行", NodeType = NodeType.End }
            },
            Edges = new List<WorkflowGraphEdge>
            {
                new() { FromNodeKey = "start", ToNodeKey = "appr_sales", Label = "提交流程" },
                new() { FromNodeKey = "appr_sales", ToNodeKey = "end", Label = "同意" }
            }
        };

        var choqutVer = new WorkflowDefinitionVersion
        {
            Id = "VER_CHOQUT_V1",
            DefinitionId = choqutDef.Id,
            VersionNum = 1,
            GraphJson = JsonSerializer.Serialize(choqutGraph),
            Status = "Published",
            PublishedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };

        var choqutBinding = new WorkflowBinding
        {
            Id = "BIND_CHOQUT",
            CompanyId = "DB_KCC",
            ObjectCode = "CHOQUT",
            VersionId = choqutVer.Id,
            Priority = 10,
            IsActive = true
        };

        await db.Definitions.AddRangeAsync(chordrDef, choqutDef);
        await db.DefinitionVersions.AddRangeAsync(chordrVer, choqutVer);
        await db.Bindings.AddRangeAsync(chordrBinding, choqutBinding);

        await db.SaveChangesAsync();
    }
}
