using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Approval.Domain.Tests;

public class WorkflowRuleMatcherMatrixTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly WorkflowRuleMatcher _matcher;

    public WorkflowRuleMatcherMatrixTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_RuleMatrix_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _matcher = new WorkflowRuleMatcher(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task MatchRule_ExplicitCheckbox_ShouldOnlyTriggerWhenFieldIsY()
    {
        var def = new WorkflowDefinition { Id = "DEF-EXP", Name = "显式勾选触发" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-EXP", DefinitionId = "DEF-EXP", Status = "Published" };
        var rule = new WorkflowRule
        {
            Id = "R-EXP",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            RuleName = "勾选特批复选框才触发",
            TriggerMode = "ExplicitCheckbox",
            TriggerFieldName = "U_APSubmit",
            TargetDefinitionId = "DEF-EXP",
            TargetVersionId = "VER-EXP",
            Priority = 1,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        // 1. U_APSubmit 为 N -> 不触发
        var payloadN = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "101",
            DocTotal = 100m,
            HeaderFields = new() { ["U_APSubmit"] = "N" },
            RawJson = """{"U_APSubmit": "N"}"""
        };
        var resN = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payloadN);
        resN.ShouldTrigger.Should().BeFalse();

        // 2. U_APSubmit 为 Y -> 触发
        var payloadY = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "102",
            DocTotal = 100m,
            HeaderFields = new() { ["U_APSubmit"] = "Y" },
            RawJson = """{"U_APSubmit": "Y"}"""
        };
        var resY = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payloadY);
        resY.ShouldTrigger.Should().BeTrue();
    }

    [Fact]
    public async Task MatchRule_Blacklist_ShouldExemptSpecifiedUsers()
    {
        var def = new WorkflowDefinition { Id = "DEF-BLK", Name = "黑名单免审" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-BLK", DefinitionId = "DEF-BLK", Status = "Published" };
        var rule = new WorkflowRule
        {
            Id = "R-BLK",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            RuleName = "高管免审",
            UserScopeMode = UserScopeMode.Blacklist,
            UserScopeListJson = """["GM_USER", "CEO_USER"]""",
            TargetDefinitionId = "DEF-BLK",
            TargetVersionId = "VER-BLK",
            Priority = 1,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        // 1. GM 制单 -> 属于黑名单，应当免审（不触发）
        var payloadGm = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "103", CreatorUserCode = "GM_USER", RawJson = "{}" };
        var resGm = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payloadGm);
        resGm.ShouldTrigger.Should().BeFalse();

        // 2. 普通员工制单 -> 不属于黑名单，触发审批
        var payloadNormal = new SapObjectPayload { CompanyId = "DB_KCC", ObjectCode = "CHORDR", ObjectKey = "104", CreatorUserCode = "STAFF_01", RawJson = "{}" };
        var resNormal = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payloadNormal);
        resNormal.ShouldTrigger.Should().BeTrue();
    }

    [Theory]
    [InlineData("ENDS_WITH", ".com", "test@company.com", true)]
    [InlineData("ENDS_WITH", ".org", "test@company.com", false)]
    [InlineData("NOT_IN", "VIP1,VIP2", "VIP3", true)]
    [InlineData("NOT_IN", "VIP1,VIP2", "VIP1", false)]
    public void EvaluateComparison_ExtendedOperators_ShouldEvaluateCorrectly(string op, string target, string actual, bool expected)
    {
        var res = WorkflowRuleMatcher.EvaluateComparison(actual, op, target);
        res.Should().Be(expected);
    }
}
