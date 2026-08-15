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
        var environment = Context.RequestServices.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            var configuration = Context.RequestServices.GetRequiredService<IConfiguration>();
            var expected = configuration["Identity:GatewaySharedSecret"];
            var supplied = Request.Headers[GatewaySecretHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(expected))
                return Task.FromResult(AuthenticateResult.Fail("生产环境未配置 Identity:GatewaySharedSecret"));
            if (string.IsNullOrWhiteSpace(supplied) || !FixedTimeEquals(expected, supplied))
                return Task.FromResult(AuthenticateResult.Fail("请求不是来自受信任认证网关"));
        }

        var userCode = Request.Headers[UserHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(userCode))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userName = Request.Headers[NameHeader].FirstOrDefault()?.Trim();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userCode),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(userName) ? userCode : userName)
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
