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

public class TestTraceContext : ITraceContext
{
    public string TraceId => "trace_test_suite_001";
    public string? ClientIp => "127.0.0.1";
    public string? CurrentUserCode => "TEST_USER";
}

public class WorkflowEngineAdvancedTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly UserDirectoryService _userDirectory;
    private readonly WorkflowRuleMatcher _ruleMatcher;
    private readonly WorkflowEngine _engine;
    private readonly ITraceContext _traceContext = new TestTraceContext();

    public WorkflowEngineAdvancedTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_EngineTest_{Guid.NewGuid():N}")
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
    public async Task StartWorkflow_AndApprove_ShouldCompleteWorkflow_AndEmitOutbox()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {
                    "nodeKey": "node_mgr", 
                    "nodeType": "Approval", 
                    "name": "部门主管审批", 
                    "candidateType": "Direct",
                    "candidateValues": ["MGR01"]
                },
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [
                {"fromNodeKey": "start", "toNodeKey": "node_mgr"},
                {"fromNodeKey": "node_mgr", "toNodeKey": "end"}
            ]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-CHORDR", Name = "型号订单标准审批" };
        var ver = new WorkflowDefinitionVersion
        {
            Id = "VER-CHORDR-01",
            DefinitionId = "DEF-CHORDR",
            VersionNum = 1,
            Status = "Published",
            GraphJson = graphJson
        };
        var binding = new WorkflowBinding
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            VersionId = "VER-CHORDR-01",
            Priority = 10,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1001",
            DocTotal = 85000.0m,
            Title = "中山市互森服饰有限公司",
            CreatorUserCode = "SALES_01",
            RawJson = """{"DocTotal": 85000.0, "CardName": "中山市互森服饰有限公司"}"""
        };

        // 1. 发起流程
        var instance = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1001", "SALES_01", "销售员张三", payload);

        instance.Should().NotBeNull();
        instance.Status.Should().Be(WorkflowStatus.Running);

        var tasks = await _db.Tasks.Where(t => t.InstanceId == instance.Id).ToListAsync();
        tasks.Should().HaveCount(1);
        tasks[0].Status.Should().Be(TaskStatus.Pending);

        // 2. 审批通过
        var completedTask = await _engine.ProcessDecisionAsync(tasks[0].Id, "MGR01", "主管", TaskDecision.Approve, "同意核准通过");
        completedTask.Status.Should().Be(TaskStatus.Completed);
        completedTask.Decision.Should().Be(TaskDecision.Approve);

        var finalInst = await _db.Instances.FirstAsync(i => i.Id == instance.Id);
        finalInst.Status.Should().Be(WorkflowStatus.Approved);
        finalInst.FinishedAt.Should().NotBeNull();

        // 验证 Outbox 消息产生
        var outboxList = await _db.Outboxes.Where(o => o.AggregateId == instance.Id).ToListAsync();
        outboxList.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ProcessDecision_Reject_ShouldSetWorkflowStatusToRejected()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {"nodeKey": "node_audit", "nodeType": "Approval", "name": "风控审批", "candidateType": "Direct", "candidateValues": ["RISK_01"]},
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [
                {"fromNodeKey": "start", "toNodeKey": "node_audit"},
                {"fromNodeKey": "node_audit", "toNodeKey": "end"}
            ]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-RISK", Name = "风控流程" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-RISK", DefinitionId = "DEF-RISK", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-RISK", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1002", DocTotal = 99999.0m, Title = "风险单据", CreatorUserCode = "SALES_02", RawJson = "{}" };
        var instance = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1002", "SALES_02", null, payload);

        var task = await _db.Tasks.FirstAsync(t => t.InstanceId == instance.Id);

        // 驳回
        await _engine.ProcessDecisionAsync(task.Id, "RISK_01", "风控员", TaskDecision.Reject, "单价低于成本底线，予以驳回");

        var finishedInst = await _db.Instances.FirstAsync(i => i.Id == instance.Id);
        finishedInst.Status.Should().Be(WorkflowStatus.Rejected);
    }

    [Fact]
    public async Task RevokeWorkflow_ShouldCancelRunningTasks_AndMarkInstanceCancelled()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {"nodeKey": "n1", "nodeType": "Approval", "name": "审批", "candidateType": "Direct", "candidateValues": ["M1"]},
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [{"fromNodeKey": "start", "toNodeKey": "n1"}, {"fromNodeKey": "n1", "toNodeKey": "end"}]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-REV", Name = "撤回测试流程", AllowSubmitterRevoke = true };
        var ver = new WorkflowDefinitionVersion { Id = "VER-REV", DefinitionId = "DEF-REV", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-REV", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1003", DocTotal = 1000m, Title = "撤回单据", CreatorUserCode = "SUBMITTER_A", RawJson = "{}" };
        var instance = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1003", "SUBMITTER_A", "发起人A", payload);

        // 发起人主动撤回
        var revokedInst = await _engine.RevokeWorkflowAsync(instance.Id, "SUBMITTER_A", "发起人A", "录错客户名称，主动撤回");

        revokedInst.Status.Should().Be(WorkflowStatus.Cancelled);

        var pendingTasks = await _db.Tasks.Where(t => t.InstanceId == instance.Id && t.Status == TaskStatus.Pending).ToListAsync();
        pendingTasks.Should().BeEmpty();
    }
}
