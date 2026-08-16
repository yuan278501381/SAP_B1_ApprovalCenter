using System;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class WorkflowInstanceStateMachineTests
{
    [Fact]
    public void Create_ShouldInitializeWithRunningStatus()
    {
        var createdAt = DateTime.UtcNow;
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", createdAt);
        
        instance.Status.Should().Be(WorkflowStatus.Running);
        instance.CompanyId.Should().Be("DB_KCC");
        instance.ObjectCode.Should().Be("CHORDR");
    }

    [Fact]
    public void MarkApproved_FromRunning_ShouldSucceed()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkApproved(DateTime.UtcNow);
        instance.Status.Should().Be(WorkflowStatus.Approved);
        instance.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkApproved_FromApproved_ShouldThrow()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkApproved(DateTime.UtcNow);
        var act = () => instance.MarkApproved(DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkRejected_FromRunning_ShouldSucceed()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkRejected(DateTime.UtcNow);
        instance.Status.Should().Be(WorkflowStatus.Rejected);
        instance.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkRejected_FromRejected_ShouldThrow()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkRejected(DateTime.UtcNow);
        var act = () => instance.MarkRejected(DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkReturned_FromRunning_ShouldSucceed()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkReturned(DateTime.UtcNow);
        instance.Status.Should().Be(WorkflowStatus.Returned);
        instance.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkCancelled_FromRunning_ShouldSucceed()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkCancelled(DateTime.UtcNow);
        instance.Status.Should().Be(WorkflowStatus.Cancelled);
        instance.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkSuperseded_FromRunning_ShouldSucceed()
    {
        var instance = WorkflowInstance.Create("DB_KCC", "CHORDR", "100", "title", "manager", "Manager", "v1", DateTime.UtcNow);
        instance.MarkSuperseded(DateTime.UtcNow);
        instance.Status.Should().Be(WorkflowStatus.Superceded);
        instance.FinishedAt.Should().NotBeNull();
    }
}
