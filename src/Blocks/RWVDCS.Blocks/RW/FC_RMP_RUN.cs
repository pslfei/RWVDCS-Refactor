using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class RMP
	{
		protected override void Run(ICommand cmd) 
		{
            // 2. 提取输入到局部变量，提升底层对象操作性能
            float currentOut = OUT;
            float endValue = END;

            // 3. 处理 RST 复位指令 (检测上升沿)
            bool rstRisingEdge = (!OLD_RST & RST);
            OLD_RST = RST; // 立即更新历史状态
            if (rstRisingEdge)
            {
                // 文档要求：RST 由 0 变 1 时，OUT=起始值，OUTD=0
                currentOut = BASE;
                OUTD[0] = false;
            }
            // 4. 处理正常斜坡逻辑 (未复位 且 未暂停)
            else if (!PAUSE)
            {
                // 如果当前值已经等于终点值，直接保持并置位完成标志
                if (Math.Abs(currentOut - endValue) < 0.000001f) // 浮点数防精度丢失比较
                {
                    currentOut = endValue;
                    OUTD[0] = true;
                }
                else
                {
                    // 核心修复：计算当前周期的真实步长 (DY单位为 /s，需乘以周期秒数)
                    // 假设 cmd.Dpu.Cycle 单位为秒 (例如 0.05 代表 50ms)
                    float cycleSec = cmd.Dpu.Cycle;
                    float step = Math.Abs(DY) * cycleSec; // 取绝对值，防止用户误填负数
                    // 判断是上升斜坡还是下降斜坡
                    if (currentOut < endValue)
                    {
                        // 向上爬坡
                        currentOut += step;
                        if (currentOut >= endValue)
                        {
                            currentOut = endValue;
                            OUTD[0] = true;
                        }
                        else
                        {
                            OUTD[0] = false;
                        }
                    }
                    else if (currentOut > endValue)
                    {
                        // 向下下坡 (核心修复：支持 BASE > END 的情况)
                        currentOut -= step;
                        if (currentOut <= endValue)
                        {
                            currentOut = endValue;
                            OUTD[0] = true;
                        }
                        else
                        {
                            OUTD[0] = false;
                        }
                    }
                }
            }

            OUT[0] = currentOut;
        }
	}
}
