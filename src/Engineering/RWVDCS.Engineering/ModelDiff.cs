namespace RWVDCS.Engineering;

/// <summary>差异条目类别。</summary>
public enum DiffKind
{
    ControllerAdded,
    ControllerRemoved,
    PointAdded,
    PointRemoved,
    PointChanged,       // 类型/默认值/量程变化
    BlockAdded,
    BlockRemoved,
    BlockTypeChanged,   // 功能码变了（视为删旧建新：状态无法保留）
    BlockWiringChanged, // 管脚连线（PointName/取反）变化
    BlockParamChanged,  // 规格参数/管脚默认值变化
}

/// <summary>单条差异。</summary>
public sealed record DiffEntry(DiffKind Kind, string Controller, string Name, string Detail)
{
    /// <summary>下装时是否有状态丢失风险（删除/类型变化）。</summary>
    public bool IsDestructive => Kind is DiffKind.ControllerRemoved or DiffKind.PointRemoved
        or DiffKind.BlockRemoved or DiffKind.BlockTypeChanged or DiffKind.PointChanged;
}

/// <summary>
/// 工程模型差异报告（在线下装的 prepare 输出）。
/// 语义对齐成熟 DCS 的 download 预检报告（DeltaV "download with verify" / CODESYS online change 的变更清单）。
/// </summary>
public sealed class ModelDiffReport
{
    public List<DiffEntry> Entries { get; } = [];

    public int PointsAdded => Count(DiffKind.PointAdded);
    public int PointsRemoved => Count(DiffKind.PointRemoved);
    public int PointsChanged => Count(DiffKind.PointChanged);
    public int BlocksAdded => Count(DiffKind.BlockAdded);
    public int BlocksRemoved => Count(DiffKind.BlockRemoved);
    public int BlocksTypeChanged => Count(DiffKind.BlockTypeChanged);
    public int BlocksWiringChanged => Count(DiffKind.BlockWiringChanged);
    public int BlocksParamChanged => Count(DiffKind.BlockParamChanged);
    public int ControllersAdded => Count(DiffKind.ControllerAdded);
    public int ControllersRemoved => Count(DiffKind.ControllerRemoved);

    public bool IsEmpty => Entries.Count == 0;

    public bool HasDestructiveChanges => Entries.Any(e => e.IsDestructive);

    private int Count(DiffKind k) => Entries.Count(e => e.Kind == k);
}

/// <summary>
/// 工程模型差异计算：old（运行中）vs new（待下装）。
/// 控制器按名匹配，点/块按名匹配（与运行时名字表同为大小写不敏感）。
/// 必须用<b>纯净模型</b>（装载原样，未经装配改写）做比较。
/// </summary>
public static class ModelDiff
{
    public static ModelDiffReport Compare(EngineeringModel oldModel, EngineeringModel newModel)
    {
        var report = new ModelDiffReport();

        var oldCtrls = oldModel.Controllers.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var newCtrls = newModel.Controllers.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in oldCtrls.Keys.Except(newCtrls.Keys, StringComparer.OrdinalIgnoreCase))
            report.Entries.Add(new DiffEntry(DiffKind.ControllerRemoved, name, name, "控制器被删除"));
        foreach (var name in newCtrls.Keys.Except(oldCtrls.Keys, StringComparer.OrdinalIgnoreCase))
            report.Entries.Add(new DiffEntry(DiffKind.ControllerAdded, name, name, "新增控制器"));

        foreach (var (name, oldC) in oldCtrls)
        {
            if (!newCtrls.TryGetValue(name, out var newC))
                continue;
            ComparePoints(oldC, newC, report);
            CompareBlocks(oldC, newC, report);
        }

