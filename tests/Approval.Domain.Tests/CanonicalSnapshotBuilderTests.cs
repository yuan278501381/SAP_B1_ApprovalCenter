using System.Text.Json;
using Approval.Domain.Services;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class CanonicalSnapshotBuilderTests
{
    [Fact]
    public void Build_ModifyNonSensitiveField_ShouldNotChangeHash()
    {
        var originalJson = """
        {
            "DocEntry": 100,
            "Price": 50.5,
            "Comments": "First remark"
        }
        """;

        var modifiedJson = """
        {
            "DocEntry": 100,
            "Price": 50.5,
            "Comments": "Second remark, changed"
        }
        """;

        var (canonical1, hash1) = CanonicalSnapshotBuilder.Build(originalJson);
        var (canonical2, hash2) = CanonicalSnapshotBuilder.Build(modifiedJson);

        hash1.Should().Be(hash2);
        canonical1.Should().Be(canonical2);
    }

    [Fact]
    public void Build_ModifySensitiveField_ShouldChangeHash()
    {
        var originalJson = """
        {
            "DocEntry": 100,
            "Price": 50.5,
            "Comments": "Same remark"
        }
        """;

        var modifiedJson = """
        {
            "DocEntry": 100,
            "Price": 55.0,
            "Comments": "Same remark"
        }
        """;

        var (_, hash1) = CanonicalSnapshotBuilder.Build(originalJson);
        var (_, hash2) = CanonicalSnapshotBuilder.Build(modifiedJson);

        hash1.Should().NotBe(hash2);
    }
}
