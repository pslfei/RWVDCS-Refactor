using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class MSFT
	{
		protected override void Run(ICommand cmd) 
		{
            //if (SEL4)
            //{
            //             float rate = T4 / 60 * cmd.Dpu.Cycle;
            //             if (X4 > OUT)
            //             {
            //                 OUT[0] = Math.Min(OUT + rate, X4);
            //             }
            //             else if (X4 < OUT)
            //             {
            //                 OUT[0] = Math.Max(OUT - rate, X4);
            //             }

            //             OUTD[0] = true;
            //}
            //         else
            //         {
            //             if (SEL3)
            //             {
            //                 float rate = T3 / 60 * cmd.Dpu.Cycle;
            //                 if (X3 > OUT)
            //                 {
            //                     OUT[0] = Math.Min(OUT + rate, X3);
            //                 }
            //                 else if (X4 < OUT)
            //                 {
            //                     OUT[0] = Math.Max(OUT - rate, X3);
            //                 }
            //                 OUTD[0] = true;
            //             }
            //             else
            //             {
            //                 if (SEL2)
            //                 {
            //                     float rate = T2 / 60 * cmd.Dpu.Cycle;
            //                     if (X2 > OUT)
            //                     {
            //                         OUT[0] = Math.Min(OUT + rate, X2);
            //                     }
            //                     else if (X2 < OUT)
            //                     {
            //                         OUT[0] = Math.Max(OUT - rate, X2);
            //                     }
            //                     OUTD[0] = true;
            //                 }
            //                 else
            //                 {
            //                     if (SEL1)
            //                     {
            //                         float rate = T1 / 60 * cmd.Dpu.Cycle;
            //                         if (X1 > OUT)
            //                         {
            //                             OUT[0] = Math.Min(OUT + rate, X1);
            //                         }
            //                         else if (X1 < OUT)
            //                         {
            //                             OUT[0] = Math.Max(OUT - rate, X1);
            //                         }
            //                     }
            //                     else
            //                     {
            //                         OUT[0] = X0;
            //                         OUTD[0] = false;
            //                     }
            //                 }

            //             }
            //         }


            // 用于存储当前周期选中的目标值和速率限制
            float targetValue = 0.0f;
            float targetRateLimit = 0.0f;
            bool targetOutD = false;
            // 2. 优先级选择逻辑 (SEL4 > SEL3 > SEL2 > SEL1)
            // 根据文档真值表，扁平化的 if-else if 结构最清晰
            if (SEL4)
            {
                targetValue = X4;
                targetRateLimit = T4;
                targetOutD = true;
            }
            else if (SEL3)
            {
                targetValue = X3;
                targetRateLimit = T3;
                targetOutD = true;
            }
            else if (SEL2)
            {
                targetValue = X2;
                targetRateLimit = T2;
                targetOutD = true;
            }
            else if (SEL1)
            {
                targetValue = X1;
                targetRateLimit = T1;
                targetOutD = true;
            }
            else // 全部未选择，默认切回 X0
            {
                targetValue = X0;
                targetRateLimit = T0;
                targetOutD = false;
            }
            // 更新 OUTD 状态
            OUTD[0] = targetOutD;
            // 3. 执行无扰动(斜坡)切换逻辑
            // 防御性编程：如果有负数输入的速率，强制视为0处理
            if (targetRateLimit <= 0.000001f)
            {
                // 文档规定：Tn = 0 时，无速率限制，直接瞬间赋值
                OUT[0] = targetValue;
            }
            else
            {
                // 计算当前周期的步长：(单位/分钟) / 60 => (单位/秒) * 周期时间(秒)
                // 注意：这里假设 cmd.Dpu.Cycle 的单位是秒 (如 0.05 代表 50ms)。
                // 如果实际框架中 Cycle 是毫秒，请改为： (targetRateLimit / 60000.0f) * cmd.Dpu.Cycle;
                float cycleStep = (targetRateLimit / 60.0f) * (float)cmd.Dpu.Cycle;
                float currentOut = OUT;
                if (currentOut < targetValue)
                {
                    // 向上爬坡：不能超过目标值
                    OUT[0] = Math.Min(currentOut + cycleStep, targetValue);
                }
                else if (currentOut > targetValue)
                {
                    // 向下溜坡：不能低于目标值
                    OUT[0] = Math.Max(currentOut - cycleStep, targetValue);
                }
                // 如果相等，则保持不变 (无需处理)
            }
        }
	}
}
