using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Approval.Api.Security;

/// <summary>
/// 接收由受信任反向代理注入的用户身份。生产环境必须阻断客户端直连 API，
/// 并由代理先完成统一认证、删除外部同名请求头后再注入这些头。
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class TrustedHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TrustedGatewayHeader";
    public const string UserHeader = "X-Approval-User";
    public const string NameHeader = "X-Approval-User-Name";
    public const string GatewaySecretHeader = "X-Approval-Gateway-Secret";

    public TrustedHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuration = Context.RequestServices.GetRequiredService<IConfiguration>();
        var gatewaySecret = configuration["Identity:GatewaySharedSecret"];

        // 1. 若显式配置了 GatewaySharedSecret，强制执行受信任网关秘钥核验 (生产反向代理模式)
        if (!string.IsNullOrWhiteSpace(gatewaySecret))
        {
            var supplied = Request.Headers[GatewaySecretHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(supplied) || !FixedTimeEquals(gatewaySecret, supplied))
            {
                return Task.FromResult(AuthenticateResult.Fail("请求未携带合法的受信任认证网关秘钥 (X-Approval-Gateway-Secret)"));
            }
        }

        // 2. 优先从 Header 获取用户标识，其次从 URL Query 参数 (适配 SAP 客户端内嵌 Web 控件传参)
        var userCode = Request.Headers[UserHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(userCode))
        {
            userCode = Request.Query["user"].FirstOrDefault()?.Trim()
                    ?? Request.Query["userCode"].FirstOrDefault()?.Trim();
        }

        // 3. 若仍未提供，检查是否显式配置了开发/演示默认兜底用户 (Identity:DefaultUserCode)
        if (string.IsNullOrWhiteSpace(userCode))
        {
            var defaultUser = configuration["Identity:DefaultUserCode"];
            if (!string.IsNullOrWhiteSpace(defaultUser))
            {
                userCode = defaultUser;
            }
        }

        // 4. 若最终仍无合法用户身份，返回 NoResult 触发 [Authorize] 的 401 安全拦截
        if (string.IsNullOrWhiteSpace(userCode))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userName = Request.Headers[NameHeader].FirstOrDefault()?.Trim()
                    ?? Request.Query["userName"].FirstOrDefault()?.Trim()
                    ?? userCode;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userCode),
            new(ClaimTypes.Name, userName)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
