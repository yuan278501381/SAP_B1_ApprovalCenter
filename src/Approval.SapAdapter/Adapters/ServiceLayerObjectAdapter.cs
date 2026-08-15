using System.Text.Json;
using Approval.Application.Common.Interfaces;
using Approval.Application.Common.Models;
using Approval.SapAdapter.ServiceLayer;

namespace Approval.SapAdapter.Adapters;

public sealed class ServiceLayerObjectAdapter : ISapObjectAdapter
{
    private readonly ServiceLayerClient _client;
    private readonly ServiceLayerObjectOptions _mapping;

    public ServiceLayerObjectAdapter(ServiceLayerClient client, ServiceLayerObjectOptions mapping)
    {
        _client = client;
        _mapping = mapping;
    }

    public string SupportedObjectCode => _mapping.ObjectCode;

    public async Task<SapObjectPayload> FetchObjectAsync(string companyId, string objectKey, CancellationToken ct = default)
    {
        EnsureCompany(companyId);
        var raw = await _client.GetRawAsync(_mapping, objectKey, ct);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var header = new Dictionary<string, object?>();
        foreach (var p in root.EnumerateObject())
            if (p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                header[p.Name] = JsonSerializer.Deserialize<object>(p.Value.GetRawText());

        var lines = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(_mapping.LineCollection) &&
            TryGetProperty(root, _mapping.LineCollection, out var lineArray) && lineArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in lineArray.EnumerateArray())
            {
                var values = new Dictionary<string, object?>();
                foreach (var p in row.EnumerateObject())
                    if (p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                        values[p.Name] = JsonSerializer.Deserialize<object>(p.Value.GetRawText());
                lines.Add(values);
            }
        }

        return new SapObjectPayload
        {
            CompanyId = companyId,
            ObjectCode = SupportedObjectCode,
            ObjectKey = objectKey,
            Title = ReadText(root, _mapping.TitleField) ?? $"{SupportedObjectCode} #{objectKey}",
            CreatorUserCode = ReadText(root, _mapping.CreatorCodeField) ?? "unknown",
            CreatorUserName = string.IsNullOrWhiteSpace(_mapping.CreatorNameField) ? null : ReadText(root, _mapping.CreatorNameField),
            DocTotal = ReadDecimal(root, _mapping.DocTotalField),
            RawJson = raw,
            HeaderFields = header,
            LineRows = lines
        };
    }

    public async Task<bool> WriteApprovalMirrorAsync(
        string companyId, string objectKey, string approvalStatus, string instanceId, string dataHash,
        CancellationToken ct = default)
    {
        EnsureCompany(companyId);
        await _client.PatchMirrorAsync(_mapping, objectKey, approvalStatus, instanceId, dataHash, ct);
        return true;
    }

    private void EnsureCompany(string companyId)
    {
        if (!companyId.Equals(_client.CompanyDb, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"请求公司 {companyId} 与 Service Layer 会话公司 {_client.CompanyDb} 不一致");
    }

    private static string? ReadText(JsonElement root, string? name) =>
        !string.IsNullOrWhiteSpace(name) && TryGetProperty(root, name, out var value) ? value.ToString() : null;

    private static decimal ReadDecimal(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.TryGetDecimal(out var number) ? number : 0m;

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }
}
