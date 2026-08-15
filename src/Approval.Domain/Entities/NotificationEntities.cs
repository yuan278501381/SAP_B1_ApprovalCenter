namespace Approval.Domain.Entities;

/// <summary>
/// 系统站内消息与审批事件通知实体
/// </summary>
public class SysNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>
    /// 通知接收人工号
    /// </summary>
    public string RecipientUserCode { get; set; } = string.Empty;

    /// <summary>
    /// 发送者工号 (如系统 system 或发起人/操作人工号)
    /// </summary>
    public string SenderUserCode { get; set; } = "system";

    /// <summary>
    /// 关联的审批实例 ID
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// 关联的业务对象编码 (如 CHORDR)
    /// </summary>
    public string? ObjectCode { get; set; }

    /// <summary>
    /// 关联的单据键值 (如 1001)
    /// </summary>
    public string? ObjectKey { get; set; }

    /// <summary>
    /// 通知标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 通知正文内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 通知类型: Revocation(发起人撤销), TaskAssigned(新待办), Approved(完审放行), Rejected(驳回)
    /// </summary>
    public string Type { get; set; } = "Revocation";

    /// <summary>
    /// 是否已读
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 阅读时间
    /// </summary>
    public DateTime? ReadAt { get; set; }
}
