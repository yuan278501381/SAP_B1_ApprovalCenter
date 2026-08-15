using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.SapAdapter;
using Approval.SapAdapter.Adapters;
using Approval.Worker;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Approval.Domain.Tests;

public class OutboxRelayWorkerTests
{
    [Fact]
    public async Task OutboxRelayWorker_InstanceApproved_ShouldProcessAndMarkSent()
    {
        var dbName = $"ApprovalDb_WorkerTest_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ApprovalDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddScoped<ISapAdapterRegistry, SapAdapterRegistry>();
        var fakeAdapter = new FakeObjectAdapter("CHORDR");
        services.AddScoped<ISapObjectAdapter>(_ => fakeAdapter);
        var provider = services.BuildServiceProvider();

        // 预先获取真实 FakeObject 的 hash 以便防篡改校验通过
        var fakePayload = await fakeAdapter.FetchObjectAsync("DB_KCC", "1001");
        var (_, validHash) = Approval.Domain.Services.CanonicalSnapshotBuilder.Build(fakePayload.RawJson);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
            var outbox = new WorkflowOutbox
            {
                EventType = "InstanceApproved",
                AggregateId = "inst_001",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    InstanceId = "inst_001",
                    CompanyId = "DB_KCC",
                    ObjectCode = "CHORDR",
                    ObjectKey = "1001",
                    Status = "Approved",
                    DataSha256 = validHash
                }),
                Status = OutboxStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                NextRetryAt = DateTime.UtcNow.AddMinutes(-1)
            };
            db.Outboxes.Add(outbox);
            await db.SaveChangesAsync();
        }

        var worker = new OutboxRelayWorker(provider, NullLogger<OutboxRelayWorker>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await worker.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) { }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
            var item = await db.Outboxes.FirstAsync();
            item.Status.Should().Be(OutboxStatus.Sent);
            item.SentAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task OutboxRelayWorker_TamperedHash_ShouldTriggerRetryAndRecordError()
    {
        var dbName = $"ApprovalDb_WorkerTamperTest_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ApprovalDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddScoped<ISapAdapterRegistry, SapAdapterRegistry>();
        services.AddScoped<ISapObjectAdapter>(_ => new FakeObjectAdapter("CHORDR"));
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
            var outbox = new WorkflowOutbox
            {
                EventType = "InstanceApproved",
                AggregateId = "inst_tamper",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    InstanceId = "inst_tamper",
                    CompanyId = "DB_KCC",
                    ObjectCode = "CHORDR",
                    ObjectKey = "1001",
                    Status = "Approved",
                    DataSha256 = "invalid_tampered_hash_12345678901234567890123456789012"
                }),
                Status = OutboxStatus.Pending,
                RetryCount = 0,
                MaxRetries = 3,
                CreatedAt = DateTime.UtcNow,
                NextRetryAt = DateTime.UtcNow.AddMinutes(-1)
            };
            db.Outboxes.Add(outbox);
            await db.SaveChangesAsync();
        }

        var worker = new OutboxRelayWorker(provider, NullLogger<OutboxRelayWorker>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token);
            await worker.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) { }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
            var item = await db.Outboxes.FirstAsync();
            item.Status.Should().Be(OutboxStatus.Pending);
            item.RetryCount.Should().Be(1);
            item.ErrorMsg.Should().Contain("DOCUMENT_CHANGED");
        }
    }
}
