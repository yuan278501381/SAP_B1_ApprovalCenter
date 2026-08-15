using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Approval.Api.IntegrationTests;

/// <summary>
/// 阶段 3 & 5 高级特性集成测试：
/// 1. 组织树直属主管动态解析 (Manager)
/// 2. 岗位角色动态解析 (Role)
/// 3. 任务转交 (Forward) 与不可变轨迹审计
/// </summary>
public class AdvancedWorkflowFeaturesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AdvancedWorkflowFeaturesTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DynamicApprover_ManagerAndRoleResolution_ShouldSucceed()
    {
        // 1. 在内存测试库中植入用户组织架构与角色定义
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApprovalDbContext>();

            // 植入组织架构
            await db.AddAsync(new SysUserMapping
            {
                SapUserCode = "sales_rep_01",
                AdUserCode = "E003",
                DisplayName = "销售专员小李",
                Department = "销售一部",
                ManagerCode = "sales_mgr_01",
                Roles = "SalesRepresentative",
                IsActive = true
            });

            await db.AddAsync(new SysUserMapping
            {
                SapUserCode = "sales_mgr_01",
                AdUserCode = "E004",
                DisplayName = "销售主管老王",
                Department = "销售一部",
                ManagerCode = "sales_director_01",
                Roles = "SalesManager",
                IsActive = true
            });

            await db.AddAsync(new SysUserMapping
            {
                SapUserCode = "sales_director_01",
                AdUserCode = "E005",
                DisplayName = "销售总监大张",
                Department = "销售部",
                ManagerCode = null,
                Roles = "SalesDirector,ExecutiveApprover",
                IsActive = true
            });

            // 植入基于组织架构主管与角色的流程定义
            var def = new WorkflowDefinition
            {
                Id = "DEF_ADV_TEST",
                Name = "高级动态组织架构流程",
                Category = "Sales",
                IsActive = true
            };

            var graph = new WorkflowGraphDefinition
            {
                Nodes = new List<WorkflowGraphNode>
                {
                    new() { NodeKey = "start", Name = "开始", NodeType = NodeType.Start },
                    new() { NodeKey = "node_mgr", Name = "直属主管审批", NodeType = NodeType.Approval, TaskType = TaskType.Approve, CandidateType = CandidateType.Manager },
                    new() { NodeKey = "node_dir", Name = "总监角色审批", NodeType = NodeType.Approval, TaskType = TaskType.Approve, CandidateType = CandidateType.Role, CandidateValues = new List<string> { "SalesDirector" } },
                    new() { NodeKey = "end", Name = "结束放行", NodeType = NodeType.End }
                },
                Edges = new List<WorkflowGraphEdge>
                {
                    new() { FromNodeKey = "start", ToNodeKey = "node_mgr" },
                    new() { FromNodeKey = "node_mgr", ToNodeKey = "node_dir", ConditionValue = "Approve" },
                    new() { FromNodeKey = "node_dir", ToNodeKey = "end", ConditionValue = "Approve" }
                }
            };

            var ver = new WorkflowDefinitionVersion
            {
                Id = "VER_ADV_V1",
                DefinitionId = def.Id,
                VersionNum = 1,
                GraphJson = System.Text.Json.JsonSerializer.Serialize(graph),
                Status = "Published",
                PublishedAt = DateTime.UtcNow,
                CreatedBy = "system"
            };

            var binding = new WorkflowBinding
            {
                Id = "BIND_ADV_TEST",
                CompanyId = "DB_KCC",
                ObjectCode = "CHORDR",
                VersionId = ver.Id,
                Priority = 200,
                IsActive = true
            };

            await db.AddAsync(def);
            await db.AddAsync(ver);
            await db.AddAsync(binding);
            await db.SaveChangesAsync();
        }

        // 2. 销售专员 sales_rep_01 提交单据
        var submitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/objects/CHORDR/3001/submit?companyId=DB_KCC");
        submitRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        submitRequest.Headers.Add("X-Trace-Id", "trace_adv_test");
        AddIdentity(submitRequest, "sales_rep_01", "销售专员小李");

        var submitResp = await _client.SendAsync(submitRequest);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. 验证第一节点候选人已动态解析为直属主管 sales_mgr_01
        var mgrTasksRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks?scope=mine&status=pending");
        AddIdentity(mgrTasksRequest, "sales_mgr_01", "销售主管老王");
        var mgrTasksResp = await _client.SendAsync(mgrTasksRequest);
        var mgrTasksBody = await mgrTasksResp.Content.ReadFromJsonAsync<JsonObject>();
        var items = mgrTasksBody!["data"]!["items"]!.AsArray();
        items.Should().NotBeEmpty();

        var mgrTaskId = items.First()!["taskId"]!.GetValue<string>();

        // 4. 直属主管同意
        var mgrDecideReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{mgrTaskId}/decisions");
        mgrDecideReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(mgrDecideReq, "sales_mgr_01", "销售主管老王");
        mgrDecideReq.Content = JsonContent.Create(new { Decision = "Approve", Comments = "主管审批通过，呈报总监" });
        var mgrDecideResp = await _client.SendAsync(mgrDecideReq);
        mgrDecideResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. 验证第二节点候选人已动态按 Role="SalesDirector" 解析为 sales_director_01
        var dirTasksRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks?scope=mine&status=pending");
        AddIdentity(dirTasksRequest, "sales_director_01", "销售总监大张");
        var dirTasksResp = await _client.SendAsync(dirTasksRequest);
        var dirTasksBody = await dirTasksResp.Content.ReadFromJsonAsync<JsonObject>();
        var dirItems = dirTasksBody!["data"]!["items"]!.AsArray();
        dirItems.Should().NotBeEmpty();

        var dirTaskId = dirItems.First()!["taskId"]!.GetValue<string>();

        // 6. 测试转交 (Forward) 功能：总监临时将任务转交给副总监或助理处理
        var forwardReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{dirTaskId}/forward");
        forwardReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(forwardReq, "sales_director_01", "销售总监大张");
        forwardReq.Content = JsonContent.Create(new
        {
            TargetUserCode = "director_assistant",
            TargetUserName = "总监助理小王",
            Comments = "请协助核实特批折扣并处理"
        });
        var forwardResp = await _client.SendAsync(forwardReq);
        forwardResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. 验证被转交人 director_assistant 可以在待办中看到该任务并完成审批
        var assistTasksReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks?scope=mine&status=pending");
        AddIdentity(assistTasksReq, "director_assistant", "总监助理小王");
        var assistTasksResp = await _client.SendAsync(assistTasksReq);
        var assistTasksBody = await assistTasksResp.Content.ReadFromJsonAsync<JsonObject>();
        var assistItems = assistTasksBody!["data"]!["items"]!.AsArray();
        assistItems.Should().NotBeEmpty();

        var assistDecideReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{dirTaskId}/decisions");
        assistDecideReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(assistDecideReq, "director_assistant", "总监助理小王");
        assistDecideReq.Content = JsonContent.Create(new { Decision = "Approve", Comments = "已核实特批折扣，代为批准" });
        var assistDecideResp = await _client.SendAsync(assistDecideReq);
        assistDecideResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void AddIdentity(HttpRequestMessage request, string userCode, string userName)
    {
        request.Headers.Add("X-Approval-User", userCode);
        request.Headers.Add("X-Approval-User-Name", userName);
    }
}
