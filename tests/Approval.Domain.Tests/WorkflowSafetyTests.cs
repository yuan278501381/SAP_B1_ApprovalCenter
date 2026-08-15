using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Approval.Domain.Tests;

public class WorkflowSafetyTests
{
    [Fact]
    public async Task NonCandidate_CannotApproveTask()
    {
        var (db, engine) = await CreateEngineAsync();
        var instance = await engine.StartWorkflowAsync("DB_KCC", "CHOQUT", "SAFE-1", "author", "发起人", Payload("CHOQUT", "SAFE-1", 100));

        var action = () => engine.ProcessDecisionAsync(instance.Tasks.Single().Id, "outsider", "无权限用户", TaskDecision.Approve, null);

        await action.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*不是*候选审批人*");
        (await db.Instances.SingleAsync(i => i.Id == instance.Id)).Status.Should().Be(WorkflowStatus.Running);
    }

    [Fact]
    public async Task Return_TerminatesCurrentInstance_AndAllowsResubmit()
    {
        var (db, engine) = await CreateEngineAsync();
        var payload = Payload("CHOQUT", "SAFE-2", 100);
        var first = await engine.StartWorkflowAsync("DB_KCC", "CHOQUT", "SAFE-2", "author", "发起人", payload);

        await engine.ProcessDecisionAsync(first.Tasks.Single().Id, "sales_mgr", "销售经理", TaskDecision.Return, "请修改价格");
        (await db.Instances.SingleAsync(i => i.Id == first.Id)).Status.Should().Be(WorkflowStatus.Returned);

        var second = await engine.StartWorkflowAsync("DB_KCC", "CHOQUT", "SAFE-2", "author", "发起人", payload);
        second.Id.Should().NotBe(first.Id);
        second.Status.Should().Be(WorkflowStatus.Running);
    }

    private static async Task<(ApprovalDbContext Db, WorkflowEngine Engine)> CreateEngineAsync()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new ApprovalDbContext(options);
        await DbInitializer.SeedAsync(db);
        var userDirectory = new UserDirectoryService(db);
        return (db, new WorkflowEngine(db, new TraceContext { TraceId = Guid.NewGuid().ToString("N") }, userDirectory));
    }

    private static SapObjectPayload Payload(string objectCode, string objectKey, decimal total) => new()
    {
        CompanyId = "DB_KCC",
        ObjectCode = objectCode,
        ObjectKey = objectKey,
        Title = $"{objectCode} #{objectKey}",
        DocTotal = total,
        RawJson = $$"""{"DocTotal":{{total}}}"""
    };
}
