using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Runtime;

/// <summary>
/// 老系统 .wrk 工况迁移导入器。
///
/// 迁移链路：LegacyRunner --load-wrk 老.wrk --export-state 桥接.tsv →
/// 本导入器按"名字→值"应用到新运行时 → Host --save 另存为新格式工况。
/// 桥接文件格式见 LegacyRunner.ExportState（V/D/P/B 行）。
///
/// 名字寻址而非字节寻址：老 .wrk 是 RTD 内存镜像 + BinaryFormatter 指令流，
/// 与新系统 Arena 布局无字节兼容性；点/管脚/状态字段名两边一致（块源码同源），
/// 因此以 DPU名+点名 / DPU名+块名+字段名 为键完成全量状态搬运。
/// </summary>
public static class LegacyStateImporter
{
    public sealed class ImportReport
    {
        public int PointsApplied;
        public int PointsSkipped;      // 本 DPU 无此点（跨 DPU 副本行，属主行会覆盖）
        public int BlockFieldsApplied;
        public int BlockFieldsSkipped; // 新系统 schema 无此字段 / 块缺失
        public int BlocksMissing;
        public List<string> Warnings { get; } = [];
    }

    // ---- 点结构子字段布局缓存：kind → (子字段名 → (偏移, 字段类型))
    private static readonly Dictionary<PointKind, Dictionary<string, (uint Offset, Type Type)>> PointFieldLayouts = BuildPointFieldLayouts();

    private static Dictionary<PointKind, Dictionary<string, (uint, Type)>> BuildPointFieldLayouts()
    {
        var map = new Dictionary<PointKind, Dictionary<string, (uint, Type)>>();
        foreach (var (kind, type) in new[]
                 {
                     (PointKind.LA, typeof(LA)), (PointKind.LD, typeof(LD)),
                     (PointKind.LP, typeof(LP)), (PointKind.LP32, typeof(LP32)),
                 })
        {
            var fields = new Dictionary<string, (uint, Type)>(StringComparer.OrdinalIgnoreCase);
            foreach (var fi in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                fields[fi.Name] = ((uint)Marshal.OffsetOf(type, fi.Name), fi.FieldType);
            map[kind] = fields;
        }
        return map;
    }

    public static ImportReport Import(DcsRuntime runtime, string bridgeFile)
    {
        var report = new ImportReport();

        DpuRuntime? dpu = null;
        string dpuName = "";
        Dictionary<string, BlockCommand>? commandIndex = null;

        foreach (var line in File.ReadLines(bridgeFile))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split('\t');

            switch (parts[0])
            {
                case "V":
                    if (parts[1] != "1")
                        throw new InvalidDataException($"桥接文件版本 {parts[1]} 不受支持");
                    break;

                case "D": // D  dpu名  cycle秒  cycleCount
                {
                    dpuName = parts[1];
                    dpu = runtime.FindDpu(dpuName);
                    commandIndex = null;
                    if (dpu == null)
                    {
                        report.Warnings.Add($"新系统中不存在 DPU {dpuName}，其状态行将被跳过");
                        break;
                    }
                    dpu.Cycle = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    dpu.CycleCount = uint.Parse(parts[3], CultureInfo.InvariantCulture);
                    break;
                }

                case "P": // P  dpu名  点名  类别  k=v;...
                {
                    if (dpu == null || parts[1] != dpuName)
                        dpu = runtime.FindDpu(dpuName = parts[1]);
                    if (dpu == null || !dpu.LocalSlots.TryGetValue(parts[2], out var slot) || !slot.IsRealPoint)
                    {
                        report.PointsSkipped++;
                        break;
                    }
                    ApplyPointSubFields(slot, parts[4]);
                    report.PointsApplied++;
                    break;
                }

                case "B": // B  dpu名  块名  FC名  字段名  规格
                {
                    if (dpu == null || parts[1] != dpuName)
                    {
                        dpu = runtime.FindDpu(dpuName = parts[1]);
                        commandIndex = null;
                    }
                    if (dpu == null)
                    {
                        report.BlockFieldsSkipped++;
                        break;
                    }

                    commandIndex ??= BuildCommandIndex(dpu);
                    if (!commandIndex.TryGetValue(parts[2], out var cmd))
                    {
                        report.BlocksMissing++;
                        report.BlockFieldsSkipped++;
                        break;
                    }

                    if (ApplyBlockField(cmd, parts[4], parts[5], report))
                        report.BlockFieldsApplied++;
                    else
                        report.BlockFieldsSkipped++;
                    break;
                }
            }
        }

        return report;
    }

    private static Dictionary<string, BlockCommand> BuildCommandIndex(DpuRuntime dpu)
    {
        var index = new Dictionary<string, BlockCommand>(dpu.Commands.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in dpu.Commands)
            index[cmd.Name] = cmd;
        return index;
    }

