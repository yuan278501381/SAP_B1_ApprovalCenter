namespace Approval.Domain.Entities;

/// <summary>
/// 企业级单据 UI 字段可见性与展示顺序配置 (支持全公司默认与个人偏好两级覆盖)
/// </summary>
public class SysUiLayout
{
    /// <summary>
    /// 主键 ID:
    /// 全局默认: "global_{CompanyId}_{ObjectCode}"
    /// 用户私有: "user_{UserCode}_{CompanyId}_{ObjectCode}"
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// SAP 公司数据库代码 (如 DB_KCC)
    /// </summary>
    public string CompanyId { get; set; } = string.Empty;

    /// <summary>
    /// SAP 单据/UDO 对象代码 (如 CHORDR, CHOQUT, ORDR)
    /// </summary>
    public string ObjectCode { get; set; } = string.Empty;

    /// <summary>
    /// 用户代码 (null 表示全公司全局默认设置，非空表示该用户的个人专属偏好)
    /// </summary>
    public string? UserCode { get; set; }

    /// <summary>
    /// 配置类型 (默认 HeaderAndTableLayout)
    /// </summary>
    public string ConfigType { get; set; } = "HeaderAndTableLayout";

    /// <summary>
    /// 完整布局配置 JSON (包含 pinnedKeys, hiddenHeaderKeys, headerOrder, colHiddenMap, colOrderMap)
    /// </summary>
    public string LayoutJson { get; set; } = "{}";

    /// <summary>
    /// 最后更新时间 (UTC)
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新人代码 (如 admin, manager)
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;
}
