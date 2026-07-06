namespace RWVDCS.Runtime;

/// <summary>交叉引用条目：某块的某管脚对某点的一次读/写。</summary>
public sealed record XrefEntry(string DpuName, string BlockName, string FcName, string PinName, bool Reversed, bool IsDead);

/// <summary>
/// 交叉引用索引（点名 → 写它的输出管脚（生产者） / 读它的输入管脚（消费者））。
/// 从构建期解析好的绑定表汇总，装配/下装换代后重建。
/// 用途：Web 界面 PointInfo 的 cross reference——输入管脚跳到源头输出管脚，
/// 输出管脚/点列出全部使用方。
/// </summary>
public sealed class XrefIndex
{
    private readonly Dictionary<string, List<XrefEntry>> _producers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<XrefEntry>> _consumers = new(StringComparer.OrdinalIgnoreCase);

    public XrefIndex(DcsRuntime runtime)
    {
        foreach (var dpu in runtime.Dpus)
        {
            foreach (var cmd in dpu.Commands)
            {
                foreach (var b in cmd.Inputs)
                {
                    Add(_consumers, b.PointName, new XrefEntry(
                        dpu.Name, cmd.Name, cmd.FcName, b.Pin.Field.Name, b.Reversed,
                        b.Source is not { IsRealPoint: true }));
                }
                foreach (var b in cmd.Outputs)
                {
                    Add(_producers, b.PointName, new XrefEntry(
                        dpu.Name, cmd.Name, cmd.FcName, b.Pin.Field.Name, b.Reversed,
                        b.Target is not { IsRealPoint: true }));
                }
            }
        }
    }

    private static void Add(Dictionary<string, List<XrefEntry>> map, string pointName, XrefEntry entry)
    {
        if (!map.TryGetValue(pointName, out var list))
            map[pointName] = list = [];
        list.Add(entry);
    }

    /// <summary>写该点的输出管脚（源头；正常工程 0 或 1 个，异常工程可能多写）。</summary>
    public IReadOnlyList<XrefEntry> ProducersOf(string pointName)
        => _producers.TryGetValue(pointName, out var list) ? list : [];

    /// <summary>读该点的输入管脚（使用方）。</summary>
    public IReadOnlyList<XrefEntry> ConsumersOf(string pointName)
        => _consumers.TryGetValue(pointName, out var list) ? list : [];
}
