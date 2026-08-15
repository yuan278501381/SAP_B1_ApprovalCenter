using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskStatus = Approval.Domain.Enums.TaskStatus;

namespace Approval.Domain.Tests;

public class WorkflowEngineTests
{
    private readonly ApprovalDbContext _db;
    private readonly WorkflowEngine _engine;
    private readonly TraceContext _traceContext;

    public WorkflowEngineTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApprovalDbContext(options);
        DbInitializer.SeedAsync(_db).GetAwaiter().GetResult();
        _traceContext = new TraceContext { TraceId = "trace_test_001" };
        var userDirectory = new UserDirectoryService(_db);
        var ruleMatcher = new WorkflowRuleMatcher(_db);
        _engine = new WorkflowEngine(_db, _traceContext, userDirectory, ruleMatcher);
    }

    [Fact]
    public async Task StartWorkflow_And_Approve_ShouldCompleteEndToEnd()
    {
        // 1. 提交审批
        var payload = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1001",
            Title = "型号订单 #1001",
            DocTotal = 85600m,
            RawJson = """{"DocEntry": 1001, "DocTotal": 85600.0, "CardCode": "C20000"}"""
        };

        var instance = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1001", "manager", "张经理", payload);

        instance.Should().NotBeNull();
        instance.Status.Should().Be(WorkflowStatus.Running);
        instance.Snapshot.Should().NotBeNull();
        instance.Snapshot!.DataSha256.Should().NotBeNullOrEmpty();
        instance.Tasks.Should().HaveCount(1);

        var firstTask = instance.Tasks.First();
        firstTask.Status.Should().Be(TaskStatus.Pending);

        // 2. 审批同意
        var decidedTask = await _engine.ProcessDecisionAsync(firstTask.Id, "director", "李总监", TaskDecision.Approve, "同意放行采购");

        decidedTask.Status.Should().Be(TaskStatus.Completed);
        decidedTask.Decision.Should().Be(TaskDecision.Approve);

        var updatedInstance = await _db.Instances.FirstAsync(i => i.Id == instance.Id);
        updatedInstance.Status.Should().Be(WorkflowStatus.Approved);
        updatedInstance.FinishedAt.Should().NotBeNull();

        // 3. 校验 Outbox 事件
        var outboxEvents = await _db.Outboxes.ToListAsync();
        outboxEvents.Should().Contain(o => o.EventType == "InstanceApproved");
    }

    [Fact]
    public async Task ProcessDecision_DuplicateDecision_ShouldThrowException()
    {
        var payload = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1002",
            RawJson = """{"DocEntry": 1002, "DocTotal": 12000.0}"""
        };

        var instance = await _engine.StartWorkflowAsync("DB_KCC", "CHORDR", "1002", "manager", "张经理", payload);
        var task = instance.Tasks.First();

        // 首次审批
        await _engine.ProcessDecisionAsync(task.Id, "manager", "审批人", TaskDecision.Approve, "首次同意");

        // 重复点击审批 (应抛出已处理异常)
        var act = async () => await _engine.ProcessDecisionAsync(task.Id, "manager", "审批人", TaskDecision.Approve, "重复点击");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*无法重复审批*");
    }
}
