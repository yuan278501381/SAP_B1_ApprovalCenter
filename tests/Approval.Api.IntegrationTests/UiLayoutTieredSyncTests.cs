using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Approval.Api.IntegrationTests;

public class UiLayoutTieredSyncTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UiLayoutTieredSyncTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static void AddIdentity(HttpRequestMessage request, string userCode, string userName)
    {
        request.Headers.Add("X-Approval-User", userCode);
        request.Headers.Add("X-Approval-User-Name", userName);
        request.Headers.Add("X-Trace-Id", "test_trace_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task UiLayout_TieredConfig_CompanyDefaultAndUserOverride_ShouldWorkCorrectly()
    {
        var companyId = "DB_KCC";
        var objectCode = "CHORDR_TEST_TIERED";

        // 1. Admin 保存全公司全局默认配置
        var globalReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ui-layouts/global");
        AddIdentity(globalReq, "admin", "系统管理员");
        globalReq.Content = JsonContent.Create(new
        {
            CompanyId = companyId,
            ObjectCode = objectCode,
            LayoutJson = "{\"pinnedKeys\":[\"DocTotal\",\"CardName\"],\"hiddenHeaderKeys\":[\"U_Comments\"]}"
        });
        var globalResp = await _client.SendAsync(globalReq);
        globalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. 普通销售员 sales01 查询配置，应继承全公司全局默认配置
        var salesGetReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ui-layouts?companyId={companyId}&objectCode={objectCode}");
        AddIdentity(salesGetReq, "sales01", "销售员01");
        var salesGetResp = await _client.SendAsync(salesGetReq);
        salesGetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var salesGetBody = await salesGetResp.Content.ReadFromJsonAsync<JsonObject>();
        salesGetBody!["data"]!["isUserCustomized"]!.GetValue<bool>().Should().BeFalse();
        salesGetBody["data"]!["effectiveLayoutJson"]!.GetValue<string>().Should().Contain("CardName");

        // 3. sales01 保存自己的个性化偏好
        var salesSaveReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ui-layouts");
        AddIdentity(salesSaveReq, "sales01", "销售员01");
        salesSaveReq.Content = JsonContent.Create(new
        {
            CompanyId = companyId,
            ObjectCode = objectCode,
            LayoutJson = "{\"pinnedKeys\":[\"DocTotal\",\"U_SpecialCode\"],\"hiddenHeaderKeys\":[]}"
        });
        var salesSaveResp = await _client.SendAsync(salesSaveReq);
        salesSaveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. sales01 再次查询，应优先返回其个人偏好
        var salesGetReq2 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ui-layouts?companyId={companyId}&objectCode={objectCode}");
        AddIdentity(salesGetReq2, "sales01", "销售员01");
        var salesGetResp2 = await _client.SendAsync(salesGetReq2);
        var salesGetBody2 = await salesGetResp2.Content.ReadFromJsonAsync<JsonObject>();
        salesGetBody2!["data"]!["isUserCustomized"]!.GetValue<bool>().Should().BeTrue();
        salesGetBody2["data"]!["effectiveLayoutJson"]!.GetValue<string>().Should().Contain("U_SpecialCode");

        // 5. sales01 重置个人偏好，应恢复为全公司默认
        var resetReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/ui-layouts?companyId={companyId}&objectCode={objectCode}");
        AddIdentity(resetReq, "sales01", "销售员01");
        var resetResp = await _client.SendAsync(resetReq);
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var salesGetReq3 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/ui-layouts?companyId={companyId}&objectCode={objectCode}");
        AddIdentity(salesGetReq3, "sales01", "销售员01");
        var salesGetResp3 = await _client.SendAsync(salesGetReq3);
        var salesGetBody3 = await salesGetResp3.Content.ReadFromJsonAsync<JsonObject>();
        salesGetBody3!["data"]!["isUserCustomized"]!.GetValue<bool>().Should().BeFalse();
        salesGetBody3["data"]!["effectiveLayoutJson"]!.GetValue<string>().Should().Contain("CardName");
    }
}
