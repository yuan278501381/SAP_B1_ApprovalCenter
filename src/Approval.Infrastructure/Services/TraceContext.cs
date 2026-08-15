using Approval.Application.Common.Interfaces;

namespace Approval.Infrastructure.Services;

public class TraceContext : ITraceContext
{
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ClientIp { get; set; } = "127.0.0.1";
    public string? CurrentUserCode { get; set; }
}
