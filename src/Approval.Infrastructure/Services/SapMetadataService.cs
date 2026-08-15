using System.Collections.Concurrent;
using System.Data;
using Approval.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Approval.Infrastructure.Services;

/// <summary>
/// SAP B1 真实元数据与动态字段解析服务 (自动从 CUFD / UFD1 提取中文字段标签与下拉值翻译)
/// </summary>
public class SapMetadataService : ISapMetadataService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SapMetadataService> _logger;
    private static readonly ConcurrentDictionary<string, (DateTime ExpireAt, ObjectMetadataResult Result)> Cache = new();
    private static readonly ConcurrentDictionary<string, (DateTime ExpireAt, CompanyInfoResult Result)> CompanyCache = new();

    public SapMetadataService(IConfiguration config, ILogger<SapMetadataService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<CompanyInfoResult> GetCompanyInfoAsync(string companyId, CancellationToken ct = default)
    {
        var cacheKey = $"company_info_{companyId}".ToUpperInvariant();
        if (CompanyCache.TryGetValue(cacheKey, out var item) && item.ExpireAt > DateTime.UtcNow)
        {
            return item.Result;
        }

        var connStr = GetSapDbConnectionString(companyId);
        string compName = companyId;
        string? addr = null;

        if (!string.IsNullOrWhiteSpace(connStr))
        {
            try
            {
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync(ct);
                const string sql = "SELECT TOP 1 CompnyName, CompnyAddr FROM OADM;";
                await using var cmd = new SqlCommand(sql, conn);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var name = reader["CompnyName"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        compName = $"{name} ({companyId})";
                    }
                    addr = reader["CompnyAddr"]?.ToString()?.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load company name from OADM for {CompanyId}", companyId);
            }
        }

        var result = new CompanyInfoResult(companyId, compName, addr, null);
        CompanyCache[cacheKey] = (DateTime.UtcNow.AddHours(1), result);
        return result;
    }

    public async Task<ObjectMetadataResult> GetObjectMetadataAsync(string companyId, string objectCode, CancellationToken ct = default)
    {
        var cacheKey = $"{companyId}_{objectCode}".ToUpperInvariant();
        if (Cache.TryGetValue(cacheKey, out var item) && item.ExpireAt > DateTime.UtcNow)
        {
            return item.Result;
        }

        var result = await LoadMetadataFromDbAsync(companyId, objectCode, ct);
        Cache[cacheKey] = (DateTime.UtcNow.AddMinutes(10), result);
        return result;
    }

    private async Task<ObjectMetadataResult> LoadMetadataFromDbAsync(string companyId, string objectCode, CancellationToken ct)
    {
        var obj = objectCode.Trim().ToUpperInvariant();
        var mainTable = obj.StartsWith('@') ? obj : $"@{obj}";
        
        // 映射常见子表
        var childTables = new List<string>();
        if (obj == "CHORDR" || obj == "@CHORDR")
        {
            mainTable = "@CH_ORDR";
            childTables.AddRange(["@CH_ORDR_1", "@CH_ORDR_3"]);
        }
        else if (obj == "CHOQUT" || obj == "@CHOQUT")
        {
            mainTable = "@CH_OQUT";
            childTables.AddRange(["@CH_OQUT_1", "@CH_OQUT_3"]);
        }
        else if (obj == "ORDR")
        {
            mainTable = "ORDR";
            childTables.Add("RDR1");
        }

        var headerFields = new Dictionary<string, FieldMetaInfo>(StringComparer.OrdinalIgnoreCase);
        var childTableFields = new Dictionary<string, Dictionary<string, FieldMetaInfo>>(StringComparer.OrdinalIgnoreCase);

        var connStr = GetSapDbConnectionString(companyId);
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return new ObjectMetadataResult(objectCode, mainTable, headerFields, childTableFields);
        }

        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            // 1. 查询所有相关表在 CUFD 中的字段定义 (包含 RTable 链接无对象表)
            var allTables = new List<string> { mainTable };
            allTables.AddRange(childTables);
            var inClause = string.Join(",", allTables.Select(t => $"'{t}'"));

            var rTablesToLoad = new Dictionary<string, List<(string TableId, string AliasId)>>(StringComparer.OrdinalIgnoreCase);

            var queryCufd = $@"
                SELECT TableID, FieldID, AliasID, Descr, TypeID, EditType, RTable 
                FROM CUFD 
                WHERE TableID IN ({inClause}) 
                ORDER BY TableID, FieldID;";

            await using (var cmd = new SqlCommand(queryCufd, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var tableId = reader["TableID"].ToString()?.Trim() ?? string.Empty;
                    var aliasId = reader["AliasID"].ToString()?.Trim() ?? string.Empty;
                    var descr = reader["Descr"].ToString()?.Trim() ?? string.Empty;
                    var typeId = reader["TypeID"].ToString()?.Trim() ?? string.Empty;
                    var rTable = reader["RTable"]?.ToString()?.Trim() ?? string.Empty;

                    var info = new FieldMetaInfo(aliasId, descr, typeId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                    if (string.Equals(tableId, mainTable, StringComparison.OrdinalIgnoreCase))
                    {
                        headerFields[aliasId] = info;
                        headerFields["U_" + aliasId] = info;
                    }
                    else
                    {
                        if (!childTableFields.TryGetValue(tableId, out var cMap))
                        {
                            cMap = new Dictionary<string, FieldMetaInfo>(StringComparer.OrdinalIgnoreCase);
                            childTableFields[tableId] = cMap;
                        }
                        cMap[aliasId] = info;
                        cMap["U_" + aliasId] = info;
                    }

                    if (!string.IsNullOrWhiteSpace(rTable))
                    {
                        if (!rTablesToLoad.TryGetValue(rTable, out var list))
                        {
                            list = new List<(string, string)>();
                            rTablesToLoad[rTable] = list;
                        }
                        list.Add((tableId, aliasId));
                    }
                }
            }

            // 2. 查询 UFD1 下拉有效值映射 (值 -> 中文描述)
            var queryUfd1 = $@"
                SELECT u.TableID, c.AliasID, u.FldValue, u.Descr 
                FROM UFD1 u 
                INNER JOIN CUFD c ON u.TableID = c.TableID AND u.FieldID = c.FieldID 
                WHERE u.TableID IN ({inClause}) 
                ORDER BY u.TableID, c.AliasID, u.IndexID;";

            await using (var cmd = new SqlCommand(queryUfd1, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var tableId = reader["TableID"].ToString()?.Trim() ?? string.Empty;
                    var aliasId = reader["AliasID"].ToString()?.Trim() ?? string.Empty;
                    var fldVal = reader["FldValue"].ToString()?.Trim() ?? string.Empty;
                    var descr = reader["Descr"].ToString()?.Trim() ?? string.Empty;

                    if (string.Equals(tableId, mainTable, StringComparison.OrdinalIgnoreCase))
                    {
                        if (headerFields.TryGetValue(aliasId, out var meta) && meta.ValidValues != null)
                        {
                            meta.ValidValues[fldVal] = descr;
                        }
                    }
                    else
                    {
                        if (childTableFields.TryGetValue(tableId, out var cMap) && cMap.TryGetValue(aliasId, out var meta) && meta.ValidValues != null)
                        {
                            meta.ValidValues[fldVal] = descr;
                        }
                    }
                }
            }

            // 3. 动态加载 RTable 自定义无对象表字典 (如 @CARTON_REQUIREMENTS, @KCC_SO_TYPE 等)
            foreach (var (rTable, targetList) in rTablesToLoad)
            {
                var rTableName = rTable.StartsWith('@') ? rTable : $"@{rTable}";
                try
                {
                    var queryRTable = $"SELECT Code, Name FROM [{rTableName}];";
                    await using var cmd = new SqlCommand(queryRTable, conn);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var code = reader["Code"]?.ToString()?.Trim() ?? string.Empty;
                        var name = reader["Name"]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(code)) continue;

                        foreach (var (tId, aId) in targetList)
                        {
                            if (string.Equals(tId, mainTable, StringComparison.OrdinalIgnoreCase))
                            {
                                if (headerFields.TryGetValue(aId, out var meta) && meta.ValidValues != null)
                                    meta.ValidValues[code] = name;
                            }
                            else if (childTableFields.TryGetValue(tId, out var cMap) && cMap.TryGetValue(aId, out var cMeta) && cMeta.ValidValues != null)
                            {
                                cMeta.ValidValues[code] = name;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "加载 RTable 表 [{TableName}] 字典失败，跳过", rTableName);
                }
            }

            // 4. 加载 SAP 核心系统字典 (销售员 OSLP, 付款条件 OCTG, 员工 OHEM 等)
            await LoadSystemDictionariesAsync(conn, mainTable, headerFields, ct);

            _logger.LogInformation("成功为对象 [{ObjectCode}] 加载 SAP 动态元数据 (表头字段: {HeaderCount}, 子表数: {ChildCount})",
                objectCode, headerFields.Count / 2, childTableFields.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 SAP 数据库加载元数据时发生异常，将返回空元数据并由前端自适应降级处理: {Message}", ex.Message);
        }

        return new ObjectMetadataResult(objectCode, mainTable, headerFields, childTableFields);
    }

    private static async Task LoadSystemDictionariesAsync(
        SqlConnection conn,
        string mainTable,
        Dictionary<string, FieldMetaInfo> headerFields,
        CancellationToken ct)
    {
        // 4.1 销售员字典 (OSLP) -> SlpCode / U_SlpCode
        try
        {
            const string querySlp = "SELECT SlpCode, SlpName FROM OSLP;";
            await using var cmd = new SqlCommand(querySlp, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var code = reader["SlpCode"]?.ToString()?.Trim() ?? string.Empty;
                var name = reader["SlpName"]?.ToString()?.Trim() ?? string.Empty;
                AttachValidValue(headerFields, "SlpCode", code, name);
            }
        }
        catch { /* 忽略异常降级 */ }

        // 4.2 付款条件字典 (OCTG) -> GroupNum / U_GroupNum
        try
        {
            const string queryCtg = "SELECT GroupNum, PymntGroup FROM OCTG;";
            await using var cmd = new SqlCommand(queryCtg, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var code = reader["GroupNum"]?.ToString()?.Trim() ?? string.Empty;
                var name = reader["PymntGroup"]?.ToString()?.Trim() ?? string.Empty;
                AttachValidValue(headerFields, "GroupNum", code, name);
            }
        }
        catch { /* 忽略异常降级 */ }

        // 4.3 员工/业务助理字典 (OHEM) -> saleass / U_saleass / Owner / EmpID
        try
        {
            const string queryHem = "SELECT empID, ISNULL(lastName,'') + ISNULL(firstName,'') AS FullName FROM OHEM;";
            await using var cmd = new SqlCommand(queryHem, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var code = reader["empID"]?.ToString()?.Trim() ?? string.Empty;
                var name = reader["FullName"]?.ToString()?.Trim() ?? string.Empty;
                AttachValidValue(headerFields, "saleass", code, name);
                AttachValidValue(headerFields, "Owner", code, name);
                AttachValidValue(headerFields, "EmpID", code, name);
            }
        }
        catch { /* 忽略异常降级 */ }
    }

    private static void AttachValidValue(Dictionary<string, FieldMetaInfo> fields, string key, string code, string name)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) return;
        if (fields.TryGetValue(key, out var meta) && meta.ValidValues != null)
        {
            meta.ValidValues[code] = name;
        }
        if (fields.TryGetValue("U_" + key, out var uMeta) && uMeta.ValidValues != null)
        {
            uMeta.ValidValues[code] = name;
        }
    }

    private string? GetSapDbConnectionString(string companyId)
    {
        var connStr = _config.GetConnectionString("ApprovalDbConnection");
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        // 将 Database=ApprovalDB 替换为目标 SAP 公司库（如 DB_KCC）
        var targetDb = string.IsNullOrWhiteSpace(companyId) ? "DB_KCC" : companyId;
        return connStr.Replace("Database=ApprovalDB", $"Database={targetDb}", StringComparison.OrdinalIgnoreCase);
    }
}
