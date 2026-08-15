using System.Text.Json.Serialization;
using Approval.Application.Common.Interfaces;
using Approval.Application.Services;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using Approval.SapAdapter;
using Approval.SapAdapter.Adapters;
using Approval.SapAdapter.ServiceLayer;
using Approval.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. 注册核心服务与控制器
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAuthentication(TrustedHeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TrustedHeaderAuthenticationHandler>(
        TrustedHeaderAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "SAP B1 10.0 通用审批平台 API",
        Version = "v1",
        Description = "世界级企业审批领域微内核 API，支持 UDO 适配、规范化快照防篡改 (SHA-256) 与 Outbox 事务可靠性"
    });
});

// 2. 数据库配置 (支持 InMemory 快速启动与 SQL Server)
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDb", false);
if (useInMemory)
{
    var inMemoryDatabaseName = $"ApprovalDb_Dev_{Guid.NewGuid():N}";
    builder.Services.AddDbContext<ApprovalDbContext>(options =>
        options.UseInMemoryDatabase(inMemoryDatabaseName));
}
else
{
    var connStr = builder.Configuration.GetConnectionString("ApprovalDbConnection")
        ?? throw new InvalidOperationException("非内存模式必须配置 ConnectionStrings:ApprovalDbConnection；禁止使用代码内置数据库密码");
    builder.Services.AddDbContext<ApprovalDbContext>(options =>
        options.UseSqlServer(connStr));
}

builder.Services.AddScoped<IApprovalDbContext>(sp => sp.GetRequiredService<ApprovalDbContext>());
builder.Services.AddScoped<ITraceContext, TraceContext>();
builder.Services.AddScoped<IWorkflowEngine, WorkflowEngine>();

// 3. 注册 SAP 适配器与路由中心。Fake 仅用于开发/测试，生产配置会拒绝启动，避免把模拟回写当成真实成功。
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

// 4. CORS 白名单。可信身份头模式绝不能同时开放 AllowAnyOrigin。
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
    allowedOrigins = new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("ApprovalWeb", policy =>
    {
        if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    });
});

var app = builder.Build();

// 5. 自动执行数据库种子数据初始化
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
    if (useInMemory)
    {
        await db.Database.EnsureCreatedAsync();
    }
    await DbInitializer.SeedAsync(db);
}

// 6. 全链路 TraceID 与异常中间件
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var traceId = context.Request.Headers["X-Trace-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Trace-Id"] = traceId;
    if (context.RequestServices.GetService<ITraceContext>() is TraceContext tc)
    {
        tc.TraceId = traceId;
        tc.ClientIp = context.Connection.RemoteIpAddress?.ToString();
        tc.CurrentUserCode = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
    await next();
});

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SAP B1 通用审批平台 API v1");
    c.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("ApprovalWeb");
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

// 使得集成测试可引用 Program
public partial class Program { }
