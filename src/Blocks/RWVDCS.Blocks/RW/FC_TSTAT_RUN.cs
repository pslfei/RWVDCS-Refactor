using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class TSTAT
    {
        protected override void Run(ICommand cmd)
        {
            float _currentOut = 0.0f;
            float T = (float)cmd.Dpu.Cycle; // 控制周期，单位：秒
            // 4. 边沿检测
            bool risingEdge = (!OLD_SET & SET);   // 统计开始
            bool fallingEdge = (OLD_SET & !SET);  // 统计结束
            // --- 阶段 A：统计结束（下降沿） ---
            if (fallingEdge)
            {
                // 将最终统计结果锁存到 OutP，供下级逻辑作为“本次批次统计结果”读取
                OutP[0] = _currentOut;
            }
            // --- 阶段 B：统计初始化（上升沿） ---
            if (risingEdge)
            {
                OLD_InitV = InitV; // 锁存初始值
                _accumulatedSum = 0;
                sampleCount = 0;
               
                // 初始状态下，不同模式的起点不同
                if (MODE == 2 || MODE == 3)
                {
                    _currentOut = X; // 求最值模式：初始输出等于当前输入
                }
                else
                {
                    _currentOut = OLD_InitV; // 积分/平均模式：输出从 InitV 开始
                }
            }
            // --- 阶段 C：持续统计运算（高电平期间） ---
            if (SET)
            {
                switch (MODE)
                {
                    case 0: // Matrix：矩形积分
                        _accumulatedSum += X * T;
                        _currentOut = OLD_InitV + _accumulatedSum;
                        break;
                    case 1: // AVG：平均值
                        _accumulatedSum += X * T;
                        sampleCount++;
                        // 遵循文档特殊公式：[InitV + 积分值] / 总时间
                        _currentOut = (OLD_InitV + _accumulatedSum) / (sampleCount * T);
                        break;
                    case 2: // MAX：最大值
                        if (X > _currentOut) _currentOut = X;
                        break;
                    case 3: // MIN：最小值
                        if (X < _currentOut) _currentOut = X;
                        break;
                    case 4: // Trapezoidal：梯形积分
                        // 文档公式：[X(n) + X(n-1)] * T / 2
                        _accumulatedSum += (X + OLD_X) * T / 2.0f;
                        _currentOut = OLD_InitV + _accumulatedSum;
                        break;
                }
            }
            // 5. 更新历史状态（供下一周期使用）
            OLD_SET = SET;
            OLD_X = X;
            // 6. 统一赋值输出管脚一次
            OUT[0] = _currentOut;
        }
    }
}
