using System.Text.Json;
using Approval.Application.Common.Models;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Approval.Domain.Tests;

public class SapMetadataServiceDeepTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(AppContext.BaseDirectory, "metadata_cache");

    public SapMetadataServiceDeepTests()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, true);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task SapMetadataService_SaveAndLoadDiskCache_ShouldRestoreCorrectly()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApprovalDbConnection"] = ""
        }).Build();
        var service = new SapMetadataService(config, NullLogger<SapMetadataService>.Instance);

        // 1. 刷新并落盘
        await service.RefreshAllMetadataAndSaveToDiskAsync("DB_KCC");

        // 2. 再次读取应当命中磁盘/内存快照
        var meta1 = await service.GetObjectMetadataAsync("DB_KCC", "CHORDR");
        meta1.Should().NotBeNull();
        meta1.ObjectCode.Should().Be("CHORDR");

        // 3. 强制刷新
        var meta2 = await service.GetObjectMetadataAsync("DB_KCC", "CHOQUT", forceRefresh: true);
        meta2.Should().NotBeNull();
        meta2.ObjectCode.Should().Be("CHOQUT");
    }

    [Fact]
    public async Task SapMetadataService_LiveDb_ShouldLoadExpenseCodeValidValues()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApprovalDbConnection"] = "Server=192.168.134.9,1433;Database=DB_KCC;User Id=sa;Password=123456@a;TrustServerCertificate=True;"
        }).Build();
        var service = new SapMetadataService(config, NullLogger<SapMetadataService>.Instance);

        var meta = await service.GetObjectMetadataAsync("DB_KCC", "CHORDR", forceRefresh: true);
        meta.Should().NotBeNull();

        var ch3 = meta.ChildTableFields["@CH_ORDR_3"];
        var expMeta = ch3["U_ExpenseCode"];
        expMeta.ValidValues.Should().NotBeNull();
        expMeta.ValidValues.Should().ContainKey("8");
        expMeta.ValidValues!["8"].Should().Be("染色費");
    }
}
