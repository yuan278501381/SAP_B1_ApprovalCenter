using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Approval.Domain.Services;

/// <summary>
/// 规范化快照生成与 SHA-256 防篡改签名构建器
/// </summary>
public static class CanonicalSnapshotBuilder
{
    // 免审白名单字段 —— 修改这些字段不会触发重新审批
    private static readonly HashSet<string> NonSensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Comments", "U_Comments", "Remark", "U_Remark",
        "U_PrintCount", "PrintCount", "U_Memo",
        "UpdateDate", "UpdateTime", "CreateDate", "CreateTime",
        "DocDate", "DocDueDate", "TaxDate"
    };
    /// <summary>
    /// 将任意 JSON 结构或对象转换为排好序的规范化 JSON (Canonical JSON) 并生成 SHA-256 签名哈希
    /// </summary>
    /// <param name="rawJson">原始 JSON 字符串</param>
    /// <returns>(规范化JSON, SHA-256指纹)</returns>
    public static (string CanonicalJson, string Sha256Hash) Build(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new ArgumentException("原始单据数据不能为空", nameof(rawJson));
        }

        var node = JsonNode.Parse(rawJson);
        if (node == null)
        {
            throw new InvalidOperationException("无法解析原始单据 JSON");
        }

        var sortedNode = SortJsonNode(node);
        var options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        var canonicalJson = sortedNode?.ToJsonString(options) ?? "{}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        var sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return (canonicalJson, sha256Hash);
    }

    /// <summary>
    /// 递归对 JsonNode 的属性按键名字典序排序
    /// </summary>
    private static JsonNode? SortJsonNode(JsonNode? node)
    {
        if (node == null) return null;

        if (node is JsonObject obj)
        {
            var sortedObj = new JsonObject();
            var sortedProperties = obj
                .Where(p => !NonSensitiveFields.Contains(p.Key))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var prop in sortedProperties)
            {
                sortedObj.Add(prop.Key, SortJsonNode(prop.Value?.DeepClone()));
            }
            return sortedObj;
        }

        if (node is JsonArray arr)
        {
            var sortedArr = new JsonArray();
            foreach (var item in arr)
            {
                sortedArr.Add(SortJsonNode(item?.DeepClone()));
            }
            return sortedArr;
        }

        return node.DeepClone();
    }
}
