using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Approval.Api.IntegrationTests;

public class RuleMatchingAndReRoutingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RuleMatchingAndReRoutingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Approval-User", "manager");
    }

    [Fact]
    public async Task WhitelistAndBlacklist_RuleMatching_ShouldFilterCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();

        // 1. 创建白名单规则：仅 sales01 和 sales02 触发
        var ruleWhite = new WorkflowRule
        {
            Id = "RULE_TEST_WHITE_" + Guid.NewGuid().ToString("N")[..8],
            CompanyId = "DB_KCC",
            ObjectCode = "TEST_WHITE",
            ObjectType = "Document",
            RuleName = "销售员白名单规则",
            UserScopeMode = UserScopeMode.Whitelist,
            UserScopeListJson = "[\"sales01\", \"sales02\"]",
            TargetDefinitionId = "DEF_CHORDR",
            TargetVersionId = "VER_CHORDR_V1",
            Priority = 1,
            IsActive = true
        };
        await db.Rules.AddAsync(ruleWhite);
        await db.SaveChangesAsync();

        // 2. 测试 sales01 (白名单内) -> 应该命中
        var testReq1 = new
        {
            CompanyId = "DB_KCC",
            ObjectCode = "TEST_WHITE",
            CreatorUserCode = "sales01",
            DocTotal = 10000m
        };
        var resp1 = await _client.PostAsJsonAsync("/api/v1/rules/test-match", testReq1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var json1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        json1.GetProperty("data").GetProperty("shouldTrigger").GetBoolean().Should().BeTrue();

        // 3. 测试 otherUser (白名单外) -> 应该不触发
        var testReq2 = new
        {
            CompanyId = "DB_KCC",
            ObjectCode = "TEST_WHITE",
            CreatorUserCode = "otherUser",
            DocTotal = 10000m
        };
        var resp2 = await _client.PostAsJsonAsync("/api/v1/rules/test-match", testReq2);
        var json2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        json2.GetProperty("data").GetProperty("shouldTrigger").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AmountCondition_TierRouting_ShouldMatchAppropriateWorkflow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();

        // 小额规则 (<= 50,000) -> 路由 DEF_CHOQUT
        var ruleSmall = new WorkflowRule
        {
            Id = "RULE_TIER_SMALL_" + Guid.NewGuid().ToString("N")[..8],
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR_TIER",
            RuleName = "普通小额单据",
            ConditionExpr = "DocTotal <= 50000",
            TargetDefinitionId = "DEF_CHOQUT",
            TargetVersionId = "VER_CHOQUT_V1",
            Priority = 5,
            IsActive = true
        };
        // 大额规则 (> 50,000) -> 路由 DEF_CHORDR
        var ruleLarge = new WorkflowRule
        {
            Id = "RULE_TIER_LARGE_" + Guid.NewGuid().ToString("N")[..8],
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR_TIER",
            RuleName = "大额特殊审批",
            ConditionExpr = "DocTotal > 50000",
            TargetDefinitionId = "DEF_CHORDR",
            TargetVersionId = "VER_CHORDR_V1",
            Priority = 10,
            IsActive = true
        };
        await db.Rules.AddRangeAsync(ruleSmall, ruleLarge);
        await db.SaveChangesAsync();

        // 验证 30,000 -> 命中 DEF_CHOQUT
        var respSmall = await _client.PostAsJsonAsync("/api/v1/rules/test-match", new
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR_TIER",
            CreatorUserCode = "manager",
            DocTotal = 30000m
        });
        var jsonSmall = await respSmall.Content.ReadFromJsonAsync<JsonElement>();
        jsonSmall.GetProperty("data").GetProperty("targetDefinitionId").GetString().Should().Be("DEF_CHOQUT");

        // 验证 80,000 -> 命中 DEF_CHORDR
        var respLarge = await _client.PostAsJsonAsync("/api/v1/rules/test-match", new
        {
            CompanyId = "DB_KCC",
            ObjectCode = "CHORDR_TIER",
            CreatorUserCode = "manager",
            DocTotal = 80000m
        });
        var jsonLarge = await respLarge.Content.ReadFromJsonAsync<JsonElement>();
        jsonLarge.GetProperty("data").GetProperty("targetDefinitionId").GetString().Should().Be("DEF_CHORDR");
    }

    [Fact]
    public async Task CompositeHeaderAndLineItem_RuleMatching_ShouldEvaluateCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();

        // 创建复合规则：表头金额 >= 50000 且 明细行包含物料 A0002 且 数量 >= 50
        var compositeConditionJson = """
        {
          "combine": "AND",
          "headerConditions": [
            { "field": "DocTotal", "op": ">=", "value": "50000" },
            { "field": "CardCode", "op": "IN", "value": "C20000,C30000" }
          ],
          "lineConditions": [
            {
              "collection": "CH_ORDR_1Collection",
              "mode": "ANY",
              "field": "ItemCode",
              "op": "==",
              "value": "A0002"
            },
            {
              "collection": "CH_ORDR_1Collection",
              "mode": "ANY",
              "field": "Quantity",
              "op": ">=",
              "value": "50"
            }
          ]
        }
        """;

        var rule = new WorkflowRule
        {
            Id = "RULE_COMPOSITE_" + Guid.NewGuid().ToString("N")[..8],
            CompanyId = "DB_KCC",
            ObjectCode = "TEST_COMPOSITE",
            ObjectType = "Document",
            RuleName = "高危物料大额复合规则",
            UserScopeMode = UserScopeMode.All,
            ConditionExpr = compositeConditionJson,
            TargetDefinitionId = "DEF_CHORDR",
            TargetVersionId = "VER_CHORDR_V1",
            Priority = 1,
            IsActive = true
        };
        await db.Rules.AddAsync(rule);
        await db.SaveChangesAsync();

        // 1. 完全命中测试 (金额85600, 客户C20000, 行表含 A0002 数量50)
        var hitPayload = new
        {
            CompanyId = "DB_KCC",
            ObjectCode = "TEST_COMPOSITE",
            CreatorUserCode = "manager",
            DocTotal = 85600m,
            HeaderFields = new Dictionary<string, object>
            {
                ["CardCode"] = "C20000"
            },
            RawJson = """
            {
              "DocEntry": 1001,
              "DocTotal": 85600,
              "CardCode": "C20000",
              "CH_ORDR_1Collection": [
                { "LineId": 1, "ItemCode": "A0001", "Quantity": 10 },
                { "LineId": 2, "ItemCode": "A0002", "Quantity": 50 }
              ]
            }
            """
        };
        var resp1 = await _client.PostAsJsonAsync("/api/v1/rules/test-match", hitPayload);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var json1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        json1.GetProperty("data").GetProperty("shouldTrigger").GetBoolean().Should().BeTrue();

        // 2. 行表不匹配测试 (行表无 A0002)
        var missPayload = new
        {
            CompanyId = "DB_KCC",
            ObjectCode = "TEST_COMPOSITE",
            CreatorUserCode = "manager",
            DocTotal = 85600m,
            HeaderFields = new Dictionary<string, object>
            {
                ["CardCode"] = "C20000"
            },
            RawJson = """
            {
              "DocEntry": 1001,
              "DocTotal": 85600,
              "CardCode": "C20000",
              "CH_ORDR_1Collection": [
                { "LineId": 1, "ItemCode": "A0001", "Quantity": 10 }
              ]
            }
            """
        };
        var resp2 = await _client.PostAsJsonAsync("/api/v1/rules/test-match", missPayload);
        var json2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        json2.GetProperty("data").GetProperty("shouldTrigger").GetBoolean().Should().BeFalse();
    }
}
