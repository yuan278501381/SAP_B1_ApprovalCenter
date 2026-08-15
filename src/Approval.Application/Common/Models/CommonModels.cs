using System.Diagnostics.CodeAnalysis;

namespace Approval.Application.Common.Models;

/// <summary>
/// 从 SAP 抓取的单据规范数据包
/// </summary>
[ExcludeFromCodeCoverage]
public class SapObjectPayload
{
    public string CompanyId { get; set; } = string.Empty;
    public string ObjectCode { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CreatorUserCode { get; set; } = string.Empty;
    public string? CreatorUserName { get; set; }
    public decimal DocTotal { get; set; }
    public string RawJson { get; set; } = "{}";
    public Dictionary<string, object?> HeaderFields { get; set; } = new();
    public List<Dictionary<string, object?>> LineRows { get; set; } = new();
}

/// <summary>
/// 统一 API 响应包装体 (包含 TraceID 与结构化状态)
/// </summary>
[ExcludeFromCodeCoverage]
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Code { get; set; } = "OK";
    public string Message { get; set; } = "成功";
    public T? Data { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string traceId = "") => new()
    {
        Success = true,
        Code = "OK",
        Message = "成功",
        Data = data,
        TraceId = traceId
    };

    public static ApiResponse<T> Fail(string code, string message, string traceId = "") => new()
    {
        Success = false,
        Code = code,
        Message = message,
        Data = default,
        TraceId = traceId
    };
}
