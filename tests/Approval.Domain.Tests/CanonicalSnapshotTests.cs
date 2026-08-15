using System.Text.Json;
using Approval.Domain.Services;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class CanonicalSnapshotTests
{
    [Fact]
    public void Build_ShouldGenerateSameSha256_RegardlessOfJsonKeyOrdering()
    {
        var json1 = """
        {
            "DocTotal": 85600.0,
            "CardCode": "C20000",
            "Lines": [
                {"Price": 350.0, "ItemCode": "A0001", "Quantity": 100},
                {"Quantity": 50, "ItemCode": "A0002", "Price": 1012.0}
            ],
            "Comments": "采购测试"
        }
        """;

        var json2 = """
        {
            "Comments": "采购测试",
            "CardCode": "C20000",
            "DocTotal": 85600.0,
            "Lines": [
                {"ItemCode": "A0001", "Quantity": 100, "Price": 350.0},
                {"ItemCode": "A0002", "Price": 1012.0, "Quantity": 50}
            ]
        }
        """;

        var (canonical1, sha1) = CanonicalSnapshotBuilder.Build(json1);
        var (canonical2, sha2) = CanonicalSnapshotBuilder.Build(json2);

        canonical1.Should().Be(canonical2);
        sha1.Should().Be(sha2);
        sha1.Should().HaveLength(64);
    }

    [Fact]
    public void Build_ShouldDetectTampering_WhenAmountIsModified()
    {
        var originalJson = """{"DocTotal": 85600.0, "CardCode": "C20000"}""";
        var tamperedJson = """{"DocTotal": 99999.0, "CardCode": "C20000"}""";

        var (_, originalSha) = CanonicalSnapshotBuilder.Build(originalJson);
        var (_, tamperedSha) = CanonicalSnapshotBuilder.Build(tamperedJson);

        originalSha.Should().NotBe(tamperedSha);
    }

    [Fact]
    public void Build_ShouldDetectTampering_WhenChildTableLineIsAddedOrRemoved()
    {
        var baseJson = """
        {
            "DocNum": 1001,
            "DocumentLines": [
                {"LineNum": 0, "ItemCode": "ITEM-01", "Price": 100.0, "Quantity": 10}
            ]
        }
        """;

        var lineAddedJson = """
        {
            "DocNum": 1001,
            "DocumentLines": [
                {"LineNum": 0, "ItemCode": "ITEM-01", "Price": 100.0, "Quantity": 10},
                {"LineNum": 1, "ItemCode": "ITEM-02", "Price": 200.0, "Quantity": 5}
            ]
        }
        """;

        var (_, baseSha) = CanonicalSnapshotBuilder.Build(baseJson);
        var (_, lineAddedSha) = CanonicalSnapshotBuilder.Build(lineAddedJson);

        baseSha.Should().NotBe(lineAddedSha);
    }

    [Fact]
    public void Build_ShouldDetectTampering_WhenChildTableFieldIsModified()
    {
        var originalJson = """
        {
            "DocNum": 1001,
            "DocumentLines": [
                {"LineNum": 0, "ItemCode": "ITEM-01", "Price": 100.0, "Quantity": 10}
            ]
        }
        """;

        var tamperedQuantityJson = """
        {
            "DocNum": 1001,
            "DocumentLines": [
                {"LineNum": 0, "ItemCode": "ITEM-01", "Price": 100.0, "Quantity": 11}
            ]
        }
        """;

        var (_, originalSha) = CanonicalSnapshotBuilder.Build(originalJson);
        var (_, tamperedSha) = CanonicalSnapshotBuilder.Build(tamperedQuantityJson);

        originalSha.Should().NotBe(tamperedSha);
    }

    [Theory]
    [InlineData("true", "false")]
    [InlineData("null", "{}")]
    [InlineData("[]", "[1]")]
    public void Build_ShouldDistinguishPrimitiveAndComplexTypes(string val1, string val2)
    {
        var json1 = $$"""{"test": {{val1}}}""";
        var json2 = $$"""{"test": {{val2}}}""";

        var (_, sha1) = CanonicalSnapshotBuilder.Build(json1);
        var (_, sha2) = CanonicalSnapshotBuilder.Build(json2);

        sha1.Should().NotBe(sha2);
    }

    [Fact]
    public void Build_ShouldHandleEmptyAndWhitespaceJson_Gracefully()
    {
        var (canonical, sha) = CanonicalSnapshotBuilder.Build("{}");
        canonical.Should().Be("{}");
        sha.Should().NotBeNullOrWhiteSpace();
        sha.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Build_ShouldThrowArgumentException_WhenRawJsonIsNullOrWhitespace(string? invalidJson)
    {
        var act = () => CanonicalSnapshotBuilder.Build(invalidJson!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_ShouldThrowJsonException_WhenJsonIsMalformed()
    {
        var act = () => CanonicalSnapshotBuilder.Build("{invalid_json: 123");
        act.Should().Throw<JsonException>();
    }
}
