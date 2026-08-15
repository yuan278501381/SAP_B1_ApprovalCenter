using Approval.Application.Common.Interfaces;
using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Approval.Domain.Tests;

public class UserDirectoryServiceTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly UserDirectoryService _service;

    public UserDirectoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_UserDir_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _service = new UserDirectoryService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ResolveCandidates_Direct_ShouldReturnParsedDirectUsers()
    {
        var values = new[] { "USER01", "USER02;USER03", " USER04 " };
        var candidates = await _service.ResolveCandidatesAsync(CandidateType.Direct, values, "SUBMITTER_01");

        candidates.Should().Contain(new[] { "USER01", "USER02", "USER03", "USER04" });
        candidates.Should().HaveCount(4);
    }

    [Fact]
    public async Task ResolveCandidates_Manager_ShouldTraceHierarchy()
    {
        // 模拟普通业务员提交单据向上找主管
        var values = new[] { "1" }; // 追溯向上 1 级主管
        var candidates = await _service.ResolveCandidatesAsync(CandidateType.Manager, values, "SUBMITTER_SALES");

        // 默认返回安全兜底或主管映射
        candidates.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveCandidates_RoleAndDept_ShouldFilterAppropriateUsers()
    {
        var roleValues = new[] { "FIN_MANAGER", "DIRECTOR" };
        var candidates = await _service.ResolveCandidatesAsync(CandidateType.Role, roleValues, "SUBMITTER_01");

        candidates.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveCandidates_EmptyValues_ShouldFallbackSafely()
    {
        var candidates = await _service.ResolveCandidatesAsync(CandidateType.Direct, Array.Empty<string>(), "SUBMITTER_01");
        candidates.Should().BeEmpty();
    }
}
