namespace RWVDCS.Engineering;

/// <summary>
/// 工程模型：从 Access 工程库（Cld_ = Control Logic Document 控制逻辑图）直读出来的
/// 装载中间表示。字段命名与老系统 NHibernate 模型对齐，装载语义见 MdbEngineeringReader。
/// </summary>
public sealed class EngineeringModel
{
    public required string ProjectPath { get; init; }
    public required IReadOnlyList<ControllerModel> Controllers { get; init; }

    /// <summary>
    /// 深克隆。装配（RuntimeBuilder）会就地改写管脚的 PointName/Reversed
    /// （pin-point 源块输出追加、逗号拆分粘滞取反），因此需要保留纯净副本
    /// 用于工程指纹、在线下装 diff。克隆体给装配器改写，纯净体存档。
    /// </summary>
    public EngineeringModel Clone() => new()
    {
        ProjectPath = ProjectPath,
        Controllers = Controllers.Select(c => new ControllerModel
        {
            Id = c.Id,
            Address = c.Address,
            Name = c.Name,
            Points = c.Points, // PointModel 全部 init-only，不可变，可共享
            Blocks = c.Blocks.Select(b => new BlockModel
            {
                Name = b.Name,
                FcName = b.FcName,
                Description = b.Description,
                Pins = b.Pins.Select(p => new PinDetailModel
                {
                    PinName = p.PinName,
                    PointName = p.PointName,
                    Reversed = p.Reversed,
                    HasDefaultValue = p.HasDefaultValue,
                    DefaultValue = p.DefaultValue,
                }).ToList(),
            }).ToList(),
        }).ToList(),
    };
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
    /// <summary>Cfg_VarSystem.ID。</summary>
    public int ID { get; init; }
    public required string Name { get; init; }
    /// <summary>LA / LD / LP / LP32。</summary>
    public required string DataType { get; init; }
    /// <summary>buffer 初值：LA→float，LD→bool，LP→short(0)，LP32→long(0)。</summary>
    public required object DefaultValue { get; init; }
    /// <summary>仅 LA 使用（写入 maxvalue 字段）。</summary>
    public float MaxValue { get; init; }
    /// <summary>仅 LA 使用（写入 minvalue 字段）。</summary>
    public float MinValue { get; init; }
    /// <summary>工程库配置的低报警一级优先级。</summary>
    public int LowAlarm1Priority { get; init; }
    /// <summary>工程库配置的低报警二级优先级。</summary>
    public int LowAlarm2Priority { get; init; }
    /// <summary>工程库配置的低报警三级优先级。</summary>
    public int LowAlarm3Priority { get; init; }
    /// <summary>工程库配置的高报警一级优先级。</summary>
    public int HighAlarm1Priority { get; init; }
    /// <summary>工程库配置的高报警二级优先级。</summary>
    public int HighAlarm2Priority { get; init; }
    /// <summary>工程库配置的高报警三级优先级。</summary>
    public int HighAlarm3Priority { get; init; }
    /// <summary>工程库配置的低报警一级限值，LA 点构建时写入实时点结构。</summary>
    public double LowAlarmLimit1Value { get; init; }
    /// <summary>工程库配置的低报警二级限值，LA 点构建时写入实时点结构。</summary>
    public double LowAlarmLimit2Value { get; init; }
    /// <summary>工程库配置的低报警三级限值，LA 点构建时写入实时点结构。</summary>
    public double LowAlarmLimit3Value { get; init; }
    /// <summary>工程库配置的高报警一级限值，LA 点构建时写入实时点结构。</summary>
    public double HighAlarmLimit1Value { get; init; }
    /// <summary>工程库配置的高报警二级限值，LA 点构建时写入实时点结构。</summary>
    public double HighAlarmLimit2Value { get; init; }
    /// <summary>工程库配置的高报警三级限值，LA 点构建时写入实时点结构。</summary>
    public double HighAlarmLimit3Value { get; init; }
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
