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
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using Serilog.Context;
using Serilog.Events;

// ==========================================================================
// 1. 世界级 Serilog 全链路结构化日志引擎初始化 (30天自动滚动保留 + TraceId 贯穿)
// ==========================================================================
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{TraceId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logDirectory, "approval-api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30, // 自动保留 30 天，超过 30 天由底层写入器自动安全物理清除
        fileSizeLimitBytes: 100 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [TraceId: {TraceId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("启动 SAP B1 10.0 通用审批平台 API 宿主...");

    var webAppOptions = new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
    };
    var builder = WebApplication.CreateBuilder(webAppOptions);
    builder.Host.UseWindowsService();
    builder.Host.UseSerilog();

    // 2. 注册核心服务与控制器
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

    // 3. 数据库配置 (支持 InMemory 快速启动与 SQL Server)
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
    builder.Services.AddScoped<IUserDirectoryService, UserDirectoryService>();
    builder.Services.AddScoped<IWorkflowRuleMatcher, WorkflowRuleMatcher>();
    builder.Services.AddScoped<IWorkflowEngine, WorkflowEngine>();
    builder.Services.AddScoped<ISapMetadataService, SapMetadataService>();
    builder.Services.AddHostedService<Approval.Api.Services.MetadataRefreshBackgroundService>();

    // 4. 注册 SAP 适配器与路由中心。
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

    // 5. CORS 策略
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("ApprovalWeb", policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin)) return false;
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                    return uri.Host is "localhost" or "127.0.0.1" || uri.Host.StartsWith("192.168.") || uri.Host.StartsWith("10.");
                })
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
            }
            else
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
                if (allowedOrigins.Length > 0)
                    policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            }
        });
    });

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<ApprovalDbContext>("ApprovalDB");

    var app = builder.Build();

    // 6. 自动执行数据库种子数据初始化
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
        if (useInMemory)
        {
            await db.Database.EnsureCreatedAsync();
        }
        await DbInitializer.SeedAsync(db);
    }

    // 7. 全链路 TraceID 贯穿与请求上下文中间件
    app.UseAuthentication();
    app.Use(async (context, next) =>
    {
        var traceId = context.Request.Headers["X-Trace-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(traceId))
        {
            traceId = "tr_" + Guid.NewGuid().ToString("N")[..12];
        }

        context.Response.Headers["X-Trace-Id"] = traceId;
        if (context.RequestServices.GetService<ITraceContext>() is TraceContext tc)
        {
            tc.TraceId = traceId;
            tc.ClientIp = context.Connection.RemoteIpAddress?.ToString();
            tc.CurrentUserCode = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        using (LogContext.PushProperty("TraceId", traceId))
        {
            await next();
        }
    });

    // Configure the HTTP request pipeline.
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SAP B1 通用审批平台 API v1");
        c.RoutePrefix = "swagger";
    });

    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                ctx.Context.Response.Headers.Append("Pragma", "no-cache");
                ctx.Context.Response.Headers.Append("Expires", "0");
            }
            else
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
            }
        }
    });
    app.UseCors("ApprovalWeb");
    app.UseAuthorization();
    app.MapHealthChecks("/health");
    app.MapControllers();
    app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API 宿主在运行时发生未处理异常崩溃");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// 使得集成测试可引用 Program
public partial class Program { }
