using Approval.Application.Common.Interfaces;
using Approval.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Approval.Infrastructure.Services;

/// <summary>
/// 企业用户目录与组织架构动态解析服务
/// 支持直接指定 (Direct)、直属主管向上追溯 (Manager)、岗位角色 (Role) 与委托代理 (Delegate)
/// </summary>
public class UserDirectoryService : IUserDirectoryService
{
    private readonly IApprovalDbContext _db;

    public UserDirectoryService(IApprovalDbContext db)
    {
        _db = db;
    }

    public async Task<List<string>> ResolveCandidatesAsync(
        CandidateType type,
        IEnumerable<string> candidateValues,
        string submitterCode,
        CancellationToken ct = default)
    {
        var rawValues = candidateValues.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var now = DateTime.UtcNow;

        var resolvedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (type)
        {
            case CandidateType.Direct:
                foreach (var code in rawValues)
                    resolvedUsers.Add(code);
                break;

            case CandidateType.Manager:
                // 查找发起人的直属主管
                var submitterMapping = await _db.UserMappings
                    .FirstOrDefaultAsync(u => u.SapUserCode == submitterCode || u.AdUserCode == submitterCode, ct);
                
                if (submitterMapping != null && !string.IsNullOrWhiteSpace(submitterMapping.ManagerCode))
                {
                    resolvedUsers.Add(submitterMapping.ManagerCode);
                }
                else
                {
                    // 若无明确主管配置，回退到传入的默认后备候选人或 manager
                    if (rawValues.Count > 0)
                    {
                        foreach (var code in rawValues) resolvedUsers.Add(code);
                    }
                    else
                    {
                        resolvedUsers.Add("manager");
                    }
                }
                break;

            case CandidateType.Role:
                // 查找包含指定角色的所有活跃用户
                var activeUsers = await _db.UserMappings.Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Roles)).ToListAsync(ct);
                foreach (var role in rawValues)
                {
                    var matched = activeUsers
                        .Where(u => u.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                                           .Contains(role, StringComparer.OrdinalIgnoreCase))
                        .Select(u => u.SapUserCode);
                    foreach (var u in matched)
                        resolvedUsers.Add(u);
                }
                break;

            case CandidateType.Delegate:
                foreach (var code in rawValues)
                {
                    var mapping = await _db.UserMappings
                        .FirstOrDefaultAsync(u => u.SapUserCode == code || u.AdUserCode == code, ct);
                    if (mapping != null && !string.IsNullOrWhiteSpace(mapping.DelegateUserCode) &&
                        mapping.DelegateStartTime <= now && (mapping.DelegateEndTime == null || mapping.DelegateEndTime >= now))
                    {
                        resolvedUsers.Add(mapping.DelegateUserCode);
                    }
                    resolvedUsers.Add(code);
                }
                break;

            default:
                throw new NotSupportedException($"未支持的审批人解析类型: {type}");
        }

        // 检查所有已解析人员是否有生效中的全局委托代理
        var finalCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var userCode in resolvedUsers)
        {
            finalCandidates.Add(userCode);
            var mapping = await _db.UserMappings
                .FirstOrDefaultAsync(u => u.SapUserCode == userCode || u.AdUserCode == userCode, ct);
            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.DelegateUserCode) &&
                mapping.DelegateStartTime <= now && (mapping.DelegateEndTime == null || mapping.DelegateEndTime >= now))
            {
                finalCandidates.Add(mapping.DelegateUserCode);
            }
        }

        return finalCandidates.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }
}
