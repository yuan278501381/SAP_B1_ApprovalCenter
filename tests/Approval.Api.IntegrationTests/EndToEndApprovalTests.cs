using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Approval.Api.IntegrationTests;

public class EndToEndApprovalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EndToEndApprovalTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CompleteApprovalFlow_CHORDR_ViaApi_ShouldSucceed()
    {
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // 1. 提交型号订单审批 (金额 85,600 > 50,000，走 director 终审节点)
        var submitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/CHORDR/1001/submit?companyId=DB_KCC");
        submitRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        submitRequest.Headers.Add("X-Trace-Id", "trace_e2e_chordr");
        AddIdentity(submitRequest, "manager", "张经理");

        var submitResp = await _client.SendAsync(submitRequest);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitBody = await submitResp.Content.ReadFromJsonAsync<JsonObject>();
        submitBody.Should().NotBeNull();
        submitBody!["success"]!.GetValue<bool>().Should().BeTrue();

        var instanceId = submitBody["data"]!["instanceId"]!.GetValue<string>();
        instanceId.Should().NotBeNullOrEmpty();

        // 2. 查询 director 待办列表
        var tasksRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks?scope=mine&status=pending");
        AddIdentity(tasksRequest, "director", "业务总监");
        var tasksResp = await _client.SendAsync(tasksRequest);
        tasksResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasksBody = await tasksResp.Content.ReadFromJsonAsync<JsonObject>();
        var items = tasksBody!["data"]!["items"]!.AsArray();
        items.Should().NotBeEmpty();

        var pendingTaskId = items.First()!["taskId"]!.GetValue<string>();
        pendingTaskId.Should().NotBeNullOrEmpty();

        // 3. 提交审批同意决定
        var decisionRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{pendingTaskId}/decisions");
        decisionRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(decisionRequest, "director", "业务总监");
        decisionRequest.Content = JsonContent.Create(new { Decision = "Approve", Comments = "型号订单大额采购终审同意" });

        var decisionResp = await _client.SendAsync(decisionRequest);
        decisionResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var decisionBody = await decisionResp.Content.ReadFromJsonAsync<JsonObject>();
        decisionBody!["success"]!.GetValue<bool>().Should().BeTrue();

        // 4. 查询实例不可变审计证据链与快照签名
        var auditRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/instances/{instanceId}/audit");
        AddIdentity(auditRequest, "director", "业务总监");
        var auditResp = await _client.SendAsync(auditRequest);
        auditResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var auditBody = await auditResp.Content.ReadFromJsonAsync<JsonObject>();
        var snapshot = auditBody!["data"]!["snapshot"]!;
        snapshot["dataSha256"]!.GetValue<string>().Should().NotBeNullOrEmpty();

        var auditLogs = auditBody["data"]!["auditLogs"]!.AsArray();
        auditLogs.Should().HaveCountGreaterOrEqualTo(2); // Submit + Approve
    }

    [Fact]
    public async Task CompleteApprovalFlow_CHOQUT_GenericAdapter_ShouldSucceed()
    {
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // 1. 提交型号报价单审批 (验证通用性，无需修改领域内核)
        var submitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/CHOQUT/2001/submit?companyId=DB_KCC");
        submitRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        submitRequest.Headers.Add("X-Trace-Id", "trace_e2e_choqut");
        AddIdentity(submitRequest, "manager", "张经理");

        var submitResp = await _client.SendAsync(submitRequest);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitBody = await submitResp.Content.ReadFromJsonAsync<JsonObject>();
        submitBody!["success"]!.GetValue<bool>().Should().BeTrue();
        var instanceId = submitBody["data"]!["instanceId"]!.GetValue<string>();

        // 2. 查询 sales_mgr 待办
        var tasksRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks?scope=mine&status=pending");
        AddIdentity(tasksRequest, "sales_mgr", "销售经理");
        var tasksResp = await _client.SendAsync(tasksRequest);
        var tasksBody = await tasksResp.Content.ReadFromJsonAsync<JsonObject>();
        var items = tasksBody!["data"]!["items"]!.AsArray();
        items.Should().NotBeEmpty();

        var pendingTaskId = items.First()!["taskId"]!.GetValue<string>();

        // 3. 销售主管审批
        var decisionRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{pendingTaskId}/decisions");
        decisionRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(decisionRequest, "sales_mgr", "销售经理");
        decisionRequest.Content = JsonContent.Create(new { Decision = "Approve", Comments = "型号报价单核价通过" });
        var decisionResp = await _client.SendAsync(decisionRequest);
        decisionResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void AddIdentity(HttpRequestMessage request, string userCode, string userName)
    {
        request.Headers.Add("X-Approval-User", userCode);
        request.Headers.Add("X-Approval-User-Name", userName);
    }
}