    // -----------------------------------------------------------------
    // 点：k=v;... 全部子字段直写 Arena（不触发 LA.Value 的报警副作用，忠实还原内存态）
    // -----------------------------------------------------------------
    private static void ApplyPointSubFields(PointSlotRef slot, string spec)
    {
        var layout = PointFieldLayouts[slot.Kind];
        foreach (var pair in spec.Split(';'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            string name = pair[..eq];
            string value = pair[(eq + 1)..];
            if (!layout.TryGetValue(name, out var f))
                continue;

            Type t = f.Type.IsEnum ? Enum.GetUnderlyingType(f.Type) : f.Type;
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Single:
                    slot.Arena.WriteField(slot.Sid, f.Offset, float.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case TypeCode.Byte:
                    slot.Arena.WriteField(slot.Sid, f.Offset, byte.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case TypeCode.UInt16:
                    slot.Arena.WriteField(slot.Sid, f.Offset, ushort.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case TypeCode.UInt32:
                    slot.Arena.WriteField(slot.Sid, f.Offset, uint.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case TypeCode.Int32:
                    slot.Arena.WriteField(slot.Sid, f.Offset, int.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case TypeCode.Boolean: // 老系统 bool 字段（桥接侧输出 0/1）→ 新系统 byte
                    slot.Arena.WriteField(slot.Sid, f.Offset, (byte)(value == "1" ? 1 : 0));
                    break;
            }
        }
    }

    // -----------------------------------------------------------------
    // 块字段：PIN / VAL / ARR / STR / NUL 规格 → live fc 对象
    // -----------------------------------------------------------------
    private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> StructFieldCache = [];

    private static bool ApplyBlockField(BlockCommand cmd, string fieldName, string spec, ImportReport report)
    {
        var schema = BlockStateSchema.For(cmd.Fc.GetType());
        if (!schema.TryGetField(fieldName, out var field))
            return false;

        int colon = spec.IndexOf(':');
        if (colon < 0)
            return false;
        string tag = spec[..colon];
        string payload = spec[(colon + 1)..];
        var ft = field.Field.FieldType;

        try
        {
            switch (tag)
            {
                case "PIN":
                {
                    object pin = field.Field.GetValue(cmd.Fc)!;
                    var fields = GetStructFields(ft);
                    foreach (var pair in payload.Split(';'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq <= 0)
                            continue;
                        if (!fields.TryGetValue(pair[..eq], out var sub))
                            continue;
                        sub.SetValue(pin, ConvertScalar(pair[(eq + 1)..], sub.FieldType));
                    }
                    field.Field.SetValue(cmd.Fc, pin);
                    return true;
                }

                case "VAL":
                    field.Field.SetValue(cmd.Fc, ConvertScalar(payload, ft));
                    return true;

                case "ARR":
                {
                    var elemType = ft.GetElementType()!;
                    string[] items = payload.Length == 0 ? [] : payload.Split(',');
                    // 目标数组容量以新系统声明为准（schema 容量），逐元素拷贝，超出截断、不足补默认
                    var current = field.Field.GetValue(cmd.Fc) as Array;
                    var target = current ?? Array.CreateInstance(elemType, field.Capacity);
                    int n = Math.Min(items.Length, target.Length);
                    for (int i = 0; i < n; i++)
                        target.SetValue(ConvertScalar(items[i], elemType), i);
                    field.Field.SetValue(cmd.Fc, target);
                    return true;
                }

                case "STR":
                    field.Field.SetValue(cmd.Fc, Uri.UnescapeDataString(payload));
                    return true;

                case "NUL":
                    field.Field.SetValue(cmd.Fc, null);
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            if (report.Warnings.Count < 50)
                report.Warnings.Add($"块 {cmd.Name} 字段 {fieldName} 应用失败：{ex.Message}");
            return false;
        }
    }

    private static Dictionary<string, FieldInfo> GetStructFields(Type t)
    {
        lock (StructFieldCache)
        {
            if (!StructFieldCache.TryGetValue(t, out var map))
            {
                map = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    map[fi.Name] = fi;
                StructFieldCache[t] = map;
            }
            return map;
        }
    }

    /// <summary>桥接标量串 → 目标 CLR 类型（float/double 用 R 往返格式，bool 用 0/1，枚举按整数）。</summary>
    private static object ConvertScalar(string s, Type target)
    {
        if (target.IsEnum)
            return Enum.ToObject(target, long.Parse(s, CultureInfo.InvariantCulture));
        return Type.GetTypeCode(target) switch
        {
            TypeCode.Single => float.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.Double => double.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.Boolean => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            TypeCode.Byte => byte.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.SByte => sbyte.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.Char => (char)int.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.Int16 => short.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.UInt16 => ushort.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.Int32 => int.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.UInt32 => uint.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.Int64 => long.Parse(s, CultureInfo.InvariantCulture),
            TypeCode.UInt64 => ulong.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"不支持的标量类型 {target.Name}"),
        };
    }
}
