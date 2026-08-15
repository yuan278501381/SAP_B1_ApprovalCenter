using Approval.SapAdapter.Adapters;
using FluentAssertions;
using Xunit;

namespace Approval.Domain.Tests;

public class FakeObjectAdapterTests
{
    [Theory]
    [InlineData("CHORDR", "1001")]
    [InlineData("CHOQUT", "2001")]
    [InlineData("ORDR", "3001")]
    public async Task FakeObjectAdapter_FetchObject_ShouldReturnValidPayload(string objectCode, string objectKey)
    {
        var adapter = new FakeObjectAdapter(objectCode);

        var payload = await adapter.FetchObjectAsync("DB_KCC", objectKey);

        payload.Should().NotBeNull();
        payload.ObjectCode.Should().Be(objectCode);
        payload.ObjectKey.Should().Be(objectKey);
        payload.CompanyId.Should().Be("DB_KCC");
        payload.DocTotal.Should().BeGreaterThan(0);
        payload.RawJson.Should().NotBeNullOrWhiteSpace();
        payload.HeaderFields.Should().NotBeEmpty();
        payload.LineRows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FakeObjectAdapter_WriteApprovalMirror_ShouldReturnTrue()
    {
        var adapter = new FakeObjectAdapter("CHORDR");

        var syncResult = await adapter.WriteApprovalMirrorAsync("DB_KCC", "1001", "Approved", "INST_001", "mock_sha256");
        syncResult.Should().BeTrue();
    }
}
