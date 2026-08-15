using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Approval.Api.IntegrationTests;

public class WorkflowRevokeAndNotificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WorkflowRevokeAndNotificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static void AddIdentity(HttpRequestMessage request, string userCode, string? userName = null)
    {
        request.Headers.Add("X-Approval-User", userCode);
        if (!string.IsNullOrWhiteSpace(userName))
            request.Headers.Add("X-Approval-User-Name", userName);
    }

    [Fact]
    public async Task SubmitterRevoke_ShouldCancelWorkflow_AndNotifyApprovers_ExcludingSubmitter()
    {
        // 1. 发起人 manager 提交型号订单 #5001
        var submitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/CHORDR/5001/submit?companyId=DB_KCC");
        submitRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        submitRequest.Headers.Add("X-Trace-Id", "trace_revoke_test_1");
        AddIdentity(submitRequest, "manager", "张经理");

        var submitResp = await _client.SendAsync(submitRequest);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitBody = await submitResp.Content.ReadFromJsonAsync<JsonObject>();
        var instanceId = submitBody!["data"]!["instanceId"]!.GetValue<string>();

        // 2. 发起人 manager 撤回审批申请
        var revokeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/instances/{instanceId}/revoke");
        revokeRequest.Headers.Add("X-Trace-Id", "trace_revoke_test_1");
        AddIdentity(revokeRequest, "manager", "张经理");
        revokeRequest.Content = JsonContent.Create(new { Reason = "单价录入错误需要重做" });

        var revokeResp = await _client.SendAsync(revokeRequest);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokeBody = await revokeResp.Content.ReadFromJsonAsync<JsonObject>();
        revokeBody!["data"]!["status"]!.GetValue<string>().Should().Be("Cancelled");

        // 3. 验证审计日志
        var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/instances/{instanceId}/audit");
        AddIdentity(auditReq, "manager", "张经理");
        var auditResp = await _client.SendAsync(auditReq);
        auditResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditBody = await auditResp.Content.ReadFromJsonAsync<JsonObject>();
        auditBody!["data"]!["instance"]!["status"]!.GetValue<string>().Should().Be("Cancelled");

        // 4. 验证通知机制：
        // 审批人 director 应该收到撤销通知
        var dirNotifReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications");
        AddIdentity(dirNotifReq, "director", "李总监");
        var dirNotifResp = await _client.SendAsync(dirNotifReq);
        var dirNotifBody = await dirNotifResp.Content.ReadFromJsonAsync<JsonObject>();
        var dirItems = dirNotifBody!["data"]!["items"]!.AsArray();
        dirItems.Any(n => n!["instanceId"]!.GetValue<string>() == instanceId && n["type"]!.GetValue<string>() == "Revocation").Should().BeTrue();

        // 发起人 manager 自身绝对不应该收到撤销通知（精准排除过滤器生效）
        var mgrNotifReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications");
        AddIdentity(mgrNotifReq, "manager", "张经理");
        var mgrNotifResp = await _client.SendAsync(mgrNotifReq);
        var mgrNotifBody = await mgrNotifResp.Content.ReadFromJsonAsync<JsonObject>();
        var mgrItems = mgrNotifBody!["data"]!["items"]!.AsArray();
        mgrItems.Any(n => n!["instanceId"]!.GetValue<string>() == instanceId && n["type"]!.GetValue<string>() == "Revocation").Should().BeFalse();
    }

    [Fact]
    public async Task NonSubmitter_AttemptRevoke_ShouldBeForbidden()
    {
        // 1. 发起人 manager 提交单据
        var submitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/CHORDR/5002/submit?companyId=DB_KCC");
        submitRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(submitRequest, "manager", "张经理");
        var submitResp = await _client.SendAsync(submitRequest);
        var submitBody = await submitResp.Content.ReadFromJsonAsync<JsonObject>();
        var instanceId = submitBody!["data"]!["instanceId"]!.GetValue<string>();

        // 2. 销售员 other_user 尝试撤回他人单据
        var revokeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/instances/{instanceId}/revoke");
        AddIdentity(revokeRequest, "other_user", "其他同事");
        revokeRequest.Content = JsonContent.Create(new { Reason = "恶意撤销" });

        var revokeResp = await _client.SendAsync(revokeRequest);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await revokeResp.Content.ReadFromJsonAsync<JsonObject>();
        body!["code"]!.GetValue<string>().Should().Be("REVOKE_DENIED");
    }
}
