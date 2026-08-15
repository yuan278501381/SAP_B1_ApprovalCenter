using Approval.Domain.Services;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class CanonicalSnapshotTests
{
    [Fact]
    public void Build_ShouldGenerateSameSha256_RegardlessOfJsonKeyOrdering()
    {
        // 乱序 JSON 1
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

        // 键顺序完全不同的 JSON 2 (数据语义完全相同)
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
}
