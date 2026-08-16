using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using System.Xml.Linq;
using Approval.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Approval.Infrastructure.Services;

/// <summary>
/// SAP B1 真实元数据与动态字段解析服务
/// 支持: 多级缓存 (内存 -> 磁盘文件持久化 -> 数据库直拉) + 10分钟后台自动刷新落盘
/// </summary>
public class SapMetadataService : ISapMetadataService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SapMetadataService> _logger;
    private static readonly ConcurrentDictionary<string, (DateTime ExpireAt, ObjectMetadataResult Result)> Cache = new();
    private static readonly ConcurrentDictionary<string, (DateTime ExpireAt, CompanyInfoResult Result)> CompanyCache = new();
    private static readonly ConcurrentDictionary<string, (DateTime ExpireAt, ChUdoFormMetadataResult Result)> FormCache = new();
    private readonly string _cacheDirectory;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// SAP B1 官方核心标准业务单据与子表元数据映射矩阵
    /// </summary>
    private static readonly Dictionary<string, (string TableName, string ObjDesc, Dictionary<string, string> ChildTables)> StandardSapDocuments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORDR"] = ("ORDR", "销售订单", new() { ["RDR1"] = "内容", ["RDR2"] = "包材与序列", ["RDR3"] = "运费", ["DocumentLines"] = "内容", ["DocumentAdditionalExpenses"] = "运费" }),
        ["Orders"] = ("ORDR", "销售订单", new() { ["RDR1"] = "内容", ["RDR3"] = "运费", ["DocumentLines"] = "内容", ["DocumentAdditionalExpenses"] = "运费" }),
        ["OQUT"] = ("OQUT", "销售报价单", new() { ["QUT1"] = "内容", ["QUT3"] = "运费", ["DocumentLines"] = "内容", ["DocumentAdditionalExpenses"] = "运费" }),
        ["Quotations"] = ("OQUT", "销售报价单", new() { ["QUT1"] = "内容", ["QUT3"] = "运费", ["DocumentLines"] = "内容" }),
        ["ODLN"] = ("ODLN", "交货单", new() { ["DLN1"] = "内容", ["DLN3"] = "运费", ["DocumentLines"] = "内容" }),
        ["DeliveryNotes"] = ("ODLN", "交货单", new() { ["DLN1"] = "内容" }),
        ["OINV"] = ("OINV", "应收发票", new() { ["INV1"] = "内容", ["INV3"] = "运费", ["DocumentLines"] = "内容" }),
        ["Invoices"] = ("OINV", "应收发票", new() { ["INV1"] = "内容" }),
        ["ORIN"] = ("ORIN", "应收贷项凭证", new() { ["RIN1"] = "内容", ["DocumentLines"] = "内容" }),
        ["OPOR"] = ("OPOR", "采购订单", new() { ["POR1"] = "内容", ["POR3"] = "运费", ["DocumentLines"] = "内容", ["DocumentAdditionalExpenses"] = "运费" }),
        ["PurchaseOrders"] = ("OPOR", "采购订单", new() { ["POR1"] = "内容" }),
        ["OPDN"] = ("OPDN", "收货采购单", new() { ["PDN1"] = "内容", ["DocumentLines"] = "内容" }),
        ["OPCH"] = ("OPCH", "应付发票", new() { ["PCH1"] = "内容", ["DocumentLines"] = "内容" }),
        ["PurchaseInvoices"] = ("OPCH", "应付发票", new() { ["PCH1"] = "内容" }),
        ["ORPC"] = ("ORPC", "应付贷项凭证", new() { ["RPC1"] = "内容", ["DocumentLines"] = "内容" }),
        ["OWOR"] = ("OWOR", "生产订单", new() { ["WOR1"] = "组件", ["ProductionOrderLines"] = "组件" }),
        ["ProductionOrders"] = ("OWOR", "生产订单", new() { ["WOR1"] = "组件" }),
        ["OWTR"] = ("OWTR", "库存转储", new() { ["WTR1"] = "内容", ["StockTransferLines"] = "内容" }),
        ["StockTransfers"] = ("OWTR", "库存转储", new() { ["WTR1"] = "内容" }),
        ["OIGN"] = ("OIGN", "收货入库", new() { ["IGN1"] = "内容", ["DocumentLines"] = "内容" }),
        ["OIGE"] = ("OIGE", "发货出库", new() { ["IGE1"] = "内容", ["DocumentLines"] = "内容" }),
        ["OJDT"] = ("OJDT", "日记账分录", new() { ["JDT1"] = "内容", ["JournalEntryLines"] = "内容" }),
        ["JournalEntries"] = ("OJDT", "日记账分录", new() { ["JDT1"] = "内容" }),
        ["OBTD"] = ("OBTD", "日记账凭证", new() { ["BTD1"] = "内容", ["JournalVoucherLines"] = "内容" }),
        ["JournalVouchers"] = ("OBTD", "日记账凭证", new() { ["BTD1"] = "内容", ["JournalVoucherLines"] = "内容" }),
        ["ODRF"] = ("ODRF", "单据草稿", new() { ["DRF1"] = "内容", ["DRF2"] = "包材与序列", ["DRF3"] = "运费", ["DocumentLines"] = "内容", ["DocumentAdditionalExpenses"] = "运费" }),
        ["Drafts"] = ("ODRF", "单据草稿", new() { ["DRF1"] = "内容", ["DRF2"] = "包材与序列", ["DRF3"] = "运费", ["DocumentLines"] = "内容", ["DocumentAdditionalExpenses"] = "运费" })
    };

    public SapMetadataService(IConfiguration config, ILogger<SapMetadataService> logger)
    {
        _config = config;
        _logger = logger;
        _cacheDirectory = Path.Combine(AppContext.BaseDirectory, "metadata_cache");
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建本地元数据持久化落盘目录 [{CacheDir}] 失败", _cacheDirectory);
        }
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

    public async Task<ObjectMetadataResult> GetObjectMetadataAsync(
        string companyId,
        string objectCode,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var cacheKey = $"{companyId}_{objectCode}".ToUpperInvariant();

        // 1. 内存高速缓存
        if (!forceRefresh && Cache.TryGetValue(cacheKey, out var item) && item.ExpireAt > DateTime.UtcNow)
        {
            return item.Result;
        }

        // 2. 本地磁盘持久化快照 (冷启动与极速直读)
        var diskFilePath = GetDiskCachePath(companyId, objectCode);
        if (!forceRefresh && File.Exists(diskFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(diskFilePath, ct);
                var diskResult = JsonSerializer.Deserialize<ObjectMetadataResult>(json, JsonOpts);
                if (diskResult != null)
                {
                    Cache[cacheKey] = (DateTime.UtcNow.AddMinutes(10), diskResult);
                    return diskResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取本地元数据磁盘快照 [{FilePath}] 失败，将回退至数据库直拉", diskFilePath);
            }
        }

        // 3. 直连 SAP 业务数据库动态加载
        var result = await LoadMetadataFromDbAsync(companyId, objectCode, ct);
        Cache[cacheKey] = (DateTime.UtcNow.AddMinutes(10), result);

        // 4. 异步持久化落盘
        await SaveToDiskAsync(diskFilePath, result, ct);

        return result;
    }

    public async Task RefreshAllMetadataAndSaveToDiskAsync(string companyId, CancellationToken ct = default)
    {
        _logger.LogInformation("⏳ 开始执行 SAP 全量动态元数据与系统字典拉取与持久化落盘 (账套: {CompanyId})...", companyId);

        // 常用业务单据与 UDO 清单
        var commonObjects = new[] { "CHORDR", "CHOQUT", "ORDR", "OQUT", "ODLN", "OINV", "OPOR", "OPDN", "OPCH", "OWOR", "OWTR", "OJDT", "ODRF" };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var successCount = 0;

        foreach (var objCode in commonObjects)
        {
            try
            {
                var meta = await LoadMetadataFromDbAsync(companyId, objCode, ct);
                var cacheKey = $"{companyId}_{objCode}".ToUpperInvariant();
                Cache[cacheKey] = (DateTime.UtcNow.AddMinutes(10), meta);

                var diskPath = GetDiskCachePath(companyId, objCode);
                await SaveToDiskAsync(diskPath, meta, ct);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台定时刷新对象 [{ObjectCode}] 元数据失败: {Message}", objCode, ex.Message);
            }
        }

        sw.Stop();
        _logger.LogInformation("✅ 成功刷新并持久化落盘 {Count}/{Total} 个单据对象元数据，总耗时: {ElapsedMs}ms",
            successCount, commonObjects.Length, sw.ElapsedMilliseconds);
    }

    private string GetDiskCachePath(string companyId, string objectCode)
    {
        var safeCode = objectCode.Replace("@", "").ToUpperInvariant();
        return Path.Combine(_cacheDirectory, $"{companyId.ToUpperInvariant()}_{safeCode}.json");
    }

    private async Task SaveToDiskAsync(string filePath, ObjectMetadataResult data, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            var tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, filePath, true);
            _logger.LogDebug("已成功将元数据安全落盘至: {FilePath} ({Bytes} bytes)", filePath, json.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "元数据持久化落盘失败: {FilePath}", filePath);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private async Task<ObjectMetadataResult> LoadMetadataFromDbAsync(string companyId, string objectCode, CancellationToken ct)
    {
        var obj = objectCode.Trim().ToUpperInvariant();
        var mainTable = obj.StartsWith('@') ? obj : $"@{obj}";
        var childTables = new List<string>();
        string? objectDescription = null;
        var childTableDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var headerFields = new Dictionary<string, FieldMetaInfo>(StringComparer.OrdinalIgnoreCase);
        var childTableFields = new Dictionary<string, Dictionary<string, FieldMetaInfo>>(StringComparer.OrdinalIgnoreCase);

        // 优先匹配 SAP B1 官方标准单据字典
        if (StandardSapDocuments.TryGetValue(obj, out var stdDoc))
        {
            mainTable = stdDoc.TableName;
            objectDescription = stdDoc.ObjDesc;
            foreach (var kvp in stdDoc.ChildTables)
            {
                if (!kvp.Key.EndsWith("Collection", StringComparison.OrdinalIgnoreCase) && !kvp.Key.Contains("Lines", StringComparison.OrdinalIgnoreCase))
                {
                    childTables.Add(kvp.Key);
                }
                childTableDescriptions[kvp.Key] = kvp.Value;
            }
        }

        var connStr = GetSapDbConnectionString(companyId);
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return new ObjectMetadataResult(objectCode, mainTable, headerFields, childTableFields, childTableDescriptions, objectDescription);
        }

        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            // 1. 动态从 SAP 数据库 (OUDO + UDO1 + OUTB) 提取 UDO 对象与所有子表的真实物理表名及中文描述
            try
            {
                const string queryUdo = @"
                    SELECT T0.Code AS UdoCode, T0.Name AS UdoName, T0.TableName AS HeaderTable, T3.Descr AS HeaderTableDesc,
                           T1.SonNum, T1.TableName AS ChildTable, T2.Descr AS ChildTableDesc
                    FROM OUDO T0
                    LEFT JOIN OUTB T3 ON T0.TableName = T3.TableName
                    LEFT JOIN UDO1 T1 ON T0.Code = T1.Code
                    LEFT JOIN OUTB T2 ON T1.TableName = T2.TableName
                    WHERE T0.Code = @ObjectCode OR T0.TableName = @CleanTable;";

                await using (var cmd = new SqlCommand(queryUdo, conn))
                {
                    cmd.Parameters.AddWithValue("@ObjectCode", obj);
                    cmd.Parameters.AddWithValue("@CleanTable", obj.TrimStart('@'));
                    await using (var reader = await cmd.ExecuteReaderAsync(ct))
                    {
                        while (await reader.ReadAsync(ct))
                        {
                            var udoName = reader["UdoName"]?.ToString()?.Trim();
                            var headerDesc = reader["HeaderTableDesc"]?.ToString()?.Trim();
                            if (string.IsNullOrWhiteSpace(objectDescription))
                            {
                                objectDescription = !string.IsNullOrWhiteSpace(udoName) ? udoName : headerDesc;
                            }

                            var headerTbl = reader["HeaderTable"]?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(headerTbl))
                            {
                                mainTable = headerTbl.StartsWith('@') ? headerTbl : $"@{headerTbl}";
                            }

                            var childTbl = reader["ChildTable"]?.ToString()?.Trim();
                            var childDesc = reader["ChildTableDesc"]?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(childTbl))
                            {
                                var formattedChild = childTbl.StartsWith('@') ? childTbl : $"@{childTbl}";
                                if (!childTables.Contains(formattedChild))
                                {
                                    childTables.Add(formattedChild);
                                }
                                if (!string.IsNullOrWhiteSpace(childDesc))
                                {
                                    childTableDescriptions[formattedChild] = childDesc;
                                    childTableDescriptions[childTbl] = childDesc;
                                    childTableDescriptions[childTbl + "Collection"] = childDesc;
                                    childTableDescriptions[formattedChild.TrimStart('@') + "Collection"] = childDesc;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query UDO metadata from OUDO/UDO1/OUTB for {ObjectCode}", objectCode);
            }

            // 兜底映射常用表
            if (childTables.Count == 0)
            {
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
            }

            // 2. 查询所有相关表在 CUFD 中的字段定义
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

            // 3. 查询 UFD1 下拉有效值映射 (值 -> 中文描述)
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

            // 4. 动态加载 RTable 自定义无对象表字典 (如 @CARTON_REQUIREMENTS, @KCC_SO_TYPE 等)
            foreach (var (rTable, targetList) in rTablesToLoad)
            {
                var rTableName = rTable.StartsWith('@') ? rTable : $"@{rTable}";
                
                // 安全红线：对动态表名进行严格正则白名单校验，拒绝一切非法字符以防御 SQL 注入
                if (!System.Text.RegularExpressions.Regex.IsMatch(rTableName, @"^[a-zA-Z0-9_@#]+$"))
                {
                    _logger.LogWarning("拒绝加载可疑表名 [{TableName}]，疑似 SQL 注入攻击", rTableName);
                    continue;
                }

                try
                {
                    var queryRTable = $"SELECT Code, Name FROM [{rTableName}];";
                    await using (var cmd = new SqlCommand(queryRTable, conn))
                    await using (var reader = await cmd.ExecuteReaderAsync(ct))
                    {
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
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "加载 RTable 表 [{TableName}] 字典失败，跳过: {Message}", rTableName, ex.Message);
                }
            }

            // 5. 加载 SAP 核心系统字典 (销售员 OSLP, 付款条件 OCTG, 员工 OHEM, 附加运费 OEXD, 税码 OSTC 等)
            await LoadSystemDictionariesAsync(conn, mainTable, headerFields, childTableFields, ct);

            _logger.LogInformation("成功为对象 [{ObjectCode}] 加载 SAP 动态元数据 (表头字段: {HeaderCount}, 子表数: {ChildCount})",
                objectCode, headerFields.Count / 2, childTableFields.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 SAP 数据库加载元数据时发生异常，将返回空元数据并由前端自适应降级处理: {Message}", ex.Message);
        }

        return new ObjectMetadataResult(objectCode, mainTable, headerFields, childTableFields, childTableDescriptions, objectDescription);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private async Task LoadSystemDictionariesAsync(
        SqlConnection conn,
        string mainTable,
        Dictionary<string, FieldMetaInfo> headerFields,
        Dictionary<string, Dictionary<string, FieldMetaInfo>> childTableFields,
        CancellationToken ct)
    {
        // 5.1 销售员字典 (OSLP) -> SlpCode / U_SlpCode
        try
        {
            const string querySlp = "SELECT SlpCode, SlpName FROM OSLP;";
            await using (var cmd = new SqlCommand(querySlp, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    var code = reader["SlpCode"]?.ToString()?.Trim() ?? string.Empty;
                    var name = reader["SlpName"]?.ToString()?.Trim() ?? string.Empty;
                    AttachValidValue(headerFields, childTableFields, "SlpCode", code, name);
                    count++;
                }
                _logger.LogDebug("成功加载 OSLP 销售员字典 {Count} 条", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 OSLP 销售员字典失败: {Message}", ex.Message);
        }

        // 5.2 付款条件字典 (OCTG) -> GroupNum / U_GroupNum
        try
        {
            const string queryCtg = "SELECT GroupNum, PymntGroup FROM OCTG;";
            await using (var cmd = new SqlCommand(queryCtg, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    var code = reader["GroupNum"]?.ToString()?.Trim() ?? string.Empty;
                    var name = reader["PymntGroup"]?.ToString()?.Trim() ?? string.Empty;
                    AttachValidValue(headerFields, childTableFields, "GroupNum", code, name);
                    count++;
                }
                _logger.LogDebug("成功加载 OCTG 付款条件字典 {Count} 条", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 OCTG 付款条件字典失败: {Message}", ex.Message);
        }

        // 5.3 员工/业务助理字典 (OHEM) -> saleass / U_saleass / Owner / EmpID
        try
        {
            const string queryHem = "SELECT empID, ISNULL(lastName,'') + ISNULL(firstName,'') AS FullName FROM OHEM;";
            await using (var cmd = new SqlCommand(queryHem, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    var code = reader["empID"]?.ToString()?.Trim() ?? string.Empty;
                    var name = reader["FullName"]?.ToString()?.Trim() ?? string.Empty;
                    AttachValidValue(headerFields, childTableFields, "saleass", code, name);
                    AttachValidValue(headerFields, childTableFields, "Owner", code, name);
                    AttachValidValue(headerFields, childTableFields, "EmpID", code, name);
                    count++;
                }
                _logger.LogDebug("成功加载 OHEM 员工字典 {Count} 条", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 OHEM 员工字典失败: {Message}", ex.Message);
        }

        // 5.4 附加运费/费用代码字典 (OEXD) -> ExpnsCode / U_ExpnsCode / ExpenseCode / U_ExpenseCode
        try
        {
            const string queryExd = "SELECT ExpnsCode, ExpnsName FROM OEXD;";
            await using (var cmd = new SqlCommand(queryExd, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    var code = reader["ExpnsCode"]?.ToString()?.Trim() ?? string.Empty;
                    var name = reader["ExpnsName"]?.ToString()?.Trim() ?? string.Empty;
                    AttachValidValue(headerFields, childTableFields, "ExpnsCode", code, name);
                    AttachValidValue(headerFields, childTableFields, "ExpenseCode", code, name);
                    AttachValidValue(headerFields, childTableFields, "U_ExpnsCode", code, name);
                    AttachValidValue(headerFields, childTableFields, "U_ExpenseCode", code, name);
                    count++;
                }
                _logger.LogDebug("成功加载 OEXD 附加运费字典 {Count} 条", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 OEXD 附加运费字典失败: {Message}", ex.Message);
        }

        // 5.5 税收代码字典 (OSTC) -> TaxCode / VatGroup / U_VatGroup
        try
        {
            const string queryStc = "SELECT Code, Name FROM OSTC;";
            await using (var cmd = new SqlCommand(queryStc, conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                var count = 0;
                while (await reader.ReadAsync(ct))
                {
                    var code = reader["Code"]?.ToString()?.Trim() ?? string.Empty;
                    var name = reader["Name"]?.ToString()?.Trim() ?? string.Empty;
                    AttachValidValue(headerFields, childTableFields, "TaxCode", code, name);
                    AttachValidValue(headerFields, childTableFields, "VatGroup", code, name);
                    AttachValidValue(headerFields, childTableFields, "U_VatGroup", code, name);
                    count++;
                }
                _logger.LogDebug("成功加载 OSTC 税码字典 {Count} 条", count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 OSTC 税码字典失败: {Message}", ex.Message);
        }
    }

    private static void AttachValidValue(
        Dictionary<string, FieldMetaInfo> headerFields,
        Dictionary<string, Dictionary<string, FieldMetaInfo>> childTableFields,
        string pattern,
        string code,
        string name)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) return;

        // 1. 表头字段匹配
        AttachToDict(headerFields, pattern, code, name);

        // 2. 子表字段匹配
        foreach (var cMap in childTableFields.Values)
        {
            AttachToDict(cMap, pattern, code, name);
        }
    }

    private static void AttachToDict(Dictionary<string, FieldMetaInfo> fields, string pattern, string code, string name)
    {
        var cleanPattern = pattern.StartsWith("U_") ? pattern.Substring(2) : pattern;

        foreach (var (fKey, meta) in fields)
        {
            var cleanKey = fKey.StartsWith("U_") ? fKey.Substring(2) : fKey;

            if (fKey.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                cleanKey.Equals(cleanPattern, StringComparison.OrdinalIgnoreCase) ||
                cleanKey.Contains(cleanPattern, StringComparison.OrdinalIgnoreCase))
            {
                meta.ValidValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                meta.ValidValues[code] = name;
            }
        }
    }

    /// <summary>
    /// 从 SAP B1 CPRF / OUSR 表获取用户针对指定单据在 SAP 客户端中配置的表格列显示顺序、列宽与隐藏列状态
    /// (支持 admin/administrator 自动映射到 SAP manager，支持多候选 FormID 智能匹配与 manager 偏好回退)
    /// </summary>
    public async Task<UserFormSettingsResult> GetUserFormSettingsAsync(
        string companyId,
        string objectCode,
        string userCode,
        CancellationToken ct = default)
    {
        var result = new UserFormSettingsResult
        {
            ObjectCode = objectCode,
            UserCode = userCode,
            HasSapSettings = false
        };

        var sapConnStr = GetSapDbConnectionString(companyId);
        if (string.IsNullOrWhiteSpace(sapConnStr)) return result;

        // 1. admin / administrator / sa 账号自动视为 SAP manager
        var targetUser = userCode;
        if (string.IsNullOrWhiteSpace(targetUser) ||
            string.Equals(targetUser, "admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetUser, "administrator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetUser, "sa", StringComparison.OrdinalIgnoreCase))
        {
            targetUser = "manager";
        }

        try
        {
            await using var conn = new SqlConnection(sapConnStr);
            await conn.OpenAsync(ct);

            // 2. 查询对应用户的 UserSign (查不到则默认回退到 manager = 1)
            int userSign = 1;
            await using (var cmdUser = conn.CreateCommand())
            {
                cmdUser.CommandText = "SELECT TOP 1 USERID FROM OUSR WHERE USER_CODE = @UserCode";
                cmdUser.Parameters.Add(new SqlParameter("@UserCode", SqlDbType.NVarChar) { Value = targetUser });
                var res = await cmdUser.ExecuteScalarAsync(ct);
                if (res != null && res != DBNull.Value)
                {
                    userSign = Convert.ToInt32(res);
                }
            }

            // 3. 计算 FormID 候选列表 (涵盖 UDO 辅助平台 FormID 与 SAP 标准 FormID)
            var formIds = new List<string>();
            var upperObj = objectCode.ToUpperInvariant();
            if (upperObj.Contains("CHORDR") || upperObj.Contains("ORDER") || upperObj == "ORDR")
            {
                formIds.AddRange(new[] { "CH_ORDR01", "CH_ORDR02", "CH_ORDR03", "139", "CHORDR" });
            }
            else if (upperObj.Contains("CHOQUT") || upperObj.Contains("QUOT") || upperObj == "OQUT")
            {
                formIds.AddRange(new[] { "CH_OQUT01", "CH_OQUT02", "149", "CHOQUT" });
            }
            else if (upperObj.Contains("INV") || upperObj == "OINV")
            {
                formIds.AddRange(new[] { "133", "OINV" });
            }
            else if (upperObj.Contains("DLN") || upperObj == "ODLN")
            {
                formIds.AddRange(new[] { "140", "ODLN" });
            }
            else if (upperObj.Contains("POR") || upperObj == "OPOR")
            {
                formIds.AddRange(new[] { "142", "OPOR" });
            }
            else if (upperObj.Contains("DRF") || upperObj == "ODRF")
            {
                formIds.AddRange(new[] { "112", "139", "ODRF" });
            }
            else
            {
                formIds.Add(objectCode);
            }

            // 4. 从 CPRF 查询表单格式 (先查目标用户，若为空则回退到 manager = 1)
            var columnList = await QueryCprfColumnsAsync(conn, userSign, formIds, ct);
            if (columnList.Count == 0 && userSign != 1)
            {
                columnList = await QueryCprfColumnsAsync(conn, 1, formIds, ct);
            }

            if (columnList.Count > 0)
            {
                result = result with
                {
                    HasSapSettings = true,
                    ColumnOrders = columnList.Select(x => x.ColId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    HiddenColumns = columnList.Where(x => x.VisInForm.Equals("N", StringComparison.OrdinalIgnoreCase)).Select(x => x.ColId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ColumnWidths = columnList.GroupBy(x => x.ColId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Width, StringComparer.OrdinalIgnoreCase)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SapMetadata] 查询用户 {UserCode} 在单据 {ObjectCode} 的 SAP CPRF 偏好失败，降级回退", userCode, objectCode);
        }

        return result;
    }

    private static async Task<List<(string ColId, int VisualIndex, string VisInForm, int Width)>> QueryCprfColumnsAsync(
        SqlConnection conn,
        int userSign,
        List<string> formIds,
        CancellationToken ct)
    {
        var columnList = new List<(string ColId, int VisualIndex, string VisInForm, int Width)>();
        var formInClause = string.Join(",", formIds.Select(f => $"'{f}'"));

        await using var cmdCprf = conn.CreateCommand();
        cmdCprf.CommandText = $@"
            SELECT ColID, VisualIndx, VisInForm, Width
            FROM CPRF
            WHERE UserSign = @UserSign
              AND (FormID IN ({formInClause}) OR FormID LIKE '%ORD%')
              AND ColID IS NOT NULL AND ColID <> '' AND ColID <> '0'
            ORDER BY VisualIndx ASC";
        cmdCprf.Parameters.Add(new SqlParameter("@UserSign", SqlDbType.SmallInt) { Value = (short)userSign });

        await using var reader = await cmdCprf.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var colId = reader.GetString(0);
            var visualIndx = reader.GetInt32(1);
            var visInForm = reader.IsDBNull(2) ? "Y" : reader.GetString(2).Trim();
            var width = reader.IsDBNull(3) ? 100 : reader.GetInt32(3);

            columnList.Add((colId, visualIndx, visInForm, width));
        }

        return columnList;
    }

    /// <summary>
    /// 世界级高性能多级缓存：获取辅助平台 [@Ch_Udo_Form] 原始设计拓扑、Tab页签、右侧物性参数、下拉选项与 CFL 穿透关联
    /// </summary>
    public async Task<ChUdoFormMetadataResult> GetUdoFormLayoutAsync(
        string companyId,
        string objectCode,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var cacheKey = $"{companyId}_{objectCode}".ToUpperInvariant();
        var now = DateTime.UtcNow;

        // Level 1: 内存高速缓存 (0ms)
        if (!forceRefresh && FormCache.TryGetValue(cacheKey, out var mem) && mem.ExpireAt > now)
        {
            return mem.Result;
        }

        // Level 2: 磁盘持久化快照 (毫秒级)
        var diskPath = Path.Combine(_cacheDirectory, $"form_{companyId}_{objectCode}.json");
        if (!forceRefresh && File.Exists(diskPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(diskPath, ct);
                var diskResult = JsonSerializer.Deserialize<ChUdoFormMetadataResult>(json, JsonOpts);
                if (diskResult != null)
                {
                    FormCache[cacheKey] = (now.AddMinutes(10), diskResult);
                    return diskResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SapMetadata] 读取磁盘表单快照失败: {Path}", diskPath);
            }
        }

        // Level 3: 从数据库直拉并流式解析 XML
        var result = new ChUdoFormMetadataResult
        {
            ObjectCode = objectCode,
            Title = $"{objectCode} 垦青单据"
        };

        var sapConnStr = GetSapDbConnectionString(companyId);
        if (string.IsNullOrWhiteSpace(sapConnStr)) return result;

        try
        {
            await using var conn = new SqlConnection(sapConnStr);
            await conn.OpenAsync(ct);

            string? title = null;
            int fWidth = 1200, fHeight = 577;
            string? foldersXml = null, formXml = null;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TOP 1 Title, fWidth, fHeight, Folders, FormXml 
                    FROM dbo.[@Ch_Udo_Form] 
                    WHERE ObjectType = @ObjCode 
                    ORDER BY CAST(Code as int) DESC";
                cmd.Parameters.Add(new SqlParameter("@ObjCode", SqlDbType.NVarChar) { Value = objectCode });

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    title = reader.IsDBNull(0) ? null : reader.GetString(0);
                    if (!reader.IsDBNull(1)) fWidth = reader.GetInt32(1);
                    if (!reader.IsDBNull(2)) fHeight = reader.GetInt32(2);
                    foldersXml = reader.IsDBNull(3) ? null : reader.GetString(3);
                    formXml = reader.IsDBNull(4) ? null : reader.GetString(4);
                }
            }

            var tabs = new List<FormFolderTabDto>();
            var headerFields = new List<FormItemFieldDto>();
            var qualitySpecs = new List<FormItemFieldDto>();
            var matrixCols = new List<FormMatrixColumnDto>();
            var linkedObjs = new Dictionary<string, FormCflLinkDto>(StringComparer.OrdinalIgnoreCase);
            var dropdowns = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // 1. 解析 Folders XML (Tab 页签)
            if (!string.IsNullOrWhiteSpace(foldersXml))
            {
                try
                {
                    var fDoc = XDocument.Parse(foldersXml);
                    foreach (var fNode in fDoc.Descendants("CUserFormFolderOBJ"))
                    {
                        var cap = (string?)fNode.Attribute("Caption") ?? "";
                        var act = (string?)fNode.Attribute("Activated") == "1";
                        if (int.TryParse((string?)fNode.Attribute("ManagerPane"), out var pId) && !string.IsNullOrWhiteSpace(cap) && !cap.Contains("未命名"))
                        {
                            tabs.Add(new FormFolderTabDto(pId, cap.Trim(), act));
                        }
                    }
                }
                catch { }
            }

            // 2. 高性能解析 FormXml
            if (!string.IsNullOrWhiteSpace(formXml))
            {
                try
                {
                    var xDoc = XDocument.Parse(formXml);

                    // 2.1 提取 ChooseFromList (主数据与单据穿透)
                    foreach (var cfl in xDoc.Descendants("ChooseFromList"))
                    {
                        var uId = (string?)cfl.Attribute("UniqueID");
                        var objType = (string?)cfl.Attribute("ObjectType") ?? "";
                        if (!string.IsNullOrWhiteSpace(uId))
                        {
                            var targetName = objType switch
                            {
                                "2" => "业务伙伴主数据 (OCRD)",
                                "4" => "物料主数据 (OITM)",
                                "17" => "销售订单 (ORDR)",
                                "23" => "销售报价单 (OQUT)",
                                "171" => "员工/业务员 (OHEM/OSLP)",
                                "112" => "草稿单据 (ODRF)",
                                _ => objType
                            };
                            linkedObjs[uId] = new FormCflLinkDto(objType, targetName);
                        }
                    }

                    // 2.2 提取 ComboBox 下拉字典
                    foreach (var cItem in xDoc.Descendants("item").Where(x => (string?)x.Attribute("type") == "113"))
                    {
                        var alias = (string?)cItem.Descendants("databind").FirstOrDefault()?.Attribute("alias");
                        if (!string.IsNullOrWhiteSpace(alias))
                        {
                            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var vNode in cItem.Descendants("ValidValue"))
                            {
                                var val = (string?)vNode.Attribute("value");
                                var desc = (string?)vNode.Attribute("description");
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    dict[val] = desc ?? val;
                                }
                            }
                            if (dict.Count > 0)
                            {
                                dropdowns[alias] = dict;
                                if (!alias.StartsWith("U_")) dropdowns["U_" + alias] = dict;
                            }
                        }
                    }

                    // 2.4 提取 Matrix / Grid 表格控件 (type="127"，如 u50 表格) 的物理列顺序、绑定字段与列宽
                    var mIndex = 0;
                    foreach (var mItem in xDoc.Descendants("item").Where(x => (string?)x.Attribute("type") == "127"))
                    {
                        var specific = mItem.Descendants("specific").FirstOrDefault();
                        if (specific != null)
                        {
                            foreach (var colNode in specific.Descendants("column"))
                            {
                                var colUid = (string?)colNode.Attribute("uid") ?? "";
                                var colAlias = (string?)colNode.Descendants("databind").FirstOrDefault()?.Attribute("alias")
                                               ?? (string?)colNode.Attribute("alias")
                                               ?? (string?)colNode.Attribute("val_on")
                                               ?? colUid;

                                var colTitle = (string?)colNode.Attribute("title")
                                               ?? (string?)colNode.Attribute("description")
                                               ?? (string?)colNode.Attribute("caption")
                                               ?? colAlias;

                                int.TryParse((string?)colNode.Attribute("width"), out var colWidth);
                                var isVis = (string?)colNode.Attribute("visible") != "0" && colWidth > 0;

                                if (!string.IsNullOrWhiteSpace(colAlias) && colAlias != "0" && colAlias != "#")
                                {
                                    matrixCols.Add(new FormMatrixColumnDto(colUid, colAlias, colTitle.Trim(), colWidth, isVis, mIndex++));
                                }
                            }
                        }
                    }

                    // 2.3 提取 Items 并智能匹配 Label 与坐标
                    var labelsByUid = new Dictionary<string, string>();
                    var labelsByLinkTo = new Dictionary<string, string>();

                    foreach (var lItem in xDoc.Descendants("item").Where(x => (string?)x.Attribute("type") == "8"))
                    {
                        var lUid = (string?)lItem.Attribute("uid") ?? "";
                        var cap = (string?)lItem.Descendants("specific").FirstOrDefault()?.Attribute("caption") ?? "";
                        var linkTo = (string?)lItem.Attribute("linkto") ?? "";
                        if (!string.IsNullOrWhiteSpace(cap))
                        {
                            labelsByUid[lUid] = cap.Trim();
                            if (!string.IsNullOrWhiteSpace(linkTo))
                            {
                                labelsByLinkTo[linkTo] = cap.Trim();
                            }
                        }
                    }

                    foreach (var item in xDoc.Descendants("item"))
                    {
                        var uid = (string?)item.Attribute("uid") ?? "";
                        var typeStr = (string?)item.Attribute("type") ?? "0";
                        int.TryParse(typeStr, out var iType);
                        if (iType == 4 || iType == 8 || iType == 99 || iType == 127) continue;

                        var alias = (string?)item.Descendants("databind").FirstOrDefault()?.Attribute("alias") ?? "";
                        if (string.IsNullOrWhiteSpace(alias) && uid.StartsWith("u")) alias = uid;

                        int.TryParse((string?)item.Attribute("top"), out var top);
                        int.TryParse((string?)item.Attribute("left"), out var left);
                        int.TryParse((string?)item.Attribute("width"), out var width);
                        int.TryParse((string?)item.Attribute("height"), out var height);
                        int.TryParse((string?)item.Attribute("from_pane"), out var pane);

                        var label = (string?)item.Descendants("specific").FirstOrDefault()?.Attribute("caption");
                        if (string.IsNullOrWhiteSpace(label) && labelsByLinkTo.TryGetValue(uid, out var linkedLabel)) label = linkedLabel;
                        if (string.IsNullOrWhiteSpace(label)) label = alias;

                        string? linkedObjCode = null;
                        if (linkedObjs.TryGetValue(uid, out var cfl))
                        {
                            linkedObjCode = cfl.ObjectType;
                        }

                        var fieldDto = new FormItemFieldDto(uid, alias, label, iType, top, left, width, height, pane, linkedObjCode);

                        // 右侧物性与工艺质量专区 (Left >= 850)
                        if (left >= 850 && pane == 0)
                        {
                            qualitySpecs.Add(fieldDto);
                        }
                        else if (pane == 0 && top < 120)
                        {
                            headerFields.Add(fieldDto);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SapMetadata] 解析 FormXml 失败");
                }
            }

            result = new ChUdoFormMetadataResult
            {
                ObjectCode = objectCode,
                Title = title ?? $"{objectCode} 垦青单据",
                FormWidth = fWidth,
                FormHeight = fHeight,
                Tabs = tabs,
                HeaderFields = headerFields.OrderBy(x => x.Top).ThenBy(x => x.Left).ToList(),
                QualitySpecsFields = qualitySpecs.OrderBy(x => x.Top).ThenBy(x => x.Left).ToList(),
                MatrixColumns = matrixCols,
                LinkedObjects = linkedObjs,
                Dropdowns = dropdowns
            };

            // 存入多级缓存 (内存 + 磁盘)
            FormCache[cacheKey] = (now.AddMinutes(10), result);
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                await File.WriteAllTextAsync(diskPath, JsonSerializer.Serialize(result, JsonOpts), ct);
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SapMetadata] 查询 @Ch_Udo_Form 布局失败: {ObjectCode}", objectCode);
        }

        return result;
    }

    private string? GetSapDbConnectionString(string companyId)
    {
        var connStr = _config.GetConnectionString("ApprovalDbConnection");
        if (string.IsNullOrWhiteSpace(connStr)) return null;

        var builder = new SqlConnectionStringBuilder(connStr)
        {
            InitialCatalog = companyId
        };
        return builder.ConnectionString;
    }
}
