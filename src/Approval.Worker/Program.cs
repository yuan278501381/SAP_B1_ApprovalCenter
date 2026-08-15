using Approval.Application.Common.Interfaces;
using Approval.Infrastructure.Persistence;
using Approval.SapAdapter;
using Approval.SapAdapter.Adapters;
using Approval.SapAdapter.ServiceLayer;
using Approval.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;

var builderSettings = new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
};
var builder = new HostApplicationBuilder(builderSettings);
builder.Services.AddWindowsService();

// Worker 必须与 API 使用同一个持久化 ApprovalDB。独立进程内存库无法共享 Outbox，因此明确禁止。
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
