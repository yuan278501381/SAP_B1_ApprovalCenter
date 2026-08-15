using Approval.Domain.Entities;
using Approval.Domain.Enums;
using Approval.Infrastructure.Persistence;
using Approval.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Approval.Domain.Tests;

public class UserDirectoryServiceAdvancedTests : IDisposable
{
    private readonly ApprovalDbContext _db;
    private readonly UserDirectoryService _service;

    public UserDirectoryServiceAdvancedTests()
    {
        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseInMemoryDatabase($"ApprovalDb_UserDirAdv_{Guid.NewGuid():N}")
            .Options;
        _db = new ApprovalDbContext(options);
        _service = new UserDirectoryService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ResolveCandidates_Role_ShouldFilterUsersWithRole()
    {
        _db.UserMappings.AddRange(
            new SysUserMapping { SapUserCode = "FIN01", DisplayName = "财务1", Roles = "FinanceMgr,Auditor", IsActive = true },
            new SysUserMapping { SapUserCode = "FIN02", DisplayName = "财务2", Roles = "Auditor", IsActive = true },
            new SysUserMapping { SapUserCode = "SALES01", DisplayName = "销售", Roles = "SalesStaff", IsActive = true },
            new SysUserMapping { SapUserCode = "FIN_INACTIVE", DisplayName = "禁用财务", Roles = "FinanceMgr", IsActive = false }
        );
        await _db.SaveChangesAsync();

        var resolved = await _service.ResolveCandidatesAsync(CandidateType.Role, new[] { "FinanceMgr" }, "SUBMITTER01");

        resolved.Should().ContainSingle(u => u == "FIN01");
    }

    [Fact]
    public async Task ResolveCandidates_Manager_ShouldFindDirectManager_OrFallback()
    {
        _db.UserMappings.AddRange(
            new SysUserMapping { SapUserCode = "STAFF01", ManagerCode = "MGR_LEADER", IsActive = true },
            new SysUserMapping { SapUserCode = "STAFF_NO_MGR", ManagerCode = null, IsActive = true }
        );
        await _db.SaveChangesAsync();

        // 1. 存在主管
        var res1 = await _service.ResolveCandidatesAsync(CandidateType.Manager, Array.Empty<string>(), "STAFF01");
        res1.Should().ContainSingle(u => u == "MGR_LEADER");

        // 2. 无主管 -> 回退到传入默认值
        var res2 = await _service.ResolveCandidatesAsync(CandidateType.Manager, new[] { "DEFAULT_MGR" }, "STAFF_NO_MGR");
        res2.Should().ContainSingle(u => u == "DEFAULT_MGR");

        // 3. 查无映射 -> 兜底 manager
        var res3 = await _service.ResolveCandidatesAsync(CandidateType.Manager, Array.Empty<string>(), "UNKNOWN_USER");
        res3.Should().ContainSingle(u => u == "manager");
    }

    [Fact]
    public async Task ResolveCandidates_Delegate_ShouldSubstituteUser_WhenWithinActiveWindow()
    {
        _db.UserMappings.AddRange(
            // 生效中
            new SysUserMapping
            {
                SapUserCode = "BOSS_VACATION",
                DelegateUserCode = "BOSS_DELEGATE",
                DelegateStartTime = DateTime.UtcNow.AddDays(-1),
                DelegateEndTime = DateTime.UtcNow.AddDays(5),
                IsActive = true
            },
            // 已过期
            new SysUserMapping
            {
                SapUserCode = "LEADER_EXPIRED",
                DelegateUserCode = "EXPIRED_DELEGATE",
                DelegateStartTime = DateTime.UtcNow.AddDays(-10),
                DelegateEndTime = DateTime.UtcNow.AddDays(-1),
                IsActive = true
            }
        );
        await _db.SaveChangesAsync();

        // 1. 代理生效中 -> 路由给 BOSS_DELEGATE
        var resActive = await _service.ResolveCandidatesAsync(CandidateType.Delegate, new[] { "BOSS_VACATION" }, "SUBMITTER01");
        resActive.Should().ContainSingle(u => u == "BOSS_DELEGATE");

        // 2. 代理已过期 -> 保持原用户 LEADER_EXPIRED
        var resExpired = await _service.ResolveCandidatesAsync(CandidateType.Delegate, new[] { "LEADER_EXPIRED" }, "SUBMITTER01");
        resExpired.Should().ContainSingle(u => u == "LEADER_EXPIRED");
    }
}
