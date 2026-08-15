using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Approval.Domain.Tests;

public class SapMetadataComprehensiveTests
{
    private readonly SapMetadataService _service;

    public SapMetadataComprehensiveTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApprovalDbConnection"] = ""
        }).Build();
        _service = new SapMetadataService(config, NullLogger<SapMetadataService>.Instance);
    }

    [Theory]
    [InlineData("ORDR", "销售订单", "RDR1")]
    [InlineData("OQUT", "销售报价单", "QUT1")]
    [InlineData("ODLN", "交货单", "DLN1")]
    [InlineData("OINV", "应收发票", "INV1")]
    [InlineData("OPOR", "采购订单", "POR1")]
    [InlineData("OPDN", "收货采购单", "PDN1")]
    [InlineData("OPCH", "应付发票", "PCH1")]
    [InlineData("OWOR", "生产订单", "WOR1")]
    [InlineData("OWTR", "库存转储", "WTR1")]
    [InlineData("OJDT", "日记账分录", "JDT1")]
    [InlineData("ODRF", "单据草稿", "DRF1")]
    public async Task GetObjectMetadata_StandardDocumentsMatrix_ShouldMapCorrectly(
        string objectCode,
        string expectedDesc,
        string expectedChildTable)
    {
        var meta = await _service.GetObjectMetadataAsync("DB_KCC", objectCode);

        meta.Should().NotBeNull();
        meta.ObjectDescription.Should().Be(expectedDesc);
        meta.ChildTableDescriptions.Should().ContainKey(expectedChildTable);
    }

    [Fact]
    public async Task GetCompanyInfo_EmptyConnection_ShouldReturnSafeFallback()
    {
        var company = await _service.GetCompanyInfoAsync("DB_KCC");

        company.Should().NotBeNull();
        company.CompanyId.Should().Be("DB_KCC");
        company.CompanyName.Should().Be("DB_KCC");
    }
}
