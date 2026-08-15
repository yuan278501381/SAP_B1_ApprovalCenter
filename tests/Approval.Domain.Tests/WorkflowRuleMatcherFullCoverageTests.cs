using Approval.Application.Common.Models;
using Approval.Application.Services;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Approval.Domain.Tests;

public class WorkflowRuleMatcherFullCoverageTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly WorkflowRuleMatcher _matcher;

    public WorkflowRuleMatcherFullCoverageTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_RuleFull_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _matcher = new WorkflowRuleMatcher(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task MatchRule_DeptScope_ShouldFilterCorrectly()
    {
        var def = new WorkflowDefinition { Id = "DEF-DEPT", Name = "部门过滤" };
        var ver = new WorkflowDefinitionVersion { Id = "VER-DEPT", DefinitionId = "DEF-DEPT", Status = "Published" };
        var rule = new WorkflowRule
        {
            Id = "R-DEPT",
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            RuleName = "仅销售一部特批",
            DeptScopeListJson = """["Dept_Sales_01"]""",
            TargetDefinitionId = "DEF-DEPT",
            TargetVersionId = "VER-DEPT",
            Priority = 1,
            IsActive = true
        };

        _db.Definitions.Add(def);
        _db.DefinitionVersions.Add(ver);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        // 1. 销售一部 -> 匹配 (通过表头 U_Department 传入)
        var p1 = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "1",
            HeaderFields = new() { ["U_Department"] = "Dept_Sales_01" },
            RawJson = """{"U_Department": "Dept_Sales_01"}"""
        };
        var res1 = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", p1);
        res1.ShouldTrigger.Should().BeTrue();

        // 2. 采购部 -> 不匹配
        var p2 = new SapObjectPayload
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR",
            ObjectKey = "2",
            HeaderFields = new() { ["U_Department"] = "Dept_Purchase" },
            RawJson = """{"U_Department": "Dept_Purchase"}"""
        };
        var res2 = await _matcher.MatchRuleAsync("DB_KCC", "CHORDR", p2);
        res2.ShouldTrigger.Should().BeFalse();
    }

    [Theory]
    [InlineData("100", "==", "100", true)]
    [InlineData("100", "!=", "200", true)]
    [InlineData("100", "<", "200", true)]
    [InlineData("200", ">", "100", true)]
    [InlineData("100", "<=", "100", true)]
    [InlineData("100", ">=", "100", true)]
    [InlineData("abc", "CONTAINS", "b", true)]
    [InlineData("abc", "STARTS_WITH", "a", true)]
    [InlineData("abc", "ENDS_WITH", "c", true)]
    [InlineData("itemA", "IN", "itemA,itemB", true)]
    [InlineData("itemC", "NOT_IN", "itemA,itemB", true)]
    [InlineData(null, "==", "100", false)]
    public void EvaluateComparison_AllCombinations_ShouldReturnExpectedResult(object? actual, string op, string target, bool expected)
    {
        var res = WorkflowRuleMatcher.EvaluateComparison(actual, op, target);
        res.Should().Be(expected);
    }
}
