using System.Diagnostics.CodeAnalysis;

namespace Approval.SapAdapter.ServiceLayer;

[ExcludeFromCodeCoverage]
public sealed class ServiceLayerOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string CompanyDb { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool AllowInvalidServerCertificate { get; set; }
    public bool MirrorEnabled { get; set; }
    public List<ServiceLayerObjectOptions> Objects { get; set; } = new();
}

[ExcludeFromCodeCoverage]
public sealed class ServiceLayerObjectOptions
{
    public string ObjectCode { get; set; } = string.Empty;
    public string EntitySet { get; set; } = string.Empty;
    public string KeyType { get; set; } = "Number"; // Number / String
    public string TitleField { get; set; } = "DocNum";
    public string DocTotalField { get; set; } = "DocTotal";
    public string CreatorCodeField { get; set; } = "Creator";
    public string? CreatorNameField { get; set; }
    public string? LineCollection { get; set; }
    public string StatusField { get; set; } = "U_APStatus";
    public string InstanceIdField { get; set; } = "U_APInstance";
    public string HashField { get; set; } = "U_APHash";
}