        return report;
    }

    private static void ComparePoints(ControllerModel oldC, ControllerModel newC, ModelDiffReport report)
    {
        // 与装配一致：重名后行无效（seenNames 跳过），取首见
        var oldPts = FirstByName(oldC.Points, p => p.Name);
        var newPts = FirstByName(newC.Points, p => p.Name);

        foreach (var (name, op) in oldPts)
        {
            if (!newPts.TryGetValue(name, out var np))
            {
                report.Entries.Add(new DiffEntry(DiffKind.PointRemoved, oldC.Name, name, $"点被删除（{op.DataType}）"));
                continue;
            }

            if (!string.Equals(op.DataType, np.DataType, StringComparison.Ordinal))
            {
                report.Entries.Add(new DiffEntry(DiffKind.PointChanged, oldC.Name, name,
                    $"数据类型 {op.DataType} → {np.DataType}（旧值无法保留）"));
                continue;
            }

            var changes = new List<string>();
            if (ProjectFingerprint.FormatValue(op.DefaultValue) != ProjectFingerprint.FormatValue(np.DefaultValue))
                changes.Add($"默认值 {ProjectFingerprint.FormatValue(op.DefaultValue)}→{ProjectFingerprint.FormatValue(np.DefaultValue)}");
            if (op.MaxValue != np.MaxValue)
                changes.Add($"量程上限 {op.MaxValue}→{np.MaxValue}");
            if (op.MinValue != np.MinValue)
                changes.Add($"量程下限 {op.MinValue}→{np.MinValue}");
            if (op.LowAlarmLimit1Value != np.LowAlarmLimit1Value)
                changes.Add($"低报警一级限值 {op.LowAlarmLimit1Value}→{np.LowAlarmLimit1Value}");
            if (op.LowAlarmLimit2Value != np.LowAlarmLimit2Value)
                changes.Add($"低报警二级限值 {op.LowAlarmLimit2Value}→{np.LowAlarmLimit2Value}");
            if (op.LowAlarmLimit3Value != np.LowAlarmLimit3Value)
                changes.Add($"低报警三级限值 {op.LowAlarmLimit3Value}→{np.LowAlarmLimit3Value}");
            if (op.HighAlarmLimit1Value != np.HighAlarmLimit1Value)
                changes.Add($"高报警一级限值 {op.HighAlarmLimit1Value}→{np.HighAlarmLimit1Value}");
            if (op.HighAlarmLimit2Value != np.HighAlarmLimit2Value)
                changes.Add($"高报警二级限值 {op.HighAlarmLimit2Value}→{np.HighAlarmLimit2Value}");
            if (op.HighAlarmLimit3Value != np.HighAlarmLimit3Value)
                changes.Add($"高报警三级限值 {op.HighAlarmLimit3Value}→{np.HighAlarmLimit3Value}");
            if (!string.Equals(op.Description, np.Description, StringComparison.Ordinal))
                changes.Add($"描述 {op.Description}→{np.Description}");
            if (!string.Equals(op.Unit, np.Unit, StringComparison.Ordinal))
                changes.Add($"单位 {op.Unit}→{np.Unit}");
            if (changes.Count > 0)
                report.Entries.Add(new DiffEntry(DiffKind.PointChanged, oldC.Name, name, string.Join("；", changes)));
        }

        foreach (var (name, np) in newPts)
        {
            if (!oldPts.ContainsKey(name))
                report.Entries.Add(new DiffEntry(DiffKind.PointAdded, oldC.Name, name, $"新增点（{np.DataType}）"));
        }
    }

    private static void CompareBlocks(ControllerModel oldC, ControllerModel newC, ModelDiffReport report)
    {
        var oldBlocks = FirstByName(oldC.Blocks, b => b.Name);
        var newBlocks = FirstByName(newC.Blocks, b => b.Name);

        foreach (var (name, ob) in oldBlocks)
        {
            if (!newBlocks.TryGetValue(name, out var nb))
            {
                report.Entries.Add(new DiffEntry(DiffKind.BlockRemoved, oldC.Name, name, $"块被删除（{ob.FcName}）"));
                continue;
            }

            if (!string.Equals(ob.FcName, nb.FcName, StringComparison.OrdinalIgnoreCase))
            {
                report.Entries.Add(new DiffEntry(DiffKind.BlockTypeChanged, oldC.Name, name,
                    $"功能码 {ob.FcName} → {nb.FcName}（按删旧建新处理，状态不保留）"));
                continue;
            }

            CompareBlockPins(oldC.Name, ob, nb, report);
        }

        foreach (var (name, nb) in newBlocks)
        {
            if (!oldBlocks.ContainsKey(name))
                report.Entries.Add(new DiffEntry(DiffKind.BlockAdded, oldC.Name, name, $"新增块（{nb.FcName}）"));
        }
    }

    private static void CompareBlockPins(string ctrl, BlockModel ob, BlockModel nb, ModelDiffReport report)
    {
        // 管脚按名首见（与 Command ctor 的 details.TryAdd 一致，大小写不敏感）
        var oldPins = FirstByName(ob.Pins, p => p.PinName);
        var newPins = FirstByName(nb.Pins, p => p.PinName);

        var wiring = new List<string>();
        var param = new List<string>();

        foreach (var (pin, op) in oldPins)
        {
            if (!newPins.TryGetValue(pin, out var np))
            {
                wiring.Add($"{pin}: 管脚配置被删除");
                continue;
            }
            if (!string.Equals(op.PointName ?? "", np.PointName ?? "", StringComparison.OrdinalIgnoreCase) ||
                op.Reversed != np.Reversed)
            {
                wiring.Add($"{pin}: {Wire(op)} → {Wire(np)}");
            }
            if (ProjectFingerprint.FormatValue(op.DefaultValue) != ProjectFingerprint.FormatValue(np.DefaultValue) ||
                op.HasDefaultValue != np.HasDefaultValue)
            {
                param.Add($"{pin}: {ProjectFingerprint.FormatValue(op.DefaultValue)} → {ProjectFingerprint.FormatValue(np.DefaultValue)}");
            }
        }

        foreach (var (pin, np) in newPins)
        {
            if (!oldPins.ContainsKey(pin))
                wiring.Add($"{pin}: 新增管脚配置 {Wire(np)}");
        }

        if (wiring.Count > 0)
            report.Entries.Add(new DiffEntry(DiffKind.BlockWiringChanged, ctrl, ob.Name, string.Join("；", wiring)));
        if (param.Count > 0)
            report.Entries.Add(new DiffEntry(DiffKind.BlockParamChanged, ctrl, ob.Name, string.Join("；", param)));

        static string Wire(PinDetailModel p) =>
            string.IsNullOrEmpty(p.PointName) ? "<空>" : (p.Reversed ? "~" : "") + p.PointName;
    }

    private static Dictionary<string, T> FirstByName<T>(IReadOnlyList<T> items, Func<T, string> nameOf)
    {
        var dict = new Dictionary<string, T>(items.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            dict.TryAdd(nameOf(item), item);
        return dict;
    }
}
