using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class ALML
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            // 动态报警限值由输入端 LLL/LL/L/H/HH/HHH 实时提供
            // DCS 报警子系统根据 PAGE/Station/Branch/Slot/Channel/TAG 定位源端，
            // 使用输入限值（含死区 DB）执行报警比较，并将报警信息上送至报警窗
            // 报警等级由参数 LLAl/LAl/HAl/HHAl/HHHAl 配置
            // LLLAl 输出和 OUT 输出由报警子系统根据源端报警状态更新
        }
    }
}
