namespace Approval.Domain.Constants;

/// <summary>业务常量字典 —— 统一收敛全系统的魔法字符串与数字</summary>
public static class BusinessConstants
{
    /// <summary>默认业务公司库</summary>
    public const string DefaultCompanyDb = "DB_KCC";
    
    /// <summary>任务默认截止天数</summary>
    public const int DefaultTaskDueDays = 3;
    
    /// <summary>Outbox 事件类型</summary>
    public static class OutboxEvents
    {
        public const string InstanceApproved = "InstanceApproved";
        public const string InstanceRejected = "InstanceRejected";
        public const string InstanceReturned = "InstanceReturned";
        public const string InstanceCancelled = "InstanceCancelled";
    }
    
    /// <summary>防篡改熔断事件</summary>
    public static class TamperEvents
    {
        public const string DocumentChanged = "DOCUMENT_CHANGED";
    }
}
