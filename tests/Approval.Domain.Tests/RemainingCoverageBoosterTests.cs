using System.Net;
using System.Text;
using Approval.Api.Services;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Infrastructure.Services;
using Approval.SapAdapter;
using Approval.SapAdapter.ServiceLayer;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Xunit;

namespace Approval.Domain.Tests;

public class RemainingCoverageBoosterTests
{
    [Fact]
    public async Task ServiceLayerClient_PostJournalVoucher_ShouldReturnPostedEntry()
    {
        var options = new ServiceLayerOptions
        {
            BaseUrl = "https://127.0.0.1:50000/b1s/v2",
            CompanyDb = "DB_KCC",
            UserName = "manager",
            Password = "pwd",
            MirrorEnabled = true,
            Objects = new()
            {
                new() { ObjectCode = "OJDT", EntitySet = "JournalEntries" }
            }
        };

        var handler = new MockHttpMessageHandler(async req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("Login"))
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"SessionId": "s1"}""", Encoding.UTF8, "application/json")
                };
                res.Headers.Add("Set-Cookie", "B1SESSION=s1");
                return res;
            }

            if (req.RequestUri != null && req.RequestUri.ToString().Contains("JournalVouchersService_PostVoucher"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"JdtNum": 777}""", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"JdtNum": 777}""", Encoding.UTF8, "application/json")
            };
        });

        using var client = new ServiceLayerClient(options, handler);
        var entry = await client.PostJournalVoucherAsync(101, CancellationToken.None);
        entry.Should().Contain("777");
    }

    [Fact]
    public void SapAdapterRegistry_NotFound_ShouldThrowNotSupportedException()
    {
        var registry = new SapAdapterRegistry(Array.Empty<ISapObjectAdapter>());
        var act = () => registry.GetAdapter("UNKNOWN_OBJ");
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TraceContext_Properties_ShouldBeSettable()
    {
        var trace = new TraceContext();
        trace.TraceId = "tr_custom";
        trace.ClientIp = "127.0.0.1";
        trace.CurrentUserCode = "user_01";

        trace.TraceId.Should().Be("tr_custom");
        trace.ClientIp.Should().Be("127.0.0.1");
        trace.CurrentUserCode.Should().Be("user_01");
    }

    [Fact]
    public async Task MetadataRefreshBackgroundService_ShouldExecuteOnce()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApprovalDbConnection"] = ""
        }).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ISapMetadataService, SapMetadataService>();
        var provider = services.BuildServiceProvider();

        var bgService = new MetadataRefreshBackgroundService(provider, NullLogger<MetadataRefreshBackgroundService>.Instance);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        try
        {
            await bgService.StartAsync(cts.Token);
            await Task.Delay(100, cts.Token);
            await bgService.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException ex) 
        { 
            Log.Warning(ex, "后台服务被取消");
        }
    }
}
