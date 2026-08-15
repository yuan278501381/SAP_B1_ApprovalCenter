using Approval.Application.Common.Interfaces;
using Approval.Infrastructure.Persistence;
using Approval.SapAdapter;
using Approval.SapAdapter.Adapters;
using Approval.SapAdapter.ServiceLayer;
using Approval.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using Serilog.Events;

// ==========================================================================
// 1. 世界级 Serilog 全链路结构化日志引擎初始化 (30天自动滚动保留 + TraceId 贯穿)
// ==========================================================================
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{TraceId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logDirectory, "approval-worker-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30, // 自动保留 30 天，超过 30 天由底层写入器自动安全物理清除
        fileSizeLimitBytes: 100 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [TraceId: {TraceId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("启动 SAP B1 10.0 通用审批平台 Worker 守护进程...");

    var builderSettings = new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
    };
    var builder = new HostApplicationBuilder(builderSettings);
    builder.Services.AddWindowsService();
    builder.Services.AddSerilog();

    // Worker 必须与 API 使用同一个持久化 ApprovalDB。
    if (builder.Configuration.GetValue<bool>("UseInMemoryDb", false))
        throw new InvalidOperationException("Approval.Worker 不支持 InMemory：请配置共享 SQL Server ApprovalDB");
    var connStr = builder.Configuration.GetConnectionString("ApprovalDbConnection")
        ?? throw new InvalidOperationException("必须配置 ConnectionStrings:ApprovalDbConnection；禁止使用代码内置数据库密码");
    builder.Services.AddDbContext<ApprovalDbContext>(options => options.UseSqlServer(connStr));

    var sapAdapterMode = builder.Configuration["SapAdapter:Mode"] ?? "NotConfigured";
    if (sapAdapterMode.Equals("Fake", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<ISapObjectAdapter>(new FakeObjectAdapter("CHORDR"));
        builder.Services.AddSingleton<ISapObjectAdapter>(new FakeObjectAdapter("CHOQUT"));
    }
    else if (sapAdapterMode.Equals("ServiceLayer", StringComparison.OrdinalIgnoreCase))
    {
        var options = builder.Configuration.GetSection("SapAdapter:ServiceLayer").Get<ServiceLayerOptions>()
            ?? throw new InvalidOperationException("SapAdapter:ServiceLayer 配置缺失");
        var client = new ServiceLayerClient(options);
        builder.Services.AddSingleton(client);
        foreach (var mapping in options.Objects)
            builder.Services.AddSingleton<ISapObjectAdapter>(new ServiceLayerObjectAdapter(client, mapping));
    }
    else
    {
        throw new InvalidOperationException("SapAdapter:Mode 必须显式配置为 Fake 或 ServiceLayer");
    }
    builder.Services.AddScoped<ISapAdapterRegistry, SapAdapterRegistry>();

    // 注册后台中继服务
    builder.Services.AddHostedService<OutboxRelayWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker 守护进程发生未处理异常崩溃");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
