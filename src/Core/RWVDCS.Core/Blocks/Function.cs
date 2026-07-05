using RWVDCS.Core.Types;

namespace RWVDCS.Core.Blocks;

/// <summary>
/// 功能块抽象基类，与老系统 DCSCommon.Function 的行为面 1:1 对齐：
/// Implement 吞异常、runable 门控、FirstRun 默认为空、Name 取自 FCName 特性。
/// 新系统中块实例是普通托管对象（状态在快照边界与 Arena 同步），不再要求 StructLayout。
/// </summary>
[VariableClass(VariableClass.Block)]
[FCName("AbstractFunction")]
[FCDisplay("功能算题的抽象模块")]
public abstract class Function
{
    [PinType(PinTypes.Constant)]
    [PinDisplay("算法块的名称")]
    public string FcName = "";

    [PinType(PinTypes.Constant)]
    [PinDisplay("算法块的编码")]
    public string FcCode = "";

    /// <summary>当前是否运行（老系统同名字段，参与状态快照）。</summary>
    [PinType(PinTypes.Internal)]
    [PinDisplay("运行状态：是否运行")]
    protected bool runable = true;

    private ICommand? command;

    public virtual ICommand? Command
    {
        get => command;
        set => command = value;
    }

    public bool Runable
    {
        get => runable;
        set => runable = value;
    }

    /// <summary>功能码名称（读 FCName 特性）。</summary>
    public string? Name
    {
        get
        {
            string? name = null;
            foreach (FCNameAttribute attribute in GetType().GetCustomAttributes(typeof(FCNameAttribute), false))
                name += attribute.ToString();
            return name;
        }
    }

    /// <summary>核心执行函数，由具体块实现。</summary>
    protected abstract void Run(ICommand cmd);

    /// <summary>第一次运行的初始化操作。</summary>
    public virtual void FirstRun(ICommand cmd)
    {
    }

    /// <summary>运行入口：runable 门控 + 吞异常（与老系统一致，保证单块异常不拖垮整个 DPU 周期）。</summary>
    public void Implement(ICommand cmd)
    {
        try
        {
            if (runable)
                Run(cmd);
        }
        catch
        {
            // 老系统在此静默吞掉块内异常，保持周期继续。
        }
    }

    public virtual void SetMemberValue(object? value, params string[] names)
    {
        if (value == null || names == null || names.Length < 1 || names[0] == null)
            return;
        switch (names[0])
        {
            case "runable":
                Runable = (bool)value;
                break;
            case "FcName":
                FcName = value.ToString() ?? "";
                break;
            case "FcCode":
                FcCode = value.ToString() ?? "";
                break;
        }
    }

    public virtual object? GetMemberValue(params string[] names)
    {
        if (names == null || names.Length < 1 || names[0] == null)
            return null;
        return names[0] switch
        {
            "runable" => Runable,
            "FcName" => FcName,
            "FcCode" => FcCode,
            _ => null,
        };
    }
}
