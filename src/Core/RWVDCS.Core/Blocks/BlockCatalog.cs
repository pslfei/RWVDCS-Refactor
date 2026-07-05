using System.Collections.Frozen;
using System.Reflection;

namespace RWVDCS.Core.Blocks;

/// <summary>
/// 功能码目录：扫描块程序集，建立 FCName → 块类型 的映射（大小写不敏感，与老系统插件目录一致）。
/// </summary>
public sealed class BlockCatalog
{
    private readonly FrozenDictionary<string, Type> _byFcName;

    public BlockCatalog(params Assembly[] assemblies)
    {
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || !typeof(Function).IsAssignableFrom(type))
                    continue;
                var attr = type.GetCustomAttribute<FCNameAttribute>(inherit: false);
                if (attr == null)
                    continue;
                map[attr.FCName] = type;
            }
        }
        _byFcName = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _byFcName.Count;

    public bool TryGet(string fcName, out Type type) => _byFcName.TryGetValue(fcName, out type!);

    public Type Get(string fcName) =>
        _byFcName.TryGetValue(fcName, out var t)
            ? t
            : throw new KeyNotFoundException($"功能码 {fcName} 不在块目录中");

    public IEnumerable<KeyValuePair<string, Type>> All => _byFcName;

    public Function CreateInstance(string fcName)
    {
        var fc = (Function)Activator.CreateInstance(Get(fcName))!;
        fc.FcName = fcName;
        return fc;
    }
}
