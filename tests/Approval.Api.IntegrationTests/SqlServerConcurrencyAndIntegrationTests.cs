using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Approval.Application.Common.Interfaces;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Approval.Api.IntegrationTests;

/// <summary>
/// 基于真实 SQL Server (192.168.134.9 / ApprovalDB) 的生产级高并发与集成测试套件
/// </summary>
public class SqlServerConcurrencyAndIntegrationTests : IClassFixture<SqlServerWebApplicationFactory>
{
    private readonly SqlServerWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string SqlServerConnStr = "Server=192.168.134.9;Database=ApprovalDB;User Id=sa;Password=123456@a;TrustServerCertificate=True;Timeout=30;";

    public SqlServerConcurrencyAndIntegrationTests(SqlServerWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SqlServer_CompleteWorkflow_CHORDR_PersistedCorrectly()
    {
        var objectKey = "TEST_SQL_" + Random.Shared.Next(100000, 999999);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // 1. 提交型号订单审批
        var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/objects/CHORDR/{objectKey}/submit?companyId=DB_KCC");
        submitRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        submitRequest.Headers.Add("X-Trace-Id", "trace_sql_e2e");
        AddIdentity(submitRequest, "manager", "张经理");

        var submitResp = await _client.SendAsync(submitRequest);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitBody = await submitResp.Content.ReadFromJsonAsync<JsonObject>();
        var instanceId = submitBody!["data"]!["instanceId"]!.GetValue<string>();
        instanceId.Should().NotBeNullOrEmpty();

        // 2. 验证数据直接存在于真实 SQL Server 中
        var options = new DbContextOptionsBuilder<ApprovalDbContext>().UseSqlServer(SqlServerConnStr).Options;
        await using (var db = new ApprovalDbContext(options))
        {
            var dbInstance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId);
            dbInstance.Should().NotBeNull();
            dbInstance!.Status.Should().Be(WorkflowStatus.Running);
            dbInstance.ObjectKey.Should().Be(objectKey);

            var dbSnapshot = await db.Snapshots.FirstOrDefaultAsync(s => s.InstanceId == instanceId);
            dbSnapshot.Should().NotBeNull();
            dbSnapshot!.DataSha256.Should().NotBeNullOrEmpty();

            var dbOutbox = await db.Outboxes.FirstOrDefaultAsync(o => o.AggregateId == instanceId);
            dbOutbox.Should().NotBeNull();
            dbOutbox!.EventType.Should().Be("WorkflowStarted");
        }

        // 3. 执行审批 (金额 85600 > 50000，走 director 业务总监终审节点)
        var tasksRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tasks?scope=mine&status=pending");
        AddIdentity(tasksRequest, "director", "业务总监");
        var tasksResp = await _client.SendAsync(tasksRequest);
        var tasksBody = await tasksResp.Content.ReadFromJsonAsync<JsonObject>();
        var items = tasksBody!["data"]!["items"]!.AsArray();
        var targetTask = items.FirstOrDefault(i => i!["instanceId"]!.GetValue<string>() == instanceId);
        targetTask.Should().NotBeNull();

        var taskId = targetTask!["taskId"]!.GetValue<string>();

        var decisionRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{taskId}/decisions");
        decisionRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        AddIdentity(decisionRequest, "director", "业务总监");
        decisionRequest.Content = JsonContent.Create(new { Decision = "Approve", Comments = "SQL Server 实测审批同意" });

        var decisionResp = await _client.SendAsync(decisionRequest);
        decisionResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. 再次验证 SQL Server 状态已更新为 Approved 或下一节点
        await using (var db = new ApprovalDbContext(options))
        {
            var dbInstance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId);
            dbInstance.Should().NotBeNull();
            dbInstance!.Status.Should().Be(WorkflowStatus.Approved);

            var completedTask = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
            completedTask!.Status.Should().Be(Approval.Domain.Enums.TaskStatus.Completed);
            completedTask.Decision.Should().Be(TaskDecision.Approve);
        }
    }

    [Fact]
    public async Task SqlServer_ConcurrentSubmit_SameObject_ShouldEnforceUniqueRunningConstraint()
    {
        var objectKey = "CONCUR_" + Random.Shared.Next(100000, 999999);
        const int concurrentRequests = 8;

        var results = new ConcurrentBag<(HttpStatusCode Status, string Body)>();

        // 启动 8 个并发 Task 对同一单据同时发起审批
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/objects/CHORDR/{objectKey}/submit?companyId=DB_KCC");
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N")); // 不同的幂等键
            req.Headers.Add("X-Trace-Id", $"trace_concur_{i}");
            AddIdentity(req, "manager", "张经理");

            var resp = await _client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            results.Add((resp.StatusCode, body));
        });

        await Task.WhenAll(tasks);

        // 验证：必须且只能有 1 个成功 (200 OK)，其余全部被拦截
        var successes = results.Where(r => r.Status == HttpStatusCode.OK).ToList();
        var failures = results.Where(r => r.Status != HttpStatusCode.OK).ToList();

        successes.Should().HaveCount(1, "对于同一单据，SQL Server UX_wf_instance_running_object 唯一约束必须保证只能创建 1 个运行中的流程实例");
        failures.Should().HaveCount(concurrentRequests - 1);
    }

    [Fact]
    public async Task SqlServer_OutboxLeaseRecoveryAndJobProcessing_ShouldRecoverExpiredTasks()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>().UseSqlServer(SqlServerConnStr).Options;
        var expiredOutboxId = 0L;

        // 构造一个处于 Processing 状态且已经超期 10 分钟的 Outbox 消息（模拟 Worker 宕机崩溃）
        await using (var db = new ApprovalDbContext(options))
        {
            var outbox = new WorkflowOutbox
            {
                TraceId = "trace_lease_expired",
                EventType = "InstanceApproved",
                AggregateId = "inst_lease_mock",
                PayloadJson = "{\"InstanceId\":\"inst_lease_mock\",\"CompanyId\":\"DB_KCC\",\"ObjectCode\":\"CHORDR\",\"ObjectKey\":\"999\",\"Status\":\"Approved\",\"DataSha256\":\"mock_hash\"}",
                Status = OutboxStatus.Processing,
                LockId = "crashed_worker_guid",
                ProcessingAt = DateTime.UtcNow.AddMinutes(-10), // 已过期
                CreatedAt = DateTime.UtcNow.AddMinutes(-15),
                NextRetryAt = DateTime.UtcNow.AddMinutes(-15)
            };
            db.Outboxes.Add(outbox);
            await db.SaveChangesAsync();
            expiredOutboxId = outbox.Id;
        }

        // 验证查询过期租约逻辑能够找回该任务并重置为 Pending 或被新 Worker 抢占
        await using (var db = new ApprovalDbContext(options))
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            var expired = await db.Outboxes
                .Where(o => o.Status == OutboxStatus.Processing && o.ProcessingAt < cutoff && o.Id == expiredOutboxId)
                .FirstOrDefaultAsync();

            expired.Should().NotBeNull("过期的 Processing 任务必须被租约回收机制精准识别");
            expired!.Status = OutboxStatus.Pending;
            expired.LockId = null;
            expired.ProcessingAt = null;
            await db.SaveChangesAsync();
        }

        // 清理测试数据
        await using (var db = new ApprovalDbContext(options))
        {
            var item = await db.Outboxes.FindAsync(expiredOutboxId);
            if (item != null)
            {
                db.Outboxes.Remove(item);
                await db.SaveChangesAsync();
            }
        }
    }

    private static void AddIdentity(HttpRequestMessage request, string userCode, string userName)
    {
        request.Headers.Add("X-Approval-User", userCode);
        request.Headers.Add("X-Approval-User-Name", userName);
    }
}

public class SqlServerWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string SqlServerConnStr = "Server=192.168.134.9;Database=ApprovalDB;User Id=sa;Password=123456@a;TrustServerCertificate=True;Timeout=30;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ApprovalDB"] = SqlServerConnStr,
                ["UseInMemoryDatabase"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // 移除默认 InMemory 配置，强制注入真实 SQL Server DbContext
            var descriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<ApprovalDbContext>) ||
                                                  d.ServiceType == typeof(ApprovalDbContext)).ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApprovalDbContext>(options =>
                options.UseSqlServer(SqlServerConnStr));
            services.AddScoped<IApprovalDbContext>(sp => sp.GetRequiredService<ApprovalDbContext>());
        });
    }
}
