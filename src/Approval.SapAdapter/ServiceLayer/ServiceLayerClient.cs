using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Approval.SapAdapter.ServiceLayer;

/// <summary>SAP B1 Service Layer 会话客户端；401 时自动重新登录一次。</summary>
public sealed class ServiceLayerClient : IDisposable
{
    private readonly ServiceLayerOptions _options;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private volatile bool _loggedIn;
    private string? _sessionId;
    private string? _routeId = ".node1";

    public ServiceLayerClient(ServiceLayerOptions options)
    {
        _options = options;
        ValidateOptions(options);
        var handler = new HttpClientHandler();
        if (options.AllowInvalidServerCertificate)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public string CompanyDb => _options.CompanyDb;
    public bool MirrorEnabled => _options.MirrorEnabled;

    public async Task<string> GetRawAsync(ServiceLayerObjectOptions mapping, string objectKey, CancellationToken ct)
    {
        using var response = await SendWithReloginAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildObjectPath(mapping, objectKey)), ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, raw);
        return raw;
    }

    public async Task PatchMirrorAsync(
        ServiceLayerObjectOptions mapping,
        string objectKey,
        string status,
        string instanceId,
        string hash,
        CancellationToken ct)
    {
        if (!_options.MirrorEnabled)
            throw new InvalidOperationException("SapAdapter:ServiceLayer:MirrorEnabled=false，已禁止真实 SAP 回写");
        var body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [mapping.StatusField] = status,
            [mapping.InstanceIdField] = instanceId,
            [mapping.HashField] = hash
        });
        using var response = await SendWithReloginAsync(() => new HttpRequestMessage(HttpMethod.Patch, BuildObjectPath(mapping, objectKey))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody);
    }

    private async Task<HttpResponseMessage> SendWithReloginAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        await EnsureLoginAsync(ct);
        var req = requestFactory();
        if (!string.IsNullOrEmpty(_sessionId))
        {
            req.Headers.Add("Cookie", $"B1SESSION={_sessionId}; ROUTEID={_routeId ?? ".node1"}");
        }
        var response = await _http.SendAsync(req, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        _loggedIn = false;
        _sessionId = null;
        await EnsureLoginAsync(ct);
        var retryReq = requestFactory();
        if (!string.IsNullOrEmpty(_sessionId))
        {
            retryReq.Headers.Add("Cookie", $"B1SESSION={_sessionId}; ROUTEID={_routeId ?? ".node1"}");
        }
        return await _http.SendAsync(retryReq, ct);
    }

    private async Task EnsureLoginAsync(CancellationToken ct)
    {
        if (_loggedIn && !string.IsNullOrEmpty(_sessionId)) return;
        await _loginLock.WaitAsync(ct);
        try
        {
            if (_loggedIn && !string.IsNullOrEmpty(_sessionId)) return;
            var loginPayload = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["CompanyDB"] = _options.CompanyDb,
                ["UserName"] = _options.UserName,
                ["Password"] = _options.Password
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, "Login")
            {
                Content = new StringContent(loginPayload, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            EnsureSuccess(response, body);
            
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("SessionId", out var sidProp))
                {
                    _sessionId = sidProp.GetString();
                }
            }
            catch
            {
                // ignore
            }

            _loggedIn = true;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private static string BuildObjectPath(ServiceLayerObjectOptions mapping, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(mapping.EntitySet))
            throw new InvalidOperationException($"对象 {mapping.ObjectCode} 未配置 Service Layer EntitySet");
        var key = mapping.KeyType.Equals("String", StringComparison.OrdinalIgnoreCase)
            ? $"'{objectKey.Replace("'", "''", StringComparison.Ordinal)}'"
            : long.TryParse(objectKey, out _) ? objectKey : throw new ArgumentException($"对象键 {objectKey} 不是数字");
        return $"{mapping.EntitySet}({key})";
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        var safeBody = body.Length > 1000 ? body[..1000] : body;
        throw new HttpRequestException($"SAP Service Layer 返回 {(int)response.StatusCode} {response.ReasonPhrase}: {safeBody}");
    }

    private static void ValidateOptions(ServiceLayerOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _)) throw new InvalidOperationException("Service Layer BaseUrl 无效");
        if (string.IsNullOrWhiteSpace(options.CompanyDb)) throw new InvalidOperationException("Service Layer CompanyDb 未配置");
        if (string.IsNullOrWhiteSpace(options.UserName) || string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("Service Layer 凭据未配置；请使用环境变量或 Secret Store，禁止提交到仓库");
    }

    public void Dispose()
    {
        _http.Dispose();
        _loginLock.Dispose();
    }
}
