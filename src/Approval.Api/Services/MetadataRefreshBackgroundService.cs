using Approval.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Approval.Api.Services;

/// <summary>
/// SAP 元数据与动态系统字典 (OEXD/OSLP/OCTG/OHEM/CUFD/UFD1) 10分钟后台自动刷新与持久化落盘守护服务
/// </summary>
public class MetadataRefreshBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MetadataRefreshBackgroundService> _logger;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(10);

    public MetadataRefreshBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<MetadataRefreshBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 [元数据定时守护服务] 已启动，调度周期: 每 {Minutes} 分钟自动同步 SAP 业务库字典并持久化落盘",
            _refreshInterval.TotalMinutes);

        // 1. 服务启动时，先延迟 3 秒等主服务完全就绪后，立即执行首次预热刷新
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            await DoRefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "首次元数据预热刷新发生异常，服务将继续等待下一个周期: {Message}", ex.Message);
        }

        // 2. 启动 .NET 8 高性能异步 PeriodicTimer 循环调度
        using var timer = new PeriodicTimer(_refreshInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DoRefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时刷新 SAP 动态元数据任务执行失败: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("🛑 [元数据定时守护服务] 已安全退出");
    }

    private async Task DoRefreshAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var metadataService = scope.ServiceProvider.GetRequiredService<ISapMetadataService>();

        const string defaultCompany = "DB_KCC";
        _logger.LogInformation("🔄 触发周期性 SAP 元数据全量同步 (CUFD/UFD1/OEXD/OSLP/OCTG/OHEM) -> 账套 [{CompanyId}]", defaultCompany);

        await metadataService.RefreshAllMetadataAndSaveToDiskAsync(defaultCompany, ct);
    }
}
