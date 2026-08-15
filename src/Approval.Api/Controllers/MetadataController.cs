using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace Approval.Api.Controllers;

[ApiController]
[Route("api/v1/metadata")]
public class MetadataController : ControllerBase
{
    private readonly ISapMetadataService _metadataService;
    private readonly ITraceContext _traceContext;

    public MetadataController(ISapMetadataService metadataService, ITraceContext traceContext)
    {
        _metadataService = metadataService;
        _traceContext = traceContext;
    }

    /// <summary>
    /// 获取当前 SAP 业务账套/公司真实名称与基本信息 (从 OADM 读取 CompnyName)
    /// </summary>
    [HttpGet("company")]
    public async Task<ActionResult<ApiResponse<CompanyInfoResult>>> GetCompanyInfo(
        [FromQuery] string companyId = "DB_KCC",
        CancellationToken ct = default)
    {
        var info = await _metadataService.GetCompanyInfoAsync(companyId, ct);
        return Ok(ApiResponse<CompanyInfoResult>.Ok(info, _traceContext.TraceId));
    }

    /// <summary>
    /// 获取指定单据/主数据对象的全量动态字段中英文定义与下拉有效值翻译字典 (支持多级缓存与按需强制刷新)
    /// </summary>
    [HttpGet("objects/{objectCode}")]
    public async Task<ActionResult<ApiResponse<ObjectMetadataResult>>> GetObjectMetadata(
        string objectCode,
        [FromQuery] string companyId = "DB_KCC",
        [FromQuery] bool refresh = false,
        CancellationToken ct = default)
    {
        var meta = await _metadataService.GetObjectMetadataAsync(companyId, objectCode, refresh, ct);
        return Ok(ApiResponse<ObjectMetadataResult>.Ok(meta, _traceContext.TraceId));
    }

    /// <summary>
    /// 手动即时触发全量 SAP 元数据与动态系统字典 (OEXD/OSLP/OCTG/OHEM/CUFD/UFD1) 刷新并持久化落盘
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<string>>> RefreshAllMetadata(
        [FromQuery] string companyId = "DB_KCC",
        CancellationToken ct = default)
    {
        await _metadataService.RefreshAllMetadataAndSaveToDiskAsync(companyId, ct);
        return Ok(ApiResponse<string>.Ok($"成功完成账套 [{companyId}] 全量元数据与系统字典刷新并落盘", _traceContext.TraceId));
    }
}
