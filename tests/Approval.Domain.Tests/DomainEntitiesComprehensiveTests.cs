using Approval.Domain.Entities;
using Approval.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class DomainEntitiesComprehensiveTests
{
    [Fact]
    public void WorkflowActionLog_ShouldInitialize_AndTrackDetails()
    {
        var log = new WorkflowActionLog
        {
            TraceId = "tr_001",
            InstanceId = "inst_001",
            TaskId = "task_001",
            OperatorCode = "USER_MGR",
            OperatorName = "经理",
            Action = "Approve",
            FromStatus = "Running",
            ToStatus = "Approved",
            Comment = "核准放行",
            ClientIp = "192.168.1.100",
            ActionTime = DateTime.UtcNow
        };

        log.TraceId.Should().Be("tr_001");
        log.OperatorCode.Should().Be("USER_MGR");
        log.Action.Should().Be("Approve");
        log.ActionTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WorkflowSnapshot_ShouldRetainCanonicalAndHash()
    {
        var snapshot = new WorkflowSnapshot
        {
            InstanceId = "inst_002",
            RawJson = """{"DocTotal": 100}""",
            CanonicalJson = """{"DocTotal":100}""",
            DataSha256 = "6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b",
            SnapshottedAt = DateTime.UtcNow
        };

        snapshot.InstanceId.Should().Be("inst_002");
        snapshot.DataSha256.Should().HaveLength(64);
        snapshot.CanonicalJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SysUiLayout_ShouldSupportUserAndCompanyCustomizations()
    {
        var layout = new SysUiLayout
        {
            UserCode = "USER01",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            LayoutJson = """{"pinnedKeys": ["DocNum"], "zebraPattern": true}""",
            ConfigType = "HeaderAndTableLayout",
            UpdatedAt = DateTime.UtcNow
        };

        layout.UserCode.Should().Be("USER01");
        layout.ObjectCode.Should().Be("CHORDR");
        layout.LayoutJson.Should().Contain("zebraPattern");
    }

    [Fact]
    public void SysUserMapping_ShouldSupportDelegateTimeWindow()
    {
        var mapping = new SysUserMapping
        {
            SapUserCode = "SALES01",
            AdUserCode = "sales01@company.com",
            ManagerCode = "MGR01",
            DelegateUserCode = "SALES_BACKUP",
            DelegateStartTime = DateTime.UtcNow.AddDays(-1),
            DelegateEndTime = DateTime.UtcNow.AddDays(7),
            Department = "销售一部"
        };

        mapping.SapUserCode.Should().Be("SALES01");
        mapping.ManagerCode.Should().Be("MGR01");
        mapping.DelegateUserCode.Should().Be("SALES_BACKUP");
        mapping.Department.Should().Be("销售一部");
    }

    [Fact]
    public void WorkflowRule_ShouldSupportDeptAndUserScopeLists()
    {
        var rule = new WorkflowRule
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            RuleName = "销售二部特批",
            TriggerMode = "ExplicitCheckbox",
            TriggerFieldName = "U_APSubmit",
            UserScopeMode = UserScopeMode.Blacklist,
            UserScopeListJson = """["SALES_DIRECTOR"]""",
            DeptScopeListJson = """["SalesDept2"]""",
            Priority = 5,
            IsActive = true
        };

        rule.TriggerMode.Should().Be("ExplicitCheckbox");
        rule.UserScopeMode.Should().Be(UserScopeMode.Blacklist);
        rule.DeptScopeListJson.Should().Contain("SalesDept2");
    }
}
