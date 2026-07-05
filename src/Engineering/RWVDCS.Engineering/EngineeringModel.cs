namespace RWVDCS.Engineering;

/// <summary>
/// 工程模型：从 Access 工程库（Cld_ = Control Logic Document 控制逻辑图）直读出来的
/// 装载中间表示。字段命名与老系统 NHibernate 模型对齐，装载语义见 MdbEngineeringReader。
/// </summary>
public sealed class EngineeringModel
{
    public required string ProjectPath { get; init; }
    public required IReadOnlyList<ControllerModel> Controllers { get; init; }
}

/// <summary>控制器（≈ 一个 DPU）。</summary>
public sealed class ControllerModel
{
    public required int Id { get; init; }
    public required string Address { get; init; }
    public required string Name { get; init; }

    /// <summary>点表（Cfg_VarSystem 装载序）。</summary>
    public required IReadOnlyList<PointModel> Points { get; init; }

    /// <summary>块表（Cld_FCBlock 装载序 = 老系统 Command 创建序）。</summary>
    public required IReadOnlyList<BlockModel> Blocks { get; init; }
}

/// <summary>点定义（对齐老系统 PointDetails 的有效字段）。</summary>
public sealed class PointModel
{
    public required string Name { get; init; }
    /// <summary>LA / LD / LP / LP32。</summary>
    public required string DataType { get; init; }
    /// <summary>buffer 初值：LA→float，LD→bool，LP→short(0)，LP32→long(0)。</summary>
    public required object DefaultValue { get; init; }
    /// <summary>仅 LA 使用（写入 maxvalue 字段）。</summary>
    public float MaxValue { get; init; }
    /// <summary>仅 LA 使用（写入 minvalue 字段）。</summary>
    public float MinValue { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
}

/// <summary>块定义。</summary>
public sealed class BlockModel
{
    /// <summary>运行块名 AlgName，如 "1001$1$DCON5"。</summary>
    public required string Name { get; init; }
    /// <summary>功能码名 FunctionName，如 "DCON"。</summary>
    public required string FcName { get; init; }
    public string? Description { get; init; }

    /// <summary>管脚初始化细节（老系统 fcDetails[AlgName]，插入序保留：先 Input 行、再 Output 行、再 Parameter 行、最后 Description）。</summary>
    public required List<PinDetailModel> Pins { get; init; }

    public PinDetailModel? FindPin(string name)
    {
        // 与老系统 Command 构造时的大小写不敏感对齐一致
        foreach (var p in Pins)
            if (string.Equals(p.PinName, name, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }
}

/// <summary>
/// 管脚装载细节，对齐老系统 PinDetails&lt;T&gt; 的运行期有效面：
/// PointName（已按 AnalysePointName 规则处理 ~ 取反）、HasDefaultValue、DefaultValue。
/// </summary>
public sealed class PinDetailModel
{
    public required string PinName { get; init; }

    /// <summary>连接的点名（可为 null；可含逗号分隔多点；单点时 ~ 已剥离并置 Reversed）。</summary>
    public string? PointName { get; set; }

    /// <summary>是否取反（老系统 PinDetails.Reverse；Output 多点场景在 Command 构造时逐点重解析）。</summary>
    public bool Reversed { get; set; }

    public bool HasDefaultValue { get; init; }

    /// <summary>float / float[] / string（与老系统 PinDetails&lt;float&gt;/&lt;float[]&gt;/&lt;string&gt; 对应）。</summary>
    public object? DefaultValue { get; init; }
}
