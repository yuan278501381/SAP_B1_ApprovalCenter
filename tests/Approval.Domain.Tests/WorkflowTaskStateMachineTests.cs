using System;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class WorkflowTaskStateMachineTests
{
    [Fact]
    public void Approve_FromPending_ShouldSetStatusAndDecision()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Approve("manager", "OK", DateTime.UtcNow);
        task.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Completed);
        task.Decision.Should().Be(TaskDecision.Approve);
    }

    [Fact]
    public void Approve_FromCompleted_ShouldThrow()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Approve("manager", "OK", DateTime.UtcNow);
        var act = () => task.Approve("other", "again", DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reject_FromPending_ShouldSetStatusAndDecision()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Reject("manager", "No", DateTime.UtcNow);
        task.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Completed);
        task.Decision.Should().Be(TaskDecision.Reject);
    }

    [Fact]
    public void Reject_FromCompleted_ShouldThrow()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Reject("manager", "No", DateTime.UtcNow);
        var act = () => task.Reject("other", "again", DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Return_FromPending_ShouldSetStatusAndDecision()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Return("manager", "Return", DateTime.UtcNow);
        task.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Completed);
        task.Decision.Should().Be(TaskDecision.Return);
    }

    [Fact]
    public void Return_FromCompleted_ShouldThrow()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Return("manager", "Return", DateTime.UtcNow);
        var act = () => task.Return("other", "again", DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_FromPending_ShouldSetStatus()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Cancel("Cancel");
        task.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromCancelled_ShouldThrow()
    {
        var task = WorkflowTask.Create("inst_1", "node_1", TaskType.Approve, DateTime.UtcNow, null);
        task.Cancel("Cancel");
        var act = () => task.Cancel("again");
        act.Should().Throw<InvalidOperationException>();
    }
}
