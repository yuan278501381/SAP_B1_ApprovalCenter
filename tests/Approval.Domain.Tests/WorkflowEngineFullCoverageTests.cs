using System.Globalization;
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

namespace Approval.Domain.Tests;

public class WorkflowEngineFullCoverageTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly UserDirectoryService _userDirectory;
    private readonly WorkflowRuleMatcher _ruleMatcher;
    private readonly WorkflowEngine _engine;
    private readonly ITraceContext _traceContext = new TestTraceContext();

    public WorkflowEngineFullCoverageTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_EngineFull_{Guid.NewGuid():N}")
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

    [Theory]
    [InlineData("""{"nodes": [], "edges": []}""", "流程图没有节点")]
    [InlineData("""{"nodes": [{"nodeKey": "n1", "nodeType": "Approval"}], "edges": []}""", "流程必须且只能有一个 Start 节点")]
    [InlineData("""{"nodes": [{"nodeKey": "s1", "nodeType": "Start"}, {"nodeKey": "s2", "nodeType": "Start"}], "edges": []}""", "流程必须且只能有一个 Start 节点")]
    [InlineData("""{"nodes": [{"nodeKey": "k1", "nodeType": "Start"}, {"nodeKey": "k1", "nodeType": "Approval"}], "edges": []}""", "流程节点标识为空或重复")]
    [InlineData("""{"nodes": [{"nodeKey": "s", "nodeType": "Start"}, {"nodeKey": "a", "nodeType": "CC"}], "edges": []}""", "尚未实现")]
    [InlineData("""{"nodes": [{"nodeKey": "s", "nodeType": "Start"}, {"nodeKey": "e", "nodeType": "End"}], "edges": [{"fromNodeKey": "s", "toNodeKey": "unknown"}]}""", "流程连线引用了不存在的节点")]
    public async Task StartWorkflow_InvalidGraphStructure_ShouldThrowDescriptiveExceptions(string invalidGraphJson, string expectedMessagePart)
    {
        var def = new WorkflowDefinition { Id = "DEF-ERR", Name = "错误图结构" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-ERR", DefinitionId = "DEF-ERR", GraphJson = invalidGraphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-ERR", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "9001", DocTotal = 100m, CreatorUserCode = "U1", RawJson = "{}" };

        var act = () => _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "9001", "U1", "用户", payload);
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.WithMessage($"*{expectedMessagePart}*");
    }

    [Fact]
    public async Task StartWorkflow_CyclicConditionGraph_ShouldDetectLoopAndHalt()
    {
        // 构造条件网关死循环：Start -> C1 -> C2 -> C1
        var loopGraphJson = """
        {
            "nodes": [
                {"nodeKey": "start", "nodeType": "Start"},
                {"nodeKey": "c1", "nodeType": "Condition", "conditionExpression": "DocTotal > 100"},
                {"nodeKey": "c2", "nodeType": "Condition", "conditionExpression": "DocTotal > 100"},
                {"nodeKey": "end", "nodeType": "End"}
            ],
            "edges": [
                {"fromNodeKey": "start", "toNodeKey": "c1"},
                {"fromNodeKey": "c1", "toNodeKey": "c2", "conditionValue": "True"},
                {"fromNodeKey": "c2", "toNodeKey": "c1", "conditionValue": "True"}
            ]
        }
        """;

        var def = new WorkflowDefinition { Id = "DEF-LOOP", Name = "环路测试" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-LOOP", DefinitionId = "DEF-LOOP", GraphJson = loopGraphJson, Status = "Published" };
        var binding = new WorkflowBinding { CompanyId = "DB_KCC", ObjectCode = "CHORDR", VersionId = "VER-LOOP", Priority = 10, IsActive = true };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Bindings.Add(binding);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "9002", DocTotal = 500m, CreatorUserCode = "U1", RawJson = "{}" };

        var act = () => _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "9002", "U1", "用户", payload);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*存在循环*");
    }

    [Fact]
    public async Task ProcessDecision_TaskNotFound_OrUnauthorized_ShouldThrow()
    {
        // 1. 无身份
        var actNoAuth = () => _engine.ProcessDecisionAsync("t1", "", "无名", TaskDecision.Approve, "通过");
        await actNoAuth.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. 未找到任务
        var actNotFound = () => _engine.ProcessDecisionAsync("t_not_found", "U1", "有名", TaskDecision.Approve, "通过");
        await actNotFound.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ForwardTask_ValidationExceptions_ShouldThrow()
    {
        // 1. 无身份
        var actNoAuth = () => _engine.ForwardTaskAsync("t1", "", "无名", "U2", "目标", "转交");
        await actNoAuth.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. 目标人为空
        var actNoTarget = () => _engine.ForwardTaskAsync("t1", "U1", "名", "", "目标", "转交");
        await actNoTarget.Should().ThrowAsync<ArgumentException>();

        // 3. 任务未找到
        var actNotFound = () => _engine.ForwardTaskAsync("t_missing", "U1", "名", "U2", "目标", "转交");
        await actNotFound.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RevokeWorkflow_InstanceNotFound_OrInvalidStatus_ShouldThrow()
    {
        // 1. 未找到
        var actNotFound = () => _engine.RevokeWorkflowAsync("inst_missing", "U1", "名", "原因");
        await actNotFound.Should().ThrowAsync<KeyNotFoundException>();

        // 2. 非 Running 状态
        var inst = new WorkflowInstance { Status = WorkflowStatus.Approved, SubmitterCode = "U1" };
        _db.Instances.Add(inst);
        await _db.SaveChangesAsync();

        var actInvalidStatus = () => _engine.RevokeWorkflowAsync(inst.Id, "U1", "名", "原因");
        await actInvalidStatus.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*仅允许撤回流转中*");
    }
}
