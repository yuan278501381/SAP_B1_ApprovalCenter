using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Approval.Api.Health;

/// <summary>SAP Service Layer 连通性健康探针</summary>
public class SapServiceLayerHealthCheck : IHealthCheck
{
    private readonly string _baseUrl;

    public SapServiceLayerHealthCheck(IConfiguration configuration)
    {
        _baseUrl = configuration["ServiceLayer:BaseUrl"] ?? "https://127.0.0.1:50000/b1s/v1/";
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(_baseUrl, ct);
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? HealthCheckResult.Healthy($"SAP Service Layer 可达 ({response.StatusCode})")
                : HealthCheckResult.Degraded($"SAP Service Layer 响应异常 ({response.StatusCode})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SAP Service Layer 不可达", ex);
        }
    }
}
