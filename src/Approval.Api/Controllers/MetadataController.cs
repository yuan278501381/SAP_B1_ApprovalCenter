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
    /// 获取指定单据/主数据对象的全量动态字段中英文定义与下拉有效值翻译字典
    /// </summary>
    [HttpGet("objects/{objectCode}")]
    public async Task<ActionResult<ApiResponse<ObjectMetadataResult>>> GetObjectMetadata(
        string objectCode,
        [FromQuery] string companyId = "DB_KCC",
        CancellationToken ct = default)
    {
        var meta = await _metadataService.GetObjectMetadataAsync(companyId, objectCode, ct);
        return Ok(ApiResponse<ObjectMetadataResult>.Ok(meta, _traceContext.TraceId));
    }
}
