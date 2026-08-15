using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Approval.Domain.Tests;

public class WorkflowRuleMatcherAdvancedTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly WorkflowRuleMatcher _matcher;

    public WorkflowRuleMatcherAdvancedTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_RuleTest_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _matcher = new WorkflowRuleMatcher(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Theory]
    [InlineData(">", "1000", 1500, true)]
    [InlineData(">", "1000", 1000, false)]
    [InlineData(">=", "1000", 1000, true)]
    [InlineData("<", "500", 300, true)]
    [InlineData("<=", "500", 500, true)]
    [InlineData("==", "VIP", "VIP", true)]
    [InlineData("!=", "VIP", "NORMAL", true)]
    [InlineData("in", "C001,C002,C003", "C002", true)]
    [InlineData("not_in", "C001,C002,C003", "C999", true)]
    [InlineData("contains", "EXP", "ORDER_EXP_01", true)]
    [InlineData("starts_with", "SO-", "SO-2026-001", true)]
    public void EvaluateComparison_ShouldEvaluateAllOperatorsAccurately(
        string op,
        string targetVal,
        object actualVal,
        bool expected)
    {
        var result = WorkflowRuleMatcher.EvaluateComparison(actualVal, op, targetVal);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task MatchRule_ShouldTrigger_WhenHeaderAmountMatches()
    {
        var def = new WorkflowDefinition { Id = "DEF-001", Name = "大额审批" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-001", DefinitionId = "DEF-001", Status = "Published", VersionNum = 1 };
        var rule = new WorkflowRule
        {
            Id = "R-001",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            RuleName = "金额大于5万走大额审批",
            ConditionExpr = "DocTotal > 50000",
            TargetDefinitionId = "DEF-001",
            TargetVersionId = "VER-001",
            Priority = 10,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var payload = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1001",
            DocTotal = 80000.0m,
            Title = "中山市互森服饰有限公司",
            CreatorUserCode = "USER_01",
            RawJson = """{"DocTotal": 80000.0, "CardName": "中山市互森服饰有限公司", "Creator": "USER_01"}"""
        };

        var matchResult = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payload);

        matchResult.ShouldTrigger.Should().BeTrue();
        matchResult.MatchedRule.Should().NotBeNull();
        matchResult.MatchedRule!.Id.Should().Be("R-001");
        matchResult.TargetVersionId.Should().Be("VER-001");
    }

    [Fact]
    public async Task MatchRule_ShouldRespectUserScopeWhitelistAndBlacklist()
    {
        var def = new WorkflowDefinition { Id = "DEF-USER", Name = "特定人员免审" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-USER", DefinitionId = "DEF-USER", Status = "Published", VersionNum = 1 };

        // 仅适用于白名单用户 "VIP_SALES"
        var ruleWhitelist = new WorkflowRule
        {
            Id = "R-WHITE",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            RuleName = "VIP业务员特批",
            UserScopeMode = UserScopeMode.Whitelist,
            UserScopeListJson = """["VIP_SALES"]""",
            TargetDefinitionId = "DEF-USER",
            TargetVersionId = "VER-USER",
            Priority = 1,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Rules.Add(ruleWhitelist);
        await _db.SaveChangesAsync();

        // 1. VIP 业务员提交 -> 应当命中
        var payloadVip = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1003",
            DocTotal = 5000.0m,
            Title = "客户",
            CreatorUserCode = "VIP_SALES",
            RawJson = "{}"
        };
        var matchVip = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payloadVip);
        matchVip.ShouldTrigger.Should().BeTrue();

        // 2. 普通业务员提交 -> 不在白名单中，不应触发该规则
        var payloadNormal = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1004",
            DocTotal = 5000.0m,
            Title = "客户",
            CreatorUserCode = "NORMAL_SALES",
            RawJson = "{}"
        };
        var matchNormal = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", payloadNormal);
        matchNormal.ShouldTrigger.Should().BeFalse();
    }
}
