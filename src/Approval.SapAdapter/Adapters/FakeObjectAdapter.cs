using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;

namespace Approval.SapAdapter.Adapters;

/// <summary>
/// 模拟单据适配器 (用于 Phase 1 纯隔离环境下的端到端纵向切片验证)
/// </summary>
public class FakeObjectAdapter : ISapObjectAdapter
{
    private readonly string _supportedCode;
    public static readonly Dictionary<string, (string Status, string InstanceId, string Hash)> MirrorStorage = new();

    public FakeObjectAdapter(string supportedCode = "CHORDR")
    {
        _supportedCode = supportedCode;
    }

    public string SupportedObjectCode => _supportedCode;

    public Task<SapObjectPayload> FetchObjectAsync(string companyId, string objectKey, CancellationToken ct = default)
    {
        // 构造标准的型号订单 / 报价单 Mock 数据 (包含表头及两张子表)
        var rawData = new
        {
            DocEntry = int.TryParse(objectKey, out var docEntry) ? docEntry : 1001,
            DocNum = "ORD-202608-001",
            CardCode = "C20000",
            CardName = "北京中源高新智能技术有限公司",
            DocDate = "2026-08-15",
            DocTotal = 85600.00m,
            Comments = "2026年度智能传感器批量采购订单",
            Creator = "manager",
            CH_ORDR_1Collection = new[]
            {
                new { LineId = 1, ItemCode = "A0001", ItemDescription = "工业级温湿度高精度变送器", Quantity = 100.0, Price = 350.00, LineTotal = 35000.00 },
                new { LineId = 2, ItemCode = "A0002", ItemDescription = "4G/5G 工业物联网采集网关", Quantity = 50.0, Price = 1012.00, LineTotal = 50600.00 }
            },
            CH_ORDR_3Collection = new[]
            {
                new { LineId = 1, ExpenseCode = "EXP01", ExpenseName = "精密校准与出厂检验费", Amount = 1200.00 }
            }
        };

        var rawJson = JsonSerializer.Serialize(rawData);

        var payload = new SapObjectPayload
        {
            CompanyId = companyId,
            ObjectCode = _supportedCode,
            ObjectKey = objectKey,
            Title = $"型号订单 #{objectKey} (北京中源高新智能技术有限公司)",
            CreatorUserCode = "manager",
            CreatorUserName = "张主管 (Manager)",
            DocTotal = 85600.00m,
            RawJson = rawJson,
            HeaderFields = new Dictionary<string, object?>
            {
                ["单据编号"] = "ORD-202608-001",
                ["客户代码"] = "C20000",
                ["客户名称"] = "北京中源高新智能技术有限公司",
                ["单据日期"] = "2026-08-15",
                ["单据金额"] = "￥85,600.00",
                ["备注说明"] = "2026年度智能传感器批量采购订单"
            },
            LineRows = new List<Dictionary<string, object?>>
            {
                new() { ["行号"] = 1, ["物料编码"] = "A0001", ["物料描述"] = "工业级温湿度高精度变送器", ["数量"] = 100, ["单价"] = "￥350.00", ["行总计"] = "￥35,000.00" },
                new() { ["行号"] = 2, ["物料编码"] = "A0002", ["物料描述"] = "4G/5G 工业物联网采集网关", ["数量"] = 50, ["单价"] = "￥1,012.00", ["行总计"] = "￥50,600.00" }
            }
        };

        return Task.FromResult(payload);
    }

    public Task<bool> WriteApprovalMirrorAsync(
        string companyId,
        string objectKey,
        string approvalStatus,
        string instanceId,
        string dataHash,
        CancellationToken ct = default)
    {
        var key = $"{companyId}:{_supportedCode}:{objectKey}";
        MirrorStorage[key] = (approvalStatus, instanceId, dataHash);
        return Task.FromResult(true);
    }
}
