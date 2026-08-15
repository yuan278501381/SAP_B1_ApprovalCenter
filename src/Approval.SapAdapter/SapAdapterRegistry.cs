using Approval.Application.Common.Interfaces;

namespace Approval.SapAdapter;

public class SapAdapterRegistry : ISapAdapterRegistry
{
    private readonly IEnumerable<ISapObjectAdapter> _adapters;

    public SapAdapterRegistry(IEnumerable<ISapObjectAdapter> adapters)
    {
        _adapters = adapters;
    }

    public ISapObjectAdapter GetAdapter(string objectCode)
    {
        var adapter = _adapters.FirstOrDefault(a => a.SupportedObjectCode.Equals(objectCode, StringComparison.OrdinalIgnoreCase));
        if (adapter == null)
        {
            throw new NotSupportedException($"暂不支持业务对象类型: {objectCode}，请检查适配器注册");
        }
        return adapter;
    }
}
