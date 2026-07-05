using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class RALM
	{
		protected override void Run(ICommand cmd) 
		{
                // 2. 核心修复：计算单位为 "值/分钟" 的真实速率
            float deltaX = X - OLD_X;
            float cycleTimeSec = cmd.Dpu.Cycle; // 假设 Cycle 单位是秒(如 0.05代表50ms)
            // 速率 = (每周期变化量 / 周期秒数) * 60秒 => 值/分钟
            float actualRatePerMin = (deltaX / cycleTimeSec) * 60.0f;
            // 提取增速率(正)和减速率(正)
            float incRate = actualRatePerMin;  // 大于0时为增速率
            float decRate = -actualRatePerMin; // 大于0时为减速率
            // 3. 处理速率死区 XDB (过滤微小波动)
            // 文档要求：小于 XDB 时，不进行报警判断（视同速率为0，正常走恢复逻辑即可）
            if (Math.Abs(actualRatePerMin) < XDB)
            {
                incRate = 0.0f;
                decRate = 0.0f;
            }
            // 4. 增速率报警判断 (IAlm)
            if (EnI)
            {
                if (incRate > IRL)
                {
                    IAlm[0] = true;
                }
                else if (incRate <= (IRL - IDB))
                {
                    IAlm[0] = false;
                }
                // 注意：介于 (IRL - IDB) 和 IRL 之间时，保持当前状态不变（死区作用）
            }
            else
            {
                IAlm[0] = false; // 增速报警被禁用时强制复位
            }
            // 5. 减速率报警判断 (DAlm)
            // 核心修复：已将下降率转为正数 decRate，现在可以正确与正数限值 DRL 比较
            if (EnD)
            {
                if (decRate > DRL)
                {
                    DAlm[0] = true;
                }
                else if (decRate <= (DRL - DDB))
                {
                    DAlm[0] = false;
                }
            }
            else
            {
                DAlm[0] = false; // 减速报警被禁用时强制复位
            }
            // 6. 汇总全局报警
            Alm[0] = IAlm | DAlm;
            // 7. 更新历史值，为下一周期做准备
            OLD_X = X;
        }
	}
}
