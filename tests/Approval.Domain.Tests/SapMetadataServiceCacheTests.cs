using System.Collections.Concurrent;
using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Approval.Domain.Tests;

public class SapMetadataServiceCacheTests : IDisposable
{
    private readonly string _testCacheDir;
    private readonly IConfiguration _config;
    private readonly SapMetadataService _service;

    public SapMetadataServiceCacheTests()
    {
        _testCacheDir = Path.Combine(AppContext.BaseDirectory, "metadata_cache");
        Directory.CreateDirectory(_testCacheDir);

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApprovalDbConnection"] = "" // 离线模式
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        _service = new SapMetadataService(_config, NullLogger<SapMetadataService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            var testFiles = Directory.GetFiles(_testCacheDir, "TEST_*.json");
            foreach (var f in testFiles)
            {
                File.Delete(f);
            }
        }
        catch { }
    }

    [Fact]
    public async Task GetObjectMetadata_ShouldReadFromDiskFile_WhenDiskSnapshotExists()
    {
        const string companyId = "TEST_COMPANY";
        const string objectCode = "CHORDR";
        var diskFilePath = Path.Combine(_testCacheDir, $"{companyId}_{objectCode}.json");

        // 构造磁盘持久化快照
        var headerFields = new Dictionary<string, FieldMetaInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["DocTotal"] = new("DocTotal", "总金额", "B", null),
            ["SlpCode"] = new("SlpCode", "销售员", "N", new() { ["1"] = "张三 (1)" })
        };
        var childFields = new Dictionary<string, Dictionary<string, FieldMetaInfo>>(StringComparer.OrdinalIgnoreCase)
        {
            ["@CH_ORDR_3"] = new()
            {
                ["ExpenseCode"] = new("ExpenseCode", "费用代码", "N", new() { ["8"] = "染色費" })
            }
        };
        var mockResult = new ObjectMetadataResult(objectCode, "@CH_ORDR", headerFields, childFields, new(), "型号订单");

        await File.WriteAllTextAsync(diskFilePath, JsonSerializer.Serialize(mockResult, new JsonSerializerOptions { WriteIndented = true }));

        // 第一次读取：应当直接从磁盘快照反序列化加载
        var result = await _service.GetObjectMetadataAsync(companyId, objectCode, forceRefresh: false);

        result.Should().NotBeNull();
        result.ObjectCode.Should().Be(objectCode);
        result.HeaderFields.Should().ContainKey("DocTotal");
        result.ChildTableFields.Should().ContainKey("@CH_ORDR_3");
        result.ChildTableFields["@CH_ORDR_3"]["ExpenseCode"].ValidValues.Should().ContainKey("8");
        result.ChildTableFields["@CH_ORDR_3"]["ExpenseCode"].ValidValues!["8"].Should().Be("染色費");
    }

    [Fact]
    public async Task GetObjectMetadata_ShouldReturnStandardSapDictionary_ForStandardDocuments()
    {
        var metaOrdr = await _service.GetObjectMetadataAsync("DB_KCC", "ORDR");
        metaOrdr.Should().NotBeNull();
        metaOrdr.TableName.Should().Be("ORDR");
        metaOrdr.ObjectDescription.Should().Be("销售订单");
        metaOrdr.ChildTableDescriptions.Should().ContainKey("RDR1");
        metaOrdr.ChildTableDescriptions!["RDR1"].Should().Be("内容");

        var metaOqut = await _service.GetObjectMetadataAsync("DB_KCC", "OQUT");
        metaOqut.TableName.Should().Be("OQUT");
        metaOqut.ObjectDescription.Should().Be("销售报价单");
    }
}
