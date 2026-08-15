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

public class WorkflowEngineSupercededAndReturnTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly UserDirectoryService _userDirectory;
    private readonly WorkflowRuleMatcher _ruleMatcher;
    private readonly WorkflowEngine _engine;
    private readonly ITraceContext _traceContext = new TestTraceContext();

    public WorkflowEngineSupercededAndReturnTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_EngineSuperceded_{Guid.NewGuid():N}")
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
    public async Task StartWorkflow_WhenDocumentModifiedDuringApproval_ShouldTriggerSupercededReRouting()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {"nodeKey": "n1", "nodeType": "Approval", "name": "主管审批", "candidateType": "Direct", "candidateValues": ["MGR01"]},
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [{"fromNodeKey": "start", "toNodeKey": "n1"}, {"fromNodeKey": "n1", "toNodeKey": "end"}]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-SUP", Name = "重路由测试" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-SUP", DefinitionId = "DEF-SUP", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-SUP", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        // 1. 第一次提交 (金额 50000)
        var payloadV1 = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1001", DocTotal = 50000m, Title = "订单", CreatorUserCode = "U1", RawJson = """{"DocTotal": 50000}""" };
        var inst1 = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1001", "U1", "用户", payloadV1);
        inst1.Status.Should().Be(WorkflowStatus.Running);

        // 2. 第二次提交内容未变 -> 应当阻止重复发起
        var actDuplicate = () => _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1001", "U1", "用户", payloadV1);
        await actDuplicate.Should().ThrowAsync<InvalidOperationException>();

        // 3. 第三次提交金额变更为 60000 -> 触发重路由: 原实例置为 Superceded，新实例置为 Running
        var payloadV2 = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1001", DocTotal = 60000m, Title = "订单改单", CreatorUserCode = "U1", RawJson = """{"DocTotal": 60000}""" };
        var inst2 = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1001", "U1", "用户", payloadV2);

        inst2.Id.Should().NotBe(inst1.Id);
        inst2.Status.Should().Be(WorkflowStatus.Running);

        var oldInst = await _db.Instances.FirstAsync(i => i.Id == inst1.Id);
        oldInst.Status.Should().Be(WorkflowStatus.Superceded);
    }

    [Fact]
    public async Task ProcessDecision_Return_ShouldSetInstanceStatusToReturned()
    {
        var graphJson = """
        {
            "allowSubmitterRevoke": true,
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start", "name": "开始"},
                {"nodeKey": "n1", "nodeType": "Approval", "name": "主管审批", "candidateType": "Direct", "candidateValues": ["MGR01"]},
                {"nodeKey": "end", "nodeType": "End", "name": "结束"}
            ],
            "edges": [{"fromNodeKey": "start", "toNodeKey": "n1"}, {"fromNodeKey": "n1", "toNodeKey": "end"}]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-RET", Name = "退回测试" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-RET", DefinitionId = "DEF-RET", GraphJson = graphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-RET", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "1002", DocTotal = 1000m, Title = "退回单", CreatorUserCode = "U1", RawJson = "{}" };
        var inst = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1002", "U1", "用户", payload);

        var task = await _db.Tasks.FirstAsync(t => t.InstanceId == inst.Id);

        // 执行退回
        await _engine.ProcessDecisionAsync(task.Id, "MGR01", "主管", TaskDecision.Return, "附件缺失，退回补充");

        var updatedInst = await _db.Instances.FirstAsync(i => i.Id == inst.Id);
        updatedInst.Status.Should().Be(WorkflowStatus.Returned);
    }
}
