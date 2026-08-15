using Approval.Application.Common.Models;

namespace Approval.Application.Common.Interfaces;

/// <summary>
/// SAP 业务对象适配器标准契约
/// </summary>
public interface ISapObjectAdapter
{
    /// <summary>
    /// 支持的 UDO / 业务对象编码 (例如 CHORDR, CHOQUT, 17)
    /// </summary>
    string SupportedObjectCode { get; }

    /// <summary>
    /// 从 SAP Service Layer / 模拟数据中安全抓取单据完整表头与明细
    /// </summary>
    Task<SapObjectPayload> FetchObjectAsync(string companyId, string objectKey, CancellationToken ct = default);

    /// <summary>
    /// 将审批结果镜像状态安全回写至 SAP UDO 镜像字段
    /// </summary>
    Task<bool> WriteApprovalMirrorAsync(
        string companyId,
        string objectKey,
        string approvalStatus,
        string instanceId,
        string dataHash,
        CancellationToken ct = default);
}

/// <summary>
/// SAP 适配器注册与路由中心
/// </summary>
public interface ISapAdapterRegistry
{
    ISapObjectAdapter GetAdapter(string objectCode);
}
