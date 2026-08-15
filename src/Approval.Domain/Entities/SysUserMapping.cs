namespace Approval.Domain.Entities;

/// <summary>
/// 企业用户身份映射与组织架构关系实体
/// 打通 SAP B1 用户体系与企业统一身份 (AD / LDAP / 企微工号)
/// </summary>
public class SysUserMapping
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>SAP B1 OUSR.USERID</summary>
    public int? SapUserId { get; set; }

    /// <summary>SAP B1 OUSR.USER_CODE</summary>
    public string SapUserCode { get; set; } = string.Empty;

    /// <summary>企业统一身份工号 / AD 账号 / 企微账号</summary>
    public string AdUserCode { get; set; } = string.Empty;

    /// <summary>用户真实姓名</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>所属部门</summary>
    public string? Department { get; set; }

    /// <summary>直属上级 / 主管的用户 Code (用于组织树逐级审批)</summary>
    public string? ManagerCode { get; set; }

    /// <summary>岗位角色列表 (以逗号分隔，如 "SalesDirector,FinanceManager")</summary>
    public string? Roles { get; set; }

    /// <summary>委托代理人 Code (在请假/外出期间代行审批)</summary>
    public string? DelegateUserCode { get; set; }

    /// <summary>委托生效起始时间</summary>
    public DateTime? DelegateStartTime { get; set; }

    /// <summary>委托生效截止时间</summary>
    public DateTime? DelegateEndTime { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
