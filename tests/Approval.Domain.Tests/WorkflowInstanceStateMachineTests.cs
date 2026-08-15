using Approval.Domain.Entities;
using Approval.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class WorkflowInstanceStateMachineTests
{
    [Fact]
    public void WorkflowInstance_ShouldInitialize_WithDefaultState()
    {
        var instance = new WorkflowInstance
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1001",
            Title = "中山市互森服饰有限公司 (C02503)",
            SubmitterCode = "MGR01",
            Status = WorkflowStatus.Running,
            CurrentVersionId = "ver_101"
        };

        instance.Status.Should().Be(WorkflowStatus.Running);
        instance.ObjectCode.Should().Be("CHORDR");
        instance.ObjectKey.Should().Be("1001");
        instance.SubmitterCode.Should().Be("MGR01");
        instance.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WorkflowInstance_ShouldSupportCompletionStatusTransitions()
    {
        var instance = new WorkflowInstance
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1002",
            Status = WorkflowStatus.Running
        };

        // 1. 通过
        instance.Status = WorkflowStatus.Approved;
        instance.FinishedAt = DateTime.UtcNow;
        instance.PostedDocEntry = "2001";
        instance.PostedDocNum = "SO-2026-0001";

        instance.Status.Should().Be(WorkflowStatus.Approved);
        instance.PostedDocEntry.Should().Be("2001");
        instance.FinishedAt.Should().NotBeNull();

        // 2. 驳回
        var rejectInstance = new WorkflowInstance
        {
            Status = WorkflowStatus.Rejected,
            FinishedAt = DateTime.UtcNow
        };
        rejectInstance.Status.Should().Be(WorkflowStatus.Rejected);

        // 3. 撤回
        var cancelInstance = new WorkflowInstance
        {
            Status = WorkflowStatus.Cancelled,
            FinishedAt = DateTime.UtcNow
        };
        cancelInstance.Status.Should().Be(WorkflowStatus.Cancelled);
    }

    [Fact]
    public void WorkflowTask_ShouldTrackCandidateAndDecisions()
    {
        var task = new WorkflowTask
        {
            InstanceId = "inst_01",
            NodeInstanceId = "node_01",
            TaskType = TaskType.Approve,
            Status = Approval.Domain.Enums.TaskStatus.Pending
        };

        task.Candidates.Add(new WorkflowTaskCandidate
        {
            UserCode = "MGR_FINANCE",
            UserName = "财务总监",
            CandidateType = CandidateType.Direct
        });

        task.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Pending);
        task.Candidates.Should().HaveCount(1);
        task.Candidates[0].UserCode.Should().Be("MGR_FINANCE");

        // 完成审批任务
        task.Status = Approval.Domain.Enums.TaskStatus.Completed;
        task.Decision = TaskDecision.Approve;
        task.Comments = "符合财务预算，予以核准通过";
        task.CompletedBy = "MGR_FINANCE";
        task.CompletedAt = DateTime.UtcNow;

        task.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Completed);
        task.Decision.Should().Be(TaskDecision.Approve);
        task.Comments.Should().Contain("核准通过");
    }

    [Fact]
    public void WorkflowOutbox_ShouldManageRetryCountAndStatus()
    {
        var outbox = new WorkflowOutbox
        {
            TraceId = "tr_test_123",
            EventType = "ApprovalCompletedEvent",
            AggregateId = "inst_01",
            PayloadJson = """{"Status": "Approved"}""",
            Status = OutboxStatus.Pending,
            RetryCount = 0
        };

        outbox.Status.Should().Be(OutboxStatus.Pending);
        outbox.RetryCount.Should().Be(0);

        // 模拟失败一次
        outbox.RetryCount++;
        outbox.ErrorMsg = "Service Layer 会话超时";
        outbox.NextRetryAt = DateTime.UtcNow.AddSeconds(30);

        outbox.RetryCount.Should().Be(1);
        outbox.ErrorMsg.Should().Contain("超时");

        // 模拟成功投递
        outbox.Status = OutboxStatus.Sent;
        outbox.SentAt = DateTime.UtcNow;

        outbox.Status.Should().Be(OutboxStatus.Sent);
        outbox.SentAt.Should().NotBeNull();
    }
}
