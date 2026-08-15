using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskStatus = Approval.Domain.Enums.TaskStatus;

namespace Approval.Domain.Tests;

public class WorkflowEngineGatewayAdvancedTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly UserDirectoryService _userDirectory;
    private readonly WorkflowRuleMatcher _ruleMatcher;
    private readonly WorkflowEngine _engine;
    private readonly ITraceContext _traceContext = new TestTraceContext();

    public WorkflowEngineGatewayAdvancedTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_GatewayTest_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _userDirectory = new UserDirectoryService(_db);
        _ruleMatcher = new WorkflowRuleMatcher(_db);
        _engine = new WorkflowEngine(_db, _traceContext, _userDirectory, _ruleMatcher);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task StartWorkflow_WithConditionGateway_ShouldRouteToCorrectBranch()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {
                    "nodeKey": "gate_amount", 
                    "nodeType": "Condition", 
                    "name": "金额是否大于5万", 
                    "conditionExpression": "DocTotal > 50000"
                },
                {
                    "nodeKey": "node_high", 
                    "nodeType": "Approval", 
                    "name": "总监审批", 
                    "candidateType": "Direct",
                    "candidateValues": ["DIRECTOR_01"]
                },
                {
                    "nodeKey": "node_low", 
                    "nodeType": "Approval", 
                    "name": "经理审批", 
                    "candidateType": "Direct",
                    "candidateValues": ["MANAGER_01"]
                },
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [
                {"fromNodeKey": "start", "toNodeKey": "gate_amount"},
                {"fromNodeKey": "gate_amount", "toNodeKey": "node_high", "conditionValue": "True"},
                {"fromNodeKey": "gate_amount", "toNodeKey": "node_low", "conditionValue": "False"},
                {"fromNodeKey": "node_high", "toNodeKey": "end"},
                {"fromNodeKey": "node_low", "toNodeKey": "end"}
            ]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-GATEWAY", Name = "条件网关分支流程" };
        var ver = new WorkflowDefinitionVersion
        {
            Id = "VER-GATEWAY-01",
            DefinitionId = "DEF-GATEWAY",
            VersionNum = 1,
            Status = "Published",
            GraphJson = graphJson
        };
        var binding = new WorkflowBinding
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            VersionId = "VER-GATEWAY-01",
            Priority = 10,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        // 1. 金额 80,000 > 50,000 -> 应当自动路由至 node_high (DIRECTOR_01)
        var payloadHigh = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1001",
            DocTotal = 80000.0m,
            Title = "大单",
            CreatorUserCode = "USER_01",
            RawJson = """{"DocTotal": 80000.0}"""
        };

        var instanceHigh = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1001", "USER_01", "员工", payloadHigh);
        var taskHigh = await _db.Tasks.Include(t => t.Candidates).FirstAsync(t => t.InstanceId == instanceHigh.Id);
        taskHigh.Candidates.First().UserCode.Should().Be("DIRECTOR_01");

        // 2. 金额 20,000 <= 50,000 -> 应当自动路由至 node_low (MANAGER_01)
        var payloadLow = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1002",
            DocTotal = 20000.0m,
            Title = "小单",
            CreatorUserCode = "USER_01",
            RawJson = """{"DocTotal": 20000.0}"""
        };

        var instanceLow = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1002", "USER_01", "员工", payloadLow);
        var taskLow = await _db.Tasks.Include(t => t.Candidates).FirstAsync(t => t.InstanceId == instanceLow.Id);
        taskLow.Candidates.First().UserCode.Should().Be("MANAGER_01");
    }

    [Fact]
    public async Task ForwardTask_ShouldReassignCandidate_AndRecordActionLog()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {"nodeKey": "n1", "nodeType": "Approval", "name": "初审", "candidateType": "Direct", "candidateValues": ["USER_OLD"]},
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [{"fromNodeKey": "start", "toNodeKey": "n1"}, {"fromNodeKey": "n1", "toNodeKey": "end"}]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-FWD", Name = "转办流程" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-FWD", DefinitionId = "DEF-FWD", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-FWD", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1003", DocTotal = 1000m, Title = "测试", CreatorUserCode = "U1", RawJson = "{}" };
        var inst = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1003", "U1", "用户", payload);

        var task = await _db.Tasks.FirstAsync(t => t.InstanceId == inst.Id);

        // 执行转办给 USER_NEW
        await _engine.ForwardTaskAsync(task.Id, "USER_OLD", "旧经办人", "USER_NEW", "新经办人", "转交专业工程师核查");

        var updatedTask = await _db.Tasks.Include(t => t.Candidates).FirstAsync(t => t.Id == task.Id);
        updatedTask.Candidates.Should().ContainSingle(c => c.UserCode == "USER_NEW");

        var logs = await _db.ActionLogs.Where(l => l.InstanceId == inst.Id).ToListAsync();
        logs.Should().Contain(l => l.Action == "Forward" || l.Action == "Delegate");
    }
}
