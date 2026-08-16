using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Approval.Domain.Services;

namespace Approval.Worker;

/// <summary>
/// 发件箱消息中继守护进程 (Outbox Pattern Relay)
/// </summary>
public class OutboxRelayWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxRelayWorker> _logger;

    public OutboxRelayWorker(IServiceProvider serviceProvider, ILogger<OutboxRelayWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox 消息中继 Worker 已启动，持续监听发件箱事件...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理 Outbox 队列时发生未捕获异常");
            }

            await Task.Delay(3000, stoppingToken);
        }
    }

    private async Task ProcessPendingOutboxMessagesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<ISapAdapterRegistry>();

        var now = DateTime.UtcNow;
        var staleBefore = now.AddMinutes(-5);
        var pendingList = await db.Outboxes
            .Where(o =>
                (o.Status == OutboxStatus.Pending && o.NextRetryAt <= now) ||
                (o.Status == OutboxStatus.Processing && (o.ProcessingAt == null || o.ProcessingAt < staleBefore)))
            .OrderBy(o => o.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        foreach (var msg in pendingList)
        {
            _logger.LogInformation("[TraceID: {TraceId}] 正在投递 Outbox 事件: {EventType} (聚合ID: {AggregateId})",
                msg.TraceId, msg.EventType, msg.AggregateId);

            msg.Status = OutboxStatus.Processing;
            msg.ProcessingAt = DateTime.UtcNow;
            msg.LockId = Guid.NewGuid().ToString("N");
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.Entry(msg).State = EntityState.Detached;
                continue; // 已被另一个 Worker 抢占
            }

            try
            {
                if (msg.EventType is "InstanceApproved" or "InstanceRejected" or "InstanceReturned" or "WorkflowStarted")
                {
                    using var doc = JsonDocument.Parse(msg.PayloadJson);
                    var root = doc.RootElement;
                    var companyId = root.GetProperty("CompanyId").GetString() ?? "DB_KCC";
                    var objectCode = root.GetProperty("ObjectCode").GetString() ?? "CHORDR";
                    var objectKey = root.GetProperty("ObjectKey").GetString() ?? "";
                    var status = root.GetProperty("Status").GetString() ?? "Pending";
                    var instanceId = root.GetProperty("InstanceId").GetString() ?? "";
                    var dataHash = root.GetProperty("DataSha256").GetString() ?? "";

                    var adapter = registry.GetAdapter(objectCode);
                    if (msg.EventType == "InstanceApproved")
                    {
                        var currentDocument = await adapter.FetchObjectAsync(companyId, objectKey, ct);
                        var (_, currentHash) = CanonicalSnapshotBuilder.Build(currentDocument.RawJson);
                        if (!currentHash.Equals(dataHash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("DOCUMENT_CHANGED：SAP 单据已与审批快照不一致，禁止回写 Approved");

                        // 1. 若为草稿单据 (Drafts / 营销/库存/收付款草稿)：自动调用 Service Layer 原生过账
                        if (string.Equals(objectCode, "Drafts", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(objectCode, "112", StringComparison.OrdinalIgnoreCase))
                        {
                            var slClient = scope.ServiceProvider.GetService<Approval.SapAdapter.ServiceLayer.ServiceLayerClient>();
                            if (slClient != null && slClient.MirrorEnabled)
                            {
                                var (postedEntry, postedNum) = await slClient.SaveDraftToDocumentAsync(objectKey, ct);
                                _logger.LogInformation("[TraceID: {TraceId}] 草稿单据 #{DraftKey} 审批通过，已自动过账转为正式单据 (DocEntry: {PostedEntry}, DocNum: {PostedNum})",
                                    msg.TraceId, objectKey, postedEntry, postedNum);

                                var inst = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
                                if (inst != null)
                                    inst.SetPostedDocument(postedEntry, postedNum);
                            }
                        }
                        // 2. 若为日记账凭证批 (Journal Vouchers)：自动调用 Service Layer 记账过账
                        else if (string.Equals(objectCode, "JournalVouchers", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(objectCode, "OBTD", StringComparison.OrdinalIgnoreCase))
                        {
                            var slClient = scope.ServiceProvider.GetService<Approval.SapAdapter.ServiceLayer.ServiceLayerClient>();
                            if (slClient != null && slClient.MirrorEnabled && int.TryParse(objectKey, out var vNum))
                            {
                                await slClient.PostJournalVoucherAsync(vNum, ct);
                                _logger.LogInformation("[TraceID: {TraceId}] 日记账凭证批 #{VoucherNum} 审批通过，已成功过账转为正式日记账分录 (OJDT)",
                                    msg.TraceId, vNum);
                            }
                        }
                    }

                    var writeSucceeded = await adapter.WriteApprovalMirrorAsync(companyId, objectKey, status, instanceId, dataHash, ct);
                    if (!writeSucceeded) throw new InvalidOperationException("SAP Adapter 返回回写失败");

                    var sync = await db.SapSyncStates.FirstOrDefaultAsync(s =>
                        s.CompanyId == companyId && s.ObjectCode == objectCode && s.ObjectKey == objectKey, ct);
                    if (sync == null)
                    {
                        sync = new Approval.Domain.Entities.SapSyncState
                        {
                            CompanyId = companyId,
                            ObjectCode = objectCode,
                            ObjectKey = objectKey
                        };
                        await db.SapSyncStates.AddAsync(sync, ct);
                    }
                    sync.InstanceId = instanceId;
                    sync.ExpectedStatus = status;
                    sync.LastSyncedStatus = status;
                    sync.SyncStatus = "Synced";
                    sync.LastSyncAttempt = DateTime.UtcNow;
                    sync.ErrorMessage = null;
                }

                msg.Status = OutboxStatus.Sent;
                msg.SentAt = DateTime.UtcNow;
                msg.ProcessingAt = null;
                msg.LockId = null;
                msg.ErrorMsg = null;
                _logger.LogInformation("[TraceID: {TraceId}] Outbox 事件处理成功: {EventType}", msg.TraceId, msg.EventType);
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.ErrorMsg = ex.Message;
                msg.ProcessingAt = null;
                msg.LockId = null;
                if (msg.RetryCount >= msg.MaxRetries)
                {
                    msg.Status = OutboxStatus.Failed;
                    _logger.LogCritical(ex, "[TraceID: {TraceId}] Outbox 事件重试已达上限，移入死信: {EventType}", msg.TraceId, msg.EventType);
                }
                else
                {
                    msg.Status = OutboxStatus.Pending;
                    msg.NextRetryAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, msg.RetryCount)); // 指数退避
                    _logger.LogWarning(ex, "[TraceID: {TraceId}] Outbox 事件投递失败，将在 {NextRetry} 重试 (第 {Retry} 次)",
                        msg.TraceId, msg.NextRetryAt, msg.RetryCount);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
